# Spawn Row Duel

A 1v1 trading-card game with RTS/MOBA strategic depth, built as a **single
self-contained HTML/CSS/JS file** (no frameworks). Contest the neutral center
row, manage workers and colored mana, and win by razing the enemy command center.

## Layout

```
spawn-row-duel-v26.html   the whole game — open or serve this one file
assets/
  cards/                  your card images (PNG/JPG, square) — the only external assets
    README.md             art specs + filename checklist
    _TEMPLATE-512.png     sizing / safe-area guide
```

The game is one self-contained file. Card images are the only thing it loads from
outside, and if an image can't be found (or can't load), that card shows its
built-in placeholder drawing — the game always runs.

## Run it

- **Quickest:** open `spawn-row-duel-v26.html` in any browser. The game runs fully.
- **To load your custom card images** (and to play on a phone), **serve the
  folder** rather than opening the raw file — browsers block sibling image files
  over `file://`, especially on mobile:

  ```
  python3 -m http.server 8000
  # then open http://localhost:8000/spawn-row-duel-v26.html
  ```

  Or push to GitHub and enable **GitHub Pages** (Settings → Pages → deploy from
  `main`). That gives you a URL that works on any device with your art loading.

### Portable single-file build (opens anywhere, incl. phones)

To get a build you can just open on a phone or share — with your art baked in,
no server required — run:

```
python3 tools/embed-art.py
```

That reads the images in `assets/cards/` and writes
`spawn-row-duel-v26.portable.html`, a fully self-contained file. Re-run it
whenever you add or change art. The editable source (`spawn-row-duel-v26.html`)
is left untouched, so your drop-a-file / swap-by-name workflow stays intact.

## Adding / swapping card art

1. Make a square image (512×512+), name it to match the checklist in
   [`assets/cards/README.md`](assets/cards/README.md), drop it in `assets/cards/`.
2. Refresh. To swap art later, replace the file with the **same name**.

The card-name → filename mapping lives in a clearly-marked **`CARD ART`** block
near the top of `spawn-row-duel-v26.html` (search for "CARD ART"). You only edit
it to change a filename or add a brand-new card; existing cards already work.

## Toward release (decisions to make)

- **Steam:** wrap the web build in a desktop shell (Electron or a lightweight
  web-wrapper).
- **Play Store:** package via Capacitor or ship a Trusted Web Activity (needs an
  app shell, icons, store assets).
- **Hosting/entry name:** GitHub Pages and most wrappers expect `index.html` at
  the root. The versioned name (`spawn-row-duel-vNN.html`) is kept; when ready,
  rename the live build to `index.html` or add a tiny `index.html` that redirects.

## Art ownership

All art is placeholder.  No copyright infringement intended.  Any art that is not
original to this project will be replaced in final release.
