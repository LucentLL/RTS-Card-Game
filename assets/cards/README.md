# Card Art

Drop your card images in this folder. They load automatically — no code changes
needed for any card already listed below. If a file isn't here yet, the game
falls back to the original built-in placeholder drawing, so it always looks
complete while you add art one card at a time.

## Image specs

- **Shape:** square, 1:1. The card frame crops to a square ("cover"), so a square
  source shows with no cropping.
- **Size:** 512×512 minimum; 1024×1024 for crisp results. (Bigger is fine.)
- **Format:** `.png` or `.jpg`. PNG transparency is fine but the in-game frame has
  its own background, so filling the square is recommended.
- **Composition:** in-game, the card overlays its **name + cost across the top**
  and **stats across the bottom**. Keep faces and focal points in the **centre
  band**. See `_TEMPLATE-512.png` in this folder for the exact safe area.

## Filenames (your art checklist)

Name each file exactly as below and drop it here.

**Ember creatures**
`sparkimp.png` · `cinderling.png` · `ashfang.png` · `pyrewing.png` · `magmaw.png`

**Tide creatures**
`mistling.png` · `rippler.png` · `tidecaller.png` · `surgeling.png` · `leviath.png`

**Structures**
`emberforge.png` · `tidewell.png` · `longhouse.png`

**Workers** (the spawned minion token)
`villager.png`

**Ember spells**
`emberbolt.png` · `cavein.png` · `snarepit.png`

**Tide spells**
`frostlance.png` · `dissolve.png` · `whirltrap.png`

**Command Centers** (your base / the win-loss structure)
`emberbastion.png` · `tidespire.png` · `thornwall.png`

That's **22 images** total (Longhouse and the worker each use one file across both
colors).

## Changing a filename or using a different format

Names are mapped in the **`CARD ART`** block near the top of
`../../spawn-row-duel-v26.html` (search the file for "CARD ART" — the
`CARD_FILES` table). To use a different filename or a `.webp`/`.jpg` for a card,
edit that one line. New cards you add later don't need an entry — their filename
is derived from the card name automatically (e.g. "Storm Drake" →
`stormdrake.png`).

**Note:** to actually see your images, serve the project over a local server or
GitHub Pages (see the top-level README). Opening the raw `.html` over `file://`
can block these image files from loading, especially on phones — the game still
runs, but cards fall back to their built-in placeholders.
