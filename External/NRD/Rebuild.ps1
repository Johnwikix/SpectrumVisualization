param([string]$DxcPath)
$ErrorActionPreference = 'Stop'
if (-not $DxcPath) { $DxcPath = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.direct3d.dxc/1.9.2609.5/build/native/bin/x64/dxc.exe' }
if (-not (Test-Path -LiteralPath $DxcPath)) { throw 'Restore Microsoft.Direct3D.DXC or pass -DxcPath with a matching DXC executable.' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$cmake = & $vswhere -latest -products '*' -find 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe' | Select-Object -First 1
if (-not $cmake) { throw 'Install the Visual Studio C++ CMake tools.' }
$cache = Join-Path $PSScriptRoot 'cache'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
$archives = @(
    @{ Name='NRD'; Folder='NRD-792eff196afdd350fd9c3f862119017ccb438a0e'; Url='https://codeload.github.com/NVIDIA-RTX/NRD/zip/792eff196afdd350fd9c3f862119017ccb438a0e'; Sha='AD148D3653E7E4A149AF0D1608EC662EEB522144CF34F6A29F9DFD333933BAA8' },
    @{ Name='ShaderMake'; Folder='ShaderMake-18f5a344e7ca8fa65daaf079d07bc8ce38453e05'; Url='https://github.com/NVIDIA-RTX/ShaderMake/archive/18f5a344e7ca8fa65daaf079d07bc8ce38453e05.zip'; Sha='35DE2547E28CF10F18A0E2782CF154A3D1610212D00D829FA8E73A18210623B6' },
    @{ Name='MathLib'; Folder='MathLib-11'; Url='https://github.com/NVIDIA-RTX/MathLib/archive/refs/tags/v11.zip'; Sha='D3A592E0DF176DA3B19088F553FF1CD7711211C00594BA1C1E6512F66BB315BD' }
)
foreach ($archive in $archives) {
    $zip = Join-Path $cache ($archive.Name + '.zip')
    if (-not (Test-Path -LiteralPath $zip)) { Invoke-WebRequest -Uri $archive.Url -OutFile $zip }
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $archive.Sha) { throw "Source archive checksum mismatch: $($archive.Name)" }
    Expand-Archive -LiteralPath $zip -DestinationPath $cache -Force
}
$source = Join-Path $cache $archives[0].Folder
$shaderMake = Join-Path $cache $archives[1].Folder
$mathLib = Join-Path $cache $archives[2].Folder
$build = Join-Path $cache 'build'
& $cmake -S $source -B $build -G 'Visual Studio 18 2026' -A x64 -T v145 `
    -DNRD_NORMAL_ENCODING=4 -DNRD_ROUGHNESS_ENCODING=1 -DNRD_NRI=OFF `
    -DNRD_EMBEDS_DXBC_SHADERS=OFF -DNRD_EMBEDS_SPIRV_SHADERS=OFF -DNRD_EMBEDS_DXIL_SHADERS=ON `
    -DSHADERMAKE_FIND_COMPILERS=OFF "-DSHADERMAKE_DXC_PATH=$DxcPath" -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded `
    "-DFETCHCONTENT_SOURCE_DIR_SHADERMAKE=$shaderMake" "-DFETCHCONTENT_SOURCE_DIR_MATHLIB=$mathLib" -DFETCHCONTENT_FULLY_DISCONNECTED=ON
if ($LASTEXITCODE -ne 0) { throw 'NRD CMake configure failed.' }
& $cmake --build $build --config Release --target NRD --parallel 8
if ($LASTEXITCODE -ne 0) { throw 'NRD build failed.' }
foreach ($folder in @('Include','Shaders','Runtime')) { New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot $folder) | Out-Null }
foreach ($name in @('NRD.h','NRDDescs.h','NRDSettings.h')) { Copy-Item -LiteralPath (Join-Path $source "Include/$name") -Destination (Join-Path $PSScriptRoot 'Include') }
foreach ($name in @('NRD.hlsli','NRDConfig.hlsli')) { Copy-Item -LiteralPath (Join-Path $source "Shaders/$name") -Destination (Join-Path $PSScriptRoot 'Shaders') }
Copy-Item -LiteralPath (Join-Path $source '_Bin/Release/NRD.dll') -Destination (Join-Path $PSScriptRoot 'Runtime')
Copy-Item -LiteralPath (Join-Path $source 'LICENSE.txt') -Destination $PSScriptRoot
Copy-Item -LiteralPath (Join-Path $mathLib 'LICENSE.txt') -Destination (Join-Path $PSScriptRoot 'MathLib-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $shaderMake 'LICENSE.txt') -Destination (Join-Path $PSScriptRoot 'ShaderMake-LICENSE.txt')
# Rebuilding intentionally refreshes the locally produced DLL's checksum; upstream archives remain pinned.
$hashes = [ordered]@{}
@('Include/NRD.h','Include/NRDDescs.h','Include/NRDSettings.h','Shaders/NRD.hlsli','Shaders/NRDConfig.hlsli','Runtime/NRD.dll','LICENSE.txt','MathLib-LICENSE.txt','ShaderMake-LICENSE.txt') | ForEach-Object {
    $hashes[$_] = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_) -Algorithm SHA256).Hash
}
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'checksums.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'Verify.ps1')
