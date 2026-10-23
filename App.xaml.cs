using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using System;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.System.UserProfile;
using WinExSpectrumTest.Service;
using WinExSpectrumTest.Services;
using WinExSpectrumTest.ViewModel;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinExSpectrumTest
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public static MainWindow MainWindow { get; private set; }
        internal static bool IsChineseLanguage { get; private set; }
        public static MediaInfoService MediaInfoService { get; private set; } = new();
        internal static FoliaPlaybackService FoliaPlayback { get; } = new(MediaInfoService);
        public static IServiceProvider Services { get; private set; }
        internal static Rendering.GpuShaderWarmup ShaderWarmup { get; } = new();
        private static FoliaSystemThemeService FoliaSystemTheme { get; } = new();
        private static readonly IHost _host = Host.CreateDefaultBuilder()
             .ConfigureServices((context, services) =>
             {
                 services.AddSingleton(_ => new WinExSpectrumTest.Audio.SpectrumAnalyzer(captureAudio: !FoliaPlaybackService.PreviewEnabled));
                 services.AddSingleton<SettingViewModel>();
             }).Build();

        // Prevent eager beforefieldinit execution from running Host construction before Program installs diagnostics.
        static App() { }

        /// <summary>
        /// Initializes XAML after Program has established diagnostics and single-instance ownership.
        /// </summary>
        public App()
        {
            UnhandledException += OnXamlUnhandledException;
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            InitializeComponent();
            Services = _host.Services;

            try
            {
                var systemLanguages = GlobalizationPreferences.Languages;
                IsChineseLanguage = systemLanguages.Count > 0 && systemLanguages[0].StartsWith("zh", StringComparison.OrdinalIgnoreCase);
                if (IsChineseLanguage)
                {
                    Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "zh";
                }
                else
                {
                    Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "en";
                }
            }
            catch (global::System.Exception caughtException) {
                AppLog.Caught(caughtException);
            }
        }

        private static void OnXamlUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            AppLog.Fatal("XAML unhandled exception", e.Exception, e.Message);
        }

        internal static void WriteCrashLog(string source, string message, Exception? exception,
            [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            if (exception != null) AppLog.Caught(exception, message == exception.Message ? source : $"{source}: {message}", member, file, line);
            else AppLog.Info($"{source}: {message}");
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected async override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                _startupTask = StartAsync();
                await _startupTask;
            }
            catch (Exception error)
            {
                AppLog.Fatal("Application startup failed", error);
                Current_Exit(1);
            }
        }

        private static Task? _startupTask;
        private static bool _settingsLoaded;

        private static async Task StartAsync()
        {
            AppLog.Info("Starting application services and loading settings.");
            await _host.StartAsync();
            if (IsExiting) return;
            await DataJsonService.LoadSettingAsync();
            _settingsLoaded = true;
            if (IsExiting) return;
            FoliaSystemTheme.Start();
            if (FoliaPlaybackService.PreviewEnabled)
            {
                Model.AppSettings.VisualEffect = "folia";
                Model.AppSettings.WallpaperEnabled = false;
            }
            if (IsExiting) return;
            _ = ShaderWarmup.Start();
            MainWindow = new MainWindow();
            MainWindow.Activate();
            var dispatcher = MainWindow.DispatcherQueue;
            Microsoft.UI.Dispatching.DispatcherQueueHandler showExisting = MainWindow.ShowFromActivation;
            Program.Instance?.Listen(() =>
            {
                if (!dispatcher.TryEnqueue(showExisting))
                    AppLog.Info("Activation was rejected because the UI dispatcher is stopping.");
            });
            AppLog.Info($"Main window ready; version={MainWindow.ViewModel.AppVersion}; wallpaper={Model.AppSettings.WallpaperEnabled}; background={Model.AppSettings.IsRunningBackend}; effect={Model.AppSettings.VisualEffect}.");
            if (Model.AppSettings.WallpaperEnabled)
            {
                // Let WinUI finish its first layout before reparenting its HWND.
                if (!MainWindow.DispatcherQueue.TryEnqueue(() => MainWindow.SetWallpaperMode(true)))
                    AppLog.Info("Startup wallpaper request rejected by the UI dispatcher.");
            }
            // SMTC session enumeration can take hundreds of milliseconds; don't let it
            // delay the first frame. Effects receive late media updates through events.
            if (!FoliaPlaybackService.PreviewEnabled) _ = MediaInfoService.InitializeAsync();
        }

        private static Task? _exitTask;
        private static bool _isExiting;
        internal static bool IsExiting => _isExiting;
        public static void Current_Exit() => Current_Exit(0);
        private static void Current_Exit(int exitCode)
        {
            if (_isExiting) return;
            _isExiting = true;
            _exitTask = ExitAsync(exitCode);
        }

        private static async Task ExitAsync(int exitCode)
        {
            AppLog.Info($"Application shutdown requested; exitCode={exitCode}.");
            // Startup may still be awaiting settings/Host. Stop its continuation before releasing services.
            if (_startupTask != null)
            {
                try { await _startupTask; }
                catch (Exception error) { AppLog.Caught(error, "Startup ended with a failure during shutdown."); }
            }
            try { FoliaSystemTheme.Dispose(); }
            catch (Exception ex) { WriteCrashLog("Theme shutdown", ex.Message, ex); }
            Task warmupShutdown = CleanupAsync("Shader warmup shutdown", ShaderWarmup.StopAsync);
            Task shaderMaintenance = CleanupAsync("Shader cache shutdown", Rendering.ShaderCacheMaintenance.StopAsync);
            try { if (_settingsLoaded) DataJsonService.SaveSettingNow(); }
            catch (Exception ex) { WriteCrashLog("Settings shutdown", ex.Message, ex); }
            try
            {
                if (MainWindow != null) await MainWindow.StopRenderingAsync();
            }
            catch (Exception ex)
            {
                WriteCrashLog("Render shutdown", ex.Message, ex);
            }
            await shaderMaintenance;
            await warmupShutdown;
            try { await MediaInfoService.StopAsync(); }
            catch (Exception ex) { WriteCrashLog("Media shutdown", ex.Message, ex); }
            try { await FoliaPlayback.StopAsync(); }
            catch (Exception ex) { WriteCrashLog("Lyrics shutdown", ex.Message, ex); }
            try { Effects.Folia.FoliaSourcePlanner.ClearResults(); }
            catch (Exception error) { AppLog.Caught(error, "Folia source cache cleanup failed."); }
            try { Effects.Folia.FoliaGlyphCache.ClearMemory(); }
            catch (Exception error) { AppLog.Caught(error, "Folia glyph cache cleanup failed."); }
            try { MainWindow?.ReleaseWallpaper(); }
            catch (Exception ex) { WriteCrashLog("Wallpaper shutdown", ex.Message, ex); }
            try
            {
                await _host.StopAsync();
            }
            catch (Exception ex) { WriteCrashLog("Service shutdown", ex.Message, ex); }
            try { _host.Dispose(); }
            catch (Exception ex) { WriteCrashLog("Service disposal", ex.Message, ex); }
            AppLog.Info("Application shutdown completed.");
            await Task.Run(AppLog.Shutdown);
            Environment.Exit(exitCode);
        }

        private static async Task CleanupAsync(string source, Func<Task> cleanup)
        {
            try { await cleanup(); }
            catch (Exception error) { AppLog.Caught(error, source); }
        }
    }
}
