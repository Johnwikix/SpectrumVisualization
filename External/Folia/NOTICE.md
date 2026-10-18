# Folia renderer provenance

The Folia effect ports visual algorithms from chthollyphile/folia-major
(https://github.com/chthollyphile/folia-major), licensed under GNU AGPL
version 3 (see LICENSE). Reference checkout:
`G:/SoftwareProject/web/folia-major`, version 0.7.20.

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

Earcut (ISC, Mapbox) triangulates the original Tempera polygons, including holes,
when plans are prepared. The original Cappella emoji PNGs and the 100 Lumiere
light/line-art profiles are preserved from the reference checkout. Relevant
license texts accompany this file.

Text uses original DirectWrite Bezier outlines encoded as half-float MTSDF through
msdfgen v1.13 (MIT), commit 1874bcf7d9624ccc85b4bc9a85d78116f690f35b.
Only the native cold conversion bridge invokes it; steady-state shaders reconstruct
coverage using screen derivatives without re-rasterizing or resizing bitmap text.
