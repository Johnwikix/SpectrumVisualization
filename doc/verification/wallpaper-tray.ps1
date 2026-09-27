param(
    [ValidateSet('Menu','Toggle','Settings','Cycle','Unlock','Exit','LeftClick')][string]$Action = 'Menu',
    [string]$LvtPath = 'G:\Tool\lvt\lvt.exe',
    [int]$MenuAttempt = 0
)
$ErrorActionPreference = 'Stop'
function Read-Nodes($Node) {
    $Node
    foreach ($child in $Node.children) { Read-Nodes $child }
}
function Invoke-Lvt {
    $result = & $LvtPath @args
    if ($LASTEXITCODE -ne 0) { throw "lvt failed: $args" }
    $result | ConvertFrom-Json
}
$desktop = & "$PSScriptRoot/wallpaper-state.ps1" | ConvertFrom-Json
$trayHandle = '0x{0:X}' -f ($desktop.Windows | Where-Object Class -eq 'Shell_TrayWnd' | Select-Object -First 1).Handle
$tray = Invoke-Lvt dump --hwnd $trayHandle --uia --format json
$icon = Read-Nodes $tray.root | Where-Object { $_.type -eq 'Button' -and $_.text.Trim() -eq 'Spectrum' } | Select-Object -First 1
if (!$icon) {
    $chevron = Read-Nodes $tray.root | Where-Object { $_.text -in @('显示隐藏的图标','Show hidden icons') } | Select-Object -First 1
    if ($chevron) {
        $overflow = $desktop.Windows | Where-Object { $_.Class -eq 'TopLevelWindowForOverflowXamlIsland' -and $_.Visible } | Select-Object -First 1
        if (!$overflow) {
            Invoke-Lvt press-key Space --focus-first "uia:$($chevron.properties.RuntimeId)" --hwnd $trayHandle | Out-Null
        }
        Start-Sleep -Milliseconds 200
        $desktop = & "$PSScriptRoot/wallpaper-state.ps1" | ConvertFrom-Json
        $overflow = $desktop.Windows | Where-Object { $_.Class -eq 'TopLevelWindowForOverflowXamlIsland' -and $_.Visible } | Select-Object -First 1
        if ($overflow) {
            $trayHandle = '0x{0:X}' -f $overflow.Handle
            $tray = Invoke-Lvt dump --hwnd $trayHandle --uia --format json
            $icon = Read-Nodes $tray.root | Where-Object { $_.type -eq 'Button' -and $_.text.Trim() -eq 'Spectrum' } | Select-Object -First 1
        }
    }
}
if (!$icon) { throw 'Spectrum tray icon not visible.' }
if ($Action -eq 'LeftClick') {
    Invoke-Lvt click "uia:$($icon.properties.RuntimeId)" --hwnd $trayHandle
    return
}
# Keyboard context-menu activation also works while a full-screen window covers
# the physical taskbar. This is still the user's system-tray entry point.
Invoke-Lvt press-key 'Shift+F10' --focus-first "uia:$($icon.properties.RuntimeId)" --hwnd $trayHandle | Out-Null
Start-Sleep -Milliseconds 200
$desktop = & "$PSScriptRoot/wallpaper-state.ps1" | ConvertFrom-Json
$menuHost = $desktop.Windows | Where-Object { $_.Title -eq 'WinUI Desktop' -and $_.Visible } | Select-Object -First 1
if (!$menuHost) { throw 'Spectrum menu host not visible.' }
$menu = Invoke-Lvt dump --hwnd ('0x{0:X}' -f $menuHost.Handle) --uia --format json
if ($menu.target.processName -ne 'Spectrum.exe') { throw 'Popup does not belong to Spectrum.' }
$items = @(Read-Nodes $menu.root | Where-Object type -eq 'MenuItem')
# The first cold UIA activation can race the tray library's initial flyout layout.
# Reopen through the same user entry point, with a bounded retry.
if ($items.Count -eq 0 -and $MenuAttempt -lt 2) {
    Start-Sleep -Milliseconds 300
    & $PSCommandPath -Action $Action -LvtPath $LvtPath -MenuAttempt ($MenuAttempt + 1)
    return
}
if ($Action -eq 'Menu') {
    $items | ForEach-Object { [pscustomobject]@{ Text=$_.text; Toggle=$_.properties.'Toggle.ToggleState'; Enabled=$_.properties.IsEnabled } }
    return
}
$id = switch ($Action) { Toggle { 'WallpaperMode' }; Settings { 'Settings' }; Cycle { 'SwitchEffect' }; Unlock { 'Unlock' }; Exit { 'Exit' } }
$item = $items | Where-Object { $_.properties.AutomationId -eq $id } | Select-Object -First 1
if (!$item) { throw "Tray item not found: $id" }
# Target the owning window; activating the popup HWND itself dismisses the menu.
# ToggleMenuFlyoutItem's legacy action is unreliable here. lvt double-click uses
# real mouse input; the first click closes the menu after toggling the mode.
$verb = if ($Action -eq 'Toggle') { 'double-click' } else { 'click' }
Invoke-Lvt $verb "uia:$($item.properties.RuntimeId)" --hwnd $menu.target.hwnd
Start-Sleep -Milliseconds 300
