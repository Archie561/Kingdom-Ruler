# Architecture — Kingdom Ruler (Unity)

> Companion to `GDD.md`. That document says *what* the game does; this one says *how* it's
> built. Read both before touching code. If a design change in the GDD implies an
> architecture change, update this document in the same change — don't let them drift.

---

## 1. Guiding principles

1. **Domain logic is plain C#, not MonoBehaviours.** Leveling math, offer generation, cost
   curves, save/load — none of it needs `GameObject`, none of it should require the Unity
   Editor to be open to unit test.
2. **One source of truth per piece of state.** The resource ledger (crystals, gold, 6
   characteristics, 6 trade resources) lives in exactly one place (`Shared/Ledger`, §4.3).
   Every module reads and writes it through that one API — nobody keeps a private duplicate
   of "how much wood the player has."
3. **Decoupling via events, not direct references.** The Trade module doesn't need a
   reference to the Laws module. Systems publish domain events on a lightweight event bus
   (`Core/EventBus`); anyone who cares subscribes.
4. **Time is a seam, not `Time.time`.** Every timer in this game (law queue, trade refresh,
   resource regen, business storage) must survive the app being closed. Model these as
   "last-updated UTC timestamp + rate," compute elapsed progress on load/resume. Never use a
   countdown that only ticks while a MonoBehaviour's `Update()` is running.
5. **Feature-first folder structure.** The project is organized primarily by *module*
   (Laws, Trade, Economy, Cities, Events, Shop), not by technical role. Changing Trade
   should mean working inside one folder, not hunting across scattered top-level
   role-based folders. This is what actually delivers "modifying one module shouldn't
   affect another" — see §3.
6. **DI via VContainer, MVP for UI.** One root `LifetimeScope`, created in the Bootstrap
   scene, registers every shared service and every module's Manager. UI follows
   Model–View–Presenter: **Manager** (plain C#, the Model — owns a module's behavior and
   talks to the Ledger) → **Presenter** (plain C#, mediates Manager ↔ View, unit-testable)
   → **View** (MonoBehaviour, dumb — renders and raises input events, owns DOTween
   animations). Keep to one root scope; only add a child `LifetimeScope` if a module has a
   concrete, demonstrated need for a different object lifetime — don't pre-slice scopes
   speculatively.
7. **Two abstraction seams built in from day one, even though both are simple today:**
   - **Save:** `ISaveService`, currently backed by a local JSON file (Newtonsoft.Json, see
     §5). No gameplay code should know or care that it's local.
   - **Purchasing:** `IPurchasingService`, currently a mock that instantly succeeds. Real
     store billing later is a second implementation of the same interface.
8. **No premature machinery beyond the above.** No ECS/DOTS, no Addressables, until the
   project's content size actually requires them. VContainer is the one piece of "framework"
   this project takes on, deliberately, in exchange for testability and clean composition —
   don't add a second DI mechanism or a deep tree of nested scopes alongside it.

## 2. Project setup & core packages

- **Unity version:** latest Unity 6 LTS (6000.3.x / "Unity 6.3 LTS" as of mid-2026) — confirm
  the current recommended LTS in Unity Hub when you create the project.
- **Render pipeline:** URP with the 2D Renderer (pixel-perfect camera support, room for 2D
  lighting later if the cozy art direction wants it).
- **Input:** the new Input System package — all interaction is touch (swipe-to-decide,
  tap), no need for the legacy Input Manager.
- **VContainer** — dependency injection. Lightweight and fast relative to older Unity DI
  containers (e.g. Zenject), which matters on mobile where startup time and GC pressure are
  real constraints. One root `LifetimeScope` in the Bootstrap scene. Plain C# classes
  (Managers, Presenters) get constructor injection; MonoBehaviours (Views) can't be
  constructor-injected in Unity, so they get method injection via `[Inject]`, registered
  with `RegisterComponentInHierarchy`/`RegisterComponentInNewPrefab` as appropriate.
- **Unity Localization** — every user-facing string (law card text, city descriptions, UI
  labels, occurrence flavor text) goes through a String Table key. Retrofitting localization onto
  hardcoded strings later is expensive; routing through tables is nearly free.
  **Current status: deliberately deferred.** Content assets already carry key *fields*
  (`TitleKey`, `FlavorTextKey`), but the Views render those keys as literal text rather than
  resolving them through a String Table, and no tables exist yet. This is a known, accepted
  shortcut until the main mechanics are built — the localization pass happens in one go afterwards,
  which is cheap precisely because the key fields are already in the data model. Keep authoring new
  user-facing text as a key field; don't hardcode display strings into content assets.
- **DOTween** — UI animation. Lives in the **View** layer only. A Presenter tells a View
  "show accepted state"; the View decides *how*, including which DOTween sequence plays.
  Presenters and Managers should never construct a `Tween` directly — that's presentation
  detail leaking into logic.
- **UGUI + TextMeshPro** (no UI Toolkit). Split UI onto **separate Canvases by concern**:
  each of the 5 bottom-nav screens gets its own Canvas, and within a screen, anything that
  animates or updates frequently (a ticking currency counter, an animating law card) gets
  its own Canvas or nested sub-canvas so it doesn't force a rebuild of static sibling UI.
  For the pixel-art look, prefer a bitmap-style font asset in TMP over a smooth vector font,
  to stay visually consistent with the art.
- **Newtonsoft.Json** — save serialization (see §5 for the IL2CPP caveat). Install via
  Unity's official package (`com.unity.nuget.newtonsoft-json`), not a manually vendored DLL.
- **Pixel-perfect camera:** URP's Pixel Perfect Camera component; fix a reference resolution
  and PPU up front and hold to it for all art.

## 3. Folder structure

```
Assets/
  _Game/
    Core/
      Scripts/
        Bootstrap/         # Bootstrap scene entry point, root VContainer LifetimeScope
        EventBus/           # the pub/sub MECHANISM only — no event message types (§4.2)
    Shared/
      Fonts/
      UI/                   # shared UI atoms: buttons, toasts, currency pips, popups
        Scripts/
        Prefabs/
      Services/
        Save/
          Scripts/          # ISaveService, LocalJsonSaveService, versioned DTOs
        Purchasing/
          Scripts/          # IPurchasingService, MockPurchasingService
        Audio/
          Scripts/
          ScriptableObjects/  # AudioConfig + sound-clip registry
        Haptics/
          Scripts/
        Clock/
          Scripts/          # IClock abstraction — makes time-based logic testable
      Ledger/
        Scripts/            # KingdomLedger (the one source of truth) + state types +
                            # leveling math (the points-required formula) — shared by any
                            # module that mutates a characteristic (Laws today, Random
                            # Occurrences later)
          Events/           # cross-module ledger events — see §4.2 tier 2
        ScriptableObjects/  # starting-values config + leveling-curve coefficients
    Modules/
      Laws/
        Scripts/
          Domain/           # pure C#: card-queue and card-effect resolution specific
                            # to Laws, plus Laws-only math like crystal buy-up pricing.
                            # NOT the leveling curve — that's Ledger-owned, see §4.3.
          Events/           # module-local bus events — §4.2 tier 3. Only create this
                            # when a module actually needs one; Laws deliberately uses
                            # a plain event on its Manager instead.
          LawsManager.cs    # Model: orchestrates Domain + Ledger + queue timers
          Presenters/
          Views/
        ScriptableObjects/
          Config/           # LawsConfig.asset — queue cap, replenish time, crystal costs
          Data/             # one LawCardDefinition asset per card
        Prefabs/
        Images/             # sprites/textures for this mechanic (see note below)
      Trade/                # same internal shape as Laws
      Economy/              # same internal shape as Laws
      Cities/                # same internal shape as Laws
      RandomOccurrences/      # same internal shape as Laws — the mechanic in GDD §10
      Shop/                    # same internal shape as Laws
    Scenes/
      Bootstrap.unity        # loads first: root LifetimeScope, services, then loads Main
      Main.unity              # the 5 screens live here as Views under their own Canvases
  Tests/
    EditMode/
      Modules/               # mirrors Modules/ — one test folder per module
      Shared/
        Ledger/
    PlayMode/                 # sparse — only for things that need the Unity runtime
docs/
  GDD.md
  ARCHITECTURE.md
CLAUDE.md
```

The rule from the brief generalizes cleanly: **group by type within whatever folder you're
in.** Inside a module: `Scripts/` (further split by role — Domain, Events, Manager, Presenters,
Views) and `ScriptableObjects/` (further split into `Config/` and `Data/`). Inside
`Shared/Services/Audio`: `Scripts/` and `ScriptableObjects/`. Same pattern, applied
consistently, all the way down.

**This tree is the recommended shape, not an exhaustive whitelist.** A module may add asset folders
the tree doesn't list — `Images/` for that mechanic's sprites and textures, `Audio/`, `Fonts/` —
whenever it keeps the mechanic's assets next to the mechanic. What the tree *is* strict about is the
`Scripts/` role split and the `Config/` vs. `Data/` split (§7), because those two carry real
architectural meaning. Adding a sprite folder does not need a doc update; adding a new **script
role** or a new top-level folder under `_Game/` does.

## 4. Core systems

### 4.1 Composition root (`Core/Bootstrap`)

The Bootstrap scene holds the root `LifetimeScope`. It registers, in order: shared services
(Clock, Save, Purchasing, Audio, Haptics), the Ledger, then each module's Manager. This is
the only place that constructs top-level services — everything downstream receives what it
needs through injection, it doesn't look anything up itself. Bootstrap then loads `Main`.

### 4.2 Event bus (`Core/EventBus`) and where event types live

A minimal typed pub/sub, registered as a singleton in the root scope so anyone can inject it.
Presenters subscribe to update Views without polling.

**"Event" always means a message on this bus.** The mailbox mechanic in `GDD.md` §10 is called
*Random Occurrences* precisely so the word stays unambiguous — don't reintroduce "event" as a name
for that mechanic.

Event *types* live in one of three places, by who is allowed to subscribe:

**Tier 1 — `Core/EventBus/`: the mechanism only.** `EventBus.cs` and nothing else. No message types
live here.

**Tier 2 — `Shared/Ledger/Scripts/Events/`: cross-module ledger events.**
`CharacteristicLeveledUp`, `ResourceChanged`, `GoldChanged`, `CrystalsChanged`, `CityPurchased`,
`RegionCompleted`. These describe changes to the one shared source of truth, so any module may
subscribe — this is the tier the Random Occurrences mechanic reaches across modules through.

They live in `Shared`, not `Core`, and this is a hard constraint rather than a preference: they carry
`CharacteristicType` / `TradeResourceType`, which are `Shared.Ledger` types. `KingdomRuler.Shared`
references `KingdomRuler.Core`, so putting them in `Core` would require the reverse reference too —
an assembly-definition cycle Unity will reject.

**Tier 3 — `Modules/<X>/Scripts/Events/`: module-local events.** Namespace
`KingdomRuler.Modules.<X>`. Published and subscribed entirely inside one module. If something outside
the module needs to subscribe, that's the signal it belongs in tier 2, not a reason to reach into the
module's namespace. **No module has one today**, so the folder is created when a module first earns
it — see the next paragraph for why.

**Before adding a tier-3 event, check you need the bus at all.** When the only subscriber already
holds a direct reference to the publisher (a Presenter and its own Manager, say), a plain
`event Action` on the publisher is simpler, typed, and doesn't need an unsubscribe on the bus. Reach
for the bus when the publisher shouldn't have to know who's listening.

Worked example: the law card queue used to publish a `CardQueueChanged` message on the bus. Its only
subscriber was `LawsPresenter`, which is constructed with the `LawsManager` that published it — so it
is now `LawsManager.QueueChanged`, a plain event, and `LawsManager` no longer takes an `EventBus` at
all. The trigger to promote it back to tier 2 would be a subscriber outside Laws, such as a
bottom-nav badge showing how many cards are waiting.

### 4.3 The Ledger (`Shared/Ledger`)

One class, `KingdomLedger`, owns:
- Gold and Crystals (simple amounts)
- 6 `TradeResourceState` (amount, capacity, regen rate)
- 6 `CharacteristicState` (level, points-into-current-level, permanent level floor)

It also owns the **leveling math** — the points-required-per-level curve — even though today only
Laws exercises it. Anything that mutates a characteristic (Laws now, Random Occurrences later per
the GDD) calls into this one implementation rather than each module reimplementing or duplicating
it. A module can still own its own *config* for how it presents this to the player (e.g. Laws'
crystal buy-up divisor), but the curve itself is Ledger-owned.

**The curve is a formula, not a table, and the Ledger holds it — callers do not pass it in.**
Concretely:

- `LevelingCurve` is a plain C# value type in `Shared/Ledger` holding `basePoints`, `growthFactor`,
  and `roundToNearest`, with the formula from `GDD.md` §6. Keeping it a struct rather than a
  `ScriptableObject` keeps `KingdomLedger` free of `UnityEngine` types and trivially unit-testable.
- A `LevelingConfig` SO in `Shared/Ledger/ScriptableObjects/` holds the designer-editable
  coefficients and produces that struct. It validates in `OnValidate` — a non-positive base or
  growth would make the level-up loop non-terminating.
- `KingdomLedger` is constructed with the curve. `AddCharacteristicPoints` and
  `ReduceCharacteristicPoints` take `(type, points)` only.

The anti-pattern this replaces, and the reason it's spelled out: callers used to pass a
`Func<int, float>` per call. Laws built one from its own config while the occurrences module shipped
`level => 100f`, so **the same characteristic leveled at two different rates depending on which
mechanic touched it.** Any API that lets a caller supply the curve will drift this way again.

**Note on the permanent level floor.** `GDD.md` §6 guarantees a characteristic never drops below a
level it has reached. That is satisfied by clamping points at 0 within the current level — the floor
is always exactly the current level, so it is not tracked as a separate field. If a future mechanic
needs a floor that genuinely diverges from the current level, that's a real design change: update
the GDD first.

**Notify only once the mutation has settled.** The event bus is synchronous, so a subscriber runs
*inside* the call that changed the state. Finish mutating, then publish — never publish from inside
a loop that is still applying changes, or a Presenter can render a half-applied state.

#### Where new math goes — the ownership test

Every mechanic brings its own calculations (warehouse upgrade costs, business profit, offer
valuation). Almost none of them belong in `Shared/Ledger`. Ask one question:

> **If a second mechanic performed this same operation, would the result have to match?**

- **Yes → `Shared/Ledger`.** The math governs how shared state changes, and divergence would be a
  bug. This set is small and mostly closed: the characteristic leveling curve (Laws and Random
  Occurrences both award points) and the trade-resource regen rate derived from warehouse capacity.
- **No → the module's own `Domain/`.** The math produces a number *that module* then asks the Ledger
  to move. The Ledger neither knows nor cares how it was derived, and nothing else computes it.
  `WarehouseUpgradeCalculator`, `BusinessCostCalculator`, `BusinessAccrualCalculator`,
  `CityAffordabilityChecker` and `CrystalBuyUpCalculator` are all this kind.

Worked example: the leveling curve is Ledger-owned because two mechanics award characteristic points
and a stat must level at one rate. Crystal buy-up pricing sits in `Modules/Laws/` even though it is
*about* levelling, because only Laws prices a buy-up — and per `GDD.md` §10 occurrences can never
touch crystals, so it cannot acquire a second consumer.

**Do not inject module calculators into the Ledger.** Two reasons. Structurally, `KingdomRuler.Shared`
is referenced *by* the modules; for the Ledger to hold a Trade calculator, `Shared` would need a
reference back to `Modules.Trade` — an assembly-definition cycle Unity rejects. Semantically, letting
one module supply the policy for shared state makes every other mechanic depend on that module's
rules, which is the drift the Ledger exists to prevent. Modules compute a number and hand it to the
Ledger; they never hand the Ledger a way to compute.

Every module's Manager queries and mutates state through this one object — nobody holds a
private copy. `CitiesManager.CanAfford(cost)` is a pure function over a `KingdomLedger`
snapshot plus a `CityCost` data object.

### 4.4 Per-module Managers, Presenters, Views (`Modules/*`)

Each module (`LawsManager`, `TradeManager`, `EconomyManager`, `CitiesManager`,
`RandomOccurrenceManager`, `ShopManager`) owns the behavior specific to its mechanic — card
queueing, offer generation, business accrual — and calls into `KingdomLedger` to actually
move resources. Modules don't call each other directly; coordination happens through the
event bus, except for reads that legitimately need the shared Ledger (e.g. Cities checking
characteristics — that's reading the one shared source of truth, not module-to-module
coupling).

Each screen follows MVP: **View** (MonoBehaviour — layout, DOTween, forwards taps/swipes as
plain events) → **Presenter** (plain C#, injected with the module's Manager, subscribes to
its changes, decides what the View should show) → **Manager** (the Model). Presenters are
unit-testable without ever instantiating a View.

### 4.5 Offline/idle accrual

Every timer-driven system (law queue, trade offer refresh, resource regen, business
storage) stores a `lastUpdatedUtc` timestamp and a rate. On app resume and on load, each
system computes `elapsed = now - lastUpdatedUtc` **once** and fast-forwards state
accordingly (capped where a cap applies). This must not be simulated tick-by-tick — a
player who was away 10 hours shouldn't cause 10 hours of simulated frames.

**The Manager owns its timer; the View only displays it.** A timer must never be advanced from a
View's `Update()` — that couples the mechanic's progress to a UI object being alive and to which tab
the player happens to be on. Drive accrual from a VContainer entry point (`ITickable`) plus an
application-focus/pause hook, and persist the timestamp so the timer survives the app closing. A
Presenter may read remaining time to render a countdown; it must not be what makes the countdown
advance.

Pump these at a coarse interval (a few times a second), not every frame — these are minute-scale
timers and per-frame work on them is wasted battery. Note also that a UI *sound* tied to a timer
firing (a law card arriving, say) should only play when that screen is actually open; the state
change itself still happens either way, so the screen is correct when the player returns to it.

## 5. Save system

- `ISaveService` with `Save(GameStateDto)` / `Load() -> GameStateDto?`.
- **Current implementation:** local JSON file (Newtonsoft.Json) in
  `Application.persistentDataPath`.
- **⚠ IL2CPP caveat:** Newtonsoft.Json relies on reflection that IL2CPP's code stripping can
  remove on device builds, even though everything works fine in the Editor (which runs
  Mono/CoreCLR, not IL2CPP). Ship a `link.xml` preserving the Newtonsoft.Json assembly, and
  test save/load on an actual device build early — don't discover this the week before
  submission.
- **DTOs are versioned from day one** (`{ "schemaVersion": N, ... }`), and `GameStateDto` keeps a
  schema history comment recording what changed at each bump.
- **Migration is deliberately deferred.** No real player saves exist yet, so an unreadable save is
  discarded and a fresh one started rather than migrated. Keep bumping `schemaVersion` and recording
  what changed — that history is what the first real migration will be written from. Write that
  migration when there is player data worth preserving, which is **before the first external build**,
  not before.
- Domain objects are not serialized directly — map to/from plain DTOs at the save boundary,
  so `Domain/` and Manager classes stay free to evolve without fighting a serializer.
- Cloud save later (per the GDD, intentionally deferred) is a second `ISaveService`
  implementation plus a conflict-resolution policy — gameplay code doesn't change.

## 6. Purchasing / IAP abstraction

- `IPurchasingService` with something like `PurchaseCrystalPack(string packId) ->
  PurchaseResult` (async).
- **Current implementation:** `MockPurchasingService` — instantly returns success, caller
  credits crystals via `KingdomLedger`. No store SDK, no receipts.
- Real store billing later is a second implementation behind the same call sites — this is
  the seam that lets the whole crystal economy be built and balanced today without wiring
  App Store/Play billing yet.
- The Shop View/Presenter and no Manager should ever call a store SDK directly — always
  through `IPurchasingService`.

## 7. Config SO vs. content Data assets

Per module, two distinct kinds of ScriptableObject, kept separate on purpose:

- **`ScriptableObjects/Config/`** — one small asset holding *tunable parameters*: queue
  caps, timer durations, cost-curve coefficients, crystal-cost formulas. This is what
  changes when someone is balancing the game.
- **`ScriptableObjects/Data/`** — one asset **per content item** (one city, one business,
  one law card, one random occurrence). This is what changes when someone is adding content.

Keeping these separate means adding a city doesn't touch the same file another city's edit
touched (git-friendly, parallel-editing-friendly), and tuning a curve doesn't require
scrolling past 40 content entries to find the one number that matters.

## 8. Testing strategy

- **EditMode tests are the default and the bulk of coverage**, mirroring `Modules/` under
  `Tests/EditMode/Modules/`. Cover: leveling math (including that every mechanic awarding
  characteristic points levels at the same rate, and that points clamp at 0 within a level rather
  than dropping it — §4.3), offer
  generation ratio (statistically, across many generated batches), cost curves, offline
  accrual math, city afford-checks. Presenters are plain C# and testable directly; VContainer's
  constructor injection makes it easy to hand a Presenter a fake Manager or fake Ledger in a
  test instead of the real one.
- **PlayMode tests are the exception** — only for things that genuinely need the Unity
  runtime (a scene loads and wires up without null refs).
- **Not worth testing:** exact pixel layout, DOTween easing feel, art. Polish is verified by
  playing the game.

## 9. Mobile performance notes

- Object-pool anything spawned repeatedly (law cards, floating "+resource" popups, particle
  bursts).
- Split Canvases by concern (§2) — this is the single highest-leverage UGUI performance
  practice for a UI-heavy mobile game like this one; an animating element on its own Canvas
  doesn't force a rebuild of everything else.
- Batch sprites into atlases per screen.
- Idle accrual math runs on resume/load, not every frame — keep it that way, don't let it
  drift into `Update()`.
- Prefer event-driven Presenter updates over `Update()` polling for anything that isn't
  animating every frame.
- No physics needed for the card-swipe UI — a UI drag gesture plus a DOTween tween, not
  `Rigidbody2D`.

## 10. Coding conventions

- Namespaces mirror the folder structure: `KingdomRuler.Core`,
  `KingdomRuler.Shared.Services.Audio`, `KingdomRuler.Shared.Ledger`,
  `KingdomRuler.Modules.Laws`, etc.
- PascalCase for types and public members, camelCase for locals/parameters, `_camelCase` for
  private fields.
- One public type per file, file name matches type name.
- Constructor injection for plain C# (Managers, Presenters); `[Inject]` method injection for
  MonoBehaviours (Views) — never try to constructor-inject a MonoBehaviour, Unity won't call
  that constructor.
- Prefer small interfaces (`ISaveService`, `IPurchasingService`, `IClock`) over base classes.
- No `static` mutable game state outside what the composition root owns — no
  singletons-by-convention scattered through the codebase.

## 11. Suggested `.gitignore` additions (Unity-specific)

```
[Ll]ibrary/
[Tt]emp/
[Oo]bj/
[Bb]uild/
[Bb]uilds/
[Ll]ogs/
[Mm]emoryCaptures/
*.csproj
*.sln
.vs/
.vsconfig
```

Never commit `Library/`. Do commit `.meta` files for everything that has one.
