# Folia renderer provenance

The Folia effect ports visual algorithms from chthollyphile/folia-major
(https://github.com/chthollyphile/folia-major), licensed under GNU AGPL
version 3 (see LICENSE). Reference checkout:
`G:/SoftwareProject/web/folia-major`, version 0.7.20.

Sonnet MG geometry additionally follows the user-designated checkout
`D:/code/node/folia-major`, version 0.7.21, commit 26c57f9b (2026-10-19).
`sonnet-graphics.js` contains its 100 main geometries, eight HUD and fixed-geometry
variants, six decorative layouts and original stagger recipes. Regenerate with
`node Build/FoliaSonnet/sync.mjs <folia-major-checkout>` on Node 24 or newer.
The recording adapter flattens curves and clips hatching during cancellable CPU
preparation; only immutable native plans reach the render loop. Circle start
angles are deterministic. Spectrum uses a native vector flower fallback because
its cover-derived themes do not supply Folia theme SVG icon lists.

The same bundle now includes the original segment-fixed artwork and deterministic
open-frame specifications. The four open-frame growth recipes, short-line render
hints, text-based chorus fallback, and Tempera paragraph transition formulas are
adapted to preallocated native frame state. Classic's cold adapter reuses the
vendored parser-preserving semantic/sticky layout helpers. Lumiere prepares its
maximum mote population once; live tuning selects the required prefix.

The optional realtime motion effects use Spectrum's existing live band transients.
They do not run Beat This! or htdemucs, infer bar positions, or claim isolated drums.

Paper shader sources in PaperShaders are from @paper-design/shaders, distributed
with the reference checkout. Their license is reproduced in Paper-LICENSE.txt.
The generated HLSL preserves the original shader formulas; the D3D12 host,
DirectWrite text atlas, SMTC integration and QQ lyric adapter are Spectrum code.

Lyricify.Lyrics.Helper is copied from the user-designated music_player/External
directory, including its source-generated JSON and cancellation adaptations.
Its Apache 2.0 license is preserved alongside its sources.

The embedded native-layout.js contains the bundled original pure
Sonnet, Tempera, Diorama, Fume, Tilt, Pendolo, Cadenza, Partita and Lumiere
planners, plus the original population-weighted cover palette. It executes only at song, font
or viewport changes through Jint 4.17.0 (BSD-2-Clause), using Acornima 1.7.0
(BSD-3-Clause). Per-frame playback uses immutable C# plans and D3D12 shaders.
There is no browser or WebView renderer. Lyric content is passed as JSON data;
CLR reflection interop is not exposed to scripts.

The embedded themes.js preserves Folia's original non-AI cover palette analysis,
harmonious light/dark theme generator and keyword range matching. Regenerate it
with `node Build/FoliaThemes/sync.mjs <folia-major checkout> [upstream revision]`.
Spectrum seeds the original random source per song for repeatable preparation;
the user can regenerate the pair, select the original midnight/daylight presets,
or edit independent custom light/dark colors and manual keyword rules. No AI
theme service or emotion inference is included. Pendolo and Tempera receive the
selected theme in their cold planner input. Sonnet preserves keyword glow targets,
and native Diorama keyword adaptation follows dioramaKeywordColor.ts, including
linear/sRGB conversion and the original displayed-contrast correction.

Pendolo optionally hands its original Float32Array directly to a native cold
callback, avoiding a large numeric JSON round trip. Its complete JSON path remains
available for reference comparisons. The reusable clockwork mesh cache is keyed
only by the viewport and colors the original builder actually reads.

Earcut (ISC, Mapbox) triangulates the original Tempera polygons, including holes,
when plans are prepared. The original Cappella emoji PNGs and the 100 Lumiere
light/line-art profiles are preserved from the reference checkout. Relevant
license texts accompany this file.

Text uses original DirectWrite Bezier outlines encoded as half-float MTSDF through
msdfgen v1.13 (MIT), commit 1874bcf7d9624ccc85b4bc9a85d78116f690f35b.
Only the native cold conversion bridge invokes it; steady-state shaders reconstruct
coverage using screen derivatives without re-rasterizing or resizing bitmap text.
