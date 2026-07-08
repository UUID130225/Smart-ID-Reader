using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;

namespace SmartIDReader.Helpers
{
    public static class AdminHelper
    {
        public static bool IsRunningAsAdmin()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool TryRelaunchAsAdmin(string[] args = null)
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return false;

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = args != null && args.Length > 0 ? string.Join(" ", args.Select(a => $"\"{a}\"")) : ""
                };

                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
