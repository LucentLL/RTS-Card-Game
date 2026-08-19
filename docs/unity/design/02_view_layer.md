# Design 02 — Unity View Layer (PC / Steam)

**Target:** Unity 6000.5.5f1, URP (Universal 3D template), Windows/Steam first, mouse + keyboard primary.
**Scope:** everything that is *not* the rules core — scenes, camera, board rendering, standees, prefabs,
input, UI, campaign globe, FX/audio, art pipeline.
**Companion specs (read first):** `docs/unity/spec/09_presentation_ux.md` (the presentation extraction),
`docs/unity/spec/08_campaign.md` (campaign), `01_board_geometry_state.md`, `03_combat_v3.md`.

Three tags carried over from the specs are used here as well:

| Tag | Meaning |
| --- | --- |
| **[REQ]** | Real requirement. Must exist in Unity in some form. |
| **[PRES]** | A look/timing/color choice. Port it, but art direction may change it. |
| **[NEW]** | A deliberate addition or change relative to the browser build. Every one is called out so nothing changes by accident. |

---

## 0. The three laws of this layer

Everything below follows from three constraints that are non-negotiable, because they are what make the
locked decisions (deterministic core, deferred netcode, headless-testable rules) actually hold.

1. **The view never mutates game state.** It reads an immutable snapshot and emits *intents*. Every
   mouse click, key press, drag and marquee funnels into one `IIntentSink.Submit(IGameCommand)` — the
   Unity equivalent of the JS "everything routes through `onHand`/`onCell`" property
   (`09_presentation_ux.md` §11.1). If it does not go through the funnel, it does not happen.
2. **The core never knows the view exists.** No `UnityEngine` reference in `SpawnRow.Core`. Presentation
   is attached by *subscribing to an event stream*, never by wrapping/patching (this replaces all 27
   monkey patches of `22_fx_wrappers.js`). Delete the whole presentation assembly and the game still
   simulates.
3. **Animation time is view time; the core has no clock.** The core resolves a command to completion
   synchronously and emits an ordered event list. The view *replays* those events over wall-clock time
   and blocks **input**, never the simulation. Pending choices (blockers, absorber, retaliation,
   response window) come back as `PendingRequest` objects that the view answers when its animation
   queue has drained.

---

## 1. Assemblies and folder layout

### 1.1 Assembly definitions

Assembly boundaries are the enforcement mechanism for law #2 — `SpawnRow.Core` has
`noEngineReferences: true`, so a stray `UnityEngine` using is a compile error, not a code review note.

| Assembly | References | Engine refs? | Contents |
| --- | --- | --- | --- |
| `SpawnRow.Core` | — | **no** | Rules engine, board geometry, combat resolver, turn machine, AI policy, campaign rules, command/event types. *Owned by design doc 01.* |
| `SpawnRow.Core.Tests` | Core, NUnit | no | EditMode unit tests, golden-seed replays. |
| `SpawnRow.Data` | Core | yes | ScriptableObjects: card defs, structure defs, commanders, element palette, bark sets, audio cues, art tables, the baked `HexSphereAsset`. Pure data + `ToCore()` projections. |
| `SpawnRow.View.Board` | Core, Data | yes | Board scene rendering: cells, units, standees, camera rig, board raycaster, cell projector. |
| `SpawnRow.View.UI` | Core, Data, View.Board | yes | All UI Toolkit: walls/HUD, hand, card frame, panels, deck builder, menus, campaign HUD, dialogue. |
| `SpawnRow.View.Campaign` | Core, Data | yes | Globe mesh builder, orbit controller, tile picking, territory/border/marker rendering. |
| `SpawnRow.Presentation` | Core, Data, View.Board, View.UI | yes | Event router, FX catalogue, VFX spawning, audio bank, damage numbers, cut-ins, reduced-motion gating. |
| `SpawnRow.App` | everything above | yes | Bootstrap, scene director, service locator, settings, save I/O, session state. |
| `SpawnRow.Editor` | all | yes (Editor) | Card importer, art table generator, hexsphere baker, card-face bake tool, validation menu. |
| `SpawnRow.PlayTests` | all | yes | PlayMode smoke tests (§15.3). |

**Dependency direction is strictly downward.** `View.*` may reference `Core`; `Core` may reference nothing.
`Presentation` may reference views (it needs anchors) but nothing references `Presentation` — it is a leaf,
which is what makes it deletable.

### 1.2 Project tree

```
unity/                                     ← the Unity project root, inside this repo
├─ Assets/
│  ├─ SpawnRow/
│  │  ├─ Art/
│  │  │  ├─ Cards/               → symlink-free copies imported from ../../assets/cards (§14.1)
│  │  │  │  ├─ Creatures/<Element>/<slug>_cardart.png
│  │  │  │  ├─ Creatures/<Element>/<slug>_fieldart.png
│  │  │  │  ├─ Spells/  Traps/  Structures/
│  │  │  ├─ Frames/              card chrome, banners, gems, cost circles, ribbons (9-slice)
│  │  │  ├─ Sleeves/             cardback.png, frame_<element>.png (optional skins)
│  │  │  ├─ Board/               ground, hatch mat, lane decals, trench, scorch, tuft/rock/banner props
│  │  │  ├─ Walls/               battlement silhouettes, stone tiles, tower window frames
│  │  │  ├─ Icons/               ⚒ ◆ ◈ ♥ ⚔ 💤 ⤧ ⟳ ⚑ element kanji, phase glyphs (one atlas)
│  │  │  └─ Globe/               tile top/side textures, marker rings, ocean gradient
│  │  ├─ Audio/
│  │  │  ├─ Cues/                23 .wav clips (§13.4)
│  │  │  └─ Music/               (empty for now — no music in the browser build)
│  │  ├─ Data/
│  │  │  ├─ Cards/               CardDefinition assets (generated from cards.json)
│  │  │  ├─ Structures/          StructureDefinition assets
│  │  │  ├─ Commanders/          CommanderDefinition assets (36)
│  │  │  ├─ Elements/            ElementPalette (9) + ElementBarkSet (8) + RivalExchange (8)
│  │  │  ├─ Art/                 CardArtTable, PlaceholderArtTable
│  │  │  ├─ Audio/               AudioCueBank, AudioCue (23)
│  │  │  ├─ Fx/                  FxCatalogue, ElementFxSet (9)
│  │  │  └─ World/               HexSphereAsset_f4 (baked topology + mesh + tri→tile LUT)
│  │  ├─ Prefabs/
│  │  │  ├─ Board/               BoardRoot, CellAnchor, UnitView, StandeeView, WorkerStackView,
│  │  │  │                       CastleWallProp, BattlefieldScenery
│  │  │  ├─ Campaign/            GlobeRoot, TerritoryMarker, BorderRenderer
│  │  │  ├─ Fx/                  one prefab per FX primitive + 9 ElementImpact_<element>
│  │  │  └─ App/                 Bootstrapper, ServiceHost, LoadingScreen
│  │  ├─ Scenes/
│  │  │  ├─ 00_Boot.unity
│  │  │  ├─ 01_Shell.unity          (persistent, additive)
│  │  │  ├─ 10_MainMenu.unity
│  │  │  ├─ 20_Campaign.unity
│  │  │  ├─ 30_Battle.unity
│  │  │  └─ 40_DeckBuilder.unity
│  │  ├─ Scripts/
│  │  │  ├─ Core/                (SpawnRow.Core.asmdef — doc 01 owns the contents)
│  │  │  ├─ Data/                (SpawnRow.Data.asmdef)
│  │  │  ├─ View.Board/
│  │  │  ├─ View.UI/
│  │  │  ├─ View.Campaign/
│  │  │  ├─ Presentation/
│  │  │  ├─ App/
│  │  │  └─ Editor/
│  │  ├─ Settings/
│  │  │  ├─ URP/                 PC_RendererData (Decal + FullScreenPass features), PC_RPAsset,
│  │  │  │                       Volume profiles (Battle, Campaign, Menu)
│  │  │  ├─ Input/               SpawnRowControls.inputactions
│  │  │  └─ UIToolkit/           PanelSettings_HUD, PanelSettings_Screens, PanelSettings_CardBake
│  │  ├─ Shaders/                BoardCells.shadergraph, Standee.shadergraph, CardFace.shadergraph,
│  │  │                          GlobeTile.shadergraph, Battlement.shadergraph
│  │  ├─ UI/
│  │  │  ├─ Uxml/                CardFrame, Hand, WallPlayer, WallFoe, PhaseTrack, WorkerColumn,
│  │  │  │                       Hint, CardActionMenu, InspectPanel, BuildPanel, ContestPanel,
│  │  │  │                       RespondBar, DeckBuilder, MainMenu, FactionSelect, CampaignHud,
│  │  │  │                       ChallengeDialogue, Banner, Settings, Viewer, LogPanel
│  │  │  ├─ Uss/                 tokens.uss, card.uss, walls.uss, board-hud.uss, screens.uss,
│  │  │  │                       deckbuilder.uss, campaign.uss
│  │  │  └─ Themes/              SpawnRow.tss
│  │  └─ VFX/                    VFX Graph assets (per §13.3)
│  └─ StreamingAssets/           (empty; saves go to persistentDataPath)
├─ Packages/manifest.json        URP 17, Cinemachine 3.1, Input System 1.11, Addressables 2.x,
│                                Visual Effect Graph, UI Toolkit (built-in), Test Framework
└─ ProjectSettings/
```

Art lives **inside** `unity/Assets` rather than being read from `../assets` — Unity cannot import assets
outside the project. §14.1 defines the one-way sync tool that keeps `assets/cards/**` (authoring source,
where the artist drops files) mirrored into `Assets/SpawnRow/Art/Cards/**`.

---

## 2. Scene architecture and bootstrap flow

### 2.1 Scene set

| Scene | Load mode | Lifetime | Contents |
| --- | --- | --- | --- |
| `00_Boot` | Single (build index 0) | ~1 frame | `Bootstrapper` only. Creates the service host, then loads `01_Shell` additively and hands off. Nothing else — it exists so a cold start has a deterministic entry regardless of which scene was open in the editor. |
| `01_Shell` | Additive, **never unloaded** | whole session | `ServiceHost` (DontDestroyOnLoad-free — the scene *is* the persistence), `EventSystem` + `InputSystemUIInputModule`, `UIDocument` for the *overlay* panel layer (loading screen, banner, settings, toasts), global `AudioListener` proxy, `SceneDirector`, `PresentationSettings`, `SaveService`. |
| `10_MainMenu` | Additive content | until nav | Menu UIDocument, ornament VFX (embers), a menu camera. |
| `20_Campaign` | Additive content | until battle/menu | Globe root, orbit camera, campaign HUD UIDocument, challenge dialogue host. |
| `30_Battle` | Additive content | until match ends | Board root, battlefield scenery, camera rig, battle HUD UIDocuments, FX pools, card-face bake rig. |
| `40_DeckBuilder` | Additive content | until nav | Deck builder UIDocument + card art streaming. Separate scene because it touches the whole card-art pool; unloading it releases that memory. |

**Exactly one content scene is loaded at a time**, additive over `01_Shell`. The main camera lives in the
content scene; `01_Shell`'s overlay `UIDocument` uses a `PanelSettings` with **Screen Space Overlay** so
it needs no camera and survives content swaps.

Why additive rather than `LoadSceneMode.Single` for content: the Shell owns the loading screen, audio
listener and service host, and a Single load would destroy them mid-transition. Additive keeps the fade
alive across the swap.

### 2.2 Flow

```
00_Boot
  └─ Bootstrapper.Awake()
        1. Application.targetFrameRate = -1; QualitySettings.vSyncCount = 1
        2. ServiceHost.Install(...)   ← DI container / plain service locator, see §2.3
        3. SettingsService.Load()     ← srd.* keys migrate to a single settings.json
        4. await SceneDirector.LoadShell()
        5. await SceneDirector.Go(AppScreen.MainMenu)

MainMenu ──Solo──►    DeckPick ─► FoePick ─► Battle
         ──Campaign─► Campaign (WorldMap)
         ──DeckBuilder─► DeckBuilder
         ──Rules/Settings─► overlay only (no scene change)

Campaign ──attack──► ChallengeDialogue (in 20_Campaign, overlay) ──► Battle (30_Battle)
Battle   ──BattleFinished(outcome)──► ResultBanner (Shell overlay)
              ├─ campaign context ─► Campaign  (CampaignSession.Resolve(outcome))
              └─ solo context     ─► MainMenu
```

**[REQ]** The battle→campaign coupling is inverted relative to the JS (`checkWin` → `campResolve`).
`BattleSession` raises `BattleFinished(BattleOutcome)`; `CampaignSession` subscribes. Surrender produces
`BattleOutcome.Abandoned`, which the campaign resolver treats as "the assault never happened" — replacing
the JS trick of nulling `CAMPAIGN.target` in three different files (`08_campaign.md` §12.4).

### 2.3 Services (installed once in Boot, resolved by scene roots)

```csharp
public interface IServiceHost {
    T Get<T>() where T : class;
}

// Registered in Bootstrapper:
//   ISettingsService          persisted PresentationSettings + keybinds
//   ISaveService              campaign save, saved decks (JSON @ persistentDataPath)
//   ISceneDirector            async additive load/unload + fade
//   ICardRegistry             from SpawnRow.Data (ScriptableObject → core defs)
//   ICardArtResolver          §14.3
//   IAudioService             §13.4
//   IPresentationBus          §13.1
//   IBattleSession            wraps the core DuelEngine for one match
//   ICampaignSession          wraps the core campaign resolvers + save
```

`SceneDirector` sketch:

```csharp
public sealed class SceneDirector : MonoBehaviour, ISceneDirector {
    string _current;                      // name of the loaded content scene
    public async Task Go(AppScreen screen, object payload = null) {
        await _loadingScreen.FadeIn(0.18f);
        if (_current != null) await SceneManager.UnloadSceneAsync(_current);
        var name = SceneNameFor(screen);
        await SceneManager.LoadSceneAsync(name, LoadSceneMode.Additive);
        SceneManager.SetActiveScene(SceneManager.GetSceneByName(name));  // lighting/skybox owner
        _current = name;
        SceneRootFor(screen).Enter(payload);   // typed payload: BattleLaunchRequest, DeckBuilderArgs…
        await Resources.UnloadUnusedAssets();
        await _loadingScreen.FadeOut(0.18f);
    }
}
```

Each content scene has exactly one `SceneRoot : MonoBehaviour` (`BattleSceneRoot`, `CampaignSceneRoot`, …)
with an `Enter(payload)` / `Exit()` pair. Nothing uses `Awake` ordering tricks and nothing uses static
mutable state, so **Enter Play Mode without Domain Reload** works — important because iteration speed on
a board this size matters.

### 2.4 Campaign ⇄ Battle round trip

`20_Campaign` is **unloaded** while a battle runs (the globe mesh is small, but its 22 territory markers,
HUD document and dialogue host are not worth keeping resident). Continuity is preserved by two service-held
objects, mirroring the JS's module-scoped `campView` surviving re-mounts:

```csharp
public sealed class CampaignSession {           // lives in ServiceHost, survives scene swaps
    public CampaignState State;                 // core type, serialisable
    public GlobeViewPose ViewPose;              // yaw/pitch/vyaw — restored on re-entry
    public int? PendingTerritory;               // == State.TargetTerritory
    public BattleLaunchRequest BuildLaunch(int territoryId, string commanderId, ulong deckSeed);
    public IReadOnlyList<CampaignEvent> Resolve(BattleOutcome outcome);
}
```

---

## 3. The board — geometry, meshes, and scene graph

### 3.1 World units and the coordinate contract

**[REQ]** One board cell = **1.0 × 1.0 world units** on the XZ plane, board top surface at `y = 0`.
Everything else derives from that, so designers can reason in cells.

| Quantity | Value | Rationale |
| --- | --- | --- |
| Cell footprint | 1.00 (X) × 1.00 (Z) | The CSS cell is 0.74 : 1 (w : h) *after* a 45° pitch foreshortens Z by cos45 ≈ 0.707. A square world cell reproduces the browser's on-screen proportion in Tilted mode almost exactly, and is correct (not squashed) in Top-Down. **[NEW]** — deliberate correction, called out because it changes Top-Down proportions vs. the browser. |
| Column gap | 0.06 | The `clamp(3px,.7vw,9px)` gap, expressed in cells. |
| Row gap | 0.06 | |
| Board extent | X ∈ [−3.71, +3.71], Z ∈ [−2.65, +2.65] | 7 cols, 5 rows + gaps. |
| Column → X | `x = (col - 3) * 1.06` | Column 3 (`BASE_COL`) is dead centre. |
| Row → Z | `z = (2 - rowIndex) * 1.06` | Row 0 (`FoeBack`) is at +Z (far), row 4 (`YouBack`) at −Z (near). **The camera looks down −Z toward +Z**, i.e. from the player's side. |
| Virtual wall rows | `z = ±(2 + 1) * 1.06 = ±3.18` | `FoeWall` (row −1) at +3.18, `YouWall` (row 5) at −3.18. No cells, but a **castle wall prop and a click collider** live there (§3.6). |

```csharp
public static class BoardLayout {
    public const float CellSize = 1.00f, Gap = 0.06f, Pitch = CellSize + Gap;   // 1.06
    public static Vector3 CellCenter(RowKey row, int col) =>
        new Vector3((col - BoardGeometry.BaseColumn) * Pitch, 0f, (2 - (int)row) * Pitch);
    public static Vector3 WallCenter(Side defender) =>
        new Vector3(0f, 0f, defender == Side.Foe ? +3f * Pitch : -3f * Pitch);
    public static Bounds BoardBounds => new Bounds(Vector3.zero, new Vector3(7 * Pitch, 0.2f, 5 * Pitch));
}
```

### 3.2 Board scene graph

```
30_Battle
├─ BattleSceneRoot                          [BattleSceneRoot, ServiceConsumer]
├─ CameraRig                                 (§4)
│  ├─ MainCamera                             [Camera(physical), CinemachineBrain, URP Additional Camera Data,
│  │                                          AudioListener, BoardRaycaster, CellProjector]
│  ├─ VCam_TopDown                           [CinemachineCamera, prio 10]
│  └─ VCam_Tilted                            [CinemachineCamera, prio 20]
├─ Lighting
│  ├─ KeyLight (Directional)                 rot (50, -30, 0), 1.15 intensity, warm 5200K, soft shadows
│  ├─ FillLight (Directional)                rot (-20, 150, 0), 0.28, cool 7500K, no shadows
│  └─ GlobalVolume                           [Volume → BattleProfile]
├─ BoardRoot                                [BoardView, BoardCellStateBuffer]
│  ├─ BoardSurface                          [MeshFilter(board quad), MeshRenderer(BoardCells material)]
│  ├─ CellColliders                          35 × [BoxCollider (1.0×0.05×1.0), CellCollider(RowKey,Col)]
│  │                                         layer = BoardCell
│  ├─ CellAnchors                            35 × empty Transform (FX/popover/standee parent)
│  ├─ Units            (pooled)              UnitView instances, reparented to CellAnchors
│  ├─ WorkerStacks     (8)                   WorkerStackView, one per (Side × WorkerZone)
│  ├─ CastleWall_Foe                        [CastleWallProp, BoxCollider (WallTarget), layer = BoardCell]
│  ├─ CastleWall_You                        [CastleWallProp]
│  └─ CenterBanner                           "⚔ CONTESTED CENTER ⚔" world-space decal on the mat
├─ Battlefield                              [BattlefieldView]  (§3.7)
├─ FxRoot                                   [FxPool, DamageNumberStack]
├─ UI
│  ├─ UIDoc_HUD          [UIDocument → PanelSettings_HUD]     walls, hand, phase track, hint, buttons
│  ├─ UIDoc_Popovers     [UIDocument → PanelSettings_HUD]     card action menu, floating worker chips,
│  │                                                          inspect panel, aim arrow, marquee box
│  └─ UIDoc_Modals       [UIDocument → PanelSettings_Screens] build/contest/charge/viewer/respond
└─ CardFaceBakeRig       [CardFaceBaker, UIDocument → PanelSettings_CardBake (targetTexture)]
```

### 3.3 The board surface and cell state — one shader, no per-cell overlays

**[REQ]** Every cell visual state from `09_presentation_ux.md` §3.6 must exist. **[NEW]** They are drawn by
the board surface shader from a 7×5 state texture, **not** by 35 decal projectors or 35 quads.

Rationale: the states are *in-plane* borders and glows that must foreshorten correctly under the 45° tilt,
they animate (1.1 s pulse, marching dashes), and there are up to 35 of them at once. A single mesh with a
tiny lookup texture is one draw call, is trivially animatable in the shader, and avoids depth-fighting a
transparent overlay against the ground.

```csharp
[Flags] public enum CellFlags : uint {
    None = 0,
    BackRow = 1<<0, Center = 1<<1, CenterLane = 1<<2, CenterFlank = 1<<3,
    MineHere = 1<<4, FoeHere = 1<<5,
    Tappable = 1<<6, Target = 1<<7, Selected = 1<<8, AttackSelected = 1<<9,
    Intercept = 1<<10, DeclaredAttacker = 1<<11, DeclaredTarget = 1<<12, DeclaredBlocker = 1<<13,
    DragHover = 1<<14, MarqueeHighlight = 1<<15,
    TargetingMode = 1<<16          // the louder body.targeting treatment
}

public sealed class BoardCellStateBuffer : MonoBehaviour {
    readonly CellFlags[] _flags = new CellFlags[35];
    Texture2D _tex;                       // 7×5, RGBA32: R,G = flag bits packed, B = anim phase seed
    MaterialPropertyBlock _mpb;
    public void Set(RowKey row, int col, CellFlags f);       // marks dirty
    void LateUpdate() { if (_dirty) { UploadToTexture(); _dirty = false; } }
}
```

`BoardCells.shadergraph` (Lit, Opaque, on the board top surface):
- Base: ground/mat albedo (tiled stone + hatch), an element-tinted territory blend per half (§3.7).
- Cell UV → cell index → sample the 7×5 state texture (point filter).
- Per-flag border draw using a signed-distance box on the cell-local UV:
  gold `#d4af37` (Tappable / Selected / AttackSelected / DeclaredAttacker),
  red `#e35b4f` (Target / DeclaredTarget), cyan `#7fd0f5` (Intercept / DeclaredBlocker, **dashed**),
  green `rgba(120,220,150,.95)` (MarqueeHighlight), gold offset ring (DragHover).
- Inner glow = the same SDF blurred, additive, alpha from a `_Pulse` node (`0.5+0.5*sin(t*2π/1.1)`) for the
  states the CSS pulses (AttackSelected, Target-in-targeting-mode, life-aim).
- `MineHere` / `FoeHere` inset rings (gold / blue).
- `CenterFlank` with no occupant and no target flag renders **bare ground** — no border, no fill
  (matches `.centerstruct`, `09` §3.6).
- **[NEW] Colorblind channel:** a `_StateShapeMode` toggle adds a distinct corner glyph per semantic class
  (▲ your action, ✖ enemy target, ⌒ interception, ▭ marquee, ┈ committed block) so the meaning does not
  live in hue alone. Closes `09` §25 open question 5.

Cell colliders are separate flat boxes on layer `BoardCell` — the surface mesh has **no** collider, so a
raycast never returns "the board" ambiguously.

### 3.4 Unit prefabs

```
UnitView (prefab)                       [UnitView, Animator("StandeePose")]
├─ CardPlate                            the mini card lying flat in the board plane
│  ├─ Quad (0.92 × 0.92, rot X 90°)     [MeshRenderer, material: CardFace (instanced, _MainTex = baked atlas page,
│  │                                     _UvRect = atlas slot)]
│  └─ Rot180Pivot                       flips the plate 180° when Owner == Foe   ([REQ], 09 §3.7)
├─ StandeeRoot                          y = 0.02
│  ├─ BobPivot                          [StandeeBob]  sine, period 3.4 s, amplitude 0.07 × height
│  │  └─ FigureQuad                     [MeshRenderer, material: Standee (alpha-clip)]
│  │                                    pivot at bottom-centre; height = min(1.50, 1.20 × cellW)
│  └─ ShadowDecal                       [DecalProjector] blob, 0.58 × 0.12 cells, y-projected
├─ StatusChips                          world-space TMP/quads: 💤 ⤧ ⟳ ◆N ⚒ FS  (billboard yaw only)
└─ SelectionFx                          child anchor for ring/burst FX spawned by Presentation
```

Separate prefab variants: `UnitView_Creature`, `UnitView_Structure` (taller planted standee, no bob,
never laid), `UnitView_FaceDown` (card back plate, **no standee**), `UnitView_Trap` (card back + ⚠ chip).
`WorkerStackView` is *not* a cell occupant — it is a small cluster of 1–3 tiny figures parked at the outer
edge of its zone's row, plus a floating chip (§7.4), because workers are pool members, not board objects.

**Standee rendering decision [REQ]:** cut-outs are **alpha-clipped opaque** quads (`_AlphaClip 0.5`), not
alpha-blended. Reason: on a 45° board with 35 potential figures, blended quads sort by object origin and
visibly pop through one another; alpha clip writes depth and sorts per-pixel for free. Soft edges are
recovered with `Alpha To Coverage` under MSAA 4× (PC can afford it).

**Standee pose [REQ]** — `canActNow()` from the core drives an enum, the camera preset drives the axis:

```csharp
public enum StandeePose { Up, Laid, Hidden }

public sealed class StandeeView : MonoBehaviour {
    [SerializeField] float _laidLerpSpeed = 8f;
    public StandeePose Pose;                 // from Core: CanActNow(state, cell)
    public BoardAngle Angle;                 // from the camera preset
    void LateUpdate() {
        // Up  in Tilted  → upright, X-rotation 0 (quad stands out of the board plane)
        // Up  in TopDown → lies in-plane at 6° so it reads from directly above, scale 0.86
        // Laid (both)    → in-plane, +6° tilt, greyscale 0.4 / brightness 0.6, bob off, shadow 0.4
        float targetX = (Pose == StandeePose.Up && Angle == BoardAngle.Tilted) ? 0f : 84f;
        ...
    }
}
```

Billboarding: the battle camera never orbits (only two fixed presets), so a standee that is *upright and
facing −Z* is correct in both presets. `StandeeBillboard` therefore defaults to `Mode.UprightFixed`.
A `Mode.CameraYaw` option exists behind a flag for a future free-look camera, but is **off** — full
camera-facing billboarding on a fixed camera only introduces shimmer. This is the exact behaviour the CSS
`rotateX(-45deg)` counter-rotation produced.

### 3.5 Binding view instances to core state

**[REQ]** Views are keyed by the core's monotonic instance id (`uid`), never by cell coordinates — a unit
that moves must animate, not teleport-by-rebuild.

```csharp
public sealed class BoardView : MonoBehaviour {
    readonly Dictionary<int, UnitView> _byId = new();       // core Occupant.Id → view
    readonly CellAnchor[,] _anchors = new CellAnchor[5, 7];
    ObjectPool<UnitView> _creaturePool, _structurePool, _faceDownPool;

    public void Reconcile(IReadOnlyGameState s) {           // called after every command batch
        _seen.Clear();
        foreach (var (cell, occ) in s.OccupiedCells()) {
            if (!_byId.TryGetValue(occ.Id, out var v)) { v = Spawn(occ); _byId[occ.Id] = v; }
            v.Bind(occ, s);                                  // updates plate texture, chips, pose
            v.MoveTo(_anchors[(int)cell.Row, cell.Col], animate: true);
            _seen.Add(occ.Id);
        }
        foreach (var id in _byId.Keys.Except(_seen).ToList()) { Despawn(_byId[id]); _byId.Remove(id); }
        _cellStates.RebuildFrom(s, _interaction);            // §3.3
    }
}
```

`Reconcile` is a *snapshot diff*, not the JS full rebuild. It is idempotent, so the presentation layer can
also call it mid-animation without side effects. Death removal is deferred: `Despawn` is queued and only
runs after the presentation queue has played that unit's death FX (§13.2).

### 3.6 Castle walls as world objects [NEW]

The browser has no wall object — you strike the enemy life pool by clicking the ♥ in their HUD
(`.lifeaim`). In 3D there is an obvious diegetic target: a **castle wall prop** standing at the virtual
row (`z = ±3.18`), with a collider on `BoardCell` layer that reports `AttackTarget.Wall(defender)`.

- **[REQ] Keep the ♥ affordance too** — it is discoverable and it is what the hint text points at. Both
  routes emit the identical `DeclareAttack(attacker, WallTarget(side))` command.
- The wall prop takes damage visually (cracks via a `_Damage` material float driven by `life/maxLife`),
  which finally gives "raze their base" a physical read.
- The prop is *not* a rules object. It has no HP of its own; it renders `PlayerState.Life`.

### 3.7 Battlefield scenery

**[REQ]** from `09` §3.4: two element-tinted territories, a scorched contested frontier, worn lane paths
down columns 1/3/5, scattered debris. **[PRES]** the exact prop counts.

Implementation:
- One `BattlefieldGround` mesh (a single large quad, 1.06× the board extent) with a material that blends
  three bands (foe territory / churned no-man's-land / your territory), tinted by
  `_FoeTint` / `_YouTint` from `ElementPalette.Bg[]`.
- Lane paths, trench ridges, scorch cracks, crater rims: **URP decal projectors**, authored once, placed at
  fixed board coordinates. No CSS measurement of column gaps needed — the board is a known size.
- Props (rocks, tufts, banners, braziers, tents, stake lines) are prefab variants placed by
  `BattlefieldView.Scatter(seed)` using the **core's presentation RNG** (a separate stream from the
  simulation RNG — §13.5), so scenery can be re-rolled without touching a replay.
- Ambient motes / smoke / cloud shadow → 3 small Shuriken systems; all disabled under Reduced Motion.

```csharp
public sealed class BattlefieldView : MonoBehaviour {
    public void Build(ElementId you, ElementId foe, uint presentationSeed) { … }
}
```

---

## 4. Camera

### 4.1 The single most important camera decision

**Both presets use the SAME perspective camera.** Top-Down is *not* an orthographic camera.

Justification:
1. Cinemachine cannot blend between an orthographic and a perspective virtual camera — the projection
   flips discontinuously at the cut. The browser's angle switch is an animated 0.24 s transition and
   should stay one.
2. A steep perspective camera with a long focal length is visually indistinguishable from orthographic at
   this scale, while keeping standees, walls and FX consistent between presets.
3. One projection means one `BoardRaycaster`, one `CellProjector`, one set of FX distance tuning.

| Preset | Pitch | Field of view | Distance | Feel |
| --- | --- | --- | --- | --- |
| **Top-Down** | 78° | 20° (long lens, near-ortho) | fit-computed (~16 u) | Flat, readable, the "board game" view. |
| **Tilted** (default) | 45° | 34° | fit-computed (~10 u) | The diorama. Matches `rotateX(45deg)` + `perspective: 260vh`. |

`perspective: 260vh` on a viewport-height board ≈ a 21–22° half-angle; the 34° FOV at the closer Tilted
distance reproduces the browser's magnification of the near row well. **[PRES]** — tune against the
browser build side by side.

Pitch 78° rather than a literal 90° for Top-Down: at exactly 90° standees lying in-plane are the only
thing you see, and the castle wall props vanish. 78° keeps a hint of the walls' faces. **[NEW]**, small.

### 4.2 Rig

```
CameraRig
├─ MainCamera        Camera(usePhysicalProperties = true), CinemachineBrain
│                    default blend: Ease In Out, 0.24 s  (matches cubic-bezier(.34,1.18,.5,1) ≈ back-out;
│                    use a custom blend curve asset with a 6% overshoot to reproduce the bounce)
├─ VCam_TopDown      CinemachineCamera, Lens FOV 20, priority 10
└─ VCam_Tilted       CinemachineCamera, Lens FOV 34, priority 20
```

Both vcams are children of a `BoardFramer` that positions them each time the aspect changes:

```csharp
public sealed class BoardFramer : MonoBehaviour {
    [SerializeField] CinemachineCamera _topDown, _tilted;
    [SerializeField] float _marginX = 0.35f, _marginZ = 0.25f;    // world units of breathing room
    [SerializeField] float _handReserveVh = 0.16f;                // the hand strip, in viewport height

    public void Reframe(Camera cam) {
        // For each preset: place the vcam on the pitch ray through the board centre at the distance
        // where BoardBounds (+ wall props + margins) fits BOTH the horizontal and the vertical
        // frustum, after subtracting the hand strip from the usable vertical extent.
        // Vertical fit:   d = halfDepthProjected / tan(fovV/2)
        // Horizontal fit: d = halfWidth        / tan(fovH/2)   with fovH from aspect
        // distance = max(vertical, horizontal)
    }
}
```

This is the entire replacement for `fitBoard()`'s 12-iteration shrink loop and the `--extscale` feedback
loop (`09` §3.2, §21 row 1). **[REQ]** the *requirement* survives verbatim: the full 7×5 field plus the
hand strip must be visible at any window size with no scrolling and no letterboxing. Verify at 16:9, 16:10,
21:9, 4:3 and 32:9.

### 4.3 `--wallY` → lens shift, not camera movement

**[REQ]** When a castle wall opens, the board slides vertically to make room (−14% / +9% of viewport).

The precise analogue of a CSS `translateY` on a projected plane is a **physical-camera lens shift**, which
offsets the projection without rotating the camera or changing perspective:

```csharp
public sealed class WallOffsetController : MonoBehaviour {
    [SerializeField] Camera _cam;                 // usePhysicalProperties = true
    public WallState Wall;                        // None | Player | Foe   (§8.2)
    public bool PhaseForcesOpen;                  // draw / upkeep force the player wall open

    static float ShiftFor(WallState w, BoardAngle a) => w switch {
        WallState.Player => a == BoardAngle.Tilted ? +0.12f : +0.14f,   // board rises (view shifts down)
        WallState.Foe    => a == BoardAngle.Tilted ? -0.08f : -0.09f,
        _ => 0f
    };
    // Animate _cam.lensShift.y toward ShiftFor(...) over 0.24 s with the back-out curve.
}
```

Values are the CSS percentages of viewport height, which map 1:1 onto normalised lens shift. Exactly one
wall is ever open (§8.2), so the two never sum.

### 4.4 Lighting and URP settings

| Setting | Value | Why |
| --- | --- | --- |
| Render pipeline | URP 17, **Forward+** | Many small lights later (braziers, FX); Forward+ removes the per-object light limit. |
| MSAA | 4× | Alpha-to-coverage on standee cut-outs depends on it. |
| HDR | On | Bloom on gold/ember accents. |
| Shadows | 1 cascade, 30 m distance, soft | Only the key light casts; the board is tiny. |
| SSAO | On, intensity 0.4, radius 0.15 | Seats standees and structures on the ground plane. |
| Decal Renderer Feature | On, DBuffer | Blob shadows, lane paths, scorch, trench, cell one-offs. |
| Full Screen Pass Renderer Feature | ×2 | (a) hurt vignette, (b) the `.mat::after` board vignette. |
| Light Probes | A 3×3×2 grid over the board | Standee quads are unlit-ish but pick up territory tint. |
| Static batching | On for board/scenery | |
| GPU Resident Drawer | On | Free, and the globe scene benefits. |
| Color space | Linear | |
| Volume (Battle profile) | Bloom 0.35/threshold 1.1, Vignette 0.28, Tonemapping ACES, subtle Color Adjustments hooked to the match's element pair | Replaces the CSS radial vignette and the element tint variables. |

Key light at (50°, −30°) puts standee shadows *away* from the camera in Tilted mode so figures never
shadow the card plate the player is reading.

### 4.5 Sorting and occlusion on a tilted board

The browser needed `preserve-3d`, `isolation: isolate` and z-index arbitration. Unity needs none of it,
but three explicit rules prevent the equivalent problems:

1. **Everything on the board writes depth.** Card plates opaque; standees alpha-clipped opaque; blob
   shadows are decals (no sorting). Only genuine particle FX are transparent.
2. **Card plates sit at y = 0.001, standee shadow decals project onto y = 0**, so plates never z-fight the
   ground.
3. **Transparent FX use a shared sorting priority band** (`FxRoot` sets `renderQueue = 3050`), spawned at
   the cell anchor's world position, so damage numbers and bursts always draw over the board but under UI.
4. Far-row legibility: the vignette full-screen pass is masked to the screen edges only, so the foe back
   row does not dim into unreadability the way the CSS vignette did (which is exactly why
   `body.targeting` needed a "louder" target treatment). **[NEW]** — keep the louder targeting treatment
   anyway, it reads well.

---

## 5. Card faces — one frame, four scales, baked once

### 5.1 The decision

**[REQ]** `09` §6 defines one DM_Template frame reused at four scales (hand, big inspect, board mini,
deck-builder tile), all driven by a single accent variable `--ec`. Reproducing that as (a) a UI Toolkit
template *and* (b) a separate world-space mesh layout would immediately fork the design.

**Decision: author the frame ONCE as `CardFrame.uxml` + `card.uss`, and render it to a texture for the
board.** UI Toolkit `PanelSettings.targetTexture` makes a runtime panel render into a `RenderTexture`;
`CardFaceBaker` composites one card into an atlas slot and hands the slot's UV rect to the board quad's
material.

Why not pre-bake at build time: the face depends on *runtime* state (current HP, tapped/sick chips, banked
◆, first-strike badge, owner tint) — that is a combinatorial space no build-time bake can cover. Why not
per-frame UI in world space: 35 world-space UI documents laying out every frame is wasteful and UI
Toolkit's world-space support in 6000.5 is still awkward.

### 5.2 `CardFaceBaker`

```csharp
public readonly struct CardFaceKey : IEquatable<CardFaceKey> {
    public readonly CardId Card; public readonly CardFaceVariant Variant; public readonly int Hp, Bank;
    public readonly CardFaceFlags Flags;   // Sick|Tapped|Moved|Moved2|Foe|FaceDown|Ready
}

public sealed class CardFaceBaker : MonoBehaviour {
    [SerializeField] UIDocument _bakeDoc;            // PanelSettings_CardBake, targetTexture = _scratch
    [SerializeField] RenderTexture _scratch;         // 512 × 711  (744/1033 aspect)
    RenderTexture[] _atlas;                          // 2048×2048 pages, 4×3 slots of 512×711 → 12/page
    readonly Dictionary<CardFaceKey, AtlasSlot> _cache = new();   // LRU, cap ≈ 96 live faces
    readonly Queue<CardFaceKey> _pending = new();

    public AtlasSlot Request(CardFaceKey key);       // returns a placeholder slot if not yet baked
    void LateUpdate() { /* bake at most 3 per frame; Graphics.CopyTexture scratch → atlas slot */ }
}
```

- Board plates read `_MainTex = atlasPage`, `_UvRect = slot.Rect` via `MaterialPropertyBlock` → one draw
  call per atlas page for the whole board with GPU instancing.
- Hand cards, the inspect panel, deck-builder tiles instantiate the **same** `CardFrame.uxml` live (they
  are few, and they need crisp text at large sizes).
- Face-down / trap plates use the shared card-back recipe (`09` §6.5) with the optional image sleeve
  (`Sleeves/cardback.png`, `frame_<element>.png`) swapped in if the asset exists — the same skinnability
  requirement, resolved at load instead of by `Image()` probing.

### 5.3 Rules text is generated, never authored

**[REQ]** `abilityBrief`, `spellText`, `bldEffectText`, `kwName` (`09` §6.6) become one localisable
service in `SpawnRow.Data`:

```csharp
public interface ICardTextService {
    string TypeLine(CardId id);              // "Human Wizard" / "Structure" / "✦ Spell"
    string AbilityBrief(CardId id);          // hand-card short form, " · " joined
    string AbilityFull(CardId id);           // inspector long form
    string KeywordLabel(Keyword k, int v);   // "Detonate 1500", "Reap 500"
    string StructureEffect(StructEffect e, int val, int sup);
}
```
Backed by a `LocalizedStringTable`; the format strings live in data, the numbers come from the card defs.

---

## 6. Input

### 6.1 Input System, not legacy — and why

Use **com.unity.inputsystem** exclusively (`activeInputHandling = Input System Package (New)`).

1. Steam release needs **rebindable** keys and gamepad support; the legacy `Input` class has neither
   without hand-rolling a binding layer.
2. UI Toolkit's runtime event system integrates through `InputSystemUIInputModule`; mixing legacy input
   with UI Toolkit picking produces the exact "who ate this click" ambiguity the CSS build suffered from.
3. Composite bindings (`Shift+Click`, `Ctrl+Drag`) and interaction modifiers (`Hold`, `MultiTap`) are
   declarative, which matters for the drag-vs-tap threshold and hold-to-inspect.
4. Steam Input / Steam Deck later needs the abstraction anyway.

`SpawnRowControls.inputactions`, action maps:

| Map | Actions |
| --- | --- |
| `Board` | `Point` (Vector2), `Click` (Button), `ClickAlt` (right button), `Drag` (Value, pass-through), `AddToSelection` (Shift modifier), `Cancel`, `Inspect` (Hold 0.18 s / right click), `CameraToggle` |
| `Hand` | `SelectSlot1..9`, `SelectSlot0`, `NextCard`, `PrevCard` |
| `Turn` | `EndTurn`, `Harvest`, `Draw`, `ResolveCombat`, `Build`, `Cancel` |
| `Cursor` (keyboard/gamepad board navigation) | `MoveCursor` (Vector2, repeat), `Confirm`, `Cancel`, `NextUnit`, `PrevUnit`, `ToggleAttackGroup` |
| `UI` | the standard UI map for UI Toolkit |
| `Campaign` | `Orbit`, `Pick`, `EndTurn`, `Back` |

### 6.2 The funnel

```csharp
public interface IIntentSink { void Submit(IGameCommand cmd); }     // the ONLY way to change state

public sealed class BattleInteractionController : MonoBehaviour, IIntentSink {
    InteractionState _ui;            // Mode, SelectedHandIndex, ArmedPlayMode, MoveSource,
                                     // ManaSource, AttackGroup, OpenMenu  — view-owned, never in core
    IBattleSession _session;

    public void OnCellActivated(CellRef cell) { /* the exact priority ladder of 09 §11.1 onCell */ }
    public void OnHandActivated(int index)    { /* 09 §11.1 onHand */ }
    public void OnWallActivated(Side defender){ /* routeAttack wall */ }
    public void OnWorkerStackActivated(Side owner, WorkerZone zone) { … }
}
```

**[REQ]** Mouse click, keyboard confirm, gamepad A, drag-drop, marquee release and the forgiveness snap all
call `OnCellActivated` / `OnHandActivated`. Nothing bypasses them. This is the single property that kept
the browser build's rules, costs, traps, win checks, FX and SFX identical across input methods, and it is
worth defending with a code review rule.

### 6.3 Picking — `BoardRaycaster`

```csharp
public interface IBoardRaycaster {
    bool TryPick(Vector2 screenPoint, PickMode mode, out BoardPick pick);
}
public enum PickMode { Exact, Forgiving }
public readonly struct BoardPick {
    public readonly PickKind Kind;      // Cell | Wall | WorkerStack | None
    public readonly CellRef Cell; public readonly Side WallSide;
    public readonly Side PoolOwner; public readonly WorkerZone PoolZone;
}
```

Algorithm:
1. `Physics.Raycast` against layer mask `BoardCell` (35 cell colliders + 2 wall colliders + 8 worker-stack
   colliders). This deletes the whole `elementFromPoint` class of bugs (`09` §21 row 7).
2. If `mode == Forgiving` **and** the hit is not a currently-legal cell **and** the hit cell is empty:
   apply the **44 px screen-space snap**, ported exactly from `snapLegalCell` (`09` §11.2):
   - candidate set = cells currently flagged `Tappable` or `Target`;
   - `rect = CellProjector.ScreenRect(cell)`; `dx = max(rect.xMin − p.x, p.x − rect.xMax, 0)`, same for y;
     `d = dx² + dy²`;
   - ties (`d == 0`, overlapping projected rects) break on squared distance to rect centre × 1e-6;
   - accept if `d ≤ radius²`, radius default **44** (a serialized, settings-exposed float).
3. Forgiveness applies **only** to activations on empty non-lit cells — never to a click on an occupied
   card, which is always an intentional card interaction. Same gate as `snapContext()`.

**[REQ]** Keep the forgiveness. It was half bug-workaround and half accessibility; the raycast kills the
bug half, and the comfort half is worth more on a tilted board where far rows are small.

### 6.4 `CellProjector` — the world→screen bridge

```csharp
public interface ICellProjector {
    Rect ScreenRect(CellRef cell);        // in *panel* coordinates for the HUD PanelSettings
    Rect ScreenRect(Side wall);
    Vector2 Point(CellRef cell, Vector3 localOffset);
}
```

Implementation projects the cell's four corner points and takes the AABB. **Critical detail:** UI Toolkit
runtime panels use their own coordinate space; every returned rect must go through
`RuntimePanelUtils.CameraTransformWorldToPanel` (or `ScreenToPanel`) before being used to position a
`VisualElement`. Getting this wrong is the #1 source of "the popover is 40 px off" bugs.

Consumers: card action menu anchoring, floating worker chips, aim arrow endpoints, marquee hit test,
damage-number spawn points, the drag ghost.

### 6.5 Drag and drop

One gesture machine, three kinds — matching `09` §11.3.

```csharp
public enum DragKind { None, Hand, Board, Marquee }

public sealed class DragController : MonoBehaviour {
    const float MouseThreshold = 7f;      // Manhattan px  (touch 15 kept for a later mobile port)
    DragKind _kind; int _handIndex; CellRef _from; Vector2 _origin;

    // Begin (pointer down): rejected if not your turn / busy / over / phase ∈ {Draw, End},
    //   or if the pointer started over a UI element that picks.
    //   .hc under pointer   → Hand   (Action phase only)
    //   your ready creature under pointer AND AttackGroup is EMPTY AND CanMove → Board
    //   else, over the board with no hand/move selection → Marquee
    // Start: mirror the action-menu affordability gate; refuse a drag that could not legally drop and
    //   write the reason into the hint (identical to the browser).
}
```

**[REQ] No board drag while an attack group is held.** Building the group is click-click-click and a
slightly rolled click must not become a move that wipes the group (`09` §11.3, `01` §14.2).

Ghosts:
- **Hand drag:** a `VisualElement` clone of the card following the pointer (rotate −2°, scale 1.05,
  opacity .92) — pure UI Toolkit.
- **Board drag:** **[NEW]** no 2D ghost. The unit's standee lifts ~0.25 u, tilts slightly toward travel,
  and follows a ray-plane intersection against `y = 0`; legal destination cells light through
  `CellFlags.Tappable` and the hovered cell gets `DragHover`. In-world drag reads far better on a
  perspective board than a screen-space ghost.
- Drop resolves through `cellUnder` semantics: exact raycast first, then the same 44 px forgiveness.
- A failed drop cancels the selection (`cancelSel`), a *near-miss* drop snaps and succeeds.

### 6.6 RTS marquee — the PC signature

**[REQ]** `09` §11.4. Mouse/pen only.

```csharp
public sealed class MarqueeController : MonoBehaviour {
    VisualElement _box;                                   // 1.5 px rgba(120,220,150,.95) border, .16 fill
    public void OnDragUpdate(Rect screenRect) {
        _hits.Clear();
        foreach (var cell in _board.OwnReadyCreatureCells(Side.You))     // creature && you && !worker
            if (screenRect.Overlaps(_proj.ScreenRect(cell))) _hits.Add(cell); //          && !sick && !tapped
        _cellStates.SetOnly(CellFlags.MarqueeHighlight, _hits);
    }
    public void OnDragEnd() {
        if (_hits.Count == 0) _interaction.ClearAttackGroup();
        else _interaction.SetAttackGroup(_hits);          // Combat v3: mixed rows ALLOWED
        _interaction.ClearSelection();                    // sel, moveFrom, cardMenu
    }
}
```

**[REQ]** Combat v3 allows mixed rows in solo. The MP "reduce to the single row with the most hits" branch
stays as a **disabled policy hook** (`IMarqueePolicy`) so netcode can enable it later without touching the
controller (`09` §25 open question 9).

### 6.7 Hover to inspect

**[REQ]** PC is `FINE_POINTER`, so hover-to-inspect is the primary inspect path (`09` §10.1).

- Show delay **180 ms**, hide grace **120 ms**.
- Keyed by card identity (`hand:<i>` / `<row>|<col>`) so re-entering the same card does not re-trigger.
- Suppressed while dragging, while the game is over, while a *blocking* modal is open, or while any
  full-screen screen is up. **Not** suppressed by an open card action menu — the card text must stay
  readable while weighing the choices.
- **[NEW]** Right-click is promoted to the **global instant inspect** gesture (it currently only inspects
  worker chips). Hover-delay remains for discoverability. Closes `09` §25 open question 3.

### 6.8 Keyboard and gamepad [NEW]

Nothing exists in the browser build; a Steam release needs it (`09` §20). Proposed default bindings, all
rebindable, all routed through the funnel:

| Action | Keyboard | Gamepad |
| --- | --- | --- |
| End Turn | `E` | Start / Menu |
| Harvest (upkeep) | `H` | Y |
| Draw (draw phase) | `D` or `Space` | A on the deck |
| Resolve combat | `R` or `Enter` | X |
| Open Build panel | `B` | LB |
| Select hand card 1–10 | `1`–`9`, `0` | LB/RB cycle + A |
| Board cursor | Arrow keys / WASD | Left stick (cell-snapped, repeat 8/s) |
| Confirm cell | `Enter` / `Space` | A |
| Add to attack group | `Shift`+Confirm | Hold LT + A |
| Cancel / deselect | `Esc` | B |
| Cycle own ready units | `Tab` / `Shift+Tab` | RB / LB |
| Inspect focused | `I` / right-click | RS click |
| Toggle board angle | `V` | D-pad up |
| Toggle standees | `F` | — |
| Log / Rules / Settings | `L` / `F1` / `F10` | D-pad down / Select |

The cursor is a `CellRef` with a visible focus ring (`CellFlags` gains a `Focused` bit) that moves through
*legal* cells first (`Tab` order = board order), then all cells. Focus is fully mirrored into the UI
Toolkit focus ring so keyboard users can move between board and HUD with `Tab`.

---

## 7. UI — surface by surface

### 7.1 Technology choice

**UI Toolkit for every screen-space surface. uGUI nowhere. World-space only for things that must live in
the board's perspective.** Rationale per surface:

| Surface | Tech | Justification |
| --- | --- | --- |
| Castle walls + tower windows | UI Toolkit | Pure screen furniture with a flexbox tower/centre/tower split. USS transitions give the 0.24 s slide. The battlement silhouette is a 9-sliced sprite instead of a 60-point `clip-path`. |
| Hand | UI Toolkit | Overlapping z-ordered cards with a height transition — exactly what flexbox + USS transitions do. Manipulators handle the drag. |
| Card frame (4 scales) | UI Toolkit (`CardFrame.uxml`) | Single authoring source; §5. |
| Phase track, turn label, buttons, hint | UI Toolkit | Static HUD. |
| Worker column (left tower) | UI Toolkit | A 5-row list bound to derived worker figures. |
| Vitals / commander cluster (incl. the ♥ life-aim target) | UI Toolkit | It is HUD; it must be clickable as an attack target, which UI picking handles. |
| Card action menu (popover) | UI Toolkit, positioned from `ICellProjector` | Must track a cell in the 3D scene; same clamp/flip algorithm as `09` §12. |
| Floating worker chips | UI Toolkit, projected | Same. |
| Inspect panel (hover + modal modes) | UI Toolkit | Text-heavy; hover mode sets `pickingMode = Ignore` so it cannot eat clicks. |
| Build / charge / contest / viewer / respond / log / rules / settings / banner | UI Toolkit | Modal panels. |
| Deck builder | UI Toolkit + `ListView` | Hundreds of tiles need virtualisation; `ListView`/`GridView` provide it for free. |
| Main menu, faction select, solo pickers | UI Toolkit | Static layout; ornamentation is decorative (§7.9). |
| Campaign HUD, turn log, confirm, toast | UI Toolkit | |
| Challenge dialogue | UI Toolkit + world/2D portraits | Text box + two portrait images; no 3D needed. |
| Marquee box, aim arrow, drag ghost | UI Toolkit overlay | Screen-space by nature. |
| Damage numbers, combat pops | **World-space TMP** | §13.6 — answers `09` §25 open question 6. |
| Cell highlights | **Board shader** | §3.3 — must foreshorten in-plane. |

Three `PanelSettings` assets: `PanelSettings_HUD` (sort order 0, scale-with-screen 1920×1080, match 0.5),
`PanelSettings_Screens` (sort order 10, full-screen overlays), `PanelSettings_CardBake` (render to texture,
§5.2).

### 7.2 The wall state machine

**[REQ]** Replaces `:has()` selectors, `!important` duels and capture-phase listeners (`09` §4.4, §21
rows 12–13) with one explicit state:

```csharp
public enum WallState { None, Player, Foe }

public sealed class WallController : MonoBehaviour {
    public WallState State { get; private set; }
    [SerializeField] float _revealBandPlayer = 64f, _revealBandFoe = 28f;   // px from the screen edge

    void Update() {
        var next = WallState.None;
        if (_phase is TurnPhase.Draw or TurnPhase.Upkeep) next = WallState.Player;   // forced open
        else if (_pinnedByHandHover || _handCardSelected)  next = WallState.Player;
        else if (PointerNearBottom(_revealBandPlayer))     next = WallState.Player;
        else if (!_targeting && PointerNearTop(_revealBandFoe)) next = WallState.Foe;
        Apply(next);      // toggles two USS classes + drives WallOffsetController (§4.3)
    }
}
```

Invariants that must hold and are trivially checkable here (and were not in CSS):
- **Exactly one wall open at a time.**
- **The foe wall never rises while `_targeting`** — it would cover the far row you are aiming at.
- **Off-click on empty board retracts both walls and deselects a held hand card, but NEVER clears the
  attack group or the move source.** A fat-finger miss must not cancel an in-progress action (`09` §4.4).

### 7.3 Wall layout

```
WallPlayer.uxml
└─ .wall.player                    height clamp(170px, 26vh, 250px); translateY 0 (open) /
   ├─ .wall__stone                 calc(100% - 18px) (rest); transition 0.24 s back-out
   ├─ .wall__rail                  element-tinted 9 px stripe (--youelem)
   ├─ .tower.tower--left           left 1.6%, width 17.8%, inset top 16% bottom 8%
   │  └─ .twin  (window)
   │     ├─ .vitals               ♜ leader · ♥ life (.lifeaim when targetable) · ◆ mana ◈ vaultCap
   │     │                        · ⌂ structures ⚒ workers
   │     ├─ .build-button         ⚒ Build   (disabled outside the Action phase)
   │     └─ .worker-column        5 rows: Enemy Base / Raid / Center / Front / Base   (§7.4)
   ├─ .hand-span                  21% – 79%   → the Hand element mounts here
   └─ .tower.tower--right
      └─ .twin
         ├─ .pile.pile--deck      real stacked layers, ≤10, top = element-tinted card back
         └─ .pile.pile--grave     top = the last destroyed card's art, desaturated
```

`WallFoe.uxml` mirrors it (height `clamp(140px, 21vh, 210px)`, rest shows a 46 px rail, foe vitals in its
left tower, foe hand backs across the top edge).

**[REQ]** Deck/graveyard render as **real card piles** — one thin layer per card capped at 10, offset
∓1.2 px per layer, a count badge on the top card's outer corner, an empty pile as a dashed vacant slot with
a `0` badge. At rest a compact `Deck: N  GY: M` line shows instead. Clicking the deck **draws** during the
Draw phase (with a gold pulse) and opens the viewer at any other time.

### 7.4 Worker column and floating chips

**[REQ]** Five rows ordered to match the board top→bottom so the column reads against the field, each
showing `<label> ⚒<N>`, a `<up>✓` ready-count chip, and a dimmed `· ⚒<foeN>` for the opponent's figure in
that physical row. Shortfall rows go dark red; zero rows go 50% opacity. Tooltips carry the actual rules
explanation per state. The foe's own wall shows **no** worker chips.

Floating on-board chips (`rowFloatChips`) appear **only when actionable**: an enemy worker stack you can
strike while holding an attack group (red, pulsing, clickable → `DeclareAttack(WorkerStackTarget)`), or a
shortfall warning. They are UI Toolkit elements positioned via `ICellProjector` at the row's outer edge.

### 7.5 Hand

```
Hand.uxml
└─ .hand                           bottom-anchored, centred, gap 3 px, max-width 58vw
   └─ .hc  (repeated)              rest height --peek (name+cost banner only);
                                   expanded height --hch on hand hover / focus-within / selection;
                                   hover: translateY(-8%) scale(1.1); selected: gold outline + glow;
                                   z-index 10+index (flat stack, NO fan — deliberately removed)
```

**[REQ] `body.placing` behaviour survives as an explicit state:** while a hand card is armed with a
board-drop mode, every *non-selected* hand card goes `pickingMode = Ignore; opacity: .35`. Likewise
**`body.targeting`** fades the foe hand strip to .2 and ghosts the turn label — both were framed as DOM
bug fixes in the source but are genuinely good UX (`09` §5.2, §5.3).

### 7.6 Card action menu

**[REQ]** Positioned popover, algorithm ported verbatim (`09` §12):
`left = clamp(anchorCentreX − w/2, 6, panelW − w − 6)`; `top = anchorTop − h − 12`, flipping below with a
`.below` class if that would be `< 6`; a triangular pointer on the side facing the card. Anchors: hand
menus to the hand card element; board menus to `ICellProjector.ScreenRect(cell)`. Hidden entirely when it
is not your turn or the game is busy/over. Two skins (gold board menu / blue circular-icon hand menu), each
button carrying icon + label + a cost-or-reason sub-label, with disabled buttons showing the reason.

### 7.7 Hint line

**[REQ]** The persistent context-sensitive instruction line is the game's primary teaching surface. Port
**every string** in `09` §9.3 into the localisation table verbatim, including the inline action buttons
(`⚔ Resolve combat`, `cancel`). `HintPresenter` subscribes to `InteractionState` changes and to
`PendingRequest`s; it never composes strings from rules internals.

### 7.8 Modals, and the respond bar

All modal panels from `09` §13 port 1:1 as UXML. Two deserve special attention:

**Contest panel (block chooser).** Its meta line (`your interceptors ⚔D · incoming ⚔A`) and its
"Interpose N (deal ⚔D)" / "Let it through" pair are how a player learns row-interval blocking. It renders
from a `BlockerRequest` (`PendingRequest`), and its answer is the request's response — no direct state
mutation. Keep the optional countdown parameter for later MP.

**Respond bar (priority window).** **[REQ]** Port the whole concept including the **anti-tell** property:
the AI's answer executes exactly at window end whether or not it holds a trap. In Unity the *timer* is a
view concern but the *window* is a core `ResponseWindowRequest`; the view must not shorten it based on
what the AI does. Settings: `Off | 3 s | 4 s | 6 s`, default 4 s, plus the 15 s Pause escape hatch.
**[NEW] Open question carried forward:** whether a single-player-only build should keep the window at all
(`09` §13.1 / spec 03 open questions) — it is a real decision, and the setting's `Off` value already covers
the "no" answer, so ship it defaulted on.

### 7.9 Menus, deck builder, campaign UI

- **Main menu:** rotating ray field and the counter-rotating element ring as shader-driven
  `VisualElement` backgrounds (160 s / 90 s periods), 16 ember particles as a small Shuriken system behind
  the panel, title with a metal gradient. All ornament disabled under Reduced Motion.
- **Deck builder:** three-column flex (`minmax(260px,0.9fr) 1.3fr 1.7fr`), left detail column showing the
  full `CardFrame`, centre deck column with leader picker + mana curve + deck tiles, right pool column with
  a virtualised `ListView`. **[REQ]** constraints (40 / 3 copies / 5 saved decks), the ordered
  `deckErrors` messages, the 5 sort orders with their exact tiebreaks, the toggle-off-on-repeat filters,
  the counter ring, the duplicate-name element disambiguation, and the hover-zoom preview (fine pointers,
  prefers the left of the cursor, `pickingMode = Ignore`).
- **Campaign HUD / faction select / turn log / confirm / toast:** straight UXML ports, wording preserved
  (`08` §8). Toast auto-hides at 2600 ms and a new toast resets the timer.
- **Challenge dialogue:** UXML box + two portrait `Image`s (attacker bottom-left, defender bottom-right and
  **mirrored** `scaleX(-1)`), non-speaker at `brightness .45 saturate .7`, speaker lifted 6 px and scaled
  1.04, element-tinted radial glow behind each, **typewriter at 14 ms/char**, bobbing ▼ when a line
  completes, click-while-typing completes the line, click-when-complete advances, a 7 px (mouse) travel
  guard so a drag does not advance, and a `Skip ▸▸`. Portraits come from `ICardArtResolver.FieldArt` of the
  element's champion card, falling back to card art.

---

## 8. Interaction state and the view↔core contract

### 8.1 Where each piece of state lives

**[REQ]** The JS conflates rules state and interaction state inside `G`, and the MP layer has to null six
fields on every snapshot adopt. In Unity the split is structural:

| State | Lives in | Notes |
| --- | --- | --- |
| Board, hands, decks, mana, life, phase, turn, declarations | `SpawnRow.Core.GameState` | Serialisable. **Declarations included** — spec 03 §14 flags their omission as the main netcode trap. |
| Selection, armed play mode, move source, mana source, attack group, open menu, drag | `View.UI.InteractionState` | Never serialised, never sent to the core. |
| Board angle, standees, cut-ins, reduced motion, volume, response window, keybinds | `PresentationSettings` | Persisted to `settings.json`. |
| Camera pose, wall state, hover target | View components | Transient. |

### 8.2 The command / event / request loop

```csharp
// Per frame, in BattleSceneRoot.LateUpdate:
while (_session.TryDequeueEvent(out GameEvent e))  _presentation.Enqueue(e);    // animation timeline
if (_presentation.Idle) {
    if (_session.Pending is PendingRequest req)     _requestPresenter.Show(req); // modal or AI answer
    else                                            _boardView.Reconcile(_session.State);
}
```

- The core resolves a command **fully and synchronously** and produces an ordered `GameEvent` list.
- The presentation timeline replays them over wall-clock time; input is locked while it plays
  (`IsInputFrozen`), which is the honest replacement for `G.busy`.
- A `PendingRequest` is only surfaced once the timeline is idle, so the player never sees a block prompt
  before the lunge animation that provoked it. This also removes the JS bug where `G.busy` was toggled
  **off** mid-resolution to allow the absorber prompt (spec 03 port risks).
- `IsInputFrozen` is also the hook a future netcode layer sets (`MP.frozen`), so the view never needs to
  learn about the network.

---

## 9. The campaign globe in real 3D

### 9.1 What is discarded

Everything in `08_campaign.md` §1.2 and §11: the orthographic `P(v)` projection, `rot`/`unrot` matrices,
painter's-algorithm sorting, corner-based back-face culling with the `z < -0.35` heuristic and the seam
bleed-through workaround, per-frame extruded skirt polygons, the `shade()` hex-string colour maths, the
hand-rolled `LI` light, the inverse-ray picker with its `R*EXH` correction and 1.06 slop radius, and the
whole `fit`/`fitTick`/`campGlobeStop` render-loop lifecycle.

### 9.2 What is baked

**[REQ]** The GP(4,0) topology is generated **once**, in the editor, into an immutable asset. Tile index
order is save-format-critical (saves store `tileTerr` as a raw index array), so it must never drift.

```csharp
[CreateAssetMenu(menuName = "SpawnRow/Hex Sphere")]
public sealed class HexSphereAsset : ScriptableObject {
    public int Frequency = 4;                       // 162 tiles, 320 corners
    public Vector3[] TileCenters;                   // unit vectors, index order frozen
    public Vector3[] Corners;
    public int[] CornerRingStart, CornerRingIndices;  // CCW as seen from outside, flattened
    public int[] AdjacencyStart, AdjacencyIndices;    // 5 or 6 per tile
    public Mesh   TileMesh;                           // one merged prism mesh, all 162 tiles
    public int[]  TriangleToTile;                     // RaycastHit.triangleIndex → tile id
    public int[]  TileSubmeshVertexStart;             // for per-tile vertex-colour writes
}
```

Generation (editor tool `Tools ▸ SpawnRow ▸ Bake Hex Sphere`) reproduces `08` §3.2 exactly:
icosahedron vertices from φ, the 20 faces **in their listed order**, the triangular lattice per face, and
vertex de-duplication. The one change: **replace the `toFixed(6)` string-key weld with a spatial hash at
epsilon 1e-6**, then assert the resulting tile order against a golden fixture (tile 0's centre and its
adjacency list) so a future refactor cannot silently renumber tiles and corrupt every save.

Mesh construction: for each tile, a top face whose corners are lerped 7 % toward the tile centre
(`INSET = 0.93`, preserving the authored bevel look) at radius `1 + H` (`H = 0.05`), plus side skirts down
to radius 1. All tiles merged into one mesh; per-tile colour written into the **vertex colour** channel so
recolouring a captured territory is a `mesh.SetColors` on a cached array — no material churn.

### 9.3 Globe scene graph

```
20_Campaign
├─ CampaignSceneRoot
├─ GlobeRig
│  ├─ MainCamera            Camera(perspective, FOV 32), positioned at +Z looking at origin
│  └─ GlobeRoot            [GlobeOrbitController]  ← yaw/pitch applied HERE (rotate the globe, not the cam,
│     │                                              which is exactly what the JS did and keeps the light
│     │                                              camera-fixed for free)
│     ├─ GlobeMesh         [MeshFilter(HexSphereAsset.TileMesh), MeshRenderer(GlobeTile), MeshCollider]
│     ├─ Ocean             sphere at r = 1.001, unlit #0a1424
│     ├─ GlowRing          billboarded quad, radial rgba(96,118,210,.16) from 0.88R to 1.2R
│     ├─ Borders           [BorderMeshBuilder] one procedural line mesh, rebuilt on ownership change
│     └─ Markers           22 × TerritoryMarker (billboarded, at anchorTile.center × 1.07)
├─ Lighting                one directional light **parented to the camera** (matches the JS's view-space
│                          light dir normalize(-0.45, 0.55, 0.72)); ambient 0.62 to match
│                          `lum = 0.62 + 0.5·max(0, n·L)`
├─ UIDoc_CampaignHud       faction/turn/lands/capitals/allies, End Turn, New, Menu, legend
├─ UIDoc_CampaignOverlays  confirm, turn log, toast, banner
└─ ChallengeDialogueHost   UIDoc + portraits
```

### 9.4 Rendering territories, borders and markers

- **Territory colour** = owner element colour, written to vertex colours; player-owned tiles get the
  `× 1.18` brightness boost via a vertex-colour alpha flag read by `GlobeTile.shadergraph`.
- **Borders** (`08` §11.3) rebuild only when ownership changes: for each ordered adjacent tile pair with
  differing territory, emit a quad strip along the two shared corner indices at radius `EXH`. Three styles
  by the same rules: same-owner/different-territory `rgba(0,0,0,.35)` thin; different-owner involving the
  player **gold `#d9b64a`** thick; different-owner not involving the player white-ish medium.
- **Markers** are world-space billboards with a UI-style ring: capital = element glyph + garrison number
  below; ordinary = garrison number. Attackable territories pulse gold at
  `0.55 + 0.45·sin(t/0.45s)`; player-owned ring white; others faint. Hidden when the tile faces away
  (dot(tileNormal, toCamera) < 0.18) — a cheap explicit test, not a culling hack.
- **Picking:** `Physics.Raycast` the `MeshCollider` → `hit.triangleIndex` → `TriangleToTile` → tile id →
  `map.TileTerritory[tile]` → territory. Three lines, and it is exact — deleting both the `R*EXH`
  correction and the 1.06 slop.

### 9.5 Orbit feel — the constants that must survive

```csharp
public sealed class GlobeOrbitController : MonoBehaviour {
    const float DragToRadians = 0.005f;      // per pixel, both axes
    const float PitchClamp    = 1.25f;       // ±71.6°
    const float InertiaSeed   = 0.0009f;     // vyaw = dx * seed, per move event
    const float InertiaDecay  = 0.93f;       // per frame
    const float IdleSpin      = 0.0011f;     // rad/frame after 2600 ms of no interaction
    const float IdleDelay     = 2.6f;
    const float TapThreshold  = 7f;          // Manhattan px (mouse)
    public GlobeViewPose Pose;               // yaw, pitch, vyaw — persisted in CampaignSession
}
```

`campGlobeAimAt` becomes `Pose = GlobeViewPose.LookingAt(tileCenter)` — the same yaw/pitch formula, used on
first mount to aim at the player faction's capital anchor. A second pointer is ignored so it cannot hijack
the drag or produce a spurious pick.

**[NEW]** Add mouse-wheel zoom (camera dolly, clamped) — free in 3D, genuinely useful on a 162-tile sphere,
absent in the browser build.

---

## 10. Battle ⇄ campaign handoff in the view

```csharp
public sealed class BattleLaunchRequest {         // core type; the view only carries it
    public string PlayerCommanderId;              // chosen banner (solo or dual)
    public string EnemyCommanderId;               // the defender element's SOLO commander
    public ulong  DeckSeed;                       // [NEW] seeds BOTH decks deterministically
    public int    TerritoryId;                    // context only; the duel ignores it today
}
```

Flow: `CampaignSceneRoot` → confirm overlay → `ChallengeDialogueBuilder.Build(...)` (4 lines, presentation
RNG) → dialogue plays → `SceneDirector.Go(AppScreen.Battle, request)`.
`BattleSceneRoot.Enter(request)` constructs the `DuelEngine`; on `BattleFinished(outcome)` the Shell shows
the result banner, then `SceneDirector.Go(AppScreen.Campaign)` and `CampaignSession.Resolve(outcome)`
applies capture / absorb cascade / completion latch — or, for `Abandoned`, nothing at all.

---

## 11. Presentation events — replacing the 27 monkey patches

### 11.1 The bus

```csharp
public interface IPresentationBus {                 // implemented in SpawnRow.Presentation
    void Enqueue(GameEvent e);
    bool Idle { get; }
    void Skip();                                    // fast-forward (settings / holding a key)
}
```

The core emits `GameEvent` records (see the core design doc). Every row of `09` §18's wrapper table maps to
exactly one event, and the mapping is the porting checklist:

| # | JS wrapper | Event | Presentation |
| --- | --- | --- | --- |
| 1 | `applyDmg` | `DamageApplied(unitId, cell, amount)` | world-space `−N` pop, red |
| 2 | `resolveCombat` | `CombatClash(attackers, defenders, anyDefenderSurvived)` | battle cut-in, `clash` SFX, board shake, slash, element burst at the defender; if a defender survived → `block` SFX + blue flash (the parry beat) |
| 3 | `toGrave` | `UnitDestroyed(cell, kind, element, isWorker)` | building → `raze` + grey burst + shake; charge → `trap` + blue burst; creature → element-coloured burst |
| 4–6 | `doAttack` / `attackBackRow` / `attackMinionStack` | `AttackLunge(attackers, target)` | staggered 70 ms lunges, aim arrow, `swing`, element comet; wall version adds a `big` burst + shake when life actually dropped |
| 7 | `place` | `CardPlayed(handIndex, cell, mode, element, cost)` | card flies hand→cell (300 ms), `set`/`raise`/`place`, ring + element flash + burst |
| 8 | `flip` | `CardFlipped(cell, kind, bigReveal)` | `summon`/`raise`, ring, flash, burst; **splash reveal if cost ≥ 4 or First Strike** |
| 9 | `castSpell` | `SpellCast(handIndex, effect)` | `spell` SFX |
| 10 | `springTrap` | `TrapSprung(cell)` | `trapSnap` + `trap` SFX |
| 11–12 | `doMove` / `aiMoveCreature` | `UnitMoved(from, to, owner)` | trail (blue yours / red theirs), 240 ms glide, ring, `move` |
| 13 | `onCreatureEnter` | `CreatureEntered(cell, element, cost, firstStrike)` | AI-summon parity |
| 14–15 | `placeBuild` / `aiBuild` | `StructureRaised(cell)` | `build`, ring, blue flash, burst |
| 16 | `resolveSpell` | `SpellResolved(effect, primary, chainTargets, element)` | per-effect: burn comet + flame; raze big burst + shake; chain two sequential electric bursts 110 ms apart; bounce water burst |
| 17–19 | `doHarvest` / `applyHarvest` / `applyRes` | `ManaGained(side, amount)` | `mana` + `+N` cyan pop at the mana readout |
| 20 | `trainVillager` | `WorkerTrained(side)` | `train` |
| 21–22 | `dealOpening` / `drawCard` | `CardDrawn(side, isOpeningDeal)` | `draw` (silent on the opening deal) |
| 23 | `startTurn` | `TurnStarted(side)` | turn ribbon + `turnYou` / `turnFoe` |
| 24 | `render` life diff | `LifeChanged(side, delta)` | `±N` pop; your loss also fires the hurt vignette + `hit` |
| 25 | `startGame` | `MatchStarted` | `shuffle`, `DUEL START` ribbon, `turnYou` |
| 26 | `checkWin` | `MatchEnded(playerWon)` | `win` + confetti / `lose`; **read from state, never from banner text** |
| 27 | `renderCharSel` | — | menu-side decoration, view-local |

**[REQ]** Two additional events the JS had no place for, both needed by the Combat v3 UX:
`AttackDeclared(attacker, target, blockers)` and `BlockersCommitted(...)` — so the board can paint
`DeclaredAttacker` / `DeclaredTarget` / `DeclaredBlocker` outlines **for the AI's declarations too**. The JS
keeps AI declarations in a local array and never renders them, which the Combat v3 design explicitly wanted
visible (spec 03/07 port risks). **[NEW]**, and a real UX improvement.

### 11.2 The timeline

```csharp
public sealed class PresentationDirector : MonoBehaviour, IPresentationBus {
    readonly Queue<GameEvent> _q = new();
    Coroutine _playing;
    public bool Idle => _q.Count == 0 && _playing == null;

    IEnumerator Play(GameEvent e) {
        var beat = _catalogue.Resolve(e);          // FxBeat: prefab(s), SFX cue, duration, blocking?
        _audio.Play(beat.Cue);
        foreach (var spawn in beat.Spawns) _pool.Spawn(spawn.Prefab, Anchor(spawn.Cell));
        if (beat.Blocking) yield return new WaitForSeconds(beat.Duration * _speedScale);
    }
}
```

- **Non-blocking beats** (damage numbers, small bursts, SFX) do not gate the queue; **blocking beats**
  (lunge 280 ms, card fly 300 ms, cut-in 1100 ms, ribbon 1500 ms) do.
- Reduced Motion collapses every blocking duration to ~0 and skips all particle spawns except pops and
  ribbons — matching the browser's four reduced-motion carve-outs.
- A `Skip` input (hold `Space`, or a settings "fast animations" slider 1×/1.5×/2×) scales `_speedScale`.
  **[NEW]** — PC players replaying a campaign will want it.

### 11.3 FX catalogue

`FxCatalogue` (ScriptableObject) maps `GameEvent` type + element → `FxBeat`. The 15 primitives of `09`
§17.1 become prefabs (`Fx_Pop`, `Fx_Slash`, `Fx_Ring`, `Fx_Burst`, `Fx_Shake`, `Fx_Hurt`, `Fx_Ribbon`,
`Fx_Arrow`, `Fx_AimArrow`, `Fx_Splash`, `Fx_Confetti`, `Fx_Flash`, `Fx_Trail`, `Fx_Fly`, `Fx_ElemShot`);
the 9 elemental impact compositions of §17.2 become **VFX Graph** assets whose authoring brief is that
table verbatim:

| Element | Signature |
| --- | --- |
| fire | 9 rising teardrop flames + a central plume |
| water | 10 droplets arcing out then **falling**, plus a splash ellipse |
| earth | 9 tumbling shards under gravity + a brown dust flash |
| wind | 3 spinning crescents + 8 direction-aligned streaks |
| forest | 9 sway-falling leaves + a thorn-whip lash |
| electric | a jagged bolt striking **down** from above + 10 square sparks |
| light | 8 rays at 45° + 5 rising motes |
| dark | **implosion** — 10 motes converge, then a violet void ring blooms after 170 ms |
| divine | oversized white flood + 10 gold/white rays at 36° + 4 rising motes |

The five generic CSS motion classes (`out`, `grav`, `in`, `sway`, `ray`) become five reusable VFX Graph
subgraphs, so a new element needs a palette and a particle shape, not a new graph.

Screen-space effects (hurt vignette, turn ribbon, splash reveal, confetti, battle cut-in) are UI Toolkit
elements + a URP full-screen pass — they must not live in world space or they will foreshorten.

### 11.4 Audio

**[REQ]** 23 cues, exact set and trigger points preserved (`09` §16). No assets exist — the table is an
audio design brief. Re-author as `.wav` clips; **do not** re-implement the Web Audio synth.

```csharp
[CreateAssetMenu] public sealed class AudioCue : ScriptableObject {
    public AudioClip[] Variants;          // round-robin/random to avoid machine-gun repetition
    [Range(0,1)] public float Volume = 1f;
    public Vector2 PitchJitter = new(0.97f, 1.03f);
    public bool Spatial;                  // board-anchored cues (impacts) vs UI cues (click, draw)
    public int MaxConcurrent = 3;
}
[CreateAssetMenu] public sealed class AudioCueBank : ScriptableObject {
    public AudioCue Click, Draw, Place, Set, Summon, Raise, Whoosh, Hit, Clash, Raze, Spell, Trap,
                    Mana, Train, TurnYou, TurnFoe, Win, Lose, Move, Block, Swing, Build, Shuffle;
}
```

Master gain 0.5 to match the browser's perceived level; two mixer groups (`SFX`, `UI`) under `Master` so
the settings slider and mute map cleanly. Spatial cues play at the cell anchor with a modest 3D blend
(0.3) — enough to place a clash on the left flank without making far-row events inaudible.

### 11.5 RNG hygiene

**[REQ]** Presentation randomness (scenery scatter, particle jitter, cue variant choice, dialogue line
selection, ember placement) must draw from a **separate** RNG stream from the simulation. In the browser
every one of these shares `Math.random()`, which means toggling FX changes the sequence the AI sees. The
view must never touch `IDeterministicRandom` from the core.

### 11.6 Damage numbers — the 3D answer

**[NEW]** Answers `09` §25 open question 6. Damage numbers are **world-space TMP** anchored at the cell,
billboarded to the camera, rising 0.6 u over 950 ms. To prevent the overlap problem the open question
worries about, `DamageNumberStack` keeps a per-cell stack index and offsets each subsequent number by
0.18 u laterally + 0.1 u vertically, and caps simultaneous numbers per cell at 4 (further hits merge into
a summed number). Correct perspective, no detachment, no pile-up.

---

## 12. Art pipeline

### 12.1 Source → project sync

The artist drops files into the repo's existing `assets/cards/<Type>/<Element>/<slug>_cardart.png` layout;
that convention is load-bearing and stays. An editor tool
(`Tools ▸ SpawnRow ▸ Sync Card Art`) mirrors `../../assets/cards/**` into
`Assets/SpawnRow/Art/Cards/**`, preserving relative paths, skipping unchanged files by hash, and reporting
adds/removals. Meta files (and therefore GUIDs) persist across syncs because paths are stable.

### 12.2 Import settings

| Asset class | Type | Max size | Compression | Mips | Pivot | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| `_cardart` | Sprite (Single) | **1024** | BC7 | **off** | Center | Square, opaque. Sources run up to 1.6 MB / 2048 px; 1024 is ample even for the 250 px inspect card at 4K. |
| `_fieldart` | Sprite (Single) | **512** | BC7 (alpha) | **on** | **Bottom** | Cut-out standees with alpha; bottom pivot reproduces `object-position: bottom`. Mips on because standees scale with camera distance. |
| Card frame chrome, icons, gems, chips | Sprite (Single/9-slice) | 256 | BC7 | off | — | Atlased. |
| Card backs, element frame skins | Sprite | 1024 | BC7 | off | Center | Optional per-element skins. |
| Board/scenery/decal textures | Default | 2048 | BC7 | on | — | |
| Globe tile textures | Default | 1024 | BC7 | on | — | |

`sRGB` on for all colour textures; `Read/Write` **off** everywhere (nothing samples pixels at runtime).

### 12.3 Atlasing and loading

- **Do not atlas `_cardart`.** 68+ cards × 1024² is ~200 MB uncompressed; they are used sparsely (a deck is
  ≤ 40 distinct cards). Load per-card via **Addressables**, grouped by element
  (`cards-fire`, `cards-water`, …, `cards-neutral`), so a mono-element match loads two groups.
- **Do atlas `_fieldart`** — one `SpriteAtlas` per element (2048 pages). A match can show a dozen standees
  at once and they are small.
- **Do atlas** all UI chrome, icons, element gems, chips into `UI_Common.spriteatlas`.
- Deck builder loads *all* element groups; that is exactly why it is its own scene (§2.1) — leaving it
  releases them.

### 12.4 The art table — resolving the 3-tier fallback at import time

**[REQ]** `09` §7's fallback chain is a real requirement (drop a file in and it appears), but the browser
implemented it by **404-walking at runtime** with a `FIELD_MISS` negative cache. In Unity it resolves once,
in the editor:

```csharp
[CreateAssetMenu] public sealed class CardArtTable : ScriptableObject {
    [Serializable] public struct Entry {
        public string Slug;                    // slugify(name): lowercase, drop leading "the ", strip non-alnum
        public AssetReferenceSprite CardArt;   // tier 1
        public AssetReferenceSprite FieldArt;  // tier 2a — a true cut-out
        public bool FieldArtIsBorrowedCardArt; // tier 2b — render as a FRAMED standee ('fromart')
        public bool UsesPlaceholder;           // tier 3
    }
    public Entry[] Entries;
}
```

Generator (`Tools ▸ SpawnRow ▸ Rebuild Card Art Table`) walks the *same* probe order the JS used —
typed folder then flat folder, `png, jpg, jpeg, webp` for card art and `png, webp, jpg` for field art (note
the extension orders genuinely differ) — and cross-checks every entry against
`docs/unity/spec/cards.json`'s precomputed `cardArtUrls` / `fieldArtUrls` arrays, which already encode the
exact candidate lists. It fails the build with a readable table of missing art rather than silently
falling through.

```csharp
public interface ICardArtResolver {
    Sprite CardArt(CardId id);
    Sprite FieldArt(CardId id, out bool isBorrowedCardArt);   // tier 2 → framed standee when true
    Sprite CardBack(ElementId owner);
    Sprite FrameOverlay(ElementId element);                    // optional skin, null when absent
}
```

**[REQ] the `fromart` distinction survives:** a borrowed square card art renders as a *framed* standee
(rounded corners, a border, shorter height) rather than a cut-out, so the player can tell "this unit has no
bespoke field art yet" at a glance — which is what drove the artist's workflow.

### 12.5 Placeholders

`02_art.js`'s parametric SVG generator does not port. Instead: one `PlaceholderArtTable` with a single
per-element "art missing" card frame (element-tinted background + kanji watermark + a silhouette that
scales with cost tier), authored as 9 sprites. `UsesPlaceholder` entries are listed by the validation tool
so missing art is a visible, tracked backlog rather than an invisible fallback.

---

## 13. Settings, accessibility, persistence

| Setting | Values | Default | Storage key |
| --- | --- | --- | --- |
| Board angle | Top-Down \| **Tilted** | Tilted | `angle` |
| Standees (Figures) | On \| Off | On (**force-on in Tilted**) | `standees` — **[NEW]** now persisted |
| Battle cut-ins | On \| Off | On | `cutins` |
| Response window | Off \| 3s \| 4s \| 6s | 4s | `respwin` |
| Master volume / mute | 0–100 / bool | 50 / unmuted | `volume`, `muted` — **[NEW]** now persisted |
| Reduced motion | On \| Off | Off | `reducedMotion` — Unity cannot read the OS pref portably |
| Animation speed | 1× \| 1.5× \| 2× | 1× | `animSpeed` **[NEW]** |
| Colorblind shapes | On \| Off | Off | `cbShapes` **[NEW]** |
| Key/gamepad bindings | rebind map | defaults | `bindings` (Input System JSON) |
| Surrender | two-step confirm | — | — |

All of it in one `settings.json` under `Application.persistentDataPath` (replacing the scattered
`srd.*` localStorage keys), with an explicit `schemaVersion` and a migration hook. Saved decks
(`srd.decks.v1`) and the campaign (`srd.campaign.v3`) get their own files, same treatment — **no
"delete the old key" wipes** (`08` §16 finding 14).

**Accessibility commitments for the Steam build** (all absent in the browser):
1. Full keyboard control of every action (§6.8), with a visible focus ring on board and UI.
2. Gamepad parity.
3. Colorblind secondary encoding on cell states (§3.3).
4. Reduced motion toggle gating the same four categories the CSS did.
5. UI scale slider (`PanelSettings.scale`) 0.8×–1.4× — card text at 4K is small.

---

## 14. Dead weight — explicitly not ported

Everything in `09` §21 plus the following, listed so nobody spends a day on them:

| Not ported | Reason |
| --- | --- |
| `fitBoard()` and `--extscale` | Replaced by `BoardFramer` (§4.2). |
| The 32° `board-tilt` middle angle | Locked decision: exactly two angles. |
| `renderMinions`, `workerChipRow`, `positionDeck`, `positionGrave`, `GUARDIAN_SVG`, `#conscriptBtn` (⚒ Train), the `#harvestPanel` colour-allocation UI | Dead code in the JS; verified uncalled. |
| The command-center card frame (`ccx`, COMMAND ribbon) | `findCC()` returns null. **Keep only the leader identity** (name, element, life, workers) in the left tower. If a campaign boss keep is ever added, it is a new prefab, not a resurrected frame. |
| Monkey patching, `cellElFor` reverse lookups, pre-mutation rect capture | Replaced by the event bus + stable transforms (§11). |
| `elementFromPoint` fallbacks, pointer-capture-on-`<html>`, `user-select`/`touch-action` suppression, `::selection`, tap-highlight | No Unity analogue. |
| `probeSleeves()` `Image()` existence probing, art 404-walking, `FIELD_MISS` | Resolved at import (§12.4). |
| Service worker / PWA manifest / `#rotateNote` / fullscreen+orientation lock | Web-only. |
| Container-query units | Replaced by baked card faces + panel scaling. |
| The globe's projection maths, painter sort, culling heuristics, skirt quads, `shade()`, `fit`/`fitTick` | §9.1. |

---

## 15. Verification

### 15.1 Visual parity checklist

Run the browser build and the Unity build side by side and confirm, in this order: cell state colours and
their semantics; standee up/laid poses across turn boundaries; the 0.24 s wall slide with its overshoot;
one wall open at a time; the foe wall staying down while aiming; hand rest/expand; card frame proportions
at all four scales; the deck/graveyard pile stacking; phase track lighting (including Combat lighting
*alongside* Action); the 44 px snap behaviour; the marquee green box; the aim arrow arc; splash reveal
threshold (cost ≥ 4 or First Strike); the turn ribbon.

### 15.2 Framing matrix

Confirm no scrolling/letterboxing and a fully visible hand at 1280×720, 1920×1080, 2560×1080 (21:9),
3440×1440, 3840×2160, and 1600×1200 (4:3), in both angle presets, with each wall state.

### 15.3 Automated

| Test | Assembly | What it asserts |
| --- | --- | --- |
| `BoardLayoutTests` | Core.Tests | `CellCenter` round-trips through `CellRef`; wall rows sit outside board bounds. |
| `HexSphereGoldenTests` | Core.Tests | 162 tiles / 320 corners; tile 0's centre and adjacency match a golden fixture; every corner ring is CCW. |
| `CardArtTableTests` | Editor | Every card in `cards.json` has an entry; every `UsesPlaceholder` is on a known-missing list. |
| `PresentationCoverageTests` | PlayTests | Every `GameEvent` type has an `FxCatalogue` entry (fails when a new event is added without presentation). |
| `IntentFunnelTests` | PlayTests | Every input path (click / key / gamepad / drag / marquee / snap) produces an identical command for the same target. |
| `ProjectorTests` | PlayTests | `CellProjector.ScreenRect` round-trips against `BoardRaycaster.TryPick` for all 35 cells at 6 aspect ratios in both presets. |
| `SceneSmokeTests` | PlayTests | Boot → MainMenu → Campaign → Battle → back, with no exceptions and no leaked scenes. |

---

## 16. Decisions taken here that design should confirm

1. **Square 1×1 world cells** (§3.1) rather than the browser's 0.74:1 — correct in Top-Down, near-identical
   in Tilted.
2. **Top-Down is a steep perspective camera, not orthographic** (§4.1) — required for a blended transition.
3. **Diegetic castle wall props are added at the virtual rows** (§3.6), with the HUD ♥ kept as a second
   route to the same command.
4. **Board card faces are baked from the same UXML card frame** (§5) rather than authored twice.
5. **Cell highlights live in the board shader**, not as overlays (§3.3), and gain a colorblind shape
   channel.
6. **AI declarations are rendered** with the same declAtk/declTgt/declBlk language the player's get (§11.1)
   — the browser silently never painted them.
7. **Damage numbers are world-space** with per-cell stacking (§11.6).
8. **Right-click becomes the global inspect gesture** (§6.7), hover-delay retained.
9. **Standees, volume and mute become persisted settings**; **animation speed** and **colorblind shapes**
   are new settings (§13).
10. **Board drags lift the unit in-world** instead of showing a 2D ghost (§6.5).
11. **Mouse-wheel zoom on the campaign globe** (§9.5).

Still open, inherited from the specs and *not* decided here because they are rules-adjacent:
whether the response window should exist at all in a pure single-player build; whether the harvest
allocation panel is truly dead (it is deleted here); whether the command-center frame ever returns for a
campaign boss.
