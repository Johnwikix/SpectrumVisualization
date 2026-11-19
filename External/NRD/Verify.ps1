$ErrorActionPreference = 'Stop'
$hashes = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'checksums.json') -Raw | ConvertFrom-Json
foreach ($entry in $hashes.PSObject.Properties) {
    $file = Join-Path $PSScriptRoot $entry.Name
    if (-not (Test-Path -LiteralPath $file)) { throw "NRD file missing: $($entry.Name). Restore vendored files or run External/NRD/Rebuild.ps1." }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.Value) { throw "NRD checksum mismatch: $($entry.Name)" }
}
