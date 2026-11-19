# RTXPT shader dependencies

Source: https://github.com/NVIDIA-RTX/RTXPT

Pinned revision: `f08d1c739071e0faad0c7c274d861124c511abab` (RTXPT 1.8.1).

This software contains source code provided by NVIDIA Corporation.
The original copyright notices are retained. See `Licenses/RTXPT.txt` in the
application output (source: `DXRDemo/Licenses/RTXPT.txt`). The Intel MIT notice
in `NoiseAndSequences.hlsli` is also retained.

| Local file | Upstream path under `Rtxpt/Shaders/PathTracer` | Integration change |
| --- | --- | --- |
| `Microfacet.hlsli` | `Rendering/Materials/Microfacet.hlsli` | Replace Donut configuration dependency with `K_PI` |
| `BxDFConfig.hlsli` | `Rendering/Materials/BxDFConfig.hlsli` | Unmodified |
| `NoiseAndSequences.hlsli` | `Utils/NoiseAndSequences.hlsli` | Remove unused `Utils.hlsli` dependency |

The Machi path tracer uses these sampling and microfacet routines in
`Effects/Folia/Shaders/MachiTrace.hlsl`. Spectrum supplies the scene buffers,
emitter alias tables, light selection, glass interfaces, optical guides
and D3D12 dispatch.
