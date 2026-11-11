# Brief: plan the Economy mechanic

You are starting a new session on **Kingdom Ruler**, a Unity 6 2D portrait mobile game (pixel-art
kingdom sim). Two mechanics are built: **Laws** (the reference module) and **Trade** (rebuilt in the
same architecture, the most recent and closest analogue). **Your task now is to write an
implementation plan for the Economy mechanic (GDD §8) as a Markdown file at
`docs/plans/economy-plan.md`.**

**In this session, create only that file.** Do not modify any code, asset, prefab, scene or doc.
Reading is unrestricted; Unity MCP may be used read-only (inspecting prefabs, scenes and assets,
running the existing tests).

**How the work proceeds after that.** The user reviews the plan, then asks for **one step per
prompt**. Each step is planned in detail and implemented in its own session. So the plan describes
each step **in general terms**: its goal, what it touches, and how it is verified. It leaves the
detailed design (exact APIs, prefab hierarchies, numbers) to the step itself. Do not start a step
until the user asks for it. When a step is finished:
1. Mark it **✔** in the plan, with a short note of anything that turned out differently, and keep
   the progress line at the top of the plan current.
2. Give the user a **detailed breakdown**:
   - what changed and why;
   - how it was verified: compile clean, the test count, and Play mode where relevant;
   - the full list of changed files (the user handles git);
   - anything that needs their decision.

The user handles git — never commit, stage or reset anything.

---

## 1. Read everything before planning

Read these in full, in this order:

1. `CLAUDE.md` — working rules, the autonomy tiers (🟢/🟡/🔴), the definition of done.
2. `docs/GDD.md` — **§8 is the spec**, rewritten with the user's decisions (§3 below). Also §2
   (pillars), §3 (feel), §4 (core loop), §5 (glossary), §9 (Cities spend gold), §10 (occurrences
   touch the gold balance), §12 (monetization), §13 (screens).
3. `docs/ARCHITECTURE.md` — especially:
   - §2 (UI and localization);
   - §3 (folders, and "Inside a module");
   - §4.2–§4.7 (events, the Ledger, MVP, offline accrual, navigation, popups);
   - §5 (save), §7 (config vs content assets), §8 (tests), §10 (comments and docs).
4. `docs/modules/Laws.md` — the reference module, especially its content assets and derived
   localization keys.
5. `docs/modules/Trade.md` — the closest analogue. A business storage is a Trade warehouse in
   disguise (§3.4 below); the warehouse popup is the business panel's model.
6. `docs/modules/Popups.md` — `Ask` for a question; `Show` + a `WaitForChoice` loop for a panel.
7. Code:
   - **Trade:** `Assets/_Game/Modules/Trade/Scripts/**`, especially `TradeManager`,
     `TradePresenter` (`OpenWarehouse`, `ConfirmUpgrade`), `TradeScreenView`, `ResourceTileView`
     and `WarehousePopup`; and its tests.
   - **Laws:** `Data/` (content assets, `LawsConfig`) and `LawsContentTests`.
   - **Ledger:** `Systems/Ledger` — `KingdomLedger` (gold is a `long`, crystals an `int`),
     `GoldChangedEvent`, `CrystalsChangedEvent`, `TradeResources/TradeResourceState.cs` (the
     whole-unit regen), and `Tests/EditMode/Systems/Ledger/TradeResourceRegenTests.cs`.
   - **Save:** `Systems/Save/Scripts/Dto/*` (`GameStateDto.CurrentSchemaVersion` is 8) and
     `WholeNumberJsonConverter`.
   - **Core:** `Core/Bootstrap/Scripts/GameBootstrapper.cs`, `GameStateCoordinator.cs`.
   - **Navigation:** `Modules/Navigation/**` (`ScreenId.Economy` exists; its tab has no screen yet).
   - **Shared:** `Shared/Text/TimeFormat.cs`, `Shared/UI/Scripts/SafeAreaFitter.cs`.
   - **Main.unity:** the Laws and Trade screen instances, and the nav bar.

---

## 2. Established principles (do not re-litigate; the docs above have the detail)

- **Module shape:**
  - `Scripts/Data/` holds what designers author;
  - `Scripts/Model/` holds the mechanic in plain C#;
  - `Scripts/UI/` holds the Presenter, the screen View, `<X>UIText` and one folder per UI component;
  - namespaces are `KingdomRuler.Modules.Economy` and `….Economy.UI`;
  - the composition root registers only the config, the Manager and the Presenter.

  The shape is the default, not a cage: if Economy genuinely needs something different, **propose
  it in the plan as an open question** rather than forcing it or quietly deviating.
- **The Manager is the only class touching the Ledger or the clock.** Each price and affordability
  check sits beside the method that charges it, and every charge re-reads its price. Tests assert
  quoted == charged.
- **Whoever decides from an accruing amount, or changes a rate, settles accrual first.**
- **Polled vs pushed:** continuous drift (accrual) publishes no event, and views poll and redraw on
  change; discrete changes are pushed. A per-frame view-data struct carries no strings.
- **Content:** one ScriptableObject asset per content item under
  `ScriptableObjects/<Content>/`; `Config/` holds tunables only. A content asset holds **no literal
  text**: its keys are derived from its id.
- **Popups:** no close button; a tap outside closes the topmost popup; data is a function re-read at
  4 Hz, with `IEquatable` popup data; nothing is spent on open.
- **Save:** DTOs live in `Systems/Save/Scripts/Dto`. Bump `schemaVersion` on any shape change. No
  migrations until the first external build. Loading is defensive: clamp, skip unknown, never fail
  the whole save.
- **Feel:** every action gets animation and sound; one haptic per action, never stacked; passive
  sounds only on a visible screen. DOTween only in Views.
- **Comments and docs** describe the code as it is, never its history.
- **User preferences:**
  - short lambdas inline;
  - UniTask, never `Task`;
  - anything touching `IPurchasingService` is 🔴;
  - deleting or bulk-overwriting prefabs, scenes or ScriptableObjects is 🔴;
  - a new package is 🔴.

**One rule changes with Economy.** Trade states "every purchase is confirmed". The project rule is
now: **crystal purchases are always confirmed; gold purchases of businesses are not** (GDD §8).
The plan's docs step must make that explicit in the docs.

---

## 3. The spec and the decisions behind it

GDD §8 is the source of truth; read it first. The user settled these points in discussion. Treat
them as decided:

### 3.1 Production and storage
- **Linear production, flat price.** Every unit of a business type produces the same base rate, so
  *n* units produce *n* × the rate. **The price per unit never grows** with the number owned.
- **One storage per business type.** Its capacity is 24 h of the type's total production, and one
  Collect empties it. A full storage stops filling. Storages fill while the app is closed.
- **Buying a unit** settles accrual, then raises the rate and the capacity; the partial coin in
  progress carries over.

### 3.2 Collecting and buying
- **Collect is per type**, with no "collect all", and it happens **inside the business panel**,
  which a tap on a business opens. The user chose this deliberately: it lengthens sessions.
- **All businesses are available from the start.** There are no unlocks.
- **Buy ×1 / ×10 / Max** in the panel. Max is as many as the player can afford.
- **Gold purchases are not confirmed**; they happen on the tap, with sound, animation and a haptic.
- **Not enough gold → a popup offers to cover exactly the missing gold with crystals:**
  - the price is `ceil(missing ÷ gold-per-crystal)`, at least 1 crystal;
  - the rate is a value on `EconomyConfig`;
  - with Max and nothing affordable, it offers a single unit;
  - **one confirmation** spends the crystals, adds the missing gold and completes the purchase, as a
    single transaction on the Manager;
  - short of crystals: a notice, with Confirm inactive.

  This is a **new crystal sink**, already written into the GDD (§5, §8, §12). It spends crystals
  through the Ledger; it does **not** touch `IPurchasingService`.

### 3.3 Starting state, numbers and display
- **A new game starts with exactly the price of the cheapest business.** Derive it from the content,
  not a second number that can drift, and decide which class grants it at new game.
- **Names are gold-themed** (bakery, tavern, tailor, market, jeweller, bank…), never
  quarry/sawmill/mine, which would read as Trade resources. Names are cheap to change later.
- **Big numbers are abbreviated** for display (1.2K, 3.4M…, localized; the suffixes need
  translating). Every amount has a **hard ceiling** far beyond play (the gold balance, units owned
  per type) so nothing overflows. Trade keeps its full `N0` numbers.
- **The total gold is shown on the Economy screen itself**, as a temporary widget. It moves to a
  global header later, which will be a `Systems/` entry and is **out of scope** here. Keep the
  widget trivially removable.
- **"Business full"** plays a sound when the screen is visible (GDD §3).

### 3.4 Accrual: one shared helper
A storage holding 24 h of output fills in exactly 24 h — **the same whole-unit math as a Trade
warehouse** (`TradeResourceState.AccruePassiveRegen` and its read-outs). Extract that algorithm into
**one pure, tested helper** in `Shared/` that both the Ledger and Economy use.
- **Proposed name:** `Shared/Accrual/WholeUnitAccrual.cs`, namespace `KingdomRuler.Shared.Accrual`.
  It is a store that fills from empty to full over a fixed period, one whole unit at a time, with the
  partial unit kept in a timestamp. A clearer name is welcome; say why.
- **Overflow is the trap.** Today's arithmetic (elapsed ticks × capacity) is exact in `long` only
  while capacity stays under about 10.6 million — which is why `KingdomLedger.MaxWarehouseCapacity`
  exists. A gold storage can be far larger. The helper needs arithmetic that stays exact up to the
  gold ceiling, and tests at that ceiling.
- The Ledger's switch to the helper must be **behaviour-preserving**: every existing regen test
  passes unchanged.
- `KingdomRuler.Systems.asmdef` does not reference `KingdomRuler.Shared` yet. The dependency
  direction allows it (Systems → Shared), but it is an asmdef change: say so in that step.

### 3.5 Kept for later — do not build
These ideas are recorded in GDD §8: a growing price (×1.15 per unit), milestone bonuses, collect
all, unlocks, occurrences acting on one business's storage, a nav-tab badge, and production
multipliers from region bonuses (§9) and Shop boosters (§11).

Build none of them. **One seam matters:** keep the rate calculation in one place, so a multiplier
from outside the module can be applied there later. Name that seam in the plan, and note that a
multiplier owned by another module will need a shared home, since modules cannot call each other.

**Also out of scope:** the global header, selling businesses, ads, real-money gold, and Random
Occurrences.

### 3.6 Tier flags you will meet
These need the go-ahead inside their step:
- **🟡** a new module and new ScriptableObject types;
- **🟡** any `KingdomLedger` API change, such as the gold ceiling;
- **🟡** the save DTO change (new `EconomyStateDto`, `schemaVersion` 8 → 9);
- **🟡** a new folder under `Shared/`, and the asmdef reference.

Editing `Main.unity` (the screen instance, and the nav bar's Economy tab `NavTabButton._screen`) is
a scene edit: coordinate it, one person per scene.

---

## 4. What the plan must contain

Write `docs/plans/economy-plan.md` with:

1. **A progress line** at the top (`steps done: none; next: step 1`), kept current by every later
   session.
2. **Summary** — the goal, the scope, and what is explicitly out of scope (§3.5).
3. **Target structure** — the folder tree for `Modules/Economy/` and anything new in `Shared/` or
   `Systems/`, with each class's responsibility in a sentence. No detailed APIs.
4. **Ordered steps**, each with:
   - its goal;
   - what it touches;
   - its tier flags;
   - what "done" means;
   - its verification checkpoint: compile clean and EditMode tests run via Unity MCP, plus a
     Play-mode pass for steps with UI.

   Every checkpoint leaves the project compiling and every test passing. A sensible order, which you
   may change if you say why:
   1. the shared accrual helper and the Ledger moving onto it;
   2. the gold ceiling;
   3. the data types, content assets and String Tables;
   4. the model, save and bootstrap wiring, with tests;
   5. number abbreviation;
   6. the presenter and view data, with tests;
   7. the prefabs, the screen in `Main` and the nav tab, with a full Play-mode pass;
   8. the docs.
5. **Proposed defaults, each marked [ASSUMED — CONFIRM]:**
   - about six placeholder businesses, each with a rate and a price that give a sensible first hour;
   - the gold-per-crystal rate;
   - the ceilings;
   - the abbreviation scheme.

   Show the arithmetic briefly: the first purchase, what an hour away earns, when the second
   business becomes affordable.
6. **Feel** — sound, animation and haptic for collect (sound and animation, **no haptic**), buy,
   the crystal top-up, a refusal, and storage full.
7. **Save** — what is persisted per business, the `schemaVersion` bump, defensive load (unknown
   business ids, damaged numbers, clamping).
8. **Tests** — what each step adds, and what can only be verified in Play mode.
9. **Docs** — a new `docs/modules/Economy.md` in the `Laws.md`/`Trade.md` outline, plus the
   `ARCHITECTURE.md` sections and the GDD §13 build status to update.
10. **Risks** and **open questions** for the user.

Then give the user a short summary of the plan and the open questions.

---

## 5. Practical notes for this project

**Unity MCP and `execute_code`**
- `execute_code` compiles with **C# 6 (CodeDom)**: no local functions and no tuples.
- Write `UnityEngine.Object`, since plain `Object` is ambiguous.
- Call DOTween extension methods statically.
- A lambda parameter must not share a name with a later local.
- `AssetDatabase.DeleteAsset` is blocked by its safety check; use `manage_asset delete`.
- Moves and renames go through the AssetDatabase, so GUIDs survive.

**Checking compiles**
- **`read_console` does not show compiler errors.** Use `%LOCALAPPDATA%\Unity\Editor\Editor.log`:
  1. note its line count;
  2. call `AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate)` and
     `CompilationPipeline.RequestScriptCompilation()` via `execute_code`, then `refresh_unity`;
  3. grep the new lines for `error CS|warning CS|Tundra build success`.
- **An unfocused Editor may not compile at all.** Check that the test count changed as expected, or
  compare the assembly timestamps. If it is stuck, ask the user to click into Unity once. A test
  run on stale assemblies "passes" with the old count.

**Play mode**
- Always enter from the **Bootstrap** scene, with `Application.runInBackground = true;` in the first
  `execute_code`.
- Afterwards, `PlayerSettings.runInBackground = false; AssetDatabase.SaveAssets();` and confirm
  `git diff ProjectSettings/` is empty.
- **Back up** `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Kingdom Ruler\kingdom_ruler_save.json`
  before every Play-mode pass and restore it byte for byte afterwards. Take a fresh backup each
  session, because the user plays between sessions.
- Tap through real `EventSystem.RaycastAll` + `ExecuteEvents`, never by invoking `onClick`, which
  bypasses the raycaster.
- Game-view sizes `KR 9:20` / `KR 9:16` / `KR 3:4` are indexes 8 / 9 / 10 of
  `UnityEditor.GameView.selectedSizeIndex`; leave it on 9:16.
- `StubAudioService` and `StubHapticService` log every call to `Editor.log`, so sound and haptic
  counts can be verified.

**UI and assets**
- A UGUI `Image` with **no sprite ignores its Filled type** and draws a full bar. Give fill bars a
  sprite, as Trade's `Images/BarFill.png` does.
- Popups start as a copy of `Systems/Popups/Prefabs/ConfirmPopup.prefab` and must be added to
  `PopupRegistry.asset`.
- Screens in `Main` are authored active; their `SafeArea` has bottom padding 160 for the nav bar.
- After rebuilding a placed prefab, *Remove Unused Overrides* on its instance.
- String Tables are edited through `LocalizationEditorSettings.GetStringTableCollection(...)`, in
  `en` and `uk`.

**Other**
- Multi-line file edits: a Python script run with `python -I`; bash heredocs with quotes break.
- **Tests:** 469 EditMode tests pass at the start of this work.
- **Known noise:** the TextMesh Pro fallback-font asset churns on its own — not ours, don't revert
  it. Expected test warnings: missing icons, corrupt-save cases.
