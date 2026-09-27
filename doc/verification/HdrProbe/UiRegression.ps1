param([Parameter(Mandatory)][int]$TargetProcess, [string]$LvtPath = 'G:\Tool\lvt\lvt.exe')
$ErrorActionPreference = 'Stop'
function Nodes($node) { $node; foreach ($child in $node.children) { Nodes $child } }
function Lvt { $result = & $LvtPath @args; if ($LASTEXITCODE) { throw "lvt failed: $args" }; $result | ConvertFrom-Json }
function Require($condition, $message) { if (!$condition) { throw $message }; "PASS: $message" }
function Tree($handle) { Lvt dump --hwnd $handle --uia }
function Ref($node) { "uia:$($node.properties.RuntimeId)" }
function ById($tree, $id) { Nodes $tree.root | Where-Object { $_.properties.AutomationId -eq $id } | Select-Object -First 1 }

$main = Lvt dump --pid $TargetProcess --uia
$mainHandle = $main.target.hwnd
$next = Nodes $main.root | Where-Object text -eq '下一效果' | Select-Object -First 1
Lvt click (Ref (ById $main 'OpenSettings')) --hwnd $mainHandle | Out-Null
Start-Sleep -Milliseconds 350
$desktop = & "$PSScriptRoot/../wallpaper-state.ps1" | ConvertFrom-Json
$settingsWindow = $desktop.Windows | Where-Object { $_.ProcessId -eq $TargetProcess -and $_.Title -eq 'SettingWindow' } | Select-Object -First 1
Require ($null -ne $settingsWindow) 'Settings window opened through UI'
$settingsHandle = '0x{0:X}' -f $settingsWindow.Handle
$settings = Tree $settingsHandle
$labels = @(Nodes $settings.root).text
Require ($labels -contains '极光之环' -and $labels -contains '音域回响') 'Chinese effect names resolve at runtime'
$toggle = ById $settings 'HdrEnabled'
if ($toggle.properties.'Toggle.ToggleState' -ne 'On') { Lvt toggle (Ref $toggle) --hwnd $settingsHandle | Out-Null }
$white = ById $settings 'HdrWhiteNits'
$peak = ById $settings 'HdrPeakNits'
Lvt set-value (Ref $peak) 80 --hwnd $settingsHandle | Out-Null
$settings = Tree $settingsHandle
Require ((ById $settings 'HdrWhiteNits').properties.'RangeValue.Value' -eq '80') 'Lower peak clamps white immediately'
Lvt set-value (Ref $white) 400 --hwnd $settingsHandle | Out-Null
$settings = Tree $settingsHandle
Require ((ById $settings 'HdrPeakNits').properties.'RangeValue.Value' -eq '400') 'Higher white raises peak immediately'
Lvt set-value (Ref $white) 200 --hwnd $settingsHandle | Out-Null
Lvt set-value (Ref $peak) 1000 --hwnd $settingsHandle | Out-Null
if (@(Nodes $settings.root).text -match '极光之环使用 SDR') { Lvt click (Ref $next) --hwnd $mainHandle | Out-Null }
Start-Sleep -Milliseconds 1800
$settings = Tree $settingsHandle
Require (@(Nodes $settings.root).text -contains 'HDR10 已启用') 'Native app actually selects HDR10 on this display'
for ($i = 0; $i -lt 10; $i++) { Lvt click (Ref $next) --hwnd $mainHandle | Out-Null }
Start-Sleep -Milliseconds 700
$settings = Tree $settingsHandle
Require (@(Nodes $settings.root).text -contains 'HDR10 已启用') 'Ten rapid effect switches preserve final Sonic intent'
Lvt toggle (Ref $toggle) --hwnd $settingsHandle | Out-Null
Start-Sleep -Milliseconds 500
$settings = Tree $settingsHandle
Require (@(Nodes $settings.root).text -contains 'SDR（HDR 已关闭）') 'HDR off changes the actual output to SDR'
Lvt toggle (Ref $toggle) --hwnd $settingsHandle | Out-Null
Start-Sleep -Milliseconds 500
Lvt screenshot --hwnd $settingsHandle --output "$PSScriptRoot/bin/native-settings.png" | Out-Null
Lvt close --hwnd $settingsHandle | Out-Null
Lvt maximize --hwnd $mainHandle | Out-Null
Start-Sleep -Milliseconds 700
Lvt screenshot --hwnd $mainHandle --output "$PSScriptRoot/bin/native-scene.png" | Out-Null
Lvt restore --hwnd $mainHandle | Out-Null
Lvt click (Ref $next) --hwnd $mainHandle | Out-Null
Start-Sleep -Milliseconds 400
Lvt screenshot --hwnd $mainHandle --output "$PSScriptRoot/bin/native-aurora.png" | Out-Null
Lvt click (Ref $next) --hwnd $mainHandle | Out-Null
Require $true 'Native resize, Aurora restoration and Sonic reactivation completed'
