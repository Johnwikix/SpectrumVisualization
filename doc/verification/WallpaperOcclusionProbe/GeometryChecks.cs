using WinExSpectrumTest.Helper;
using Rect = WinExSpectrumTest.Helper.WallpaperOcclusionDetector.WindowRect;

/// <summary>Headless regression cases using the production coverage policy and physical-pixel coordinates.</summary>
internal static class GeometryChecks
{
    public static int Run()
    {
        Rect screen = Bounds(0, 0, 2560, 1440);
        Rect bottomTaskbar = Bounds(0, 0, 2560, 1392);
        int count = 0;

        void Check(string name, Rect window, bool maximizedOnSameMonitor, bool expected,
            Rect? wallpaper = null, Rect? monitor = null, Rect? work = null)
        {
            Rect target = WallpaperOcclusionDetector.GetCoverageBounds(wallpaper ?? screen, monitor ?? screen,
                work ?? bottomTaskbar, maximizedOnSameMonitor);
            bool actual = WallpaperOcclusionDetector.Covers(window, target);
            if (actual != expected)
                throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
            count++;
            Console.WriteLine($"PASS: {name}");
        }

        Check("maximized window above bottom taskbar pauses", bottomTaskbar, true, true);
        Check("ordinary work-area-sized window retains full-screen policy", bottomTaskbar, false, false);
        Check("restoring to partial size resumes", Bounds(200, 100, 1800, 1100), false, false);
        Check("full-screen window pauses", screen, false, true);
        Check("spanning full-screen window pauses", Bounds(-100, -100, 2660, 1540), false, true);
        Check("ordinary window with one desktop pixel exposed keeps rendering",
            Bounds(0, 0, 2560, 1439), false, false);
        Check("maximized window with one work-area pixel exposed keeps rendering",
            Bounds(0, 0, 2559, 1392), true, false);
        Check("stale maximized bounds during restore keep rendering", Bounds(200, 100, 1800, 1100), true, false);

        Rect[] workAreas =
        [
            Bounds(0, 48, 2560, 1440), Bounds(64, 0, 2560, 1440), Bounds(0, 0, 2496, 1440),
            screen, Bounds(0, 0, 2560, 1438)
        ];
        string[] labels = ["top taskbar", "left taskbar", "right taskbar", "hidden taskbar", "auto-hide reserved strip"];
        for (int i = 0; i < workAreas.Length; i++)
            Check($"maximized window with {labels[i]}", workAreas[i], true, true, work: workAreas[i]);

        Check("maximized window on secondary monitor does not pause",
            Bounds(-1920, 0, 0, 1032), false, false);
        Check("secondary-monitor identity cannot use primary work area", bottomTaskbar, false, false);
        Rect negativeMonitor = Bounds(-2560, -200, 0, 1240);
        Rect negativeWork = Bounds(-2560, -200, 0, 1192);
        Check("negative virtual-screen coordinates", negativeWork, true, true,
            negativeMonitor, negativeMonitor, negativeWork);
        Rect highDpiMonitor = Bounds(0, 0, 3840, 2160);
        Rect highDpiWork = Bounds(0, 0, 3840, 2088);
        Check("high DPI uses physical-pixel work area", highDpiWork, true, true,
            highDpiMonitor, highDpiMonitor, highDpiWork);

        Check("monitor query failure keeps work-area-only window rendering", bottomTaskbar, true, false,
            monitor: default(Rect), work: default(Rect));
        Check("monitor query failure still recognizes full-screen", screen, true, true,
            monitor: default(Rect), work: default(Rect));
        Check("empty work area does not pause", bottomTaskbar, true, false, work: default(Rect));
        Check("inverted work area does not pause", bottomTaskbar, true, false, work: Bounds(100, 0, 0, 1392));
        Check("outdated work area outside monitor does not pause", bottomTaskbar, true, false,
            work: Bounds(-1920, 0, 0, 1032));
        Check("wallpaper spanning monitors cannot use one work area", bottomTaskbar, true, false,
            wallpaper: Bounds(0, 0, 4480, 1440));
        Check("empty wallpaper cannot be covered", screen, true, false, wallpaper: default(Rect));
        Check("empty window cannot cover wallpaper", default, true, false);
        Check("simple window region covering only work area is enough", bottomTaskbar, true, true);
        Check("simple window region leaving a work-area gap is rejected",
            Bounds(1, 0, 2560, 1392), true, false);

        Console.WriteLine($"PASS: {count} headless geometry checks; no windows or audio capture started.");
        return 0;
    }

    private static Rect Bounds(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };
}
