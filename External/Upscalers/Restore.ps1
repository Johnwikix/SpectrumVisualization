param([switch]$HeadersOnly, [switch]$VerifyOnly)
$ErrorActionPreference = 'Stop'
$hashes = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'checksums.json') -Raw | ConvertFrom-Json -AsHashtable
function Confirm-Hash([string]$Path, [string]$Expected) {
    if (-not $Expected -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ine $Expected) {
        throw "SDK checksum mismatch: $Path. Restore the pinned upstream file before building."
    }
}
# Fixed upstream commits keep SDK headers and redistributables on the same ABI.
$packages = @(
    @{ Name = 'xess'; Repo = 'intel/xess'; Commit = 'de0fb9c1c510661c571164e1418ceca8101dab69'; Files = @(
        'inc/xess/xess.h', 'inc/xess/xess_d3d12.h', 'LICENSE.txt', 'third-party-programs.txt', 'bin/libxess.dll') },
    @{ Name = 'fsr'; Repo = 'GPUOpen-LibrariesAndSDKs/FidelityFX-SDK'; Commit = '60f4ea81909200d8542eca14dccb2628b763a9a3'; Files = @(
        'Kits/FidelityFX/api/include/ffx_api.h', 'Kits/FidelityFX/api/include/ffx_api_types.h',
        'Kits/FidelityFX/api/include/ffx_api_loader.h', 'Kits/FidelityFX/api/include/dx12/ffx_api_dx12.h',
        'Kits/FidelityFX/upscalers/include/ffx_upscale.h', 'Kits/FidelityFX/docs/license.md',
        'Kits/FidelityFX/signedbin/amd_fidelityfx_loader_dx12.dll', 'Kits/FidelityFX/signedbin/amd_fidelityfx_upscaler_dx12.dll') },
    @{ Name = 'dlss'; Repo = 'NVIDIA/DLSS'; Commit = '374959484e79a640feaba44c93ac8cfb0a03f5b5'; Files = @(
        'include/nvsdk_ngx.h', 'include/nvsdk_ngx_defs.h', 'include/nvsdk_ngx_params.h',
        'include/nvsdk_ngx_helpers.h', 'include/nvsdk_ngx_helpers_d3d.h', 'include/nvsdk_ngx_helpers_cuda.h', 'LICENSE.txt',
        'lib/Windows_x86_64/x64/nvsdk_ngx_s.lib', 'lib/Windows_x86_64/rel/nvngx_dlss.dll') }
)
foreach ($package in $packages) {
    foreach ($file in $package.Files) {
        if ($HeadersOnly -and $file -match '\.(dll|lib)$') { continue }
        $destination = Join-Path $PSScriptRoot "$($package.Name)/$file"
        $expected = $hashes["$($package.Name)/$file"]
        if (Test-Path -LiteralPath $destination) { Confirm-Hash $destination $expected; continue }
        if ($VerifyOnly) { throw "Missing SDK file: $destination. Run External/Upscalers/Restore.ps1." }
        New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
        $url = "https://raw.githubusercontent.com/$($package.Repo)/$($package.Commit)/$file"
        Write-Host "Restoring $($package.Name)/$file"
        Invoke-WebRequest -Uri $url -OutFile "$destination.download"
        Confirm-Hash "$destination.download" $expected
        Move-Item -LiteralPath "$destination.download" -Destination $destination
    }
}
