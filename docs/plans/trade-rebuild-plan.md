# Plan: rebuild the Trade module

> Written from `docs/plans/trade-rebuild-brief.md`. Nothing in the project has been changed except
> this file. Baseline confirmed via Unity MCP before writing: **392/392 EditMode tests pass**.
>
> **Revision 2 — the user's answers are applied:**
> - offer sizing approved (§4);
> - **trade amounts become integers everywhere**, including the Ledger, which supersedes brief decision 4 (§2.4);
> - warehouse level capped at **20** by design;
> - offer rows carry no affordability hints;
> - 1-based displayed levels; gauges show stock only;
> - "Refresh now" always tappable;
> - a Trade-local "Not enough crystals" string;
> - the 🔴 list (§6.1) and the quarantine approach (§11, step 1) approved.
>
> Assumptions taken while applying these are listed in §12. Everything else is decided.

---

## 0. Corrections to the brief's audit

The audit is accurate. Checked against the code, with these additions:

| # | Finding | Where |
|---|---|---|
| A1 | **The overflow warning shows the wrong number.** `ui.warning_overflow` reads "Your {0} warehouse only has room for {1}", and the Presenter passes the *room left*. GDD §7 asks for "how much will not fit". `TradeUIText.WarningOverflow`'s own doc comment says "{1} = units that will not fit". | `TradePresenter.DescribeIssue` |
| A2 | **Two sounds in one frame in two places:** the crystal upgrade (`CrystalSpend` + `WarehouseUpgrade`) and the instant refresh (`CrystalSpend` + `OffersRefreshed`). | `TradePresenter` |
| A3 | **Seven `TradeUITable` keys are unused.** The prefab's `LocalizeStringEvent`s use only `ui.trade_title`, `ui.no_offers`, `ui.confirm`, `ui.cancel`. Unused: `ui.warehouses`, `ui.offers`, `ui.you_give`, `ui.you_get`, `ui.refresh_now`, `ui.upgrade`, `ui.next_refresh`. Most come back into use in the new prefabs (§7). | `TradeScreen.prefab` |
| A4 | **Offer amounts are `float`** everywhere (model and DTO), although they always hold whole values. | `TradeOffer`, `TradeOfferDto` |
| A5 | Under the current cap of 50, capacity exceeds `int` from level 42. **Resolved by the user's decision:** the cap becomes 20, where capacity is 332,530, so everything fits `int32`. The Ledger gains a hard capacity ceiling as a backstop (§2.4). | `TradeConfig` |
| A6 | `schemaVersion` is never read on load. **Not an issue yet** (user): the game runs only in the Editor until the first build. The plan still bumps it on a shape change, as `ARCHITECTURE.md` §5 requires. | `LocalJsonSaveService` |
| A7 | The Trade asmdef lacks a `UniTask` reference, which the popups need. The package is already in the project (Laws uses it), so this is not a new dependency. | `KingdomRuler.Modules.Trade.asmdef` |
| A8 | `ScriptableObjects/Data/` is an empty folder holding a `.gitkeep`. | Trade module |
| A9 | `Popups.md` §1 says no caller uses `Show` + `WaitForChoice` yet; `LawsPresenter.OpenCharacteristics` does. Stale doc, fixed in the doc pass (§10). | `docs/modules/Popups.md` |

Everything else in the audit checks out exactly.

---

## 1. Summary

**Goal.** Rebuild `Modules/Trade` from scratch in the Laws shape (`Data/` · `Model/` · `UI/`). Keep every behaviour in the brief's "keep" list, fix every listed defect and A1–A2, and deliver:
- offers sized by warehouse capacity (§4);
- a warehouses popup modeled on the characteristics popup;
- a confirmation for every purchase;
- whole numbers everywhere (§2.4);
- a receipt of what was received and what was lost after each accept.

**In scope:**
- all Trade scripts, `TradeConfig` (script moved, asset GUID kept), `TradeScreen.prefab` contents, new component and popup prefabs, `TradeUITable` entries, Trade tests;
- `GameBootstrapper` registration and `PopupRegistry.asset` entries;
- **the Ledger's trade-resource API, its regen, `TradeResourceChangedEvent` and `LedgerSnapshot` becoming integer (🟡, decided by the user)**;
- **`TradeResourceStateDto` and `TradeOfferDto` becoming integer, with `schemaVersion` 7 → 8**;
- the Ledger and save tests these touch;
- `docs/modules/Trade.md` and the GDD/ARCHITECTURE/Popups text that describes all of this.

**Out of scope:**
- characteristics, gold and crystals (already integer);
- `IPurchasingService`;
- art, final audio and real haptics (stubbed, as in Laws);
- a currency header and a profitability badge;
- a shared toast system;
- Laws' own "Not enough crystals" key;
- any change to the Laws module.

---

## 2. Target structure

```
Modules/Trade/
  Scripts/                                    assembly KingdomRuler.Modules.Trade (+ UniTask reference)
    Data/                                     namespace KingdomRuler.Modules.Trade
      TradeConfig.cs                          MOVED (GUID kept). Values only + OnValidate.
    Model/                                    namespace KingdomRuler.Modules.Trade
      TradeManager.cs                         entry point: lifecycle, clock, every Ledger transaction, every price
      TradeOffer.cs                           one offer: id, give/receive amounts, profitability (immutable)
      TradeAmount.cs                          (resource, units) — one line of an offer or a result
      TradeProfitability.cs                   Profitable / Neutral / Unprofitable
      TradeOfferGenerator.cs                  a batch: band mix, resource types, amounts
      TradeOfferSizing.cs                     the size formula (§4) — static, pure
      TradeOfferAllocator.cs                  whole-number apportionment (stochastic + largest remainder)
      OfferRefreshTimer.cs                    the 20-minute deadline
      WarehouseCurve.cs                       capacity(L), crystalCost(L) — formulas out of TradeConfig
      TradeResourcePair.cs                    Stone↔Wood, Metal↔Minerals, Leather↔Clay
      TradeIssue.cs                           (kind, resource, units)
      TradeIssueKind.cs                       InsufficientResource / WouldOverflowWarehouse / OfferUnavailable
      TradeOfferEvaluation.cs                 every issue for one offer + CanAccept — mutates nothing
      TradeAcceptResult.cs                    Accepted, Issues, Received, Forfeited
    UI/                                       namespace KingdomRuler.Modules.Trade.UI
      TradePresenter.cs                       model ↔ view; popups; feel hooks
      TradeScreenView.cs                      screen root: widgets, polling, forwarding taps
      TradeUIText.cs                          String Table keys for text built in code + AllKeys
      ResourceAmount/
        ResourceAmountView.cs                 icon + "12 Stone" — repeats in rows and the offer popup
        ResourceAmountViewData.cs
      OfferRow/
        OfferRowView.cs                       one offer in the list: 3+3 amount slots, tap
        OfferRowViewData.cs
      WarehouseGauge/
        WarehouseGaugeView.cs                 one compact warehouse on the screen (×6): icon, stock/capacity, bar
        WarehouseGaugeViewData.cs
      TradeOfferPopup/
        TradeOfferPopup.cs                    Popup<TradeOfferPopupData, TradeOfferPopupChoice>
        TradeOfferPopupData.cs                give/receive lines + blockers + warnings + CanAccept (IEquatable)
        TradeOfferPopupChoice.cs              Accept / Cancel
      WarehousesPopup/
        WarehousesPopup.cs                    Popup<WarehousesPopupData, WarehouseUpgradeChoice>
        WarehousesPopupData.cs                one row per resource (IEquatable)
        WarehouseRowView.cs                   one row: level, stock/capacity + bar, two upgrade buttons
        WarehouseRowViewData.cs               (IEquatable)
        WarehouseUpgradeChoice.cs             (resource, payment) — the pressed button
        WarehousePayment.cs                   Crystals / PairedResource
      Receipt/
        TradeReceiptView.cs                   the post-accept strip: "+13 Wood", "17 Wood lost"
        TradeReceiptViewData.cs
  ScriptableObjects/Config/TradeConfig.asset  KEPT (GUID 73e2f9f5a7e53384bb49ec0859a65de1)
  Prefabs/      TradeScreen (kept root), OfferRow, ResourceAmount, WarehouseGauge,
                TradeOfferPopup, WarehousesPopup, WarehouseRow
  Localization/ TradeUITable (kept collection, entries edited)
```

The new prefab names differ from the old ones (`OfferRow` rather than `TradeOfferRow`, `WarehouseGauge` rather than `WarehouseTile`), because the old prefabs keep their file names until they are deleted at the end. They also follow Laws' pairing: a compact gauge on the screen (like `CharacteristicDial`) and a large row in the popup (like `CharacteristicRow`).

**Dependencies:** `UI → Model → Data`, and everything may use `Systems/`. The Presenter needs no `EventBus` (§3, "Polled, not pushed").

```
 UI      TradeScreenView ──► TradePresenter ──► TradeManager, KingdomLedger (reads), TradeResourceRegistry,
         ├─ WarehouseGaugeView ×6                PopupSystem, IAudioService, IHapticService, ILocalizationService
         ├─ OfferRowView ×N ─► ResourceAmountView ×6
         ├─ TradeReceiptView
         ├─ TradeOfferPopup (via PopupSystem) ─► ResourceAmountView ×6
         └─ WarehousesPopup (via PopupSystem) ─► WarehouseRowView ×6
 Model   TradeManager ──► TradeOfferGenerator ──► TradeOfferSizing, TradeOfferAllocator
            ├──► OfferRefreshTimer, WarehouseCurve, TradeResourcePair
            └──► KingdomLedger (writes), IClock
 Data    TradeConfig
```

**Registration** stays three lines: `RegisterInstance(TradeConfig)`, `Register<TradeManager>`, `Register<TradePresenter>`. The factory lambda goes, and `TradeManager`'s production constructor is marked `[Inject]`.

### 2.1 Data

| Class | Responsibility |
|---|---|
| `TradeConfig` | `OfferCount` (10), `OfferRefreshTimeSeconds` (1200), `InstantRefreshCrystalCost` (3), **`OfferCapacityShare` (0.20, new)**, **`MaxWarehouseLevel` (20, new — a design cap, tunable)**, `CapacityBase/Growth/RoundTo` (100/1.5/10), `CrystalCostBase/Growth` (5/1.5). `OnValidate` clamps: `OfferCapacityShare` to `[0.01, 0.40]` (§4.3), `MaxWarehouseLevel` to ≥ 1. **No formulas.** The legacy members (`OfferBaseAmount`, the `MaxWarehouseLevel` const, `WarehouseCapacityAt`, `WarehouseUpgradeCrystalCost`, `Grow`) live only while the legacy code exists. The const is renamed out of the way in step 2 so it cannot collide with the new field (§11). |

### 2.2 Model

```csharp
public sealed class TradeManager
{
    [Inject] public TradeManager(KingdomLedger ledger, IClock clock, TradeConfig config);
    public TradeManager(KingdomLedger ledger, IClock clock, TradeConfig config, System.Random rng);   // tests

    event Action OffersChanged;                 // the list was replaced (refresh) or shortened (accept)

    // Lifecycle
    void InitializeNewGame();   void LoadFromDto(TradeStateDto dto);   TradeStateDto ToDto();
    void AdvanceTo(DateTime now);               // subscribed to IClock.Ticked in the constructor

    // Offers
    IReadOnlyList<TradeOffer> Offers;           DateTime? NextRefreshDueUtc;   float SecondsUntilRefresh;
    bool TryGetOffer(string id, out TradeOffer offer);
    TradeOfferEvaluation Evaluate(string id);   // every issue, mutates nothing
    TradeAcceptResult    AcceptOffer(string id);

    // Crystal refresh — price, check and charge side by side
    int  InstantRefreshCost;   bool CanAffordInstantRefresh;   bool TryInstantRefresh();

    // Warehouses
    int  WarehouseLevel(TradeResourceType t);   bool CanUpgradeFurther(TradeResourceType t);
    int  CapacityAfterUpgrade(TradeResourceType t);                      // "it will hold N" in the confirmation
    int  CrystalUpgradeCost(TradeResourceType t);  bool CanAffordCrystalUpgrade(TradeResourceType t);
    bool TryUpgradeWithCrystals(TradeResourceType t);
    int  PairedUpgradeCost(TradeResourceType t);   bool CanAffordPairedUpgrade(TradeResourceType t);
    bool TryUpgradeWithPairedResource(TradeResourceType t);
    const float PairedUpgradeShare = 0.8f;      // GDD §7, settled
}
```

Rules the Manager carries:
- **It is the only class touching the Ledger or the clock.** Each price sits beside its check and its charge.
- **Every `Try…` settles regen first** (`AccruePassiveResourceRegen`, never `AdvanceTo`, so no refresh lands mid-transaction), then re-reads its price. `CanAfford…` are display reads and do not settle; at most they are one 4 Hz tick stale, and the charge re-validates.
- **All integer.** Stock, capacity, offer amounts and both prices are `int`, read straight from the Ledger. A warehouse's room is `capacity − stock`. An overflow issue carries `excess = amount − room`. On accept, `received` is the Ledger's return value from `AddTradeResource` and `forfeited = amount − received`. The warning and the receipt use the same arithmetic, so the excess the player is warned about is exactly what is forfeited (unless stock changed in between, e.g. a regen unit), and `received + forfeited == offered` holds per line.
- **Paired price** = `ceil(capacity(paired) × 0.8)`, computed exactly as `(capacity × 4 + 4) / 5`: never below 80 %, and whole for any `CapacityRoundTo`.
- **The level is the stored fact.** Capacity is `WarehouseCurve.Capacity(config, level)` pushed into the Ledger. A loaded level is clamped to `0..MaxWarehouseLevel`; at the cap, `CanUpgradeFurther` is false.
- **`OffersChanged` fires once per change:** a refresh, an instant refresh or an accept. Upgrades do not raise it, because capacities are polled (§3).

| Class | Responsibility | Pure? |
|---|---|---|
| `TradeOffer` | `Id`, `Give`/`Receive` as `IReadOnlyList<TradeAmount>` (generation order, so display and save are stable), `Profitability`, `TotalGive`/`TotalReceive`. Immutable; copies its inputs. | yes |
| `TradeAmount` | `readonly struct (TradeResourceType Resource, int Units)`. | yes |
| `TradeOfferGenerator` | `List<TradeOffer> GenerateBatch(int count, float capacityShare, IReadOnlyDictionary<TradeResourceType,int> capacities)`. Band mix via `ApportionStochastic`, shuffled; types from one shuffled list (no overlap, 2–3 a side); total via `TradeOfferSizing`; multipliers 110–140 / 95–105 / 60–90 %. Takes a `System.Random`. | yes |
| `TradeOfferSizing` | `int GiveTotal(float share, give capacities, receive capacities)` and `int[] Split(int total, capacities, Random)`. Constants `MinGiveTotal = 10`, `JitterMin = 0.75`, `JitterMax = 1.25`. The formula in §4. | yes |
| `TradeOfferAllocator` | Ported unchanged in behaviour: `ApportionStochastic`, `ApportionLargestRemainder`. | yes |
| `OfferRefreshTimer` | Ported unchanged: deadline, one refresh per settle, phase preserved, pulled in to one interval. | yes |
| `WarehouseCurve` | `int Capacity(TradeConfig, int level)` and `int CrystalCost(TradeConfig, int level)`. The level is clamped to `0..config.MaxWarehouseLevel`; computed in `double`, then held to `[roundTo, KingdomLedger.MaxWarehouseCapacity]` and `[1, int.MaxValue]`, so no tuning can wrap. Keeps the banker's rounding that matches `CharacteristicLevelingCurve` (`100 × 1.5² → 220`). | yes |
| `TradeResourcePair` | `TradeResourceType PairedWith(TradeResourceType)`. Stays in code (decision 5). | yes |
| `TradeIssue` / `TradeIssueKind` | `(Kind, Resource, int Units)`, where Units is the shortfall or the excess. `BlocksAcceptance` is false only for `WouldOverflowWarehouse`. | yes |
| `TradeOfferEvaluation` | `OfferExists`, `Issues`, `CanAccept` (no blocking issue), `HasOverflowWarning`. | yes |
| `TradeAcceptResult` | `Accepted`, `Issues`, `Received`, `Forfeited` (`IReadOnlyList<TradeAmount>`; Forfeited lists only lines with a loss). | yes |

Offer DTO mapping stays as private statics in `TradeManager`, as in `LawsManager`, tested through `LoadFromDto`/`ToDto`.

### 2.3 UI

```csharp
public sealed class TradePresenter : IDisposable
{
    TradePresenter(TradeManager, KingdomLedger, IAudioService, IHapticService,
                   ILocalizationService, TradeResourceRegistry, PopupSystem);

    event Action StateChanged;                          // re-render the list and static labels
    event Action OffersArrived;                         // a new batch is on screen — rows animate in
    event Action<TradeReceiptViewData> TradeCompleted;  // show the receipt; the accepted row collapses
    event Action<TradeResourceType> WarehouseUpgraded;  // that gauge celebrates

    // Polled by the view every frame — no strings, no allocation
    WarehouseGaugeViewData GetGauge(TradeResourceType t);
    float                  SecondsUntilRefresh;
    // Read on StateChanged
    OfferRowViewData[] GetOffers();   int InstantRefreshCost;   bool HasOffers;
    string RefreshCountdownText(int seconds);           // once per whole second, via TimeFormat

    // Popup data — read by popups at 4 Hz
    TradeOfferPopupData GetOfferPopup(string offerId);  WarehousesPopupData GetWarehouses();

    // Actions
    UniTaskVoid OpenOffer(string offerId);   UniTaskVoid RequestInstantRefresh();
    UniTaskVoid OpenWarehouses();            void SetScreenVisible(bool visible);
}
```

| Class | Responsibility |
|---|---|
| `TradePresenter` | Builds localized view data, runs the popup flows, owns sound and haptics, gates passive sound on visibility, and re-renders on `OffersChanged` and `LocaleChanged`. No Unity UI types. Holds the `PopupSystem` MonoBehaviour, as `LawsPresenter` does. |
| `TradeScreenView` | Root of `TradeScreen.prefab`, replacing `TradeView` on the same GameObject. Wires the panel button, the refresh button and the rows to the Presenter. In `Update`, it rewrites the countdown only on a whole-second change and renders the six gauges, each dirty-checking. Logs an error and disables itself when not injected. Checks that every resource has exactly one gauge. Never advances anything. |
| `WarehouseGaugeView` | `Type` (serialized), `Render(data)` with dirty-check, `PlayUpgrade()` (punch). Shows stock / capacity and the bar, **no level**. It is not a tap target: the whole `WarehousePanel` is one Button (Laws §6.11). |
| `OfferRowView` | Fixed 3 give + 3 receive `ResourceAmountView` slots, unused ones hidden. `Render(data)`, `PlayArrival(delay)`, `PlayRemoval()`. Raises `Pressed(id)`. **No affordability or overflow marking** — that lives only in the confirmation popup. A hidden row is inert (its id is cleared). |
| `ResourceAmountView` | Icon (hidden when null, since an empty `Image` draws a white box) plus the "12 Stone" text. |
| `TradeOfferPopup` | Two columns of 3 `ResourceAmountView`s, a **blockers** label, a **warnings** label, and Accept (interactive iff `CanAccept`) + Cancel. Fixed text (title, column headings, captions) is `LocalizeStringEvent`. |
| `WarehousesPopup` | Six `WarehouseRowView`s matched by type, with a start-up check for exactly one per resource. Choice is `WarehouseUpgradeChoice`. No close button. |
| `WarehouseRowView` | Icon, name, "Level N" (stored level + 1), `stock / capacity` + Filled-Horizontal bar, the crystal button ("38 crystals") and the paired button ("608 Wood"). At the cap it shows "Fully upgraded" with both buttons inactive. Below the cap both buttons are always tappable; affordability shows in the confirmation. Punches its level label on a level-up. |
| `TradeReceiptView` | Up to 6 lines (3 received + up to 3 lost) for ~2.5 s on unscaled time, then fades. A new receipt replaces the current one. No raycast targets. |
| View-data structs | What each view draws, already localized. The only `UnityEngine.Object` on them is a `Sprite`. The popup data structs and `WarehouseRowViewData` are `IEquatable`, comparing arrays element by element (the `CharacteristicsPopupData` pattern). |

### 2.4 Ledger: trade amounts become integers (🟡 — decided by the user; implemented in step 5)

**Why integers beat a separate whole-number view of a decimal.** Today two numbers exist per resource: the stored decimal and what the player sees. Every reader then needs a floor, the floor has to be agreed across Trade and Cities, a filled warehouse can land one float-ulp under capacity (99/100), and every mutator needs a NaN/∞ guard. With integers there is one number, and it is the one on screen.

The only reason amounts were decimal is regen, and regen does not need a fraction. It needs to remember *how far into the next unit it is*. That fits in the timestamp the state already has, which is how this project already does timers: a deadline that advances in whole intervals, so partial progress is never lost (`ReplenishmentTimer`, `OfferRefreshTimer`, ARCHITECTURE §4.5).

**Regen, in whole units:**
```
A warehouse gains one unit every  TicksPerDay / capacity  (an empty one is full after exactly 24 h).
LastUpdatedUtc = the moment the unit now in progress started.

AccruePassiveRegen(now):
  now ≤ Last                  → Last = now                         (a clock that ran ahead resyncs — as today)
  Amount ≥ Capacity           → Last = now                         (a full warehouse banks nothing — as today)
  elapsed = (now − Last).Ticks
  elapsed ≥ TicksPerDay       → Amount = Capacity, Last = now      (a day fills any warehouse)
  units = elapsed × Capacity / TicksPerDay                         (long; exact floor)
  units == 0                  → return                             (progress stays in Last)
  Amount = min(Amount + units, Capacity)
  Last   = Amount == Capacity ? now : Last + units × TicksPerDay / Capacity
```
- **The arithmetic is exact in `long`** while `elapsed < 1 day` and `capacity ≤ 10,675,199` (`TicksPerDay × capacity < long.MaxValue`). The Ledger gets `public const int MaxWarehouseCapacity = 10_000_000`. `SetWarehouseCapacity` and `LoadFromDto` clamp to it (logged), and Trade's curve saturates at it. At level 20 capacity is 332,530, 30× below.
- Advancing `Last` by the floored tick count can only move it *earlier*, by under one tick (100 ns) per credit, so the player can never lose progress.
- **Many small ticks give exactly the same result as one long absence**, which is tested.
- **The save needs nothing new:** `amount` and `lastUpdatedUtc` already carry the whole state. The existing rule "a pair saved a fraction of a second stale is covered exactly by the next accrual" still holds.
- Upgrades still settle regen first, so the old rate is honoured up to the moment of the upgrade. The partial unit carries over into the new rate as elapsed time.
- **Still publishes no event.** At level 20 the six warehouses gain ~4 units a second each, so publishing would put up to ~24 bus messages a second on the bus. Gauges and popups poll (ARCHITECTURE §4.5 unchanged in spirit).

**API after the change:**

```csharp
// TradeResourceState (setters stay internal)
int Amount;   int Capacity;   DateTime LastUpdatedUtc;          // RegenRatePerSecond removed (derived)
// KingdomLedger
public const int MaxWarehouseCapacity = 10_000_000;
int  AddTradeResource(TradeResourceType, int amount);           // returns the units that fit
bool SpendTradeResource(TradeResourceType, int amount);
bool HasTradeResource(TradeResourceType, int amount);
void SetWarehouseCapacity(TradeResourceType, int capacity);     // > 0, clamped to MaxWarehouseCapacity
// TradeResourceChangedEvent(type, int newAmount, int delta)
// LedgerSnapshot.TradeResourceAmounts : IReadOnlyDictionary<TradeResourceType, int>
```

Negative amounts still throw `ArgumentException`, as today. The non-finite guard (`IsRejectedAsNonFinite`) goes, because an `int` cannot be NaN. Corrupt numbers in a file are handled by `WholeNumberJsonConverter` on load (§5). `RegenRatePerSecond` is removed: nothing outside the Ledger and tests reads it, and with integer regen it would be a second copy of "capacity per day". Nothing outside Trade and the Ledger's own tests uses any of this API (checked: Laws does not).

---

## 3. Data flows

### Startup / load
```
GameStateCoordinator.LoadOrInitialize()
  ├─ save → KingdomLedger.LoadFromDto(ledger)       (whole amounts, capacity clamped, timestamps)
  │         TradeManager.LoadFromDto(trade)
  │           1. levels → clamp 0..MaxWarehouseLevel → SetWarehouseCapacity(WarehouseCurve.Capacity)
  │           2. offers → skip damaged entries (< 1, unknown name; an empty side drops the offer)
  │           3. OfferRefreshTimer.Restore(deadline)       (missing → a fresh interval; > 1 interval → pulled in)
  │           4. no offers left → generate a batch         (capacities are final from step 1)
  │           5. AccruePassiveResourceRegen(now)           (offline regen, against final capacities)
  │           6. AdvanceTo(now)                            (at most one refresh for the absence)
  │           7. OffersChanged
  └─ none → InitializeNewGame(): level 0 everywhere, capacities, ResetRegenBaseline(now), batch, timer
GameEntryPoint → WhenReady → load Main → inject → TradeScreenView.Construct(presenter) → Start → Refresh
```

### A tick (4 Hz)
```
GameClock.Ticked(now)
  ├─ TradeManager.AdvanceTo(now) → timer due? → new batch sized from current capacities → OffersChanged
  │     └─ Presenter: deadline moved ⇒ it was a refresh → sound if visible → StateChanged → OffersArrived
  └─ KingdomLedger.AccruePassiveResourceRegen(now) → whole units credited, no event
TradeScreenView.Update (only while the tab is active)
  ├─ countdown: whole second changed → RefreshCountdownText
  └─ 6 × GetGauge → each gauge redraws only when stock or capacity changed
```

The Presenter tells a refresh from an accept by the deadline: every refresh moves `NextRefreshDueUtc`, and an accept never does. The state is derived rather than stored, like Laws' `_hadActiveCard`.

**Polled, not pushed.** The Presenter does not subscribe to `TradeResourceChangedEvent`. Gauges and open popups poll, so a change from another mechanic shows within a frame, and one accept causes exactly one re-render.

### Opening and accepting an offer
```
OfferRowView tap → Presenter.OpenOffer(id)                       — every tap opens the confirmation
  └─ TradeOfferPopup.Ask(() => GetOfferPopup(id), closeWhen: () => !_manager.TryGetOffer(id, out _))
        live data, re-read at 4 Hz; nothing is spent on open:
          give side short   → blocker per resource: "You need 12 more Stone"      → Accept DISABLED
          receive overflows → warning per resource: "17 Wood will not fit and will be lost"
                              (every resource that will lose units is listed)     → Accept stays ENABLED
        Accept → TradeManager.AcceptOffer(id)       settles regen, re-evaluates, refuses or applies
          ├─ refused (give side became short within one refresh) → refused sound, no haptic
          └─ accepted → OffersChanged (one re-render) → accept sound + ONE haptic
                → TradeCompleted(receipt): row collapses, gauges tween, receipt strip:
                     "+13 Wood"  "+30 Clay"  "17 Wood lost — the warehouse is full"
        backdrop / Cancel / offer refreshed away → nothing happens
```

**Why the result is a receipt strip on the screen, not in the confirmation or a result popup:**
- `Ask` closes the confirmation on the tap, so showing a result inside it would turn a question into a panel with a second "OK".
- A result popup costs a tap on every trade, against "cozy" (GDD §2).
- The strip names every amount, with the loss on its own line, with no extra tap, on the screen where the warehouses visibly change. It is one Trade component, not a new system; if Economy later wants "+500 gold", that is the trigger to promote it to `Shared/UI`.
- The loss is also announced *before* the choice, in the warning, so the receipt confirms rather than reveals.

### Instant refresh
```
"Refresh now" (always tappable) → Presenter.RequestInstantRefresh()
  └─ ConfirmPopup.Ask(() => (title, "Spend 3 crystals to replace every offer now? Otherwise new offers
                                     arrive in 12:41." [+ "Not enough crystals."], canConfirm: CanAfford),
                      closeWhen: () => NextRefreshDueUtc != dueWhenOpened)   // the free refresh landed
       Confirm → TryInstantRefresh()   price re-read, crystals spent, batch from current capacities,
                                       timer restarted → OffersChanged → refreshed sound + ONE haptic,
                                       rows animate in
```

### Warehouses popup and both upgrades
```
Tap anywhere on WarehousePanel → Presenter.OpenWarehouses()
  └─ WarehousesPopup.Show(GetWarehouses)                      stays open, re-reads at 4 Hz
       loop: WaitForChoice → (resource, payment)
         Crystals → ConfirmPopup.Ask(() => ("Upgrade the warehouse?",
                       "Spend 38 crystals to raise the Stone warehouse to level 7? It will hold 1,140."
                       [+ "Not enough crystals."], canConfirm: CanAffordCrystalUpgrade(r)))
                    Confirm → TryUpgradeWithCrystals(r)        settle regen, re-read the price, charge, level++
         Paired   → ConfirmPopup.Ask(() => ("Upgrade the warehouse?",
                       "Spend 608 Wood to raise the Stone warehouse to level 7? It will hold 1,140."
                       [+ "Not enough Wood: you have 412."], canConfirm: CanAffordPairedUpgrade(r)))
                    Confirm → TryUpgradeWithPairedResource(r)  settle regen, re-read the price, spend paired stock
         success → upgrade sound + ONE haptic → WarehouseUpgraded(r): row level punches, gauge punches;
                   the popup redraws itself on its next read
       tap outside → closes the top popup only: the confirmation first, then the warehouses popup
```

(The "level 7" in these examples is the displayed level: stored 5 → shown 6 → upgrades to 7.)

---

## 4. Offer sizing (approved)

### 4.1 The formula

```
giveCapacity    = Σ capacity of the give-side warehouses
receiveCapacity = Σ capacity of the receive-side warehouses

giveTotal    = max(10, round½↑(share × min(giveCapacity, receiveCapacity)))     share = OfferCapacityShare = 0.20
receiveTotal = round½↑(giveTotal × multiplier)                                   multiplier as today, by band

Each side is split across its resources in proportion to  capacity × jitter,  jitter ∈ [0.75, 1.25)
(one unit reserved per resource, the rest by largest remainder) → whole numbers ≥ 1, summing exactly.
```

- **Capacity, not fill:** an offer does not shrink right after the player trades stock away.
- **The smaller side's capacity** bounds every amount by its own warehouse in both directions (§4.3).
- **Split in proportion to capacity:** every upgrade makes the offers touching that warehouse bigger.
- **30/45/25 keeps its meaning.** Units are still valued 1:1, profitability is still `receiveTotal / giveTotal`, and the band mix stays the stochastic split.
- **Minimum 10 units given** keeps the bands distinct at any capacity: profitable ≥ 11 > 10, unprofitable ≤ 9 < 10, neutral 10–11. It is a correctness floor, so it is a constant with a test. At default tuning it never binds.
- **Jitter narrowed** from today's `0.01–1.01` to `0.75–1.25`, so a single offer is less lopsided than today. This is a deliberate behaviour change.
- **Rounding is half away from zero**, explicit, as Laws prices are.

### 4.2 Worked numbers (share 0.20, base 100, growth 1.5, roundTo 10)

Capacity: L0 100 · L1 150 · L2 220 · L3 340 · L4 510 · L5 760 · L6 1,140 · L7 1,710 · L8 2,560 · L9 3,840 · L10 5,770 · L15 43,790 · L20 (cap) 332,530.

**All warehouses at the same level** (2v2 means 2 resources given, 2 received):

| Level (cap) | 2v2 / 3v2 / 2v3 give | 3v3 give | Profitable receives | Neutral receives | Unprofitable receives |
|---|---|---|---|---|---|
| L0 (100) | 40 | 60 | 44–56 / 66–84 | 38–42 / 57–63 | 24–36 / 36–54 |
| L1 (150) | 60 | 90 | 66–84 / 99–126 | 57–63 / 86–95 | 36–54 / 54–81 |
| L3 (340) | 136 | 204 | 150–190 / 224–286 | 129–143 / 194–214 | 82–122 / 122–184 |
| L5 (760) | 304 | 456 | 334–426 / 502–638 | 289–319 / 433–479 | 182–274 / 274–410 |
| L10 (5,770) | 2,308 | 3,462 | 2,539–3,231 / 3,808–4,847 | 2,193–2,423 / 3,289–3,635 | 1,385–2,077 / 2,077–3,116 |
| L20 (332,530) | 133,012 | 199,518 | — | — | — |

Today every offer gives a flat 50. At L0 the new offers give 40–60, so early trading feels the same.

**Uneven warehouses** (Stone and Wood at L5 = 760, the rest at L0 = 100):

| Offer | min(Σgive, Σreceive) | Give total | Split |
|---|---|---|---|
| Stone + Wood → Metal + Clay | min(1520, 200) = 200 | 40 | ~20 Stone, ~20 Wood → ~20 Metal, ~20 Clay (neutral) |
| Metal + Clay → Stone + Wood | min(200, 1520) = 200 | 40 | ~20 Metal, ~20 Clay → ~20 Stone, ~20 Wood |
| Stone + Clay → Wood + Metal | min(860, 860) = 860 | 172 | ~152 Stone, ~20 Clay → ~152 Wood, ~20 Metal |

### 4.3 Bounds, and how it interacts with the overflow warning

- Each give amount ≤ `share × 1.25/0.75` ≈ **33 %** of its own warehouse.
- Each receive amount ≤ `share × 1.40 × 1.25/0.75` ≈ **47 %** of its own warehouse (±1 for rounding).

So:
- **no offer ever asks for more than its warehouse holds**;
- **an offer overflows only a warehouse that is more than about half full**, the same fraction at every level (today's flat 50 made overflow vanish as warehouses grew);
- `OfferCapacityShare ≤ 0.40` keeps "no amount exceeds its own warehouse" true (0.40 × 1.4 × 1.667 ≈ 0.93), and a test pins it at that maximum;
- the largest possible offer at the cap is ~0.40 × 3 × 332,530 × 1.4 ≈ 560k units, far inside `int`.

**Balance note, not solved here:** regen fills a warehouse in 24 h, so a once-a-day player often has full warehouses and sees overflow warnings on most offers. That is true today too; sizing by capacity only keeps it consistent across levels.

**Existing offers are not resized by an upgrade.** Offers are fixed, saved terms, and the next batch picks up the new capacity.

**Where the numbers live:** `OfferCapacityShare` in `TradeConfig`; `MinGiveTotal` and the jitter bounds as constants in `TradeOfferSizing`; the band multipliers in `TradeOfferGenerator`.

### 4.4 GDD §7 text to add (after "Offer generation ratio")

> - **Offer size scales with warehouse capacity** **[ASSUMED — CONFIRM share = 20%]**. An offer
>   moves a share of the warehouses it trades with, measured on the smaller side:
>   `give total = max(10, round(0.20 × min(Σ give-side capacities, Σ receive-side capacities)))`, and
>   the receive total is the give total times the offer's profitability multiplier (110–140%,
>   95–105%, 60–90%). Each side is split across its resources in proportion to their capacities, with
>   ±25% variation, every amount a whole number of at least 1. Capacity rather than stock, so an
>   offer does not shrink right after the player trades stock away; the smaller side, so no amount
>   ever exceeds about half of its own warehouse — an offer never asks for more than a warehouse
>   holds, and only overflows a warehouse that is already about half full. Upgrading a warehouse
>   makes every later offer that touches it bigger; offers already on the list keep their terms.
>   The share is `TradeConfig.OfferCapacityShare`.

---

## 5. Save

**Shape changes (types only, no new or removed fields). `schemaVersion` 7 → 8.**

| DTO field | Before | After |
|---|---|---|
| `TradeResourceStateDto.amount` | `float` | `int` + `[JsonConverter(typeof(WholeNumberJsonConverter))]` |
| `TradeResourceStateDto.capacity` | `float` | `int` + the same converter (still a fallback; Trade re-derives capacity from the level) |
| `TradeResourceStateDto.lastUpdatedUtc` | ISO string | unchanged; it now means "when the unit in progress started" |
| `TradeOfferDto.give` / `.receive` | `Dictionary<string, float>` | `Dictionary<string, int>` + `[JsonProperty(..., ItemConverterType = typeof(WholeNumberJsonConverter))]` |
| `TradeStateDto.*`, `GameStateDto.*` | — | unchanged except `CurrentSchemaVersion = 8` |

**An existing save stays readable.** `WholeNumberJsonConverter` reads `87.34` as 87, `25.0` as 25, and NaN as 0 (then rejected as a non-positive capacity, or clamped as an amount). Plain Newtonsoft would reject a decimal for an `int` field and move the whole save to `.corrupt`, which is why every changed field carries the converter. A test loads a verbatim schema-7 JSON file.

An old decimal stock rounds half away from zero, so a player gains or loses at most half a unit once. Its partial regen progress restarts from the saved timestamp, which is correct. Saved offers keep their flat-50 terms until the next refresh.

**Load rules in `TradeManager`:** an offer amount < 1 is skipped; a side left empty drops the offer; an unknown resource or profitability name is skipped or falls back to Neutral (as today). A saved level above `MaxWarehouseLevel` is clamped to it.

**IL2CPP:** no new DTO *types*, and the converter is already `[Preserve]`d. The device round trip remains an outstanding known gap (not a concern while testing is Editor-only).

Per ARCHITECTURE §5, the no-migration rule still applies before the first external build. The schema bump is recorded in the summary at the end of the implementation, not as history in `GameStateDto`.

---

## 6. Assets and prefabs

### 6.1 Inventory

| Asset | GUID | Action |
|---|---|---|
| `Scripts/TradeConfig.cs` | `73a9a621ab2aab944a432a3011ac5a71` | **move** → `Scripts/Data/` via `AssetDatabase.MoveAsset` (GUID kept, so the asset's `m_Script` resolves). Namespace unchanged. |
| `ScriptableObjects/Config/TradeConfig.asset` | `73e2f9f5a7e53384bb49ec0859a65de1` | **keep**. Referenced by `GameBootstrapper` in `Bootstrap.unity`. Gains `OfferCapacityShare: 0.2`, `MaxWarehouseLevel: 20`; re-serialized after step 9 to drop `OfferBaseAmount`. |
| `Prefabs/TradeScreen.prefab` | `896cee4109d1556448e017356c09690f` | **keep asset + root GameObject** (fileID `88228022552384697`); **rebuild contents** (§6.2) |
| `Prefabs/TradeOfferRow.prefab` | `36400c5f9971ad7428578de991d3de70` | **delete** ✅ approved |
| `Prefabs/WarehouseTile.prefab` | `bc4b6f6127e3c2a4ba774386fb03ad87` | **delete** ✅ approved |
| `Localization/TradeUITable*` | — | **keep**; entries edited through the Localization editor API (§7) |
| Old scripts: `Domain/*.cs` (8), `Presenters/*.cs` (2), `Views/*.cs` (4), `TradeManager.cs`, `TradeUIText.cs` | — | quarantined in step 1, **deleted** in step 9 ✅ approved |
| Old tests `Tests/EditMode/Modules/Trade/*.cs` (6) | — | quarantined in step 1, **deleted in step 5** (superseded there, §11) ✅ approved |
| Empty `Domain/`, `Presenters/`, `Views/`, `ScriptableObjects/Data/` folders + `.gitkeep`s | — | **delete** ✅ approved |
| `Systems/Ledger/Scripts/KingdomLedger.cs`, `TradeResources/TradeResourceState.cs`, `TradeResources/TradeResourceChangedEvent.cs`, `LedgerSnapshot.cs` | — | **edit** (§2.4) |
| `Systems/Save/Scripts/Dto/TradeResourceStateDto.cs`, `TradeOfferDto.cs`, `GameStateDto.cs` | — | **edit** (§5) |
| New prefabs: `OfferRow`, `ResourceAmount`, `WarehouseGauge`, `WarehouseRow`, `WarehousesPopup`, `TradeOfferPopup` | new | **create** |
| `Systems/Popups/ScriptableObjects/PopupRegistry.asset` | — | **edit** (additive): append the two Trade popups |
| `Scenes/Main.unity` | — | **edit**: *Remove Unused Overrides* on the `TradeScreen` instance after the rebuild. Its overrides are TMP style-hash and RectTransform values on child objects that will no longer exist; none target the `TradeView` component. ✅ approved |
| `Core/Bootstrap/Scripts/GameBootstrapper.cs`, `GameStateCoordinator.cs` | — | **edit** (usings, registration) |

**Approved 🔴 items (your answer to Q9):**
1. delete `TradeOfferRow.prefab` and `WarehouseTile.prefab`;
2. remove the six tile instances, `ConfirmPanelCanvas` and the `TradeView` component from `TradeScreen.prefab`;
3. delete the old scripts and tests;
4. delete the empty folders;
5. remove 7 `TradeUITable` entries;
6. re-serialize `TradeConfig.asset`;
7. remove the unused overrides in `Main` (only after confirming nobody else has `Main` open).

### 6.2 How the prefabs are built (through MCP; no hand-edited YAML)

- **Copy the Laws prefabs for a consistent greybox look.** `AssetDatabase.CopyAsset` `CharacteristicsPopup.prefab` → `WarehousesPopup.prefab`, and `CharacteristicRow.prefab` → `WarehouseRow.prefab` (new GUIDs). Swap the scripts, add the second button and the stock label, and wire the fields with `manage_prefabs modify_contents component_properties`. Nested rows override only `_type`. The title's `LocalizeStringEvent` → `TradeUITable/ui.warehouses`.
- **`TradeOfferPopup.prefab`:**
  - full-screen root + `TradeOfferPopup`, with a `Panel` child that scales in (no backdrop — `PopupSystem` owns it);
  - title (`ui.offer.title`);
  - two headed columns (`ui.you_give`, `ui.you_get`) of 3 nested `ResourceAmount`s;
  - a blockers label and a warnings label;
  - Accept/Cancel captions → `SharedTable/ui.confirm`, `ui.cancel`, as `ConfirmPopup` does.

  It is built by one `execute_code` script (C# 6, `UnityEngine.Object` spelled out) that creates the hierarchy and calls `PrefabUtility.SaveAsPrefabAsset`, then wired with `modify_contents`.
- **`ResourceAmount`, `OfferRow`, `WarehouseGauge`** are built the same way. `OfferRow` has a raycast-target background Button and 3+3 nested `ResourceAmount`s.
- **Register both popups** in `PopupRegistry.asset` via `execute_code` (`SerializedObject`, append, apply, save).
- **`TradeScreen.prefab` rebuild** (`open_prefab_stage` → edit → `save_prefab_stage`). Keep `Background`, `SafeArea` (`SafeAreaFitter` padding 160), `TitleLabel`, `RefreshRow`, and `OfferListPanel`/`Viewport`/`Content`/`NoOffersLabel`; their fileIDs keep the scene's overrides on them valid. Then:
  - root: remove `TradeView`, add and wire `TradeScreenView`;
  - `WarehousePanel`: delete the 6 tile instances; add 6 `WarehouseGauge`s (`_type` each) in a 3×2 `GridLayoutGroup`, plus a raycast-target background `Image` + `Button` on the panel;
  - `RefreshNowButton` gains a caption (`ui.refresh_now`) beside its cost;
  - add an `Offers` heading (`ui.offers`);
  - delete `ConfirmPanelCanvas`;
  - add `ReceiptCanvas` (nested Canvas, `overrideSorting` 10, `GraphicRaycaster`, every graphic `raycastTarget = false`) with `TradeReceiptView`.

```
TradeScreen (Canvas, TradeScreenView)           static chrome
├── Background                                   full-bleed
└── SafeArea (padding.bottom 160)
    ├── TitleLabel                               "Trade"
    ├── WarehousePanel (Canvas+Raycaster, Button) 6 gauges — numbers and fills change with regen
    ├── RefreshRow (Canvas+Raycaster)            countdown (once a second) + Refresh now [3]
    ├── OffersHeading                            "Offers"
    ├── OfferListPanel (Canvas+Raycaster, ScrollRect)  rows re-lay out while dragging
    └── ReceiptCanvas (Canvas, overrideSorting 10)     animates over the list, never intercepts taps
```

**GUIDs and references that must survive:**
- `TradeConfig.cs` `73a9a621…` (the asset's `m_Script`);
- `TradeConfig.asset` `73e2f9f5…` (`GameBootstrapper._tradeConfig`);
- `TradeScreen.prefab` `896cee41…` with root fileID `88228022552384697`. `Main.unity` references that root from the nav bar's Trade tab (`NavTabButton._screen` → `479876981`) and from `BottomNavBarView._screens[1].Root`. The root is never replaced, and the cutover check confirms the Trade tab still switches.

---

## 7. Localization

Plain `{0}` formats, `en` + `uk`, edited via `LocalizationEditorSettings.GetStringTableCollection("TradeUITable")` in `execute_code`. *Code* means on `TradeUIText.AllKeys` and checked by `TradeTextTests`. *Chrome* means a `LocalizeStringEvent`, unchecked.

**Kept**

| Key | Kind | en (uk unchanged) |
|---|---|---|
| `ui.trade_title` | chrome | Trade |
| `ui.no_offers` | chrome | The caravans are still on the road |
| `ui.warehouses` | chrome — warehouses popup title | Warehouses |
| `ui.offers` | chrome — list heading | Offers |
| `ui.you_give`, `ui.you_get` | chrome — offer popup columns | You give / You get |
| `ui.refresh_now` | chrome — refresh button | Refresh now |
| `ui.blocker_insufficient` | code | You need {0} more {1} |
| `ui.warehouse_level` | code — {0} = stored level + 1 | Level {0} |
| `ui.upgrade_cost_crystals` | code | {0} crystals |
| `ui.upgrade_cost_paired` | code | {0} {1} |
| `ui.upgrade_maxed` | code | Fully upgraded |
| `ui.refresh_in` | code — {0} = `TimeFormat.MinutesSeconds` | Refresh in {0} |

**Added**

| Key | Kind | en | uk |
|---|---|---|---|
| `ui.offer.title` | chrome | Trade offer | Торгова пропозиція |
| `ui.offer.overflow` | code — {0} excess, {1} resource | {0} {1} will not fit and will be lost | {0} од. «{1}» не вміститься на складі й пропаде |
| `ui.refresh.title` | code | Refresh the offers? | Оновити пропозиції? |
| `ui.refresh.body` | code — {0} cost, {1} time | Spend {0} crystals to replace every offer now? Otherwise new offers arrive in {1}. | Витратити {0} кристалів, щоб замінити всі пропозиції зараз? Інакше нові з'являться через {1}. |
| `ui.not_enough_crystals` | code (a Trade copy, by decision) | Not enough crystals. | Недостатньо кристалів. |
| `ui.upgrade.title` | code | Upgrade the warehouse? | Покращити склад? |
| `ui.upgrade.body_crystals` | code — {0} cost, {1} resource, {2} level, {3} capacity | Spend {0} crystals to raise the {1} warehouse to level {2}? It will hold {3}. | Витратити {0} кристалів, щоб підняти склад «{1}» до рівня {2}? Він вміщатиме {3}. |
| `ui.upgrade.body_paired` | code — {0} cost, {1} paired, {2} resource, {3} level, {4} capacity | Spend {0} {1} to raise the {2} warehouse to level {3}? It will hold {4}. | Витратити {0} од. «{1}», щоб підняти склад «{2}» до рівня {3}? Він вміщатиме {4}. |
| `ui.upgrade.not_enough_paired` | code — {0} resource, {1} held | Not enough {0}: you have {1}. | Недостатньо «{0}»: у вас {1}. |
| `ui.receipt.received` | code | +{0} {1} | +{0} {1} |
| `ui.receipt.forfeited` | code | {0} {1} lost — the warehouse is full | {0} од. «{1}» втрачено — склад повний |

**Removed (approved):**
- `ui.warning_overflow` — its meaning changes (A1); a new key forces both locales to be rewritten;
- `ui.blocker_unavailable` — a vanished offer closes its popup via `closeWhen`;
- `ui.warehouse_amount` — "a / b" is a number format drawn by the view, as in Laws;
- `ui.confirm`, `ui.cancel` — the popups use `SharedTable`'s;
- `ui.upgrade`, `ui.next_refresh` — never used.

Resource names come from `SharedTable` via `TradeResourceRegistry.NameKeyFor`. The Presenter re-renders on `LocaleChanged`. Large numbers use `N0`; whether the group separator should follow the selected locale rather than the device culture goes on the known-gaps list.

---

## 8. Feel

**One haptic per action, never stacked.**

| Action | Sound | Animation | Haptic |
|---|---|---|---|
| Tap an offer row | popup open (`PopupSystem`) | Button press tint; popup scales in | — |
| Accept confirmed | `sfx_trade_offer_accept` | row collapses out; gauges tween; numbers tick; receipt slides in, holds ~2.5 s, fades | **1** |
| Accept refused at confirm (rare race) | `sfx_trade_offer_refused` | — | — |
| Cancel / tap outside | popup close (`PopupSystem`) | popup shrinks out | — |
| Instant refresh confirmed | `sfx_trade_offers_refreshed` (one sound, A2) | rows stagger in (fade + scale, ~40 ms apart) | **1** |
| Auto refresh, screen visible | `sfx_trade_offers_refreshed` | rows stagger in | — (passive) |
| Auto refresh, screen hidden | silent | list updated; nothing animates off-screen | — |
| Open the warehouses popup | popup open | popup scales in | — |
| Crystal or paired upgrade confirmed | `sfx_trade_warehouse_upgrade` (one sound, both paths) | row level punch + bar re-scale; the screen gauge punches | **1** |
| Regen | — | the gauge bar tweens when a whole unit lands | — |
| Countdown | — | label changes once a second | — |

An upgrade makes the bar *drop* (the same stock in a bigger warehouse), so the punch goes on the level and capacity numbers, which rise. Tweens run on unscaled time and are killed in `OnDisable`/`OnDestroy`.

---

## 9. Tests

### 9.1 Ported (old → new)

| Old | New | Notes |
|---|---|---|
| `OfferRefreshTimerTests` (13) | `OfferRefreshTimerTests` (same names) | unchanged |
| `WarehouseUpgradeCurveTests.Capacity_*` (6) | `WarehouseCurveTests.Capacity_*` | `IsFiniteAtAnAbsurdLevel` → `AnAbsurdLevel_IsClampedToTheConfiguredCap` |
| `…CrystalCost_MatchesTheOldHandAuthoredTable_AtTheFirstLevels` | `WarehouseCurveTests.CrystalCost_FirstLevelsAre_5_8_11_17_25` | |
| `…CrystalCost_IsStrictlyIncreasing / IsAlwaysAtLeastOne / StaysPositiveAtAnAbsurdLevel` | same names | evaluated over `0..MaxWarehouseLevel` |
| `TradeOfferGeneratorTests.LargestRemainder_*`, `Stochastic_*` (3) | `TradeOfferAllocatorTests` (same names) | |
| `TradeOfferGeneratorTests` batch / ratio / shape / profitability / ids / immutability | `TradeOfferGeneratorTests` (same names) | generated with random capacities; `EveryAmount_IsAWholeNumber` dropped (the type is `int`) |
| `TradeDomainTests.GetPairedResource_MatchesTheGddPairing`, `Pairing_IsSymmetric` | `TradeResourcePairTests` | |
| `TradeDomainTests` construction / refresh / regen / instant refresh / evaluate / overflow / accept / upgrades / save-load | `TradeManagerTests`, same scenarios | renames: `…RaisesTradeStateChanged…` → `…RaisesOffersChanged…`; `InstantRefresh_*` → `TryInstantRefresh_*`; `UpgradeWithCrystals_*` → `TryUpgradeWithCrystals_*`; `UpgradePaired_*` → `TryUpgradeWithPairedResource_*`; `LoadFromDto_V4SaveWithNoOffers…` → `LoadFromDto_WithNoOffers_GeneratesOneFreshBatch`; `…SetsBaseCapacityAndTheDerivedRegenRate` / `AfterAnyUpgrade_RegenRateStaysCapacityOver24Hours` → assert behaviour ("an empty warehouse fills in exactly 24 h") since the rate property is gone; `UpgradesStopAtTheSupportedCeiling` → `UpgradesStopAtMaxWarehouseLevel` |
| `TradePresenterTests.Constructor_RejectsNullDependencies` | same | |
| `GetOfferDisplays_ReturnsOneRowPerOffer`, `OfferLines_CarryLocalizedNamesFromTheSharedRegistry` | `GetOffers_ReturnsOneRowPerOffer`, same | |
| `WarehouseDisplay_FillFractionIsClampedToOne` | `Gauge_FillIsClampedToOne` | |
| `WarehouseDisplay_NamesThePairedResource`, `…AtTheCeiling_ReportsMaxedRatherThanAPrice` | `WarehouseRow_PairedPriceNamesThePairedResource`, `WarehouseRow_AtTheCap_ShowsFullyUpgraded` | |
| `AnUnaffordableOffer_StillProducesAConfirmationPanel`, `ABlockerMessage_NamesTheResourceAtFault`, `AnOverflowingOfferIsStillAcceptable_AndWarnsInsteadOfBlocking` | `OfferPopup_WhenShort_DisablesAcceptAndNamesTheResource`, `…_NamesTheShortfall`, `OfferPopup_WhenAWarehouseOverflows_ListsEveryLossAndKeepsAcceptEnabled` | asserts the **excess**, not the room (A1) |
| `SetScreenVisible_ReRendersOnShowOnly`, `ALocaleChange_TriggersAReRender`, `RefreshCountdown_IsResolvedThroughTheKeysType` | same | |
| `AManagerStateChange_TriggersAReRender`, `Dispose_UnsubscribesFromAllThreeSources` | `OffersChanged_TriggersAReRender`, `Dispose_StopsListening` | |
| `TradeTextTests` | `TradeTextTests` against `Trade.UI.TradeUIText` | |
| Ledger: `SetWarehouseCapacity_ChangesCapacityAndRegenRate` | `SetWarehouseCapacity_ChangesCapacityAndTheFillTime` | |
| Ledger: `RoundTrip_PreservesWarehouseCapacityAmountAndRegenRate` | `RoundTrip_PreservesCapacityAmountAndRegenProgress` | |
| Ledger: `Load_NonFiniteValues_FallBackToDefaults` | same name; the trade part now goes through the converter (NaN → 0 → defaults) | |
| Ledger: the other trade tests (`AddTradeResource_*`, `SpendTradeResource_*`, `HasTradeResource_*`, `Accrue…`, `TakeSnapshot_*`, the save clamps) | same names, `int` literals | |

### 9.2 Dropped, and why

| Old | Why |
|---|---|
| `TradePresenterTests.*Cost_IsTheCostCharged` (×3) | Prices live on the Manager; "quoted == charged" moves to `TradeManagerTests` across every level (§9.3). |
| `AcceptingAnOffer_PlaysASoundAndAHaptic`, `ARefusedOffer_MakesANoiseButDoesNotBuzz`, `UpgradingAWarehouse_RaisesOnWarehouseUpgraded`, `AFailedUpgrade_RaisesNothingAndIsSilent` | These actions now run only after a popup confirmation, which EditMode cannot answer (the same constraint as Laws' buy-up). Verified in Play mode (§9.4). |
| `ACrossModuleResourceChange_TriggersAReRender` | The Presenter no longer listens to the bus; replaced by `Gauge_ReflectsAnExternalLedgerChangeWithoutAnyEvent`. |
| `OfferRow_MarksOverflowWithoutMarkingItUnacceptable` | Rows carry no hints (decision 5). |
| `TryGetConfirmation_ForAnUnknownId_ReturnsFalse` | Replaced by `OfferPopup_ForAMissingOffer_CannotBeAccepted`. |
| `TradeOfferGeneratorTests.EveryAmount_IsAWholeNumber` | Guaranteed by `int`. |
| Ledger `TradeResourceMutations_NonFinite_AreRefused` | An `int` cannot be non-finite; the file-level case is covered by the converter test. |

### 9.3 New

- **`TradeOfferSizingTests`:**
  - `GiveTotal_AtBaseCapacity_Is40For2v2And60For3v3`
  - `GiveTotal_UsesTheSmallerSide`
  - `GiveTotal_IsNeverBelowTheMinimum`
  - `GiveTotal_AtLevel10_Is2308`
  - `Split_IsProportionalToCapacity`
  - `Split_SumsExactlyAndGivesEveryResourceAtLeastOne`
  - `AtTheMinimum_TheBandsStayDistinct`
- **`TradeOfferGeneratorTests` (added):**
  - `EveryAmount_FitsWithinItsOwnWarehouse_AtTheMaximumShare` (2,000 batches, random levels 0–20, share 0.40)
  - `OffersGrowWithCapacity`
  - `TheRatioHolds_AtUnevenCapacities`
- **`TradeConfigContentTests`** (the real asset):
  - `CapacityAtTheCap_FitsUnderTheLedgerCeiling`
  - `CurvesIncreaseStrictlyUpToTheCap`
  - `TheLargestPossibleOffer_FitsAnInt`
- **`TradeManagerTests` (added):**
  - `CrystalUpgrade_QuotedEqualsCharged_AtEveryLevel` (0 → cap)
  - `PairedUpgrade_QuotedEqualsCharged_AtEveryLevel`
  - `PairedUpgradeCost_RoundsUp`
  - `CanAfford…_MatchesWhetherTheChargeSucceeds` (×3)
  - `Accept_IntoANearlyFullWarehouse_ReportsReceivedAndForfeited` (87/100, offer 30 → +13, 17 lost, stock exactly 100)
  - `Accept_ForfeitedEqualsTheWarnedExcess`
  - `Refresh_SizesOffersFromCurrentCapacities`
  - `AnUpgrade_DoesNotResizeOffersAlreadyListed`
  - `LoadFromDto_ClampsALevelAboveTheCap`
  - `LoadFromDto_GeneratesFromRestoredCapacities`
  - `OffersChanged_FiresOncePerAccept`
- **`TradePresenterTests` (added):**
  - `AutoRefresh_OnAVisibleScreen_PlaysTheSoundWithoutAHaptic`
  - `AutoRefresh_OnAHiddenScreen_IsSilent`
  - `OffersArrived_OnRefreshButNotOnAccept`
  - `OpeningAnOffer/TheRefresh/TheWarehouses_SpendsNothing` (each expects the "No popup registry" exception, as Laws' tests do)
  - `WarehousesData_UnchangedState_ComparesEqual`
  - `OfferPopupData_UnchangedState_ComparesEqual`
  - `WarehouseRow_ShowsLevelPlusOne`
  - `RefreshPrice_IsTheManagersPrice`
- **Ledger (added):**
  - `Regen_CreditsWholeUnitsOnly`
  - `Regen_ManySmallTicksEqualOneLongAbsence`
  - `Regen_EmptyWarehouseFillsInExactly24Hours_AtEveryCapacity` (100 … 332,530)
  - `Regen_FullWarehouseBanksNothing`
  - `Regen_PartialProgressSurvivesSaveAndLoad`
  - `Regen_AfterAnUpgrade_KeepsTheProgressMade`
  - `SetWarehouseCapacity_AboveTheCeiling_IsClampedAndLogged`
  - `AddTradeResource_FillingToCapacity_LandsExactlyOnCapacity`
- **Save (added):**
  - `Load_ASchema7FileWithDecimalAmounts_RoundsToWholeNumbers` — a verbatim old JSON, ledger and offers;
  - `RoundTrip_WritesIntegers`.

### 9.4 Verified in Play mode (always from `Bootstrap`)

Back up `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Kingdom Ruler\kingdom_ruler_save.json` first. Run `Application.runInBackground = true;` in the first `execute_code`. Clicks go through real `EventSystem` raycasts.

1. The existing (schema 7) save loads with no `.corrupt` file. Amounts are rounded whole, offer ids and terms are equal to the file, the deadline is unchanged, levels and capacities are correct, and the Trade tab is live.
2. Gauges show `stock / capacity` and change one whole unit at a time. A Ledger add via `execute_code` shows within a frame with no event. `TradeScreenView.Update` allocates nothing in steady state (heap growth with the GC count held constant).
3. Taps *between* gauges open the warehouses popup. Every row's level (1-based), stock, capacity and both prices are right.
4. Crystal upgrade: the confirmation shows the price; Confirm charges exactly that and closes only the confirmation; the row redraws at the new level and the gauge punches. When short: the notice shows, Confirm is inactive, nothing is charged. The same for the paired path, charged from the paired stock.
5. At the cap (level 20, via an edited save): "Fully upgraded" shows and both buttons are inactive.
6. Outside taps close the confirmation, then the popup. Nothing is charged on open or on dismiss.
7. **Every offer tap opens the confirmation.** With the give side short: a blocker per short resource and Accept disabled. Nothing is spent.
8. With a full receive warehouse: a warning per resource that will lose units, naming the exact amount; Accept enabled. Accepting shows the receipt with `+received` and `N lost`, `received + lost = offered` per line, and the gauge reads exactly full.
9. A normal accept: the row collapses, the receipt shows, the list re-renders once.
10. Instant refresh: the live price and countdown show; Confirm charges exactly the price, replaces the list and restarts the timer. When short: the notice shows and Confirm is inactive. An auto refresh landing while it is open closes it with nothing spent.
11. Auto refresh on a hidden tab: silent, and the list is updated on return.
12. Regen across a long absence (move `lastUpdatedUtc` back in the save): the stock is exactly what the formula gives.
13. Both locales: every popup string, the receipt, and the numbers.
14. Layout at `KR 3:4`, `KR 9:16`, `KR 9:20`: nothing under the nav bar; the receipt never blocks a row tap; every nested canvas's buttons respond to a real raycast.
15. Restore the save byte for byte.

**Not observable yet:** the sound ids and the haptic count on the confirmed paths, because the audio and haptic services are no-op stubs. These are checked by code reading and listed as a known gap, as for Laws.

---

## 10. Docs

**New `docs/modules/Trade.md`**, the `Laws.md` outline, present tense only:
1. The mechanic in one paragraph.
2. Structure.
3. Classes + API sketches.
4. The offer model: sizing formula and bounds; integers end to end.
5. Data flows.
6. Rules that must hold:
   - only the Manager touches Ledger/clock;
   - overflow warns, never blocks;
   - prices beside the charge, quoted == charged;
   - the deadline timer;
   - levels are authoritative and capped at `MaxWarehouseLevel`;
   - offers are persisted, never rerolled, never resized;
   - 30/45/25 is a long-run average;
   - polled, not pushed;
   - accept by id;
   - five canvases;
   - gauges/rows matched by type;
   - one haptic per action;
   - localization;
   - the warehouses popup;
   - the receipt and why it is not a popup.
7. How to extend.
8. Tests + Play-mode verification.
9. Known gaps: icons, greybox, stub SFX/haptics, `link.xml` on device, no PlayMode tests, Ukrainian plurals, the number separator, the daily-player overflow observation.

**`GDD.md`:**
- §5 glossary: trade resources stored as whole units.
- §7:
  - "Warehouse levels for all 6 resources at the top" → "warehouse stock for all 6 resources at the top; tapping that panel opens the **warehouses popup**: every warehouse as a large row with its level, stock / capacity and both upgrade options with their prices";
  - regen: "regenerate passively **one whole unit at a time**, at a rate that fills an *empty* warehouse in 24 hours";
  - replace "capped at a high ceiling purely as a save-integrity guard … not a design cap" with "Upgrade levels are capped at **[ASSUMED — CONFIRM] 20** (`TradeConfig.MaxWarehouseLevel`), shown as *Fully upgraded*; a saved level above the cap is clamped to it on load";
  - add "**Every purchase is confirmed** — accepting an offer, the instant refresh and both upgrades; when the player cannot pay, the confirmation says so and its button stays inactive; confirming an upgrade closes only the confirmation";
  - add "**Whole numbers throughout** — stock, capacity, offer amounts and prices";
  - add the §4.4 sizing bullet;
  - the offer-panel paragraph: "Tapping an offer always opens its confirmation. A side the player cannot pay disables Accept and names each short resource; a received resource that will not fit is listed with how many units will be lost, and Accept stays enabled";
  - replace "What was actually received is reported back" with "After an accept, a short receipt on the Trade screen lists what was received and, separately, what was lost to a full warehouse";
  - note the band multipliers.

**`ARCHITECTURE.md`:**
- §3 tree: the Trade comment becomes the Laws-shape lines.
- §4.3: replace "Non-finite trade amounts are refused. Trade resources stay decimal…" with the integer rule. Trade amounts are whole; regen credits whole units and carries the partial unit in `LastUpdatedUtc`; `MaxWarehouseCapacity` keeps that arithmetic exact. "`TradeConfig`'s warehouse curves" → "Trade's `WarehouseCurve`".
- §4.5:
  - the method names in "whoever decides from a trade amount…";
  - "warehouse bars read the amount each frame" → "gauges poll and redraw when a whole unit lands";
  - "continuous drift" → "regen ticks in whole units but still publishes no event — at the level cap that would be up to ~24 messages a second";
  - the "save on focus loss" paragraph stays true.
- §5: schema 8 is current.

**`docs/modules/Popups.md`:**
- §1: real `Show` + `WaitForChoice` callers (A9);
- §3: the Trade popups as module popups;
- §8: remove the "Trade's confirm panel" gap.

**`CLAUDE.md`, `Laws.md`:** no change.

---

## 11. Ordered implementation steps

Each step ends with a checkpoint. **Compile** means the console is read via MCP with no new errors or warnings (known noise: test-expected warnings, the MCP WebSocket warning, the TMP fallback-font warning). **Tests** means the EditMode suite is run via MCP and all pass. Multi-line edits are done by Python scripts in the scratchpad run with `python -I`; moves and renames go through the AssetDatabase.

**Step 0 — Baseline.** Back up the save. Compile; 392 pass (confirmed while planning).

**Step 1 — Quarantine the legacy code** (approved). Two `TradeManager`s cannot coexist, and C# name lookup prefers types in an enclosing namespace over `using` imports, so the old manager, presenter and views would bind to the *new* `TradeOffer` etc.
- `MoveAsset` every old script except `TradeConfig.cs` into `Scripts/Legacy/{Domain,Presenters,Views}/`, keeping GUIDs so `TradeScreen.prefab` keeps its components.
- Script-replace the namespaces `KingdomRuler.Modules.Trade[.X]` → `KingdomRuler.Modules.TradeLegacy[.X]`, importing `TradeConfig` by alias (`using TradeConfig = KingdomRuler.Modules.Trade.TradeConfig;`).
- Move the old tests to `Tests/EditMode/Modules/TradeLegacy/` (namespace `…Modules.TradeLegacy`).
- Update the usings in `GameBootstrapper` and `GameStateCoordinator`.
- *Checkpoint:* compile; 392 pass; Play mode: the old Trade screen still works (one accept, one upgrade).

**Step 2 — Data.**
- `MoveAsset` `TradeConfig.cs` → `Scripts/Data/`.
- Rename the legacy `const MaxWarehouseLevel` to `LegacyMaxWarehouseLevel` (legacy call sites updated), then add the `MaxWarehouseLevel` field (20) and `OfferCapacityShare` (0.20) with their `OnValidate` clamps, and set both on the asset.
- Add the `UniTask` reference to the Trade asmdef.
- *Checkpoint:* compile; 392 pass; the asset still resolves on Bootstrap's `GameBootstrapper`.

**Step 3 — Pure model + tests.**
- Classes: `TradeAmount`, `TradeProfitability`, `TradeOffer`, `TradeOfferAllocator`, `TradeOfferSizing`, `TradeOfferGenerator`, `OfferRefreshTimer`, `WarehouseCurve`, `TradeResourcePair`, `TradeIssue(Kind)`, `TradeOfferEvaluation`, `TradeAcceptResult`. `WarehouseCurve` references `KingdomLedger.MaxWarehouseCapacity`, so add that one constant here (inert until step 5).
- Tests: the matching test classes + `TradeConfigContentTests` + `FakeClock.cs`.
- *Checkpoint:* compile; all pass.

**Step 4 — UI strings.**
- Add the new `TradeUITable` entries (en + uk); keep the old ones until step 9.
- *Checkpoint:* compile; all pass.

**Step 5 — Ledger to integers + the new `TradeManager`** (🟡 approved in principle; the largest step).
- Ledger, events, snapshot and DTOs per §2.4/§5; `schemaVersion` 8; Ledger and save tests updated and added.
- New `TradeManager` + `TradeManagerTests` (not registered yet).
- Legacy runtime code: mechanical `int` casts at its ~6 Ledger call sites, so the old screen keeps working until the cutover.
- **Legacy Trade tests are deleted in this step:** they assert decimal-stock behaviour that no longer exists, and `TradeManagerTests` supersedes them in the same step (approved deletion, moved earlier).
- *Checkpoint:* compile; all pass; Play mode: the existing save loads (no `.corrupt`), the old screen still accepts an offer.

**Step 6 — UI code.**
- `TradeUIText`, view-data structs, `TradePresenter`, all views and popups.
- `TradePresenterTests`, `TradeTextTests`.
- *Checkpoint:* compile; all pass.

**Step 7 — Prefabs.**
- `ResourceAmount`, `OfferRow`, `WarehouseGauge`, `WarehouseRow`, `WarehousesPopup`, `TradeOfferPopup` (§6.2).
- Register both popups in `PopupRegistry.asset`.
- *Checkpoint:* compile; all pass; each prefab has no missing references (MCP hierarchy + component reads).

**Step 8 — Cutover.** One agent on `Main`.
- `GameBootstrapper` registers the new types and drops the factory; `GameStateCoordinator` goes back to `KingdomRuler.Modules.Trade`.
- Rebuild `TradeScreen.prefab` contents (§6.2).
- Remove unused overrides in `Main` and save.
- *Checkpoint:* compile; all pass; **the full Play-mode pass §9.4**, with the save backed up and restored byte for byte.

**Step 9 — Delete the legacy code** (approved).
- Delete `Scripts/Legacy/`, `TradeOfferRow.prefab`, `WarehouseTile.prefab` and the empty folders.
- Strip the legacy members from `TradeConfig` and re-serialize the asset.
- Remove the 7 table entries.
- *Checkpoint:* compile; all pass (count reported); Play-mode smoke (load, accept, upgrade, refresh).

**Step 10 — Docs.**
- Write `Trade.md`; apply §10.
- Grep for stale names: `TradeView`, `TradeConfirmPanelView`, `OfferBaseAmount`, `TradeStateChanged`, `RegenRatePerSecond`, `IsRejectedAsNonFinite`, `Modules.Trade.Domain`, `TradeLegacy`.
- *Checkpoint:* compile; all pass; a summary of what changed and why.

---

## 12. Risks, and the assumptions taken in revision 2

### Risks

| Risk | Mitigation |
|---|---|
| The quarantine touches 16 scripts and 6 tests mechanically. | AssetDatabase moves; a scripted namespace rewrite; a Play-mode check of the old screen. |
| Step 5 changes the Ledger and the save together. | Old-save test with a verbatim schema-7 file; a Play-mode load of the real save; the save backed up first. |
| Integer regen gets a boundary wrong (off by one unit, lost progress). | Exact `long` arithmetic; tests for small-ticks == one absence, exact 24 h fill at every capacity, progress across save/load and upgrades. |
| `TradeScreen` rebuild loses the root or a referenced child. | Edit inside the prefab stage; keep the root; verify the nav tab after the cutover. |
| `Main` scene conflict. | One agent on the scene; the only change is removing unused overrides. |
| Sizing feels off in play. | One knob, `OfferCapacityShare`; the bounds test keeps any value ≤ 0.40 safe. |
| Popup data rebuilt at 4 Hz while open. | Only while open; the same cost as the characteristics popup; redraws only on change. |
| A fast tap answers a popup still scaling in (Popups known gap). | Every charge re-validates. |
| An unfocused Editor throttles Play mode. | `runInBackground = true` first. |

### Assumptions taken while applying your answers (say if any is wrong)

1. **The level cap is a tunable `TradeConfig.MaxWarehouseLevel = 20`**, since "we might change this later". A content test proves the configured curve stays inside `int` and under the Ledger's ceiling at the cap.
2. **The Ledger gets `MaxWarehouseCapacity = 10,000,000`** as a backstop, so no tuning can overflow the integer regen arithmetic. At level 20, capacity is 332,530.
3. **`RegenRatePerSecond` is removed** from `TradeResourceState`, because with whole-unit regen it would duplicate "capacity per day". Its tests are rewritten to assert behaviour.
4. **An old decimal stock rounds half away from zero on load** (≤ ½ unit, once), and its partial regen restarts from the saved timestamp.
5. **The legacy Trade tests are deleted in step 5 instead of step 9,** because the Ledger switch invalidates their decimal assertions and their replacements land in the same step.
6. The offer confirmation behaves exactly as your answer to Q4 describes. This was already the plan; it is now spelled out in §3 and §9.4 (7–8).

No open questions remain that block step 1.
