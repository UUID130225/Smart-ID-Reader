using System;

namespace SmartIDReader.Models
{
    public class AppSettings
    {
        public string Port                     { get; set; } = "";
        public string VendorId                 { get; set; } = "";
        public string ProductId                { get; set; } = "";
        public string DeviceName               { get; set; } = "";
        
        public int    BaudRate                 { get; set; } = 9600;
        public int    DataBits                 { get; set; } = 8;
        public string Parity                   { get; set; } = "none";
        public float  StopBits                 { get; set; } = 1f;
        public string Encoding                 { get; set; } = "utf8";

        public bool   StartWithWindows         { get; set; } = false;
        public bool   AutoReconnect            { get; set; } = true;
        public int    ReconnectIntervalSeconds { get; set; } = 5;
    }
}
