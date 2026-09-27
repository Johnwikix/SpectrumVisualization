using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using System.Reflection;
using Windows.Storage.Streams;
using Windows.UI;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Services;

namespace AuroraRenderProbe;

internal static class ProbeProgram
{
    [STAThread]
    static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(args =>
        {
            SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            _ = new ProbeApp();
        });
    }
}

public sealed partial class ProbeApp : Application
{
    private Window _window = null!;
    private readonly string _output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../"));
    private readonly List<string> _results = [];
    public ProbeApp()
    {
        UnhandledException += (_, e) => File.WriteAllText(Path.Combine(_output, "aurora-render-results.txt"), e.Exception.ToString());
        AppDomain.CurrentDomain.UnhandledException += (_, e) => File.WriteAllText(Path.Combine(_output, "aurora-render-results.txt"), e.ExceptionObject.ToString());
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window { Title = "Aurora rendering regression" };
        var control = new CanvasAnimatedControl { Width = 640, Height = 640, Paused = true };
        _window.Content = control;
        control.CreateResources += (s, e) => e.TrackAsyncAction(RunAsync(control).AsAsyncAction());
        _window.Activate();
    }

    private static object? Field(object owner, string name) => owner.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner);
    private static bool Animating(AuroraRingEffect effect) => (bool)Field(effect, "_titleText")!
        .GetType().GetProperty("IsAnimating")!.GetValue(Field(effect, "_titleText"))!;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Media(AuroraRingEffect effect, string? title, string? artist, IRandomAccessStreamReference? cover)
        => typeof(AuroraRingEffect).GetMethod("OnMediaTextChanged", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(effect, [title, artist, cover]);

    private async Task RunAsync(CanvasAnimatedControl control)
    {
        int exitCode = 0;
        try
        {
            using var analyzer = new SpectrumAnalyzer();
            var media = new MediaInfoService();
            typeof(MediaInfoService).GetProperty(nameof(MediaInfoService.Duration))!.SetValue(media, TimeSpan.FromMinutes(3));
            using var effect = new AuroraRingEffect();
            effect.Initialize(new VisualizerServices { Control = control, Analyzer = analyzer, Media = media });
            effect.OnResize(640, 640);
            AppSettings.RotationSpeed = 0;
            AppSettings.CoverPulseEnabled = false;
            AppSettings.IsDrawRoundSpectrum = true;
            AppSettings.FontShadow = 70;
            using var target = new CanvasRenderTarget(control.Device, 640, 640, 96);
            void Frame()
            {
                effect.Update(1.0 / 60);
                using var ds = target.CreateDrawingSession();
                ds.Clear(Color.FromArgb(0, 0, 0, 0));
                effect.Draw(ds, 640, 640);
            }
            async Task<IRandomAccessStreamReference> Cover(Color color)
            {
                using var bitmap = new CanvasRenderTarget(control.Device, 80, 120, 96);
                using (var ds = bitmap.CreateDrawingSession()) ds.Clear(color);
                var stream = new InMemoryRandomAccessStream();
                await bitmap.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                stream.Seek(0);
                return RandomAccessStreamReference.CreateFromStream(stream);
            }
            var red = await Cover(Color.FromArgb(255, 240, 50, 40));
            var blue = await Cover(Color.FromArgb(255, 30, 100, 230));
            Media(effect, "Aurora 北极光", "Artist 艺术家", red);
            Frame();
            await Task.Delay(250);
            for (int i = 0; i < 30; i++) Frame();
            Require(Field(effect, "_albumArt") != null, "First cover failed to decode");
            Media(effect, "Aurora 北极光", "Artist 艺术家", blue);
            Frame();
            await Task.Delay(250);
            Frame();
            Require(Field(effect, "_previousAlbumArt") != null, "Scale transition lost its outgoing cover");
            for (int i = 0; i < 8; i++) Frame();
            await target.SaveAsync(Path.Combine(_output, "aurora-cover-transition.png"), CanvasBitmapFileFormat.Png);
            for (int i = 0; i < 30; i++) Frame();
            Require(Field(effect, "_previousAlbumArt") == null, "Outgoing cover was not released");
            _results.Add("PASS: image decode, scale overlap, transition completion and outgoing bitmap release");

            foreach (string mode in new[] { "default", "fade", "wipe", "blur", "elastic", "motion-blur", "pivot", "zoom", "none" })
            {
                AppSettings.SmtcTextAnimation = mode;
                Frame();
                Media(effect, $"{mode} 星河 e\u0301 🎵 العربية long title for ellipsis", "新艺术家 New Artist", blue);
                Frame();
                Require(Animating(effect) == (mode != "none"), $"{mode}: animation not started");
                for (int i = 0; i < 10; i++) Frame();
                var layer = (CanvasRenderTarget)Field(effect, "_textLayer")!;
                Require(layer.GetPixelColors().Any(c => c.A > 0), $"{mode}: shadow source is empty");
                Require(Field(effect, "_shadowInner") != null && Field(effect, "_shadowOuter") != null, "Shadows disappeared");
                if (mode == "default")
                    await target.SaveAsync(Path.Combine(_output, "aurora-text-transition.png"), CanvasBitmapFileFormat.Png);
                for (int i = 0; i < 150; i++) Frame();
                Require(!Animating(effect), $"{mode}: animation never settled");
                Media(effect, $"{mode} 星河 e\u0301 🎵 العربية long title for ellipsis", "新艺术家 New Artist", blue);
                Frame();
                Require(!Animating(effect), "Duplicate metadata restarted animation");
                AppSettings.FontOpacity = 0;
                Frame();
                AppSettings.FontOpacity = 0.5f;
                Frame();
                Require(((CanvasRenderTarget)Field(effect, "_textLayer")!).GetPixelColors().Max(c => c.A) <= 128,
                    "Shadow source did not follow font opacity");
                AppSettings.FontOpacity = 1;
                _results.Add($"PASS: {mode}: CJK, combining marks, emoji, RTL, ellipsis, animated shadow, completion, duplicate metadata, opacity");
            }
            AppSettings.SmtcTextAnimation = "default";
            Media(effect, "first", "one", red);
            Frame();
            Media(effect, "last", "two", blue);
            Frame();
            await Task.Delay(350);
            for (int i = 0; i < 150; i++) Frame();
            Require((string?)Field(effect, "_title") == "last", "Rapid track changes applied stale metadata");
            var lastCover = (CanvasBitmap)Field(effect, "_albumArt")!;
            Require(lastCover.GetPixelColors()[0].B > 200, "Rapid track changes applied stale cover");
            Media(effect, null, null, null);
            for (int i = 0; i < 150; i++) Frame();
            Require(Field(effect, "_albumArt") == null && Field(effect, "_previousAlbumArt") == null, "Empty session retained album art");
            effect.OnResize(420, 420);
            using (var ds = target.CreateDrawingSession()) effect.Draw(ds, 420, 420);
            Media(effect, "pending at disposal", "", red);
            Frame();
            effect.Dispose();
            await Task.Delay(250);
            Require(Field(effect, "_pendingCover") == null, "Disposed effect accepted a decoded bitmap");
            _results.Add("PASS: rapid changes, latest cover wins, empty session, resize, pending decode at disposal");
        }
        catch (Exception ex)
        {
            _results.Add(ex.ToString());
            exitCode = 1;
        }
        File.WriteAllLines(Path.Combine(_output, "aurora-render-results.txt"), _results);
        Environment.Exit(exitCode);
    }
}
