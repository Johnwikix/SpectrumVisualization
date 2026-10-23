using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WinExSpectrumTest.Helper;
using WinExSpectrumTest.Service;

namespace WinExSpectrumTest;

internal static class Program
{
    internal static SingleInstance? Instance { get; private set; }

    [STAThread]
    private static void Main(string[] args)
    {
        // Initialize managed diagnostics before constructing App/Host or loading XAML.
        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpectrumVisualization", "Logs");
        AppLog.Initialize(fallback);
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => AppLog.Shutdown();
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            try { AppLog.SetDirectory(Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "Logs")); }
            catch (Exception error) { AppLog.Caught(error, "Package LocalState unavailable; using per-user fallback logs."); }
            AppLog.Info($"Process starting; OS={Environment.OSVersion}; runtime={Environment.Version}; x64={Environment.Is64BitProcess}.");
            Instance = new SingleInstance();
            if (!Instance.TryAcquire())
            {
                AppLog.Info("Another instance owns the mutex; requesting activation and exiting.");
                Instance.RequestActivation();
                return;
            }
            Application.Start(_ =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                new App();
            });
        }
        catch (Exception error)
        {
            AppLog.Fatal("Application entry point", error);
            Environment.ExitCode = 1;
        }
        finally
        {
            try { Instance?.Dispose(); }
            catch (Exception error) { AppLog.Caught(error, "Single-instance cleanup failed."); }
            AppLog.Shutdown();
        }
    }

    private static void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs args) =>
        AppLog.Fatal("AppDomain unhandled exception", args.ExceptionObject as Exception, $"Terminating={args.IsTerminating}; {args.ExceptionObject}");

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        AppLog.Caught(args.Exception, "Unobserved background task exception.");
        args.SetObserved();
    }
}
