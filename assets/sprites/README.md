# Board Sprites (floating standee figures)

Drop a sprite here and it **hovers isometrically above that creature's card** on
the battlefield — the Duel Links "monster figure on the field" look. Like the
card art, sprites load automatically by name; no code changes, ever.

## The naming rule

A creature loads its sprite from:

```
assets/sprites/<slug>_sprite.<ext>
```

- **`<slug>`** = the card name, lowercased, spaces/punctuation removed, leading
  "The " dropped. So **Magmaw → `magmaw_sprite.png`**, **Blue Dragon →
  `bluedragon_sprite.png`**. (Same slug rule as `assets/cards/`.)
- **`<ext>`** = `png`, `webp`, or `jpg` (tried in that order).

## Fallback (so it works right now)

If a creature has **no** `_sprite` file yet, the figure falls back to that card's
own art (`assets/cards/<slug>_cardart.*`, then the built-in placeholder). So the
floating-figure effect is visible immediately and your dedicated sprite simply
overrides it when you add one.

## Image specs

- **Transparent PNG cut-outs look best** — just the creature, no background, so it
  reads as a standee rather than a floating tile.
- **Shape:** taller-than-wide framing works well (the figure stands up out of the
  card). The art is anchored at the **bottom** (its "feet" sit on the card) and
  scaled to the board automatically.
- **Size:** ~512 wide × ~768 tall is plenty; bigger is fine.

## Toggle

In a duel, the **🧍 Figures** button toggles sprites on/off.

## Portable build

`python3 tools/embed-art.py` bakes both `assets/cards/` **and** `assets/sprites/`
into `spawn-row-duel-v26.portable.html`. Re-run it whenever you add or change art.
