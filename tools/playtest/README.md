# Playtest harness

Plays whole matches of the **living JS game** headlessly, through the same functions a tapping
human reaches, and writes down what happened. It exists to answer design questions with numbers:
which playstyle wins, which element wins, what a rule change is worth, and what breaks.

It is a different errand from `tools/diffjs/` — that one re-expresses the rules owner-generically
so a C# trace can be replayed against them. Here nothing is re-expressed: the pilot calls
`doHarvest`, `doDraw`, `place`, `placeBuild`, `castSpell`, `doMove`, `CMB.declare/resolve` and
`endTurn`, and the opponent is the shipped `foeTurn` AI. Only the four places the game **stops and
asks a human** are replaced with policy callbacks — `askBlock`, `askAbsorb`/`askRetaliate`,
`playerTrapOnSummon`, `RESP.defendWindow`. Those four answers *are* the playstyle.

Three stubs buy the speed and change no rule: `setTimeout` collapses to 0, `render()` becomes a
no-op (it is pure view — 12_render.js), and `log()` goes to an array instead of the DOM. Pass
`--live-render` to put the view layer back and catch crashes in it.

## Run it

```bash
node tools/playtest/run.mjs --matrix smoke --verbose        # one match per playstyle, ~2 s
node tools/playtest/run.mjs --matrix grid --seeds 3         # every playstyle x every matchup, ~5 min
node tools/playtest/run.mjs --matrix full --seeds 3         # adds dual-element and mirror decks
node tools/playtest/run.mjs --matches 8 --live-render       # with the real render(), for view crashes
node tools/playtest/analyze.mjs tools/playtest/out/grid.json
node tools/playtest/experiments.mjs --seeds 3               # A/B: one rule changed per variant
```

Flags: `--seeds N` (samples per cell) · `--turn-cap N` (rounds before a match is called a timeout)
· `--out FILE` · `--reboot-every N` (fresh jsdom realm every N matches) · `--matches N` (truncate).

Every match is seeded, so `probe-log.mjs <persona> <youCC> <foeCC> <seed>` replays any result
line-for-line and prints the narrated log.

## The eight playstyles (`pilot.mjs`)

| pilot | economy | what it attacks |
|---|---|---|
| `aggro` | 3 structures, cheap creatures early | the wall, always |
| `wallrush` | 3 structures, biggest creatures | the wall, only ever the wall |
| `balanced` | foundry → camp → forge → longhouse → outpost | kills what it can kill, else the wall |
| `multiattack` | same as balanced | the whole army onto one target every turn |
| `turtle` | the full build list, upgrades, face-downs, traps, banked ◆ | only from turn 14, only good trades |
| `sapper` | midrange | enemy structures first, always |
| `raider` | midrange | the enemy worker stacks |
| `hunter` | control deck | enemy creatures only, never the wall |

Deck shapes (`makeDeck`) bias the curve to match: `aggro`, `midrange`, `control`, or `random`
(exactly what `deckOf()` deals).

## What comes out

`out/<name>.json` — one record per match: winner, plies, per-turn snapshots (life, mana, workers,
structures, creatures, who is standing in whose half), every action the pilot took, a digest read
out of the narrated log (tower shots, mana drained, revives, wall hits, deaths), and any invariant
violation. `out/<name>.<persona>.log` — one full readable match per playstyle.

Invariants are checked at every turn boundary from outside the pilot (`engine.mjs`
`checkInvariants`): mana and life in range, no dead unit left on the board, no unit in two cells,
creatures only in centre lanes and structures only off them, worker pools matching the derived
figure, `checkWin` firing when a life pool empties, and 40-card decks not multiplying.

## Probes

Small scripts that reproduce one thing exactly, for when a statistic needs a mechanism behind it:

* `probe-log.mjs` — the narrated log of any seeded match (`--from N --to N` to window it)
* `probe-blocking.mjs` — who may intercept: tapped creatures, harvested workers, the siege square
* `probe-token-bounce.mjs` — Riptide on a token leaves a permanent 0-cost card in hand
* `probe-lineage.mjs` — `longhouse` has no `from`, so build caps stop seeing the Encampment
* `probe-conserve.mjs` — watches a 40-card deck for cards appearing out of nowhere
