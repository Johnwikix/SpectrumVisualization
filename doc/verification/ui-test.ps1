. "$PSScriptRoot/desktop.ps1"
$ErrorActionPreference='Stop'
$scope=[System.Windows.Automation.TreeScope]::Descendants
function Find-Name($root,$name,$type) {
 $all=$root.FindAll($scope,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$name)))
 $all | Where-Object {$_.Current.ControlType -eq $type} | Select-Object -First 1
}
function Invoke-Button($b){([System.Windows.Automation.InvokePattern]$b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()}
function Select-Item($b){([System.Windows.Automation.SelectionItemPattern]$b.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()}
function Settings-Root {
 [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'SettingWindow')))
}
$w=Settings-Root
$combo=$w.FindFirst($scope,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ComboBox)))
([System.Windows.Automation.ExpandCollapsePattern]$combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
Select-Item (Find-Name $combo 'Aurora Ring' ([System.Windows.Automation.ControlType]::ListItem))
Start-Sleep -Milliseconds 250
$config=Get-Content "$PSScriptRoot/../../bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/appsettings.json" | ConvertFrom-Json
if($config.VisualEffect -ne 'aurora-ring'){throw 'Settings effect selection did not reach the renderer/storage'}
'PASS: settings selection activates and saves Aurora Ring'
Select-Item (Find-Name $w 'Aurora Ring' ([System.Windows.Automation.ControlType]::ListItem))
Start-Sleep -Milliseconds 250
$toggle=Find-Name $w '封面随音频律动' ([System.Windows.Automation.ControlType]::Button)
if(!$toggle){$toggle=$w.FindAll($scope,[System.Windows.Automation.Condition]::TrueCondition) | Where-Object {$_.GetSupportedPatterns().ProgrammaticName -contains 'TogglePatternIdentifiers.Pattern'} | Select-Object -First 1}
$pattern=[System.Windows.Automation.TogglePattern]$toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
if($pattern.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On){$pattern.Toggle()}
([System.Windows.Automation.WindowPattern]$w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close()
Start-Sleep -Milliseconds 250
$config=Get-Content "$PSScriptRoot/../../bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/appsettings.json" | ConvertFrom-Json
if($config.CoverPulseEnabled){throw 'CoverPulseEnabled=false not persisted'}
'PASS: cover pulse off saved'
Invoke-Button (Find-Name (Get-SpectrumUI) '设置' ([System.Windows.Automation.ControlType]::Button))
Start-Sleep -Milliseconds 300
$w=Settings-Root
Select-Item (Find-Name $w 'Aurora Ring' ([System.Windows.Automation.ControlType]::ListItem))
Start-Sleep -Milliseconds 250
$toggle=$w.FindAll($scope,[System.Windows.Automation.Condition]::TrueCondition) | Where-Object {$_.GetSupportedPatterns().ProgrammaticName -contains 'TogglePatternIdentifiers.Pattern'} | Select-Object -First 1
$pattern=[System.Windows.Automation.TogglePattern]$toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
if($pattern.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::Off){throw 'Cover toggle did not retain state'}
'PASS: reopening settings retains cover pulse off'
$pattern.Toggle()
Select-Item (Find-Name $w 'Sonic Topography' ([System.Windows.Automation.ControlType]::ListItem))
Start-Sleep -Milliseconds 250
$w.FindAll($scope,[System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name+' | '+$_.Current.ControlType.ProgrammaticName }
