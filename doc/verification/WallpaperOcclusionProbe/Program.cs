using System.Diagnostics;
using System.Text.RegularExpressions;
using WinExSpectrumTest.Helper;
using Rect = WinExSpectrumTest.Helper.WallpaperOcclusionDetector.WindowRect;

internal static class Program
{
    private static readonly List<string> Results = [];
    private static int _exitCode;

    [STAThread]
    private static int Main(string[] args)
    {
        // Keep pure geometry regression checks independent of WinForms, windows and audio devices.
        if (args is ["--geometry"]) return GeometryChecks.Run();
        ApplicationConfiguration.Initialize();
        if (args.Length != 3) throw new ArgumentException("Arguments: wallpaper HWND, render-metrics.log, results.txt");
        IntPtr wallpaper = new(long.Parse(args[0]));
        var detector = new WallpaperOcclusionDetector(wallpaper);
        using var cover = new Form
        {
            Text = "Spectrum wallpaper occlusion verification",
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            BackColor = Color.FromArgb(28, 32, 40),
            TopMost = true,
            Bounds = new Rectangle(100, 100, 480, 240),
        };
        cover.Controls.Add(new Label
        {
            Text = "Wallpaper pause/resume verification — closes automatically",
            ForeColor = Color.White, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        });
        cover.Shown += async (_, _) =>
        {
            try
            {
                var primary = Screen.PrimaryScreen!.Bounds;
                Require(WallpaperOcclusionDetector.Covers(new Rect { Left = -10, Top = -10, Right = 110, Bottom = 110 },
                    new Rect { Left = 0, Top = 0, Right = 100, Bottom = 100 }), "spanning window covers display");
                Require(!WallpaperOcclusionDetector.Covers(new Rect { Left = -1920, Top = 0, Right = 0, Bottom = 1080 },
                    new Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 }), "secondary display does not cover primary");
                Require(!WallpaperOcclusionDetector.Covers(default, default), "empty rectangles are not covered");

                (bool Paused, long Frames)? lastPause = null;
                async Task Check(string stage, bool expected, int wait = 1400)
                {
                    await Task.Delay(wait);
                    Require(detector.IsFullyCovered() == expected, $"{stage}: native detection");
                    string[] lines = File.ReadAllLines(args[1]);
                    var state = lines.Select(line => Regex.Match(line, @"suspended=(True|False), totalFrames=(\d+)"))
                        .LastOrDefault(match => match.Success);
                    if (expected || lastPause != null)
                    {
                        Require(state != null && bool.Parse(state.Groups[1].Value) == expected,
                            $"{stage}: actual CanvasPanel pause state");
                    }
                    if (expected)
                    {
                        lastPause = (true, long.Parse(state!.Groups[2].Value));
                    }
                    else if (lastPause is { } paused)
                    {
                        long resumedFrames = long.Parse(state!.Groups[2].Value);
                        long added = resumedFrames - paused.Frames;
                        Require(added is >= 0 and <= 1, $"{stage}: no rendered frames while paused (actual {added})");
                        lastPause = null;
                    }
                    Results.Add($"PASS: {stage}, covered={expected}");
                }

                await Check("partial window", false);
                cover.FormBorderStyle = FormBorderStyle.Sizable;
                cover.WindowState = FormWindowState.Maximized;
                await Check("maximized work-area window", true);
                await Check("maximized held for three seconds", true, 3000);
                cover.WindowState = FormWindowState.Minimized;
                await Check("minimized maximized window resumes rendering", false);
                cover.WindowState = FormWindowState.Maximized;
                await Check("restored maximized window pauses again", true);
                cover.WindowState = FormWindowState.Normal;
                cover.FormBorderStyle = FormBorderStyle.None;
                cover.Bounds = new Rectangle(100, 100, 480, 240);
                await Check("restored partial window resumes rendering", false);
                cover.Bounds = primary;
                await Check("opaque full-screen", true);
                await Check("full-screen held for three seconds", true, 3000);
                cover.WindowState = FormWindowState.Minimized;
                await Check("minimized full-screen resumes rendering", false);
                cover.WindowState = FormWindowState.Normal;
                cover.Bounds = primary;
                await Check("restored full-screen pauses again", true);
                cover.Opacity = 0.6;
                await Check("transparent overlay keeps rendering", false);
                cover.Opacity = 1;
                await Check("opaque again", true);
                cover.Hide();
                await Check("hidden window resumes rendering", false);
                cover.Show();
                cover.Bounds = new Rectangle(primary.X, primary.Y, primary.Width, primary.Height - 1);
                await Check("one visible desktop pixel prevents pause", false);
                Screen? secondary = Screen.AllScreens.FirstOrDefault(screen => !screen.Primary);
                if (secondary != null)
                {
                    cover.Bounds = secondary.Bounds;
                    await Check("full-screen on secondary does not pause primary", false);
                }
                else Results.Add("SKIP: no secondary display attached");
                cover.Bounds = primary;
                await Check("final full-screen", true);
                cover.Hide();
                await Check("final resume", false);
                var lastResume = File.ReadAllLines(args[1]).Last(line => line.Contains("suspended=False"));
                await Task.Delay(5500);
                string finalMetric = File.ReadAllLines(args[1]).Last();
                Require(finalMetric != lastResume && finalMetric.Contains("FPS="), "rendered frames continue after resume");
                Results.Add("PASS: resumed render loop produces frames: " + finalMetric);
            }
            catch (Exception ex)
            {
                Results.Add(ex.ToString());
                _exitCode = 1;
            }
            finally
            {
                File.WriteAllLines(args[2], Results);
                cover.Close();
            }
        };
        Application.Run(cover);
        foreach (string result in Results) Console.WriteLine(result);
        return _exitCode;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
