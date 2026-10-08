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
   characteristics, 6 trade resources) lives in exactly one place (`Systems/Ledger`, §4.3).
   Every module reads and writes it through that one API — nobody keeps a private duplicate
   of "how much wood the player has."
3. **Decoupling via events, not direct references.** The Trade module doesn't need a
   reference to the Laws module. Systems publish domain events on a lightweight event bus
   (`Systems/Events`); anyone who cares subscribes.
4. **Time is a seam, not `Time.time`.** Every timer in this game (law queue, trade refresh,
   resource regen, business storage) must survive the app being closed. Model these as
   "last-updated UTC timestamp + rate," compute elapsed progress on load/resume. Never use a
   countdown that only ticks while a MonoBehaviour's `Update()` is running.
5. **Feature-first folder structure.** The project is organized primarily by *module*
   (Laws, Trade, Economy, Cities, RandomOccurrences, Shop, Navigation), not by technical role,
   with the systems they all use (Ledger, popups, sound, save…) in `Systems/` and passive
   building blocks in `Shared/`. Changing Trade should mean working inside one folder, not
   hunting across scattered top-level role-based folders. This is what actually delivers
   "modifying one module shouldn't affect another" — see §3.
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
- **Unity Localization** — every user-facing string goes through a String Table.
  **Status: live for Laws and for the bottom nav bar** (locales `en` + `uk`); the other four
  modules have no content or Views yet and adopt the same rules when they do.

  ### How it works, in short

  A String Table is a spreadsheet of `key → text`, one column per language. To show text you
  need two things: **which table**, and **which key**.

  `ILocalizationService.Resolve(table, key)` returns the text in the player's language.
  Missing entries come back as `[key]` — visible, never blank, never an exception. There is a
  `Resolve(table, key, args)` overload for text with numbers in it.

  **You never type a table name or a key at the call site.** Both come from the type that owns
  the text, so there is exactly one place to change and nothing can quietly disagree:

  | The text you need | Where its table + key come from | Example |
  |---|---|---|
  | Belongs to a data asset (a card, a characteristic) | that asset's `*Definition` type | `LawCardDefinition.StringTable` + `card.TitleKey` |
  | Fixed label on a screen, never changes at runtime | nowhere in code — a `LocalizeStringEvent` component on the prefab | the ACCEPT / REJECT stamps |
  | Built in code but has no data asset (a popup, a toast) | a small `<Module>UIText` class in that module | `LawsUIText.StringTable` + `LawsUIText.RefillConfirm` |

  **When the player changes language**, the `LocalizeStringEvent` components update themselves.
  Text built in code does **not** — a Presenter must subscribe to
  `ILocalizationService.LocaleChanged` and re-render. `LawsPresenter` does this in its
  constructor and unsubscribes in `Dispose`; every new Presenter that resolves text must do the
  same, or its labels will sit in the old language while the chrome around them switches.

  **Nothing renders before localization has loaded** — `GameEntryPoint` waits (see below).
  Do not remove that wait: neither path recovers from starting too early.

  The rest of this section is the reasoning behind those rules and the traps that produced them.

  ---

  **Keys are derived from data, never authored as fields.** A law card's text lives at
  `{CardId}.title` / `{CardId}.flavor`; a characteristic's name at
  `characteristic.{enumname}`. Content assets therefore carry *no* `TitleKey`-style fields —
  the id already is the key. Nothing to wire, nothing to mistype, nothing to drift. The cost
  is that a missing *table* entry only shows up at runtime, which is what the **content
  tests** exist to catch (§8): `LedgerDataTests` for anything Ledger-owned, `LawsContentTests`
  and `TradeTextTests` for each module's own keys.

  **A table name and its key derivation are owned by the type whose text they address** —
  together, on the same type, and never re-declared by the code that reads them.
  `LawCardDefinition` owns `StringTable` + `TitleKey`/`FlavorKey` +
  `BuildTitleKey`/`BuildFlavorKey`; `CharacteristicDefinition` owns `StringTable` + `NameKey`
  + `BuildNameKey`. Presenters and the content tests both resolve *through those members*.

  A **registry is an index, not an owner**: `CharacteristicRegistry` maps an enum to its
  definition and offers convenience lookups, but declares no table name of its own. Splitting
  the table onto the registry and the key onto the definition makes it ambiguous which type
  owns the convention — they are one fact and belong together.

  This matters more than it looks. A test that re-spells `id + ".title"` locally still
  passes while the game asks for something else — a green test and blank text on screen,
  which is the exact failure the test exists to prevent. Resolving through one definition
  makes that divergence impossible rather than merely unlikely.

  **Text with no data asset behind it** — a confirmation popup, an error toast, "Upgrade
  warehouse for {0} gold?" — has no `*Definition` to own it, but still needs resolving in code
  because it takes arguments or is chosen at runtime. It gets a small static **text-keys
  type** in the module that owns the screen, holding the table name and the key constants
  together:

  ```csharp
  public static class LawsUIText            // Modules/Laws/Scripts/
  {
      public const string StringTable   = "LawsUITable";
      public const string RefillConfirm = "ui.refill_confirm";
  }
  ```

  **`public`, and a type of its own — not `internal`, and not nested inside the Presenter.**
  The content tests live in a *separate* assembly (`KingdomRuler.Tests.EditMode`), so an
  `internal` or privately-nested holder is invisible to them, and they would be forced back
  into re-typing the literals — which is the entire failure this rule prevents. The old
  `LawsPresenter.Tables` class was privately nested for exactly that reason and had to be
  replaced.

  Same rule, same shape as a `*Definition` — one declaration, imported by both the Presenter
  and the module's content test (`TradeTextTests` walks `TradeUIText.AllKeys` instead of
  repeating literals). Put it beside the Presenter that uses it; promote it out of the module
  only if the text itself becomes something a second module needs, by the usual ownership test.

  A string resolved **only** by a `LocalizeStringEvent` component on a prefab — a button
  caption, a tab label — has no code reference at all and is not checked: fixed chrome is on
  screen the moment its screen opens, so a gap there cannot go unnoticed. The moment a string
  is resolved from code, it goes on the module's keys type, and from there into its test.

  **Which table** — same ownership test as the leveling curve (§4.3): *would a second
  mechanic need the identical string?*

  | Table | Holds | Because |
  |---|---|---|
  | `Systems/Localization/Tables/SharedTable` | characteristic names, cross-cutting words | Cities and Random Occurrences need the identical strings |
  | `Modules/<X>/Localization/<X>UITable` | that screen's fixed chrome | small, stable, changes with the screen |
  | `Modules/Trade/Localization/TradeUITable` | Trade's chrome **and** its arg-taking messages | the first table in the project resolved from code, so it owns a `TradeUIText` keys type — see below |
  | `Modules/Navigation/Localization/NavigationUITable` | the bottom bar's tab labels | the middle row's pattern — Navigation is a module (§4.6). Nothing outside the nav bar draws the word "Laws", so these are chrome, not cross-cutting words. It is resolved only by `LocalizeStringEvent` components, so no code names it and no test checks it — the tabs are on screen from the first frame |
  | `Modules/<X>/Localization/<X>…Table` | that module's content, keyed by id | grows with authored content; what a translator is handed in bulk |

  **Who resolves it** — two paths, and the split is not stylistic:

  - **Static chrome** (fixed at author time, one GameObject: ACCEPT, REJECT, an empty-state
    line) → a `LocalizeStringEvent` component in the prefab. No code.
  - **Dynamic content** (derived from data, composed, or inflected: card title and flavor,
    characteristic name, anything with a count in it) → resolved in the **Presenter** via
    `ILocalizationService` and delivered through the existing display structs. It cannot use
    the component: the entry to fetch isn't known until runtime, a composed or pluralized
    string is not a single entry, and pointing a component at a card title would hand the
    View back the data reference the MVP split exists to remove.

  **Two hazards, both found the hard way — see `docs/modules/Laws.md` §6:**
  1. Localization initialises **asynchronously**, and neither path recovers from rendering
     too early. `GameEntryPoint` therefore waits on `ILocalizationService.WhenReady` before
     loading a scene. Don't remove that gate on the theory that placeholders are a
     transient first-frame flash — they are permanent.
  2. Subscribe to `SelectedLocaleChanged` **after** initialisation completes. A handler
     attached before it is silently never invoked.
- **DOTween** — UI animation. Lives in the **View** layer only. A Presenter tells a View
  "show accepted state"; the View decides *how*, including which DOTween sequence plays.
  Presenters and Managers should never construct a `Tween` directly — that's presentation
  detail leaking into logic.
- **UGUI + TextMeshPro** (no UI Toolkit). Split UI onto **separate Canvases by concern**:
  each of the 5 bottom-nav screens gets its own Canvas, and within a screen, anything that
  animates or updates frequently (a ticking currency counter, an animating law card) gets
  its own Canvas or nested sub-canvas so it doesn't force a rebuild of static sibling UI.
  A Canvas is the unit of *rebuild* — dirty one Graphic and every Graphic sharing that canvas
  re-batches — so the thing to isolate is whatever changes per frame. `Image.fillAmount` is
  the easy one to miss: it dirties vertices, so any tweened progress bar or ring re-batches
  its whole canvas every frame it animates.

  **A nested Canvas needs its own `GraphicRaycaster`.** Graphics register to the nearest
  Canvas, so the parent's raycaster cannot see them: split a panel onto its own Canvas and
  every button inside it goes dead, silently and with no error. Add the raycaster with the
  Canvas, and verify with an actual raycast rather than by invoking `onClick` — invoking the
  handler directly bypasses the raycaster and passes either way. `overrideSorting` is a
  separate concern: needed only when the sub-canvas must draw above its siblings, never for
  rebuild isolation.

  For the pixel-art look, prefer a bitmap-style font asset in TMP over a smooth vector font,
  to stay visually consistent with the art.
- **Newtonsoft.Json** — save serialization (see §5 for the IL2CPP caveat). Install via
  Unity's official package (`com.unity.nuget.newtonsoft-json`), not a manually vendored DLL.
- **Pixel-perfect camera:** URP's Pixel Perfect Camera component; fix a reference resolution
  and PPU up front and hold to it for all art.

## 3. Folder structure

`_Game/` has four top-level folders. Each has a one-line test, so a new piece of code has exactly
one place to go:

| Folder | Holds | The test |
|---|---|---|
| `Modules/` | one game mechanic or screen each — Laws, Trade, Navigation… | **nothing else may call it.** A module is a leaf. |
| `Systems/` | what any module may use — the Ledger, popups, sound, haptics, save… | **it does something:** it has state or behaviour, and you get it through injection |
| `Shared/` | passive building blocks — fonts, UI atoms, static helpers | **it is used directly:** referenced on a prefab or called statically, with no state of its own |
| `Core/` | the composition root (`Bootstrap/`) | the one place allowed to know every module, because it registers them (§4.1) |

**Dependencies run one way: `Core → Modules → Systems → Shared`.** One assembly per folder makes
the compiler enforce it — `KingdomRuler.Shared` references nothing of ours, `KingdomRuler.Systems`
references only packages, and each `KingdomRuler.Modules.<X>` references Systems and Shared but no
other module. A Shared helper that reached for the Ledger, or a system that reached for a module,
simply does not compile.

The test that settles the borderline cases: **if a module would need to call it, it cannot be a
module**, because modules never reference each other. Navigation is a module today because nothing
calls it; the first module that needs to switch screens itself (a "go to the Shop" button in a
popup, say) is the trigger to move it into `Systems/`.

A system keeps everything it owns together, even when part of it looks like something else:
`ConfirmPopup` stays in `Systems/Popups` although it is a UI component, and `SharedTable` stays in
`Systems/Localization/Tables` although it is data. Module-specific popups and string tables stay in
their module.

```
Assets/
  _Game/
    Core/
      Bootstrap/
        Scripts/            # Bootstrap scene entry point, root VContainer LifetimeScope and
                            # GameStateCoordinator — the classes that legitimately know every
                            # module (§4.1). Namespace KingdomRuler.Core.
    Systems/                # assembly KingdomRuler.Systems
      Events/               # EventBus — the pub/sub MECHANISM only, no message types (§4.2).
                            # Named Events, not EventBus: a namespace may not share its
                            # class's name, or C# resolves `EventBus` to the namespace.
      Ledger/
        Scripts/            # KingdomLedger (the one source of truth) at the root, then one
                            # folder per game resource holding everything about it — type,
                            # state, definition, registry and its rules. All one namespace,
                            # KingdomRuler.Systems.Ledger: the folders organize, they do not
                            # split the namespace (every caller would gain a using for nothing).
          Characteristics/  # …incl. CharacteristicLevelingCurve (the points-required formula)
                            # and CharacteristicLeveledUpEvent
          TradeResources/   # …incl. TradeResourceChangedEvent
          Gold/             # GoldChangedEvent — the rest of gold still lives in KingdomLedger
          Crystals/         # CrystalsChangedEvent — likewise
                            # Each resource's bus event lives in its folder (§4.2 tier 2).
        ScriptableObjects/  # mirrors Scripts/: one folder per resource
          Characteristics/  # the CharacteristicRegistry + one CharacteristicDefinition each
          TradeResources/   # the TradeResourceRegistry + one TradeResourceDefinition each
      Popups/               # the popup system — see §4.7
        Scripts/
          Core/             # the mechanism: PopupSystem, popup base classes, registry
          ConfirmPopup/     # one folder per kind of popup — see "Systems with variants"
        Prefabs/
        ScriptableObjects/  # PopupRegistry.asset
      Localization/
        Scripts/            # ILocalizationService, UnityLocalizationService
        Tables/             # SharedTable — strings more than one module needs (§2)
      Save/
        Scripts/            # ISaveService, LocalJsonSaveService, versioned DTOs, link.xml
      Purchasing/
        Scripts/            # IPurchasingService, MockPurchasingService
      Audio/
        Scripts/
        ScriptableObjects/  # AudioConfig + sound-clip registry
      Haptics/
        Scripts/
      Clock/
        Scripts/            # IClock + GameClock — the time, and the 4 Hz tick every
                            # time-driven system subscribes to (§4.5)
    Shared/                 # assembly KingdomRuler.Shared
      Fonts/
      UI/                   # UI atoms: buttons, toasts, currency pips
        Scripts/            # SafeAreaFitter — notch/gesture-bar insets (§4.6)
        Prefabs/
      Text/                 # TimeFormat — static formatting helpers
    Modules/
      Navigation/           # the bottom nav bar — see §4.6. 4 classes; it deals only in
        Scripts/            # ScreenId, and binds screens as plain GameObjects
        Prefabs/            # BottomNavBar.prefab
        Localization/       # NavigationUITable — the tab labels
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
        Localization/       # this module's String Tables: one for UI chrome, one per
                            # content type keyed by id (§2)
        ScriptableObjects/
          Config/           # LawsConfig.asset — queue cap, replenish time, crystal costs
          Data/             # one LawCardDefinition asset per card
        Prefabs/
        Images/             # sprites/textures for this mechanic (see note below)
      Trade/                # same internal shape as Laws.
                            # ScriptableObjects/Data/ stays empty: offers are generated, not
                            # authored, so there is no content table either (docs/modules/Trade.md)
      Economy/              # same internal shape as Laws
      Cities/                # same internal shape as Laws
      RandomOccurrences/      # same internal shape as Laws — the mechanic in GDD §10
      Shop/                    # same internal shape as Laws
    Scenes/
      Bootstrap.unity        # loads first: root LifetimeScope, services, then loads Main
      Main.unity              # the 5 screens live here as Views under their own Canvases,
                              # plus the BottomNavBar that toggles between them (§4.6)
  Tests/
    EditMode/
      Modules/               # mirrors Modules/ — one test folder per module
      Systems/               # mirrors Systems/ — Ledger/ plus the service tests and the
                             # shared test fakes (TestServiceFakes)
    PlayMode/                 # sparse — only for things that need the Unity runtime
docs/
  GDD.md
  ARCHITECTURE.md
  modules/
    Laws.md              # per-module architecture — see note below
    Trade.md             # ditto, for the Trade mechanic
    Popups.md            # the popup system
CLAUDE.md
```

The rule from the brief generalizes cleanly: **group by type within whatever folder you're
in.** Inside a module: `Scripts/` (further split by role — Domain, Events, Manager, Presenters,
Views) and `ScriptableObjects/` (further split into `Config/` and `Data/`). Inside a system:
`Scripts/`, plus `Prefabs/` or `ScriptableObjects/` only if it has any. **Each module and system
holds what it actually contains** — the folders are not stamped out identically, and an empty one
is not created for symmetry. Namespaces mirror the folders: `KingdomRuler.Systems.Ledger`,
`KingdomRuler.Modules.Navigation`, `KingdomRuler.Shared.UI`.

**This tree is the recommended shape, not an exhaustive whitelist.** A module may add asset folders
the tree doesn't list — `Images/` for that mechanic's sprites and textures, `Audio/`, `Fonts/` —
whenever it keeps the mechanic's assets next to the mechanic. It may also name its content folder
for what it holds (Laws uses `ScriptableObjects/LawCards/` rather than a generic `Data/`). What the
tree *is* strict about is the `Scripts/` role split and the `Config/` vs. content split (§7),
because those two carry real architectural meaning. Adding a sprite folder does not need a doc
update; adding a new **script role** or a new top-level folder under `_Game/` does.

### Systems with variants: split `Scripts/` into Core plus one folder per variant

Group by type at the top level as everywhere else — `Scripts/`, `Prefabs/`,
`ScriptableObjects/`. But when a system is made of **several variants of the same thing** —
kinds of popup, kinds of offer — split `Scripts/` further, by variant:

```
Systems/Popups/
  Scripts/
    Core/            the mechanism itself: base classes, the registry, PopupSystem
    ConfirmPopup/    everything code-side for one variant
    <Variant>Popup/  the next one, laid out the same way
  Prefabs/
  ScriptableObjects/
```

Without this, a variant's view, model and result type scatter across role folders and nothing
shows which files belong together.

The test for where a file goes: **delete a variant's script folder, and `Core/` must still
compile.** If it would not, something variant-specific has leaked into the core. Namespaces
follow the folders (`…Popups` for the core, `…Popups.Confirm` for the variant).

### Naming

- **A name says what the thing is responsible for** — not how it is implemented, not where it
  happened to be written first.
- **Call sites should read as a sentence.** `if (choice == ConfirmPopupChoice.Confirm)`
  reads; `if (result.Or(No) == Yes)` has to be decoded. When a call site needs a comment to be
  understood, rename before commenting.
- **Members of one family share a prefix**, so they sort together and nobody has to guess the
  odd one out: `ConfirmPopup`, `ConfirmPopupData`, `ConfirmPopupChoice`.
- **Paired operations get paired names** that make the difference obvious:
  `Choose(choice)` / `Close()`, `Show(data)` / `Ask(data)`.
- **One word, one meaning, project-wide.** "Manager" is a plain-C# module Model; "Event" is a
  message on the bus. Where something must break a convention, say so in its doc comment.
- **An object owns itself, not its surroundings.** A view is responsible for its own content,
  animation and lifecycle; where it sits relative to its siblings, or on a canvas it did not
  create, belongs to whatever owns that canvas. If a method on a child only makes sense in
  terms of the parent's layout, it belongs on the parent.
- **No unused public API.** Delete it. The exception is a deliberate hook with a known first
  caller, which says so in its doc comment — otherwise the next reader has to work out whether
  it matters.

### Per-module architecture docs (`docs/modules/`)

This document covers rules that apply to *every* module. Once a module grows past a handful of
classes, it gets its own file under `docs/modules/` describing its layers, its state model, its data
flows, and — most importantly — the decisions inside it that look arbitrary from the outside and
would otherwise be undone by accident. A **system** (`Systems/`) that grows past a handful of classes
gets one too — `docs/modules/Popups.md` — even though it is not a module; the folder is
"per-system architecture", not "per-module" in the §3 sense.
`docs/modules/Laws.md` is the worked example and the template
to follow; write the equivalent for a module when someone other than its author needs to extend it.

## 4. Core systems

### 4.1 Composition root (`Core/Bootstrap`)

The Bootstrap scene holds the root `LifetimeScope`. It registers, in order: shared services
(Clock, Save, Purchasing, Audio, Haptics, Localization), the Ledger, then each module's
Manager. This is the only place that constructs top-level services — everything downstream
receives what it needs through injection, it doesn't look anything up itself.

`GameEntryPoint` then runs, in this order, and the order is load-bearing:

1. **Hydrate** state from the save (`GameStateCoordinator.LoadOrInitialize`). Views render
   whatever they find on `Start`, so a module initialised after its View has already
   rendered shows a blank screen until the next change.
2. **Wait** for localization (`ILocalizationService.WhenReady`). Verified by removing it:
   the scene loaded first, and the chrome kept its authored English while every
   Presenter-built string sat on a `[key]` placeholder — permanently, long after
   initialisation finished. Neither path self-heals.
3. **Load** `Main` additively.
4. **Inject** each root of the loaded scene. The root scope lives in Bootstrap and
   VContainer only wires MonoBehaviours it can see in its own scene, so Views in an
   additively-loaded scene are invisible to it without this step. Doing it here — rather
   than giving `Main` a child scope — keeps the single-root-scope rule in §1.6 intact.

A service that needs asynchronous startup hooks into step 2 rather than inventing its own
gate. If a second one ever appears, that is the point to generalize `WhenReady` into a list
of awaited services — not before.

### 4.2 Event bus (`Systems/Events`) and where event types live

A minimal typed pub/sub, registered as a singleton in the root scope so anyone can inject it.
Presenters subscribe to update Views without polling.

**"Event" always means a message on this bus.** The mailbox mechanic in `GDD.md` §10 is called
*Random Occurrences* precisely so the word stays unambiguous — don't reintroduce "event" as a name
for that mechanic.

Event *types* live in one of three places, by who is allowed to subscribe:

**Tier 1 — `Systems/Events/`: the mechanism only.** `EventBus.cs` and nothing else. No message types
live here.

**Tier 2 — in the Ledger, next to the resource each one describes: cross-module ledger events.**

| Event | Folder (`Systems/Ledger/Scripts/…`) |
|---|---|
| `CharacteristicLeveledUpEvent` | `Characteristics/` |
| `TradeResourceChangedEvent` | `TradeResources/` |
| `GoldChangedEvent` | `Gold/` |
| `CrystalsChangedEvent` | `Crystals/` |

These describe changes to the one shared source of truth, so any module may subscribe — this is the
tier the Random Occurrences mechanic reaches across modules through. Each sits with its resource
because it is *about* that resource: whoever changes how a characteristic levels finds the event
that announces it in the same folder (§3, "one folder per resource"). The bus and the Ledger share
the `KingdomRuler.Systems` assembly, so this is a choice of folder, not an assembly constraint —
which is exactly why tier 1's "mechanism only" rule has to be stated rather than enforced.

**Every bus message type ends in `Event`.** Beside definitions, states and registries in a resource
folder, the name is the only thing that marks a type as a message on the bus — and it reinforces
that "event" means exactly that here. Call sites still read as a sentence:
`Subscribe<CharacteristicLeveledUpEvent>(…)`. (A plain C# `event Action` on a class is not a bus
message and keeps a plain name, like `LawsManager.QueueChanged`.)

**A cross-module event that is not about a Ledger resource has no home yet**, deliberately. Two such
types, `CityPurchased` and `RegionCompleted`, sat in a shared `Ledger/Events/` folder with no
publisher and no subscriber, and were deleted. Modules cannot reference each other, so the first real
one — say, Random Occurrences reacting to a city purchase — has to live in `Systems/`; it decides
where then, rather than a folder existing for it now.

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

### 4.3 The Ledger (`Systems/Ledger`)

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

- `CharacteristicLevelingCurve` (`Systems/Ledger/Scripts/Characteristics/`) is a **static class**:
  three constants (`BasePoints`, `GrowthFactor`, `RoundToNearest`) and the `GDD.md` §6 formula,
  `PointsRequired(level)`. These are rules of the Ledger, so they live in its code — nothing is
  wired, injected or configured, and retuning is a code change (`GDD.md` §6 says so).
- `PointsRequired` is **always strictly positive** — load-bearing, because the Ledger's level-up
  loop would otherwise never terminate. With fixed positive constants that holds by construction,
  and a test checks it across 200 levels so an edit to the constants cannot break it unnoticed.
- Only `KingdomLedger` calls it. Callers ask the Ledger (`PointsRequiredForLevel`,
  `PointsRemainingForNextLevel`), and `AddCharacteristicPoints` / `ReduceCharacteristicPoints` take
  `(type, points)` only — no caller can supply a curve of its own.

How it got here, so neither step is reintroduced: first a plain-C# `LevelingCurve` struct produced
by a `LevelingConfig` asset (to keep `UnityEngine` types out of the Ledger), then a single
`CharacteristicLevelingCurve` asset wired through the bootstrapper. Both cost wiring — a conversion
factory, a bootstrapper field, a test-only Ledger constructor, runtime validation of
designer-entered values — for something only the Ledger reads. Constants in its own code need none
of it.

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
valuation). Almost none of them belong in `Systems/Ledger`. Ask one question:

> **If a second mechanic performed this same operation, would the result have to match?**

- **Yes → `Systems/Ledger`.** The math governs how shared state changes, and divergence would be a
  bug. This set is small and mostly closed: the characteristic leveling curve (Laws and Random
  Occurrences both award points) and the trade-resource regen rate derived from warehouse capacity.
- **No → the module's own `Domain/`.** The math produces a number *that module* then asks the Ledger
  to move. The Ledger neither knows nor cares how it was derived, and nothing else computes it.
  `TradeConfig`'s warehouse curves, `BusinessCostCalculator`, `BusinessAccrualCalculator`,
  `CityAffordabilityChecker` and `CrystalBuyUpCalculator` are all this kind.

Worked example: the leveling curve is Ledger-owned because two mechanics award characteristic points
and a stat must level at one rate. Crystal buy-up pricing sits in `Modules/Laws/` even though it is
*about* levelling, because only Laws prices a buy-up — and per `GDD.md` §10 occurrences can never
touch crystals, so it cannot acquire a second consumer.

**Do not inject module calculators into the Ledger.** Two reasons. Structurally, `KingdomRuler.Systems`
is referenced *by* the modules; for the Ledger to hold a Trade calculator, `Systems` would need a
reference back to `Modules.Trade` — an assembly-definition cycle Unity rejects. Semantically, letting
one module supply the policy for shared state makes every other mechanic depend on that module's
rules, which is the drift the Ledger exists to prevent. Modules compute a number and hand it to the
Ledger; they never hand the Ledger a way to compute.

Every module's Manager queries and mutates state through this one object — nobody holds a
private copy. `CitiesManager.CanAfford(cost)` is a pure function over a `KingdomLedger`
snapshot plus a `CityCost` data object.

#### Shared *display* data: the registry pattern

The same ownership test decides where a thing's **presentation** lives, not just its math.
`CharacteristicRegistry` + `CharacteristicDefinition` (`Systems/Ledger/`) hold the icon and
name key for each of the 6 characteristics, because Laws draws them on its bars, Cities needs
them for purchase requirements, and Random Occurrences for outcome text. A module injects the
registry and asks; it does not re-derive.

**This is the pattern to copy** for any per-thing metadata more than one module displays —
trade resources were the obvious next one, and `TradeResourceRegistry` +
`TradeResourceDefinition` now follow it exactly (`resource.stone` … `resource.clay`, in
`SharedTable` beside the characteristic names). Three rules make it work:

1. **Name it for what it is, not for whoever needed it first.** `CharacteristicRegistry`, not
   `LawCharacteristicRegistry`. The name is what grants the next person permission to reach
   for it.
2. **Localization keys are computed, never serialized.** `CharacteristicDefinition.NameKey`
   derives from the enum member. A hand-typed `nameKey` field can be mistyped and can drift
   from the asset that declares it — the same reason `LawCardDefinition` has no `TitleKey`
   (§2). Only what genuinely cannot be derived — a sprite reference — is authored.
3. **A registry of separate assets, not one asset with inline blocks** (`CLAUDE.md` §7), and
   it is checked: a missing or duplicate type fails `LedgerDataTests`, and unassigned art is
   reported there as a warning (§8). A half-wired registry degrades rather than breaking —
   `NameKeyFor` falls back to the derivation, so text keeps working and only the icon is
   absent.

Presenters read the registry and pass the resolved sprite out on their display struct; Views
never hold the registry themselves, for the same reason they never hold a content asset.

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

**The Manager owns its timer and its schedule; the View only displays it.** A timer must never be
advanced from a View's `Update()` — that couples the mechanic's progress to a UI object being alive
and to which tab the player happens to be on. Persist the timestamp so the timer survives the app
closing. A Presenter may read remaining time to render a countdown; it must not be what makes the
countdown advance. A UI *sound* tied to a timer firing (a law card arriving, say) should only play
when that screen is open; the state change itself happens either way.

**The game clock ticks; time-driven systems subscribe.** `GameClock` (`Systems/Clock`) is the
`IClock` every manager already receives. Four times a second, on unscaled time, it raises
`Ticked(now)`, and each time-driven system settles itself to that `now`:

| Subscriber | Subscribed by | Settles |
|---|---|---|
| `LawsManager.AdvanceTo` | its own constructor | the replenishment slots |
| `TradeManager.AdvanceTo` | its own constructor | the offer refresh |
| `EconomyManager.AdvanceTo` | its own constructor | business storage |
| `RandomOccurrenceManager.AdvanceTo` | its own constructor | occurrence spawning |
| `KingdomLedger.AccruePassiveResourceRegen` | `GameBootstrapper` — the Ledger deliberately holds no clock | warehouse regen |

**Adding a time-driven module is one line in its own constructor**, `_clock.Ticked += AdvanceTo;`.
The clock knows none of its subscribers. This replaced `AccrualDriver`, a class in `Core` that
called each manager by name — and had silently missed the two modules added after it: Economy's
storage only filled on load and on collect, and occurrence letters only arrived at app start.

**Order between subscribers does not matter, and that is a rule rather than luck.** No tick reads
another system's result: the offer refresh does not read amounts, and neither does law
replenishment. **Whoever decides from a trade amount, or changes a capacity, settles regen
itself first** — `AcceptOffer`, both warehouse upgrades, and `TradeManager.LoadFromDto`, which is
the first moment capacities are final at load. Correctness therefore never depends on whether a
tick happened to land first. (The old driver justified itself by fixing an order between regen
and the timers; checked, that order protected nothing.)

**A failing subscriber does not stop the others.** `GameClock` calls each one separately and logs
an exception rather than letting it skip everyone after it — verified in Play mode with a
deliberately throwing subscriber.

**Subscribers never unsubscribe.** The managers and the clock are root-scope singletons with the
same lifetime. A manager starts ticking once it is constructed; all of them are, at startup,
because `GameStateCoordinator` needs them to load the save.

**One rate, 4 Hz, for everything.** These are minute-scale timers, so per-frame work would be
wasted battery (`GDD.md` §3). A subscriber that wanted less could count ticks; nothing does. UI in
particular does not use the clock: a View polls its Presenter and redraws only when the displayed
value changes, and popups refresh themselves (`docs/modules/Popups.md`). A slower "once a second"
event was considered and rejected — its phase is arbitrary against a deadline, so a countdown
driven by it would step unevenly and up to a second late. The tick rate also only decides how soon
a change is *noticed*: every amount settles from its stored timestamp, so a slower tick (the Editor
throttles unfocused) changes no result.

**What it costs: nothing measurable.** Every system is ticked whether or not its screen is open,
because GDD §13 says no mechanic pauses on another tab; each tick is a timestamp comparison until
something is due. Measured in Play mode: a full tick, all five subscribers, takes ~1.3 µs in the
Editor's Mono runtime (≈5 µs per second, against a 16 ms frame) and **allocates nothing** — no
subscriber allocates in steady state, and `GameClock` keeps its subscribers in an array copied on
subscribe rather than calling `GetInvocationList()` (~70 bytes) on every tick. While the app is in
the background Unity runs no player loop, so there are no ticks at all; the first one after resume
settles the absence. (Unity's Mono does not implement `GC.GetAllocatedBytesForCurrentThread` — it
reports 0 for anything — so measure allocation by heap growth with the GC count held constant.)

**No application focus/resume hook is needed, and its absence is deliberate.** Every system
derives elapsed time from a stored UTC timestamp, so the first tick after the app returns settles
the entire absence in one step. The save on focus loss writes each amount together with its own
matching timestamp, so a tick that last ran a fraction of a second earlier persists a slightly
stale pair that the next accrual covers exactly — no drift, no double-count.

**Continuous drift is polled; discrete changes go on the bus.** `KingdomLedger.
AccruePassiveResourceRegen` deliberately publishes **no** `TradeResourceChangedEvent`: at 4 Hz across six
resources that would be ~24 messages a second, forever, on every screen, to announce a delta of
roughly a third of a thousandth of a warehouse — precisely the idle battery drain `GDD.md` §3
forbids. The warehouse bars read the amount each frame and redraw only when the displayed value
changes, the same way the Laws countdown is rendered. Accepting an offer, upgrading a warehouse
and a Cities purchase all still publish normally. **"This mutation doesn't publish an event"
reads like a bug and is not** — see the doc block on that method.

### 4.6 Screen navigation (`Modules/Navigation`)

The bottom tab bar from `GDD.md` §13. It is a **module** — `Modules/Navigation`, assembly
`KingdomRuler.Modules.Navigation` — because nothing calls it: only the composition root registers
it, and it reaches the screens through serialized references rather than code (below). It is not
`Shared/UI/` either, because that is for passive UI atoms (buttons, toasts, `SafeAreaFitter`) while
this has a Model, Views and its own String Table.

**The trigger to move it into `Systems/`** is the first module that needs to switch screens in
code — a "Not enough crystals — go to the Shop?" button in a popup, say. A module cannot call
another module, so at that point Navigation stops being one (§3).

**Four classes, and that is the whole system:**

```
ScreenNavigator (plain C#, root scope)   Model: Current + Show() + CurrentChanged, + SFX/haptic
   ▲
BottomNavBarView (MonoBehaviour)         drives the tabs AND toggles the screens
   │                                     holds ONE array and ONE lookup: the tabs
NavTabButton ×5                          a ScreenId, the screen it opens, its own select tween

ScreenId                                 the shared vocabulary
```

**A tab carries the screen it opens, and availability is derived from that.** There is no
separate "is available" flag and no second list pairing ids to GameObjects — a tab is tappable
**iff** it has a screen assigned. That is the same reasoning `docs/modules/Laws.md` §4 applies to
the queue counts: two facts that must agree are two chances to disagree, so store one and derive
the other. An earlier draft had the flag on the tab and the bindings on the bar, which needed a
runtime check for "marked available but nothing bound"; that state can no longer be authored, so
the check is gone. Making a tab live is one action — drag its screen in.

A tab that should be disabled while its screen *does* exist (a progression lock) is a genuinely
different concept and gets its own `IsUnlocked` rather than being folded into this one.

An earlier draft split this across seven types — a `ScreenRoot` marker component on each screen
prefab, a separate `ScreenSwitcherView`, and a `NavigationUIText` holding the table name. All
three were removed as ceremony. Recording why, so they don't come back by reflex:

- **`ScreenRoot`** existed so a screen could declare its own id. But a serialized `GameObject`
  reference doesn't name a module type either, so the assembly-cycle argument never required it —
  the reference on each tab does the same job in one place instead of two. Reintroduce it only if
  something needs to ask a GameObject *which screen it is* at runtime; nothing does.
- **`ScreenSwitcherView`** did nothing but the `SetActive` loop, and `BottomNavBarView` was
  already subscribed to the same event. The cost of merging is that the nav-bar prefab
  carries a screens array overridden per scene — acceptable while `Main` is the only scene with a
  bar. If a second scene ever needs one, splitting it out again is the fix.
- **`NavigationUIText`** held a table name and a `"nav." + id` concat with **zero runtime
  callers** — the prefab stores literal keys in its YAML. Its only consumer was the Editor
  validator, which no longer exists: tab labels are fixed chrome, on screen from the first
  frame, and §2 does not check those.

**Navigation cannot reference another module.** Modules never reference each other (§3), so the
nav bar cannot hold a `LawsView`. Screens are therefore bound as plain `GameObject`s tagged with
a `ScreenId`. The payoff is concrete: **adding navigation required no change to `LawsView` at
all**, and moving Navigation between folders changed none of the screens either.

**Switching is `SetActive`, not a Canvas toggle.** That is what makes a screen's own
`OnEnable`/`OnDisable` fire, which is how `LawsView` already tells its Presenter to gate audio —
a card arriving while the player is on another tab updates the queue silently. Disabling only
the Canvas would keep those callbacks from running and leave each View's `Update()` ticking on a
screen nobody can see. The mechanic itself is unaffected either way: timers belong to tick
drivers, not Views (§4.5).

**Screens are authored *active* in `Main` and switched off in `BottomNavBarView.Start()`.** The
alternative — authoring them inactive — depends on whether VContainer's `InjectGameObject` walks
inactive children, and a boot sequence that silently leaves a View uninjected is a bad thing to
build on a maybe.

**The screen switch is a hard cut, and this is a known deviation from `GDD.md` §3**, which lists
"screen transition" among the animations required on every state change. An earlier version faded
the incoming screen in via a `CanvasGroup`; it was removed as unnecessary weight for now. The tab
button still animates on selection, so the tap is not without feedback, but the screens
themselves cut instantly. Re-adding it means a `CanvasGroup` on each screen root and one tween in
`ApplyScreens` — deliberately cheap to reverse. Decide before the mechanic is judged on feel.

**The nav bar's Canvas must sort above every screen.** Laws nests a `CardCanvas` at sorting order
10 with `Override Sorting`, so a nav bar left at the default 0 is drawn *underneath* the law
card. The prefab ships at 100.

**Safe area: the buttons are inset, the background is not.** `BottomNavBar.prefab` is structured
so those two are separate, because insetting both leaves the device's gesture-bar strip showing
the screen behind it as a mismatched band:

```
BottomNavBar (Canvas, sortingOrder 100)
└── SafeArea      SafeAreaFitter — vertical only, so the bar stays full-width
    ├── Background  Image, rect overshoots 500px BELOW the safe area  ← reaches the screen edge
    └── Bar          HorizontalLayoutGroup + the 5 tabs               ← sits on the safe floor
```

The overshoot is deliberate, not a mistake to tidy up: UGUI does not clip to the canvas, so the
excess falls off-screen, and 500 reference px comfortably exceeds any real bottom inset (~80px
in these 1080×1920 units). The invariant to preserve is that `Background`'s **top edge aligns
with `Bar`'s top** while its **bottom sits below the safe-area floor** — verified in Play mode on
a profile with a genuine 102px inset. `SafeAreaFitter` has `_applyHorizontal` off here for the
same reason: a full-bleed background must not be pulled in from the screen edges.

**Every screen follows the same split, plus one extra inset.** Background full-bleed, UI inset —
and because the nav bar sits on top of every screen, screen UI must also clear the bar or it ends
up underneath it (which is exactly where `LawsScreen`'s `TimerRow` was before this was fixed):

```
<X>Screen (Canvas)
├── Background     Image, full stretch — covers the notch and the gesture bar
└── SafeArea       SafeAreaFitter, padding.bottom = 160 (the nav bar's height)
    └── …all the screen's UI…
```

`SafeAreaFitter._padding` is a generic extra inset, in **canvas units**, applied on top of the
safe area. The component deliberately knows nothing about navigation — the 160 is authored per
screen and visible in the Inspector. Units matter here: the safe area is applied as *anchors*
(resolution-independent fractions) while the padding is applied as *offsets* (canvas units), so
the padding scales with the CanvasScaler exactly as the bar's own height does. Expressing the
padding in screen pixels instead would make the two drift apart on every device.

**If the bar height changes, update `padding.bottom` on every screen.** That duplication is the
accepted cost of keeping `SafeAreaFitter` a reusable UI atom rather than one that imports the
navigation module.

**Tabs and screens are both keyed by `ScreenId`, never by array index** — the same rule as the
Laws characteristic bars (`docs/modules/Laws.md` §6.6). Index binding survives a reorder and then
quietly shows the wrong screen.

**There is deliberately no Presenter.** It would mediate nothing: tab labels resolve through
`LocalizeStringEvent` components with no code (§2), a tab's availability is authored on its
button, and selection state is exactly `ScreenNavigator.Current`. Adding a pass-through layer
now is the speculative abstraction `CLAUDE.md` §1.4 warns against. **Add one** the moment a tab
needs derived state — a card-count badge, or availability driven by progression — because that
is where a View would otherwise start computing. Note that a card-count badge is also the
trigger named in §4.2 for promoting `LawsManager.QueueChanged` to a tier-2 bus event; the two
changes arrive together.

**Sound and haptics are raised by `ScreenNavigator`, not the Views**, matching `LawsPresenter`:
the plain-C# layer owns feel hooks, the View owns only DOTween (§2, §4.4). It also puts them
behind the same early-out that gates the event, so "tapping the tab you are already on is
silent" is covered by an EditMode test rather than by a guard each View could forget.

`ScreenId` carries all five screens from `GDD.md` §13 while only Laws and Trade are built; the
other three tabs simply have no screen assigned, which dims them and switches their Button off,
so the bar can never be asked for a screen that isn't there. **Active tab is
not persisted** — no save DTO involvement, no `schemaVersion` bump. The launch screen is a const
on `ScreenNavigator`, and should become `Kingdom` once that screen exists.

### 4.7 Popups (`Systems/Popups`)

A system every mechanic uses. Full detail: `docs/modules/Popups.md`.

A caller creates a popup by its type, gives it data, and awaits the button the player pressed;
the popup being open is a pause in the middle of one method:

```csharp
var choice = await _popups.Create<ConfirmPopup>().Ask(new ConfirmPopupData(title, body));
if (choice == ConfirmPopupChoice.Confirm) DoTheThing();
```

`Create` only builds the popup, hidden and without data. `Ask` (one question: show, wait for one
choice, close) or `Show` (a panel that stays open) is what fills it and puts it on screen.

`PopupSystem` lives in the Bootstrap scene so anything built at startup can use it. It owns the
stack and a single backdrop kept behind the topmost popup. Every kind of popup derives from
`Popup<TData, TChoice>` and differs only in the data it shows and the buttons it has.

Rules that reach beyond this system:

- **Live data is re-read, not pushed.** Pass a function instead of a value and the popup
  re-reads it a few times a second while open, redrawing only when the result changes. The
  function must only read. This replaced an event-driven design whose callers had to forward
  every relevant event plus run their own timer — see `docs/modules/Popups.md` §6.
- **Closing without a choice gives `null`** — the backdrop, `closeWhen`, a destroyed popup. It
  never equals a real choice, so it can never be mistaken for one.
- **The tap spends nothing.** A purchase is charged only after the confirmation, and the price
  is re-read at that moment rather than taken from what the popup displayed.
- **`UniTaskVoid` + `.Forget()`, never `async void`**, whose exceptions are unobservable. An
  awaited object that can be destroyed must release its awaiter when it is.

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

**Testability never outranks design.** If making a class testable would make it worse — an
interface invented for a fake, a dependency loosened to `null`, logic pulled out of the place
it belongs — don't. Write the better class and verify it in Play mode instead, and say so in
the module's doc so the gap is deliberate rather than an oversight. This is a standing
decision by the project owner, not a per-case judgement call.

What that does *not* license: skipping tests for plain-C# domain logic that is already
testable. `KingdomLedger`, the leveling curve, the shuffle bag, timer arithmetic and Presenter
state are all testable without distortion, and stay tested. The exemption is for code that
would have to be bent to fit — mostly MonoBehaviour and DOTween.

Where the exemption has been used, the module doc lists what was verified in Play mode
instead; `docs/modules/Popups.md` §7 is the worked example.

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
- **Content tests check what is authored**, not data a test builds for itself:
  `LedgerDataTests` (the registries and the names in `SharedTable`), `LawsContentTests` (every
  card's id and text, the refill messages) and `TradeTextTests` (every message in
  `TradeUIText.AllKeys`). They load the project's real assets and String Tables, so they run
  with every test pass — there is no menu item to remember. They replaced four Editor
  validators that did the same checks only when someone clicked them.

  **A wiring mistake fails; unfinished content warns.** A key the game asks for that exists in
  no locale, a missing string table, a type with no registry definition, two cards sharing a
  `CardId` — these are never a normal state, so the test fails. An untranslated entry or a
  missing icon is normal mid-project, so the test logs **one grouped warning** and passes.
  (Unity's NUnit is 3.5, which has no `Assert.Warn`; the Test Framework fails only on logged
  errors, so a logged warning shows in the console and the test output without failing.)
  `LocalizationCheck` in `Tests/EditMode/Systems/` implements that split for String Tables.

  Every module with authored content or code-resolved text gets one — Cities and Random
  Occurrences next. Fixed chrome on a prefab is not checked: it is on screen the moment its
  screen opens.
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

- Namespaces mirror the folder structure: `KingdomRuler.Core` (the composition root),
  `KingdomRuler.Systems.Audio`, `KingdomRuler.Systems.Ledger`, `KingdomRuler.Shared.UI`,
  `KingdomRuler.Modules.Laws`, etc. A folder must not share its main class's name
  (`Systems/Events/EventBus.cs`, not `Systems/EventBus/EventBus.cs`): C# would resolve the
  class name to the namespace inside every sibling namespace. One deliberate exception: the
  Ledger's per-resource folders (`Characteristics/`, `TradeResources/`) all share
  `KingdomRuler.Systems.Ledger` (§3).
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
