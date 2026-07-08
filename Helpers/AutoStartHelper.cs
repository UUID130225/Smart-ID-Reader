using System;
using Microsoft.Win32;
using Serilog;

namespace SmartIDReader.Helpers
{
    public static class AutoStartHelper
    {
        private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "SmartIDReader";

        public static void SetAutoStart(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true))
                {
                    if (key == null) return;
                    if (enabled)
                    {
                        string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(exe))
                            key.SetValue(AppName, $"\"{exe}\" --hidden");
                    }
                    else
                    {
                        key.DeleteValue(AppName, throwOnMissingValue: false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Lỗi khi đặt auto-start");
            }
        }

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: false))
                    return key?.GetValue(AppName) != null;
            }
            catch { return false; }
        }
    }
}
