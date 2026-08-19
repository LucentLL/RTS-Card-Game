# 06 — Card Registry, Keyword Engine, Spells/Traps, Leaders, and Art Resolution

**Subsystem:** "cards" — the card database and the keyword/effect engine.
**Source of truth:** the JavaScript in `src/js/`. There is no other written spec. Everything below was
extracted by reading the files in full and chasing every cross-file reference.
**Companion machine-readable export:** `docs/unity/spec/cards.json`, produced by
`tools/export_cards.mjs` (a *dynamic* export — it evaluates the real registry scripts; see §12).

> **Reading the citations.** `03_cards_creatures.js:5` means `src/js/03_cards_creatures.js`, line 5.
> All line numbers are from the working tree at commit `8b90375`.

---

## 0. Executive orientation — what "a card" actually is here

This codebase does **not** have one card type. It has **five distinct template shapes** and **six
distinct runtime instance shapes**, and they are converted into one another by hand-written object
literals scattered across nine files. Field sets differ at every hop, and fields are silently dropped
at some hops (this is the single biggest source of port bugs — see §5.7).

| Layer | What it is | Where built |
|---|---|---|
| **Template** | The immutable card definition in the registry | `03_cards_creatures.js` (creatures, spells, structures), `04_cards_leaders.js` (commanders) |
| **Registry entry** | `{key,type,color,nm,tpl}` — the deck-building index | `06_mana_workers.js:38` (`CARD_REG`) |
| **Deck entry** | `{type,color,...template}` — a shuffled draw pile item | `06_mana_workers.js:26` (`deckOf`), `:81` (`expandDeck`) |
| **Hand card** | `{kind:'handcard', id, ...}` | `11_deck_builder.js:250` (`drawCard`) |
| **Board instance** | `{kind:'creature'\|'building', id, owner, mutable stats}` | `06_mana_workers.js:90` (`mkCre`), `:94` (`mkBld`) |
| **Face-down** | `{kind:'charge'}` (creature or structure) / `{kind:'trap'}` | `13_input.js:224`, `:233` |
| **Grave record** | A flattened, lossy summary | `07_structures.js:67` (`toGrave`) |

There are **no card IDs**. Identity is by **name string** (`nm`), and by the composite deck key
`"<color|'neutral'>|<nm>"` (`06_mana_workers.js:39`). The multiplayer layer relies on this
(`41_mp_sync.js:19` rehydrates art by looking up `CARD_BY_KEY[(rec.color||'neutral')+'|'+rec.nm]`).
**For Unity: give every card a stable string ID equal to the current `nm`, and keep a
`(elementOrNeutral, name)` composite key for save-file compatibility.**

---

## 1. Elements (attributes)

Nine elements are defined in `01_core_defs.js:15-26`. Eight are "major" (deckable / commander-able);
`divine` is reserved for Ace/Boss/God cards and is excluded from `MAJORS` (`01_core_defs.js:27`).
**Spell and Trap cards are NOT an element** — they carry `color: null` and render with a neutral
`◇` gem (`02_art.js:47`).

| id | name | glyph | color | accent | deep | bg stops | hp | wk | deckable |
|---|---|---|---|---|---|---|---|---|---|
| `fire` | Fire | 炎 | `#e0613f` | `#ff8a1f` | `#86291c` | `#5e1d10`,`#2a0f08`,`#080403` | 10000 | 2 | yes |
| `water` | Water | 水 | `#3fa3e0` | `#7fd0f5` | `#0e5a7a` | `#0f3a52`,`#0a2230`,`#03090f` | 10000 | 3 | yes |
| `earth` | Earth | 地 | `#c0863c` | `#e5b66a` | `#7a5320` | `#4a3413`,`#2a1c0a`,`#0a0704` | 10000 | 2 | yes |
| `wind` | Wind | 風 | `#76c7c0` | `#cdeeea` | `#2f726b` | `#123d3a`,`#0c2422`,`#04100f` | 10000 | 3 | yes |
| `forest` | Forest | 森 | `#4fae5e` | `#a6f0ac` | `#27692f` | `#173d1d`,`#0d250f`,`#041206` | 10000 | 2 | yes |
| `electric` | Electric | 雷 | `#f2cf3b` | `#fff7a8` | `#9a7a16` | `#3e3408`,`#241d05`,`#0a0802` | 10000 | 3 | yes |
| `light` | Light | 光 | `#ece3c0` | `#ffffff` | `#b0a45e` | `#3a3622`,`#221f12`,`#0a0905` | 10000 | 3 | yes |
| `dark` | Dark | 闇 | `#9a5cc6` | `#caa0ec` | `#56307a` | `#2e1a40`,`#1a0f26`,`#080510` | 10000 | 2 | yes |
| `divine` | Divine | 神 | `#c9d4ec` | `#ffffff` | `#5a6a96` | `#2b3450`,`#171d2e`,`#070a12` | 10000 | 2 | **no** |

`COLORS` is `MAJORS.slice()` — the canonical order used for every generated table
(dual commanders, forge lists, `CARD_REG` ordering). **Preserve this order exactly**; the
dual-commander lore strings are indexed positionally against it (`04_cards_leaders.js:19`).

`clsOf` (`01_core_defs.js:29`) maps element → CSS class `"<el>-c"`. Presentation only.

`hp` and `wk` are **commander-derived stats**, not creature stats: `hp` becomes the player's starting
life pool, `wk` becomes the base worker count that the back row is credited with
(`05_board_state.js:66`). Mana is **generic** — element does not gate cost (`06_mana_workers.js:6-7`).

---

## 2. The card definition schemas (EVERY field)

### 2.1 Creature template (`03_cards_creatures.js:5-19`, `:22`)

Creature templates are bare object literals inside eight per-element arrays. **A field that is absent
is not `null` — it is `undefined`**, and every consumer applies its own default. The authoritative
defaults are the ones in `mkCre` (`06_mana_workers.js:90-92`).

| Field | Type | Required | Default when absent | Meaning / who reads it |
|---|---|---|---|---|
| `nm` | string | **yes** | — | Card name. **This is the identity.** Drives art slug, registry key, grave grouping. |
| `c` | int | **yes** | — | Mana cost. Range in the current registry: 1..6. Also the "cost" a face-down must be funded to. |
| `a` | int | **yes** | — | Attack. Range 0..4500 (Sap Pod is 0). |
| `h` | int | **yes** | — | Hit points. Copied to both `h` and `maxh` on instantiation. Range 500..4000. |
| `up` | int | no | `0` | **Worker upkeep** — subtracted from its row's worker figure (`05_board_state.js:65`). Range 1..3. |
| `fs` | bool | no | `false` | **First Strike**. Not a keyword — a flag with its own combat tier (§6.1). |
| `kw` | string\|null | no | `null` | Element keyword id. One of `detonate,undertow,entrench,ward,reap,chrysalis,scour,overcharge`. **Single-valued.** |
| `det` | int | no | `0` | Detonate damage. |
| `reap` | int | no | `0` | Reap token attack/HP. |
| `wardhp` | int | no | `2` (**legacy — see §11.2**) | Ward (Lumen) token HP. |
| `ward` | int | no | `0` | **Vestigial.** No template sets it; nothing reads it except the copy chains. |
| `grow` | int | no | `0` | Chrysalis counters gained per upkeep (`onCreatureEnter` uses `grow\|\|1` at use site). |
| `hatch` | int | no | `0` | Chrysalis counters needed to hatch (use sites default to `3`). |
| `into` | object\|null | no | `null` | Chrysalis hatch form: `{nm,a,h}` in the registry; the transform also honours `up,fs,kw` if present (`06_mana_workers.js:149`). |
| `entrench` | bool | no | `false` | Immovable flag. Set redundantly alongside `kw:'entrench'` on all four Earth cards. **Checked as a flag, not via `kw`** (`06_mana_workers.js:137`, `14_spells_traps.js:21`). |
| `tribe` | string\|null | no | `null` | Lineage. `TRIBES = ['Human','Dragon']` (`03_cards_creatures.js:27`). **Only `'Dragon'` is used** (5 cards). |
| `subtype` | string\|null | no | `null` | Class. `SUBTYPES = ['Wizard','Warrior']` (`:28`). Used on 3 cards. |
| `token` | bool | no | `false` | Never set on a registry template; set by `mkToken` (`06_mana_workers.js:114`). |
| `art` | string (data URI) | no | `undefined` | **Placeholder** SVG only — NOT the shipping art. See §9. |
| `cnt` | int | no | `0` | Runtime-only (Chrysalis counter). Never on a template. |
| `oc` | int | no | `0` | Runtime-only (Overcharge bank). Never on a template. |

**Pool shape convention** (`03_cards_creatures.js:1-3`): each element pool is exactly **8 creatures**
with costs `1,1,2,2,3,4,5,6`; `up` scales with cost (`1,1,1,1,2,2,3,3`); **the cost-3 card is always
the First Strike card** in every one of the eight pools. This is a hard invariant in the data today
and should be preserved as a design rule.

#### Complete creature registry (64 cards, 8 per element)

| Element | Name | c | a | h | up | FS | kw | keyword data | tribe | subtype |
|---|---|---|---|---|---|---|---|---|---|---|
| fire | Sparkimp | 1 | 500 | 500 | 1 | | | | | |
| fire | Emberfly | 1 | 500 | 500 | 1 | | detonate | det 1000 | | |
| fire | Cinderling | 2 | 1000 | 1000 | 1 | | | | | |
| fire | Scorchling | 2 | 1500 | 500 | 1 | | detonate | det 1000 | | |
| fire | Ashfang | 3 | 1500 | 1000 | 2 | ✔ | | | | Warrior |
| fire | Pyrewing | 4 | 1500 | 2000 | 2 | | | | Dragon | Warrior |
| fire | Infernox | 5 | 2500 | 2000 | 3 | | detonate | det 1500 | | |
| fire | Magmaw | 6 | 3000 | 2500 | 3 | | | | Dragon | |
| water | Mistling | 1 | 500 | 1000 | 1 | | | | | |
| water | Brinekin | 1 | 500 | 1000 | 1 | | | | | |
| water | Rippler | 2 | 1000 | 1000 | 1 | | | | | |
| water | Undertow | 2 | 500 | 1500 | 1 | | undertow | | | |
| water | Tidecaller | 3 | 500 | 2000 | 2 | ✔ | | | | Wizard |
| water | Surgeling | 4 | 2000 | 1500 | 2 | | | | | |
| water | Maelstrom | 5 | 1500 | 2500 | 3 | | undertow | | | |
| water | Leviath | 6 | 2000 | 3500 | 3 | | undertow | | Dragon | |
| earth | Pebbling | 1 | 500 | 1000 | 1 | | | | | |
| earth | Gravelkin | 1 | 500 | 1000 | 1 | | | | | |
| earth | Mosshide | 2 | 500 | 1500 | 1 | | entrench | `entrench:true` | | |
| earth | Loamhide | 2 | 500 | 2000 | 1 | | | | | |
| earth | Cragtooth | 3 | 500 | 2000 | 2 | ✔ | | | | |
| earth | Bouldroot | 4 | 1000 | 2500 | 2 | | entrench | `entrench:true` | | |
| earth | Monolith | 5 | 1500 | 3000 | 3 | | entrench | `entrench:true` | | |
| earth | Titanore | 6 | 2000 | 4000 | 3 | | entrench | `entrench:true` | | |
| wind | Gustling | 1 | 1000 | 500 | 1 | | | | | |
| wind | Breezeling | 1 | 500 | 1000 | 1 | | | | | |
| wind | Zephyr | 2 | 1500 | 500 | 1 | | scour | | | |
| wind | Galeling | 2 | 1000 | 1000 | 1 | | | | | |
| wind | Skirl | 3 | 2000 | 500 | 2 | ✔ | | | | |
| wind | Talonwind | 4 | 2500 | 1000 | 2 | | scour | | | |
| wind | Cyclone | 5 | 3000 | 1500 | 3 | | scour | | | |
| wind | Tempest | 6 | 4000 | 1500 | 3 | | scour | | | |
| forest | Sapling | 1 | 500 | 1000 | 1 | | | | | |
| forest | Thornling | 1 | 1000 | 500 | 1 | | | | | |
| forest | Sap Pod | 2 | 0 | 1500 | 1 | | chrysalis | grow 1, hatch 3, into **Canopy Beast** 2500/2000 | | |
| forest | Vinewhip | 2 | 1000 | 1000 | 1 | | | | | |
| forest | Pouncer | 3 | 1500 | 1000 | 2 | ✔ | | | | |
| forest | Grovekeep | 4 | 1000 | 2500 | 2 | | | | | |
| forest | Maulhorn | 5 | 2500 | 2000 | 3 | | | | | |
| forest | Hive Cradle | 6 | 1500 | 3000 | 3 | | chrysalis | grow 1, hatch 2, into **Broodtitan** 4000/3500 | | |
| electric | Spark | 1 | 1000 | 500 | 1 | | | | | |
| electric | Jolt | 1 | 1000 | 500 | 1 | | | | | |
| electric | Volt | 2 | 1000 | 1000 | 1 | | overcharge | | | |
| electric | Crackle | 2 | 1500 | 500 | 1 | | | | | |
| electric | Surge | 3 | 2000 | 500 | 2 | ✔ | | | | |
| electric | Thunderhead | 4 | 2000 | 1500 | 2 | | overcharge | | | |
| electric | Stormcall | 5 | 3000 | 1500 | 3 | | overcharge | | | |
| electric | Galvanwyrm | 6 | 4000 | 1500 | 3 | | overcharge | | Dragon | |
| light | Dawnmote | 1 | 500 | 1000 | 1 | | | | | |
| light | Sunmote | 1 | 500 | 1000 | 1 | | | | | |
| light | Gleamward | 2 | 500 | 1500 | 1 | | ward | wardhp 1000 | | |
| light | Radiant | 2 | 1000 | 1000 | 1 | | | | | |
| light | Lumenfang | 3 | 1000 | 1500 | 2 | ✔ | | | | |
| light | Aegisol | 4 | 1500 | 2000 | 2 | | ward | wardhp 1500 | | |
| light | Solstice | 5 | 2000 | 2500 | 3 | | | | | |
| light | Seraphine | 6 | 2500 | 3000 | 3 | | ward | wardhp 2000 | | |
| dark | Wraithling | 1 | 1000 | 500 | 1 | | reap | reap 500 | | |
| dark | Shadeling | 1 | 1000 | 500 | 1 | | | | | |
| dark | Gravelurk | 2 | 1500 | 500 | 1 | | | | | |
| dark | Grimfang | 2 | 1500 | 500 | 1 | | reap | reap 500 | | |
| dark | Nightstalker | 3 | 2000 | 500 | 2 | ✔ | | | | |
| dark | Dreadmaw | 4 | 2500 | 1000 | 2 | | reap | reap 1000 | | |
| dark | Maledict | 5 | 3500 | 1000 | 3 | | reap | reap 1500 | | |
| dark | Voidwyrm | 6 | 4500 | 1000 | 3 | | reap | reap 2000 | | Dragon | |

#### Divine roster (`03_cards_creatures.js:22`) — **not deckable, not in any pool map**

| Name | c | a | h | up | FS |
|---|---|---|---|---|---|
| Cherub | 1 | 500 | 1000 | 1 | |
| Valkar | 3 | 1500 | 1000 | 2 | ✔ |
| Archon | 4 | 2000 | 1500 | 2 | |
| Empyrean | 6 | 3000 | 2500 | 3 | |

`DIVINE` is declared and never referenced anywhere else in `src/js`. It is not in `POOLS`, not in
`CARD_REG`, has no `PLACEHOLDERS` entry and no art-directory mapping. Port it as data with an
`IsPlayable=false` / "reserved" flag.

#### `WORKER` template (`03_cards_creatures.js:25`)

`{nm:'Worker', c:0, a:0, h:1000, art:ART.villager}`. It is only used to register a placeholder
(`04_cards_leaders.js:152`). The actual worker instance is built by `mkVil` (`06_mana_workers.js:93`)
from an **inline literal** with the same numbers — the two are not linked. Keep them linked in C#.

### 2.2 Spell / Trap template (`03_cards_creatures.js:81-96`)

Spells and traps live in ONE array, `SPELL_NEUTRAL`. **They are element-neutral by design** — no
`color`, generic-mana cost, legal in every deck (`03_cards_creatures.js:80`). `SPELL` at `:97` is a
dead compatibility shim (`{fire:SPELL_NEUTRAL, water:SPELL_NEUTRAL, ...}`) — nothing reads it.

| Field | Type | Required | Meaning |
|---|---|---|---|
| `nm` | string | **yes** | Name / identity. |
| `c` | int | **yes** | Mana cost — **only paid when CAST**. Setting a trap costs a flat ◆1 regardless (§7.3). |
| `trap` | bool | **yes** | `true` → set face-down and springs on a trigger; `false` → cast at instant speed from hand. |
| `effect` | string | **yes** | `burn` \| `raze` \| `chain` \| `bounce` \| `pitfall` \| `thornmail`. **This, not `nm`, drives resolution.** |
| `val` | int | no | Magnitude. `burn`/`chain` damage. Absent on `raze`, `pitfall`, `thornmail`. |
| `target` | string | no | `'enemy'` \| `'building'`. **DEAD FIELD** — carried through `drawCard` (`11_deck_builder.js:250`) but never read by any targeting code. `validSpellTarget` switches on `effect` instead (`13_input.js:53`). |
| `trigger` | string | traps only | `'summon'` \| `'attack'`. Which window arms the trap (`14_spells_traps.js:36`). |
| `ic` | string | no | Display glyph: `'✦'` spells, `'⚠'` traps. Presentation. |
| `art` | data URI | no | Placeholder art. |

#### Complete spell/trap registry (14 cards: 9 spells + 5 traps)

| # | Name | c | trap | effect | val | target | trigger | ic |
|---|---|---|---|---|---|---|---|---|
| 0 | Ember Bolt | 2 | no | burn | 1500 | enemy | — | ✦ |
| 1 | Frost Lance | 2 | no | burn | 1500 | enemy | — | ✦ |
| 2 | Cave-In | 3 | no | raze | — | building | — | ✦ |
| 3 | Dissolve | 3 | no | raze | — | building | — | ✦ |
| 4 | Snare Pit | 1 | **yes** | pitfall | — | — | summon | ⚠ |
| 5 | Whirl Trap | 1 | **yes** | pitfall | — | — | summon | ⚠ |
| 6 | Cinder Volley | 2 | no | burn | 1500 | enemy | — | ✦ |
| 7 | Searing Brand | 3 | no | burn | 2000 | enemy | — | ✦ |
| 8 | Topple the Spire | 3 | no | raze | — | building | — | ✦ |
| 9 | Collapsing Floor | 1 | **yes** | pitfall | — | — | summon | ⚠ |
| 10 | Arc Flash | 3 | no | chain | 1000 | enemy | — | ✦ |
| 11 | Riptide | 3 | no | bounce | — | enemy | — | ✦ |
| 12 | Overgrowth | 1 | **yes** | thornmail | — | — | attack | ⚠ |
| 13 | Backlash | 1 | **yes** | burn | 1500 | — | attack | ⚠ |

Note `Backlash` is the only trap whose `effect` is shared with a castable spell (`burn`); its
behaviour differs by *which* spring function reads it (§7.4).

### 2.3 Structure definition (`03_cards_creatures.js:53-71`)

**Structures are NOT deck cards.** They are built from the commander's build menu, paid in mana,
gated by a prerequisite tech tree (`03_cards_creatures.js:30-34`).

| Field | Type | Meaning |
|---|---|---|
| `bid` | string | Build id — the tech-tree token. Duplicated across per-element forges (`forge`, `grandforge`). |
| `nm` | string | Display name / art identity. |
| `c` | int | Mana cost (also the upgrade cost when reached via `up2`). |
| `h` | int | HP (→ `h` and `maxh`). |
| `eff` | string | Upkeep effect: `mana` \| `villager` \| `damage` \| `wall` \| `vault` \| `revive` \| `none` (`command` exists only on the dead `mkCC`). |
| `val` | int | Effect magnitude (◆/turn, damage/turn, vault capacity). |
| `sup` | int | Worker **support** added to its row. Can be **negative** (Cannon Tower `-2`). |
| `ic` | string | Glyph for the build menu / cardless render. |
| `prereq` | string[] | Build ids that must already be on your field (satisfied through `bidLineage`, §8.3). |
| `from` | string? | Present ⇒ **upgrade-only tier**, reached only by upgrading the named `bid`. Excluded from the build menu. |
| `up2` | string[]? | In-place upgrade targets. Multiple entries = a branch. |
| `row` | string? | Row gate: `'front'` \| `'back'`. Enforced on build placement and on upgrade (`07_structures.js:10`). |
| `color` | string\|null | Element tint. Only forges set it (from `forgeDef`/`grandForgeDef`). |
| `desc` | string | Build-menu blurb. Presentation — **but see §11.3, two of these are stale.** |
| `art` | data URI | Placeholder silhouette. |

| bid | nm | c | h | eff | val | sup | prereq | from | up2 | row |
|---|---|---|---|---|---|---|---|---|---|---|
| `foundry` | The Foundry | 2 | 3000 | mana | 1 | 2 | — | — | keep | — |
| `encampment` | Encampment | 2 | 2500 | none | 0 | 2 | foundry | — | longhouse | — |
| `longhouse` | Longhouse | 4 | 3000 | villager | 0 | 3 | foundry | — | barracks | front |
| `vault` | Mana Vault | 4 | 3000 | vault | 4 | 0 | foundry | — | grandvault | — |
| `bulwark` | Bulwark | 5 | 6000 | wall | 0 | 1 | forge | — | — | — |
| `outpost` | Outpost | 2 | 3000 | none | 0 | 1 | forge | — | tower, bastion | — |
| `tower` | Cannon Tower | 4 | 4000 | damage | 1000 | **-2** | forge | — | — | — |
| `reliquary` | Reliquary | 5 | 3500 | revive | 0 | 1 | longhouse | — | — | — |
| `keep` | Keep | 3 | 5000 | mana | 2 | 3 | — | foundry | citadel | back |
| `citadel` | Citadel | 4 | 7500 | mana | 3 | 4 | — | keep | — | back |
| `barracks` | Barracks | 3 | 5000 | villager | 0 | 4 | — | longhouse | — | front |
| `bastion` | Bastion | 3 | 9000 | wall | 0 | 2 | — | outpost | — | — |
| `grandvault` | Grand Vault | 5 | 4500 | vault | 10 | 0 | — | vault | — | — |

Per-element forges are **generated**, not stored (`03_cards_creatures.js:70-71`):

* `forgeDef(el)` → `{bid:'forge', nm:FORGE_NAMES[el], c:3, h:2500, eff:'mana', val:2, sup:2, ic:'⛭', prereq:['foundry'], color:el, up2:['grandforge'], art:forgeArt(el), desc:"A <Element> forge — yields ◆2 each turn and raises ⚒+2. Upgrades to a Grand Forge."}`
* `grandForgeDef(el)` → `{bid:'grandforge', nm:'Grand '+FORGE_NAMES[el], c:6, h:3500, eff:'mana', val:3, sup:3, ic:'⛭', prereq:['forge'], from:'forge', color:el, art:forgeArt(el), desc:"Furnaces past mortal heat — yields ◆3 each turn and raises ⚒+3."}`

`FORGE_NAMES` (`03_cards_creatures.js:23`): fire→**Emberforge**, water→**Tidewell**, earth→**Stonewell**,
wind→**Galewell**, forest→**Thornwell**, electric→**Stormforge**, light→**Dawnwell**, dark→**Gloomwell**,
divine→**Empyreum**. (The divine forge is generatable but unreachable — `buildList` only walks a
commander's colours, and no commander is divine.)

`resolveStruct(bid,color)` (`06_mana_workers.js:199`) is the single lookup:
`'forge'→forgeDef(color)`, `'grandforge'→grandForgeDef(color)`, else `STRUCT_DEFS[bid] || null`.

### 2.4 Commander / Command Center definition (`04_cards_leaders.js:9-22`)

| Field | Type | Meaning |
|---|---|---|
| `id` | string | `<element>` for solos, `<a>_<b>` for duals (canonical order, `a` before `b` in `COLORS`). |
| `name` | string | `E.name` for solos; `"A / B"` for duals. |
| `hp` | int | Starting **life pool** (all 10000 today). |
| `wk` | int | Base workers credited to the back row. |
| `colors` | string[] | 1 or 2 element ids. Gates deck legality and the forge build list. |
| `desc` | string | Lore. Solos use `ELEMENTS[el].lore`; duals use `DUAL_LORE[n]` (`04_cards_leaders.js:7`, 28 strings consumed in pair order). |

**36 commanders: 8 solo + 28 dual (`C(8,2)`).** Generation (`04_cards_leaders.js:14-21`):

```
n = 0
for i in 0..7:
  for j in i+1..7:
    a = COLORS[i]; b = COLORS[j]
    id   = a + "_" + b
    name = ELEMENTS[a].name + " / " + ELEMENTS[b].name
    hp   = Math.round( (ELEMENTS[a].hp + ELEMENTS[b].hp) / 2 )
    wk   = Math.round( (ELEMENTS[a].wk + ELEMENTS[b].wk) / 2 )
    desc = DUAL_LORE[n++] || "Two banners over one keep."
```

⚠ **PORT HAZARD:** JS `Math.round(2.5) === 3` (half-up). C# `Math.Round(2.5) == 2` (banker's
rounding). Use `Math.Round(x, MidpointRounding.AwayFromZero)` or integer `(a+b+1)/2`.
With the current data every dual is `hp=10000`, and `wk` is **2** for the six pairs drawn from
`{fire, earth, forest, dark}` (all wk 2) and **3** for the other 22 pairs. Getting the rounding wrong
silently drops one worker on 16 of the 36 commanders.

`mkCC` (`04_cards_leaders.js:23`) builds a commander as a board *building* with `cc:true` — **it is
dead code, never called.** `findCC` (`:25`) hard-returns `null`. Command centers were removed: the
whole back row is the stronghold and `P.life` is the pool (`09_game_start.js:7-8`). Code that reads
`o.cc` (e.g. `13_input.js:55`, `15_combat.js:73`, `06_mana_workers.js:137`) is defensive dead-branch
protection. **Do not port `cc` units.** Keep the *concept* (commander = profile), drop the *object*.

---

## 3. Deck registry, deck building, and deck construction

### 3.1 `CARD_REG` (`06_mana_workers.js:38-43`)

```
reg = []
for el in COLORS:            for t in POOLS[el]: reg.push({key: el+"|"+t.nm,        type:'creature', color: el,   nm:t.nm, tpl:t})
for t in SPELL_NEUTRAL:                          reg.push({key: "neutral|"+t.nm,    type:'spell',    color: null, nm:t.nm, tpl:t})
```

**78 entries** (64 creatures + 14 spells). Structures are deliberately excluded — "structures are
built, not drawn" (`06_mana_workers.js:40`). `CARD_BY_KEY` is the `key → entry` map (`:44`).
`SPELL_NAMES` is the `Set` of spell names (`:45`), used by save migration.

### 3.2 Deck rules (`06_mana_workers.js:37`)

`DECK_SIZE = 40`, `MAX_COPIES = 3`, `MAX_DECKS = 5`, `DECKS_KEY = 'srd.decks.v1'`.

`deckErrors(deck)` (`:67`) — a deck is valid iff:
1. `CCS[deck.cc]` exists.
2. Every key resolves in `CARD_BY_KEY`.
3. Every card's `color` is `null` (neutral) **or** in the commander's `colors`.
4. Every count is `1..MAX_COPIES`.
5. The total is **exactly** `DECK_SIZE`.

### 3.3 Random deck generation — `deckOf(colors)` (`06_mana_workers.js:26-35`)

```
1. d = []; n = colors.length
2. for each col in colors:
     repeat round(28/n) times: d.push({type:'creature', color:col, ...randomOf(POOLS[col] ?? EMBER)})
     repeat round(12/n) times: d.push({type:'spell',    color:null, ...randomOf(SPELL_NEUTRAL)})
3. while d.length < 40: d.push({type:'creature', color:colors[0], ...randomOf(POOLS[colors[0]])})
4. Fisher–Yates shuffle over d
5. return d.slice(0, 40)
```
Uniform random **with replacement** — no copy limit is enforced here (a `deckOf` deck can legally
contain 6 Magmaws). Solo: 28 creatures + 12 spells. Dual: 14+14 creatures, 6+6 spells.

### 3.4 Saved-deck expansion — `expandDeck(deck)` (`06_mana_workers.js:81-89`)

For each `[key,count]`, push `count` copies of `{type: entry.type, color: entry.color, ...entry.tpl}`,
then Fisher–Yates shuffle. **No truncation** — validity is the caller's job.

### 3.5 Save migration (`06_mana_workers.js:54-63`)

* `COLOR_ALIAS = {ember:'fire', tide:'water', verdant:'wind'}`
* `CC_ALIAS = {emberbastion:'fire', tidespire:'water', thornwall:'fire_water'}`
* `migrateKey(key)`: if the colour prefix isn't `neutral` and the name is in `SPELL_NAMES` → rewrite
  to `neutral|<nm>`; else map the colour through `COLOR_ALIAS`.
* `migrateDeckCards`: drop any key that no longer resolves (retired cards, structures), clamp each
  count to `MAX_COPIES`.

Port these as a versioned save-upgrade step; they are the only evidence of the old card naming.

### 3.6 Draw and opening hand

* `dealOpening(o)` (`11_deck_builder.js:247`): clears the hand, draws **4**.
* `drawCard(o)` (`:250`): `deck.pop()` (draws from the END of the array), builds a hand card.
* Empty deck is **not** a loss — `doDraw` just logs "nothing to draw" (`17_turns_ai.js:80`).
  **There is no deck-out rule.** The only loss condition is `life <= 0` (`17_turns_ai.js:392`).

---

## 4. Runtime instance schemas

### 4.1 Hand card — `drawCard` (`11_deck_builder.js:250-251`)

```
{kind:'handcard', id:uid++, type, color: (type==='spell' ? null : (t.color || G.P[o].color)),
 nm, a, h, c, fs, up, sup, eff, val, ic, art, trap, effect, target, trigger,
 kw, det, ward, wardhp, reap, grow, hatch, into, entrench, tribe, subtype}
```
A *union* record: creature fields, structure fields (`sup/eff/val/ic`) and spell fields
(`trap/effect/target/trigger`) all present, mostly `undefined`. `type` discriminates.

Two other producers of hand cards:
* `handcardFromCreature(cr)` (`06_mana_workers.js:112`) — a bounced/undertowed board creature.
  `h: cr.maxh ?? cr.h` (returns at **full** HP). **Does not copy `token`** — see §11.5.
* `reviveFromGrave(owner)` (`17_turns_ai.js:13`) — the Reliquary. Scans the grave from the END for the
  first record with `type==='creature' && !token`, splices it out, and pushes a hand card with the
  same field list.

### 4.2 Board creature — `mkCre(t, owner, worker)` (`06_mana_workers.js:90-92`)

```
{kind:'creature', id:uid++, owner, worker:!!worker,
 color: t.color || G.P[owner].color,
 nm, a: t.a, h: t.h, maxh: t.h, c: t.c,
 fs: !!t.fs, up: t.up||0,
 sick:false, tapped:false, moved:false, bank:0, art: t.art,
 kw: t.kw||null, det: t.det||0, ward: t.ward||0, wardhp: t.wardhp||2, reap: t.reap||0,
 grow: t.grow||0, hatch: t.hatch||0, into: t.into||null,
 cnt: t.cnt||0, oc: t.oc||0, entrench: !!t.entrench, token: !!t.token, blocked:false,
 tribe: t.tribe||null, subtype: t.subtype||null}
```
Mutable per-turn state, set elsewhere: `sick`, `tapped`, `moved`, `moved2`, `paid`, `blocked`,
`_dis` (Overcharge discharge), `bank` (stored ◆), `cnt` (Chrysalis), `oc` (Overcharge bank).

Reset at the owner's `startTurn` (`17_turns_ai.js:53`):
`sick=false, tapped=false, moved=false, moved2=false, paid=false, blocked=false, _dis=0`.
Note `bank`, `cnt`, `oc` **persist** across turns.

* `mkVil(owner)` (`:93`) = `mkCre({nm:'Worker', a:0, h:1000, c:0, up:0, art:ART.villager}, owner, true)`.
* `mkToken(owner,nm,a,h,color)` (`:114`) = `mkCre({nm,a,h,c:0,up:0},owner,false)` with `token=true`
  and an explicit colour. Tokens have `kw=null`, `up=0`, `c=0`, and **no `art`**.

### 4.3 Board structure — `mkBld(t, owner)` (`06_mana_workers.js:94`)

```
{kind:'building', id:uid++, owner, color: t.color || G.P[owner].color,
 nm, h: t.h, maxh: t.h, c: t.c, eff: t.eff, val: t.val||0, sup: t.sup||0,
 ic: t.ic, art: t.art, bank:0, bid: t.bid || null}
```
`bid` is what makes upgrades and the tech tree work at runtime.

### 4.4 Face-down "charge" — `place(... 'set' ...)` (`13_input.js:226-234`)

```
{kind:'charge', owner, w: which, ctype: 'creature'|'building', card: <frozen copy>, inv: 1, setTurn: G.turnNo}
```
The frozen copy is deliberately narrower than the hand card:
* creature: `{nm,a,h,c,fs,up,art, kw,det,ward,wardhp,reap,grow,hatch,into,entrench,tribe,subtype}`
* building: `{nm,c,h,eff,val,sup,ic,art}`

`inv` starts at **1** because setting costs ◆1 and that ◆1 **banks toward the card's cost**.
Mirrored exactly by the MP path (`42_mp_apply.js:137-139`).

### 4.5 Set trap (`13_input.js:224`, AI `17_turns_ai.js:300`, MP `42_mp_apply.js:129`)

```
{kind:'trap', owner, w: which, setTurn: G.turnNo,
 card: {nm, c, effect, trigger, val, ic, art, trap:true}}
```
`h` is absent — traps have no HP and are never damaged; they are destroyed by being sprung, by
Scour, or by a `burn` spell hitting a `charge` (traps are excluded from spell targeting).

### 4.6 Grave record — `toGrave(owner,obj)` (`07_structures.js:67-76`)

| Source `kind` | Emitted record |
|---|---|
| `creature` | `{type: worker?'villager':'creature', nm, a, h: maxh??h, c, up, fs, art, color, token, kw: token?null:kw, det, ward, wardhp, reap, grow, hatch, into, entrench, tribe\|\|null, subtype\|\|null}` |
| `building` | `{type:'building', nm, h: maxh??h, c, eff, val, sup, ic}` |
| `charge` | `{type: ctype\|\|'creature', nm, a, h, c, up, sup, eff, val, ic}` (read from `.card`) |
| `trap` | `{type:'spell', nm, c, trap:true, effect, val, ic}` |

`spellRec(card)` (`13_input.js:71`) is the parallel record for a **cast** spell:
`{type:'spell', nm, c, trap:!!trap, effect, val, ic}`.

Notes: the record stores **max HP**, so the Reliquary revives at full health. A charge's record
loses every keyword field, so a face-down creature that dies unflipped revives keyword-less.

---

## 5. Card lifecycle — where instances are created

| Transition | Function | Key rules |
|---|---|---|
| Hand → board (summon) | `place(idx,'summon',which,slot)` `13_input.js:212` | pay `c`; `sick=true`; `onCreatureEnter`; `foeTrapOnSummon` |
| Hand → board (build) | `place(idx,'build',...)` `13_input.js:207` | pay `c`; structures may also take centre **non-lane** slots via `handDeployOK` (`13_input.js:43`) |
| Hand → board over a banked card | `place` occupied branch `13_input.js:184-205` | old card destroyed (`toGrave`), `min(occ.bank, c)` covers the cost, surplus `occ.bank-c` carries to the newcomer as `bank` |
| Hand → face-down | `place(...,'set',...)` `13_input.js:226` | costs ◆1, banks it as `inv:1` |
| Hand → set trap | `place(...,'settrap',...)` `13_input.js:220` | costs ◆1, **not** banked (traps have no `inv`) |
| Face-down → board | `flip(owner,key,slot)` `14_spells_traps.js:110` | see §5.1 |
| Menu → board (structure) | `placeBuild(which,i)` `06_mana_workers.js:221` | mana, tech tree, `placeRowOK`, centre lanes forbidden |
| Board → grave | `cleanup()` `16_movement.js:193` | see §6.4 |
| Board → hand | `handcardFromCreature` via Undertow / `bounce` | |
| Grave → hand | `reviveFromGrave` (Reliquary, at upkeep) | |
| Nothing → board (token) | `mkToken` via Ward / Reap | placed by `firstEmptyCell` |

### 5.1 `flip()` — face-down resolution (`14_spells_traps.js:110-127`)

```
1. ch = row[slot]
2. if ch.ctype === 'building':
     b = mkBld(ch.card, owner); b.bank = max(0, ch.inv - ch.card.c); row[slot] = b; return
3. bank = max(0, ch.inv - ch.card.c)
4. sick = (G.turnNo <= (ch.setTurn ?? G.turnNo))        // set THIS turn ⇒ still summoning-sick
5. cr = mkCre(ch.card, owner, false); cr.bank = bank; cr.sick = sick; row[slot] = cr
6. onCreatureEnter(cr, owner)                            // Ward fires here
7. syncWorkers(owner)
```
**`flip()` does NOT trigger summon traps.** `foeTrapOnSummon` / `playerTrapOnSummon` are called only
from `place()` (`13_input.js:200`, `:219`), the AI summon loop (`17_turns_ai.js:312`) and the MP
summon path (`42_mp_apply.js:97,101,123`). Flipping a set creature is therefore a legal way to
dodge a Pitfall. Preserve or fix deliberately.

### 5.2 Charge funding (`14_spells_traps.js:83-109`)

The charging panel lets the owner pour mana into a face-down (`camtPour` → `ch.inv += p`) and flip it
once `inv >= card.c` (`camtFlip`). The AI auto-pours the full remainder at the start of its turn and
flips immediately if funded (`17_turns_ai.js:271-272`). Excess `inv` becomes the unit's `bank`.

---

## 6. The keyword engine

### 6.0 Hook table — exact trigger points

`kwOf(o)` (`06_mana_workers.js:98`) returns the keyword **only** for `kind==='creature' && !worker`.
Workers and structures never have keywords. Tokens carry `kw=null` and so are keyword-inert.

| Keyword | Element | Hook | Implementation | Called from |
|---|---|---|---|---|
| `ward` | Light | **ENTER** (summon or flip) | `onCreatureEnter` `06:118` | `place` `13:199,218`; `flip` `14:124`; AI summon `17:312`; MP `42:99,121` |
| `detonate` | Fire | **DEATH** | `onCreatureDeath` `06:125` | `cleanup` `16:201` |
| `reap` | Dark | **DEATH** | `onCreatureDeath` `06:130` | `cleanup` `16:201` |
| `undertow` | Water | **PRE-COMBAT** (as defender/target) | `applyUndertow` `06:135` | `resolveCombat` `15:50`; `CMB.pairFight` `15:273`; `CMB.targetFight` `15:288` |
| `entrench` | Earth | **PASSIVE** (immunity) | flag test | `applyUndertow` `06:137`; `resolveSpell` bounce `14:21` |
| `chrysalis` | Forest | **UPKEEP** (owner's turn start) | `chrysalisUpkeep` `06:144` | `startTurn` `17:54` |
| `overcharge` | Electric | **UPKEEP** + **ATTACK** | `overchargeUpkeep` `06:154`, `dischargeOvercharge` `06:159` | `startTurn` `17:55`; `CMB._resolveNow` `15:315`; `doAttack` `16:66`; `attackBackRow` `16:97`; MP `42:218` |
| `scour` | Wind | **DECLARE** (ignores blockers) + **ON-HIT** | `groupIsScour` `06:174`, `scourStrike` `06:165` | `CMB.declare` `15:251`; `CMB._resolveNow` `15:344-362`; `doAttack` `16:65,83`; `foeTurn` `17:333,367-383` |
| `fs` (First Strike) | all (1/pool) | **COMBAT TIER** | `resolveCombat` `15:53`, `CMB` tiers `15:277,295` | combat only |

### 6.1 First Strike (`fs`) — not a keyword, but rules-load-bearing

There are exactly **8** First Strike creatures among the 64 deckable ones — one per element, always
the cost-3 card. (The Divine roster's Valkar is a ninth, `03_cards_creatures.js:22`.)
In *every* damage step, blows are dealt in two tiers: FS blows land first and are applied before
non-FS blows are computed; anything killed in the FS tier never strikes back
(`15_combat.js:52-63`, `15_combat.js:277-282`, `:295-300`).
**An FS unit strikes only once** — it does not strike again in the main tier
(`15_combat.js:59`: `mainA = groupA.filter(c => !c.fs && c.h > 0)`).

### 6.2 Per-keyword algorithms

#### `ward` — Light. On ENTER, conjure a Lumen token blocker.
```
onCreatureEnter(cr, owner):
  if kwOf(cr) != 'ward': return
  spot = firstEmptyCell(owner)                       // 06:105
  if spot == null: log "no room"; return
  tok = mkToken(owner, 'Lumen', 0, cr.wardhp || 2, cr.color)
  tok.sick = true
  spot.arr[spot.i] = tok
```
`firstEmptyCell(owner)` scan order (`06_mana_workers.js:105-107`):
**1)** `G.P[owner].back` index 0→6, **2)** `G.P[owner].front` 0→6, **3)** `G.center` first empty
index that `isLane(i)` (i.e. 1, 3, 5). Returns `null` if all are full.
The Lumen token is 0 attack — it exists only to absorb a blow. `wardhp` in the registry is
1000 / 1500 / 2000; the `||2` default is legacy (§11.2).

#### `detonate` — Fire. On DEATH, blast the deadliest enemy.
```
onCreatureDeath(cr, owner):            // owner = the DYING creature's owner
  n = cr.det || 0; if n <= 0: return
  cres = liveEnemyCreatures(owner)                       // enemy creatures, non-worker, h>0
         .sort by (b.a - a.a) then (a.h - b.h)           // highest attack, then lowest HP
  tgt = cres[0] ?? liveEnemyStructures(owner).sort(a.h - b.h)[0]
  if tgt: tgt.h -= n
```
Creatures are strictly preferred; only if there are none does it hit the **weakest** enemy structure.
Nothing prevents it hitting an enemy *worker*? — `liveEnemyCreatures` filters `!o.worker`, so workers
in board slots are excluded (workers live in pools, not slots, anyway). It never touches the life pool.

#### `reap` — Dark. On DEATH, raise a Shade.
```
onCreatureDeath(cr, owner):
  spot = firstEmptyCell(owner); if null: nothing happens
  a = cr.reap || 1
  tok = mkToken(owner, 'Shade', a, a, cr.color); tok.sick = true
  spot.arr[spot.i] = tok
```
Note the log text says "in its place" but the token goes to `firstEmptyCell`, which — because the
dying creature's cell was already cleared by `cleanup` — is *usually* but not always its own cell.

`detonate` and `reap` are mutually exclusive in `onCreatureDeath` (`if / else if`), which is fine
since `kw` is single-valued.

#### `undertow` — Water. Hurl the strongest attacker back to hand.
```
applyUndertow(groupA /*attackers*/, groupB /*defenders*/):
  wardens = groupB where kwOf(c)=='undertow' and c.h>0
  if none: return
  marks = groupA where kind=='creature' and h>0 and !worker and !token and !entrench and !cc
          sorted by (b.c - a.c)                    // highest MANA COST first
  a = marks[0]; if none: return
  ow = removeUnitFromBoard(a)                      // clears its slot OR its minion-pool entry
  if ow: G.P[ow].hand.push(handcardFromCreature(a)); remove a from groupA
```
* Fires **once per combat call**, no matter how many Undertow wardens are present.
* Selection is by **mana cost**, not attack.
* Immune: workers, tokens, `entrench` units, `cc` units.
* Fires **before any damage** in `resolveCombat`, `pairFight` and `targetFight` — the removed
  attacker deals and takes nothing.
* It fires when the Undertow creature is the *target* of an unblocked attack too, not only when it
  blocks (`CMB.targetFight` passes `[T]` as groupB, `15:288`).

#### `entrench` — Earth. Immovable.
Not an active hook. Two effects only:
1. `applyUndertow` skips entrenched attackers (`06:137`).
2. `resolveSpell` `bounce` fizzles against them, logging "slides off" but **still consuming the
   spell** and returning `true` (`14_spells_traps.js:21`).
It does **not** prevent the owner from moving the unit voluntarily (`16_movement.js` never checks it).

#### `chrysalis` — Forest. Cocoon that swells and hatches.
```
chrysalisUpkeep(owner):                             // startTurn, owner's turn only
  for o in ownUnits(owner) where kwOf(o)=='chrysalis':
    o.cnt = (o.cnt||0) + (o.grow||1)
    if o.cnt >= (o.hatch||3):
        into = o.into || {}
        o.nm   = into.nm ?? o.nm
        o.a    = into.a  ?? o.a
        o.maxh = into.h  ?? o.maxh
        o.h    = into.h  ?? o.maxh ?? o.h          // full heal to the new max
        o.up   = into.up ?? o.up
        o.fs   = into.fs ?? o.fs
        o.kw   = into.kw || null                    // keyword cleared → stops swelling
        o.sick = true                               // cannot attack the turn it hatches
    else:
        o.sick = true                               // cocoon can never attack
```
Consequences worth stating explicitly:
* A cocoon is re-set to `sick` **every** upkeep, so it can never attack, but summoning-sick units
  **may still block** (`16_movement.js:29`) and may still reposition (`:35`).
* `cnt` is **not** reset on hatch; `kw` becoming `null` is what stops the loop.
* `into` in the registry only carries `{nm,a,h}` — `up`, `fs` are inherited from the cocoon
  (Sap Pod: `up 1`; Hive Cradle: `up 3`). The hatched form keeps the cocoon's `c`, `tribe`, `subtype`.
* The transformation mutates the SAME instance (id, owner, bank, cell preserved).

#### `overcharge` — Electric. Bank ◆, discharge on attack.
```
overchargeUpkeep(owner):    for each own unit with kw 'overcharge': o.oc = min(3, (o.oc||0)+1)
dischargeOvercharge(atts):  for each a with kw 'overcharge' and a.oc>0: a._dis = a.oc; a.oc = 0
effA(c) = (c.a||0) + (c._dis||0)                    // 06:115
clearDischarge(units):      a._dis = 0
```
`effA` is used for damage in `focusFire` (`15:28,38,39,43`), the pair/target fights
(`15:278,296`), wall damage (`15:344`, `16:107`), and the AI's power estimate (`15:254`).
Raw `.a` is used for **retaliation** damage (`15:279,297`) — i.e. a defender's counter-blow never
includes a discharge. `_dis` is cleared by `clearDischarge` after each resolution and by `startTurn`.

⚠ **Balance note for the port:** `oc` is capped at 3, added to attack values in the 500–4500 range.
As implemented, Overcharge adds +1..+3 attack — effectively nothing. This is almost certainly a
missed ×500 rescale (§11.2).

#### `scour` — Wind. Flier: ignores interceptors, shatters a back-row card.
Two distinct effects:

**(a) Blocking bypass.** In Combat v3 (`CMB.declare`, `15:251`) the check is **per attacker**:
`if (kwOf(A) !== 'scour' && aIdx !== tIdx) { ...offer blocks... }` — a Scour attacker is simply never
offered to blockers. In the legacy single-shot path (`doAttack` `16:65`, `attackBackRow` `16:96`) the
check is `groupIsScour(attackers)` (`06:174`), which requires **every** attacker in the group to have
Scour. The MP path uses the group form (`42_mp_apply.js:223`). Unify on the per-attacker rule.

**(b) On-hit back-row shatter** (`scourStrike(att, defOwner)`, `06:165-173`):
```
back = G.P[defOwner].back
idx = first index where kind=='charge' or kind=='trap'          // face-downs preferred
if idx < 0: idx = first index where kind=='building' and !cc
if idx < 0: return                                              // nothing to shatter
tgt = back[idx]
if tgt is charge or trap:  toGrave(defOwner,tgt); back[idx] = null
else:                      tgt.h = 0                            // structure dies at next cleanup
```
It fires once per surviving unblocked Scour attacker, collected in `scourHits` and applied after all
other damage (`15:362`, `17:383`). Blocked Scour declarations do **not** scour.

### 6.3 Keyword inspect text (`kwText`, `06_mana_workers.js:176-185`)

The player-facing rules text. Reproduce these strings (they are the only in-game rules
documentation). Short labels for the hand card come from `kwName` (`13_input.js:73`).

| kw | Text |
|---|---|
| detonate | **Detonate {det}.** When destroyed, deals {det} to the deadliest enemy creature (or an enemy structure). Never hits a command center. |
| undertow | **Undertow.** When this blocks or is attacked, the strongest attacking creature is hurled back to its owner's hand (re-summoning-sick). |
| entrench | **Entrench.** Immovable — cannot be bounced or pushed; effects like Undertow slide off. |
| ward | **Ward.** On entry, conjures a 0/{wardhp} Lumen token blocker beside it. |
| reap | **Reap {reap}.** When destroyed, raises a {reap}/{reap} Shade token in its place. |
| chrysalis | **Chrysalis {cnt}/{hatch}.** Cannot attack; swells +{grow} each of your turns, then hatches into {into.nm} (⚔{into.a}/♥{into.h}). |
| scour | **Scour.** Flier — ignores interceptors and shatters an enemy back-row card on attack. |
| overcharge | **Overcharge.** Banks ◆ each of your turns (up to 3); when it attacks it discharges them as bonus ⚔. |

### 6.4 Death sweep — `cleanup()` (`16_movement.js:193-205`)

```
loop (max 40 iterations, re-running while anything died):
  for each row key in ROWS, for i in 0..SLOTS-1:
     c = row[i]
     if c and (kind creature or building) and c.h <= 0:
        row[i] = null                                   // cell freed FIRST
        if c.kind=='creature' and !c.worker: onCreatureDeath(c, c.owner)   // Detonate/Reap here
        toGrave(c.owner, c)
  for each owner, each worker pool (back/front/center): remove and grave anything with h <= 0
```
The cell is cleared **before** the death keyword fires, which is why a Reap Shade can land in the
dead creature's own square. The loop re-sweeps so chained kills (a Detonate that kills another
Detonate) resolve in one call. Command centers are never swept — `checkWin` ends the duel instead.

---

## 7. Spells and traps

### 7.1 Activation windows

| Card class | When it can be played | Gate |
|---|---|---|
| Spell (`trap:false`) | Your **Action** phase only | `onHand` returns early unless `G.phase==='action'` (`13_input.js:3`); `castSpell` needs `type==='spell' && !trap` |
| Trap (`trap:true`) | Set during your **Action** phase; springs during the **opponent's** turn | `place(...,'settrap',...)`; arming needs `G.turnNo > setTurn` |
| Face-down creature/structure | Set during Action; flipped by funding or by being attacked | §5.1, §7.5 |

There is **no instant-speed spell casting on the opponent's turn.** The only opponent-turn response
is springing a trap through the RESP window (§7.6).

### 7.2 Spell casting — `castSpell(idx,key,i)` (`14_spells_traps.js:26-33`)

```
1. card = G.P.you.hand[idx]; require card.type==='spell' && !card.trap
2. require manaTotal('you') >= card.c            else hint "Not enough mana."
3. require canPay('you', card)                   else hint "<nm> needs ◆<c>."
4. require resolveSpell(card, key, i) === true   else hint "Not a legal target for that spell."
5. payCost('you', card); hand.splice(idx,1); grave.push(spellRec(card))
6. clear selection; render; checkWin
```
**Cost is paid only after the effect resolves successfully** — an illegal target costs nothing.

**Target legality is enforced by the CALLER, not by `resolveSpell`.** `onCell` (`13_input.js:109`)
requires `o.owner === 'foe' && validSpellTarget(card, o)`. `resolveSpell` itself never checks
ownership, so an incorrectly wired caller could burn your own creature. The AI paths
(`17_turns_ai.js:290`, `:295`) search only for `o.owner==='you'` targets before calling.

`validSpellTarget(card, o)` (`13_input.js:53-61`):
```
if !o                     -> false
if o.cc                   -> false        (dead branch; no cc units exist)
raze   -> o.kind==='building'
burn   -> o.kind==='creature' || 'building' || 'charge'
chain  -> o.kind==='creature' && !o.worker
bounce -> o.kind==='creature' && !o.worker
else   -> false
```
`spellHasTarget(card)` (`13_input.js:49`) scans all 5 rows × 7 slots for any foe-owned legal target;
the Cast button is disabled when it returns false.

### 7.3 Spell resolution — `resolveSpell(card, key, i)` (`14_spells_traps.js:2-25`)

Returns `true` if the spell resolved (and is therefore spent), `false` to reject the target.
`o = rowArr(key)[i]`; `towner = o.owner`. Always ends with `cleanup()`.

| effect | Algorithm |
|---|---|
| `burn` | If `o.kind==='charge'`: destroy it outright — `toGrave(towner,o)`, clear the cell (the invested ◆ is lost). Otherwise `o.h -= card.val`. |
| `raze` | If `o.kind !== 'building'` → return `false`. Otherwise destroy outright: `toGrave`, clear cell. **Ignores HP** — a 9000-HP Bastion dies to a ◆3 Cave-In. |
| `chain` | If `o.kind!=='creature' \|\| o.worker` → `false`. `caster = enemyOf(towner)`; take `liveEnemyCreatures(caster)` (= creatures owned by `towner`) sorted by `(b.a-a.a)` then `(a.h-b.h)`, **slice(0,2)**; each takes `card.val`. If the list is empty → `false`. **The clicked target only identifies the side** — it may take no damage at all if it isn't in the top two by attack. |
| `bounce` | If `o.kind!=='creature' \|\| o.worker` → `false`. If `o.entrench` → log "slides off", **return `true`** (spell is spent, nothing happens). Otherwise `removeUnitFromBoard(o)` and push `handcardFromCreature(o)` to that owner's hand. |
| anything else | return `false` |

`pitfall` and `thornmail` have **no** `resolveSpell` branch — they exist only as trap effects.

### 7.4 Trap mechanics

**Arming.** `findArmedTrap(owner, trigger)` (`14_spells_traps.js:34-40`):
```
for w in ['front','back']: for i in 0..SLOTS-1:
    o = G.P[owner][w][i]
    if o.kind=='trap' and o.card.trigger==trigger and G.turnNo > (o.setTurn ?? 0): return {o,w,i}
for i in 0..SLOTS-1:
    o = G.center[i]
    if o.kind=='trap' and o.owner==owner and o.card.trigger==trigger and G.turnNo > (o.setTurn??0): return {o,w:'center',i}
return null
```
* Returns the **first** match; only ONE trap fires per trigger event.
* `G.turnNo` increments once per *player* turn (`17_turns_ai.js:50`), so a trap set on your turn N is
  armed from the opponent's turn N+1 — i.e. it can never spring on the turn it was set.
* `RESP.findArmedTraps` (`30_resp.js:10`) is the identical plural version used to populate the
  response bar.

**Trigger `summon`.**
* *Opponent's trap vs YOUR summon* — `foeTrapOnSummon(cr,w,i)` (`14_spells_traps.js:42-50`):
  auto-springs, no choice. Destroys the just-summoned creature (`toGrave`, clear cell), moves the
  trap to the foe's grave, clears the trap's cell, `cleanup()`.
* *YOUR trap vs the AI's summon* — `playerTrapOnSummon(cr,w,i)` (`14_spells_traps.js:52-69`), a
  Promise-returning modal with **Spring it / Hold**. **This whole function is REPLACED at load time
  by the RESP layer** (`30_resp.js:124-133`), which swaps the modal for the response bar (§7.6).
* ⚠ **Both summon-trap functions ignore `card.effect` entirely** — any `trigger:'summon'` trap simply
  destroys the summoned creature. Today all three are `pitfall`, so it is invisible, but the data
  model implies otherwise. Decide explicitly in the port.

**Trigger `attack`.** `springAttackTrap(defOwner, attackers, defender)` (`15_combat.js:111-118`):
```
t = findArmedTrap(defOwner,'attack'); if none: return
if card.effect=='thornmail':
   if defender is a creature (and !cc): defender.a += 500; defender.maxh += 1000; defender.h += 1000   // PERMANENT
else if card.effect=='burn':
   for each attacker: a.h -= (card.val||0)
grave.push(spellRec(card)); clear the trap's cell
```
Called **only** for creature targets (`15:335`, `16:78`) and structure targets (`15:350`, `16:78`,
`42:249`). It is **not** called when the target is a face-down, a trap, a worker stack, or the wall.
`RESP.springAttackTrapRef` (`30_resp.js:92`) is the identical body but takes an already-chosen trap
ref, with a re-validation guard (`cellArr(defOwner,t.w)[t.i] !== t.o` → abort).

**Trap struck directly.** `springTrap(defOwner,key,slot,attackers)` (`15_combat.js:101-109`):
```
pitfall -> highest-attack attacker gets h = 0                (sort by b.a - a.a, take [0])
burn    -> every attacker takes card.val
thornmail -> nothing (fizzles: there is no defending creature)
always: grave.push(spellRec(card)); row[slot] = null; cleanup()
```
This path **ignores `trigger`** — attacking any set trap springs it.

**Trap destroyed without springing:** Scour (`06:172`).

### 7.5 Face-down provocation — `provokeFaceDown` (`15_combat.js:87-99`)

```
o = row[slot]; require kind=='charge'
if o.inv < o.card.c:
   destroyed — "interrupted", toGrave, clear cell, cleanup, return     // the banked ◆ is LOST
else:
   flip(defOwner,key,slot)                                             // spends cost, banks surplus
   now = row[slot]
   if now is creature: resolveCombat(attackers,[now])                  // it fights back, simultaneous
   else:               applyDmg(focusFire(attackers,[now])); cleanup() // structure just eats the hit
```

### 7.6 The RESP (pause-to-respond) layer — `30_resp.js`

A **timing rule**, not a card rule, but it changes *when* traps resolve, so it is part of this
subsystem's contract.

* Setting: `localStorage['srd.respwin']` ∈ `off|3|4|6`, default `'4'` → window in ms
  (`30_resp.js:6`, `:22`). MP forces 4000 but returns immediately (the MP layer owns windows).
* `RESP.actingGate(trigger, then)` (`:35`): when *you* act, a slim "Opponent may respond…" pill runs
  for the window duration, then `then()` executes. It wraps `doAttack`, `attackBackRow`,
  `attackMinionStack` (`:107-112`) and `foeTrapOnSummon` (`:118-121`). **Anti-tell:** the AI's trap
  check happens inside `then()`, so the pause is constant-length whether or not a trap exists.
* `RESP.defendWindow(trigger, ctx)` (`:57`): when the *opponent* acts, a bar shows one button per
  armed trap of that trigger, plus **⏸ Pause** (swaps in a fresh 15 000 ms timer) and **Pass**.
  Timeout = auto-pass. Resolves to the chosen trap ref or `null`.
* Input is hard-locked while a window is open — `onCell` and `onHand` are wrapped to no-op
  (`30_resp.js:102-103`).

For the AI turn, `foeTurn` opens **one** defend window over the whole declaration set
(`17_turns_ai.js:328`) and applies the chosen trap to the first eligible declaration
(`17_turns_ai.js:361`, `:373`) — `springRef` is consumed once.

---

## 8. Structures as a card class

### 8.1 Build gating — `canBuild(owner, def)` (`06_mana_workers.js:198`)

`manaTotal(owner) >= def.c` **AND** `prereqMet(owner,def)` **AND** `hasPlacement(owner,def)`.

* `prereqMet` (`:193`) — every `bid` in `def.prereq` must be present via `hasBuild` (`:192`).
* `hasPlacement` (`:197`) — some empty slot in back/front/center that satisfies `placeRowOK`.
* `placeRowOK(owner,which,def)` (`:196`) — `def.sup >= 0` **or**
  `rowWorkers(owner,which) + def.sup >= 0`. This is what stops you dropping a Cannon Tower (`sup:-2`)
  into a row that cannot crew it.
* Centre placement: `centerSlotOK(which,slot,isBld)` (`01_core_defs.js:7`) — structures only on the
  **non-lane** centre slots `0,2,4,6`; creatures only on lanes `1,3,5`.

### 8.2 Build list — `buildList(ccId)` (`03_cards_creatures.js:73-79`)

Order (this is the menu order and the AI's priority order):
`foundry`, then `forgeDef(el)` for each of the commander's colours, then
`encampment, longhouse, vault, outpost, bulwark, tower, reliquary`, then
`grandForgeDef(el)` for each colour. Solo commanders get 10 entries, duals 12.

### 8.3 Upgrades (`07_structures.js:4-31`)

* `upgradeTargets(o)` (`:4`) — `resolveStruct(o.bid, o.color).up2` mapped through `resolveStruct`.
  Returns `[]` for `cc` units and for units with no `bid`.
* `upgradeWhy(owner,o,key,def)` (`:9`) — the first failing reason:
  1. `def.row` set and `whichOf(key) !== def.row` → "only in your back/front row"
  2. `manaTotal(owner) < def.c` → "need ◆N"
  3. `def.sup < 0` and `rowWorkers(owner,whichOf(key)) - o.sup + def.sup < 0` → "row has no ⚒ to spare"
* `applyUpgrade(o,def)` (`:16`) — mutates the SAME instance (keeps `id`, `owner`, `bank`, cell):
  ```
  o.bid,o.nm,o.eff,o.ic = def.*;  o.val = def.val||0;  o.sup = def.sup||0
  dmg   = max(0, (o.maxh ?? def.h) - o.h)     // accumulated damage carries through
  o.maxh = def.h
  o.h    = max(1, def.h - dmg)                // upgrading REPAIRS NOTHING
  o.c    = def.c;  o.art = def.art
  if def.color: o.color = def.color
  ```
* `bidLineage(b)` (`06_mana_workers.js:191`) — walks `def.from` up to 8 hops, so a Keep still
  satisfies a `foundry` prereq and a Grand Forge still satisfies `forge`.

### 8.4 Structure upkeep effects — `buildingUpkeep(owner)` (`17_turns_ai.js:2-11`)

Runs at the owner's `startTurn`, over the owner's `front`, `back` arrays and their centre units.

| `eff` | Effect |
|---|---|
| `mana` | `P.mana = min(99, P.mana + val)` |
| `damage` | `buildingDamage(owner,val,nm)` (`17:25`) — scan the enemy's `front`, then `center`, then `back` in index order, hit the **first** non-worker creature for `val`. No target ⇒ nothing. |
| `revive` | `reviveFromGrave(owner)` — **once per turn regardless of how many Reliquaries you own** (guarded by a local `revived` flag, `17:3,7`). |
| `villager` | **NO BRANCH — does nothing.** See §11.3. |
| `wall` | No branch (passive by design). See §11.3. |
| `vault` | No branch here; read at end of turn by `vaultCap`. |
| `none` | Nothing. |

`vaultCap(owner)` (`17:33`) sums `val` over all `eff==='vault'` buildings; `drainMana`/`endTurnDrain`
(`17:34,38`) clamp `P.mana` to that cap at end of turn — everything above it evaporates.

### 8.5 Worker economy contribution (`05_board_state.js:61-68`)

```
rowWorkers(owner, zone):
  s = 0
  for each row key in zoneKeys(owner,zone), for each unit o in that row where o.owner==owner:
     if o.kind=='building':  s += (o.sup||0) + (o.eff=='villager' ? (o.val||0) : 0)
     if o.kind=='creature' && !o.worker: s -= (o.up||0)
  if zone=='back': s += CCS[G.P[owner].cc].wk
  return s
```
Because every `villager` structure has `val:0`, the `eff==='villager'` term is always 0 today.

---

## 9. Art resolution (the 3-tier fallback)

⚠ **The assignment named `02_art.js`, but the art *resolution* logic actually lives in
`04_cards_leaders.js:49-158`.** `02_art.js` contains only the procedural placeholder SVG generators
that feed the third tier.

### 9.1 Slug derivation — `slugify(n)` (`04_cards_leaders.js:53`)

```
slugify(n) = String(n ?? "").toLowerCase()
                            .replace(/^the\s+/, "")      // drop a leading "The "
                            .replace(/[^a-z0-9]+/g, "")  // strip everything else
```
Examples: `Magmaw → magmaw`, `Snare Pit → snarepit`, `Cave-In → cavein`,
`The Foundry → foundry`, `Grand Emberforge → grandemberforge`, `Topple the Spire → topplethespire`
(only a *leading* "The " is dropped).

⚠ **Collision risk:** slugs are not unique-checked. `Sap Pod → sappod` and a hypothetical
`Sap-Pod` would collide. Validate uniqueness at import time in Unity.

### 9.2 Directory table — `dirTable()` (`04_cards_leaders.js:59-75`)

Built **lazily and memoised** into `DIR_BY_SLUG` on the first art request, from the live card data:

```
for el in COLORS: for c in POOLS[el]:
    t[slug(c.nm)] = c.type=='spell'    ? (c.trap ? 'Traps/' : 'Spells/')
                  : c.type=='building' ? 'Structures/'
                  : 'Creatures/' + capitalize(c.color || el) + '/'
for c in SPELL_NEUTRAL:       t[slug(c.nm)] = c.trap ? 'Traps/' : 'Spells/'
for k in STRUCT_DEFS:         t[slug(STRUCT_DEFS[k].nm)] = 'Structures/'
for el in FORGE_NAMES:        s = slug(FORGE_NAMES[el]); t[s] = 'Structures/'; t['grand'+s] = 'Structures/'
```
Pool templates have no `type` field, so the creature branch always wins → `Creatures/<Element>/`
with the element capitalised (`Fire`, `Water`, `Earth`, `Wind`, `Forest`, `Electric`, `Light`, `Dark`).
**Divine creatures, `Worker`, and commander names are absent from the table** and therefore resolve
flat only.

On-disk layout (verified): `assets/cards/{Creatures/{Dark,Earth,Electric,Fire,Forest,Light,Water,Wind},Spells,Structures,Traps}` plus the flat `assets/cards/` fallback.

### 9.3 Probe order

```
ART_DIR    = 'assets/cards/'                    (04:50)
ART_EXTS   = ['png','jpg','jpeg','webp']        (04:51)   — card art
FIELD_EXTS = ['png','webp','jpg']               (04:116)  — field art (DIFFERENT ORDER)
SPRITE_DIR = 'assets/sprites/'                  (04:108)
SPRITE_EXTS= ['png','webp','jpg']               (04:109)

artDirs(n)   = DIR_BY_SLUG[slug] ? [ART_DIR+dir, ART_DIR] : [ART_DIR]      (04:76)
artURLs(n)   = for each dir, for each ART_EXTS  → dir + slug + '_cardart.'  + ext   (04:79)
fieldURLs(n) = for each dir, for each FIELD_EXTS→ dir + slug + '_fieldart.' + ext   (04:119)
```
So a typed card has **8** card-art candidates and **6** field-art candidates; an untyped card has
4 and 3. Example for `Sap Pod`:
```
assets/cards/Creatures/Forest/sappod_cardart.png | .jpg | .jpeg | .webp
assets/cards/sappod_cardart.png | .jpg | .jpeg | .webp
```

### 9.4 The three tiers

**Tier 1 — `_cardart` (the square card face).**
`cardArtImg(card, extra)` (`04:86`) emits
`<img class="cardart …" data-card="<nm>" data-ext="0" src="artPath(nm)" onerror="artFallback(this)">`.
`artPath(n) = EMBEDDED[slug] || artURLs(n)[0]` (`04:83`) — `EMBEDDED` is the baked data-URI map
injected by `tools/embed-art.py` for the portable build.
`artFallback(img)` (`04:93-103`):
```
1. if EMBEDDED[slug]: use it, stop
2. ei = (+data-ext) + 1; urls = artURLs(nm)
3. if ei < urls.length: data-ext = ei; src = urls[ei]; return       // walk the whole candidate list
4. else: src = PLACEHOLDERS[nm]  (tier 3)  — or remove src entirely if there is none
```

**Tier 2 — `_fieldart` (the on-board standee cut-out).**
`spriteImg(card)` (`04:124-129`):
```
if FIELD_MISS[slug] and no EMBEDDED_FIELD[slug]:
    emit <img class="spritefig fromart" data-stage="cardart" src=artPath(nm)>   // known-missing: skip the 404s
else
    emit <img class="spritefig"         data-stage="field"   src=fieldPath(nm)>
```
`spriteFallback(img)` (`04:131-148`) is a **two-stage** chain:
```
stage 'field':
   walk the remaining fieldURLs
   when exhausted:  FIELD_MISS[slug] = true          // never re-request this card's field art
                    add class 'fromart'; data-stage='cardart'; data-ext='0'; src = artPath(nm)
stage 'cardart':
   EMBEDDED[slug] → use it
   walk remaining artURLs
   exhausted → PLACEHOLDERS[nm], else remove src
```
The `FIELD_MISS` memo is a **browser 404-avoidance workaround**, not a game rule (Unity's addressables
resolve synchronously; drop it).

**Tier 3 — built-in placeholder SVG.** `PLACEHOLDERS[nm] = template.art`, populated at load
(`04:150-157`) from: every `POOLS[el]` card, every `SPELL_NEUTRAL` card, `WORKER`, every
`STRUCT_DEFS` entry, `forgeDef(el)`/`grandForgeDef(el)` for each colour, and every commander name →
`CC_ART[id]`. `PLACEHOLDERS['Worker'] = ART.villager`.
⚠ `DIVINE` creatures get **no** placeholder entry (`04:151` iterates `COLORS` only), so a Divine card
whose file is missing would render with **no `src`**.

### 9.5 Placeholder generation (`02_art.js`) — presentation only

* `A_BG` (`:5`) — one radial gradient per element plus three neutral themes: `wood` (structures),
  `arc` (spells), `snare` (traps).
* `frame(bg, inner)` (`:13`) → a 120×120 SVG; `artURI(s)` (`:14`) → `data:image/svg+xml,` + encoded.
* `phArt(el, kind, tier)` (`:39`) — the parametric fallback: `kind==='bld'` → element glyph watermark
  + forge silhouette; otherwise glyph watermark + a creature body scaled `0.78 + tier*0.045`, with
  horns at tier ≥ 3 and a third horn at tier ≥ 5.
* `ccArt(colors)` (`:40`) — one- or two-tone tower for commanders.
* `ART` (`:55-76`) — 20 hand-drawn placeholder scenes (5 Fire creatures, 5 Water creatures,
  `emberforge`, `tidewell`, `longhouse`, `villager`, and 6 spell/trap scenes).
* `elemBadge(el,size)` / `elemGem(el,size)` (`:42`, `:47`) — the kanji gem shown beside a card's cost;
  `elemGem(null)` renders a neutral `◇` gem for spells/traps.
* `BLD_ART` (`03_cards_creatures.js:40-52`) — 12 distinct structure silhouettes.
* `FORGE_ART` (`03:24`) only defines fire and water; `forgeArt(el)` (`03:35`) falls back to
  `phArt(el,'bld')` for the other seven.

**Everything in `02_art.js` and `BLD_ART` can be deleted in the Unity port** — replace with a single
"missing art" sprite plus the element tint. Nothing in the rules depends on `art` being non-null
except two cosmetic `card.art ? … : …` branches (`12_render.js:139,150`).

### 9.6 Sleeves and frames (`04_cards_leaders.js:167-212`) — presentation only

`probeSleeves()` uses `new Image()` (silent on 404) to detect
`assets/sleeves/cardback.(png|webp)` and `assets/sleeves/frame_<element>.(png|webp)` (nine elements
plus `neutral`), and flips `<html>` classes + CSS custom properties. Pure skinning; no rules.

### 9.7 Vestigial art API

`artBase` (`04:82`), `fieldBase` (`04:122`), `spritePath` (`04:113`), `SPRITE_DIR`/`SPRITE_EXTS`
(`04:108-109`) are defined but never called by the game — the "`_sprite`" naming convention was
superseded by "`_fieldart`". `EMBEDDED_SPRITES` is still filled by `tools/embed-art.py:113`.

---

## 10. Suggested C# types

```csharp
// ---------- pure data (ScriptableObject-backed, no UnityEngine in the rules core) ----------
public enum Element { Fire, Water, Earth, Wind, Forest, Electric, Light, Dark, Divine }
public enum CardType { Creature, Spell, Structure }      // structures are NOT drawn; kept as a type
public enum Keyword  { None, Detonate, Undertow, Entrench, Ward, Reap, Chrysalis, Scour, Overcharge }
public enum SpellEffect { Burn, Raze, Chain, Bounce, Pitfall, Thornmail }
public enum TrapTrigger { None, Summon, Attack }
public enum StructureEffect { None, Mana, Villager, Damage, Wall, Vault, Revive }
public enum Tribe   { None, Human, Dragon }
public enum Subtype { None, Wizard, Warrior }

public readonly struct CardId : IEquatable<CardId> { public readonly string Value; }   // == nm today
public readonly struct DeckKey { public readonly Element? Color; public readonly string Name; }  // "fire|Magmaw"

public sealed class ElementDef {                 // 9 rows, from 01_core_defs.js:15
    public Element Id; public string Name, Glyph, Lore;
    public string ColorHex, AccentHex, DeepHex; public string[] BgStops;
    public int Hp, Wk; public bool Deckable;
}

public sealed class CreatureCard {               // 64 + 4 divine
    public CardId Id; public string Name; public Element Element;
    public int Cost, Attack, Health, Upkeep;
    public bool FirstStrike, Entrench;
    public Keyword Keyword;
    public int Detonate, Reap, WardHp, Grow, Hatch;      // 0 == "unset"
    public HatchForm Into;                                // null when not Chrysalis
    public Tribe Tribe; public Subtype Subtype;
}
public sealed class HatchForm { public string Name; public int Attack, Health; public int? Upkeep; public bool? FirstStrike; public Keyword? Keyword; }

public sealed class SpellCard {                  // 14 (9 spells + 5 traps)
    public CardId Id; public string Name; public int Cost;
    public bool IsTrap; public SpellEffect Effect; public int Value; public TrapTrigger Trigger;
}

public sealed class StructureDef {               // 13 static + 2 generated families
    public string Bid; public string Name; public int Cost, Health, Value, Support;
    public StructureEffect Effect;
    public string[] Prereq; public string From; public string[] UpgradesTo;
    public RowGate Row;                          // None | Front | Back
    public Element? Color; public string Description;
    public bool IsUpgradeOnly => From != null;
}
public sealed class CommanderDef { public string Id, Name, Lore; public int Hp, Workers; public Element[] Colors; }

// ---------- runtime (mutable, serializable, no UnityEngine) ----------
public enum UnitKind { Creature, Building, Charge, Trap }
public sealed class Unit {
    public int InstanceId; public UnitKind Kind; public PlayerSide Owner; public Element Color;
    public CardId Card; public string Name;
    public int Attack, Health, MaxHealth, Cost, Upkeep, Support, Value;
    public bool FirstStrike, Entrench, IsWorker, IsToken;
    public Keyword Keyword;
    public int Detonate, Reap, WardHp, Grow, Hatch, ChrysalisCount, OverchargeBank, DischargeBonus, StoredMana;
    public HatchForm Into;
    public bool Sick, Tapped, Moved, MovedTwice, PaidUpkeep, HasBlocked;
    public string Bid;                            // structures: current tier
    public int EffectiveAttack => Attack + DischargeBonus;
}
public sealed class FaceDown { public bool IsStructure; public CardSnapshot Card; public int Invested; public int SetTurn; }
public sealed class SetTrap  { public SpellCard Card; public int SetTurn; }
public sealed class GraveRecord { /* flattened; see §4.6 — keep enough to revive a creature */ }

// ---------- registries ----------
public interface ICardRegistry {
    IReadOnlyList<CreatureCard>  Creatures { get; }      // 64
    IReadOnlyList<SpellCard>     Spells    { get; }      // 14
    IReadOnlyDictionary<string, StructureDef> Structures { get; }
    IReadOnlyDictionary<string, CommanderDef> Commanders { get; }   // 36
    StructureDef ResolveStructure(string bid, Element? color);      // forge / grandforge synthesis
    CreatureCard ByDeckKey(DeckKey key);
}

// ---------- keyword dispatch (deterministic, UI-free) ----------
public interface IKeywordHandler {
    Keyword Keyword { get; }
    void OnEnter (GameState s, Unit self);
    void OnDeath (GameState s, Unit self);
    void OnUpkeep(GameState s, Unit self);
    void OnBeforeCombat(GameState s, IList<Unit> attackers, IList<Unit> defenders);
    bool IgnoresBlockers { get; }                 // Scour
    void OnHit(GameState s, Unit self, PlayerSide defender);   // Scour back-row shatter
}
```
**Determinism:** every place the JS uses `Math.random()` (`deckOf`, `expandDeck` shuffles, the AI's
`Math.random()<0.6` / `<0.3` target rolls at `17_turns_ai.js:259,263`) must become a seeded
`IRandom` threaded through `GameState`, so the same seed + same command list replays identically —
this is the prerequisite for host-authoritative netcode later.

**Sort stability:** the JS sorts in `focusFire`, `detonate` targeting, `chain` targeting and
`applyUndertow` are `Array.prototype.sort`, which is **stable** in modern V8. `List<T>.Sort` in .NET
is **unstable**. Use `OrderBy`/`OrderByDescending` (stable LINQ) or add an explicit tiebreak on
board position, or blocked/attacked-unit selection will differ between the JS reference and the port.

---

## 11. Cross-file behaviour, dead code, and defects the port must decide about

### 11.1 Where this subsystem is mutated from outside its own files

| What | Where | Rules-relevant? |
|---|---|---|
| The **entire keyword engine** lives in `06_mana_workers.js:96-185`, not in the "cards" files | — | **Yes** — easy to miss |
| Keyword upkeep hooks are invoked from `startTurn` (`17_turns_ai.js:54-56`) | | Yes |
| Death keywords are invoked from `cleanup` (`16_movement.js:201`) | | Yes |
| Combat keyword hooks from `15_combat.js` and `16_movement.js` | | Yes |
| `playerTrapOnSummon` is **wholesale replaced** by `30_resp.js:124` | | **Yes** |
| `foeTrapOnSummon`, `doAttack`, `attackBackRow`, `attackMinionStack` are wrapped by `30_resp.js:107-121` to insert the response window | | **Yes** (timing) |
| `22_fx_wrappers.js` monkey-patches `applyDmg, resolveCombat, toGrave, doAttack, attackBackRow, attackMinionStack, place, flip, castSpell, springTrap, doMove, aiMoveCreature, onCreatureEnter, placeBuild, aiBuild, resolveSpell, doHarvest, applyHarvest, applyRes, trainVillager, dealOpening, drawCard, startTurn, render, startGame, checkWin, renderCharSel` | `22_fx_wrappers.js` | **No** — every wrapper calls through and only adds SFX/FX. Verified line by line. **Do not port.** |
| `41_mp_sync.js:10-27` strips `art` from snapshots and rehydrates it via `CARD_BY_KEY` | | Serialization only — but it proves `(color,nm)` must remain a stable lookup key |
| `42_mp_apply.js:80-142, 200-259` re-implements summon/set/build/attack with server-side validation | | **Yes** — it is the authoritative-validation reference for the future netcode; its checks (`bad(m.q,'mana'|'slot'|'card'|'phase'|'target'|'row')`) enumerate exactly which preconditions must be re-verified host-side |
| `10_campaign_dialogue.js:7` `CAMP_CHAMPS` names one flagship creature per element (Magmaw, Leviath, Titanore, Tempest, Hive Cradle, Galvanwyrm, Seraphine, Voidwyrm) | | Content dependency on card names |
| `11_deck_builder.js:24-27` `DB_KW_LABEL`/`DB_KW_ORDER` and the tribe filter | | UI, but it is the canonical keyword display-name list |

### 11.2 Incomplete ×500 stat rescale (probable bugs — decide explicitly)

Creature stats were rescaled ×500 (HP up to 10000 for strongholds), but several keyword constants
were not:

| Constant | Current value | Comparable scale | Where |
|---|---|---|---|
| `wardhp` default in `mkCre` | `2` | should be ~1000 | `06_mana_workers.js:91` |
| `wardhp` default in `kwText` | `2` | ~1000 | `06:180` |
| `reap` default in `onCreatureDeath` | `1` | ~500 | `06:131` |
| Overcharge bank cap | `3` (added straight to attack) | ~1500 | `06:156,160` |
| `bldEffectText` says Longhouse trains a "0/2 ⚒" minion | worker HP is actually 1000 | | `18_inspect_viewers.js:23` |
| `aiChooseInterceptors` threshold `P >= 4` | attack values are ≥500 — this is always true | | `15_combat.js:74` |

The registry values are all correctly scaled; only the **fallback defaults** are stale. Because every
shipping card sets `wardhp`/`reap` explicitly, only Overcharge and the AI threshold change behaviour
today.

### 11.3 Stale rules text — two structure effects do nothing

* **`eff:'villager'` (Longhouse, Barracks).** `buildingUpkeep` (`17_turns_ai.js:2-11`) has branches
  for `mana`, `damage`, `revive` — **there is no `villager` branch**. `trainVillager`
  (`14_spells_traps.js:128`) is never called (its only reference is the FX wrapper at
  `22_fx_wrappers.js:212`). The worker contribution comes entirely from `sup` (3 and 4). The
  descriptions ("trains a Worker each turn", `03:57,66`) and inspect text (`18:23`) are stale.
* **`eff:'wall'` (Bulwark, Bastion).** `untappedInterceptors` (`15_combat.js:15-19`) only collects
  `kind==='creature'` units and worker stacks — **a structure can never intercept**. The
  "screens the line / can intercept" text (`03:59`, `18:24`) is stale; a Bulwark is purely HP + `sup`.

Both should be either implemented or re-worded in the port; do not port the text as-is.

### 11.4 Other confirmed inconsistencies

1. **Scour blocking-bypass is per-attacker in v3 (`15:251`) but group-wide in the legacy/MP paths
   (`16:65`, `42:223`).** Unify.
2. **`raze` ignores HP entirely.** A ◆3 spell destroys a 9000-HP Bastion. Intentional? It is the only
   unconditional destruction effect in the game.
3. **`chain` may not damage the card you clicked.** The click only picks the *side*; damage always
   goes to that side's top two creatures by attack.
4. **`bounce` on an Entrench unit still consumes the spell** (returns `true`, `14:21`).
5. **Summon traps ignore `effect`** — see §7.4.
6. **`springAttackTrap` never fires against a wall / worker-stack / face-down / trap target.**
7. **`thornmail` gives permanent `+500/+1000`** with no source tracking (`15:115`, `30:96`); it stacks
   if multiple Overgrowths spring on the same creature over several turns.
8. **`provokeFaceDown` on an under-funded charge destroys it with no combat**, and the invested ◆ is
   lost — a hard tempo blowout worth calling out in the rules text.
9. **No deck-out loss.** Drawing from an empty deck is a no-op (`17:80`).
10. **`mana` is capped at 99** on gain (`17:5`, `16:184`, `17:160`, `15:158`) but not by the vault drain, which
    clamps to `vaultCap`.

### 11.5 Dead / vestigial identifiers (do NOT port)

| Identifier | Where | Note |
|---|---|---|
| `SPELL` | `03:97` | Compat shim map; nothing reads it |
| `DIVINE` | `03:22` | Declared, never referenced |
| `WORKER` | `03:25` | Only used to register a placeholder; `mkVil` re-inlines the numbers |
| `mkCC`, `findCC` | `04:23,25` | Command centers removed |
| `TRIBES` contains `'Human'` | `03:27` | No card uses it |
| `ward` field | `06:91` | Copied everywhere, set by nothing, read by nothing |
| `target` field on spells | `03:82…` | Copied by `drawCard`, never read |
| `artBase`, `fieldBase`, `spritePath`, `SPRITE_DIR/EXTS` | `04:82,122,113,108` | Superseded by `_fieldart` |
| `colorNeed`, `extractColors`, `manaGlyph`, `canExtract` | `06:6,11,10`, `12:408` | Colored-mana leftovers; all return constants |
| `trainVillager` | `14:128` | Unreachable (§11.3) |
| `handcardFromCreature` drops `token` | `06:112` | **A bounced Shade/Lumen becomes a permanent ◆0 hand card.** Real exploit — filter tokens in `bounce` (Undertow already does, `06:137`). |

---

## 12. The machine-readable export — `tools/export_cards.mjs` → `docs/unity/spec/cards.json`

**This is a FULL DYNAMIC EXPORT, not a static parse.** The tool concatenates
`01_core_defs.js`, `02_art.js`, `03_cards_creatures.js`, `04_cards_leaders.js`, `06_mana_workers.js`
into one script and evaluates it in a `node:vm` context with a minimal DOM stub
(`document`, `Image`, `localStorage`, `matchMedia`), then reads the resulting globals. Derived data
(dual-commander HP/workers, per-element forge names and descriptions, `CARD_REG` keys, art probe
orders) is therefore **exactly what the game computes at runtime** — there is no transcription risk.

One implementation note: top-level `const` in a classic script lives in the *global lexical scope*,
not on `window`, so the tool appends an epilogue that publishes the needed bindings onto `window`
before reading them (`tools/export_cards.mjs`, `EXPORT_NAMES`).

Run: `node tools/export_cards.mjs` (add `--no-art` to omit the inline placeholder data URIs;
`--out <path>` to redirect).

### Verified output — 352.3 KB, complete

| Bucket | Count |
|---|---|
| Elements | **9** (8 deckable + divine) |
| Commanders | **36** (8 solo + 28 dual) |
| Creatures (deckable) | **64** — fire 8, water 8, earth 8, wind 8, forest 8, electric 8, light 8, dark 8 |
| Divine creatures | **4** |
| Spells + traps | **14** (9 castable spells + 5 traps) |
| Structure defs (`STRUCT_DEFS`) | **13** |
| Generated forges | **18** (`forgeDef` + `grandForgeDef` × 8 colours, plus the 2 unreachable divine variants) |
| Deck registry entries (`CARD_REG`) | **78** (64 creatures + 14 spells) |
| Tokens (Worker, Lumen, Shade) | **3** |

Top-level keys: `rules`, `counts`, `elements`, `keywords`, `commanders`, `creatures`, `divine`,
`spells`, `structures`, `forges`, `worker`, `tokens`, `deckRegistry`.
Every card entry carries all template fields (explicit `null` for absent optionals) **plus** derived
art data: `slug`, the full ordered `cardArtUrls` and `fieldArtUrls` probe lists, and `spriteBase`.
`rules` carries `DECK_SIZE`, `MAX_COPIES`, `MAX_DECKS`, `DECKS_KEY`, `SLOTS`, `CENTER_LANES`,
`BASE_COL`, the art directory/extension constants, `TRIBES`, `SUBTYPES`, `FORGE_NAMES`, and the two
save-migration alias maps.

Suggested Unity import path: a one-shot editor script reads `cards.json` and generates
`CreatureCardSO` / `SpellCardSO` / `StructureDefSO` / `CommanderDefSO` assets, keyed by `nm`.
Re-run the exporter whenever the JS registry changes until the JS is retired.
