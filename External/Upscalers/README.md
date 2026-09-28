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

`ReconstructionCreateV2` accepts the host's exact input dimensions, derived from
the common 1–100% render scale. Vendor quality enums are internal initialization
hints, not a second resolution setting. Queries choose the closest quality hint;
their recommended min/max ranges do not veto custom fixed input sizes. A scale
change recreates the context. DLSS creation and evaluation both receive the actual
input dimensions, and XeSS execution receives them directly. In particular,
DLSS Ultra Performance's min=max recommendation must not reject 33% or 40% inputs.
The current SDKs execute 16x9 inputs in the offscreen regression probe; this tests
execution and finite, nonempty output, not perceptual quality. SDK errors still
trigger FXAA fallback. Return code `-30` denotes an invalid resolution configuration
(including DLSS outputs below 32x32) without disabling the provider; dimensions
changes retry initialization. See `doc/verification/reconstruction.md` for evidence.

The DLSS adapter explicitly sets J/K/L/M (default K) in all NGX quality preset
slots. This is the application's requested preset; driver overrides can still
affect model selection. SDK initialization and resource replacement run only on
the render thread after outstanding GPU work completes. Unlimited frame rate
removes application timer pacing, not these resource-lifetime synchronization
requirements. No frame generation is included.

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
