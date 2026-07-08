using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Runtime.InteropServices;
using SmartIDReader.Helpers;
using SmartIDReader.Services;
using Serilog;

namespace SmartIDReader
{
    public partial class App : Application
    {
        private static Mutex _mutex;
        private const string MutexName = "SmartIDReader_SingleInstance";
        private const string PipeName = "SmartIDReader_Pipe";
        private const string RestoreMessageName = "SmartIDReader_RestoreWindow";
        private uint RestoreMessageId;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern uint RegisterWindowMessage(string lpString);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ── 1. Serilog: Cấu hình ghi log ra Console và ObservableLogSink ──
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Sink(new ObservableLogSink(new Serilog.Formatting.Display.MessageTemplateTextFormatter(
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}", null)))
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            // Đăng ký tin nhắn phục hồi cửa sổ
            RestoreMessageId = (uint)RegisterWindowMessage(RestoreMessageName);

            // ── 2. Global exception handlers ──
            AppDomain.CurrentDomain.UnhandledException += (_, a) =>
            {
                try { Log.Fatal(a.ExceptionObject as Exception, "AppDomain CRASH"); Log.CloseAndFlush(); } catch { }
            };

            Current.DispatcherUnhandledException += (_, a) =>
            {
                try { Log.Fatal(a.Exception, "Dispatcher CRASH"); Log.CloseAndFlush(); } catch { }
                a.Handled = true;
                Current.Shutdown(1);
            };

            Log.Information("=== SmartID Reader start ===");

            // ── 3. Single instance (Sử dụng Named Pipe để gửi tín hiệu khôi phục cửa sổ) ──
            if (!HasArg(e, "--no-mutex"))
            {
                try
                {
                    _mutex = new Mutex(initiallyOwned: true, name: "Global\\" + MutexName, out bool created);
                    if (!created)
                    {
                        Log.Information("App is already running. Signaling first instance and exiting.");
                        SignalFirstInstance();
                        Shutdown();
                        return;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    Log.Information("App is already running (Mutex access denied). Signaling first instance and exiting.");
                    SignalFirstInstance();
                    Shutdown();
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Lỗi kiểm tra Mutex");
                }
            }

            // Khởi động Named Pipe Server ở instance đầu tiên
            StartPipeServer();

            // ── 4. Admin check ──
            bool isAdmin = AdminHelper.IsRunningAsAdmin();
            Log.Information("Admin: {IsAdmin}", isAdmin);

            if (!isAdmin && !HasArg(e, "--no-admin"))
            {
                if (AdminHelper.TryRelaunchAsAdmin(e.Args))
                {
                    _mutex?.ReleaseMutex();
                    Log.Information("Relaunching as admin, bye.");
                    Log.CloseAndFlush();
                    Shutdown();
                    return;
                }
                Log.Warning("UAC declined, running without admin.");
                MessageBox.Show(
                    "Ứng dụng cần quyền Administrator để hoạt động chính xác (ghi cấu hình, log và giả lập bàn phím).\n\nVui lòng khởi động lại phần mềm bằng cách chuột phải chọn 'Run as administrator'.",
                    "Cảnh báo quyền hạn",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            // ── 5. Khởi tạo dịch vụ và cửa sổ chính ──
            try
            {
                var serial = new SerialPortService();
                var tray = new TrayService(isAdmin);
                bool startHidden = HasArg(e, "--hidden");
                var window = new MainWindow(serial, tray, isAdmin, startHidden);
                MainWindow = window;

                if (startHidden)
                {
                    Log.Information("Khởi động ẩn xuống khay hệ thống.");
                    window.WindowState = WindowState.Minimized;
                    window.ShowInTaskbar = false;
                }
                
                window.Show();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Startup FAILED");
                Log.CloseAndFlush();
                MessageBox.Show($"Lỗi khởi động:\n{ex.GetType().Name}: {ex.Message}",
                    "SmartID Reader", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private static void SignalFirstInstance()
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(800); // Chờ kết nối tối đa 800ms
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Không thể gửi tín hiệu khôi phục cửa sổ qua Named Pipe");
            }
        }

        private void StartPipeServer()
        {
            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        // Cấu hình PipeSecurity cho phép Everyone (WorldSid) kết nối kể cả khi chạy khác quyền (Admin vs User thường)
                        var ps = new PipeSecurity();
                        ps.AddAccessRule(new PipeAccessRule(
                            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                            PipeAccessRights.ReadWrite,
                            AccessControlType.Allow));

                        using (var server = new NamedPipeServerStream(
                            PipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous,
                            0,
                            0,
                            ps))
                        {
                            server.WaitForConnection();
                            Log.Information("Nhận kết nối từ instance thứ hai, phục hồi cửa sổ.");
                            
                            // Gọi khôi phục cửa sổ trên UI Thread
                            Current.Dispatcher.Invoke(() =>
                            {
                                if (MainWindow is MainWindow win)
                                {
                                    win.ShowMe();
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Lỗi Named Pipe Server");
                        Thread.Sleep(1000);
                    }
                }
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("=== SmartID Reader exit (code={Code}) ===", e.ApplicationExitCode);
            Log.CloseAndFlush();
            try { _mutex?.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }

        private static bool HasArg(StartupEventArgs e, string arg) =>
            Array.IndexOf(e.Args, arg) >= 0;
    }
}
