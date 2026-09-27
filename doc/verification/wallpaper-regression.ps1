param([string]$LvtPath = 'G:\Tool\lvt\lvt.exe')
$ErrorActionPreference = 'Stop'
function Assert($Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
    "PASS: $Message"
}
function Get-State {
    $desktop = & "$PSScriptRoot/wallpaper-state.ps1" | ConvertFrom-Json
    $window = $desktop.Windows | Where-Object Title -eq 'WinExSpectrumTest' | Select-Object -First 1
    if (!$window) { throw 'Start one Spectrum instance before running this test.' }
    $window
}
function Read-Nodes($Node) { $Node; foreach ($child in $Node.children) { Read-Nodes $child } }
function Invoke-Button([string]$Id) {
    $window = Get-State
    $handle = '0x{0:X}' -f $window.Handle
    $tree = & $LvtPath dump --hwnd $handle --uia --format json | ConvertFrom-Json
    if ($LASTEXITCODE) { throw 'Cannot inspect Spectrum.' }
    $button = Read-Nodes $tree.root | Where-Object { $_.type -eq 'Button' -and $_.properties.AutomationId -eq $Id } | Select-Object -First 1
    if (!$button) { throw "Button missing: $Id" }
    & $LvtPath invoke "uia:$($button.properties.RuntimeId)" --hwnd $handle | Out-Null
    if ($LASTEXITCODE) { throw "Cannot invoke $Id" }
    Start-Sleep -Milliseconds 250
}
function Tray([string]$Action) {
    & "$PSScriptRoot/wallpaper-tray.ps1" -Action $Action -LvtPath $LvtPath | Out-Null
    Start-Sleep -Milliseconds 300
}
function Assert-Wallpaper {
    $desktop = & "$PSScriptRoot/wallpaper-state.ps1" | ConvertFrom-Json
    $window = $desktop.Windows | Where-Object Title -eq 'WinExSpectrumTest' | Select-Object -First 1
    $primary = $desktop.PrimaryMonitor
    $expected = @($primary.L, $primary.T, ($primary.R - $primary.L), ($primary.B - $primary.T)) -join ','
    Assert ($window.Child -and $window.ParentClass -in @('Progman','WorkerW')) 'Wallpaper is an Explorer child'
    Assert (($window.Bounds -join ',') -eq $expected) 'Wallpaper covers exactly the primary monitor'
    Assert (($window.ClientBounds -join ',') -eq $expected) 'Wallpaper client area has no system frame inset'
    Assert (($window.ContentBounds -join ',') -eq $expected) 'XAML content covers the screen without a top border gap'
    Assert (!$window.Topmost -and $window.ClickThrough -and $window.NoActivate -and $window.ToolWindow) 'Wallpaper does not activate, intercept clicks or stay topmost'
    if ($window.ParentClass -eq 'Progman') {
        Assert ($window.PreviousSiblingClass -eq 'SHELLDLL_DefView') 'Wallpaper stays immediately below desktop icons'
    }
}
$initial = Get-State
if ($initial.Child) { Tray Toggle; $initial = Get-State }
if ($initial.Topmost) { Tray Unlock; $initial = Get-State }
$bounds = $initial.Bounds -join ','
Tray Toggle
Assert-Wallpaper
Tray LeftClick
Assert-Wallpaper
Tray Cycle
Tray Cycle
Tray Toggle
$restored = Get-State
Assert (!$restored.Child -and $restored.Parent -eq 0 -and !$restored.ClickThrough) 'Disabling restores a normal interactive window'
Assert (($restored.Bounds -join ',') -eq $bounds) 'Disabling restores original window position and size'

Invoke-Button LockBtn
$locked = Get-State
Assert $locked.Topmost 'Widget starts locked'
Tray Toggle
Assert-Wallpaper
Tray Toggle
$restored = Get-State
Assert (!$restored.Child -and $restored.Topmost -and $restored.ClickThrough) 'Disabling restores locked widget behavior'
Assert (($restored.Bounds -join ',') -eq ($locked.Bounds -join ',')) 'Locked widget bounds restored'
Tray Unlock

Invoke-Button FullScreenBtn
$fullScreen = Get-State
Tray Toggle
Assert-Wallpaper
Tray Toggle
$restored = Get-State
Assert (!$restored.Child -and ($restored.Bounds -join ',') -eq ($fullScreen.Bounds -join ',')) 'Disabling restores full screen'
Invoke-Button FullScreenBtn
Assert (!(Get-State).Child) 'Full screen can be exited after wallpaper mode'

foreach ($mode in @('maximize','minimize')) {
    $handle = '0x{0:X}' -f (Get-State).Handle
    & $LvtPath $mode --hwnd $handle | Out-Null
    if ($LASTEXITCODE) { throw "Cannot $mode widget" }
    Start-Sleep -Milliseconds 250
    Tray Toggle
    Assert-Wallpaper
    Tray Toggle
    $restored = Get-State
    $expectedState = if ($mode -eq 'maximize') { $restored.Maximized } else { $restored.Minimized }
    Assert (!$restored.Child -and $expectedState) "Disabling restores $mode state"
    & $LvtPath restore --hwnd $handle | Out-Null
}
