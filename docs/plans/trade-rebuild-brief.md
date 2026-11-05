# Brief: plan the rebuild of the Trade module

You are starting a new session on **Kingdom Ruler**, a Unity 6 2D portrait mobile game (pixel-art
kingdom sim). The previous session rebuilt the Laws module into the project's reference
architecture, cleaned up `Systems/`, `Core/`, `Shared/` and `Modules/Navigation`, and audited the Trade
module. **Your task is to write a detailed, step-by-step implementation plan for rebuilding Trade
from scratch — as a Markdown file at `docs/plans/trade-rebuild-plan.md`.**

**You must not modify any code, asset, prefab, scene or doc other than creating that plan file.**
Reading is unrestricted; Unity MCP may be used read-only (inspecting prefabs, scenes, assets,
running the existing tests). The implementation happens in a later session, from your plan, after
the user approves it.

The user handles git — never commit, stage or reset anything.

---

## 1. Read everything before planning

Read these in full, in this order, before writing a line of the plan:

1. `CLAUDE.md` — working rules, autonomy tiers (🟢/🟡/🔴), definition of done.
2. `docs/GDD.md` — especially §2 (pillars), §3 (feel rules), §7 (Trading), §12–§13.
3. `docs/ARCHITECTURE.md` — especially §3 "Inside a module: `Data/`, `Model/`, `UI/`", §4.2–§4.5,
   §4.7, §5, §8, §10 "Comments and documentation".
4. `docs/modules/Laws.md` — **the reference module.** Trade must look like this when done.
5. `docs/modules/Popups.md` — the popup system every confirmation must use.
6. The Laws module code: `Assets/_Game/Modules/Laws/Scripts/**` and its tests
   `Assets/Tests/EditMode/Modules/Laws/**`. Pay particular attention to `LawsManager`, `LawQueue`,
   `LawsPresenter` (`RequestRefill`, `OpenCharacteristics`, `ConfirmBuyUp`), `LawsScreenView`,
   `UI/CharacteristicsPopup/**`, `LevelFill`.
7. The current Trade module, all of it: `Assets/_Game/Modules/Trade/**` (scripts, `TradeConfig.asset`,
   prefabs, `TradeUITable`), its tests `Assets/Tests/EditMode/Modules/Trade/**`, and
   `docs/modules/Trade.md`. **The old `Trade.md` and the old code comments are full of history; take
   facts from them, not their shape.**
8. Everything Trade touches outside the module: `Systems/Ledger` (`KingdomLedger`,
   `TradeResources/**`, `DefinitionRegistry`), `Systems/Save/Scripts/Dto/Trade*.cs` and
   `GameStateDto.cs`, `Core/Bootstrap/Scripts/GameBootstrapper.cs` and `GameStateCoordinator.cs`,
   `Systems/Popups/**`, `Scenes/Main.unity` (the Trade screen instance and the nav bar's Trade tab).

**Verify the audit below against the code.** It was written near the end of a long session; if
anything in it is wrong, say so in the plan rather than planning around it.

---

## 2. Architectural principles already established (do not re-litigate)

**Module shape** (`ARCHITECTURE.md` §3):
- `Scripts/Data/` — what designers author (ScriptableObject types and plain types serialized in them).
  Values only; no behaviour beyond tiny derivations.
- `Scripts/Model/` — the mechanic, plain C#. **The Manager is the only class that touches the Ledger
  or the clock**; every transaction lives there, each price and affordability check beside the method
  that charges it. Everything else in `Model/` is pure: handed the time, returns results, unit-tested.
- `Scripts/UI/` — the Presenter, the screen View, `<X>UIText`, and **one subfolder per UI component**
  holding its View and the view-data struct it draws.
- Namespaces: `KingdomRuler.Modules.Trade` for Data + Model, `KingdomRuler.Modules.Trade.UI` for UI.
- The composition root registers **only** config, Manager, Presenter. The Manager builds its helpers.
  A class with a test-only constructor (seeded `System.Random`) marks its production one `[Inject]` —
  no bootstrapper factory.

**UI / MVP** (`ARCHITECTURE.md` §4.4):
- One Presenter per screen, no Unity UI types in it. It builds view data, carries actions to the
  Manager, owns sound and haptics. The View holds the Presenter; the Presenter never holds the View.
- A UI element gets its own View class only if it repeats or has behaviour of its own; plain widgets
  are serialized fields on the screen View.
- DOTween only in Views. Events are named without an `On` prefix; commands are verbs.
- Canvases split by what changes at runtime; every nested Canvas needs its own `GraphicRaycaster`.

**Popups** (`Popups.md`):
- `_popups.Create<T>().Ask(() => data, closeWhen: …)` for a question; `.Show(() => data)` + a
  `WaitForChoice` loop for a panel that stays open. Data is a function re-read at 4 Hz; view-data
  structs a popup re-reads implement `IEquatable` (see `CharacteristicsPopupData`).
- **No close button; a tap outside closes the topmost popup.** Closing gives `null`.
- **Money waits for the choice:** nothing is spent on open; the price is re-read when the player
  confirms. **Every purchase is confirmed** — crystals or resources.
- Confirming closes only the confirmation; a panel underneath stays open and redraws.
- The worked example to copy is the Laws characteristics popup flow (`OpenCharacteristics` →
  `ConfirmBuyUp`), including "not enough" shown as a notice with Confirm inactive.

**Ledger** (`ARCHITECTURE.md` §4.3, §4.5):
- Ledger state is read-only outside `KingdomRuler.Systems`; every change goes through `KingdomLedger`.
- Non-finite trade amounts are refused (logged, not thrown).
- Whoever decides from a trade amount, or changes a capacity, settles regen first
  (`AccruePassiveResourceRegen`). Regen publishes no event; views poll and redraw on change.
- The event bus logs a throwing subscriber and never rethrows to the publisher.

**Timers** (`ARCHITECTURE.md` §4.5): deadline-based; a stored deadline is never more than one period
away; `GameClock` ticks at 4 Hz and each Manager subscribes itself in its constructor.

**Save** (`ARCHITECTURE.md` §5): DTOs live in `Systems/Save/Scripts/Dto`. Bump `schemaVersion` on any
shape change. No migrations and no schema history until the first external build. An unreadable save
is moved to `.corrupt`, never deleted. Whole-number fields that may be stored as decimals use
`WholeNumberJsonConverter`.

**Localization** (`ARCHITECTURE.md` §2): fixed chrome via `LocalizeStringEvent` on prefabs; text built
in code via the module's public `<X>UIText` keys with an `AllKeys` array checked by a content test;
the Presenter re-renders on `LocaleChanged`. Entries are plain `{0}` formats; both `en` and `uk`.

**Comments and docs** (`ARCHITECTURE.md` §10): describe the code as it is, never past states; comment
the why (and the how of non-obvious logic); XML docs only where a name does not say everything; short.

**Feel** (`GDD.md` §3): every player action gets animation + sound; haptic on swipe decisions,
purchase confirmations and level-ups — **one haptic per action**, never stacked.

**Asset safety** (`CLAUDE.md` §4): moves and renames through the AssetDatabase so GUIDs survive; a
renamed serialized field gets a temporary `[FormerlySerializedAs]`, the asset is re-serialized, then
the attribute is removed. **Deleting or bulk-overwriting prefabs/scenes/ScriptableObjects is 🔴** —
the plan must list every such item for the user's explicit OK.

**User preferences:** short lambdas inline; UniTask everywhere (never `Task`); anything touching
`IPurchasingService` is 🔴.

---

## 3. Audit of the current Trade module

### Behaviour to keep (the spec — all correct, matches GDD §7, tested)
- Offer mix 30/45/25 profitable/neutral/unprofitable as a **long-run average**: each batch of 10 floors
  to 3/4/2 and awards the two spare slots by weighted chance (`TradeOfferAllocator.ApportionStochastic`);
  tested statistically over 2,000 batches.
- 2–3 resource types per side, give and receive sides never overlap, every amount a whole number ≥ 1,
  unique ids. Profitable returns 110–140%, neutral 95–105%, unprofitable 60–90% of what it asks.
- Refresh every 20 min; a long absence refreshes exactly **once**; the phase is preserved; a deadline
  more than one interval away is pulled in (`OfferRefreshTimer`).
- Accept by id; refused **only** if the give side can't be paid, spending nothing; a full warehouse
  **warns and never blocks**, and the forfeited excess is computed; regen settled first; the offer
  list is never refreshed during an accept. Every issue is listed at once.
- Pair upgrade costs 80% of the paired warehouse's **capacity**, paid from its **stock**; pairs
  Stone↔Wood, Metal↔Minerals, Leather↔Clay.
- Capacity and crystal cost are formulas; capacity is derived from the saved level on load; the level
  is clamped to 50 (save-integrity guard).
- Offers and the refresh deadline are saved; relaunching cannot reroll them; damaged/unknown offer
  entries are skipped.

### Defects to fix
1. **Crystals and resources spent without confirmation** — instant refresh, crystal upgrade, pair
   upgrade all spend on tap.
2. **Received/forfeited amounts never shown** (GDD §7 violation): `TradeAcceptResult` carries them,
   the Presenter returns a bool.
3. **Per-frame allocation:** `TradeView.Update` rebuilds all six warehouse display structs every frame
   (~30 localization lookups + strings) just to notice regen.
4. **Affordability computed in the Presenter** (`CanAffordInstantRefresh`, both upgrade checks), not
   beside the charge.
5. Minor: one accept causes 5–7 full re-renders (each Ledger event plus the module event); two sounds
   in one frame on an upgrade; countdown formatting duplicates `TimeFormat`; the confirmation is a
   hand-rolled screen-local panel.

### Breaks with the structure
`Domain/` mixes data types with logic; curve formulas live inside `TradeConfig`; `TradeDisplayData.cs`
holds four types; `On…` events; old Presenters/Views layout; bootstrapper factory; history-laden
comments and `Trade.md`.

---

## 4. Decisions the user has made

1. **Offer size grows with warehouse capacity.** Every offer currently moves a flat 50 units at every
   stage, which makes trading irrelevant once warehouses are upgraded (base capacity 100, level 10 ≈
   5,770). Size offers as a **share of warehouse capacity**, to reward upgrading. Interpreted as
   *capacity*, not current fill — sizing by fill would shrink offers right after the player trades
   stock away. **The plan must propose the exact formula with worked numbers** (which warehouses'
   capacity — give side, receive side, or both; the share; rounding to whole numbers; minimums), show
   it keeps the 30/45/25 meaning, consider how it interacts with the overflow warning, say where its
   coefficients live (`TradeConfig`), and include the GDD §7 text to add. If the formula needs a user
   decision, list it as an open question.
2. **Warehouse upgrades move into a warehouses popup modeled on the Laws characteristics popup:** tap
   the warehouse panel → a popup with a large row per resource (level, stock / capacity, both upgrade
   options with their prices) → each upgrade asks for confirmation on top; the popup stays open after a
   purchase; "not enough" is a notice with Confirm inactive.
3. **Confirm every purchase** — offer accept, instant refresh, crystal upgrade, pair upgrade.
4. **Stored trade amounts stay decimal** (regen adds fractions every tick); everything the player
   sees, gives or pays is a whole number.
5. **The resource pairs stay in code** (`TradeResourcePair`), not config.

Decisions from the audit discussion, also settled:
- **Rebuild from scratch, then delete the old code** once the new module compiles and passes.
- **No shared timer with Laws** — the two timers share only the five-line clamp.
- **Keep:** `TradeConfig.asset` (the bootstrapper references it by GUID — move the script, do not
  delete it); `TradeScreen.prefab` (placed in `Main`, referenced by the nav bar's Trade tab — keep the
  asset and its root, rebuild its contents); `TradeUITable` keys whose meaning survives; the save
  sections (offers, levels, deadline) — compatible unless offer sizing forces a change.
- **Rebuild** the offer-row and warehouse-tile prefabs (deletion is 🔴: list them for explicit OK).
- **After an accept, show what was received and what was forfeited** — the plan decides how (in the
  confirmation before closing, a result popup, or a short toast) and justifies it.

---

## 5. What the plan must contain

Write `docs/plans/trade-rebuild-plan.md` with:

1. **Summary** — goal, scope, what is explicitly out of scope.
2. **Target structure** — the full folder tree, and every class with its responsibility and a public
   API sketch (as `Laws.md` §3 does). Data types that are runtime model values (offers, issues,
   results) belong in `Model/`.
3. **Data flows** — startup/load, a tick, opening and accepting an offer (with the result shown),
   instant refresh, the warehouses popup and both upgrades.
4. **Offer sizing** — the formula, worked examples at several warehouse levels, the GDD text.
5. **Save** — exact DTO impact, whether `schemaVersion` bumps (currently 7), compatibility with an
   existing save.
6. **Assets and prefabs** — an inventory of every Trade asset marked *keep / move / rebuild / delete*,
   with the 🔴 items listed for the user's OK; how each prefab is built or changed through MCP; GUIDs
   that must survive; the Main scene and nav bar references.
7. **Localization** — keys kept, added, removed, in both locales; which are chrome and which are code.
8. **Feel** — the sound, animation and haptic for every action (one haptic per action).
9. **Tests** — which old tests port (old name → new name), which are dropped and why, which are new;
   what is verified in Play mode instead, step by step.
10. **Docs** — the new `Trade.md` outline, and every change to `GDD.md`, `ARCHITECTURE.md`, `CLAUDE.md`.
11. **Ordered implementation steps** — small, each ending with a verification checkpoint (compile clean
    via MCP console, EditMode tests run via MCP, Play-mode check where relevant). The order must keep
    the project compiling at each checkpoint and delete the old code only at the end.
12. **Risks** and **open questions** for the user.

Then give the user a short summary of the plan and the open questions.

---

## 6. Practical notes for this project

- **Unity MCP:** the Editor is normally open. `execute_code` compiles with **C# 6 (CodeDom)** — no local
  functions, no tuples; write `UnityEngine.Object` (plain `Object` is ambiguous); DOTween extension
  methods must be called statically (`DG.Tweening.TweenExtensions.Duration(t, true)`).
- **Play mode:** always enter from the **Bootstrap** scene. An unfocused Editor throttles to a couple
  of frames: run `Application.runInBackground = true;` in the first `execute_code` call. Back up
  `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Kingdom Ruler\kingdom_ruler_save.json` before any
  Play-mode pass and restore it byte for byte afterwards.
- **Multi-line edits:** write a Python script to the session scratchpad and run it with `python -I`;
  bash heredocs containing quotes tend to break.
- **Tests:** 392 EditMode tests pass at the start of this work. Content tests (`TradeTextTests`,
  `LedgerDataTests`) check real String Tables and assets.
- **Known console noise:** expected warnings from tests (missing icons, corrupt-save cases), an MCP
  WebSocket warning, and a TextMesh Pro fallback-font import warning — none come from game code.
