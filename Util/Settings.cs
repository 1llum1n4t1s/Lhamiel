using System.Text.Json;
using System.IO;

namespace GGEZArchiver.Util
{
    public class Settings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GGEZArchiver",
            "settings.json");

        public string OutputDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        public bool AssociateZip { get; set; } = true;
        public bool Associate7z { get; set; } = true;
        public bool AssociateLzh { get; set; } = true;
        public bool AssociateCab { get; set; } = true;

        public static Settings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<Settings>(json);
                    return settings ?? new Settings();
                }
            }
            catch
            {
                // 設定ファイルの読み込みに失敗した場合はデフォルト設定を使用
            }

            return new Settings();
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // 設定の保存に失敗した場合は無視
            }
        }
    }
} 