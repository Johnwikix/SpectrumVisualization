# AnimatedTextBlock rendering core

Copied from the user-provided local `music_player/External/AnimatedWin2dControls/AnimatedWin2dControls/Controls/AnimatedTextBlock` on 2026-09-27.

Includes the eight text effects, DirectWrite glyph shaping, cluster diffing and their supporting types. The XAML control, document model and independent rendering loop are not needed: `Effects/AnimatedTrackText.cs` hosts these effects in Aurora's existing render loop, and renders the animated glyphs into the same layer used for shadows.

Local adaptations: nullable annotations disabled for the imported sources; the sole ZLinq emptiness check uses `IList.Count`, avoiding an extra package dependency. The source directory contains no license file; no new license is asserted here.
