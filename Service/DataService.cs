using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.Playlists;
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
                Sensitivity = AppSettings.Sensitivity
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
