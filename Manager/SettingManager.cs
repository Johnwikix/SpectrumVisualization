using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinExSpectrumTest.Manager
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Windows.Storage;
    using WinExSpectrumTest.Model; // 确保引用了你的 SaveSetting 类所在的命名空间

    public static class SettingManager
    {
        // 配置文件的名称
        private static readonly string FileName = "appsettings.json";

        /// <summary>
        /// 获取配置文件路径（通常位于应用本地数据文件夹）
        /// </summary>
        private static string GetSettingFilePath()
        {
            try
            {
                var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                return Path.Combine(localFolder, FileName);
            }
            catch {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
            }
        }

        // -------------------------------------------------------------

        /// <summary>
        /// 异步加载配置
        /// </summary>
        /// <returns>配置对象，如果加载失败或文件不存在则返回一个带有默认值的新对象</returns>
        public static async Task<SaveSetting> LoadSettingsAsync()
        {
            string filePath = GetSettingFilePath();

            if (!File.Exists(filePath))
            {
                // 如果文件不存在，返回默认配置
                var newSetting = new SaveSetting();
                await SaveSettingsAsync(newSetting);
                return newSetting;
            }

            try
            {
                // 异步读取文件内容
                using FileStream openStream = File.OpenRead(filePath);
                var settings = await JsonSerializer.DeserializeAsync(openStream, SettingsJsonContext.Default.SaveSetting);
                return settings ?? new SaveSetting();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载配置失败: {ex.Message}");
                return new SaveSetting();
            }
        }

        // -------------------------------------------------------------

        /// <summary>
        /// 异步保存配置
        /// </summary>
        /// <param name="settings">要保存的配置对象</param>
        /// <returns>保存操作是否成功</returns>
        public static async Task<bool> SaveSettingsAsync(SaveSetting settings)
        {
            string filePath = GetSettingFilePath();

            try
            {
                // WriteIndented = true 使输出的 JSON 格式化，便于阅读
                var options = new JsonSerializerOptions { WriteIndented = true };
                using FileStream createStream = File.Create(filePath);
                await JsonSerializer.SerializeAsync(createStream, settings, SettingsJsonContext.Default.SaveSetting);
                return true;
            }
            catch (Exception ex)
            {
                // 捕获写入文件或序列化过程中的任何异常
                System.Diagnostics.Debug.WriteLine($"保存配置失败: {ex.Message}");
                return false;
            }
        }

    }
}
