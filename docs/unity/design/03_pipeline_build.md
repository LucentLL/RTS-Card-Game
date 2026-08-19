# 03 — Data Pipeline, Project Setup, Build & Release

**Scope:** Unity project creation, Git/LFS policy, the card-data pipeline (JS registry → `cards.json`
→ ScriptableObjects), art import, Windows/Steam build, and test infrastructure.
**Target:** Unity **6000.5.5f1**, URP (Universal 3D template), PC/Steam first, mouse + keyboard.
**Companion specs:** `docs/unity/spec/01`–`09`. This document assumes the rules-core architecture
decided there (pure C#, no `UnityEngine`, deterministic, command-driven).

> Every path in this document is absolute or repo-root-relative from
> `C:/Users/mcgee/code/RTS-Card-Game`.

---

## 0. Preflight — verified facts about this machine and repo

These were checked, not assumed. They change several recommendations below.

| Fact | Value | Consequence |
|---|---|---|
| Unity editor installed | `C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe` | CLI paths below are literal, not placeholders |
| Installed player modules | `AndroidPlayer`, `WebGLSupport`, `windowsstandalonesupport` | Windows target present |
| Windows build variations | `win64_player_{development,nondevelopment}_mono`, win32, win_arm64 — **mono only** | ⚠ **IL2CPP is NOT installed.** See §7.1 |
| `unity/` directory | **does not exist** | Unity Hub will not hit the non-empty-folder refusal |
| Node / npm | v24.15.0 / 11.12.1 | The exporter and the art sync script run as-is |
| Git LFS | `git-lfs/3.7.1` installed | Available, but **not recommended** — §4 |
| `docs/unity/spec/cards.json` | **exists**, 356 KB, generated `tools/export_cards.mjs` | Verified complete — §5.1 |
| `assets/` total | 23 MB, 197 PNGs, 200 files | §4.3, §6 |
| `assets/cards/` art coverage | **partial** — 83 `_cardart`, 69 `_fieldart` | Importer must tolerate missing art — §6.5 |
| `.gitignore` Unity block | present but **uncommitted** working-copy edit | §4 refines it rather than re-adding it |

---

## 1. Unity project creation

### 1.1 The Hub dialog that is open right now

The dialog has **Project name = `RTS TCG`**, **Location = `C:\Users\mcgee`**. Unity Hub creates the
project at `<Location>\<Project name>`, so those values would produce `C:\Users\mcgee\RTS TCG` —
wrong drive location, wrong name, and a space in the path (which breaks several CLI invocations
later unless carefully quoted).

**Type exactly these two values instead:**

| Field | Value to type |
|---|---|
| **Project name** | `unity` |
| **Location** | `C:\Users\mcgee\code\RTS-Card-Game` |

That resolves to `C:\Users\mcgee\code\RTS-Card-Game\unity` — the locked target path.

**Also set in the same dialog:**

| Field | Value |
|---|---|
| Editor version | `6000.5.5f1` |
| Template | **Universal 3D** (URP) |
| Connect to Unity Cloud | **off** |
| Use Unity Version Control | **off** (this repo uses Git) |

> **Why the project is literally named `unity`, not `SpawnRowDuel`:** the folder name *is* the project
> name in Hub, and the locked decision is that the project lives at `<repo>/unity`. The shipped
> product name is set later in Player Settings (§7.2) and is what users see; the folder name is
> invisible to players.

### 1.2 If Unity Hub refuses the folder

Hub refuses to create a project in a directory that **already exists and is non-empty**. `unity/`
does not exist today, so this should not fire. If it does (e.g. a partial attempt left files behind):

**Option A — clear it (preferred, only when you are sure it holds nothing you want):**
```bash
# from the repo root, in Git Bash
rm -rf "C:/Users/mcgee/code/RTS-Card-Game/unity"
```
Then retry the dialog with the values from §1.1.

**Option B — create elsewhere and move.** Create the project as `RTSTCG` under
`C:\Users\mcgee\code` (no spaces), close the editor completely, then:
```bash
mv "C:/Users/mcgee/code/RTSTCG" "C:/Users/mcgee/code/RTS-Card-Game/unity"
```
Unity projects are fully relocatable — nothing in `Library/`, `ProjectSettings/` or the `.meta` files
stores an absolute path. Re-open via Hub → **Add** → browse to the new location. Delete `unity/Library/`
first if you want a clean reimport (it is regenerated and git-ignored anyway).

**Option C — create empty, then let Hub adopt it.** Not supported by Hub's *Create* flow; Hub's *Add*
flow requires an existing `ProjectSettings/ProjectVersion.txt`. Use A or B.

### 1.3 Immediately after creation — verification

```bash
cat "C:/Users/mcgee/code/RTS-Card-Game/unity/ProjectSettings/ProjectVersion.txt"
# expect: m_EditorVersion: 6000.5.5f1

ls "C:/Users/mcgee/code/RTS-Card-Game/unity"
# expect: Assets  Library  Logs  Packages  ProjectSettings  UserSettings

grep -n "render-pipelines.universal" \
  "C:/Users/mcgee/code/RTS-Card-Game/unity/Packages/manifest.json"
# expect a com.unity.render-pipelines.universal entry -> confirms URP, not built-in
```

If `render-pipelines.universal` is missing you picked the plain **3D** template rather than
**Universal 3D**. Delete and recreate — retro-fitting URP onto a Built-In project means converting
every material by hand and is not worth it on day one.

### 1.4 Packages to add / remove first

Edit `unity/Packages/manifest.json` (or use Window → Package Manager).

**Add:**
```jsonc
"com.unity.inputsystem": "1.14.2",   // PC-first: keyboard/gamepad. Spec 09 flags input as a ship blocker.
"com.unity.cinemachine": "3.1.4",    // two camera rigs replace fitBoard()  (spec 09 port risk)
"com.unity.test-framework": "1.5.1", // usually already present in Unity 6
"com.unity.ide.rider": "3.0.36"      // or com.unity.ide.visualstudio — whichever you use
```
Version numbers: take whatever Package Manager offers as *Verified/Recommended* for 6000.5.x; the
above are indicative, not pinned requirements. `packages-lock.json` is what actually pins them and
**must be committed**.

**Remove** (the URP template ships sample content you do not want in a shipping repo):
- `Assets/TutorialInfo/`
- `Assets/Scenes/SampleScene.unity` — replace with your own `Assets/Game/Scenes/Boot.unity`
- Any `Readme.asset` / `Assets/Settings/` samples you do not use (keep the URP asset + renderer!)

**Do not add Addressables yet.** With 197 PNGs and a single desktop platform, direct references from
the generated ScriptableObjects are simpler, synchronous, and remove a whole class of async bugs.
Revisit only if build size or memory becomes a real problem. (Spec 09 §7 suggests "addressables/Resources";
either satisfies its requirement, which is *"resolve art once at load into a ScriptableObject, not via
failed requests"* — §5 and §6 below satisfy that with direct references.)

### 1.5 Editor settings to change on day one

Project Settings → **Editor**:

| Setting | Value | Why |
|---|---|---|
| Asset Serialization → Mode | **Force Text** | `.asset` / `.unity` files become YAML → diffable, mergeable. **Non-negotiable** for the generated card assets in §5. |
| Version Control → Mode | **Visible Meta Files** | `.meta` files committed alongside assets |
| Enter Play Mode Settings | Enabled, both reloads **disabled** | Fast iteration; the rules core is pure so it has no static state to leak |
| Line Endings For New Scripts | **Unix** | Repo is mixed-platform-friendly; avoids CRLF churn |

Project Settings → **Player** → Other Settings:

| Setting | Value | Why |
|---|---|---|
| Api Compatibility Level | **.NET Standard 2.1** | Lets the *identical* rules-core `.cs` files compile in a plain `dotnet test` project (§8.3) |
| Allow 'unsafe' Code | off | Not needed |
| Active Input Handling | **Input System Package (New)** | Matches §1.4 |

---

## 2. Directory layout inside `unity/`

```
unity/
├── Assets/
│   ├── Game/
│   │   ├── Scenes/            Boot.unity, MainMenu.unity, Duel.unity, Campaign.unity
│   │   ├── Data/
│   │   │   ├── Cards/         ← GENERATED by the importer (§5). One .asset per card.
│   │   │   │   ├── Creatures/{Fire,Water,Earth,Wind,Forest,Electric,Light,Dark,Divine}/
│   │   │   │   ├── Spells/          (9 castable)
│   │   │   │   ├── Traps/           (5)
│   │   │   │   ├── Structures/      (13 static + 18 generated forges)
│   │   │   │   ├── Commanders/      (36)
│   │   │   │   └── Elements/        (9)
│   │   │   └── CardDatabase.asset   ← GENERATED index (§5.5)
│   │   ├── Art/
│   │   │   ├── Cards/         ← JUNCTION → <repo>/assets/cards   (§6). git-ignored.
│   │   │   └── UI/            Unity-only art (frames, icons, backs)
│   │   ├── Audio/             23 re-authored SFX clips (spec 09 §16)
│   │   └── Prefabs/
│   ├── Scripts/
│   │   ├── Rules/             PURE C#. asmdef has noEngineReferences: true. (§3)
│   │   ├── Data/              ScriptableObjects + ICardRegistry impl (§5.3)
│   │   ├── View/              MonoBehaviours, rendering, input
│   │   ├── Platform/          ISteamServices + Null/Steamworks impls (§7.5)
│   │   └── Editor/            Importer, build CLI, validators (§5.4, §7.4)
│   ├── Settings/              URP asset + renderer (from the template — keep)
│   └── Tests/
│       ├── EditMode/          Rules-core tests (§8.2)
│       └── PlayMode/          Smoke tests only
├── Packages/manifest.json, packages-lock.json     (committed)
├── ProjectSettings/                                (committed)
├── steam_appid.txt                                 (committed — §7.5)
└── Library/, Temp/, Logs/, UserSettings/, Build/   (ignored)
```

---

## 3. Assembly definitions

The asmdef layout is the **enforcement mechanism** for the locked "rules core has no
`UnityEngine` dependency" decision. `noEngineReferences: true` makes a violation a *compile error*,
not a code-review catch.

**`unity/Assets/Scripts/Rules/SpawnRowDuel.Rules.asmdef`**
```json
{
  "name": "SpawnRowDuel.Rules",
  "rootNamespace": "SpawnRowDuel.Rules",
  "references": [],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": true
}
```

**`unity/Assets/Scripts/Data/SpawnRowDuel.Data.asmdef`**
```json
{
  "name": "SpawnRowDuel.Data",
  "rootNamespace": "SpawnRowDuel.Data",
  "references": ["SpawnRowDuel.Rules"],
  "autoReferenced": true,
  "noEngineReferences": false
}
```

**`unity/Assets/Scripts/Editor/SpawnRowDuel.Editor.asmdef`**
```json
{
  "name": "SpawnRowDuel.Editor",
  "rootNamespace": "SpawnRowDuel.Editor",
  "references": ["SpawnRowDuel.Rules", "SpawnRowDuel.Data"],
  "includePlatforms": ["Editor"],
  "autoReferenced": true
}
```

**`unity/Assets/Tests/EditMode/SpawnRowDuel.Rules.Tests.asmdef`**
```json
{
  "name": "SpawnRowDuel.Rules.Tests",
  "rootNamespace": "SpawnRowDuel.Rules.Tests",
  "references": [
    "SpawnRowDuel.Rules",
    "SpawnRowDuel.Data",
    "SpawnRowDuel.Editor",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner"
  ],
  "includePlatforms": ["Editor"],
  "precompiledReferences": ["nunit.framework.dll"],
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "autoReferenced": false
}
```

`SpawnRowDuel.View` and `SpawnRowDuel.Platform` follow the Data pattern (reference Rules + Data;
View additionally references Platform). **View must never be referenced by Rules or Data.**

---

## 4. Git, `.gitignore`, and the LFS decision

### 4.1 Collision analysis against the existing `.gitignore` — verified with `git check-ignore`

The repo's pre-existing rules are **unanchored** (no leading `/`), so Git matches them at *every*
depth. Actual measured results:

| Path | Result | Matched by |
|---|---|---|
| `unity/Assets/Art/build/x` | **IGNORED** ⚠ | `.gitignore:19: build/` |
| `unity/Assets/Art/dist/x` | **IGNORED** ⚠ | `.gitignore:18: dist/` |
| `unity/Build/Win64/Game.exe` | IGNORED ✓ (intended) | `.gitignore:38: unity/[Bb]uild/` |
| `unity/Logs/a.log` | IGNORED ✓ | `unity/[Ll]ogs/` |
| `unity/Assets/Scripts/Foo.cs` | tracked ✓ | — |
| `unity/ProjectSettings/ProjectVersion.txt` | tracked ✓ | — |
| `unity/Packages/manifest.json` | tracked ✓ | — |
| `assets/cards/x.png.meta` | tracked ✓ | — (matters for §6) |
| `unity/Assets/StreamingAssets/aa/x.bundle` | tracked ⚠ | should be ignored if Addressables is ever added |

**Two live collisions, both inside `Assets/`:**

1. **`dist/` (line 18)** and **`build/` (line 19)** silently swallow any folder of those names
   *anywhere*, including inside `unity/Assets/`. An ignored folder inside `Assets/` is genuinely
   dangerous in Unity: the editor still imports it and writes `.meta` files, but Git tracks neither
   the assets nor their metas, so a fresh clone opens the project with dangling references and
   regenerated (different) GUIDs.
2. **`*.exe` (line 21)** is unanchored — harmless for player output (already under an ignored
   `unity/Build/`), but it will silently drop any tool executable you later try to commit.

**Fix — do not touch lines 18/19 (the HTML build depends on them); add a negation guard instead** so
nothing inside `Assets/` can ever be accidentally ignored, and adopt the convention **never name a
Unity output folder `dist` or `build`** (the Unity build target below is `unity/Build/`, which is
*explicitly* ignored by an anchored rule — that is intended and fine).

### 4.2 The `.gitignore` block

The working copy already carries a Unity block (uncommitted). Replace it wholesale with the version
below — it fixes the collisions, adds the missing standard entries, and adds the art junction. **Leave
lines 1–30 of the existing file (including `.claude/`) untouched.**

```gitignore
# ─────────────────────────────────────────────────────────────
# Unity project (unity/) — Unity 6000.5.5f1, URP
# All rules are ANCHORED to unity/ so the HTML build's own dist//build/ rules
# (lines 18-19, unanchored by design) stay independent.
# ─────────────────────────────────────────────────────────────
unity/[Ll]ibrary/
unity/[Tt]emp/
unity/[Oo]bj/
unity/[Bb]uild/
unity/[Bb]uilds/
unity/[Ll]ogs/
unity/[Uu]ser[Ss]ettings/
unity/[Mm]emoryCaptures/
unity/[Rr]ecordings/
unity/[Ee]xported[Oo]bj/
unity/[Ll]ocal[Cc]ache*/

# Asset folders Unity itself treats as disposable
unity/[Aa]ssets/AssetStoreTools*
unity/[Aa]ssets/Plugins/Editor/JetBrains*

# Addressables build output (only if the package is ever added)
unity/[Aa]ssets/[Ss]treaming[Aa]ssets/aa*
unity/[Aa]ssets/AddressableAssetsData/*/*.bin*

# Card art is a junction/symlink to <repo>/assets/cards — the real files (and
# their .meta files) are tracked at assets/cards/. Tracking the link too would
# duplicate all 18 MB, because Git recurses into Windows junctions.
# The folder's own sidecar (unity/Assets/Game/Art/Cards.meta) IS tracked.
unity/[Aa]ssets/[Gg]ame/[Aa]rt/[Cc]ards/

# Generated IDE / solution files — Unity regenerates these on demand
unity/.vs/
unity/.gradle/
unity/*.csproj
unity/*.unityproj
unity/*.sln
unity/*.suo
unity/*.tmp
unity/*.user
unity/*.userprefs
unity/*.pidb
unity/*.booproj
unity/*.svd
unity/*.pdb
unity/*.mdb
unity/*.opendb
unity/*.VC.db

# Unity generated crash / profiler output
unity/sysinfo.txt
unity/crashlytics-build.properties
unity/*.stackdump

# Built player output
unity/*.apk
unity/*.aab
unity/*.unitypackage
unity/*.app

# ── SAFETY NET ───────────────────────────────────────────────
# Lines 18-19 (dist/ and build/) are UNANCHORED and would otherwise swallow any
# folder of those names inside unity/Assets/, which breaks Unity badly (assets
# imported but untracked -> GUIDs regenerate on a fresh clone). Re-include them.
!unity/[Aa]ssets/**/dist/
!unity/[Aa]ssets/**/build/

# ── Steam ────────────────────────────────────────────────────
# steam_appid.txt IS committed (needed for editor play). Steamworks SDK
# redistributables that the package pulls in are not.
unity/[Ss]team[Ll]ibs/
sdk/                      # Steamworks SDK drop, if you unzip it into the repo
```

Also add, near the existing Node section, so a future `tools/package-lock.json` is kept but junk is not:
```gitignore
tools/node_modules/
```
(`node_modules/` on line 12 is unanchored and already covers this — listed only for clarity; adding it
is optional.)

**Verified behaviour.** The block above was tested in a scratch repo against the repo's real lines
1–30 using `git check-ignore -v`. All 20 probe paths behaved as designed:

| Path | Result | Why it matters |
|---|---|---|
| `unity/Assets/Game/Art/Cards/x.png` | **IGNORED** ✓ | The junction body is not duplicated into Git |
| `unity/Assets/Game/Art/Cards.meta` | tracked ✓ | The junction folder's own sidecar must be committed |
| `assets/cards/…/magmaw_cardart.png` | tracked ✓ | The real bytes stay tracked once, at the real path |
| `assets/cards/…/magmaw_cardart.png.meta` | **tracked** ✓ | **Load-bearing** — this is what makes GUIDs survive a fresh clone (§6.3) |
| `unity/Assets/Game/Data/Cards/*.asset` + `.meta` | tracked ✓ | Generated card assets are committed |
| `unity/Assets/Art/dist/x.png` | tracked ✓ | Negation defeats the unanchored line 18 |
| `unity/Assets/Art/build/y.png` | tracked ✓ | Negation defeats the unanchored line 19 |
| `unity/Build/Win64/Game.exe` | IGNORED ✓ | Player output, correctly excluded |
| `unity/Library/x`, `unity/*.sln` | IGNORED ✓ | Regenerated artefacts |
| `unity/Packages/{manifest,packages-lock}.json`, `unity/ProjectSettings/*`, `unity/steam_appid.txt` | tracked ✓ | Required for a reproducible open |
| `unity/Assets/StreamingAssets/aa/x.bundle` | IGNORED ✓ | Addressables output, if ever added |
| `tests/**/obj/` ignored, `tests/**/*.cs` tracked | ✓ | §8.3 |
| `dist/spawn-row-duel.html` | IGNORED ✓ | **No regression** to the HTML build's own rules |

**Files that must stay tracked — verify after the first commit:**
```bash
git status --short unity/ | head -40
# Expect Assets/**/*.cs, Assets/**/*.meta, ProjectSettings/*, Packages/manifest.json,
# Packages/packages-lock.json  — and NO Library/, Temp/, Logs/, .csproj, .sln
```

### 4.3 Git LFS — recommendation: **DO NOT use LFS.** ❌

This is a firm no, for one decisive reason plus three supporting ones.

**Decisive: GitHub Pages does not resolve Git LFS objects.** A file stored in LFS is committed to the
repo as a ~130-byte pointer file:
```
version https://git-lfs.github.com/spec/v1
oid sha256:4d7a2...
size 481920
```
GitHub Pages serves the **pointer text**, not the PNG. Moving `assets/**/*.png` into LFS would break
every card image on <https://lucentll.github.io/RTS-Card-Game/> instantly. Per the project's own
notes, that Pages URL is the **only** mobile test surface for the HTML build. LFS here would take out
the primary QA channel to save 18 MB.

Supporting reasons:

1. **The repo is nowhere near needing it.** 23 MB of art total; GitHub's soft warning starts at 1 GB
   and the per-file hard limit is 100 MB (largest PNG here is a fraction of that). LFS solves a
   problem this repo does not have.
2. **LFS has a real bandwidth quota** (1 GB/mo free). CI checkouts and clones burn it; exceeding it
   *blocks pushes and fetches repo-wide* until you buy more. That is a hard failure mode traded for a
   non-problem.
3. **It adds a required install step for every clone.** `git clone` without `git-lfs` installed
   silently yields pointer files. Combined with the art junction in §6, a developer would get a Unity
   project full of 130-byte "PNGs" and a very confusing importer failure.

**Revisit LFS only if all three become true:** (a) the repo exceeds ~750 MB, (b) the large files live
under a path GitHub Pages never serves (e.g. `unity/Assets/Game/Art/Source/**` for layered PSD/PSB
masters or WAV stems), and (c) the HTML build is retired or no longer served from this repo. If you do
adopt it then, scope it narrowly — never `assets/`:

```gitattributes
# ONLY IF §4.3's three conditions are met. Never add assets/ here.
unity/Assets/Game/Art/Source/**/*.psd  filter=lfs diff=lfs merge=lfs -text
unity/Assets/Game/Audio/Source/**/*.wav filter=lfs diff=lfs merge=lfs -text
```

### 4.4 `.gitattributes` — add this now (this is the part that actually matters)

Unity YAML files merge badly with default Git settings, and CRLF churn on `.meta` files creates
noisy diffs on a Windows machine. Create `C:/Users/mcgee/code/RTS-Card-Game/.gitattributes`:

```gitattributes
* text=auto eol=lf

# Unity text assets: force LF, and never let Git's rename/merge heuristics mangle them.
*.cs        text eol=lf diff=csharp
*.meta      text eol=lf -merge
*.unity     text eol=lf -merge
*.asset     text eol=lf -merge
*.prefab    text eol=lf -merge
*.mat       text eol=lf -merge
*.controller text eol=lf -merge
*.asmdef    text eol=lf
*.json      text eol=lf

# Binaries — never touch, never diff.
*.png  binary
*.jpg  binary
*.jpeg binary
*.webp binary
*.wav  binary
*.ogg  binary
*.dll  binary
*.7z   binary
```

`-merge` on Unity YAML means a conflict leaves the file untouched rather than producing an
unopenable half-merged scene. (If you later work with a second person, add Unity's *Smart Merge*
`UnityYAMLMerge` as the merge driver for `*.unity`/`*.prefab`.)

### 4.5 First commit sequence

```bash
cd "C:/Users/mcgee/code/RTS-Card-Game"
git checkout -b unity-port

# 1. tooling + spec, no Unity project yet
git add .gitignore .gitattributes docs/unity tools/export_cards.mjs
git commit -m "Unity port: spec, card export tool, Unity gitignore/gitattributes"

# 2. create the project via Hub (§1), then:
git add unity/
git status --short unity/ | wc -l     # sanity: hundreds, not tens of thousands
git commit -m "Unity 6000.5.5f1 URP project skeleton at unity/"
```

If step 2 stages tens of thousands of files, `unity/Library/` is leaking — re-check §4.2 landed
before you staged.

---

## 5. Card data pipeline

### 5.1 The flow

```
src/js/{01_core_defs,02_art,03_cards_creatures,04_cards_leaders,06_mana_workers}.js
      │  (the ONLY source of truth for card data — hand-edited by the designer)
      │
      │  node tools/export_cards.mjs          [EXISTS — dynamic node:vm evaluation]
      ▼
docs/unity/spec/cards.json                     [EXISTS — 356 KB, verified complete]
      │
      │  Unity menu: Tools ▸ Spawn Row Duel ▸ Import Cards          [TO BUILD — §5.4]
      │  CI:         -executeMethod CardImportCli.Verify
      ▼
unity/Assets/Game/Data/Cards/**/*.asset        [GENERATED, committed, diff-friendly]
unity/Assets/Game/Data/CardDatabase.asset      [GENERATED index, committed]
      │
      │  CardDatabase implements ICardRegistry (pure interface, lives in Rules)
      ▼
SpawnRowDuel.Rules  — consumes plain records, never touches UnityEngine
```

**Direction is one-way.** `cards.json` is generated, never hand-edited. The `.asset` files are
generated, never hand-edited. To change a card you edit the JS, re-run the exporter, re-run the
importer, and commit all three layers together. §5.7 describes the CI guard that enforces this.

> **Why keep the JS as the source of truth at all?** Because the HTML build must keep working
> (Pages is the mobile test surface) and it reads the JS registry directly. Two independent card
> databases would drift within a week. When the HTML build is eventually retired, invert the flow:
> make the `.asset` files authoritative and generate `cards.json` from Unity for any remaining web use.

### 5.2 What `cards.json` actually contains (verified)

Top-level keys, with measured counts:

| Key | Shape | Count | Notes |
|---|---|---|---|
| `$schemaNote`, `generatedAt`, `sourceFiles`, `artIncluded` | provenance | — | `generatedAt` changes every run → see §5.7 |
| `rules` | object | — | `DECK_SIZE:40`, `MAX_COPIES:3`, `MAX_DECKS:5`, `SLOTS:7`, `CENTER_LANES:[1,3,5]`, `BASE_COL:3`, art dirs/exts, `TRIBES`, `SUBTYPES`, `FORGE_NAMES`, `COLOR_ALIAS`, `CC_ALIAS` |
| `counts` | object | — | Self-describing totals — the importer asserts against these |
| `elements` | array | **9** | `id,name,glyph,color,accent,deep,bg[3],hp,wk,lore,deckable,cssClass` |
| `keywords` | array | **8** | `{id, inspectText}` — text is HTML, regenerate it in C# (spec 09) |
| `commanders` | array | **36** | `id,name,hp,wk,colors[],desc,dual,buildList[]` (buildList entries like `"forge:fire"`) |
| `creatures` | array | **64** | full template + `slug`, `cardArtUrls[8]`, `fieldArtUrls[6]`, `spriteBase` |
| `divine` | array | **4** | same shape; **not deckable**, flat art paths only |
| `spells` | array | **14** | 9 castable + 5 traps; `effect`, `val`, `trap`, `trigger`, `ic` |
| `structures` | array | **13** | `bid,nm,c,h,eff,val,sup,ic,prereq[],from,up2[],row,color,desc,buildable` |
| `forges` | array | **18** | generated `forge:<el>` / `grandforge:<el>` × 9 (incl. unreachable divine) |
| `worker` | object | 1 | the Worker token template |
| `tokens` | array | 2 | Lumen / Shade — **descriptive only**, stats derive from the creating keyword |
| `deckRegistry` | array | **78** | `CARD_REG` = 64 creatures + 14 spells |

Every card entry carries `art` — a **placeholder SVG data URI**, and roughly 250 KB of the file's
356 KB. Spec 06 §9.5 and spec 09 §7 both say to delete these in the port. **The importer ignores the
`art` field entirely.** (`tools/export_cards.mjs --no-art` also exists and shrinks the file to
~100 KB; keep the full file as the reference export and just ignore the field — one file, one truth.)

### 5.3 `CardDefinition` — the ScriptableObject

One SO type with a `CardKind` discriminator, rather than four sibling types. Rationale: the deck
builder, the hand, the graveyard and the MP snapshot all hold heterogeneous card lists, and a single
type keeps `List<CardDefinition>` working without abstract-class serialization gymnastics. Fields not
relevant to a kind stay at their defaults and are hidden by a custom inspector.

`unity/Assets/Scripts/Data/CardDefinition.cs`:

```csharp
using System;
using UnityEngine;
using SpawnRowDuel.Rules;                 // enums + plain records live in the pure assembly

namespace SpawnRowDuel.Data
{
    public enum CardKind { Creature, Spell, Structure, Commander, Element, Token }

    [CreateAssetMenu(menuName = "Spawn Row Duel/Card Definition", fileName = "card")]
    public sealed class CardDefinition : ScriptableObject
    {
        // ── identity ────────────────────────────────────────────────────────
        // ExportKey is the row's `key` from cards.json ("fire|Magmaw", "forge:fire",
        // "foundry", "light_dark"). It is the importer's primary key and MUST be stable.
        [SerializeField] string exportKey;
        [SerializeField] CardKind kind;
        [SerializeField] string displayName;      // == nm. Identity in the JS; keep it exact.
        [SerializeField] string slug;             // slugify(nm) — art lookup key
        [SerializeField] Element element;         // Element.None for neutral spells/traps
        [SerializeField] bool isNeutral;          // true when the JS color was null
        [SerializeField] bool isPlayable = true;  // false for divine + unreachable forges

        // ── shared ──────────────────────────────────────────────────────────
        [SerializeField] int cost;                // c

        // ── creature ────────────────────────────────────────────────────────
        [SerializeField] int attack;              // a
        [SerializeField] int health;              // h  (== maxh on instantiation)
        [SerializeField] int upkeep;              // up
        [SerializeField] bool firstStrike;        // fs — a FLAG, not a keyword
        [SerializeField] Keyword keyword;         // single-valued
        [SerializeField] int detonate, reap, wardHp, grow, hatch;
        [SerializeField] bool entrench;
        [SerializeField] Tribe tribe;
        [SerializeField] Subtype subtype;
        [SerializeField] HatchFormData into;      // Chrysalis target; null-equivalent when Name == ""

        // ── spell / trap ────────────────────────────────────────────────────
        [SerializeField] bool isTrap;
        [SerializeField] SpellEffect spellEffect;
        [SerializeField] int spellValue;          // val
        [SerializeField] TrapTrigger trapTrigger;
        [SerializeField] string glyph;            // ic — presentation

        // ── structure ───────────────────────────────────────────────────────
        [SerializeField] string buildId;          // bid
        [SerializeField] StructureEffect structEffect;
        [SerializeField] int structValue;         // val
        [SerializeField] int support;             // sup — MAY BE NEGATIVE (tower = -2)
        [SerializeField] string[] prereq;
        [SerializeField] string upgradedFrom;     // from  (null => buildable from the menu)
        [SerializeField] string[] upgradesTo;     // up2
        [SerializeField] RowGate rowGate;

        // ── commander ───────────────────────────────────────────────────────
        [SerializeField] int life;                // hp
        [SerializeField] int baseWorkers;         // wk
        [SerializeField] Element[] colors;
        [SerializeField] string[] buildList;      // "foundry", "forge:fire", ...

        // ── element (only when kind == Element) ─────────────────────────────
        [SerializeField] string glyphKanji, colorHex, accentHex, deepHex;
        [SerializeField] string[] bgStops;

        // ── flavour + art ───────────────────────────────────────────────────
        [TextArea(2, 5)] [SerializeField] string description;   // desc / lore
        [SerializeField] Sprite cardArt;    // resolved from slug at import (§6.5)
        [SerializeField] Sprite fieldArt;   // standee cut-out; null => borrow cardArt ("fromart")

        // ── public read-only surface ────────────────────────────────────────
        public string ExportKey  => exportKey;
        public CardKind Kind     => kind;
        public string DisplayName=> displayName;
        public string Slug       => slug;
        public Element Element   => element;
        public int Cost          => cost;
        public Sprite CardArt    => cardArt;
        public Sprite FieldArt   => fieldArt;
        public bool HasFieldArt  => fieldArt != null;

        /// Deck / save / (future) network key: "<color|'neutral'>|<nm>". Spec 06 §0 —
        /// this composite must stay stable for save-file compatibility.
        public string DeckKey => (isNeutral ? "neutral" : element.ToString().ToLowerInvariant())
                                 + "|" + displayName;

        // ── projections into the PURE rules assembly ────────────────────────
        // The rules core never sees a ScriptableObject. These build plain records.
        public CreatureCard ToCreatureCard() => new CreatureCard {
            Id = displayName, Name = displayName, Element = element,
            Cost = cost, Attack = attack, Health = health, Upkeep = upkeep,
            FirstStrike = firstStrike, Entrench = entrench, Keyword = keyword,
            Detonate = detonate, Reap = reap, WardHp = wardHp, Grow = grow, Hatch = hatch,
            Into = into != null && !string.IsNullOrEmpty(into.Name) ? into.ToRecord() : null,
            Tribe = tribe, Subtype = subtype,
        };

        public SpellCard ToSpellCard() => new SpellCard {
            Id = displayName, Name = displayName, Cost = cost,
            IsTrap = isTrap, Effect = spellEffect, Value = spellValue, Trigger = trapTrigger,
        };

        public StructureDef ToStructureDef() => new StructureDef {
            Bid = buildId, Name = displayName, Cost = cost, Health = health,
            Effect = structEffect, Value = structValue, Support = support,
            Prereq = prereq, From = upgradedFrom, UpgradesTo = upgradesTo,
            Row = rowGate, Color = isNeutral ? (Element?)null : element, Description = description,
        };

        public CommanderDef ToCommanderDef() => new CommanderDef {
            Id = exportKey, Name = displayName, Lore = description,
            Hp = life, Workers = baseWorkers, Colors = colors,
        };

#if UNITY_EDITOR
        /// Importer-only mutation surface. Internal so runtime code cannot write card data.
        internal void __EditorApply(Action<CardDefinition> mutate) => mutate(this);
#endif
    }

    [Serializable]
    public sealed class HatchFormData
    {
        public string Name; public int Attack, Health;
        public int Upkeep = -1;            // -1 == "inherit"
        public int FirstStrike = -1;       // -1 == inherit, 0 == false, 1 == true
        public Keyword Keyword = Keyword.None;
        public HatchForm ToRecord() => new HatchForm {
            Name = Name, Attack = Attack, Health = Health,
            Upkeep  = Upkeep     >= 0 ? Upkeep : (int?)null,
            FirstStrike = FirstStrike >= 0 ? FirstStrike == 1 : (bool?)null,
            Keyword = Keyword != Keyword.None ? Keyword : (Keyword?)null,
        };
    }
}
```

### 5.4 The importer

**Design decision: a menu-item / CLI generator writing one `.asset` per card — NOT a
`ScriptedImporter`, and NOT an `AssetPostprocessor`.**

| Approach | Verdict |
|---|---|
| `ScriptedImporter` on `cards.json` | Rejected. It would require `cards.json` to live *inside* `Assets/` (duplicating it), and it produces sub-assets whose GUIDs are `mainGuid + localFileID` — meaning the whole database is one binary-ish blob in Git with no per-card diffs. Fails the "diff-friendly" requirement. |
| `AssetPostprocessor` | Rejected as the *trigger*. `cards.json` lives at `docs/unity/spec/` — outside `Assets/` — so no asset event ever fires. A tiny `AssetPostprocessor` is still used for one thing: re-linking art sprites when a PNG is (re)imported (§6.5). |
| **Menu item + static CLI entry point** | **Chosen.** Explicit, scriptable from CI, and writes one YAML `.asset` per card so `git diff` shows exactly which card's stats changed. |

**Idempotency mechanics (this is the whole design):**

1. **Deterministic path from a deterministic key.** `AssetPathFor(row)` maps the export `key` to a
   fixed path: `fire|Magmaw` → `Assets/Game/Data/Cards/Creatures/Fire/fire__magmaw.asset`,
   `forge:fire` → `.../Structures/forge__fire.asset`, `light_dark` → `.../Commanders/light_dark.asset`.
2. **Load-then-mutate, never delete-then-create.** `AssetDatabase.LoadAssetAtPath<CardDefinition>(path)`
   returns the existing object; we write fields into *that instance*. The `.meta` file — and therefore
   the **GUID — is never touched**. Every prefab/scene reference survives a re-import. Only when the
   load returns `null` do we `CreateInstance` + `CreateAsset`.
3. **Change detection before dirtying.** Serialize the object to JSON before and after the field
   writes (`EditorJsonUtility.ToJson`) and only call `EditorUtility.SetDirty` when they differ. A
   no-op import touches zero files → `git status` stays clean → re-running the importer is safe and
   free.
4. **Stable ordering.** Rows are sorted by `key` (ordinal) before processing, and the `CardDatabase`
   index array is written in that same order, so the index asset's YAML diff is minimal and
   deterministic.
5. **Orphan reporting, never silent deletion.** Everything under the generated root that was not
   produced by this run is listed in the console and — only with the `Import Cards (prune orphans)`
   menu variant, or `--prune` on the CLI — deleted. A silent delete would destroy hand-linked art.
6. **Fail loud on unknown enum strings.** `ParseEnum` throws with the offending value and card key
   rather than defaulting to `None`. Spec 06's biggest port risk is silently-wrong data; a card whose
   `kw` was typo'd must break the import, not ship as a vanilla creature.

`unity/Assets/Scripts/Editor/CardImporter.cs` (sketch — the load-bearing parts, elided where
mechanical):

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using SpawnRowDuel.Rules;
using SpawnRowDuel.Data;

namespace SpawnRowDuel.Editor
{
    public static class CardImporter
    {
        const string GeneratedRoot = "Assets/Game/Data/Cards";
        const string DatabasePath  = "Assets/Game/Data/CardDatabase.asset";

        /// cards.json lives OUTSIDE Assets/ on purpose: one copy, and Unity never
        /// imports a 356 KB TextAsset it does not need at runtime.
        public static string CardsJsonPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/unity/spec/cards.json"));

        [MenuItem("Tools/Spawn Row Duel/Import Cards from cards.json %#i")]
        public static void ImportMenu() => Run(prune: false, dryRun: false);

        [MenuItem("Tools/Spawn Row Duel/Import Cards (prune orphans)")]
        public static void ImportPruneMenu()
        {
            if (EditorUtility.DisplayDialog("Prune orphans?",
                    "Deletes generated card assets that no longer appear in cards.json. " +
                    "References to them will break. Continue?", "Prune", "Cancel"))
                Run(prune: true, dryRun: false);
        }

        public static ImportReport Run(bool prune, bool dryRun)
        {
            var report = new ImportReport();
            var json   = File.ReadAllText(CardsJsonPath, Encoding.UTF8);
            var root   = CardsJson.Parse(json);           // thin typed wrapper; see note below

            Validate(root, report);                        // §5.6 — throws on hard failures

            var expected = new HashSet<string>(StringComparer.Ordinal);

            try
            {
                AssetDatabase.StartAssetEditing();         // batch: ~10x faster, one refresh

                foreach (var row in EnumerateAllRows(root).OrderBy(r => r.Key, StringComparer.Ordinal))
                {
                    var path = AssetPathFor(row);
                    expected.Add(path);
                    UpsertOne(row, path, report, dryRun);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }

            // ---- orphans -------------------------------------------------------
            foreach (var guid in AssetDatabase.FindAssets("t:CardDefinition", new[] { GeneratedRoot }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (expected.Contains(p)) continue;
                report.Orphans.Add(p);
                if (prune && !dryRun) AssetDatabase.DeleteAsset(p);
            }

            if (!dryRun) UpsertDatabase(root, expected, report);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            report.Log();
            return report;
        }

        static void UpsertOne(CardRow row, string path, ImportReport report, bool dryRun)
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

            var so = AssetDatabase.LoadAssetAtPath<CardDefinition>(path);
            bool created = so == null;
            if (created)
            {
                so = ScriptableObject.CreateInstance<CardDefinition>();
                if (!dryRun) AssetDatabase.CreateAsset(so, path);   // GUID minted ONCE, here
            }

            // (3) change detection: snapshot -> mutate -> compare
            var before = EditorJsonUtility.ToJson(so);
            so.__EditorApply(c => Populate(c, row, report));
            var after  = EditorJsonUtility.ToJson(so);

            if (created)                 report.Created.Add(path);
            else if (before != after)  { report.Updated.Add(path); if (!dryRun) EditorUtility.SetDirty(so); }
            else                         report.Unchanged++;
        }

        static void Populate(CardDefinition c, CardRow r, ImportReport report)
        {
            // ... straight field copies (elided) ...
            // Enum parsing FAILS LOUDLY — never silently defaults:
            //   c.keyword       = ParseEnum<Keyword>(r.Kw, Keyword.None, r.Key, "kw");
            //   c.spellEffect   = ParseEnum<SpellEffect>(r.Effect, ..., r.Key, "effect");
            //   c.structEffect  = ParseEnum<StructureEffect>(r.Eff, ..., r.Key, "eff");
            //   c.trapTrigger   = ParseEnum<TrapTrigger>(r.Trigger, TrapTrigger.None, r.Key, "trigger");
            //
            // Art is resolved by slug from the junctioned folder — see §6.5. It is
            // assigned only when found, so a hand-linked override for a card whose
            // file is still missing is never clobbered:
            //   var art = ArtLinker.FindCardArt(r.Slug);
            //   if (art != null) c.cardArt = art;
            //   var field = ArtLinker.FindFieldArt(r.Slug);
            //   if (field != null) c.fieldArt = field;
        }

        static T ParseEnum<T>(string raw, T whenNull, string cardKey, string field) where T : struct
        {
            if (string.IsNullOrEmpty(raw)) return whenNull;
            if (Enum.TryParse<T>(raw, ignoreCase: true, out var v)) return v;
            throw new InvalidDataException(
                $"cards.json: card '{cardKey}' has unknown {field} value '{raw}'. " +
                $"Add it to enum {typeof(T).Name} or fix the JS registry — the importer will not guess.");
        }

        static string AssetPathFor(CardRow r) => r.Kind switch
        {
            CardKind.Creature  => $"{GeneratedRoot}/Creatures/{Cap(r.Element)}/{Safe(r.Key)}.asset",
            CardKind.Spell     => $"{GeneratedRoot}/{(r.IsTrap ? "Traps" : "Spells")}/{Safe(r.Key)}.asset",
            CardKind.Structure => $"{GeneratedRoot}/Structures/{Safe(r.Key)}.asset",
            CardKind.Commander => $"{GeneratedRoot}/Commanders/{Safe(r.Key)}.asset",
            CardKind.Element   => $"{GeneratedRoot}/Elements/{Safe(r.Key)}.asset",
            _                  => $"{GeneratedRoot}/Tokens/{Safe(r.Key)}.asset",
        };

        /// "fire|Magmaw" -> "fire_magmaw";  "forge:fire" -> "forge_fire";
        /// "light_dark" -> "light_dark" (already safe).
        /// Deterministic, lossy (both '|' and ':' fold to '_'), and therefore
        /// collision-checked by V3 in Validate() (§5.6) rather than trusted.
        static string Safe(string key) =>
            key.ToLowerInvariant()
               .Replace('|', '_').Replace(':', '_').Replace(' ', '_').Replace('/', '_');
    }
}
```

> **JSON parsing note.** `JsonUtility` cannot deserialize this document: it has no support for
> polymorphic `null` on value types, and `bg`/`prereq`/`up2` are heterogeneous-ish arrays. Use
> `Newtonsoft.Json` via the built-in **`com.unity.nuget.newtonsoft-json`** package (add to
> `manifest.json`) and deserialize into explicit DTO classes with `int?`/`string` fields so the
> JS `null` vs `0` distinction survives — it matters for `wardhp`, `reap`, `grow`, `hatch`, `val`.

### 5.5 `CardDatabase` — the index

```csharp
namespace SpawnRowDuel.Data
{
    [CreateAssetMenu(menuName = "Spawn Row Duel/Card Database")]
    public sealed class CardDatabase : ScriptableObject, ICardRegistry
    {
        [SerializeField] string sourceHash;        // SHA-256 of cards.json  (§5.7)
        [SerializeField] string sourceGeneratedAt; // provenance, informational only
        [SerializeField] CardDefinition[] all;     // sorted by ExportKey, ordinal

        // Rules constants lifted from cards.json `rules` so the core never hardcodes them.
        [SerializeField] int deckSize = 40, maxCopies = 3, maxSavedDecks = 5;
        [SerializeField] int boardSlots = 7, baseColumn = 3;
        [SerializeField] int[] centerLanes = { 1, 3, 5 };

        Dictionary<string, CardDefinition> _byDeckKey;   // built in OnEnable, ordinal comparer

        public IReadOnlyList<CreatureCard> Creatures { get; private set; }
        public IReadOnlyList<SpellCard>    Spells    { get; private set; }
        public IReadOnlyDictionary<string, StructureDef> Structures  { get; private set; }
        public IReadOnlyDictionary<string, CommanderDef> Commanders  { get; private set; }

        public StructureDef ResolveStructure(string bid, Element? color) => bid switch {
            "forge"      => Structures[$"forge:{color?.ToString().ToLowerInvariant()}"],
            "grandforge" => Structures[$"grandforge:{color?.ToString().ToLowerInvariant()}"],
            _            => Structures.TryGetValue(bid, out var d) ? d : null,
        };

        public CreatureCard ByDeckKey(DeckKey key) => _byDeckKey[key.ToString()].ToCreatureCard();

        void OnEnable() { /* project `all` into the four typed views, once */ }
    }
}
```

Note `sourceHash`: the SHA-256 of `cards.json` **excluding the `generatedAt` field** (which changes
on every export run and would otherwise make every re-export look like a data change). Compute it
over the parsed-then-re-serialized document with `generatedAt` removed.

### 5.6 Import-time validation — where the spec's port risks get caught

The importer runs these before writing anything and **throws** on a hard failure. Each maps directly
to a risk the extraction flagged. These same checks are mirrored as EditMode tests (§8.2) so they run
in CI even when nobody re-imports.

| # | Check | Guards against | Severity |
|---|---|---|---|
| V1 | Counts match the `counts` block exactly (9/36/64/4/14/13/18/78) | Truncated or partially-written export | **throw** |
| V2 | Every `slug` is unique across creatures+spells+structures+forges | Spec 06 §9.1 "slugs are not unique-checked" — a collision silently gives two cards the same art | **throw** |
| V3 | Every asset path from `Safe(key)` is unique | Two cards colliding onto one `.asset` file | **throw** |
| V4 | Every combat number (`a`, `h`, `det`, `reap`, `wardhp`, spell `val`) is **0 or a multiple of 500** | The incomplete ×500 rescale. Spec 06 §11.2 + the explicit "add a unit test asserting all combat values are multiples of 500" port risk | **throw** |
| V5 | No deckable card has `c == 0` | Spec 04's data invariant ("no deckable card may cost ◆0"), currently hand-maintained | **throw** |
| V6 | Each of the 8 element pools has exactly 8 creatures with costs `1,1,2,2,3,4,5,6`, upkeep `1,1,1,1,2,2,3,3`, and the **cost-3 card has `fs == true`** | Spec 06 §2.1 design invariant | **throw** |
| V7 | Each dual commander's `wk` equals `Math.Round((wkA+wkB)/2, MidpointRounding.AwayFromZero)` | JS half-up vs C# banker's rounding — would silently drop one worker on 16 of 36 commanders (spec 06 §2.4) | **throw** |
| V8 | `up2`/`from` symmetry: for every `X.up2` containing `Y`, `Y.from == X` | Surfaces the known `tower` defect (has no `from:'outpost'` while `bastion` does) — spec 05 open question 3 | **warn**, listed by name |
| V9 | Every `prereq` and `up2` entry resolves to a known `bid` (with `forge`/`grandforge` treated as families) | Typo'd tech tree | **throw** |
| V10 | Every commander `buildList` entry resolves via `ResolveStructure` | Broken build menu | **throw** |
| V11 | Every `kw`, `eff`, `effect`, `trigger`, `tribe`, `subtype` string maps to a declared enum member | Silently-vanilla cards | **throw** |
| V12 | Report (do not fail) cards with no `_cardart` file and no `_fieldart` file | Art coverage tracking — currently 83/197 card art, 69 field art | **info** |

V8 is deliberately a warning: the defect is real, it is in the *data*, and the port must decide
(spec 05 open question 3) rather than have the importer refuse to run.

### 5.7 CI guard — "the three layers agree"

The failure mode this prevents: someone edits `src/js/03_cards_creatures.js`, forgets to re-run the
exporter or the importer, and Unity ships stale card stats that no longer match the HTML build.

`unity/Assets/Scripts/Editor/CardImportCli.cs`:

```csharp
public static class CardImportCli
{
    /// CI entry point. Re-imports into a scratch state and fails if anything would change.
    public static void Verify()
    {
        try
        {
            var report = CardImporter.Run(prune: false, dryRun: true);
            var db = AssetDatabase.LoadAssetAtPath<CardDatabase>("Assets/Game/Data/CardDatabase.asset");

            var drift = report.Created.Count + report.Updated.Count + report.Orphans.Count;
            if (drift > 0 || db.SourceHash != CardImporter.HashOf(CardImporter.CardsJsonPath))
            {
                Debug.LogError($"Card assets are STALE vs cards.json " +
                    $"({report.Created.Count} new, {report.Updated.Count} changed, " +
                    $"{report.Orphans.Count} orphaned). Run: node tools/export_cards.mjs " +
                    $"then Tools > Spawn Row Duel > Import Cards, and commit the result.");
                EditorApplication.Exit(1);
            }
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogError(e); EditorApplication.Exit(1); }
    }

    /// Used by the pipeline script: export is done by node first, then this writes the assets.
    public static void ImportAndExit()
    {
        try { CardImporter.Run(prune: true, dryRun: false); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogError(e); EditorApplication.Exit(1); }
    }
}
```

Full regeneration, one command (`tools/regen-cards.sh`):

```bash
#!/usr/bin/env bash
set -euo pipefail
ROOT="C:/Users/mcgee/code/RTS-Card-Game"
UNITY="C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe"

node "$ROOT/tools/export_cards.mjs"

"$UNITY" -batchmode -nographics \
  -projectPath "$ROOT/unity" \
  -executeMethod SpawnRowDuel.Editor.CardImportCli.ImportAndExit \
  -logFile - -silent-crashes -accept-apiupdate

git -C "$ROOT" status --short docs/unity/spec/cards.json unity/Assets/Game/Data
```

CI verification step:
```bash
"$UNITY" -batchmode -nographics -projectPath "$ROOT/unity" \
  -executeMethod SpawnRowDuel.Editor.CardImportCli.Verify -logFile -
# exit 0 = in sync, exit 1 = stale (the log names what drifted)
```

---

## 6. Art import

### 6.1 The constraint

`assets/cards/**` must keep working **unchanged** for the HTML build: `index.html` resolves art at
runtime by probing `assets/cards/<dir>/<slug>_cardart.<ext>`, and GitHub Pages serves those exact
paths. Any solution that moves, renames, or LFS-ifies those files breaks the only mobile test surface.

Unity, meanwhile, only imports what is under `unity/Assets/`.

### 6.2 Options considered

| Option | Verdict |
|---|---|
| **A. Copy the files into `Assets/` and commit both copies** | Rejected. Duplicates 18 MB in Git *and* creates two sources of truth; an artist updating one and not the other produces a silent HTML/Unity mismatch that nothing detects. |
| **B. Sync script (one-way copy, hash-gated), Unity copy committed** | Viable fallback. Still duplicates the bytes in Git, but the copy is provably derived. Kept as the documented plan B. |
| **C. Sync script, Unity copy git-ignored** | Rejected outright. Unity `.meta` files would be ignored too, so **every fresh clone regenerates new GUIDs** and every card's art reference in every generated `.asset` breaks. |
| **D. Local UPM package (`file:` reference into `assets/`)** | Rejected. UPM packages must sit outside `Assets/` and carry a `package.json`, which would have to be added to `assets/` — polluting the web asset root and confusing the HTML build's own tooling. Also, package assets are read-only in the editor, which fights art iteration. |
| **E. Directory junction: `unity/Assets/Game/Art/Cards` → `<repo>/assets/cards`** | **Chosen.** |

### 6.3 Recommendation: **directory junction (option E)**

```
<repo>/assets/cards/                          ← the ONE copy of the bytes. Tracked in Git.
        Creatures/Fire/magmaw_cardart.png
        Creatures/Fire/magmaw_cardart.png.meta   ← Unity writes this HERE. Tracked in Git.

<repo>/unity/Assets/Game/Art/Cards             ← junction pointing at the folder above. IGNORED.
<repo>/unity/Assets/Game/Art/Cards.meta        ← the folder's own sidecar. Tracked.
```

Why this wins:

- **One copy of the bytes.** Git repo size unchanged; no drift possible, because there is only one file.
- **GUIDs are stable and committed.** Unity writes `.meta` files into the real directory
  (`assets/cards/`), which is already tracked. A fresh clone + junction gives byte-identical GUIDs, so
  every `cardArt` reference in every generated `.asset` resolves. This is the property option C loses.
- **The HTML build is untouched.** Pages keeps serving `assets/cards/**` exactly as before. The added
  `.meta` files (~200 bytes each, ~200 files, ~40 KB total) are never requested by `index.html` and are
  harmless if served.
- **No admin rights needed.** `mklink /J` (junction) works for any user on Windows; only `/D`
  symlinks require elevation or Developer Mode.
- **Artist workflow is unchanged.** Drop `newcard_cardart.png` into `assets/cards/Creatures/Fire/`;
  the HTML build finds it by probe, and Unity imports it through the junction on next focus.

Trade-offs, stated honestly:

- The junction must be **recreated after every fresh clone** (it is not a tracked file). The setup
  script in §6.4 does this and should be the documented first step in the README.
- It is **platform-specific** (`mklink /J` on Windows, `ln -s` elsewhere). The setup script handles
  both.
- Git on Windows **recurses into junctions**, which is exactly why §4.2 ignores
  `unity/Assets/Game/Art/Cards/` — without that rule you would get the 18 MB duplicated in the index.

### 6.4 Setup script

`tools/setup-unity-links.mjs` — run once after clone, and after the Unity project is created:

```js
#!/usr/bin/env node
/* Creates the art junction/symlink that lets Unity see <repo>/assets/cards
   without duplicating it. Idempotent; safe to re-run. */
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

const LINKS = [
  { from: 'unity/Assets/Game/Art/Cards', to: 'assets/cards' },
  // add more only if Unity needs them; assets/structures + assets/elements are
  // deliberately NOT linked — see §6.6.
];

for (const { from, to } of LINKS) {
  const linkPath   = path.join(ROOT, from);
  const targetPath = path.join(ROOT, to);

  if (!fs.existsSync(targetPath)) {
    console.error(`target missing: ${to}`); process.exit(1);
  }
  fs.mkdirSync(path.dirname(linkPath), { recursive: true });

  // Idempotency: if it already points at the right place, leave it alone.
  let st = null;
  try { st = fs.lstatSync(linkPath); } catch {}
  if (st) {
    const real = fs.realpathSync(linkPath);
    if (path.resolve(real) === path.resolve(targetPath)) {
      console.log(`ok (exists): ${from} -> ${to}`); continue;
    }
    // A real directory here means someone copied files in. Refuse rather than delete.
    if (st.isDirectory() && !st.isSymbolicLink()) {
      console.error(`${from} is a real directory, not a link. Move it aside first.`);
      process.exit(1);
    }
    fs.rmSync(linkPath, { recursive: true, force: true });
  }

  if (process.platform === 'win32') {
    // /J = directory junction: no admin rights, unlike /D symlinks.
    execFileSync('cmd', ['/c', 'mklink', '/J', linkPath, targetPath], { stdio: 'inherit' });
  } else {
    fs.symlinkSync(path.relative(path.dirname(linkPath), targetPath), linkPath, 'dir');
  }
  console.log(`linked: ${from} -> ${to}`);
}
```

Equivalent one-liner if you would rather not use the script:
```cmd
mklink /J "C:\Users\mcgee\code\RTS-Card-Game\unity\Assets\Game\Art\Cards" "C:\Users\mcgee\code\RTS-Card-Game\assets\cards"
```

**Plan B (option B) if Unity's importer misbehaves through the junction.** Symptoms would be: the
AssetDatabase not noticing external file changes, or `.meta` files appearing under
`unity/Assets/Game/Art/Cards/` rather than in `assets/cards/`. If that happens, switch to a hash-gated
one-way copy (`tools/sync-art-to-unity.mjs`): walk `assets/cards/**`, compare SHA-1 against a
`.artsync.json` manifest, copy only changed files into `unity/Assets/Game/Art/Cards/`, **never delete
`.meta` files**, and commit the copy. Remove the ignore rule from §4.2 in that case. The cost is
18 MB duplicated in Git; the benefit is total independence from filesystem link support.

### 6.5 Linking sprites to `CardDefinition`

Two pieces, both editor-only:

**`ArtLinker`** (called by the importer, §5.4) resolves a slug to a `Sprite` using **the same probe
order as the JS** so the two builds agree on which file wins:

```csharp
static class ArtLinker
{
    const string Root = "Assets/Game/Art/Cards";
    // Card art: png, jpg, jpeg, webp   (spec 06 §9.3, ART_EXTS)
    static readonly string[] CardExts  = { "png", "jpg", "jpeg", "webp" };
    // Field art uses a DIFFERENT order: png, webp, jpg   (FIELD_EXTS) — preserve it.
    static readonly string[] FieldExts = { "png", "webp", "jpg" };

    public static Sprite FindCardArt(string slug, string typedDir) =>
        Probe(slug, "_cardart", CardExts, typedDir);
    public static Sprite FindFieldArt(string slug, string typedDir) =>
        Probe(slug, "_fieldart", FieldExts, typedDir);

    static Sprite Probe(string slug, string suffix, string[] exts, string typedDir)
    {
        // typed folder first, then the flat fallback — mirrors artDirs()
        foreach (var dir in new[] { $"{Root}/{typedDir}", Root })
            foreach (var ext in exts)
            {
                var p = $"{dir}/{slug}{suffix}.{ext}";
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                if (s != null) return s;
            }
        return null;   // caller leaves the existing reference untouched
    }
}
```

**`CardArtPostprocessor`** — the one legitimate `AssetPostprocessor`. When a PNG under
`Assets/Game/Art/Cards/` is imported, it (a) forces the correct import settings and (b) re-runs the
link step for just the affected slug, so dropping in new art wires it up without a full re-import:

```csharp
class CardArtPostprocessor : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Game/Art/Cards/")) return;
        var t = (TextureImporter)assetImporter;
        t.textureType         = TextureImporterType.Sprite;
        t.spriteImportMode    = SpriteImportMode.Single;
        t.mipmapEnabled       = false;      // 2D UI art; mips cost memory and blur
        t.alphaIsTransparency = true;       // _fieldart cut-outs are alpha-keyed
        t.maxTextureSize      = 1024;       // README specifies 512 min / 1024 crisp
        t.textureCompression  = TextureImporterCompression.Compressed;
        // Standalone override: BC7 gives clean gradients on the element backgrounds.
        t.SetPlatformTextureSettings(new TextureImporterPlatformSettings {
            name = "Standalone", overridden = true, maxTextureSize = 1024,
            format = TextureImporterFormat.BC7,
            textureCompression = TextureImporterCompression.Compressed,
        });
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    { /* for each imported path under the cards root, relink the matching CardDefinition by slug */ }
}
```

**Missing art is normal and must not fail.** Measured coverage today: 83 `_cardart` and 69
`_fieldart` files for ~100 art-bearing cards; Dark and Electric creatures have **no art at all**. The
3-tier fallback from spec 06 §9.4 / spec 09 §7 becomes, in Unity:

1. `fieldArt` sprite → render as a cut-out standee.
2. `fieldArt` null → use `cardArt`, rendered as a *framed* standee (the JS `fromart` class).
3. both null → a single shared "art missing" sprite tinted by the element's `colorHex`.

Do **not** port `02_art.js`'s procedural SVG generators, `BLD_ART`, `FORGE_ART`, the `FIELD_MISS`
negative cache, or the sleeve/frame `Image()` prober — all are browser 404-avoidance machinery with no
Unity analogue (spec 06 §9.5, §9.7; spec 09 §21).

### 6.6 ⚠ Third-party art already in `assets/` — do not import, do not ship

Two directories contain material that is not this project's IP:

| Path | Contents | Action |
|---|---|---|
| `assets/structures/` | 43 PNGs named `MS-DOS - Warcraft II - Humans - *.png` — Warcraft II sprite rips | **Do not link into Unity. Do not ship.** Placeholder/reference material only. |
| `assets/elements/` | `hi_res_yugioh_attributes_by_aaiki_*.png` — a Yu-Gi-Oh attribute-icon fan asset | **Do not link into Unity. Do not ship.** |

Neither is reachable by the card art probe (`artDirs` only ever yields `assets/cards/<typed>/` and
`assets/cards/`), so the HTML build does not serve them as card art — but they *are* in the repo and
would be in any source distribution. The §6.4 link list deliberately covers only `assets/cards`.

Before a Steam submission: confirm the provenance of every file under `assets/cards/` too, and either
replace or license anything that is not original. Valve requires you to hold the rights to shipped
assets, and a takedown after release is far more expensive than an audit now.

---

## 7. Build and Steam

### 7.1 ⚠ Install the IL2CPP module first

Verified on this machine: `Editor/Data/PlaybackEngines/windowsstandalonesupport/Variations/` contains
**only** `*_mono` variants. IL2CPP cannot be selected until the module is installed.

**Unity Hub → Installs → 6000.5.5f1 → ⚙ → Add modules → check "Windows Build Support (IL2CPP)".**

IL2CPP additionally requires a C++ toolchain on the machine:
- **Visual Studio 2022** with the **"Desktop development with C++"** workload
  (MSVC v143 build tools + Windows 10/11 SDK). The free Community edition is sufficient.
- Verify after install: a Windows IL2CPP build should complete without a
  `"Unable to find suitable toolchain"` error.

### 7.2 Player settings — Windows / Steam

Project Settings → Player, **Windows** tab:

| Setting | Dev / iteration | **Release / Steam** | Notes |
|---|---|---|---|
| Company Name | `LucentLL` | `LucentLL` | Determines the save path `%USERPROFILE%\AppData\LocalLow\LucentLL\Spawn Row Duel\` — **fix this before first release**, changing it later orphans player saves |
| Product Name | `Spawn Row Duel` | `Spawn Row Duel` | Window title + save path |
| Version | `0.1.0` | semver | Bump per build |
| Target architecture | x86_64 | x86_64 | 32-bit and ARM64 are not worth supporting |
| **Scripting Backend** | **Mono** | **IL2CPP** | See §7.3 |
| Api Compatibility Level | .NET Standard 2.1 | .NET Standard 2.1 | Keeps the rules core `dotnet test`-able (§8.3) |
| C++ Compiler Configuration | Debug | **Release** | IL2CPP only |
| IL2CPP Code Generation | — | **Faster runtime** | Not "Faster (smaller) builds" — this is a shipping build |
| Managed Stripping Level | Disabled | **Low** | Start Low. Raise to Medium/High only with a full playthrough smoke test — Newtonsoft.Json + any reflection is what breaks |
| Incremental GC | on | on | Avoids frame-time spikes during combat resolution |
| Static/Dynamic batching | default | default | URP; use SRP Batcher (on by default) |
| Color Space | **Linear** | Linear | URP default; do not switch to Gamma |
| Graphics APIs | Auto (DX11, DX12) | DX11 first, DX12 second | DX11 is the safest primary on the widest Steam hardware survey range |
| Fullscreen Mode | Windowed | **Fullscreen Window** | Borderless is the modern default; still offer windowed in options |
| Default resolution | 1920×1080 | 1920×1080 | Verify HUD framing at 16:9, 21:9 and 4:3 (spec 09 port risk) |
| Display Resolution Dialog | Disabled | Disabled | Deprecated UX |
| Capture Single-Screen | off | off | |
| Use Player Log | on | on | Steam support tickets need it |
| Scripting Define Symbols | — | — | Steamworks.NET is compiled *in* by default; define `DISABLESTEAMWORKS` to compile it out (§7.5) |

### 7.3 IL2CPP vs Mono — recommendation

**Ship IL2CPP. Iterate on Mono.**

| | Mono | IL2CPP |
|---|---|---|
| Build time (this project size) | ~1–2 min | ~8–20 min (C++ compile of the whole managed surface) |
| Runtime speed | JIT baseline | typically 1.5–2× faster on hot managed code |
| Reverse engineering | trivial (`.dll` opens in dnSpy/ILSpy) | ahead-of-time compiled to native — meaningfully harder |
| Debugging | attach + step in the IDE, easy | possible but painful |
| Failure mode | none notable | stripping + reflection issues surface only at runtime |

The rules core is a tight deterministic simulation loop that will run hundreds of AI-turn evaluations
in a frame budget — IL2CPP's throughput advantage lands exactly there. And a Steam release with a
trivially decompilable `Assembly-CSharp.dll` invites save editing and (once multiplayer arrives)
client-side cheating; IL2CPP is not real protection, but it is the free baseline.

The practical rule: **any build a player touches is IL2CPP.** Nightly/CI builds should be IL2CPP too,
so stripping regressions are caught within a day, not the week of ship.

### 7.4 Headless CI build

`unity/Assets/Scripts/Editor/BuildCli.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpawnRowDuel.Editor
{
    public static class BuildCli
    {
        [MenuItem("Tools/Spawn Row Duel/Build Windows64 (Release)")]
        public static void BuildWindows64Menu() => Build(dev: false, exit: false);
        public static void BuildWindows64()     => Build(dev: false, exit: true);   // CI entry
        public static void BuildWindows64Dev()  => Build(dev: true,  exit: true);   // CI entry

        static void Build(bool dev, bool exit)
        {
            try
            {
                var outDir = Arg("-buildOutput")
                             ?? Path.GetFullPath(Path.Combine(Application.dataPath,
                                    dev ? "../Build/Win64-Dev" : "../Build/Win64"));
                Directory.CreateDirectory(outDir);

                // IL2CPP for release, Mono for dev — keeps CI dev builds fast.
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,
                    dev ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP);
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone,
                    dev ? ManagedStrippingLevel.Disabled : ManagedStrippingLevel.Low);
                if (Arg("-buildVersion") is string v) PlayerSettings.bundleVersion = v;

                var opts = new BuildPlayerOptions {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled)
                                                       .Select(s => s.path).ToArray(),
                    locationPathName = Path.Combine(outDir, "SpawnRowDuel.exe"),
                    target      = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options     = dev ? (BuildOptions.Development | BuildOptions.AllowDebugging)
                                      : BuildOptions.None,
                };

                if (opts.scenes.Length == 0)
                    throw new Exception("No enabled scenes in Build Settings.");

                var report  = BuildPipeline.BuildPlayer(opts);
                var summary = report.summary;
                Debug.Log($"Build {summary.result}: {summary.totalSize / 1048576} MB " +
                          $"in {summary.totalTime}  -> {opts.locationPathName}");

                if (exit) EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                if (exit) EditorApplication.Exit(1);
            }
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(a, name);
            return (i >= 0 && i + 1 < a.Length) ? a[i + 1] : null;
        }
    }
}
```

Command line:

```bash
UNITY="C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe"
ROOT="C:/Users/mcgee/code/RTS-Card-Game"

# Release (IL2CPP)
"$UNITY" \
  -batchmode -nographics -silent-crashes -accept-apiupdate \
  -projectPath "$ROOT/unity" \
  -buildTarget Win64 \
  -executeMethod SpawnRowDuel.Editor.BuildCli.BuildWindows64 \
  -buildOutput "$ROOT/unity/Build/Win64" \
  -buildVersion "0.1.0" \
  -logFile -
echo "exit=$?"    # 0 = success
```

Notes that matter:

- **Do not pass `-quit`** alongside `-executeMethod`. `-quit` can terminate the editor before an async
  build finishes; `EditorApplication.Exit(code)` inside the method is the correct, exit-code-bearing
  way out.
- `-buildTarget Win64` is passed so the editor does not switch platforms mid-run (a platform switch
  triggers a full reimport and can take many minutes).
- `-logFile -` streams the log to stdout, which is what CI wants.
- `-nographics` is safe for building. Do **not** use it if you later add graphics-dependent PlayMode
  tests.
- The output directory is `unity/Build/`, which §4.2 ignores by an **anchored** rule. Never name it
  `dist` or `build` (lowercase) — those are the unanchored HTML-build rules from §4.1.
- First IL2CPP build on a clean machine is slow (10–25 min). Cache `unity/Library/` between CI runs to
  cut subsequent builds dramatically.

### 7.5 Steamworks

**Package: [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET)** (MIT). Recommended over
the alternatives:

| Option | Verdict |
|---|---|
| **Steamworks.NET** | **Chosen.** MIT, thin 1:1 P/Invoke binding over the official SDK, the de-facto Unity standard, IL2CPP-compatible, no runtime dependencies beyond `steam_api64.dll`. Its 1:1 mapping means Valve's own documentation applies directly. |
| Facepunch.Steamworks | Nicer async/C# API, MIT, but a smaller community and historically slower to track SDK releases. Fine choice; not the safe one. |
| Heathen Steamworks | Paid Asset Store layer *on top of* Steamworks.NET. Buys editor tooling you do not need for a single-player-first release. |

**Install** (UPM git dependency — add to `unity/Packages/manifest.json`):
```jsonc
"com.rlabrecque.steamworks.net": "https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net#20.2.0"
```
Pin the tag; the wrapper version must match the Steamworks SDK version Valve currently expects.

**What it needs:**

1. **`unity/steam_appid.txt`** — a single line containing the AppID, sitting next to the project (for
   editor play) and next to the built `.exe`. Use **`480`** (Spacewar, Valve's public test app) until
   you have a real AppID. **Commit this file** — without it `SteamAPI.Init()` returns false in the
   editor and every Steam feature silently no-ops.
2. **A running Steam client** logged into an account that owns the app. Not required for the game to
   *run* — see the graceful-degradation requirement below.
3. **`SteamManager`** — ships with the package. It handles `SteamAPI.Init()`, per-frame
   `SteamAPI.RunCallbacks()`, and `SteamAPI.Shutdown()`. Drop it in the boot scene.
4. **`SteamAPI.RestartAppIfNecessary(appId)`** at the very top of startup, so launching the `.exe`
   directly re-launches it through Steam. Skip this in the editor.

**Architecture requirement — isolate it behind an interface.** The rules core must stay pure and the
game must run with Steam absent (editor, CI, tests, DRM-free builds). Put this in
`SpawnRowDuel.Platform`:

```csharp
public interface ISteamServices
{
    bool IsAvailable { get; }
    string PersonaName { get; }
    void UnlockAchievement(string apiName);
    void SetRichPresence(string key, string value);
}

// Used in the editor, in tests, in CI, and whenever SteamAPI.Init() fails.
public sealed class NullSteamServices : ISteamServices
{
    public bool IsAvailable => false;
    public string PersonaName => "Player";
    public void UnlockAchievement(string apiName) { }
    public void SetRichPresence(string key, string value) { }
}
```
`SteamworksServices : ISteamServices` is the only file in the whole project that may `using Steamworks;`,
and it is wrapped in `#if !DISABLESTEAMWORKS`. Nothing in `SpawnRowDuel.Rules` or `SpawnRowDuel.Data`
ever references either.

**Steam Cloud — use Auto-Cloud, write zero code.** Campaign progress currently lives in
`localStorage` under `srd.campaign.v3`; in Unity it becomes a JSON file under
`%USERPROFILE%\AppData\LocalLow\LucentLL\Spawn Row Duel\`. In the Steamworks partner site configure
Auto-Cloud with root `WinAppDataLocalLow`, subdirectory `LucentLL/Spawn Row Duel`, pattern `*.json`.
No API calls, and it works before you have written any Steam integration at all.

> This pairs with spec 08's port risk: the campaign save has **no schema migration** (the JS loader
> deletes the old key). Add a `SchemaVersion` int and a real migration hook *before* the first Steam
> build, because Cloud will faithfully sync a save your next version cannot read.

**Depot upload** — `steamcmd` plus a VDF build script (`ci/steam/app_build.vdf`):
```vdf
"AppBuild"
{
  "AppID" "480"                       // replace with the real AppID
  "Desc"  "Spawn Row Duel 0.1.0"
  "ContentRoot" "..\..\unity\Build\Win64\"
  "BuildOutput" "..\..\unity\Build\SteamOutput\"
  "Depots" { "481" { "FileMapping" { "LocalPath" "*" "DepotPath" "." "recursive" "1" } } }
}
```
```bash
steamcmd +login <builder_account> +run_app_build "<abs path>\ci\steam\app_build.vdf" +quit
```
Use a dedicated **builder account** with only the Edit App Metadata / publish permission, never a
personal account, and never commit its credentials. `unity/Build/SteamOutput/` sits under the ignored
`unity/Build/` rule.

**Do not ship `steam_appid.txt` in the depot.** It overrides the AppID the Steam client supplies and
will confuse a released build. Exclude it from the depot file mapping or delete it in a post-build step.

---

## 8. Testing

### 8.1 Why there are two test runners

The locked decision says the rules core must be *"unit-testable outside Unity"*. That is best served by
**two** paths, not one:

| Runner | Speed | Purpose |
|---|---|---|
| **`dotnet test`** on a plain SDK project (§8.3) | ~1–3 s | The TDD inner loop for `SpawnRowDuel.Rules`. No editor, no domain reload. This is where you will spend most of your time. |
| **Unity Test Framework**, EditMode (§8.2) | ~30–60 s | Everything that needs the AssetDatabase: the card importer, the validators, the ScriptableObject round-trip, `CardDatabase` wiring. Plus a CI re-run of the rules tests inside the real Unity compilation. |

Both compile *the same `.cs` files*. There is no duplicated logic.

### 8.2 Unity Test Framework — EditMode

`com.unity.test-framework` ships with Unity 6. The test asmdef is in §3.

`unity/Assets/Tests/EditMode/CardDataValidationTests.cs` — these mirror §5.6 so the invariants are
enforced in CI even when nobody re-runs the importer:

```csharp
using NUnit.Framework;
using UnityEditor;
using SpawnRowDuel.Data;

public class CardDataValidationTests
{
    CardDatabase _db;

    [OneTimeSetUp] public void Load() =>
        _db = AssetDatabase.LoadAssetAtPath<CardDatabase>("Assets/Game/Data/CardDatabase.asset");

    [Test] public void Database_exists_and_is_populated() {
        Assert.IsNotNull(_db, "CardDatabase.asset missing — run Tools > Import Cards.");
        Assert.AreEqual(64, _db.Creatures.Count);
        Assert.AreEqual(14, _db.Spells.Count);
        Assert.AreEqual(36, _db.Commanders.Count);
    }

    // V4: the ×500 rescale audit the extraction explicitly asked for.
    [TestCaseSource(nameof(AllCombatValues))]
    public void Combat_values_are_zero_or_a_multiple_of_500((string card, string field, int v) c) =>
        Assert.That(c.v % 500, Is.EqualTo(0),
            $"{c.card}.{c.field} = {c.v} is not on the x500 stat scale (spec 06 §11.2).");

    // V7: JS Math.round is half-UP; C# Math.Round is banker's.
    // Getting this wrong silently drops one worker on 16 of the 36 commanders.
    [Test] public void Dual_commander_workers_use_away_from_zero_rounding() {
        foreach (var cc in _db.Commanders.Values) {
            if (cc.Colors.Length != 2) continue;
            var expected = (int)System.Math.Round(
                (_db.ElementOf(cc.Colors[0]).Wk + _db.ElementOf(cc.Colors[1]).Wk) / 2.0,
                System.MidpointRounding.AwayFromZero);
            Assert.AreEqual(expected, cc.Workers, $"commander {cc.Id}");
        }
    }

    // V6: pool shape invariant.
    [Test] public void Every_element_pool_has_8_cards_and_the_cost_3_card_has_first_strike() { /* ... */ }

    // V2/V3: art + asset-path collisions.
    [Test] public void Slugs_are_unique() { /* ... */ }

    // V8: reported, not enforced — `tower` is the known offender (spec 05 OQ3).
    [Test] public void Upgrade_graph_from_and_up2_are_symmetric() =>
        Assert.Warn(/* list asymmetric pairs */);
}
```

`unity/Assets/Tests/EditMode/CardImporterTests.cs`:
```csharp
[Test] public void Reimport_is_idempotent() {
    var first  = CardImporter.Run(prune: false, dryRun: true);
    Assert.AreEqual(0, first.Created.Count + first.Updated.Count + first.Orphans.Count,
        "Generated card assets are stale vs cards.json.");
}
```

**Command line:**
```bash
UNITY="C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe"
ROOT="C:/Users/mcgee/code/RTS-Card-Game"

"$UNITY" \
  -runTests -batchmode -nographics \
  -projectPath "$ROOT/unity" \
  -testPlatform EditMode \
  -testResults "$ROOT/unity/Build/TestResults-EditMode.xml" \
  -logFile - -silent-crashes -accept-apiupdate
echo "exit=$?"
```

Exit codes: **0** = all passed, **2** = tests failed, **3** = run could not start. **Never pass
`-quit` with `-runTests`** — it terminates the editor before the run completes. Results are NUnit3
XML, which every CI system can render.

Filter to a subset:
```bash
  -testFilter "CardDataValidationTests"      # or a namespace/regex
  -testCategory "Fast"
```

PlayMode tests: keep to a handful of smoke tests (boot scene loads, a duel can be started, no
exceptions in the first N frames). They are slow and the valuable logic is all in EditMode.

### 8.3 `dotnet test` — the fast loop, no Unity

Because `SpawnRowDuel.Rules` has `noEngineReferences: true` and the project targets .NET Standard 2.1,
the exact same source files compile in a plain SDK project. Create `tests/SpawnRowDuel.Rules.Tests/`
**outside** `unity/`:

`tests/SpawnRowDuel.Rules.Tests/SpawnRowDuel.Rules.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>9.0</LangVersion>            <!-- match Unity 6's C# 9 -->
    <Nullable>disable</Nullable>
    <IsPackable>false</IsPackable>
    <!-- Do not auto-glob this folder; we pull in the Unity sources explicitly. -->
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>

  <ItemGroup>
    <!-- THE SAME FILES Unity compiles. One source of truth. -->
    <Compile Include="../../unity/Assets/Scripts/Rules/**/*.cs" />
    <Compile Include="**/*.cs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="NUnit" Version="4.2.2" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
  </ItemGroup>
</Project>
```

```bash
cd "C:/Users/mcgee/code/RTS-Card-Game/tests/SpawnRowDuel.Rules.Tests"
dotnet test                                   # ~1-3 s
dotnet test --filter "FullyQualifiedName~Combat"
dotnet watch test                             # re-runs on every save
```

If this project ever fails to compile, it means something leaked a `UnityEngine` reference into the
rules core — which is exactly the regression `noEngineReferences` and this project both exist to catch.
Add `dotnet build` of this project as the **first** CI step: it is the cheapest possible signal.

Add to `.gitignore`:
```gitignore
tests/**/bin/
tests/**/obj/
```
(`obj/` is *not* covered by the existing rules — `unity/[Oo]bj/` is anchored to `unity/`.)

### 8.4 Golden-parity tests against the JS — the highest-value tests to build

The extraction repeatedly flags that C#'s **unstable `List<T>.Sort`**, **`Dictionary` iteration order**,
and **banker's rounding** will silently diverge from the JS reference in ways no ordinary unit test
catches (`focusFire`, `aiPickTarget`, `aiChooseInterceptors`, `applyUndertow`, Detonate targeting,
`chain`'s top-two, the `byT` grouping, dual-commander workers). A hand-written test suite will not
find these; a differential test will.

**Design:**

1. `tools/gen_golden.mjs` — reuses the `node:vm` harness that `export_cards.mjs` already proves works.
   It loads the *full* JS game (all 29 files), swaps `Math.random` for a seeded PRNG, replays a fixed
   command list, and after every ply writes a canonical **state hash** plus a compact state dump.
2. Output: `tests/golden/<scenario>.json` — `{ seed, commands[], plies:[{ n, hash, state }] }`.
   Commit these; they are small and they are the behavioural contract.
3. The C# test replays the identical command list through `DuelEngine` with the same seeded
   `IDeterministicRandom` and asserts hash equality per ply. On mismatch it diffs the full state dump
   so the failure names the exact field and unit.
4. Scenarios worth having on day one: a gang-block with an absorber choice; a joint attack with a
   retaliation pick; a Detonate chain; a Chrysalis hatch; an upkeep shortfall settled by each of
   Move/Pay/Sacrifice; 200 plies of pure AI-vs-AI (the broadest net for ordering bugs).

The canonical state hash must serialize in a **pinned order** (rows in `ROWS` order, slots 0..6
ascending, then worker pools) — the same order `cleanup()` sweeps in — so the hash is meaningful
rather than incidental.

This is the single most valuable piece of test infrastructure in the port, because it converts every
"the JS did X by accident" risk in the extraction from a code-review problem into a red test.

### 8.5 Suggested CI order

Cheapest-first, so failures surface fast:

```
1. dotnet build  tests/SpawnRowDuel.Rules.Tests     ← catches UnityEngine leaking into Rules (seconds)
2. dotnet test   tests/SpawnRowDuel.Rules.Tests     ← rules + golden-parity (seconds)
3. node tools/export_cards.mjs && git diff --exit-code docs/unity/spec/cards.json
                                                    ← catches "edited the JS, forgot to export"
4. Unity -executeMethod CardImportCli.Verify        ← catches "exported, forgot to import" (§5.7)
5. Unity -runTests -testPlatform EditMode           ← importer + data validation (§8.2)
6. Unity -executeMethod BuildCli.BuildWindows64Dev  ← Mono, fast, proves it links
7. (nightly) BuildWindows64 (IL2CPP) + steamcmd upload to a private branch
```

Cache `unity/Library/` between runs — it is the difference between a 3-minute and a 20-minute job.

---

## 9. Ordered task checklist

| # | Task | Depends on | §  |
|---|---|---|---|
| 1 | Fix the open Hub dialog: name `unity`, location `C:\Users\mcgee\code\RTS-Card-Game`, template **Universal 3D**, editor 6000.5.5f1 | — | §1.1 |
| 2 | Verify `ProjectVersion.txt` + URP in `manifest.json` | 1 | §1.3 |
| 3 | Editor settings: **Force Text** serialization, Visible Meta Files, .NET Standard 2.1 | 2 | §1.5 |
| 4 | Replace the Unity `.gitignore` block; add `.gitattributes`; commit | 2 | §4.2, §4.4 |
| 5 | Delete URP template sample content; add Input System + Cinemachine + Newtonsoft | 3 | §1.4 |
| 6 | Create the folder tree + 5 asmdefs (`noEngineReferences: true` on Rules) | 5 | §2, §3 |
| 7 | Run `tools/setup-unity-links.mjs`; confirm Unity imports the art and writes `.meta` into `assets/cards/` | 6 | §6.4 |
| 8 | Commit the new `assets/cards/**/*.meta` files | 7 | §6.3 |
| 9 | Write `CardDefinition` + `CardDatabase` | 6 | §5.3, §5.5 |
| 10 | Write `CardImporter` + validators + `CardImportCli` | 9 | §5.4, §5.6, §5.7 |
| 11 | First import; commit the ~150 generated `.asset` files + `CardDatabase.asset` | 10 | §5.4 |
| 12 | Stand up `tests/SpawnRowDuel.Rules.Tests` (`dotnet test` green on an empty suite) | 6 | §8.3 |
| 13 | EditMode test asmdef + the data-validation tests | 11 | §8.2 |
| 14 | Install the **IL2CPP module** + VS2022 C++ workload | — (do early, it is a long download) | §7.1 |
| 15 | `BuildCli`; produce a first Mono dev build | 6, 14 | §7.4 |
| 16 | Player settings pass: Company/Product name **before** any save format is written | 15 | §7.2 |
| 17 | `tools/gen_golden.mjs` + the first parity scenario | 12 | §8.4 |
| 18 | Steamworks.NET + `ISteamServices`/`NullSteamServices` + `steam_appid.txt` (480) | 15 | §7.5 |
| 19 | Audit `assets/` for third-party art before any public build | — | §6.6 |

---

## 10. Open questions for the designer

1. **Product identity.** Company Name and Product Name are baked into the save path
   (`%LOCALLOW%\<Company>\<Product>\`) and into Steam Auto-Cloud config. `LucentLL` / `Spawn Row Duel`
   is assumed above. Changing either after the first release orphans every player's save. Confirm now.
2. **Steam AppID.** Everything in §7.5 uses `480` (Spacewar). A real AppID is needed before any depot
   upload, and it must be set in `steam_appid.txt`, the VDF, and `RestartAppIfNecessary`.
3. **Card art provenance.** `assets/structures/` (Warcraft II rips) and `assets/elements/` (a Yu-Gi-Oh
   fan asset) are definitely not shippable. The 83 files under `assets/cards/` need the same audit —
   are they original, commissioned, or generated? This gates the Steam submission, not the port.
4. **When does the JS build retire?** The pipeline in §5 keeps the JS registry authoritative because
   the HTML build (and therefore the Pages mobile test surface) still reads it. Once Unity is the only
   client, the flow should invert. Knowing the intended date changes whether §5's one-way generator is
   a permanent fixture or scaffolding.
5. **Divine.** `cards.json` carries 4 Divine creatures and 2 unreachable Divine forges. The importer
   generates them with `isPlayable = false`. Confirm that is wanted (a campaign boss hook) rather than
   omitting them from the asset set entirely.
6. **Art overrides.** The importer assigns `cardArt`/`fieldArt` only when a file is found by slug, so
   a manual override survives re-import — but there is then no record of *why* a card's art differs
   from its slug. Should hand-overrides be forbidden (art is always slug-resolved), or should
   `CardDefinition` carry an explicit `artOverride` field the importer never touches?
