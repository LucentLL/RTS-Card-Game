# Spawn Row Duel

A 1v1 trading-card game with RTS/MOBA strategic depth — vanilla HTML/CSS/JS, no
frameworks. Contest the neutral center rows, manage workers and mana, and win by
draining the enemy stronghold's life.

**Play it:** https://lucentll.github.io/RTS-Card-Game/

## Layout

```
index.html            the game shell — DOM + ordered stylesheet/script includes
src/
  styles/             the game's CSS, split by concern (00_base … 05_overlays_screens)
  js/                 29 modules in LOAD ORDER (filename order IS the architecture):
                        01–09  data & core rules (cards, board, mana, structures)
                        10–18  screens, render, input, combat, movement, turns/AI
                        20–22  SFX/FX + presentation wrappers
                        30–31  pause-to-respond (RESP) + UI shell
                        40–44  multiplayer (WebRTC/relay, sync, intents, lobby)
                        99     bootstrap
assets/cards/         card images — the only external assets (README has art specs)
tools/
  build.py            reassemble ONE single-file build -> dist/spawn-row-duel.html
  embed-art.py        portable build with art baked in -> dist/spawn-row-duel.portable.html
  split_monolith.py   the (re-runnable) splitter that produced src/ from the old monolith
docs/                 ARCHITECTURE.md · MIGRATION.md · PUBLISHING.md (Steam / Play Store)
sw.js                 service worker: art cached once, HTML/JS always fresh
spawn-row-duel-v26.html   legacy URL — a redirect stub to index.html
```

The modules are classic scripts sharing one global scope, loaded in filename
order. Later layers (FX, RESP, MP) wrap earlier functions by reassignment, so
**never reorder the includes** — see `docs/ARCHITECTURE.md` before adding code.

## Run it locally

```
python3 -m http.server 8000
# then open http://localhost:8000/
```

(Serving matters: browsers block sibling files over `file://`. For a
no-server file, build the portable — below.)

## Builds

```
py tools/build.py       # dist/spawn-row-duel.html — the game as ONE file (needs assets/ for art)
py tools/embed-art.py   # dist/spawn-row-duel.portable.html — art baked in, opens anywhere
```

`dist/` is gitignored — build artifacts are produced, not committed.

## Adding / swapping card art

Card art is **derived from the card name** — no table to edit, ever.

1. Make a square image (512×512+) named `<slug>_cardart.<ext>` — the slug is the
   card name lowercased with spaces/punctuation removed and a leading "The "
   dropped (**Magmaw → `magmaw_cardart.png`**). `png/jpg/webp` all work.
   Full checklist: [`assets/cards/README.md`](assets/cards/README.md).
2. Drop it in `assets/cards/` and refresh. Missing art falls back to the card's
   built-in placeholder drawing — the game always runs.

On-field standee cut-outs use the same convention with `_fieldart`.

## Toward release

See [`docs/PUBLISHING.md`](docs/PUBLISHING.md): Tauri shell for desktop/Steam,
Capacitor for Android/Play Store (the same stack as
[DriverCity](https://github.com/LucentLL/Racing-Game-2)). The staged
de-monolith plan lives in [`docs/MIGRATION.md`](docs/MIGRATION.md).

## Art ownership

All art is placeholder. No copyright infringement intended. Any art that is not
original to this project will be replaced before release.
