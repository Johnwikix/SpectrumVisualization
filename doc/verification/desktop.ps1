Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type 'using System; using System.Runtime.InteropServices; public class DesktopTest { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a,int x,int y,int w,int t,uint f); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT r); public struct RECT {public int L,T,R,B;} }'
[DesktopTest]::SetProcessDPIAware() | Out-Null
function Capture-Spectrum([string]$Name) {
 $p=Get-Process Spectrum
 $r=New-Object DesktopTest+RECT
 [DesktopTest]::GetWindowRect((Get-MainHandle),[ref]$r) | Out-Null
 $img=New-Object System.Drawing.Bitmap ($r.R-$r.L),($r.B-$r.T)
 $g=[System.Drawing.Graphics]::FromImage($img)
 $g.CopyFromScreen($r.L,$r.T,0,0,$img.Size)
 $img.Save("G:/SoftwareProject/winui/spectrum/doc/verification/$Name.png")
 $g.Dispose(); $img.Dispose()
}
function Get-SpectrumUI {
 $p=Get-Process Spectrum
 [System.Windows.Automation.AutomationElement]::FromHandle((Get-MainHandle))
}

function Get-MainHandle {
 $w=[System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'WinExSpectrumTest')))
 [IntPtr]$w.Current.NativeWindowHandle
}
