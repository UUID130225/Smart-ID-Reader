using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using SmartIDReader.Helpers;
using SmartIDReader.Models;
using SmartIDReader.Services;
using Serilog;

namespace SmartIDReader
{
    public partial class MainWindow : Window
    {
        private readonly SerialPortService _serial;
        private readonly TrayService       _tray;
        private readonly bool              _startHidden;

        private bool        _suppressCbChange = true; // Khóa mặc định trong lúc khởi tạo
        private AppSettings _settings;
        private readonly DispatcherTimer _refreshTimer;
        private CancelEventHandler _closingGuard;

        // Windows Messages để theo dõi cắm rút thiết bị USB/COM
        private const int WM_DEVICECHANGE = 0x0219;

        public MainWindow(SerialPortService serial, TrayService tray, bool isAdmin, bool startHidden = false)
        {
            _suppressCbChange = true;
            InitializeComponent();
            _serial = serial;
            _tray   = tray;
            _startHidden = startHidden;

            if (isAdmin) AdminBadge.Visibility = Visibility.Visible;

            _serial.StatusChanged += (ok, msg) => Dispatcher.Invoke(() => UpdateStatus(ok, msg));
            _serial.DataReceived  += (raw, fmt)  => Dispatcher.Invoke(() => HandleScan(fmt));

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _refreshTimer.Tick += (_, __) =>
            {
                RefreshPorts(keepSelection: true);
                CheckConnectionHealth();
            };

            // Đóng cửa sổ (nút X chuẩn của Win) -> Ẩn xuống khay hệ thống
            _closingGuard = (_, ev) => { ev.Cancel = true; HideToTray(); };
            Closing += _closingGuard;

            Loaded += OnLoaded;
        }

        // Đăng ký hook WndProc để bắt sự kiện cắm/rút USB (WM_DEVICECHANGE)
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE)
            {
                Log.Information("Phát hiện thay đổi thiết bị phần cứng (cắm/rút USB). Quét lại cổng COM...");
                // Chạy trên luồng UI để cập nhật ComboBox và kiểm tra kết nối ngay lập tức
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    RefreshPorts(keepSelection: true);
                    CheckConnectionHealth();
                }));
            }
            return IntPtr.Zero;
        }

        // ══════════════ STARTUP ══════════════

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Nạp toàn bộ log lưu trong buffer trước khi load UI vào terminal
            foreach (var log in ObservableLogSink.LogBuffer)
            {
                TxtTerminal.AppendText(log + Environment.NewLine);
            }
            TxtTerminal.ScrollToEnd();

            // Đăng ký sự kiện nhận log mới
            ObservableLogSink.LogAdded += OnLogAdded;

            _settings = SettingsManager.Load();
            
            // Đồng bộ và dọn dẹp cấu hình auto-start cũ sang Task Scheduler
            try
            {
                AutoStartHelper.SetAutoStart(_settings.StartWithWindows);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Lỗi đồng bộ cấu hình auto-start ban đầu");
            }
            
            _suppressCbChange = true;
            try
            {
                ApplySettingsToUI();
                RefreshPorts(keepSelection: false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Lỗi nạp cấu hình ban đầu");
            }

            _tray.ShowWindowRequested += () => Dispatcher.Invoke(ShowMe);
            _tray.ExitRequested       += () => Dispatcher.Invoke(ExitApp);
            _tray.Initialize();

            _serial.Connect(_settings);

            _refreshTimer.Start();

            // Chỉ mở khóa bắt sự kiện thay đổi sau khi toàn bộ UI đã load và kết nối xong
            _suppressCbChange = false;

            if (_startHidden)
            {
                Hide();
                WindowState = WindowState.Normal;
                ShowInTaskbar = true;
            }
        }

        // ══════════════ PORT LIST ══════════════

        private void RefreshPorts(bool keepSelection)
        {
            if (CbComPort == null || CbComPort.IsDropDownOpen) return;

            string current = keepSelection
                ? (CbComPort.SelectedItem as PortComboItem)?.Path
                : _settings.Port;

            bool oldSuppress = _suppressCbChange;
            _suppressCbChange = true;
            try
            {
                CbComPort.Items.Clear();
                CbComPort.Items.Add(new PortComboItem("", "Tự động chọn (Auto Detect)"));

                int sel = 0;
                var ports = SerialPortService.ListPorts();
                for (int i = 0; i < ports.Count; i++)
                {
                    CbComPort.Items.Add(new PortComboItem(ports[i].Path, ports[i].DisplayName));
                    if (ports[i].Path.Equals(current, StringComparison.OrdinalIgnoreCase))
                        sel = i + 1;
                }
                CbComPort.SelectedIndex = sel;
            }
            finally { _suppressCbChange = oldSuppress; }
        }

        // ══════════════ CONNECTION HEALTH (AUTO SWAP PORT) ══════════════

        private void CheckConnectionHealth()
        {
            string activePort = _settings.Port;
            if (string.IsNullOrEmpty(activePort)) return;

            // Lấy danh sách cổng thực tế đang cắm trên máy
            var availablePorts = SerialPortService.ListPorts();
            bool portStillExists = availablePorts.Exists(p => p.Path.Equals(activePort, StringComparison.OrdinalIgnoreCase));

            // Nếu thiết bị đang báo kết nối nhưng cổng đó đã biến mất khỏi phần cứng (bị rút USB)
            if (_serial.IsConnected && !portStillExists)
            {
                Log.Warning("Cổng kết nối {Port} đã bị rút khỏi máy. Ngắt kết nối để chuẩn bị tìm lại.", activePort);
                _serial.Disconnect(); // Sẽ kích hoạt StartReconnect()
            }
            // Nếu đã rút và cắm lại cổng mới (hoặc chưa kết nối)
            else if (!_serial.IsConnected)
            {
                _serial.Connect(_settings);
            }
        }

        // ══════════════ STATUS ══════════════

        private void UpdateStatus(bool connected, string message)
        {
            if (StatusMsg == null || StatusDot == null) return;
            StatusMsg.Text = message;
            StatusDot.Fill = connected
                ? (SolidColorBrush)FindResource("SuccessBrush")
                : (SolidColorBrush)FindResource("DangerBrush");
            _tray?.UpdateStatus(connected, message);
        }

        // ══════════════ SCAN ══════════════

        private void HandleScan(string formatted)
        {
            try { System.Windows.Clipboard.SetText(formatted); }
            catch (Exception ex) { Log.Error(ex, "Clipboard error"); }
            KeyboardSimulator.PasteAndEnterAsync();
        }

        // ══════════════ SETTINGS ══════════════

        private void ApplySettingsToUI()
        {
            SetCb(CbBaudRate, _settings.BaudRate.ToString());
            SetCb(CbDataBits, _settings.DataBits.ToString());
            SetCb(CbParity,   _settings.Parity?.ToLower() ?? "none");
            SetCb(CbStopBits, _settings.StopBits.ToString());
            SetCb(CbEncoding, _settings.Encoding ?? "utf8");
            if (ChkAutoStart != null) ChkAutoStart.IsChecked = _settings.StartWithWindows;
        }

        private AppSettings BuildSettings()
        {
            string port = CbComPort != null ? ((CbComPort.SelectedItem as PortComboItem)?.Path ?? "") : "";
            string vid = "", pid = "", dev = "";

            if (!string.IsNullOrEmpty(port))
            {
                var found = SerialPortService.ListPorts()
                    .Find(p => p.Path.Equals(port, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    vid = found.VendorId ?? "";
                    pid = found.ProductId ?? "";
                    dev = string.IsNullOrEmpty(found.FriendlyName) ? "" :
                          System.Text.RegularExpressions.Regex.Replace(found.FriendlyName, @"\s*\([^)]*\)$", "").Trim();
                }
            }

            string enc = Cb(CbEncoding);
            return new AppSettings
            {
                Port        = port,
                VendorId    = vid,
                ProductId   = pid,
                DeviceName  = dev,
                BaudRate    = int.TryParse(Cb(CbBaudRate), out int br) ? br : 9600,
                DataBits    = int.TryParse(Cb(CbDataBits), out int db) ? db : 8,
                Parity      = Cb(CbParity),
                StopBits    = float.TryParse(Cb(CbStopBits),
                                  System.Globalization.NumberStyles.Any,
                                  System.Globalization.CultureInfo.InvariantCulture, out float sb) ? sb : 1f,
                Encoding              = enc,
                StartWithWindows      = ChkAutoStart?.IsChecked == true,
                AutoReconnect         = _settings.AutoReconnect,
                ReconnectIntervalSeconds = _settings.ReconnectIntervalSeconds
            };
        }

        private void SaveAndConnect()
        {
            _settings = BuildSettings();
            SettingsManager.Save(_settings);
            AutoStartHelper.SetAutoStart(_settings.StartWithWindows);
            _serial.Connect(_settings);
        }

        // ══════════════ WINDOW ══════════════

        public void ShowMe()   
        { 
            Show(); 
            WindowState = WindowState.Normal; 
            Activate(); 
            Topmost = true; 
            Topmost = false; 
        }
        
        private void HideToTray() => Hide();

        private void ExitApp()
        {
            _refreshTimer.Stop();
            _serial.Dispose();
            _tray.Dispose();
            ObservableLogSink.LogAdded -= OnLogAdded;
            if (_closingGuard != null)
            {
                Closing -= _closingGuard;
                _closingGuard = null;
            }
            Application.Current.Shutdown();
        }

        // ══════════════ EVENT HANDLERS (AUTO-APPLY) ══════════════

        private void CbComPort_SelectionChanged(object s, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressCbChange) return;
            SaveAndConnect();
        }

        private void Advanced_SelectionChanged(object s, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressCbChange) return;
            SaveAndConnect();
        }

        private void AutoStart_Changed(object s, RoutedEventArgs e)
        {
            if (_suppressCbChange) return;
            SaveAndConnect();
        }

        // ══════════════ HELPERS ══════════════

        private static void SetCb(System.Windows.Controls.ComboBox cb, string val)
        {
            if (cb == null) return;
            foreach (System.Windows.Controls.ComboBoxItem it in cb.Items)
                if (it.Content?.ToString()?.Equals(val, StringComparison.OrdinalIgnoreCase) == true)
                { cb.SelectedItem = it; return; }
        }

        private static string Cb(System.Windows.Controls.ComboBox cb) =>
            cb == null ? "" : ((cb.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "");

        private void OnLogAdded(string logLine)
        {
            if (Dispatcher.CheckAccess())
            {
                AppendLogToTerminal(logLine);
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => AppendLogToTerminal(logLine)));
            }
        }

        private void AppendLogToTerminal(string logLine)
        {
            if (TxtTerminal == null) return;
            // Giới hạn độ dài text tránh tràn bộ nhớ
            if (TxtTerminal.Text.Length > 100000)
            {
                TxtTerminal.Text = TxtTerminal.Text.Substring(TxtTerminal.Text.Length - 50000);
            }
            TxtTerminal.AppendText(logLine + Environment.NewLine);
            TxtTerminal.ScrollToEnd();
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            if (TxtTerminal != null)
            {
                TxtTerminal.Clear();
            }
        }

        private class PortComboItem
        {
            public string Path    { get; }
            public string Display { get; }
            public PortComboItem(string path, string display) { Path = path; Display = display; }
            public override string ToString() => Display;
        }
    }
}
