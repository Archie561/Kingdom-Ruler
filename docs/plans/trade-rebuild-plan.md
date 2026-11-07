# Plan: rebuild the Trade module

> Written from `docs/plans/trade-rebuild-brief.md`. Baseline before any change: **392/392 EditMode
> tests pass** (confirmed via Unity MCP).
>
> **Revision 2** applied the user's first answers:
> - offer sizing approved;
> - **trade amounts become integers everywhere**, including the Ledger (supersedes brief decision 4);
> - warehouse level capped at **20**;
> - no hints on offer rows; 1-based levels;
> - "Refresh now" always tappable;
> - a Trade-local "Not enough crystals";
> - the 🔴 list and the quarantine approach approved.
>
> **Revision 3** applies the layout change:
> - six always-visible **resource tiles** (icon, name, level, amount / capacity + bar);
> - a **per-resource warehouse popup** opened by tapping a tile (upgrades + regen progress), replacing the one all-warehouses popup;
> - **no collect button** — regen stays automatic (user's choice);
> - the offer list fills the rest of the screen, with the countdown and refresh button in its header;
> - the screen title is dropped (the Laws screen has none either).
>
> **GDD §7 is already updated to match** (done with revision 3).
>
> **Progress:** all steps (0–10) done (§11). The module's reference is now `docs/modules/Trade.md`.

---

## 0. Corrections to the brief's audit

| # | Finding | Where |
|---|---|---|
| A1 | **The overflow warning shows the wrong number.** It shows the room left, while GDD §7 asks for how much will not fit. `TradeUIText.WarningOverflow`'s doc comment says "{1} = units that will not fit". | `TradePresenter.DescribeIssue` |
| A2 | **Two sounds in one frame in two places:** the crystal upgrade (`CrystalSpend` + `WarehouseUpgrade`) and the instant refresh (`CrystalSpend` + `OffersRefreshed`). | `TradePresenter` |
| A3 | **Unused `TradeUITable` keys.** The prefab uses only `ui.trade_title`, `ui.no_offers`, `ui.confirm`, `ui.cancel`. Unused: `ui.warehouses`, `ui.offers`, `ui.you_give`, `ui.you_get`, `ui.refresh_now`, `ui.upgrade`, `ui.next_refresh`. | `TradeScreen.prefab` |
| A4 | **Offer amounts are `float`** in the model and the DTO, though always whole. | `TradeOffer`, `TradeOfferDto` |
| A5 | Capacity exceeds `int` from level 42 under the old cap of 50. **Resolved:** the cap is level 20 as shown, stored 19 (capacity 221,680), and the Ledger gets a hard capacity ceiling (§2.4). | `TradeConfig` |
| A6 | `schemaVersion` is never read on load. **Not an issue until the first build** (user). The plan still bumps it on a shape change. | `LocalJsonSaveService` |
| A7 | The Trade asmdef lacked `UniTask`. **Fixed in step 2.** | asmdef |
| A8 | `ScriptableObjects/Data/` is an empty folder holding a `.gitkeep`. | Trade module |
| A9 | `Popups.md` §1 says no caller uses `Show` + `WaitForChoice` yet; `LawsPresenter.OpenCharacteristics` does. | `docs/modules/Popups.md` |

---

## 1. Summary

**Goal.** Rebuild `Modules/Trade` in the Laws shape (`Data/` · `Model/` · `UI/`). Keep every behaviour in the brief's "keep" list, fix every listed defect and A1–A2, and deliver:
- offers sized by warehouse capacity (§4);
- six always-visible resource tiles, each opening its own warehouse popup;
- a confirmation for every purchase;
- whole numbers everywhere (§2.4);
- a receipt of what was received and what was lost after each accept.

**In scope:**
- all Trade scripts, `TradeConfig` (script moved, asset GUID kept), `TradeScreen.prefab` contents, new component and popup prefabs, `TradeUITable` entries, Trade tests;
- `GameBootstrapper` registration and `PopupRegistry.asset` entries;
- the Ledger's trade-resource API and regen becoming integer, plus two regen read-outs for the popup (🟡, decided by the user);
- `TradeResourceStateDto` and `TradeOfferDto` becoming integer, with `schemaVersion` 7 → 8;
- one formatting helper in `Shared/Text/TimeFormat` (h:mm:ss);
- the Ledger, save and `TimeFormat` tests these touch;
- `docs/modules/Trade.md` and the ARCHITECTURE/Popups text describing them.

**Out of scope:**
- characteristics, gold and crystals;
- `IPurchasingService`;
- art, final audio and real haptics (stubbed);
- a currency header and a profitability badge;
- a shared toast system;
- **manual resource collection** (decided against: regen stays automatic, GDD §7);
- Laws' "Not enough crystals" key;
- any Laws change.

---

## 2. Target structure

```
Modules/Trade/
  Scripts/                                    assembly KingdomRuler.Modules.Trade (references UniTask)
    Data/                                     namespace KingdomRuler.Modules.Trade
      TradeConfig.cs                          values only + OnValidate   (moved — step 2 ✔)
    Model/                                    namespace KingdomRuler.Modules.Trade
      TradeManager.cs                         entry point: lifecycle, clock, every Ledger transaction, every price
      TradeOffer.cs                           one offer: id, give/receive amounts, profitability (immutable)
      TradeAmount.cs                          (resource, units)
      TradeProfitability.cs                   Profitable / Neutral / Unprofitable
      TradeOfferGenerator.cs                  a batch: band mix, resource types, amounts
      TradeOfferSizing.cs                     the size formula (§4) — static, pure
      TradeOfferAllocator.cs                  whole-number apportionment (stochastic + largest remainder)
      OfferRefreshTimer.cs                    the 20-minute deadline
      WarehouseCurve.cs                       capacity(L), crystalCost(L)
      TradeResourcePair.cs                    Stone↔Wood, Metal↔Minerals, Leather↔Clay
      TradeIssue.cs / TradeIssueKind.cs       (kind, resource, units)
      TradeOfferEvaluation.cs                 every issue for one offer + CanAccept — mutates nothing
      TradeAcceptResult.cs                    Accepted, Issues, Received, Forfeited
    UI/                                       namespace KingdomRuler.Modules.Trade.UI
      TradePresenter.cs                       model ↔ view; popups; feel hooks
      TradeScreenView.cs                      screen root: widgets, polling, forwarding taps
      TradeUIText.cs                          String Table keys for text built in code + AllKeys
      ResourceTile/
        ResourceTileView.cs                   one tile (×6): icon, name, level, amount / capacity + bar; a tap target
        ResourceTileViewData.cs
      ResourceAmount/
        ResourceAmountView.cs                 icon + "12 Stone" — repeats in rows and the offer popup
        ResourceAmountViewData.cs
      OfferRow/
        OfferRowView.cs                       one offer in the list: 3+3 amount slots, tap
        OfferRowViewData.cs
      OfferList/
        OfferListView.cs                      the scrolling list: one pooled row per offer id, arrival/removal animation
      TradeOfferPopup/
        TradeOfferPopup.cs                    Popup<TradeOfferPopupData, TradeOfferPopupChoice>
        TradeOfferPopupData.cs                give/receive lines + blockers + warnings + CanAccept (IEquatable)
        TradeOfferPopupChoice.cs              Accept / Cancel
      WarehousePopup/
        WarehousePopup.cs                     Popup<WarehousePopupData, WarehousePayment> — one resource
        WarehousePopupData.cs                 level, stock, regen, both prices (IEquatable)
        WarehousePayment.cs                   Crystals / PairedResource — the pressed upgrade button
      Receipt/
        TradeReceiptView.cs                   the post-accept strip: "+13 Wood", "17 Wood lost"
        TradeReceiptViewData.cs
        TradeReceiptLineViewData.cs
  ScriptableObjects/Config/TradeConfig.asset  KEPT (GUID 73e2f9f5a7e53384bb49ec0859a65de1)
  Prefabs/      TradeScreen (kept root), ResourceTile, ResourceAmount, OfferRow,
                TradeOfferPopup, WarehousePopup
  Localization/ TradeUITable (kept collection, entries edited)
```

`ResourceTile` and `OfferRow` are deliberately not named `WarehouseTile`/`TradeOfferRow`: the old prefabs keep those file names until step 9.

**Dependencies:** `UI → Model → Data`, and everything may use `Systems/` and `Shared/`. The Presenter needs no `EventBus` (§3, "Polled, not pushed").

```
 UI      TradeScreenView ──► TradePresenter ──► TradeManager, KingdomLedger (reads), TradeResourceRegistry,
         ├─ ResourceTileView ×6                  PopupSystem, IAudioService, IHapticService, ILocalizationService
         ├─ OfferRowView ×N ─► ResourceAmountView ×6
         ├─ TradeReceiptView
         ├─ TradeOfferPopup (via PopupSystem) ─► ResourceAmountView ×6
         └─ WarehousePopup  (via PopupSystem)
 Model   TradeManager ──► TradeOfferGenerator ──► TradeOfferSizing, TradeOfferAllocator
            ├──► OfferRefreshTimer, WarehouseCurve, TradeResourcePair
            └──► KingdomLedger (writes), IClock
 Data    TradeConfig
```

**Registration:** `RegisterInstance(TradeConfig)`, `Register<TradeManager>`, `Register<TradePresenter>`. The factory lambda goes, and the production constructor is `[Inject]`.

### 2.1 Data

| Class | Responsibility |
|---|---|
| `TradeConfig` | `OfferCount` (10), `OfferRefreshTimeSeconds` (1200), `InstantRefreshCrystalCost` (3), `OfferCapacityShare` (0.20, clamped to `[MinOfferCapacityShare, MaxOfferCapacityShare]` = `[0.01, 0.40]`), `MaxWarehouseLevel` (20, ≥ 1), `CapacityBase/Growth/RoundTo` (100/1.5/10), `CrystalCostBase/Growth` (5/1.5). **No formulas.** The legacy members (`OfferBaseAmount`, `LegacyMaxWarehouseLevel`, `WarehouseCapacityAt`, `WarehouseUpgradeCrystalCost`, `Grow`) stay only while `Scripts/Legacy/` exists (removed in step 9). *Status: new fields added in step 2.* |

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
    int   WarehouseLevel(TradeResourceType t);   bool CanUpgradeFurther(TradeResourceType t);
    int   CapacityAfterUpgrade(TradeResourceType t);                    // "it will hold N" in the confirmation
    int   CrystalUpgradeCost(TradeResourceType t);  bool CanAffordCrystalUpgrade(TradeResourceType t);
    bool  TryUpgradeWithCrystals(TradeResourceType t);
    int   PairedUpgradeCost(TradeResourceType t);   bool CanAffordPairedUpgrade(TradeResourceType t);
    bool  TryUpgradeWithPairedResource(TradeResourceType t);
    const float PairedUpgradeShare = 0.8f;      // GDD §7, settled

    // Regen read-outs for the warehouse popup — the Ledger's numbers at the clock's now
    float NextUnitProgress(TradeResourceType t);    // 0..1 toward the next unit; 0 when full
    float SecondsPerUnit(TradeResourceType t);
    float SecondsUntilFull(TradeResourceType t);    // 0 when full
    int   UnitsPerHour(TradeResourceType t);        // capacity / 24, rounded down
}
```

Rules the Manager carries:
- **It is the only class touching the Ledger or the clock.** Each price sits beside its check and its charge.
- **Every `Try…` settles regen first** (`AccruePassiveResourceRegen`, never `AdvanceTo`), then re-reads its price. `CanAfford…` are display reads; the charge re-validates.
- **All integer.** A warehouse's room is `capacity − stock`. An overflow issue carries `excess = amount − room`. On accept, `received` is `AddTradeResource`'s return value and `forfeited = amount − received`. So `received + forfeited == offered` per line, and the warned excess is exactly the forfeit unless stock changed in between.
- **Paired price** = `ceil(capacity(paired) × 0.8)`, computed as `(capacity × 4 + 4) / 5`.
- **The level is the stored fact.** Capacity is `WarehouseCurve.Capacity(config, level)` pushed into the Ledger. A loaded level is clamped to `0..WarehouseCurve.TopLevel` (= `MaxWarehouseLevel − 1`, since `MaxWarehouseLevel` is the level as shown).
- **`OffersChanged` fires once per change:** a refresh, an instant refresh or an accept. Upgrades do not raise it; tiles and popups poll.

| Class | Responsibility | Pure? |
|---|---|---|
| `TradeOffer` | `Id`, `Give`/`Receive` as `IReadOnlyList<TradeAmount>` (generation order), `Profitability`, `TotalGive`/`TotalReceive`. Immutable. | yes |
| `TradeAmount` | `readonly struct (TradeResourceType Resource, int Units)`. | yes |
| `TradeOfferGenerator` | `GenerateBatch(int count, float capacityShare, IReadOnlyDictionary<TradeResourceType,int> capacities)`. Band mix via `ApportionStochastic`, shuffled; types from one shuffled list (no overlap, 2–3 a side); total via `TradeOfferSizing`; multipliers 110–140 / 95–105 / 60–90 %. Takes a `System.Random`. | yes |
| `TradeOfferSizing` | `GiveTotal(share, giveCaps, receiveCaps)`, `Split(total, caps, Random)`. Constants `MinGiveTotal = 10`, `JitterMin = 0.75`, `JitterMax = 1.25`. | yes |
| `TradeOfferAllocator` | `ApportionStochastic`, `ApportionLargestRemainder` — ported behaviour. | yes |
| `OfferRefreshTimer` | Deadline, one refresh per settle, phase preserved, pulled in to one interval — ported behaviour. | yes |
| `WarehouseCurve` | `Capacity(config, level)` and `CrystalCost(config, level)`. `TopLevel(config)` = `MaxWarehouseLevel − 1`, the highest stored level. The level is clamped to `0..TopLevel`, computed in `double`, then held to `[roundTo, KingdomLedger.MaxWarehouseCapacity]` and `[1, int.MaxValue]`. Banker's rounding as in `CharacteristicLevelingCurve` (`100 × 1.5² → 220`). | yes |
| `TradeResourcePair` | `PairedWith(type)`. Stays in code. | yes |
| `TradeIssue` / `TradeIssueKind` | `(Kind, Resource, int Units)` — the shortfall or the excess. `BlocksAcceptance` is false only for `WouldOverflowWarehouse`. | yes |
| `TradeOfferEvaluation` | `OfferExists`, `Issues`, `CanAccept`. | yes |
| `TradeAcceptResult` | `Accepted`, `Issues`, `Received`, `Forfeited` (only lines with a loss). | yes |

The offer DTO mapping stays as private statics in `TradeManager`, tested through `LoadFromDto`/`ToDto`.

### 2.3 UI

```csharp
public sealed class TradePresenter : IDisposable
{
    TradePresenter(TradeManager, KingdomLedger, IAudioService, IHapticService,
                   ILocalizationService, TradeResourceRegistry, PopupSystem);

    event Action StateChanged;                          // offers list or locale changed — re-render
    event Action OffersArrived;                         // a new batch is on screen — rows animate in
    event Action<TradeReceiptViewData> TradeCompleted;  // show the receipt; the accepted row collapses
    event Action<TradeResourceType> WarehouseUpgraded;  // that tile celebrates

    // Polled by the view every frame — numbers only, no strings, no allocation
    ResourceTileViewData GetTile(TradeResourceType t);  float SecondsUntilRefresh;
    // Text, read on StateChanged or when the displayed number changes
    string ResourceName(TradeResourceType t);   string LevelText(int displayedLevel);
    string RefreshCountdownText(int seconds);   // via TimeFormat.MinutesSeconds
    OfferRowViewData[] GetOffers();   int InstantRefreshCost;

    // Popup data — read by the popups at 4 Hz
    TradeOfferPopupData GetOfferPopup(string offerId);   WarehousePopupData GetWarehouse(TradeResourceType t);

    // Actions
    UniTaskVoid OpenOffer(string offerId);   UniTaskVoid OpenWarehouse(TradeResourceType t);
    UniTaskVoid RequestInstantRefresh();     void SetScreenVisible(bool visible);
}
```

| Class | Responsibility |
|---|---|
| `TradePresenter` | Builds localized view data, runs the popup flows, owns sound and haptics, gates passive sound on visibility, re-renders on `OffersChanged` and `LocaleChanged`. No Unity UI types. Holds the `PopupSystem` MonoBehaviour, as `LawsPresenter` does. |
| `TradeScreenView` | Root of `TradeScreen.prefab`, replacing `TradeView` on the same GameObject. Wires the six tiles, the rows and the refresh button to the Presenter. In `Update`: the countdown (only on a whole-second change) and the six tiles (each dirty-checks). Logs an error and disables itself when not injected. Checks there is exactly one tile per resource. Never advances anything. |
| `ResourceTileView` | `Type` (serialized). A Button covering the whole tile raises `Pressed(type)`. `Render(data)` dirty-checks stock, capacity and level. It asks for text (`ResourceName`, `LevelText`) only on first render, on a level change and on `StateChanged` (locale), so steady-state polling allocates nothing. `PlayUpgrade()` punches the level label. An icon with no sprite is hidden. |
| `ResourceAmountView` | Icon (hidden when null) + "12 Stone". |
| `OfferRowView` | Fixed 3 give + 3 receive `ResourceAmountView` slots, unused ones hidden. `Render`, `PlayArrival(delay)`, `PlayRemoval()`, `Pressed(id)`. **No affordability or overflow marking.** A hidden row is inert. |
| `TradeOfferPopup` | Two columns of 3 `ResourceAmountView`s, a blockers label, a warnings label, Accept (interactive iff `CanAccept`) + Cancel. Fixed text by `LocalizeStringEvent`. |
| `WarehousePopup` | One resource. Title ("Stone warehouse"); icon; "Level 7"; `stock / capacity` + Filled-Horizontal fill bar; a **regen block** — a next-unit progress bar, "+31 per hour", "Full in 5:12:03" (or "Full"); and two upgrade buttons, each a caption ("Upgrade") over its price ("38 crystals" / "608 Wood"). At the cap: "Fully upgraded" and both buttons inactive. Below the cap both are always tappable; affordability shows in the confirmation. The next-unit bar is hidden while the warehouse is full or a unit takes under a second, because it would only flicker. Choice: `WarehousePayment`. No close button. |
| `TradeReceiptView` | Up to 6 lines (3 received + up to 3 lost) for ~2.5 s on unscaled time, then fades; a new receipt replaces the current one; no raycast targets. |
| View-data structs | What each view draws. The only `UnityEngine.Object` on them is a `Sprite`. `TradeOfferPopupData` and `WarehousePopupData` are `IEquatable` (arrays compared element by element, as `CharacteristicsPopupData` does). `WarehousePopupData` compares the next-unit progress quantized to 1/100, so the popup redraws on a visible change rather than every read. |

### 2.4 Ledger: trade amounts become integers (🟡 — decided by the user; implemented in step 5)

**Why integers.** Every reader of a decimal stock needs a floor, which Trade and Cities would have to agree on. A filled warehouse can land one float-ulp under capacity (99/100), and every mutator needs a NaN guard. Integers give one number per resource, and it is the one on screen.

Regen does not need a fraction. It needs to remember how far into the next unit it is, and the timestamp the state already holds can carry that. This is the deadline idea already used by `ReplenishmentTimer` and `OfferRefreshTimer` (ARCHITECTURE §4.5).

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
- **The arithmetic is exact in `long`** while `capacity ≤ 10,675,199`. The Ledger gets `public const int MaxWarehouseCapacity = 10_000_000`; `SetWarehouseCapacity` and `LoadFromDto` clamp to it (logged), and Trade's curve saturates at it. At the top level (stored 19, shown 20) capacity is 221,680.
- Advancing `Last` by the floored tick count can only move it earlier (by < 100 ns per credit), so progress is never lost.
- Many small ticks give exactly the same result as one long absence (tested).
- **The save needs nothing new:** `amount` + `lastUpdatedUtc` carry the whole state.
- Upgrades settle regen first; a partial unit carries over into the new rate as elapsed time.
- **Still publishes no event.** At the level cap that would be up to ~24 bus messages a second, so tiles and popups poll.

**API after the change:**
```csharp
// TradeResourceState (setters stay internal)
int Amount;   int Capacity;   DateTime LastUpdatedUtc;          // RegenRatePerSecond removed (derived)
TimeSpan TimePerUnit;                                           // TicksPerDay / Capacity
float    NextUnitProgress(DateTime now);                        // 0..1; 0 when full
TimeSpan TimeUntilFull(DateTime now);                           // zero when full
// KingdomLedger
public const int MaxWarehouseCapacity = 10_000_000;
int  AddTradeResource(TradeResourceType, int amount);           // returns the units that fit
bool SpendTradeResource(TradeResourceType, int amount);
bool HasTradeResource(TradeResourceType, int amount);
void SetWarehouseCapacity(TradeResourceType, int capacity);     // > 0, clamped to MaxWarehouseCapacity
// TradeResourceChangedEvent(type, int newAmount, int delta);  LedgerSnapshot amounts : int
```
- The three read-outs are pure reads over the state, taking `now` because the Ledger holds no clock. They live on the state because they are regen's rules, which are Ledger-owned (ARCHITECTURE §4.3); the popup is their first caller.
- Negative amounts still throw. The non-finite guard goes: corrupt numbers in a file are handled by `WholeNumberJsonConverter` (§5).
- Nothing outside Trade and the Ledger's own tests uses this API (checked).

---

## 3. Data flows

### Startup / load
```
GameStateCoordinator.LoadOrInitialize()
  ├─ save → KingdomLedger.LoadFromDto(ledger)       (whole amounts, capacity clamped, timestamps)
  │         TradeManager.LoadFromDto(trade)
  │           1. levels → clamp 0..TopLevel → SetWarehouseCapacity(WarehouseCurve.Capacity)
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
  └─ 6 × GetTile → each tile redraws only when stock, capacity or level changed
```

The Presenter tells a refresh from an accept by the deadline: a refresh always moves it, an accept never does.

**Polled, not pushed.** The Presenter does not subscribe to `TradeResourceChangedEvent`. Tiles and open popups poll, so changes from any mechanic show within a frame and one accept causes exactly one re-render.

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
                → TradeCompleted(receipt): row collapses, tiles tween, receipt strip:
                     "+13 Wood"  "+30 Clay"  "17 Wood lost — the warehouse is full"
        backdrop / Cancel / offer refreshed away → nothing happens
```

**Why a receipt strip rather than a result popup:**
- `Ask` closes the confirmation on the tap, so a result inside it would turn a question into a panel with a second "OK".
- A result popup costs a tap per trade, against "cozy" (GDD §2).
- The strip names every amount, with the loss on its own line, with no extra tap, right above the tiles that visibly change. It is one Trade component; the first other module that wants "+500 gold" is the trigger to promote it to `Shared/UI`.

### Instant refresh
```
Refresh button in the offer list's header (always tappable) → Presenter.RequestInstantRefresh()
  └─ ConfirmPopup.Ask(() => (title, "Spend 3 crystals to replace every offer now? Otherwise new offers
                                     arrive in 12:41." [+ "Not enough crystals."], canConfirm: CanAfford),
                      closeWhen: () => NextRefreshDueUtc != dueWhenOpened)   // the free refresh landed
       Confirm → TryInstantRefresh() → OffersChanged → refreshed sound + ONE haptic, rows animate in
```

### A warehouse popup and its upgrades
```
Tap anywhere on the Stone tile → Presenter.OpenWarehouse(Stone)
  └─ WarehousePopup.Show(() => GetWarehouse(Stone))                stays open, re-reads at 4 Hz
       shows: "Stone warehouse", Level 6, 412 / 760 + bar, next-unit bar, "+31 per hour",
              "Full in 8:30:12", [Upgrade · 38 crystals]  [Upgrade · 608 Wood]
       loop: WaitForChoice → payment
         Crystals → ConfirmPopup.Ask(() => ("Upgrade the warehouse?",
                       "Spend 38 crystals to raise the Stone warehouse to level 7? It will hold 1,140."
                       [+ "Not enough crystals."], canConfirm: CanAffordCrystalUpgrade(Stone)))
                    Confirm → TryUpgradeWithCrystals(Stone)        settle regen, re-read the price, charge, level++
         Paired   → ConfirmPopup.Ask(() => ("Upgrade the warehouse?",
                       "Spend 608 Wood to raise the Stone warehouse to level 7? It will hold 1,140."
                       [+ "Not enough Wood: you have 412."], canConfirm: CanAffordPairedUpgrade(Stone)))
                    Confirm → TryUpgradeWithPairedResource(Stone)  settle regen, re-read the price, spend Wood
         success → upgrade sound + ONE haptic → WarehouseUpgraded(Stone): the tile's level punches;
                   the popup redraws at the new level on its next read
       tap outside → closes the top popup only: the confirmation first, then the warehouse popup
```

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
- **30/45/25 keeps its meaning:** 1:1 valuation, profitability = `receiveTotal / giveTotal`, the stochastic band mix.
- **Minimum 10 given** keeps the bands distinct at any capacity (profitable ≥ 11 > 10, unprofitable ≤ 9 < 10). It is a correctness floor, a tested constant, and never binds at default tuning.
- **Jitter narrowed** from `0.01–1.01` to `0.75–1.25`, so a single offer is less lopsided than today. Deliberate.
- **Rounding is half away from zero**, explicit.

### 4.2 Worked numbers (share 0.20, base 100, growth 1.5, roundTo 10)

Capacity: L0 100 · L1 150 · L2 220 · L3 340 · L4 510 · L5 760 · L6 1,140 · L7 1,710 · L8 2,560 · L9 3,840 · L10 5,770 · L15 43,790 · L19 (cap, shown as 20) 221,680. Stored levels; the screen shows level + 1.

| Level (cap) | 2v2 / 3v2 / 2v3 give | 3v3 give | Profitable receives | Neutral receives | Unprofitable receives |
|---|---|---|---|---|---|
| L0 (100) | 40 | 60 | 44–56 / 66–84 | 38–42 / 57–63 | 24–36 / 36–54 |
| L1 (150) | 60 | 90 | 66–84 / 99–126 | 57–63 / 86–95 | 36–54 / 54–81 |
| L3 (340) | 136 | 204 | 150–190 / 224–286 | 129–143 / 194–214 | 82–122 / 122–184 |
| L5 (760) | 304 | 456 | 334–426 / 502–638 | 289–319 / 433–479 | 182–274 / 274–410 |
| L10 (5,770) | 2,308 | 3,462 | 2,539–3,231 / 3,808–4,847 | 2,193–2,423 / 3,289–3,635 | 1,385–2,077 / 2,077–3,116 |
| L19, the cap (221,680) | 88,672 | 133,008 | — | — | — |

| Offer (Stone, Wood at L5 = 760; the rest at L0 = 100) | min(Σgive, Σreceive) | Give total | Split |
|---|---|---|---|
| Stone + Wood → Metal + Clay | 200 | 40 | ~20 Stone, ~20 Wood → ~20 Metal, ~20 Clay (neutral) |
| Metal + Clay → Stone + Wood | 200 | 40 | ~20 Metal, ~20 Clay → ~20 Stone, ~20 Wood |
| Stone + Clay → Wood + Metal | 860 | 172 | ~152 Stone, ~20 Clay → ~152 Wood, ~20 Metal |

### 4.3 Bounds and the overflow warning

- Each give amount ≤ ≈ **33 %** of its own warehouse; each receive amount ≤ ≈ **47 %** (±1 for rounding).
- So no offer ever asks for more than its warehouse holds, and an offer overflows only a warehouse more than about half full, at every level.
- `OfferCapacityShare ≤ 0.40` keeps every amount within its own warehouse; a test pins it at the maximum.
- The largest possible offer at the cap is ~560k units, far inside `int`.
- **Balance note:** a once-a-day player often has full warehouses and sees overflow warnings on most offers. That is true today; sizing keeps it consistent across levels.
- **Offers already listed are not resized by an upgrade.**
- Where the numbers live: `OfferCapacityShare` in `TradeConfig`; `MinGiveTotal` and the jitter bounds as constants in `TradeOfferSizing`; the multipliers in `TradeOfferGenerator`.

The GDD §7 text for all of this is already in place.

---

## 5. Save

**Shape changes (types only, no new or removed fields). `schemaVersion` 7 → 8.**

| DTO field | Before | After |
|---|---|---|
| `TradeResourceStateDto.amount` | `float` | `int` + `[JsonConverter(typeof(WholeNumberJsonConverter))]` |
| `TradeResourceStateDto.capacity` | `float` | `int` + the same converter (still a fallback; Trade re-derives capacity from the level) |
| `TradeResourceStateDto.lastUpdatedUtc` | ISO string | unchanged; it now means "when the unit in progress started" |
| `TradeOfferDto.give` / `.receive` | `Dictionary<string, float>` | `Dictionary<string, int>` + `[JsonProperty(..., ItemConverterType = typeof(WholeNumberJsonConverter))]` |
| `GameStateDto.CurrentSchemaVersion` | 7 | 8 |

**An existing save stays readable.** The converter reads `87.34` as 87, `25.0` as 25, and NaN as 0. Plain Newtonsoft would reject a decimal for an `int` and move the whole save to `.corrupt`, which is why every changed field carries it. A test loads a verbatim schema-7 file.

An old decimal stock rounds half away from zero (at most ½ unit, once). Saved offers keep their flat-50 terms until the next refresh.

**Load rules in `TradeManager`:** an offer amount < 1 is skipped; an empty side drops the offer; unknown names are skipped or fall back to Neutral; a level above `TopLevel` (`MaxWarehouseLevel − 1`) is clamped to it.

**IL2CPP:** no new DTO types, and the converter is `[Preserve]`d. The device round trip remains an outstanding known gap.

---

## 6. Assets and prefabs

### 6.1 Inventory

| Asset | GUID | Action |
|---|---|---|
| `Scripts/Data/TradeConfig.cs` | `73a9a621ab2aab944a432a3011ac5a71` | **moved** in step 2 (GUID kept) ✔ |
| `ScriptableObjects/Config/TradeConfig.asset` | `73e2f9f5a7e53384bb49ec0859a65de1` | **keep**. New fields set in step 2 ✔; re-serialized after step 9 to drop `OfferBaseAmount`. |
| `Prefabs/TradeScreen.prefab` | `896cee4109d1556448e017356c09690f` | **keep asset + root GameObject** (fileID `88228022552384697`); **rebuild contents** (§6.2) |
| `Prefabs/TradeOfferRow.prefab` | `36400c5f9971ad7428578de991d3de70` | **delete** ✅ approved |
| `Prefabs/WarehouseTile.prefab` | `bc4b6f6127e3c2a4ba774386fb03ad87` | **delete** ✅ approved (replaced by `ResourceTile.prefab` + `WarehousePopup.prefab`) |
| `Localization/TradeUITable*` | — | **keep**; entries edited (§7) |
| `Scripts/Legacy/**` (16 scripts) | — | quarantined in step 1 ✔, **deleted** in step 9 ✅ |
| `Tests/EditMode/Modules/TradeLegacy/*` (6) | — | quarantined in step 1 ✔, **deleted in step 5** ✅ |
| Empty `Domain/`, `Presenters/`, `Views/`, `ScriptableObjects/Data/` folders + `.gitkeep`s | — | **delete** ✅ |
| Ledger: `KingdomLedger.cs`, `TradeResourceState.cs`, `TradeResourceChangedEvent.cs`, `LedgerSnapshot.cs` | — | **edit** (§2.4) |
| Save: `TradeResourceStateDto.cs`, `TradeOfferDto.cs`, `GameStateDto.cs` | — | **edit** (§5) |
| `Shared/Text/TimeFormat.cs` | — | **edit**: add `HoursMinutesSeconds` |
| New prefabs: `ResourceTile`, `ResourceAmount`, `OfferRow`, `TradeOfferPopup`, `WarehousePopup` | new | **create** |
| `Systems/Popups/ScriptableObjects/PopupRegistry.asset` | — | **edit** (additive): append the two Trade popups |
| `Scenes/Main.unity` | — | **edit**: *Remove Unused Overrides* on the `TradeScreen` instance after the rebuild ✅ |
| `Core/Bootstrap/Scripts/GameBootstrapper.cs`, `GameStateCoordinator.cs` | — | edited in step 1 (legacy aliases ✔); **edit** again at cutover |

**Approved 🔴 items:**
1. delete the two old prefabs;
2. remove the six tile instances, `ConfirmPanelCanvas`, `TitleLabel` and the `TradeView` component from `TradeScreen.prefab` (`TitleLabel` is new in revision 3, since the title is dropped);
3. delete the old scripts and tests;
4. delete the empty folders;
5. remove unused `TradeUITable` entries — **now 9** (revision 3 adds `ui.trade_title`, `ui.offers`, `ui.warehouses`, and keeps `ui.upgrade`, which comes back into use, §7);
6. re-serialize `TradeConfig.asset`;
7. remove the unused overrides in `Main`.

### 6.2 How the prefabs are built (through MCP; no hand-edited YAML)

- **`WarehousePopup.prefab`:** start from a copy of `ConfirmPopup.prefab` (`AssetDatabase.CopyAsset`, new GUID) for the same panel frame and styling. Swap `ConfirmPopup` for `WarehousePopup` and build the content inside `Panel`:
  - title;
  - icon + level row;
  - stock label + fill bar (`Image` Filled, Horizontal);
  - regen block (next-unit bar, rate label, time-to-full label);
  - two upgrade buttons, each a `LocalizeStringEvent` caption (`TradeUITable/ui.upgrade`) + a price label;
  - a "Fully upgraded" label.

  Wire with `manage_prefabs modify_contents component_properties`.
- **`TradeOfferPopup.prefab`:** also from a copy of `ConfirmPopup.prefab`. Content:
  - title (`ui.offer.title`);
  - two headed columns (`ui.you_give`, `ui.you_get`) of 3 nested `ResourceAmount`s;
  - a blockers label and a warnings label;
  - Accept/Cancel keep their `SharedTable/ui.confirm`, `ui.cancel` captions.
- **`ResourceTile.prefab`:** root `Image` (raycast target) + `Button` + `ResourceTileView`; children: icon, name, level, a bar (background + Filled-Horizontal fill) with the "412 / 760" label over it.
- **`ResourceAmount.prefab`**, **`OfferRow.prefab`:** built by one `execute_code` script each (C# 6, `UnityEngine.Object` spelled out, `PrefabUtility.SaveAsPrefabAsset`), then wired. `OfferRow` has a raycast-target background Button and 3+3 nested `ResourceAmount`s.
- **Register both popups** in `PopupRegistry.asset` (`SerializedObject` append, apply, save).
- **`TradeScreen.prefab` rebuild** (`open_prefab_stage` → edit → `save_prefab_stage`). Keep `Background`, `SafeArea` (`SafeAreaFitter`, padding.bottom 160), and `OfferListPanel`/`Viewport`/`Content`/`NoOffersLabel` (their fileIDs keep the scene's overrides valid). Then:
  - root: remove `TradeView`, add and wire `TradeScreenView`; remove `TitleLabel`;
  - `WarehousePanel` → renamed `ResourceGrid`: delete the 6 old tiles; add 6 `ResourceTile`s (`_type` each) in a `GridLayoutGroup`, 3 columns × 2 rows, with the columns as pairs (Stone/Wood, Metal/Minerals, Leather/Clay); top-anchored, fixed height;
  - `RefreshRow` → becomes `OffersHeader`, the top strip of the offers area: the countdown label + `RefreshNowButton` (caption `ui.refresh_now` + cost);
  - `OfferListPanel` stretches from below the header to the safe-area floor;
  - delete `ConfirmPanelCanvas`;
  - add `ReceiptCanvas`.

```
TradeScreen (Canvas, TradeScreenView)               static chrome
├── Background                                       full-bleed
└── SafeArea (SafeAreaFitter, padding.bottom 160)
    ├── ResourceGrid (Canvas + Raycaster)            top-anchored, fixed height — 6 tiles, numbers and bars move
    │     [Stone ] [Metal   ] [Leather]
    │     [Wood  ] [Minerals] [Clay   ]              columns = upgrade pairs
    ├── OffersHeader (Canvas + Raycaster)            fixed height, directly below the grid:
    │     "Refresh in 12:41"            [Refresh now · 3]     countdown rewrites once a second
    ├── OfferListPanel (Canvas + Raycaster, ScrollRect)       stretches from the header to the safe-area floor
    │     └── Viewport / Content (VerticalLayoutGroup + ContentSizeFitter) / NoOffersLabel
    └── ReceiptCanvas (Canvas, overrideSorting 10, Raycaster, no raycast targets)
                                                     over the top of the list, under the grid
```

**Why 3×2, not 2×3.**
- At 1080 reference width, three columns give ~340-unit tiles, enough for "221,680 / 221,680" on one line.
- Two rows cost ~2 × 210 units of height instead of three rows' ~3 × 180, which leaves the offer list ~120 units more. That is one more visible row at the 3:4 aspect.
- Making the columns the upgrade pairs shows each tile's paired resource directly beneath or above it.

The layout is checked at `KR 3:4`, `KR 9:16`, `KR 9:20` (§9.4). The grid and the header are fixed heights, top-anchored, and all spare height goes to the list, the same approach as Laws §6.12.

**Five canvases, by what changes:** the root (static); `ResourceGrid` (bars dirty vertices whenever a unit lands); `OffersHeader` (the countdown, once a second); `OfferListPanel` (re-lays out while scrolling); `ReceiptCanvas` (animates). Every nested canvas gets its own `GraphicRaycaster`.

**GUIDs and references that must survive:**
- `TradeConfig.cs` `73a9a621…` and `TradeConfig.asset` `73e2f9f5…` (both verified in step 2);
- `TradeScreen.prefab` `896cee41…` with root fileID `88228022552384697`, referenced from `Main.unity` by the nav bar's Trade tab (`NavTabButton._screen` → `479876981`) and by `BottomNavBarView._screens[1].Root`. The root is never replaced; the cutover check confirms the tab still switches.

---

## 7. Localization

Plain `{0}` formats, `en` + `uk`, edited via `LocalizationEditorSettings.GetStringTableCollection("TradeUITable")` in `execute_code`. *Code* means on `TradeUIText.AllKeys` and checked by `TradeTextTests`; *chrome* means a `LocalizeStringEvent`, unchecked.

**Kept**

| Key | Kind | en (uk unchanged) |
|---|---|---|
| `ui.no_offers` | chrome | The caravans are still on the road |
| `ui.you_give`, `ui.you_get` | chrome — offer popup columns | You give / You get |
| `ui.refresh_now` | chrome — offers header button | Refresh now |
| `ui.upgrade` | chrome — warehouse popup button captions | Upgrade |
| `ui.blocker_insufficient` | code | You need {0} more {1} |
| `ui.warehouse_level` | code — tile and popup; {0} = stored level + 1 | Level {0} |
| `ui.upgrade_cost_crystals` | code | {0} crystals |
| `ui.upgrade_cost_paired` | code | {0} {1} |
| `ui.upgrade_maxed` | code | Fully upgraded |
| `ui.refresh_in` | code — {0} = `TimeFormat.MinutesSeconds` | Refresh in {0} |

**Added**

| Key | Kind | en | uk |
|---|---|---|---|
| `ui.offer.title` | chrome | Trade offer | Торгова пропозиція |
| `ui.offer.overflow` | code — {0} excess, {1} resource | {0} {1} will not fit and will be lost | {0} од. «{1}» не вміститься на складі й пропаде |
| `ui.warehouse.title` | code — {0} resource | {0} warehouse | Склад «{0}» |
| `ui.warehouse.rate` | code — {0} units | +{0} per hour | +{0} за годину |
| `ui.warehouse.full_in` | code — {0} = `TimeFormat.HoursMinutesSeconds` | Full in {0} | Заповниться через {0} |
| `ui.warehouse.full` | code | Full | Заповнено |
| `ui.refresh.title` | code | Refresh the offers? | Оновити пропозиції? |
| `ui.refresh.body` | code — {0} cost, {1} time | Spend {0} crystals to replace every offer now? Otherwise new offers arrive in {1}. | Витратити {0} кристалів, щоб замінити всі пропозиції зараз? Інакше нові з'являться через {1}. |
| `ui.not_enough_crystals` | code (a Trade copy, by decision) | Not enough crystals. | Недостатньо кристалів. |
| `ui.upgrade.title` | code | Upgrade the warehouse? | Покращити склад? |
| `ui.upgrade.body_crystals` | code — {0} cost, {1} resource, {2} level, {3} capacity | Spend {0} crystals to raise the {1} warehouse to level {2}? It will hold {3}. | Витратити {0} кристалів, щоб підняти склад «{1}» до рівня {2}? Він вміщатиме {3}. |
| `ui.upgrade.body_paired` | code — {0} cost, {1} paired, {2} resource, {3} level, {4} capacity | Spend {0} {1} to raise the {2} warehouse to level {3}? It will hold {4}. | Витратити {0} од. «{1}», щоб підняти склад «{2}» до рівня {3}? Він вміщатиме {4}. |
| `ui.upgrade.not_enough_paired` | code — {0} resource, {1} held | Not enough {0}: you have {1}. | Недостатньо «{0}»: у вас {1}. |
| `ui.receipt.received` | code | +{0} {1} | +{0} {1} |
| `ui.receipt.forfeited` | code | {0} {1} lost — the warehouse is full | {0} од. «{1}» втрачено — склад повний |

**Removed (9):**
- `ui.warning_overflow` — its meaning changes (A1);
- `ui.blocker_unavailable` — a vanished offer closes its popup;
- `ui.warehouse_amount` — a number format, drawn by the view;
- `ui.confirm`, `ui.cancel` — the popups use `SharedTable`'s;
- `ui.next_refresh` — never used;
- `ui.trade_title` — the title is dropped;
- `ui.offers` — the header holds the countdown and button instead;
- `ui.warehouses` — there is no all-warehouses popup.

Resource names come from `SharedTable` via `TradeResourceRegistry.NameKeyFor`. The Presenter re-renders on `LocaleChanged`. Large numbers use `N0`; whether the separator should follow the selected locale is a known gap.

---

## 8. Feel

**One haptic per action, never stacked.**

| Action | Sound | Animation | Haptic |
|---|---|---|---|
| Tap a resource tile | popup open (`PopupSystem`) | tile press scale; popup scales in | — |
| Tap an offer row | popup open | press tint; popup scales in | — |
| Accept confirmed | `sfx_trade_offer_accept` | row collapses out; tiles tween; numbers tick; receipt slides in, holds ~2.5 s, fades | **1** |
| Accept refused at confirm (rare race) | `sfx_trade_offer_refused` | — | — |
| Cancel / tap outside | popup close | popup shrinks out | — |
| Instant refresh confirmed | `sfx_trade_offers_refreshed` (one sound, A2) | rows stagger in (fade + scale, ~40 ms apart) | **1** |
| Auto refresh, screen visible | `sfx_trade_offers_refreshed` | rows stagger in | — (passive) |
| Auto refresh, screen hidden | silent | list updated | — |
| Upgrade confirmed (either path) | `sfx_trade_warehouse_upgrade` (one sound) | popup level + capacity punch; the tile's level punches | **1** |
| Regen | — | the tile bar tweens when a unit lands; the popup's next-unit bar fills | — |
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
| `…CrystalCost_IsStrictlyIncreasing / IsAlwaysAtLeastOne / StaysPositiveAtAnAbsurdLevel` | same names | over `0..TopLevel` |
| `TradeOfferGeneratorTests.LargestRemainder_*`, `Stochastic_*` (3) | `TradeOfferAllocatorTests` | |
| `TradeOfferGeneratorTests` batch / ratio / shape / profitability / ids / immutability | `TradeOfferGeneratorTests` (same names) | random capacities; `EveryAmount_IsAWholeNumber` dropped (`int`) |
| `TradeDomainTests.GetPairedResource_MatchesTheGddPairing`, `Pairing_IsSymmetric` | `TradeResourcePairTests` | |
| `TradeDomainTests` (construction, refresh, regen, instant refresh, evaluate, overflow, accept, upgrades, save/load) | `TradeManagerTests`, same scenarios | renames as in revision 2; rate-property tests become behaviour tests (an empty warehouse fills in exactly 24 h); `UpgradesStopAtTheSupportedCeiling` → `UpgradesStopAtMaxWarehouseLevel` |
| `TradePresenterTests.Constructor_RejectsNullDependencies` | same | |
| `GetOfferDisplays_ReturnsOneRowPerOffer`, `OfferLines_CarryLocalizedNamesFromTheSharedRegistry` | `GetOffers_ReturnsOneRowPerOffer`, same | |
| `WarehouseDisplay_FillFractionIsClampedToOne` | `Tile_FillIsClampedToOne` | |
| `WarehouseDisplay_NamesThePairedResource`, `…AtTheCeiling_ReportsMaxedRatherThanAPrice` | `WarehousePopup_PairedPriceNamesThePairedResource`, `WarehousePopup_AtTheCap_ShowsFullyUpgraded` | |
| `AnUnaffordableOffer_StillProducesAConfirmationPanel`, `ABlockerMessage_NamesTheResourceAtFault`, `AnOverflowingOfferIsStillAcceptable_AndWarnsInsteadOfBlocking` | `OfferPopup_WhenShort_DisablesAcceptAndNamesTheResource`, `…_NamesTheShortfall`, `OfferPopup_WhenAWarehouseOverflows_ListsEveryLossAndKeepsAcceptEnabled` | asserts the excess (A1) |
| `SetScreenVisible_ReRendersOnShowOnly`, `ALocaleChange_TriggersAReRender`, `RefreshCountdown_IsResolvedThroughTheKeysType` | same | |
| `AManagerStateChange_TriggersAReRender`, `Dispose_UnsubscribesFromAllThreeSources` | `OffersChanged_TriggersAReRender`, `Dispose_StopsListening` | |
| `TradeTextTests` | `TradeTextTests` against `Trade.UI.TradeUIText` | |
| Ledger trade tests (`AddTradeResource_*`, `SpendTradeResource_*`, `HasTradeResource_*`, `Accrue…`, `TakeSnapshot_*`, the save clamps) | same names, `int` | `SetWarehouseCapacity_ChangesCapacityAndRegenRate` → `…AndTheFillTime`; `RoundTrip_PreservesWarehouseCapacityAmountAndRegenRate` → `…AmountAndRegenProgress`; `Load_NonFiniteValues_FallBackToDefaults` goes through the converter |

### 9.2 Dropped, and why

| Old | Why |
|---|---|
| `TradePresenterTests.*Cost_IsTheCostCharged` (×3) | Prices live on the Manager; "quoted == charged" moves to `TradeManagerTests`, across every level. |
| `AcceptingAnOffer_PlaysASoundAndAHaptic`, `ARefusedOffer_MakesANoiseButDoesNotBuzz`, `UpgradingAWarehouse_RaisesOnWarehouseUpgraded`, `AFailedUpgrade_RaisesNothingAndIsSilent` | These actions run only after a popup confirmation, which EditMode cannot answer (as with Laws' buy-up). Verified in Play mode. |
| `ACrossModuleResourceChange_TriggersAReRender` | The Presenter no longer listens to the bus; replaced by `Tile_ReflectsAnExternalLedgerChangeWithoutAnyEvent`. |
| `OfferRow_MarksOverflowWithoutMarkingItUnacceptable` | Rows carry no hints. |
| `TryGetConfirmation_ForAnUnknownId_ReturnsFalse` | Replaced by `OfferPopup_ForAMissingOffer_CannotBeAccepted`. |
| `TradeOfferGeneratorTests.EveryAmount_IsAWholeNumber` | Guaranteed by `int`. |
| Ledger `TradeResourceMutations_NonFinite_AreRefused` | An `int` cannot be non-finite; file-level corruption is covered by the converter test. |

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
  - `EveryAmount_FitsWithinItsOwnWarehouse_AtTheMaximumShare`
  - `OffersGrowWithCapacity`
  - `TheRatioHolds_AtUnevenCapacities`
- **`TradeConfigContentTests`** (the real asset):
  - `CapacityAtTheCap_FitsUnderTheLedgerCeiling`
  - `CurvesIncreaseStrictlyUpToTheCap`
  - `TheLargestPossibleOffer_FitsAnInt`
- **`TradeManagerTests` (added):**
  - `CrystalUpgrade_QuotedEqualsCharged_AtEveryLevel`
  - `PairedUpgrade_QuotedEqualsCharged_AtEveryLevel`
  - `PairedUpgradeCost_RoundsUp`
  - `CanAfford…_MatchesWhetherTheChargeSucceeds` (×3)
  - `Accept_IntoANearlyFullWarehouse_ReportsReceivedAndForfeited` (87/100, offer 30 → +13, 17 lost)
  - `Accept_ForfeitedEqualsTheWarnedExcess`
  - `Refresh_SizesOffersFromCurrentCapacities`
  - `AnUpgrade_DoesNotResizeOffersAlreadyListed`
  - `LoadFromDto_ClampsALevelAboveTheCap`
  - `LoadFromDto_GeneratesFromRestoredCapacities`
  - `OffersChanged_FiresOncePerAccept`
  - `RegenReadouts_AreTheLedgersAtTheClocksNow`
  - `UnitsPerHour_IsCapacityOver24RoundedDown`
- **`TradePresenterTests` (added):**
  - `AutoRefresh_OnAVisibleScreen_PlaysTheSoundWithoutAHaptic`
  - `AutoRefresh_OnAHiddenScreen_IsSilent`
  - `OffersArrived_OnRefreshButNotOnAccept`
  - `OpeningAnOffer/AWarehouse/TheRefresh_SpendsNothing` (each expects the "No popup registry" exception, as Laws' tests do)
  - `WarehousePopupData_UnchangedState_ComparesEqual`
  - `OfferPopupData_UnchangedState_ComparesEqual`
  - `WarehousePopup_ShowsLevelPlusOne`
  - `WarehousePopup_ShowsRateAndTimeUntilFull`
  - `WarehousePopup_WhenFull_ShowsFullAndHidesTheNextUnitBar`
  - `Tile_CarriesNoStrings` (shape test, so steady-state polling stays allocation-free)
  - `RefreshPrice_IsTheManagersPrice`
- **Ledger (added):**
  - `Regen_CreditsWholeUnitsOnly`
  - `Regen_ManySmallTicksEqualOneLongAbsence`
  - `Regen_EmptyWarehouseFillsInExactly24Hours_AtEveryCapacity`
  - `Regen_FullWarehouseBanksNothing`
  - `Regen_PartialProgressSurvivesSaveAndLoad`
  - `Regen_AfterAnUpgrade_KeepsTheProgressMade`
  - `NextUnitProgress_RisesFromZeroToOneAcrossOneUnit`
  - `TimeUntilFull_IsExactAndZeroWhenFull`
  - `SetWarehouseCapacity_AboveTheCeiling_IsClampedAndLogged`
  - `AddTradeResource_FillingToCapacity_LandsExactlyOnCapacity`
- **Save (added):** `Load_ASchema7FileWithDecimalAmounts_RoundsToWholeNumbers` (a verbatim old JSON), `RoundTrip_WritesIntegers`.
- **`Tests/EditMode/Shared/Text/TimeFormatTests`** (new folder mirroring `_Game/Shared/`): `HoursMinutesSeconds_FormatsAndClampsNegative`, plus the existing `MinutesSeconds` behaviour.

### 9.4 Verified in Play mode (always from `Bootstrap`)

Back up the save first; `Application.runInBackground = true;` in the first `execute_code`; clicks through real `EventSystem` raycasts.

1. The existing (schema 7) save loads with no `.corrupt` file: amounts rounded whole, offers identical, the deadline unchanged, levels and capacities correct, the Trade tab live.
2. All six tiles show icon, name, "Level N", `stock / capacity` and the bar; numbers change one unit at a time. A Ledger add via `execute_code` shows within a frame. `TradeScreenView.Update` allocates nothing in steady state.
3. A tap anywhere on a tile opens *that* resource's popup. Its level, stock, capacity, rate, time-to-full and both prices are right, and the next-unit bar moves.
4. Crystal upgrade from the popup: the confirmation shows the price; Confirm charges exactly that, closes only the confirmation, and the popup and tile redraw at the new level. When short: the notice shows, Confirm is inactive, nothing is charged. The same for the paired path, charged from the paired stock.
5. At the cap (stored 19, shown as level 20, via an edited save): "Fully upgraded", both buttons inactive.
6. Outside taps close the confirmation, then the popup; nothing is charged on open or dismiss.
7. Every offer tap opens the confirmation. A short give side shows a blocker per resource with Accept disabled; nothing is spent.
8. A full receive warehouse: a warning per losing resource with the exact amount; Accept enabled; accepting shows the receipt (`received + lost = offered`), and the tile reads exactly full.
9. A normal accept: the row collapses, the receipt shows, the list re-renders once.
10. Instant refresh from the offers header: live price and countdown; Confirm charges exactly the price, replaces the list and restarts the timer; the short-funds notice works; an auto refresh while it is open closes it.
11. Auto refresh on a hidden tab: silent, and the list is updated on return.
12. A long absence (move `lastUpdatedUtc` back in the save): the stock is exactly what the formula gives.
13. Both locales.
14. Layout at `KR 3:4`, `KR 9:16`, `KR 9:20`:
    - the six tiles are fully visible;
    - the header sits directly below them;
    - the list fills to the nav bar and scrolls;
    - the receipt never blocks a tap;
    - every nested canvas's buttons respond to a real raycast.
15. Restore the save byte for byte.

**Not observable yet:** sound ids and haptic counts on the confirmed paths (stub services). Checked by code reading; listed as a known gap.

---

## 10. Docs

**`GDD.md`:** **done in revision 3.** §7 rewritten (tiles and grid, warehouse popup with regen read-outs and no collection, whole numbers, the cap of 20, 1-based levels, every purchase confirmed, band multipliers, offer sizing, the confirmation's blocker/warning behaviour, the receipt), and the §5 glossary row now says whole units. Re-check §7 against the build in step 10.

**New `docs/modules/Trade.md`**, the `Laws.md` outline, present tense only:
1. The mechanic.
2. Structure.
3. Classes + APIs.
4. The offer model: sizing and bounds; integers end to end.
5. Data flows.
6. Rules that must hold:
   - only the Manager touches Ledger/clock;
   - overflow warns, never blocks;
   - quoted == charged;
   - the deadline timer;
   - levels authoritative and capped;
   - offers persisted, never rerolled, never resized;
   - 30/45/25 is a long-run average;
   - polled, not pushed;
   - accept by id;
   - five canvases;
   - tiles matched by type, columns = pairs;
   - one haptic per action;
   - localization;
   - the warehouse popup and why it has no collect button;
   - the receipt and why it is not a popup.
7. How to extend.
8. Tests + Play-mode verification.
9. Known gaps.

**`ARCHITECTURE.md`:**
- §3 tree: the Trade comment becomes the Laws-shape lines; `Shared/Text` tests mirror.
- §4.3:
  - the integer rule replaces "Non-finite trade amounts are refused. Trade resources stay decimal…";
  - `MaxWarehouseCapacity`;
  - the regen read-outs;
  - "`TradeConfig`'s warehouse curves" → "Trade's `WarehouseCurve`".
- §4.5:
  - method names;
  - "warehouse bars read the amount each frame" → "tiles poll and redraw when a unit lands";
  - regen ticks in whole units and still publishes no event.
- §5: schema 8.

**`docs/modules/Popups.md`:**
- §1: real `Show` + `WaitForChoice` callers (A9);
- §3: the Trade popups as module popups;
- §8: remove the "Trade's confirm panel" gap.

**`CLAUDE.md`, `Laws.md`:** no change.

---

## 11. Ordered implementation steps

**Compile** = console read via MCP, no new errors/warnings (known noise: test-expected warnings, the MCP WebSocket notice, the TMP fallback-font warning). **Tests** = the EditMode suite run via MCP, all passing. Multi-line edits by Python scripts in the scratchpad (`python -I`); moves through the AssetDatabase.

**Step 0 — Baseline.** ✔ Save backed up (sha256 `e0ff6a90…`); 392 pass.

**Step 1 — Quarantine the legacy code.** ✔
- Old scripts are in `Scripts/Legacy/`, old tests in `Tests/EditMode/Modules/TradeLegacy/`, all GUIDs kept, namespaces `KingdomRuler.Modules.TradeLegacy[...]`.
- Core uses aliases.
- 392 pass; the old screen accepted and upgraded in Play mode; the save was restored byte for byte.

**Step 2 — Data.** ✔
- `TradeConfig.cs` is in `Data/` (GUID kept).
- `OfferCapacityShare` 0.20 and `MaxWarehouseLevel` 20 are added and set on the asset; the old constant is `LegacyMaxWarehouseLevel`.
- The asmdef references `UniTask`.
- 392 pass.

**Step 3 — Pure model + tests.** ✔
- The 12 model classes are in `Model/`; `KingdomLedger.MaxWarehouseCapacity` added.
- `TradeOffer` validates its own terms (`TryCreate` for saves).
- 77 new tests in 8 classes.
- 469 pass.
- `FakeClock.cs` moves to step 5, its first user.

**Step 4 — UI strings.** ✔
- 15 new `TradeUITable` entries added in en + uk via the Localization editor API (20 → 35 keys).
- Each value was reloaded from the saved assets and matched exactly; the old entries are untouched.
- 469 pass.

**Step 5 — Ledger to integers + regen read-outs + the new `TradeManager`** ✔
- Ledger, event, snapshot and DTOs are integer; schema 8.
- 121 legacy tests deleted; 53 `TradeManagerTests` + 20 regen tests + 4 Ledger/save tests added; 424 pass.
- Play mode: the real schema-7 save (decimal stock, Metal at level 16) loaded with no `.corrupt` and regenerated whole units as the formula predicts; the old screen accepted an offer; the save was restored byte for byte.
- `PairedUpgradeShare` exists only as the exact `(capacity × 4 + 4) / 5`, not as a separate constant.

As planned:
- Ledger, events, snapshot and DTOs per §2.4/§5; `schemaVersion` 8; Ledger and save tests.
- New `TradeManager` + `TradeManagerTests` + `FakeClock.cs` (not registered yet).
- Legacy runtime: mechanical `int` casts at its ~6 Ledger call sites.
- Legacy tests deleted here (superseded in this step).
- *Checkpoint:* compile; all pass; Play mode: the existing save loads (no `.corrupt`), the old screen still accepts an offer.

**Step 6 — UI code.** ✔
- `TimeFormat.HoursMinutesSeconds`; `TradeUIText` (20 code keys); the view-data structs, `TradePresenter`, and every view and both popups.
- `OfferListView` (`UI/OfferList/`) added: it owns the keyed row pool and the arrival/removal animation, keeping `TradeScreenView` to wiring and polling.
- `HasOffers`, `HasOverflowWarning` and `OfferRefreshTimer.IsRunning` removed (nothing used them).
- 30 presenter + 1 text + 11 `TimeFormat` tests; the build is clean in `Editor.log`.

As planned:
- `TimeFormat.HoursMinutesSeconds` + `TimeFormatTests`.
- `TradeUIText`, view-data structs, `TradePresenter`, all views and both popups.
- `TradePresenterTests`, `TradeTextTests`.
- *Checkpoint:* compile; all pass.

**Step 7 — Prefabs.** ✔
- `ResourceAmount`, `ResourceTile`, `OfferRow` built by an editor script.
- `TradeOfferPopup` and `WarehousePopup` copied from `ConfirmPopup.prefab` (new GUIDs `ee5fa1f4…`, `d381d0a7…`; the template is unchanged), each with its component swapped and the base animation fields kept.
- Both popups appended to `PopupRegistry.asset`.
- Audit: 42 references, none null; no missing scripts; 12 nested `ResourceAmount` instances; every chrome label on its intended key.
- 466 pass.

As planned:
- `ResourceAmount`, `ResourceTile`, `OfferRow`, `TradeOfferPopup`, `WarehousePopup` (§6.2).
- Register both popups in `PopupRegistry.asset`.
- *Checkpoint:* compile; all pass; no missing references in any new prefab.

**Step 8 — Cutover.** ✔
- `GameBootstrapper` registers `TradeManager` (`[Inject]` constructor, no factory) and `TradePresenter`; `GameStateCoordinator` is back to its committed form.
- `TradeScreen.prefab` rebuilt in place; GUID `896cee41…` and root fileID `88228022552384697` are unchanged. `TradeView`, `TitleLabel`, `ConfirmPanelCanvas` and the six old tiles were removed; `ResourceGrid` (3×2, filled column-first so columns are pairs), `OffersHeader`, the stretched `OfferListPanel` with `OfferListView`, and `ReceiptCanvas` were added.
- `Main.unity`: 66 overrides on the deleted tiles removed (deletions only). The Trade tab's `NavTabButton._screen` still points at the screen. The `_screens` reference noted in §6 is a stale override on the nav bar instance for a field that no longer exists; it was left alone, as it is not Trade's.
- Play mode (from Bootstrap, the real schema-7 save, every tap a real `EventSystem` raycast), all passing:
  - tab switch; tiles, levels (1-based), `N0` numbers;
  - the warehouse popup with correct rate, time-to-full and prices;
  - crystal upgrade charged exactly 25 and the popup stayed open; paired upgrade charged exactly 272 Wood; the short-funds notice with an inactive Confirm charged nothing; outside taps closed the confirmation, then the popup;
  - a blocked offer showed its shortfall with Accept inactive; an overflow offer listed every loss and accepted with the warehouses ending exactly full; the receipt showed the gain and loss lines and never blocked a tap;
  - instant refresh charged exactly 3; short funds showed the notice; a free refresh landing while open closed the confirmation;
  - the level cap shows "Fully upgraded" with inactive buttons and no next-unit bar;
  - runtime switch to English;
  - layout at 1080×1440, 1920 and 2400.
- `StubAudioService`/`StubHapticService` log each call, so the feel hooks on the confirmed paths are verified too: one sound per action, one haptic per purchase, none on a passive refresh.
- Found and fixed in Play mode: the fill images had no sprite, so `Filled` drew full bars. They now use `Images/BarFill.png` (a 4×4 white sprite, also replacing the soft `UISprite`), and the receipt background is opaque.
- 466 pass; the save was restored byte for byte; `runInBackground` reset; no `ProjectSettings` diff.
- **Open question for the user:** with the cap at 20 stored upgrades and levels shown from 1, the top displays as "Level 21".

**Step 9 — Delete the legacy code.** ✔
- Deleted through the AssetDatabase: `Scripts/Legacy/`, `TradeOfferRow.prefab`, `WarehouseTile.prefab`, and the empty `Scripts/Domain`, `Scripts/Presenters`, `Scripts/Views` and `ScriptableObjects/Data` folders. Nothing referenced them (checked by GUID). `Prefabs/.gitkeep` and `Config/.gitkeep` stay, as in Laws.
- `TradeConfig`: `OfferBaseAmount`, `LegacyMaxWarehouseLevel`, `WarehouseCapacityAt`, `WarehouseUpgradeCrystalCost` and `Grow` removed; the asset re-serialized (only `OfferBaseAmount` dropped).
- `TradeUITable`: the 9 entries removed from the shared data and both locales (26 keys left). The `ui.confirm`/`ui.cancel` references in the popup prefabs are to `SharedTable`, not this table.
- **The user's decision on the cap:** `MaxWarehouseLevel` is the level as shown, so 20 means 19 upgrades. `WarehouseCurve.TopLevel` is the one place that converts it; the curve clamp, `TradeManager.CanUpgradeFurther`/`CapacityAfterUpgrade` and the load clamp use it. GDD §7 updated.
- 467 pass (one new: `TopLevel_IsTheCapAsThePlayerSeesIt_LessOne`). Play-mode smoke: tiles and warehouse popup correct; a real crystal upgrade from shown 19 charged exactly 7,389 and ended at "Рівень 20", *Fully upgraded*, both buttons inactive; one sound and one haptic. Save restored byte for byte; `runInBackground` reset; no `ProjectSettings` diff.

**Step 10 — Docs.** ✔
- `docs/modules/Trade.md` rewritten in the `Laws.md` outline (§10), present tense; facts checked against the code and the built prefab (canvases, grid, receipt).
- `ARCHITECTURE.md`: §3 tree (Trade in Laws' shape; `Tests/EditMode/Shared/`), the localization table's `TradeUITable` row, §4.3 (the integer rule with whole-unit regen, `MaxWarehouseCapacity`, the read-outs; `WarehouseCurve`), §4.5 (whole-unit accrual, `OfferRefreshTimer.Advance`, tiles poll). §5 names no schema number, so it needed no change.
- `Popups.md`: §1 names the real `Show` + `WaitForChoice` callers, §3 the module popups, §8 drops the confirm-panel gap.
- GDD §7 re-checked against the build: it matches. Its three `[ASSUMED — CONFIRM]` markers (the pairing, the 45 % band, the 20 % share) are left for the user.
- Stale-name grep over docs, `CLAUDE.md` and `Assets/`: none. 467 pass.

---

## 12. Risks and assumptions

### Risks

| Risk | Mitigation |
|---|---|
| Step 5 changes the Ledger and the save together. | A verbatim schema-7 test file; a Play-mode load of the real save; the save backed up first. |
| Integer regen gets a boundary wrong. | Exact `long` arithmetic; tests for small ticks == one absence, an exact 24 h fill at every capacity, progress across save/load and upgrades. |
| `TradeScreen` rebuild loses the root or a referenced child. | Edit inside the prefab stage; keep the root; verify the nav tab after the cutover. |
| The six tiles + header leave too little list at 3:4. | Fixed-height tiles sized at the 3:4 check; the list scrolls; tile height is the one knob. |
| `Main` scene conflict. | One agent on the scene; the only change is removing unused overrides. |
| Sizing feels off in play. | One knob, `OfferCapacityShare`; the bounds test keeps any value ≤ 0.40 safe. |
| Popup data rebuilt at 4 Hz while open. | Only while open; the next-unit progress is quantized so redraws track visible change. |
| A fast tap answers a popup still scaling in (Popups known gap). | Every charge re-validates. |

### Assumptions (say if any is wrong)

1. `MaxWarehouseLevel` is a `TradeConfig` setting (20), not a code constant.
2. The Ledger's `MaxWarehouseCapacity = 10,000,000` is a backstop only.
3. `RegenRatePerSecond` is removed; the three regen read-outs replace what a reader would have used it for.
4. An old decimal stock rounds half away from zero on load (≤ ½ unit, once).
5. The legacy Trade tests are deleted in step 5.
6. **Revision 3:**
   - the grid is 3 columns × 2 rows with the pairs as columns;
   - the screen title is dropped;
   - the "+N per hour" rate is rounded down to a whole number;
   - the next-unit bar hides when a unit takes under a second.
