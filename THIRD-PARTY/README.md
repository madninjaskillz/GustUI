# Third-party assets embedded in GustUI

Fonts are embedded as base64 in `src/GustUI/_Embedded/UUContent.cs`. Their licences live here,
alongside a note of what was changed — several are modified for delivery, and the licences require
that to be stated.

## Comfortaa (Bold, Light) — SIL Open Font License 1.1

Copyright 2011 The Comfortaa Project Authors (https://github.com/alexeiva/comfortaa),
with Reserved Font Name "Comfortaa". Licence text: `Comfortaa-OFL.txt`.

Used for the ezmuze studio wordmark. Two files ship, `Comfortaa-Bold.ttf` and
`Comfortaa-Light.ttf`, both derived from the upstream **variable** font:

1. instanced to a single static weight (`wght` 700 and 300) with `fontTools.varLib.instancer`,
   because the SDF baker rasterises a font's default instance and has no notion of a variation
   axis — shipping the variable file would have given Regular twice;
2. subset to Basic Latin (U+0020–U+007F) with `fontTools.subset`, which is exactly the range
   `FontManager.IconRangesFor` bakes for a non-symbol font. 197 KB → **20 KB each**.

Neither step changes a glyph outline. Subsetting and instancing for delivery are the ordinary
web-font pipeline and are not treated as creating a Modified Version under the OFL's Reserved Font
Name clause (see the OFL FAQ) — the outlines, metrics and family name are upstream's.

The OFL requires the licence and copyright notice to travel with the font, which is what this
directory is for. It does NOT require attribution in the application UI, so unlike the CC-BY
sample content in ezmuze-studio (see its `docs/soundfont-module.md`) there is no user-visible
credit to maintain.
