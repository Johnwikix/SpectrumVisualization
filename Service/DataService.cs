using SQLite;
using System.Linq;
using System.Threading.Tasks;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Service
{
    public class DataService
    {
        private static SQLiteAsyncConnection _dbConnection;
        private static string DbPath = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "MusicDatabase.db");
        public static async Task Initialize()
        {
            if (_dbConnection == null)
            {
                _dbConnection = new SQLiteAsyncConnection(DbPath);
                await _dbConnection.CreateTableAsync<SaveSetting>();
            }
        }

        public static async Task LoadSettingAsync()
        {
            var settings = await _dbConnection.Table<SaveSetting>().ToListAsync();
            AppSettings.RotationSpeed = settings.FirstOrDefault()?.RotationSpeed ?? 10.0f;
            AppSettings.CoverOpacity = settings.FirstOrDefault()?.CoverOpacity ?? 1.0f;
            AppSettings.SpectrumOpacity = settings.FirstOrDefault()?.SpectrumOpacity ?? 1.0f;
            AppSettings.FontOpacity = settings.FirstOrDefault()?.FontOpacity ?? 1.0f;
            AppSettings.SmoothingFactor = settings.FirstOrDefault()?.SmoothingFactor ?? 0.95f;
            AppSettings.IsDrawPlainSpectrum = settings.FirstOrDefault()?.IsDrawPlainSpectrum ?? false;
            AppSettings.IsDrawRoundSpectrum = settings.FirstOrDefault()?.IsDrawRoundSpectrum ?? true;
            AppSettings.Sensitivity = settings.FirstOrDefault()?.Sensitivity ?? 10.0f;
            AppSettings.AppStyle = settings.FirstOrDefault()?.AppStyle ?? "Acrylic";
            AppSettings.CustomAcrylicOpacity = settings.FirstOrDefault()?.CustomAcrylicOpacity ?? 0.5f;
            AppSettings.CustomColorAlpha = settings.FirstOrDefault()?.CustomColorAlpha ?? 255;
            AppSettings.CustomColorRed = settings.FirstOrDefault()?.CustomColorRed ?? 128;
            AppSettings.CustomColorGreen = settings.FirstOrDefault()?.CustomColorGreen ?? 128;
            AppSettings.CustomColorBlue = settings.FirstOrDefault()?.CustomColorBlue ?? 128;
            AppSettings.IsUpdateBackDrop = settings.FirstOrDefault()?.IsUpdateBackDrop ?? false;
            AppSettings.AppTheme = settings.FirstOrDefault()?.AppTheme ?? "Default";
            AppSettings.elementTheme = settings.FirstOrDefault()?.elementTheme == "Light" ? Microsoft.UI.Xaml.ElementTheme.Light : settings.FirstOrDefault()?.elementTheme == "Dark" ? Microsoft.UI.Xaml.ElementTheme.Dark : Microsoft.UI.Xaml.ElementTheme.Default;
            AppSettings.RefreshRate = settings.FirstOrDefault()?.RefreshRate ?? 60.0f;
        }

        public static async Task SaveSettingAsync()
        {
            var settings = new SaveSetting
            {
                RotationSpeed = AppSettings.RotationSpeed,
                CoverOpacity = AppSettings.CoverOpacity,
                SpectrumOpacity = AppSettings.SpectrumOpacity,
                FontOpacity = AppSettings.FontOpacity,
                SmoothingFactor = AppSettings.SmoothingFactor,
                IsDrawPlainSpectrum = AppSettings.IsDrawPlainSpectrum,
                IsDrawRoundSpectrum = AppSettings.IsDrawRoundSpectrum,
                Sensitivity = AppSettings.Sensitivity,
                AppStyle = AppSettings.AppStyle,
                CustomAcrylicOpacity = AppSettings.CustomAcrylicOpacity,
                CustomColorAlpha = AppSettings.CustomColorAlpha,
                CustomColorRed = AppSettings.CustomColorRed,
                CustomColorGreen = AppSettings.CustomColorGreen,
                CustomColorBlue = AppSettings.CustomColorBlue,
                IsUpdateBackDrop = AppSettings.IsUpdateBackDrop,
                AppTheme = AppSettings.AppTheme,
                elementTheme = AppSettings.elementTheme == Microsoft.UI.Xaml.ElementTheme.Light ? "Light" : AppSettings.elementTheme == Microsoft.UI.Xaml.ElementTheme.Dark ? "Dark" : "Default",
                RefreshRate = AppSettings.RefreshRate
            };
            var existingSettings = await _dbConnection.Table<SaveSetting>().ToListAsync();
            if (existingSettings.Count > 0)
            {
                settings.GetType().GetProperty("Id").SetValue(settings, existingSettings.First().GetType().GetProperty("Id").GetValue(existingSettings.First()));
                await _dbConnection.UpdateAsync(settings);
            }
            else
            {
                await _dbConnection.InsertAsync(settings);
            }
        }
    }
}
