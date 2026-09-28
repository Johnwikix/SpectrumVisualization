# SMAA 1x

Unmodified reference shader and lookup assets from https://github.com/iryoku/smaa,
commit `71c806a838bdd7d517df19192a20f0c61b3ca29d`. See LICENSE.txt.

The host uses HIGH search/diagonal/corner settings, color-edge detection, and an
HDR-relative threshold. Legacy BGR24 AreaTexDX10.dds is decoded to RG8; SearchTex.dds
is the packed 64x16 R8 lookup. No runtime file reads are needed: these assets are
embedded into the managed/native-AOT assembly.
