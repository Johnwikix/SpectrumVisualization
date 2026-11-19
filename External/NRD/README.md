# NVIDIA Real-Time Denoisers SDK

Spectrum uses NVIDIA-RTX/NRD v4.17.3 at commit
`792eff196afdd350fd9c3f862119017ccb438a0e`. The vendored runtime embeds the
official Shader Model 6.0 DXIL denoisers. The build enables signed normal
encoding (`NRD_NORMAL_ENCODING=4`) and linear roughness
(`NRD_ROUGHNESS_ENCODING=1`).

町景 uses RELAX_DIFFUSE_SPECULAR_SH for primary material-demodulated diffuse
and specular lighting. Reflection and transmission each run RELAX_DIFFUSE
with virtual surfaces and their own histories. The two optical denoisers
share transient textures and pipeline objects. The native D3D12 bridge
executes the complete SDK dispatch sequence on the caller's queue. Two
descriptor and constant-buffer regions keep in-flight frames independent.

The SDK runs on DXR adapters across vendors. On a compatible RTX adapter,
the optional DLSS Ray Reconstruction mode bypasses NRD and reconstructs at
the original resolution. Spectrum falls back to NRD if Ray Reconstruction
is unsupported or fails. Both paths composite the current frame's stable
emissive contributions after reconstruction.

Run `External/NRD/Verify.ps1` to verify the pinned runtime, headers, shader
packing helpers and licenses. Normal vendor-enabled builds verify the files
and compile the native bridge. They package `NRD.dll` beside
`Spectrum.Reconstruction.dll` with the SDK notices under `Licenses/`.

To rebuild the SDK, run `External/NRD/Rebuild.ps1` using PowerShell 7, the
Visual Studio C++/CMake toolchain and Windows SDK. `-DxcPath` overrides the
compiler from the pinned Microsoft.Direct3D.DXC 1.9.2609.5 package. The
script verifies the pinned source archives for NRD, ShaderMake and MathLib,
keeps temporary material in `cache/`, and refreshes the binary checksums.
