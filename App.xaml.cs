using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Runtime;
using System.Threading.Tasks;
using Windows.System.UserProfile;
using WinExSpectrumTest.Service;
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
        public static IServiceProvider Services { get; private set; }
        private static readonly IHost _host = Host.CreateDefaultBuilder()            
             .ConfigureServices((context, services) =>
             {
                 services.AddSingleton<SettingViewModel>();
             }).Build();

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            InitializeComponent();
            Services = _host.Services;
            var systemLanguages = GlobalizationPreferences.Languages;
            try
            {
                if (systemLanguages[0].StartsWith("zh"))
                {
                    Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "zh";
                }
                else
                {
                    Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "en";
                }
            }
            catch {
            }
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected async override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            await _host.StartAsync();
            await DataJsonService.LoadSettingAsync();
            MainWindow = new MainWindow();
            MainWindow.Activate();
        }

        public static void Current_Exit()
        {
            try
            {
                _host.StopAsync().Wait();
            }
            catch (Exception)
            {
            }
            finally
            {
                Environment.Exit(0);
            }
        }
    }
}
