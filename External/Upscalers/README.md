# Reconstruction SDKs

The files required to build the XeSS-SR, FSR 3.1 and DLSS/DLAA adapters are
kept in this repository at the official upstream commits listed below. A normal
checkout does not need a separate SDK download. Verify the vendored files with:

```powershell
./External/Upscalers/Restore.ps1 -VerifyOnly
./Native/Reconstruction/Build.ps1
```

`Restore.ps1` can recover a missing file from its pinned official upstream
commit if needed. It checks the SHA-256 of every downloaded and existing file.

Requires PowerShell 7, Visual Studio 2026 C++ x64 tools (v145), and a Windows SDK.
Checksums cover headers, libraries, licenses and release DLLs. Existing mismatches
fail instead of silently accepting a different ABI. Vendor binaries total
approximately 160 MiB; each vendor keeps its own license.

- XeSS: https://github.com/intel/xess, `de0fb9c1c510661c571164e1418ceca8101dab69`.
- FSR: https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK, `60f4ea81909200d8542eca14dccb2628b763a9a3`. The adapter explicitly selects a 3.1 provider from the SDK, not FSR 4 or frame generation.
- DLSS: https://github.com/NVIDIA/DLSS, `374959484e79a640feaba44c93ac8cfb0a03f5b5`. Links the static x64 NGX library and distributes only the release `nvngx_dlss.dll`, never the development/watermarked DLL.

`Rendering/ReconstructionAssets.targets` builds the bridge and copies DLLs and
licenses for normal builds, MSIX content and unpackaged publishes. It never downloads
dependencies implicitly. `SkipReconstructionNativeBuild=true` can reuse an already
built bridge. `EnableVendorReconstruction=false` builds the spatial-AA-only variant;
test that variant from a clean output directory to avoid stale optional DLLs.

The C ABI borrows the host device, command list and textures. It does not own a
swap chain, audio endpoint, UI thread or presentation loop. Runtime initialization
failures disable that provider for the current host and fall back to FXAA without
rewriting saved preferences. Reopening the host retries device capabilities.

## Distribution

Each vendored SDK retains its own license. The application's MIT license does not
relicense vendor binaries. Copies of the SDK licenses and third-party notices are
placed in `Licenses/`. The About view identifies the graphics components.

The DLSS SDK license limits distribution of SDK materials and requires the
application publisher to satisfy its distribution terms. Review `dlss/LICENSE.txt`
before pushing these files to a public remote or distributing a build. Keeping
the files in this working tree does not itself complete those requirements.

Before distributing a DLSS-enabled release, the publisher must complete the
applicable NVIDIA attribution/trademark review, end-user license and commercial
release notification requirements in `dlss/LICENSE.txt`. This change performs no
notification, trademark approval, public upload or release on the publisher's behalf.
These are release checks, not a claim that SDK licensing has already been completed.
