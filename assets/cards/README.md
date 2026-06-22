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

The game has **8 deckable elements** — Fire, Water, Earth, Wind, Forest, Electric,
Light, Dark (Divine is reserved for future Ace/Boss/God cards). **8 creatures per
element.** Spells & traps are element-neutral. **Structures are NOT deck cards** —
the commander builds them from an RTS menu — but they still load art by slug.
Cards with no art file fall back to a built-in element-tinted placeholder.

**Fire creatures (炎)**
`sparkimp` · `flicker` · `cinderling` · `scorchling` · `ashfang` · `pyrewing` · `infernox` · `magmaw`

**Water creatures (水)**
`mistling` · `brinekin` · `rippler` · `undertow` · `tidecaller` · `surgeling` · `maelstrom` · `leviath`

**Earth creatures (地)**
`pebbling` · `gravelkin` · `mosshide` · `loamhide` · `cragtooth` · `bouldroot` · `monolith` · `titanore`

**Wind creatures (風)**
`gustling` · `breezeling` · `zephyr` · `galeling` · `skirl` · `talonwind` · `cyclone` · `tempest`

**Forest creatures (森)**
`sapling` · `thornling` · `bramble` · `vinewhip` · `pouncer` · `grovekeep` · `maulhorn` · `eldertree`

**Electric creatures (雷)**
`spark` · `jolt` · `volt` · `crackle` · `surge` · `thunderhead` · `stormcall` · `galvanwyrm`

**Light creatures (光)**
`dawnmote` · `glimmer` · `gleamward` · `radiant` · `lumenfang` · `aegisol` · `solstice` · `seraphine`

**Dark creatures (闇)**
`wraithling` · `shadeling` · `gravelurk` · `grimfang` · `nightstalker` · `dreadmaw` · `maledict` · `voidwyrm`

(each is `<name>_cardart.png`, e.g. `infernox_cardart.png`.)

**Structures** (built from the commander's menu — not in the deck). Generic:
`thefoundry`… → slug `foundry`, plus `longhouse`, `bulwark`. Per-element mana forges:
`emberforge` · `tidewell` · `stonewell` · `galewell` · `thornwell` · `stormforge` · `dawnwell` · `gloomwell`.
Grand forges use `grand<forge>` (e.g. `grandemberforge`).

**Workers** (the spawned minion token): `worker_cardart.png`

**Spells & traps** (neutral): `emberbolt` · `frostlance` · `cavein` · `dissolve` · `snarepit` · `whirltrap` · `cindervolley` · `searingbrand` · `topplethespire` · `collapsingfloor`

**Command Centers** — 8 solo (slug = element: `fire`, `water`, `earth`, `wind`,
`forest`, `electric`, `light`, `dark`) + 28 dual "Compacts" (slug = the two element
names joined, e.g. `firewater`, `forestelectric`, `electricdark`).

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
