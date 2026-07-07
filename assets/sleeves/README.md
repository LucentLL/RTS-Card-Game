# Card sleeves & frames (optional image skins)

Drop images with these EXACT filenames into this folder and reload the page —
the game probes for them at load (missing files are silently ignored, you'll
only ever see the browser's own 404 line in the network tab) and switches from
the built-in procedural designs to your art automatically.

## Card back (sleeve)

| File | Where it shows |
|---|---|
| `cardback.png` (or `cardback.webp`) | every face-down surface: opponent hand backs, both castle-wall deck piles, and face-down set cards / traps on the board |

- Recommended size: **430 x 600** px (portrait). It is drawn with `cover`
  cropping on some narrow surfaces (the peeking hand backs), so keep the
  important detail centered and away from the edges.
- Until this file exists, a procedural sleeve renders instead: double border,
  diagonal weave, and a centered ❖ emblem, tinted by each player's element.

## Card frames

| File | Where it shows |
|---|---|
| `frame_fire.png` … `frame_divine.png` | hand cards and on-board cards of that element |
| `frame_neutral.png` | element-agnostic cards: structures, spells, and worker tokens |

Valid element names: `fire water earth wind forest electric light dark divine neutral`.

- Recommended size: **740 x 1030** px (portrait). The image is stretched
  full-bleed to the card, and the existing chrome (cost gem, name plate, type
  ribbon, stats bar, element ring) renders ON TOP of it — a transparent
  central art window is optional but looks best.
- Both **PNG and WEBP** work; `.png` is probed first, then `.webp`.

## Portable build note

`tools/embed-art.py` embeds only `assets/cards` (card art + `_fieldart`
cut-outs) and `assets/sprites` into the portable single-file build. Sleeve and
frame images are NOT embedded — they load from `assets/sleeves/` when the game
is served online (GitHub Pages or from the repo folder). The portable .html
gracefully falls back to the procedural sleeves and frames.
