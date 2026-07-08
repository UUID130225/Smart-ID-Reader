using System;
using System.Drawing;
using System.Windows.Forms;
using Serilog;

namespace SmartIDReader.Services
{
    public class TrayService : IDisposable
    {
        private NotifyIcon _icon;
        private readonly bool _isAdmin;

        public event Action ShowWindowRequested;
        public event Action ExitRequested;

        public TrayService(bool isAdmin) { _isAdmin = isAdmin; }

        public void Initialize()
        {
            _icon = new NotifyIcon
            {
                Text    = "SmartID Reader" + (_isAdmin ? " (Admin)" : ""),
                Icon    = GetAppIcon(),
                Visible = true
            };

            _icon.Click       += (_, e) => { if (((MouseEventArgs)e).Button == MouseButtons.Left) ShowWindowRequested?.Invoke(); };
            _icon.DoubleClick += (_, __) => ShowWindowRequested?.Invoke();

            var menu = new ContextMenuStrip();
            menu.Items.Add("Mở bảng điều khiển").Click += (_, __) => ShowWindowRequested?.Invoke();
            menu.Items.Add(new ToolStripMenuItem(_isAdmin ? "Đang chạy quyền Admin ✓" : "Chạy quyền Admin") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Thoát").Click += (_, __) => ExitRequested?.Invoke();

            _icon.ContextMenuStrip = menu;
        }

        public void UpdateStatus(bool connected, string message)
        {
            if (_icon == null) return;
            string tip = $"SmartID Reader{(_isAdmin ? " (Admin)" : "")}\n{(connected ? "✔" : "✖")} {message}";
            _icon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        private static Icon GetAppIcon()
        {
            try { return Icon.ExtractAssociatedIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? ""); }
            catch { return SystemIcons.Application; }
        }

        public void Dispose()
        {
            if (_icon == null) return;
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
    }
}
