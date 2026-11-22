# Machi scene assets

The central sets, framing points, sign artwork and original light fixtures come from Folia Major [847bb51dd80316b4d94461d9d838db50d31da2c8](https://github.com/chthollyphile/folia-major/tree/847bb51dd80316b4d94461d9d838db50d31da2c8/src/components/visualizer/machi). The offline export preserves the central roof boundaries, drainage and wind. The upstream AGPL-3.0 license is in [../LICENSE](../LICENSE). Geometry selection uses the fixed seed 20261119. Live text and actor timings use Spectrum's song metadata and glyph atlas.

Spectrum's Blender sources in `Build/FoliaMachi` add the surrounding streets and buildings, their light fixtures, the central service equipment and the replacement vehicles. The service station contains two taxis, a parked sedan, a passing car and the advertising truck. The exporter also supports the upstream kei pickup variant. Kowloon has two scooters. The moving vehicles use the original lane orientations, with longer routes across the adjoining streets.

The convenience store incorporates props from the original RainyCorner scene in the companion DXRDemo workspace, revision `7e3b814778133ebf86876e6a281062e0908b41cb`, source `Samples/RainyCorner/RainyCorner.blend`. The selector verifies SHA-256 `c5855948c62434784deb3c7bf817a6ade98f81815093b79b00b0862ef4f4e01b`. It exports the useful silhouette and surface details from the source meshes, fits them against the upstream prop positions and applies the upstream palette. Folia supplies the container structures, product layout and animation timing. The condenser housing locates the rebuilt fan shroud, rotating blades and guard.

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

The donor extraction culls or decimates repeated hardware details in Blender. Closed panes use 16 mm thickness for the former web glass planes. The RainyCorner vending glass already has thickness.

| Scene | Indexed vertices | Triangles | Sign and surface atlas | Analytic lights | Actors | Brotli bytes |
| --- | ---: | ---: | --- | ---: | ---: | ---: |
| Convenience store | 377,555 | 271,376 | 2048 × 1215 | 0 | 10 | 3,400,832 |
| Gas station | 481,999 | 359,620 | 2048 × 1626 | 2 | 4 | 4,240,422 |
| Kowloon | 469,143 | 320,116 | 2048 × 1022 | 5 | 1 | 3,820,349 |

The 600,000-triangle limit includes static geometry and all actors in each packed scene. Street luminaires, occupied windows, under-awning diffusers and vehicle lights contribute through emissive mesh triangles.

Ceiling strips, pole lamps and the Kowloon stall bulb emit from their actual mesh surfaces. The exporter integrates the matched Folia point or spot light over its solid angle and distributes its flux across the emitting area. A one-sided Lambertian fixture with area A and flux Φ has radiance Φ/(πA). The convenience store street lamp preserves the Blender spectrum and matches the luminance-weighted flux of the Folia spotlight. The vending LEDs keep their Blender linear emission, approximately (34.56, 41.76, 48). Indirect paths through the storefront supply the window spill. These are scene-relative light levels. Untextured HDR emitters store three independent linear binary16 channels in the UV slots of material 11, alongside an independent sRGB base color.

The convenience store pairs coral and cyan awning strips with mint door lights and amber sidelights. The gas station uses ice-blue canopy and column lights, amber at the pumps and rose-pink motel neon. Indigo and ruby lights flank the Kowloon bar, and a golden downlight illuminates the stall counter. Each scene's paint colors and static sign colors are baked during export. The live lyrics have a separate glyph atlas.

Selected neighboring shops carry those colors beyond the main facades. The convenience store's neighbors have glazed shopfronts and enamel awnings. The motel and roadside workshops have colored fascias and reception windows. In Kowloon, lit signs distinguish the ground-floor shops from the apartment windows above.

The Kowloon handcart keeps the original wooden body and handles. Its through axle connects both hubs to hangers beneath the bed. The exporter seats both tires and the rear parking foot on the finished alley pavement.

Printed textures supply diffuse color with zero emission. The motel arrow bulbs use material 12. Its first UV slot at vertex byte offset 18 stores the integer phase 0–3, and the sRGB color bytes store the upstream street-light palette. Both ray hits and sampled lights use the same scene time to switch between the upstream levels 0.18 and 1.9. The emitter distribution uses their time average of 1.04.

## Regeneration

Use the pinned Folia Major checkout with its dependencies installed, Blender 5.2.4 LTS, Node.js 26 and a Chromium executable supported by Playwright. Run these commands from the Spectrum repository root.

```powershell
& <blender.exe> --background --disable-autoexec <RainyCorner.blend> --python Build/FoliaMachi/extract-rainy-corner.py -- .tools/rainy-corner-props.json
& <blender.exe> --background --factory-startup --python-exit-code 1 --python Build/FoliaMachi/author.py -- .tools/machi-models
node Build/FoliaMachi/sync.mjs <folia-major-checkout> .tools/rainy-corner-props.json .tools/machi-models [chromium-executable]
```

The exporter uses headless Chromium for the upstream Canvas 2D signage. Its page is fulfilled locally and the context is offline. Microsoft YaHei is the reference typeface for the fixed sign canvas. The app consumes the Brotli assets directly. It uses the prepared Spectrum font for the live karaoke board, ticker, title, artist, Kowloon hologram and station price-board progress display. The station's vacancy sign stores both original Canvas faces in its static atlas and selects the lit NO during a chorus.

`MCH4` stores a little-endian header, framing points, actor ranges and pivots, analytic light parameters, packed vertices, indices and an RGBA8 atlas. Positions use metres with Y up. The packed geometry stores sRGB colors, four-bit roughness and four-bit metalness. Material 13 adds a dielectric clearcoat over the vehicle pigment layer. The final 258 atlas rows hold filtered, repeating asphalt, paving, masonry, concrete and standing-seam tiles. Their channels encode reflectance variation, two relief slopes and roughness variation. The DXR shader samples the tiles in world space. Dynamic text has separate UV coordinates.

Actor ranges follow the static triangles and operate on baked world coordinates. The Kowloon hologram has a separate opacity-tested range. The exporter generates `FoliaMachi.WeatherData.cs` for precipitation. `FoliaMachi.Environment.cs` defines steam volumes fitted to the baked vents, manholes and cookware. The lamp shades and diffusers cast light through the path tracer.
