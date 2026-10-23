using System;
using Windows.UI.ViewManagement;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Services;

/// <summary>Cold OS notifications publish immutable theme state; render workers never access WinRT UI state.</summary>
internal sealed class FoliaSystemThemeService : IDisposable
{
    private readonly object _gate = new();
    private UISettings? _settings;
    private bool _started;

    internal void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
            AppSettings.Changed += OnSettingsChanged;
            RefreshSubscription();
        }
    }

    private void OnSettingsChanged(string name)
    {
        if (name != nameof(AppSettings.FoliaTheme)) return;
        lock (_gate)
            if (_started) RefreshSubscription();
    }

    private void RefreshSubscription()
    {
        if (AppSettings.FoliaTheme.Variant != "system")
        {
            StopSubscription();
            return;
        }
        if (_settings != null) return;
        try
        {
            _settings = new UISettings();
            _settings.ColorValuesChanged += OnColorValuesChanged;
            Publish(_settings);
        }
        catch (Exception error)
        {
            StopSubscription();
            App.WriteCrashLog("Folia system theme", error.Message, error);
        }
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        lock (_gate)
            if (_started && ReferenceEquals(sender, _settings))
                try { Publish(sender); }
                catch (Exception error) { App.WriteCrashLog("Folia system theme update", error.Message, error); }
    }

    private static void Publish(UISettings settings)
    {
        var background = settings.GetColorValue(UIColorType.Background);
        AppSettings.UpdateFoliaSystemTheme(background.R * .2126 + background.G * .7152 + background.B * .0722 >= 128);
    }

    private void StopSubscription()
    {
        var settings = _settings;
        _settings = null;
        if (settings == null) return;
        try { settings.ColorValuesChanged -= OnColorValuesChanged; }
        catch (Exception error) { App.WriteCrashLog("Folia system theme cleanup", error.Message, error); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_started) return;
            _started = false;
            AppSettings.Changed -= OnSettingsChanged;
            StopSubscription();
        }
    }
}
