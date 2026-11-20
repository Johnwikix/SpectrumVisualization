# Machi scene assets

The three scene geometries, framing points, sign artwork and analytic light fixtures come from Folia Major [847bb51dd80316b4d94461d9d838db50d31da2c8](https://github.com/chthollyphile/folia-major/tree/847bb51dd80316b4d94461d9d838db50d31da2c8/src/components/visualizer/machi). The offline export also preserves each set's lighting cones, steam sources, roof boundaries, drainage, wind and light positions for the atmosphere pass. The upstream AGPL-3.0 license is in [../LICENSE](../LICENSE). Geometry selection uses the same fixed seed 20261119 for all three assets. Live text and actor timings use Spectrum's song metadata and glyph atlas.

The convenience store incorporates props from the original RainyCorner scene in the companion DXRDemo workspace, revision `7e3b814778133ebf86876e6a281062e0908b41cb`, source `Samples/RainyCorner/RainyCorner.blend`. The selector verifies SHA-256 `c5855948c62434784deb3c7bf817a6ade98f81815093b79b00b0862ef4f4e01b`. It exports the useful silhouette and surface details from the source meshes, fits them against the upstream prop positions and applies the upstream palette. Folia supplies the container structures, product layout and animated moving parts.

| Donor parts | Source triangles selected | Exported triangles per source prop | Placed instances |
| --- | ---: | ---: | ---: |
| Vending machine | 124,346 | 5,998 | 2 |
| Shelf | 864 | 640 | 2 |
| Refrigerator rack | 108 | 80 | 4 |
| Refrigerator handle | 108 | 80 | 5 |
| Counter and small fixtures | 3,160 | 1,428 | 1 |
| Air conditioning housing and grille | 18,048 | 1,680 | 4 |
| Bicycle | 76,808 | 12,804 | 1 |
| Street lamp | 604 | 328 | 1 |

The equivalent convenience store export with upstream props alone contains 82,154 triangles. The donor version contains 88,222. The highly repeated original hardware details were culled or decimated in Blender. Closed panes use 16 mm thickness for the former web glass planes. The RainyCorner vending glass already has thickness.

| Scene | Indexed vertices | Triangles | Static sign atlas | Remaining analytic lights | Actors | Brotli bytes |
| --- | ---: | ---: | --- | ---: | ---: | ---: |
| Convenience store | 90,360 | 88,222 | 2048 × 957 | 0 | 10 | 726,652 |
| Gas station | 30,541 | 57,032 | 2048 × 1368 | 2 | 4 | 412,292 |
| Kowloon | 53,659 | 76,226 | 2048 × 764 | 5 | 1 | 775,277 |

Ceiling strips, pole lamps and the Kowloon stall bulb emit from their actual mesh surfaces. The exporter integrates the matched Folia point or spot light over its solid angle and distributes its flux across the emitting area. A one-sided Lambertian fixture with area A and flux Φ has radiance Φ/(πA). The convenience store street lamp preserves the Blender spectrum and matches the luminance-weighted flux of the Folia spotlight. The vending LEDs keep their Blender linear emission, approximately (34.56, 41.76, 48). Indirect paths through the storefront supply the window spill. These are scene-relative light levels. Untextured HDR emitters store three independent linear binary16 channels in the UV slots of material 11, alongside an independent sRGB base color.

Printed textures supply diffuse color with zero emission. The motel arrow bulbs use material 12. Its first UV slot at vertex byte offset 18 stores the integer phase 0–3, and the sRGB color bytes store the upstream street-light palette. Both ray hits and sampled lights use the same scene time to switch between the upstream levels 0.18 and 1.9. The emitter distribution uses their time average of 1.04.

## Regeneration

Use the pinned Folia Major checkout with its dependencies installed, Blender 5.2.4 LTS, Node.js 26 and a Chromium executable supported by Playwright. Run these commands from the Spectrum repository root.

```powershell
& <blender.exe> --background --disable-autoexec <RainyCorner.blend> --python Build/FoliaMachi/extract-rainy-corner.py -- .tools/rainy-corner-props.json
node Build/FoliaMachi/sync.mjs <folia-major-checkout> .tools/rainy-corner-props.json [chromium-executable]
```

The exporter uses headless Chromium for the upstream Canvas 2D signage. Its page is fulfilled locally and the context is offline. Microsoft YaHei is the reference typeface for the fixed sign canvas. The app consumes the Brotli assets directly. It uses the prepared Spectrum font for the live karaoke board, ticker, title, artist, Kowloon hologram and station price-board progress display. The station's vacancy sign stores both original Canvas faces in its static atlas and selects the lit NO during a chorus.

`MCH3` stores a little-endian header, framing points, actor ranges and pivots, point and spot light parameters, packed vertices, indices and RGBA8 sign pixels. Positions use metres with Y up. The packed geometry stores sRGB colors, four-bit roughness and four-bit metalness. RainyCorner keeps the source roughness and metalness. Folia's painted and toon surfaces map their authored sheen roles to the native PBR surface values. Actor ranges follow the static triangles and their transforms operate on baked world coordinates. The Kowloon hologram uses an identity transform in its own range, so only that mesh needs shader-side opacity tests. The fixed texture and runtime lyric panel have independent UV regions. The exporter also generates `FoliaMachi.WeatherData.cs` with upstream roof, drip, glint and wind layout.
