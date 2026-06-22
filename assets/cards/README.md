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

The game now has **7 elements** — Fire, Water, Earth, Wind, Light, Dark, Divine.
Spells & traps are element-neutral. Cards without an art file fall back to a
built-in element-tinted placeholder, so everything works while you add art.

**Fire creatures (炎)**
`sparkimp_cardart.png` · `cinderling_cardart.png` · `ashfang_cardart.png` · `pyrewing_cardart.png` · `magmaw_cardart.png`

**Water creatures (水)**
`mistling_cardart.png` · `rippler_cardart.png` · `tidecaller_cardart.png` · `surgeling_cardart.png` · `leviath_cardart.png`

**Earth creatures (地)**
`pebbling_cardart.png` · `mosshide_cardart.png` · `cragtooth_cardart.png` · `bouldroot_cardart.png` · `titanore_cardart.png`

**Wind creatures (風)**
`gustling_cardart.png` · `zephyr_cardart.png` · `skirl_cardart.png` · `talonwind_cardart.png` · `tempest_cardart.png`

**Light creatures (光)**
`dawnmote_cardart.png` · `gleamward_cardart.png` · `lumenfang_cardart.png` · `aegisol_cardart.png` · `seraphine_cardart.png`

**Dark creatures (闇)**
`wraithling_cardart.png` · `gravelurk_cardart.png` · `nightstalker_cardart.png` · `dreadmaw_cardart.png` · `voidwyrm_cardart.png`

**Divine creatures (神)**
`cherub_cardart.png` · `seraphling_cardart.png` · `valkar_cardart.png` · `archon_cardart.png` · `empyrean_cardart.png`

**Structures** (one mana-forge per element + the shared Longhouse)
`emberforge_cardart.png` · `tidewell_cardart.png` · `stonewell_cardart.png` · `galewell_cardart.png` · `dawnwell_cardart.png` · `gloomwell_cardart.png` · `empyreum_cardart.png` · `longhouse_cardart.png`

**Workers** (the spawned minion token)
`worker_cardart.png`

**Spells & traps** (neutral)
`emberbolt_cardart.png` · `frostlance_cardart.png` · `cavein_cardart.png` · `dissolve_cardart.png` · `snarepit_cardart.png` · `whirltrap_cardart.png`

**Command Centers — solo** (your base / the win-loss structure; named by element)
`fire_cardart.png` · `water_cardart.png` · `earth_cardart.png` · `wind_cardart.png` · `light_cardart.png` · `dark_cardart.png` · `divine_cardart.png`

**Command Centers — dual Compacts** (21, one per element pair). The slug is the two
element names joined, lowercase, no separators — i.e. `<a><b>_cardart.png`:
`firewater` · `fireearth` · `firewind` · `firelight` · `firedark` · `firedivine` ·
`waterearth` · `waterwind` · `waterlight` · `waterdark` · `waterdivine` ·
`earthwind` · `earthlight` · `earthdark` · `earthdivine` ·
`windlight` · `winddark` · `winddivine` · `lightdark` · `lightdivine` · `darkdivine`
(e.g. `firewater_cardart.png`).

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
