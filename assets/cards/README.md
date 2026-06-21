# Card Art

Drop your card images in this folder. They load automatically — **no code
changes for any card, ever**. Each card looks for a file named after the card,
and if it isn't here yet the game falls back to the original built-in
placeholder drawing, so it always looks complete while you add art one card at a
time.

## The naming rule

A card loads:

```
assets/cards/<slug>_cardart.<ext>
```

- **`<slug>`** = the card name, lowercased, with spaces and punctuation removed
  and a leading "The " dropped. So **Magmaw → `magmaw`**, **Snare Pit →
  `snarepit`**, **The Tide Spire → `tidespire`**.
- **`<ext>`** = `png`, `jpg`, `jpeg`, or `webp` (tried in that order).

Just name the file to match and refresh — nothing in the code needs editing,
including brand-new cards you add later.

## Image specs

- **Shape:** square, 1:1. The card frame crops to a square ("cover"), so a square
  source shows with no cropping.
- **Size:** 512×512 minimum; 1024×1024 for crisp results. (Bigger is fine.)
- **Format:** `.png`, `.jpg`/`.jpeg`, or `.webp`. PNG transparency is fine but the
  in-game frame has its own background, so filling the square is recommended.
- **Composition:** in-game, the card overlays its **name + cost across the top**
  and **stats across the bottom**. Keep faces and focal points in the **centre
  band**. See `_TEMPLATE-512.png` in this folder for the exact safe area.

## Filenames (your art checklist)

Name each file exactly as below (shown with `.png`; any supported extension
works) and drop it here.

**Ember creatures**
`sparkimp_cardart.png` · `cinderling_cardart.png` · `ashfang_cardart.png` · `pyrewing_cardart.png` · `magmaw_cardart.png`

**Tide creatures**
`mistling_cardart.png` · `rippler_cardart.png` · `tidecaller_cardart.png` · `surgeling_cardart.png` · `leviath_cardart.png`

**Structures**
`emberforge_cardart.png` · `tidewell_cardart.png` · `longhouse_cardart.png`

**Workers** (the spawned minion token)
`worker_cardart.png`

**Ember spells**
`emberbolt_cardart.png` · `cavein_cardart.png` · `snarepit_cardart.png`

**Tide spells**
`frostlance_cardart.png` · `dissolve_cardart.png` · `whirltrap_cardart.png`

**Command Centers** (your base / the win-loss structure)
`emberbastion_cardart.png` · `tidespire_cardart.png` · `thornwallcompact_cardart.png`

That's **22 images** total (Longhouse appears in both colors but uses one file).

## Portable build

To bake whatever art is in this folder into the single-file portable build
(`spawn-row-duel-v26.portable.html`), run from the project root:

```
python3 tools/embed-art.py
```

It scans for every `<slug>_cardart.*` file and embeds it, so the portable file
shows your art with no server. Re-run it whenever you add or change art.

**Note:** to see your images in the editable `spawn-row-duel-v26.html`, serve the
project over a local server or GitHub Pages (see the top-level README). Opening
the raw `.html` over `file://` can block these image files from loading,
especially on phones — the game still runs, but cards fall back to their
built-in placeholders.
