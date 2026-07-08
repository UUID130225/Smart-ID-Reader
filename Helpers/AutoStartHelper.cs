using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Serilog;

namespace SmartIDReader.Helpers
{
    public static class AutoStartHelper
    {
        private const string TaskName = "SmartIDReader";

        // XML template dùng ONLOGON trigger, chạy với HIGHEST privilege
        // Không cần escape path, không phụ thuộc vào schtasks argument parsing
        private static string BuildTaskXml(string exePath)
        {
            // Author lấy từ current user để task chạy đúng context
            string author = $@"{Environment.MachineName}\{Environment.UserName}";
            string exeDir = Path.GetDirectoryName(exePath) ?? "";

            return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Author>{EscapeXml(author)}</Author>
    <Description>SmartID Reader - khởi động cùng Windows</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{EscapeXml(exePath)}</Command>
      <Arguments>--hidden</Arguments>
      <WorkingDirectory>{EscapeXml(exeDir)}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
        }

        private static string EscapeXml(string value) =>
            value.Replace("&", "&amp;")
                 .Replace("<", "&lt;")
                 .Replace(">", "&gt;")
                 .Replace("\"", "&quot;")
                 .Replace("'", "&apos;");

        private static bool RunSchTasks(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var process = Process.Start(psi))
                {
                    if (process == null) return false;
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                        Log.Warning("schtasks exit={Code}: {Err}", process.ExitCode, stderr.Trim());
                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Lỗi thực thi schtasks: {Args}", arguments);
                return false;
            }
        }

        public static void SetAutoStart(bool enabled)
        {
            // Dọn registry cũ (migration từ version cũ)
            CleanLegacyRegistry();

            if (enabled)
            {
                string exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exe))
                {
                    Log.Warning("Không lấy được đường dẫn exe, bỏ qua SetAutoStart.");
                    return;
                }

                // Ghi XML ra file tạm rồi import — tránh hoàn toàn vấn đề escape command line
                string tmpXml = Path.Combine(Path.GetTempPath(), $"{TaskName}_task.xml");
                try
                {
                    File.WriteAllText(tmpXml, BuildTaskXml(exe), System.Text.Encoding.Unicode);
                    bool ok = RunSchTasks($"/Create /TN \"{TaskName}\" /XML \"{tmpXml}\" /F");
                    if (ok)
                        Log.Information("Đã tạo scheduled task '{Task}' thành công.", TaskName);
                    else
                        Log.Warning("Tạo scheduled task '{Task}' thất bại.", TaskName);
                }
                finally
                {
                    try { File.Delete(tmpXml); } catch { }
                }
            }
            else
            {
                bool ok = RunSchTasks($"/Delete /TN \"{TaskName}\" /F");
                Log.Information("Đã xóa scheduled task '{Task}': {Ok}", TaskName, ok);
            }
        }

        public static bool IsAutoStartEnabled()
        {
            // Task Scheduler
            if (RunSchTasks($"/Query /TN \"{TaskName}\""))
                return true;

            // Fallback kiểm tra registry cũ
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: false))
                {
                    if (key?.GetValue(TaskName) != null) return true;
                }
            }
            catch { }

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: false))
                {
                    if (key?.GetValue(TaskName) != null) return true;
                }
            }
            catch { }

            return false;
        }

        private static void CleanLegacyRegistry()
        {
            const string regPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(regPath, writable: true))
                {
                    key?.DeleteValue(TaskName, throwOnMissingValue: false);
                }
            }
            catch (Exception ex) { Log.Debug(ex, "Dọn registry HKCU"); }

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(regPath, writable: true))
                {
                    key?.DeleteValue(TaskName, throwOnMissingValue: false);
                }
            }
            catch (Exception ex) { Log.Debug(ex, "Dọn registry HKLM"); }
        }
    }
}
