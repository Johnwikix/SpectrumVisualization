param([long]$WindowHandle = 0)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class WallpaperProbe {
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumProc callback, IntPtr p);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder b, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder b, int n);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtrW(IntPtr h, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO info);
    public struct POINT { public int X,Y; }
    public struct RECT { public int L,T,R,B; }
    public struct MONITORINFO { public int Size; public RECT Monitor,Work; public uint Flags; }
    public static string Class(IntPtr h) { var b=new StringBuilder(256); GetClassName(h,b,256); return b.ToString(); }
    public static string Title(IntPtr h) { var b=new StringBuilder(256); GetWindowText(h,b,256); return b.ToString(); }
}
'@
[WallpaperProbe]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
function Read-Window([IntPtr]$Handle) {
    $rect = New-Object WallpaperProbe+RECT
    [WallpaperProbe]::GetWindowRect($Handle, [ref]$rect) | Out-Null
    $client = New-Object WallpaperProbe+RECT
    $clientOrigin = New-Object WallpaperProbe+POINT
    [WallpaperProbe]::GetClientRect($Handle, [ref]$client) | Out-Null
    [WallpaperProbe]::ClientToScreen($Handle, [ref]$clientOrigin) | Out-Null
    $contentBounds = $null
    $contentWindow = [WallpaperProbe]::FindWindowEx($Handle, [IntPtr]::Zero, 'Microsoft.UI.Content.DesktopChildSiteBridge', $null)
    if ($contentWindow -ne [IntPtr]::Zero) {
        $content = New-Object WallpaperProbe+RECT
        [WallpaperProbe]::GetWindowRect($contentWindow, [ref]$content) | Out-Null
        $contentBounds = @($content.L, $content.T, ($content.R - $content.L), ($content.B - $content.T))
    }
    $parent = [WallpaperProbe]::GetParent($Handle)
    $style = [WallpaperProbe]::GetWindowLongPtrW($Handle, -16).ToInt64()
    $exStyle = [WallpaperProbe]::GetWindowLongPtrW($Handle, -20).ToInt64()
    [uint32]$processId = 0
    [WallpaperProbe]::GetWindowThreadProcessId($Handle, [ref]$processId) | Out-Null
    [pscustomobject]@{
        Handle = $Handle.ToInt64(); ProcessId = $processId
        Visible = [WallpaperProbe]::IsWindowVisible($Handle)
        Minimized = [WallpaperProbe]::IsIconic($Handle); Maximized = [WallpaperProbe]::IsZoomed($Handle)
        Class = [WallpaperProbe]::Class($Handle); Title = [WallpaperProbe]::Title($Handle)
        Parent = $parent.ToInt64(); ParentClass = [WallpaperProbe]::Class($parent)
        PreviousSiblingClass = [WallpaperProbe]::Class([WallpaperProbe]::GetWindow($Handle, 3))
        Bounds = @($rect.L, $rect.T, ($rect.R - $rect.L), ($rect.B - $rect.T))
        ClientBounds = @($clientOrigin.X, $clientOrigin.Y, ($client.R - $client.L), ($client.B - $client.T))
        ContentBounds = $contentBounds
        Child = ($style -band 0x40000000) -ne 0
        Topmost = ($exStyle -band 8) -ne 0
        ClickThrough = ($exStyle -band 0x20) -ne 0
        NoActivate = ($exStyle -band 0x08000000) -ne 0
        ToolWindow = ($exStyle -band 0x80) -ne 0
    }
}
if ($WindowHandle) { Read-Window ([IntPtr]$WindowHandle) | ConvertTo-Json; return }
$windows = [System.Collections.Generic.List[object]]::new()
$spectrumProcesses = @(Get-Process Spectrum -ErrorAction SilentlyContinue).Id
$childCallback = [WallpaperProbe+EnumProc]{ param($h, $p)
    if ([WallpaperProbe]::Title($h) -eq 'WinExSpectrumTest') { $windows.Add((Read-Window $h)) }
    return $true
}
[WallpaperProbe]::EnumWindows([WallpaperProbe+EnumProc]{ param($h, $p)
    $class = [WallpaperProbe]::Class($h)
    [uint32]$windowProcessId = 0
    [WallpaperProbe]::GetWindowThreadProcessId($h, [ref]$windowProcessId) | Out-Null
    if ($class -in @('Progman','WorkerW','Shell_TrayWnd','TopLevelWindowForOverflowXamlIsland') -or $windowProcessId -in $spectrumProcesses) {
        $windows.Add((Read-Window $h))
        [WallpaperProbe]::EnumChildWindows($h, $childCallback, [IntPtr]::Zero) | Out-Null
    }
    return $true
}, [IntPtr]::Zero) | Out-Null
$monitor = New-Object WallpaperProbe+MONITORINFO
$monitor.Size = [System.Runtime.InteropServices.Marshal]::SizeOf($monitor)
[WallpaperProbe]::GetMonitorInfo([WallpaperProbe]::MonitorFromPoint((New-Object WallpaperProbe+POINT),1), [ref]$monitor) | Out-Null
[pscustomobject]@{ PrimaryMonitor = $monitor.Monitor; Windows = $windows } | ConvertTo-Json -Depth 5
