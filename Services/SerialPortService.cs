using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SmartIDReader.Models;
using SmartIDReader.Services;
using Serilog;

namespace SmartIDReader.Services
{
    public class SerialPortService : IDisposable
    {
        public event Action<bool, string>   StatusChanged;  // (connected, message)
        public event Action<string, string> DataReceived;   // (raw, formatted)

        private SerialPort _port;
        private System.Timers.Timer _reconnectTimer;
        private System.Timers.Timer _scanDebounceTimer;

        private readonly object _bufferLock = new object();
        private readonly object _portLock   = new object();

        private string      _buffer = "";
        private Encoding    _decoder;
        private AppSettings _cfg;
        private bool        _disposed;

        // ─────────────────────────── Public API ─────────────────────────

        public void Connect(AppSettings config)
        {
            lock (_portLock)
            {
                _cfg = config;
                ClosePort();
                StopTimer(ref _reconnectTimer);

                string targetPort = "";

                // 1. Ưu tiên tìm thiết bị bằng VendorId / ProductId hoặc DeviceName trước
                var ports = ListPorts();
                PortInfo match = null;

                if (!string.IsNullOrEmpty(config.VendorId) && !string.IsNullOrEmpty(config.ProductId))
                {
                    match = ports.Find(p =>
                        p.VendorId.Equals(config.VendorId, StringComparison.OrdinalIgnoreCase) &&
                        p.ProductId.Equals(config.ProductId, StringComparison.OrdinalIgnoreCase));
                }

                if (match == null && !string.IsNullOrEmpty(config.DeviceName))
                {
                    match = ports.Find(p =>
                        p.FriendlyName != null &&
                        p.FriendlyName.IndexOf(config.DeviceName, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                // 2. Nếu tìm thấy thiết bị khớp, lấy cổng COM hiện tại của nó
                if (match != null)
                {
                    targetPort = match.Path;
                    Log.Information("Tìm thấy thiết bị '{DeviceName}' tại cổng: {Port}", match.FriendlyName ?? config.DeviceName, targetPort);
                    config.Port = targetPort; // Đồng bộ lại cổng COM thực tế
                }
                else
                {
                    // 3. Fallback: Nếu không thấy thiết bị khớp theo VID/PID, thử dùng cổng cứng đã lưu nếu nó còn cắm
                    if (!string.IsNullOrEmpty(config.Port))
                    {
                        bool portExists = ports.Exists(p => p.Path.Equals(config.Port, StringComparison.OrdinalIgnoreCase));
                        if (portExists)
                        {
                            targetPort = config.Port;
                        }
                    }
                }

                // 4. Nếu vẫn chưa tìm ra cổng (hoặc cấu hình trống), thực hiện dò tìm tự động
                if (string.IsNullOrEmpty(targetPort))
                {
                    Log.Information("Chưa cấu hình thiết bị, bắt đầu dò tìm tự động...");
                    var autoPort = AutoDetectPort();
                    if (autoPort != null)
                    {
                        targetPort = autoPort.Path;
                        config.Port = autoPort.Path;
                        config.DeviceName = autoPort.FriendlyName;
                        config.VendorId = autoPort.VendorId;
                        config.ProductId = autoPort.ProductId;
                        SettingsManager.Save(config); // Lưu cấu hình thiết bị tìm được
                        Log.Information("Đã tìm thấy thiết bị tự động: {Port} ({Name})", autoPort.Path, autoPort.FriendlyName);
                    }
                    else
                    {
                        Raise(false, "Không tìm thấy thiết bị đọc CCCD");
                        StartReconnect();
                        return;
                    }
                }

                string label = !string.IsNullOrEmpty(config.DeviceName)
                    ? $"{targetPort} ({config.DeviceName})" : targetPort;

                Raise(false, $"Đang kết nối {label}...");
                try
                {
                    _decoder = config.Encoding == "ascii" ? Encoding.ASCII : Encoding.UTF8;
                    lock (_bufferLock) { _buffer = ""; }

                    _port = new SerialPort
                    {
                        PortName     = targetPort,
                        BaudRate     = config.BaudRate,
                        DataBits     = config.DataBits,
                        Parity       = ParseParity(config.Parity),
                        StopBits     = ParseStopBits(config.StopBits),
                        Encoding     = _decoder,
                        ReadTimeout  = 500,
                        WriteTimeout = 500
                    };
                    _port.DataReceived  += OnData;
                    _port.ErrorReceived += OnError;
                    _port.Open();

                    Raise(true, $"Đã kết nối {label}");
                    Log.Information("Connected to {Port}", targetPort);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Lỗi mở cổng {Port}", targetPort);
                    Raise(false, $"Lỗi kết nối: {ex.Message}");
                    StartReconnect();
                }
            }
        }

        public void Disconnect()
        {
            lock (_portLock)
            {
                StopTimer(ref _reconnectTimer);
                ClosePort();
                Raise(false, "Đã ngắt kết nối");
            }
        }

        public bool IsConnected { get { lock (_portLock) { return _port?.IsOpen == true; } } }

        // ─────────────────────────── Port Auto Detection ──────────────────

        /// <summary>
        /// Dò tìm tự động cổng COM khả dụng nhất (có friendly name chứa USB/Serial hoặc Prolific/CH340/FTDI...)
        /// </summary>
        public static PortInfo AutoDetectPort()
        {
            var ports = ListPorts();
            if (ports.Count == 0) return null;

            // 1. Ưu tiên các cổng COM của thiết bị đọc thẻ/nối tiếp USB thường gặp
            var match = ports.Find(p => 
                p.FriendlyName != null && (
                    p.FriendlyName.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.FriendlyName.IndexOf("Serial", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.FriendlyName.IndexOf("Prolific", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.FriendlyName.IndexOf("CH34", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.FriendlyName.IndexOf("FTDI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.FriendlyName.IndexOf("Silicon Labs", StringComparison.OrdinalIgnoreCase) >= 0
                )
            );

            if (match != null) return match;

            // 2. Fallback: Lấy cổng COM đầu tiên
            return ports[0];
        }

        // ─────────────────────────── Port Listing ────────────────────────

        public static List<PortInfo> ListPorts()
        {
            var list = new List<PortInfo>();
            try
            {
                using (var q = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%)'"))
                {
                    foreach (ManagementObject o in q.Get())
                    {
                        string cap = o["Caption"]?.ToString() ?? "";
                        string did = o["DeviceID"]?.ToString() ?? "";

                        int s = cap.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
                        int e = s > -1 ? cap.IndexOf(')', s) : -1;
                        if (s < 0 || e < 0) continue;

                        list.Add(new PortInfo
                        {
                            Path         = cap.Substring(s + 1, e - s - 1),
                            FriendlyName = cap.Substring(0, s).TrimEnd(),
                            VendorId     = ExtractId(did, "VID_"),
                            ProductId    = ExtractId(did, "PID_")
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "WMI thất bại, dùng SerialPort.GetPortNames()");
                foreach (string p in SerialPort.GetPortNames())
                    list.Add(new PortInfo { Path = p });
            }
            list.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        // ─────────────────────────── Data Handling ───────────────────────

        private void OnData(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string text;
                lock (_portLock)
                {
                    if (_port?.IsOpen != true) return;
                    text = _port.ReadExisting();
                }

                bool hasTerm;
                lock (_bufferLock)
                {
                    _buffer += text;
                    hasTerm = _buffer.IndexOf('\n') >= 0 || _buffer.IndexOf('\r') >= 0;
                }

                var old = Interlocked.Exchange(ref _scanDebounceTimer, null);
                old?.Stop();

                if (hasTerm)
                {
                    ProcessBuffer();
                }
                else
                {
                    var t = new System.Timers.Timer(50) { AutoReset = false };
                    t.Elapsed += (_, __) => ProcessBuffer();
                    _scanDebounceTimer = t;
                    t.Start();
                }
            }
            catch (Exception ex) { Log.Error(ex, "Lỗi đọc dữ liệu serial"); }
        }

        private void ProcessBuffer()
        {
            string data;
            lock (_bufferLock)
            {
                if (string.IsNullOrWhiteSpace(_buffer)) return;
                data = _buffer.Trim();
                _buffer = "";
            }
            if (string.IsNullOrEmpty(data)) return;

            Log.Information("Scanned: {Data}", data);
            DataReceived?.Invoke(data, data);
        }

        private void OnError(object sender, SerialErrorReceivedEventArgs e)
        {
            Log.Error("Serial hardware error: {Type}", e.EventType);
            lock (_portLock)
            {
                ClosePort();
                Raise(false, $"Lỗi phần cứng: {e.EventType}");
                StartReconnect();
            }
        }

        // ─────────────────────────── Auto-Reconnect ──────────────────────

        private void StartReconnect()
        {
            if (_cfg?.AutoReconnect != true || _reconnectTimer != null) return;

            int ms = Math.Max(_cfg.ReconnectIntervalSeconds, 5) * 1000;
            _reconnectTimer = new System.Timers.Timer(ms) { AutoReset = true };
            _reconnectTimer.Elapsed += async (_, __) =>
            {
                bool open;
                lock (_portLock) { open = _port?.IsOpen == true; }
                if (open) { StopTimer(ref _reconnectTimer); return; }

                try
                {
                    var cfg   = _cfg;
                    var ports = ListPorts();
                    PortInfo found = null;

                    found = found ?? ports.Find(p => p.Path.Equals(cfg.Port, StringComparison.OrdinalIgnoreCase));

                    if (found == null && !string.IsNullOrEmpty(cfg.VendorId))
                        found = ports.Find(p =>
                            p.VendorId.Equals(cfg.VendorId, StringComparison.OrdinalIgnoreCase) &&
                            p.ProductId.Equals(cfg.ProductId, StringComparison.OrdinalIgnoreCase));

                    if (found == null && !string.IsNullOrEmpty(cfg.DeviceName))
                        found = ports.Find(p =>
                            p.FriendlyName?.IndexOf(cfg.DeviceName, StringComparison.OrdinalIgnoreCase) >= 0);

                    // Nếu mất cổng cũ nhưng cấu hình rỗng, thử tìm tự động cổng mới
                    if (found == null && string.IsNullOrEmpty(cfg.Port))
                    {
                        found = AutoDetectPort();
                    }

                    if (found != null)
                    {
                        StopTimer(ref _reconnectTimer);
                        if (!found.Path.Equals(cfg.Port, StringComparison.OrdinalIgnoreCase))
                        {
                            cfg.Port = found.Path;
                            SettingsManager.Save(cfg);
                        }
                        Connect(cfg);
                    }
                }
                catch (Exception ex) { Log.Error(ex, "Lỗi reconnect loop"); }
            };
            _reconnectTimer.Start();
        }

        // ─────────────────────────── Helpers ─────────────────────────────

        private void ClosePort()
        {
            if (_port == null) return;
            try
            {
                _port.DataReceived  -= OnData;
                _port.ErrorReceived -= OnError;
                if (_port.IsOpen) _port.Close();
                _port.Dispose();
            }
            catch { }
            finally { _port = null; }
        }

        private void Raise(bool connected, string msg) => StatusChanged?.Invoke(connected, msg);

        private static void StopTimer(ref System.Timers.Timer t)
        {
            var x = Interlocked.Exchange(ref t, null);
            x?.Stop();
            x?.Dispose();
        }

        private static Parity ParseParity(string s)
        {
            switch (s?.ToLower())
            {
                case "even":  return Parity.Even;
                case "odd":   return Parity.Odd;
                case "mark":  return Parity.Mark;
                case "space": return Parity.Space;
                default:      return Parity.None;
            }
        }

        private static StopBits ParseStopBits(float v) =>
            v >= 2f ? StopBits.Two : v >= 1.5f ? StopBits.OnePointFive : StopBits.One;

        private static string ExtractId(string s, string prefix)
        {
            int i = s.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            int start = i + prefix.Length, end = start;
            while (end < s.Length && s[end] != '&' && s[end] != '\\') end++;
            return s.Substring(start, end - start);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopTimer(ref _reconnectTimer);
            var d = Interlocked.Exchange(ref _scanDebounceTimer, null);
            d?.Stop();
            lock (_portLock) { ClosePort(); }
        }
    }

    public class PortInfo
    {
        public string Path         { get; set; }
        public string FriendlyName { get; set; }
        public string VendorId     { get; set; }
        public string ProductId    { get; set; }
        public string DisplayName  => string.IsNullOrEmpty(FriendlyName) ? Path : $"{Path} ({FriendlyName})";
    }
}
