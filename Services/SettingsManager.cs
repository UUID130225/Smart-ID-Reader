using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using SmartIDReader.Models;
using Serilog;

namespace SmartIDReader.Services
{
    public static class SettingsManager
    {
        public static readonly string AppDir = Path.GetDirectoryName(
            Assembly.GetExecutingAssembly().Location);

        public static readonly string SettingsPath = Path.Combine(AppDir, "settings.setting");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    {
                        var s = new DataContractJsonSerializer(typeof(AppSettings));
                        return (AppSettings)s.ReadObject(ms);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Không đọc được settings.setting");
            }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                using (var ms = new MemoryStream())
                {
                    var s = new DataContractJsonSerializer(typeof(AppSettings));
                    s.WriteObject(ms, settings);
                    File.WriteAllText(SettingsPath, Encoding.UTF8.GetString(ms.ToArray()), Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Không ghi được settings.setting");
            }
        }
    }
}
