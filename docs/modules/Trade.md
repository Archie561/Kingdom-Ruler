# Trade module — architecture

> Implements **GDD.md §7 — Trading**. The GDD says *what* the mechanic does; this document says
> *how it is built* and which decisions must survive future changes. Project-wide rules live in
> `ARCHITECTURE.md`.

---

## 1. The mechanic in one paragraph

Six resources sit in warehouses that refill one whole unit at a time, from empty to full in 24 hours
whatever their size. Six tiles at the top of the screen show every warehouse at all times; tapping
one opens that resource's warehouse popup, where it can be upgraded for crystals or for 80% of its
own capacity, paid from the paired resource's stock. Below the tiles, ten offers each trade
2–3 resources for 2–3 others, sized to the warehouses they touch, and are replaced every 20 minutes
or at once for crystals. Every offer opens a confirmation: a shortfall blocks it, a full warehouse
only warns, and the receipt afterwards shows what was received and what was lost.

---

## 2. Structure

```
Modules/Trade/
  Scripts/                              assembly KingdomRuler.Modules.Trade
    Data/
      TradeConfig.cs                    tunable parameters — no content list
    Model/                              the mechanic — plain C#
      TradeManager.cs                   entry point: lifecycle, clock, every Ledger transaction
      TradeOffer.cs                     one offer's immutable terms
      TradeAmount.cs                    (resource, whole units)
      TradeProfitability.cs             Profitable / Neutral / Unprofitable
      TradeOfferGenerator.cs            a batch: the band mix and each offer's resources
      TradeOfferSizing.cs               how big an offer is, and its split across resources
      TradeOfferAllocator.cs            whole-number splits: stochastic and largest-remainder
      OfferRefreshTimer.cs              the 20-minute refresh, as a deadline
      WarehouseCurve.cs                 capacity and crystal price per level, and the cap
      TradeResourcePair.cs              which resource pays for which upgrade
      TradeIssue.cs, TradeIssueKind.cs  one thing wrong with an offer
      TradeOfferEvaluation.cs           every issue with an offer, found without changing anything
      TradeAcceptResult.cs              what an accept received and forfeited
    UI/
      TradePresenter.cs                 model ↔ view; the confirmation flows; feel hooks
      TradeScreenView.cs                the screen root and its simple widgets
      TradeUIText.cs                    String Table keys for text built in code
      ResourceTile/                     ResourceTileView, ResourceTileViewData — one tile
      ResourceAmount/                   ResourceAmountView, ResourceAmountViewData — one "12 Stone" line
      OfferList/                        OfferListView — the scrolling list
      OfferRow/                         OfferRowView, OfferRowViewData — one offer
      TradeOfferPopup/                  TradeOfferPopup, …Data, …Choice — the offer confirmation
      WarehousePopup/                   WarehousePopup, …Data, WarehousePayment — one warehouse
      Receipt/                          TradeReceiptView, …ViewData, …LineViewData — after an accept
  ScriptableObjects/
    Config/TradeConfig.asset
  Prefabs/                              TradeScreen, ResourceTile, ResourceAmount, OfferRow,
                                        TradeOfferPopup, WarehousePopup
  Localization/                         TradeUITable (chrome and messages built in code)
  Images/                               BarFill.png — the sprite every fill bar uses (§6.11)
```

**There are no content assets.** Offers live 20 minutes and carry no authored text; they are built
from resource names and numbers, so the module has `ScriptableObjects/Config/` only. Resource names
are in `SharedTable` and the six icons in `Systems/Ledger/ScriptableObjects/TradeResources/`, because
both are Ledger-owned display data that other mechanics share (`ARCHITECTURE.md` §4.4).

**Namespaces.** `Data/` and `Model/` share `KingdomRuler.Modules.Trade`; `UI/` is
`KingdomRuler.Modules.Trade.UI`, so a model file reaching for the UI is easy to spot.

**Dependencies point one way:** `UI → Model → Data`, and everything may use `Systems/`.

```
 UI      TradeScreenView ──► TradePresenter ──► TradeManager, KingdomLedger (reads only),
         ├─ ResourceTileView ×6                  TradeResourceRegistry, PopupSystem,
         ├─ OfferListView → OfferRowView         IAudioService, IHapticService, ILocalizationService
         ├─ TradeReceiptView
         └─ TradeOfferPopup, WarehousePopup, ConfirmPopup (via PopupSystem)
                                                 │
 Model   TradeManager ──► TradeOfferGenerator ──► TradeOfferSizing, TradeOfferAllocator
            │        └──► OfferRefreshTimer, WarehouseCurve, TradeResourcePair
            └──► KingdomLedger (writes), IClock
                                                 │
 Data    TradeConfig
```

**Registration.** The composition root registers `TradeConfig`, `TradeManager` and `TradePresenter`
— nothing else. `TradeManager` builds its `TradeOfferGenerator` and `OfferRefreshTimer`.

---

## 3. Classes

### Data

| Class | Responsibility |
|---|---|
| `TradeConfig` | Offer count (10), refresh interval (1200 s), instant-refresh price (3), `OfferCapacityShare` (0.20, held to `[MinOfferCapacityShare, MaxOfferCapacityShare]` = `[0.01, 0.40]`), `MaxWarehouseLevel` (20, the level **as the player sees it**), and the curve coefficients. Numbers only — the formulas are `WarehouseCurve`'s. `OnValidate` stops a designer typing a value the code would have to override. |

### Model

| Class | Responsibility |
|---|---|
| `TradeManager` | The mechanic's entry point and **the only class in the module that touches the Ledger or the clock** (§6.1). Lifecycle (`InitializeNewGame`, `LoadFromDto`, `ToDto`), the clock subscription, and every transaction: accepting an offer, the instant refresh, both upgrades. Each price sits beside the transaction that charges it. Owns the warehouse levels. |
| `TradeOffer` | Immutable terms: id, give and receive amounts, profitability band, totals. Validates itself — no empty side, every amount ≥ 1, no resource twice or on both sides. `TryCreate` for loading a save. |
| `TradeOfferGenerator` | A batch: the 30/45/25 band mix (§6.7), two or three resources a side from one shuffled list (so the sides never overlap), the band's multiplier, then `TradeOfferSizing`. Takes a `System.Random` so tests can seed it. |
| `TradeOfferSizing` | `GiveTotal`, `ReceiveTotal` and `Split` (§4). Static and pure. |
| `TradeOfferAllocator` | Two ways to split a whole number into whole parts that sum exactly: `ApportionStochastic` (a ratio over many batches) and `ApportionLargestRemainder` (one offer's amounts). |
| `OfferRefreshTimer` | When the list is next replaced. Stores a **deadline** (§6.4). Pure — handed `now`. |
| `WarehouseCurve` | `Capacity(config, level)`, `CrystalCost(config, level)` and `TopLevel(config)`. Clamps every level to `0..TopLevel` and every result to a safe range, whatever the asset or the save holds. |
| `TradeResourcePair` | Stone↔Wood, Metal↔Minerals, Leather↔Clay. |
| `TradeIssue`, `TradeIssueKind` | One problem: `InsufficientResource` and `OfferUnavailable` block; `WouldOverflowWarehouse` does not (§6.2). Carries the resource and the units short or lost. |
| `TradeOfferEvaluation` | Every issue with one offer right now, in line order. `CanAccept` counts only blocking ones. |
| `TradeAcceptResult` | `Accepted`, the issues, and what was `Received` and `Forfeited` — received is what actually fit. |

```csharp
// TradeManager
void InitializeNewGame();   void LoadFromDto(TradeStateDto);   TradeStateDto ToDto();
void AdvanceTo(DateTime now);                                    // subscribed to IClock.Ticked
event Action OffersChanged;

IReadOnlyList<TradeOffer> Offers;   DateTime? NextRefreshDueUtc;   float SecondsUntilRefresh;
bool TryGetOffer(id, out TradeOffer);   TradeOfferEvaluation Evaluate(id);   TradeAcceptResult AcceptOffer(id);

int InstantRefreshCost;            bool CanAffordInstantRefresh;          bool TryInstantRefresh();

int  WarehouseLevel(type);         bool CanUpgradeFurther(type);          int CapacityAfterUpgrade(type);
int  CrystalUpgradeCost(type);     bool CanAffordCrystalUpgrade(type);    bool TryUpgradeWithCrystals(type);
int  PairedUpgradeCost(type);      bool CanAffordPairedUpgrade(type);     bool TryUpgradeWithPairedResource(type);

float NextUnitProgress(type), SecondsPerUnit(type), SecondsUntilFull(type);   int UnitsPerHour(type);
```

`TradeManager` has two constructors: the production one, marked `[Inject]` so VContainer picks it,
and one taking a `System.Random` for tests.

### UI

| Class | Responsibility |
|---|---|
| `TradePresenter` | Turns model state into localized view data, runs the confirmation flows, carries player actions to the manager, owns sound and haptics, gates passive sound on screen visibility. Plain C#, no Unity UI types. Listens to `OffersChanged` and `LocaleChanged` only — nothing on the bus (§6.8). |
| `TradeScreenView` | The screen root. Polls the tiles and the countdown every frame and writes only what changed; renders the offer list on `StateChanged`; forwards taps. Displays the countdown but never advances it. |
| `ResourceTileView` | One tile: icon, name, level, `stock / capacity` and a fill bar. The whole tile is one button. Declares its own `TradeResourceType` (§6.11). |
| `OfferListView` | The scrolling list. Keeps one row per offer id, so an accepted offer's row fades out alone and a new batch staggers in (§6.9). |
| `OfferRowView` | One offer: three give and three receive slots, one button over the whole row. Never marked or disabled (§6.2). |
| `ResourceAmountView` | One "12 Stone" line — icon, amount, name. Shared by the rows and the offer confirmation; `ShowAll` fills a set of slots and hides the rest. |
| `TradeOfferPopup` | `Popup<TradeOfferPopupData, TradeOfferPopupChoice>`: the terms, a blocker line per resource short (Accept inactive), a warning line per resource that will lose units (Accept active). |
| `WarehousePopup` | `Popup<WarehousePopupData, WarehousePayment>`: one warehouse — level, stock, the regen read-outs and both upgrades. Its choice is the payment pressed (§6.14). |
| `TradeReceiptView` | The strip shown after an accept (§6.15). |
| `…ViewData`, `…PopupData` | Exactly what each view draws, already localized; views never see a `TradeOffer`. `ResourceTileViewData` carries no strings, because it is read every frame. Both popup data types compare field by field, arrays element by element, because popups re-read them at 4 Hz. |

**When a UI element gets its own View class:** when it repeats (tile ×6, row ×10, the resource line
in every row and popup) or has behaviour of its own (the list's keyed rows, the receipt's timing).
The countdown, the refresh button and its price are serialized fields on `TradeScreenView`.

```csharp
// TradePresenter
event Action StateChanged;   event Action OffersArrived;
event Action<TradeReceiptViewData> TradeCompleted;   event Action<TradeResourceType> WarehouseUpgraded;

ResourceTileViewData GetTile(type);   float SecondsUntilRefresh;   int InstantRefreshCost;
string ResourceName(type);   string LevelText(int displayedLevel);   string RefreshCountdownText(int seconds);
OfferRowViewData[] GetOffers();   TradeOfferPopupData GetOfferPopup(id);   WarehousePopupData GetWarehouse(type);

UniTaskVoid OpenOffer(id);   UniTaskVoid RequestInstantRefresh();   UniTaskVoid OpenWarehouse(type);
void SetScreenVisible(bool);
```

---

## 4. The offer model

### Whole numbers end to end

Every trade quantity is an `int`: stock, capacity, offer amounts, prices, and the Ledger API that
moves them. One number per resource, and it is the one on screen — no floor that two mechanics must
agree on, no float landing one ulp under capacity, no NaN guard.

Regeneration still needs no fraction. A warehouse gains **one unit every `TicksPerDay / capacity`**,
so an empty one is full after exactly 24 h at any size, and `TradeResourceState.LastUpdatedUtc` is
**the moment the unit now in progress started** — the partial unit lives in the timestamp, the same
deadline idea as `OfferRefreshTimer`. The arithmetic is the shared `WholeUnitAccrual`
(`ARCHITECTURE.md` §4.5), exact at any capacity; the Ledger still clamps capacity to
`KingdomLedger.MaxWarehouseCapacity` (10,000,000) as a sanity ceiling on tuning. Many short ticks give
what one long absence gives. The rules are the Ledger's (`ARCHITECTURE.md` §4.3); Trade only
hands it a capacity.

### Sizing

```
giveTotal    = max(10, round½↑(share × min(Σ give-side capacities, Σ receive-side capacities)))
receiveTotal = max(1,  round½↑(giveTotal × multiplier))       Profitable 1.10–1.40 · Neutral 0.95–1.05
                                                              Unprofitable 0.60–0.90
each side    = split in proportion to capacity × jitter[0.75, 1.25), every resource ≥ 1, summing exactly
```

- **Capacity, not stock**, so an offer does not shrink the moment the player trades stock away.
- **The smaller side**, so every amount is bounded by its *own* warehouse: a give amount is at most
  ≈ share × 5/3 of its warehouse (≈ 33 % at 0.20), a receive amount ≈ share × 1.4 × 5/3 (≈ 47 %). No
  offer asks for more than a warehouse holds, and one overflows only a warehouse already about half
  full. `MaxOfferCapacityShare` (0.40) is the largest share for which that holds; a test pins it.
- **`MinGiveTotal` = 10** keeps the bands apart at any capacity: from 10 up, 110 % always rounds above
  the give total and 90 % below it.
- **Halves round up**, explicitly — `Math.Round` alone rounds them to even.
- **All resources are worth one unit**, so an offer's profitability is exactly receive ÷ give.

Worked numbers (share 0.20, base capacity 100): at level 1 a 2-for-2 offer gives 40 and a 3-for-3
gives 60; at level 11 (capacity 5,770), 2,308 and 3,462; at the cap, level 20 (capacity 221,680),
88,672 and 133,008. The largest possible offer — three capped warehouses, the maximum share, the best
multiplier — is ≈ 372,000 units, far inside `int`; `TradeConfigContentTests` checks the real asset.

Where the numbers live: `OfferCapacityShare` on `TradeConfig`; `MinGiveTotal` and the jitter as
constants on `TradeOfferSizing`; the multipliers in `TradeOfferGenerator`.

---

## 5. Data flow

### Startup
```
GameStateCoordinator.LoadOrInitialize()
  ├─ KingdomLedger.LoadFromDto(state.Ledger)
  ├─ save has Trade → TradeManager.LoadFromDto(dto)
  │                     1. levels → clamp 0..TopLevel → SetWarehouseCapacity(WarehouseCurve.Capacity)
  │                     2. offers + deadline restored (no offers survived → a fresh batch)
  │                     3. ledger.AccruePassiveResourceRegen(now)   ← offline regen, capacities final
  │                     4. refresh timer advanced to now             ← offline refresh, at most one
  │                     5. OffersChanged
  └─ otherwise       → TradeManager.InitializeNewGame()
                        level 0 everywhere, regen baseline at now, first batch, timer started
```

The coordinator does not accrue regen itself: inside the module's own load it cannot run against a
capacity that is about to change.

### A tick (4 Hz)
```
GameClock.Ticked(now)          (every subscriber gets the same now, in no required order)
  ├─ TradeManager.AdvanceTo(now) → deadline passed? → new batch → OffersChanged
  │     └─ Presenter: refresh sound (if visible), StateChanged → list re-renders, OffersArrived → rows stagger in
  └─ KingdomLedger.AccruePassiveResourceRegen(now)            (wired in GameBootstrapper; publishes nothing)

TradeScreenView.Update()        (every frame, independently)
  ├─ countdown label             rewritten when its whole second changes
  └─ each tile: GetTile(type)    redrawn only when stock, capacity or level changed
```

### Opening and accepting an offer
```
OfferRowView tapped → OfferListView.OfferPressed(id) → TradePresenter.OpenOffer(id)
  └─ TradeOfferPopup.Ask(() => GetOfferPopup(id), closeWhen: offer gone)   nothing mutates
       re-read at 4 Hz: blockers, warnings, CanAccept follow the stock live
       └─ Accept → TradeManager.AcceptOffer(id)
             1. accrue regen
             2. unknown id → Refused(OfferUnavailable), Ledger untouched
             3. Evaluate → any blocking issue → Refused, nothing spent
             4. spend the give side
             5. add the receive side, keeping what fit → Received, the rest → Forfeited
             6. remove the offer, OffersChanged once (the deadline does not move)
          └─ accept sound + haptic → TradeCompleted(receipt) → TradeReceiptView.Show
             OffersChanged → StateChanged → the accepted row fades out
```

### Instant refresh
```
Refresh button → TradePresenter.RequestInstantRefresh()
  └─ ConfirmPopup.Ask(live price and countdown, canConfirm: CanAffordInstantRefresh,
                      closeWhen: the deadline moved — the free refresh landed meanwhile)
       └─ Confirm → TradeManager.TryInstantRefresh()   charges now → new batch → timer restarted
            └─ haptic; the sound comes from OffersChanged, as for every refresh
```

### A warehouse popup and its upgrades
```
Tap anywhere on a tile → TradePresenter.OpenWarehouse(type)
  └─ WarehousePopup.Show(() => GetWarehouse(type))          stays open, re-reads at 4 Hz
       loop: an upgrade button → ConfirmPopup.Ask(price, level reached, capacity then, canConfirm,
                                                  closeWhen: !CanUpgradeFurther)
               └─ Confirm → TryUpgradeWithCrystals / TryUpgradeWithPairedResource(type)
                    accrue regen → re-read the price → charge → level + 1 → SetWarehouseCapacity
                    └─ upgrade sound + haptic → WarehouseUpgraded → the tile's level punches;
                       the popup redraws at the new level and punches its own
       tap outside → closes the top popup only: the confirmation first, then the warehouse popup
```

### Save / load

`GameStateCoordinator` calls `ToDto()` / `LoadFromDto()`. `TradeStateDto`
(`Systems/Save/Scripts/Dto/`) holds three fields:

| Field | Restores |
|---|---|
| `warehouseLevels` | Each warehouse's level — and through the curve, its capacity |
| `activeOffers` | The exact terms on screen (`TradeOfferDto`: id, profitability, give, receive) |
| `nextOfferRefreshDueUtc` | *When* the list is next replaced |

Stock and regen progress are the Ledger's (`TradeResourceStateDto`: `amount`, `lastUpdatedUtc`, and a
`capacity` that Trade overrides from the level). Every whole-number field reads through
`WholeNumberJsonConverter`, so a file holding `87.34` loads as 87 instead of failing as a whole.

Load is defensive, because the save is plain JSON on the device:
- a level is clamped to `0..TopLevel`;
- a resource or profitability name is parsed by **member name only** — `Enum.TryParse` alone would
  accept `"3"`; an unknown resource is skipped, an unknown profitability becomes Neutral;
- an amount below 1 is skipped, and an offer left with an empty side, or otherwise invalid, is
  dropped rather than failing the load;
- a missing or unreadable deadline starts a fresh interval; one too far away is pulled in (§6.4).

---

## 6. Rules that must hold

### 6.1 Only `TradeManager` touches the Ledger and the clock
Every Ledger write Trade makes is in one file. The generator, sizing, allocator, timer and curve are
pure: they are handed numbers and the time, and return results. A new transaction goes on the
manager. The regen read-outs (`NextUnitProgress`, `TimePerUnit`, `TimeUntilFull`) live on
`TradeResourceState` because they are regen's rules, which are Ledger-owned; the manager only
forwards them with the clock's `now`.

### 6.2 Overflow warns; it never blocks
A received resource that will not fit is a **warning** the player may accept, forfeiting the excess.
The trade is refused **only** when the give side cannot be paid. `TradeIssue.BlocksAcceptance` encodes
this, and it is false for `WouldOverflowWarehouse`. This is the easiest rule here to undo by accident,
because "the warehouse is full" reads like a reason to refuse. Three things depend on it:

- `TradeAcceptResult.Received` legitimately reports **less** than the offer stated: the invariant is
  `received == min(requested, free space)`, never `received == requested`.
- `Forfeited` is what the player knowingly gave up, and it equals the excess the confirmation warned
  of (tested). The receipt shows it on lines of its own.
- Accept stays **enabled** with warnings showing.

The row in the list carries no hint either way: affordability and overflow are the confirmation's job.

### 6.3 The price quoted is the price charged
Every price and its affordability check sit on the manager beside the `Try…` that charges it; the
presenter only reads them. A confirmation re-reads its price four times a second while open, and the
`Try…` re-reads it again at the moment it charges. Tests assert **quoted == charged** at every level,
for both upgrade paths. The paired price is `⌈capacity × 4/5⌉` of the warehouse **being upgraded**,
computed exactly in integers as `(capacity × 4 + 4) / 5`, and paid from the **paired** stock.

**The paired path keeps a pair in step.** The paired stock can never exceed the paired capacity, so
an upgrade needs the pair to be at least 80% of the upgraded warehouse's size. Capacity grows ×1.5 a
level and 1.5 × 0.8 > 1, so a warehouse can run at most one level ahead of its pair this way (tested);
crystals are the way past it. Lowering the 80% or the growth would loosen that, so retune them
together.

### 6.4 The refresh timer stores a deadline
A wall-clock deadline needs nothing running to count it down. Whenever the timer is advanced it
compares against `now`, so a 10-hour absence settles in one call — and **at most one refresh** happens,
since each refresh discards the last. The deadline moves in whole intervals, so the phase survives:
return 3.5 intervals late and the next refresh is half an interval away, not a full one.

**The deadline is never more than one interval away** (`ARCHITECTURE.md` §4.5): a clock moved back or
an edited save would otherwise freeze the list until that date. The instant refresh restarts the
interval; an accept never moves it, which is also how the presenter tells a refresh from an accept.

### 6.5 Levels are authoritative, and the cap is the level the player sees
The level is the stored fact; capacity is derived from it through `WarehouseCurve` and handed to the
Ledger, which derives regen from it. A retuned curve therefore reaches existing saves.

A stored level counts upgrades, so 0 is a fresh warehouse; the screen shows it plus one.
`TradeConfig.MaxWarehouseLevel` is written the way the player sees it — 20 means "Level 20" on screen,
19 upgrades — and **`WarehouseCurve.TopLevel` is the only place that converts between the two.** The
curve's clamp, `CanUpgradeFurther`, `CapacityAfterUpgrade` and the load clamp all go through it. The
load clamp is also a save-integrity guard: without it `100 × 1.5^9999` would reach the Ledger.

### 6.6 Offers are persisted, never rerolled, never resized
Relaunching the app restores the exact list and deadline; a reroll on launch would be a free
substitute for the crystal-priced refresh. Offers are sized from the capacities **when they are
generated**: an upgrade makes later offers bigger and leaves the listed ones alone, so terms never
change between the tap and the confirm.

### 6.7 30/45/25 is a long-run average
45 % of 10 is 4.5, so no integer split of one batch reaches it. Each batch takes the floor (3/4/2) and
awards the two spare slots by weighted chance (`ApportionStochastic`, systematic sampling), so the
expectation is exact and any one list is 3/5/2 or 3/4/3. Tested statistically across 2,000 batches,
as `ARCHITECTURE.md` §8 asks. Never `Math.Round` per band: its banker's rounding turns 2.5 into 2 and
bakes in a permanent 30/50/20. An offer's amounts use `ApportionLargestRemainder` instead —
deterministic, because a single offer is looked at on its own and nothing averages out across it.

### 6.8 Polled, not pushed
The presenter listens to nothing on the event bus. Stock changes continuously — regen, and other
mechanics through the Ledger — so the tiles and any open popup **re-read** it: `TradeScreenView.Update`
asks `GetTile` for each tile every frame and redraws only on a change, and popups re-read at 4 Hz.
`ResourceTileViewData` carries no strings, and text (names, levels, the countdown) is asked for only
when its number changes, so steady polling allocates nothing.

Regen publishes no event (`ARCHITECTURE.md` §4.5). Accepts and upgrades still publish the Ledger's
`TradeResourceChangedEvent` for other modules. Only the offer list, which changes in discrete steps,
is pushed — through `TradeManager.OffersChanged`, a plain event because its only subscriber already
holds the manager (`ARCHITECTURE.md` §4.2) — so one accept is exactly one re-render.

### 6.9 Accept by id; rows are keyed by id
The 20-minute refresh can land between tapping a row and pressing Accept. An index would then accept
a *different* trade from the one on screen; an id becomes a clean `OfferUnavailable`, and the
confirmation's `closeWhen` closes it as soon as its offer is gone.

`OfferListView` matches rows to offers by id. When offers only disappear, it is an accept: that row
fades out and the rest stay put. When new ones appear, it is a refresh: the old rows are released
and the new batch staggers in. Rows are created once and reused — at most `OfferCount` of them, so the
set itself is the pool (`ARCHITECTURE.md` §9). A released row has no id and ignores taps, so it can
never open an old offer.

### 6.10 Five canvases
A Canvas is the unit of rebuild, so anything that changes at runtime sits on its own
(`ARCHITECTURE.md` §2):

| Canvas | Why it is separate |
|---|---|
| `TradeScreen` (root) | the static rest |
| `ResourceGrid` | six bars and labels that change whenever a unit lands; `fillAmount` dirties vertices |
| `OffersHeader` | the countdown rewrites once a second |
| `OfferListPanel` | the `ScrollRect` re-lays out on every drag frame |
| `ReceiptCanvas` (`overrideSorting` 10) | animates over the list; 10 is below the nav bar's 100 |

Each nested canvas has its own `GraphicRaycaster`, or every button inside it is silently dead. Check
with real `EventSystem` raycasts, not by invoking `onClick`, which bypasses the raycaster.

### 6.11 The tiles and the layout
- **Tiles are matched by type**, never by array order. Each `ResourceTileView` declares its
  `TradeResourceType`; `TradeScreenView` logs an error at start unless every resource has exactly one.
- **Columns are the upgrade pairs.** `ResourceGrid` is a `GridLayoutGroup` filled column-first
  (`startAxis` Vertical, two fixed rows): Stone over Wood, Metal over Minerals, Leather over Clay, so a
  tile's paired resource is directly above or below it. Three columns, not two, because at 1080 units
  wide they fit "221,680 / 221,680" on one line and two rows leave the list one more visible row.
- **The whole tile is one button**, over a raycast-target background.
- **The layout runs top-down.** `ResourceGrid` and `OffersHeader` are top-anchored at fixed heights;
  `OfferListPanel` stretches from below the header to the safe-area floor, so all spare height goes
  to the list. Verified at `KR 3:4`, `KR 9:16` and `KR 9:20`. **Check layout from `Bootstrap`, never
  `Main`**: started from `Main`, nothing is injected and the screen shows prefab defaults.
- **Every fill bar has a sprite** (`Images/BarFill.png`, a 4×4 white texture). A UGUI `Image` with no
  sprite ignores its `Filled` type and draws a full bar whatever `fillAmount` says.
- **A missing icon hides its slot**: an `Image` with no sprite draws a white box.

### 6.12 One haptic per action
| Action | Sound | Haptic |
|---|---|---|
| Accept confirmed | `sfx_trade_offer_accept` | 1 |
| Accept refused at the confirm (a race: the stock or the list changed in the last instant) | `sfx_trade_offer_refused` | — |
| Instant refresh confirmed | `sfx_trade_offers_refreshed`, from `OffersChanged` | 1 |
| Automatic refresh, screen visible | `sfx_trade_offers_refreshed` | — (passive) |
| Automatic refresh, screen hidden | — | — |
| Upgrade confirmed, either payment | `sfx_trade_warehouse_upgrade` | 1 |
| Opening or closing any popup | `PopupSystem`'s | — |

The instant refresh plays its sound from the same `OffersChanged` handler as every refresh, so it
plays once. Animation: the bars tween, an upgraded tile's level punches (the bar itself *drops* — the
same stock in a bigger warehouse — so the punch goes on the number that rose), rows stagger in on a
refresh and fade out on an accept, the receipt fades in, holds 2.5 s and fades out. Every tween runs
on unscaled time and is killed in `OnDisable`/`OnDestroy`, so a tab switch never leaves a row
half-faded.

### 6.13 Localization
- **Text built in code** is a constant on `TradeUIText` and listed in its `AllKeys`, which
  `TradeTextTests` walks in every locale — **a new key goes into `AllKeys` too**, or nothing checks it.
- **Fixed chrome** (the offer popup's title and columns, "Refresh now", "Upgrade", the empty-list line)
  is a `LocalizeStringEvent` on the prefab. The popups' Accept/Cancel captions are `SharedTable`'s.
- **Resource names** come from `SharedTable` through `TradeResourceRegistry.NameKeyFor`.
- "Not enough crystals" is Trade's own entry; Laws has its own (`ui.buyup.not_enough`). Moving the
  one sentence to `SharedTable` is the ownership test's call (`ARCHITECTURE.md` §2) once a third
  screen needs it.
- Numbers are formatted `N0`; the presenter re-renders on `LocaleChanged`.
- The asynchronous-initialisation traps are the same as Laws' (`Laws.md` §6.13).

### 6.14 The warehouse popup
- **One popup per resource**, opened from its tile: level, `stock / capacity` with a bar, the progress
  toward the next unit, the rate per hour, the time until full (or "Full"), and both upgrades with
  their prices. The next-unit bar is hidden while full, and when a unit lands in under a second,
  where it would only flicker.
- **It is a panel**: `Show` + a `WaitForChoice` loop (`Popups.md` §1). Each upgrade asks for
  confirmation on top; confirming closes only the confirmation, and the panel redraws at the new level.
- **Both buttons are always tappable below the cap.** Affordability is in the confirmation: the
  price, the level and capacity it buys, and — when short — a notice with Confirm inactive. At the cap
  both buttons are inactive and "Fully upgraded" replaces the prices.
- **No close button.** A tap outside closes the topmost popup.
- **No collect button.** Regenerated units go straight into the warehouse (GDD §7). Collecting is
  Economy's mechanic (GDD §8); giving Trade one too would blur the two and add a chore.

### 6.15 The receipt is a strip, not a popup
After an accept, `TradeReceiptView` lists each resource received and, on red lines of its own, each
unit lost to a full warehouse — so a loss is always visible, never a silent deletion. A popup would
cost a tap on every trade, and the player already saw the loss in the confirmation. It sits over the
top of the list, has **no raycast targets** and a `CanvasGroup` that blocks none, so it never swallows
a tap; a new trade replaces it.

---

## 7. How to extend

**Retune the economy** — `TradeConfig`. Nothing hardcodes 10, 1200, 3, the share or the coefficients.

**Raise the level cap** — `TradeConfig.MaxWarehouseLevel`. `TradeConfigContentTests` fails if the top
capacity reaches the Ledger's ceiling or the largest offer leaves `int`.

**Add a purchase** — a price, an affordability check and a `Try…` side by side on `TradeManager`
(§6.3); a confirmation in the presenter that re-reads them; one sound and one haptic.

**Add a resource** — add it to `TradeResourceType`, create its `TradeResourceDefinition` in
`Systems/Ledger/ScriptableObjects/TradeResources/` and list it in `TradeResourceRegistry`, add
`resource.<name>` to `SharedTable`, give it a pair in `TradeResourcePair`, and add a tile (an odd
count needs a new grid shape). `LedgerDataTests` reports what is missing; the generator clamps its
per-side counts, so a different resource count degrades rather than breaks.

**Show something more on a tile, row or popup** — a field on its view data, filled in the presenter,
drawn by the view. A tile's data stays string-free (§6.8); a popup's data keeps its field-by-field
equality up to date.

**Add displayed text** — fixed chrome: a `LocalizeStringEvent` pointing at a new `TradeUITable`
entry. Built in code: a constant on `TradeUIText`, added to `AllKeys`.

**Add a language** — add the locale and fill the tables. No code. Check the TMP font has the glyphs.

**Give a resource its icon** — drop a sprite on its `TradeResourceDefinition`. No code; tiles, rows,
popups and the receipt all pick it up.

---

## 8. Tests

`Assets/Tests/EditMode/Modules/Trade/`

| File | Covers |
|---|---|
| `TradeManagerTests` (56) | Lifecycle, the clock subscription, `OffersChanged` (once per accept, only on replacement), sizing from current capacities and never resizing, the accept transaction (shortfall, stale id, regen first, received and forfeited equal to the warned excess), the instant refresh, both upgrades (**quoted == charged** at every level, rounding up, at the cap), the regen read-outs, save/load (exact terms, remaining time kept, levels authoritative and clamped, damaged names, timestamps and offers, a schema-7 decimal file, a real JSON round trip) |
| `TradePresenterTests` (30) | Tile data (level from 1, no strings, an external Ledger change seen with no event), offer rows, both popups' data (blockers, every loss, a missing offer, the cap shown as 20, rate and time to full, equality), prices are the manager's, refresh sound and haptic gating, `OffersArrived` only on a refresh, locale and visibility re-renders, opening any popup spends nothing |
| `TradeOfferGeneratorTests` (14) | The 30/45/25 mix across 2,000 seeded batches and at uneven capacities, per-batch floors, sides never overlapping, 2–3 types a side, every amount within its bound of its own warehouse at the maximum share, offers growing with capacity, the bands, unique ids |
| `TradeOfferSizingTests` (8) | The worked totals, the smaller side, the minimum, halves rounding up, distinct bands at the minimum, an exact proportional split |
| `TradeOfferAllocatorTests` (4) | Both splits sum exactly on every call; the stochastic one hits its targets in expectation; no targets give no parts |
| `TradeOfferTests` (9) | Totals, immutability, damaged terms refused, overflow warning but not blocking |
| `OfferRefreshTimerTests` (13) | One refresh per settle, phase kept, backwards clocks, restore, the one-interval pull-in |
| `WarehouseCurveTests` (13) | `TopLevel`, the pinned banker's rounding (`100 × 1.5² → 220`), worked capacities and prices, the clamp, never zero, never past the Ledger ceiling or `int` |
| `TradeConfigContentTests` (3) | The real asset: the top capacity under the Ledger ceiling, strictly rising curves, the largest offer inside `int` |
| `TradeResourcePairTests`, `TradeTextTests` | The GDD pairing, symmetric and never self; every `TradeUIText.AllKeys` entry in every locale |

Also `Systems/Ledger/TradeResourceRegenTests` (whole-unit regen: exactly 24 h at every capacity, many
ticks equal one absence, progress kept across an upgrade, the read-outs, a backwards clock),
`Shared/Accrual/WholeUnitAccrualTests` (the shared algorithm, up to the top of `long`),
`KingdomLedgerTests`/`KingdomLedgerSaveTests` (the integer API, the capacity ceiling, a schema-7 file
loading, integers written) and `Shared/Text/TimeFormatTests`.

**What EditMode cannot cover:** the confirmed paths (they wait on a popup answer) and everything
MonoBehaviour + DOTween. **Play mode**, always from `Bootstrap`, with every tap a real `EventSystem`
raycast, verified:
- the existing save loads; tiles show names, levels from 1 and grouped numbers, with the icon slots
  hidden;
- a tile tap opens that resource's popup with the correct rate, time to full and prices; the crystal
  and paired upgrades charge exactly the quoted price, close only the confirmation and redraw the
  popup and tile; short funds show the notice with Confirm inactive and charge nothing; an upgrade into
  the top level shows "Level 20" and "Fully upgraded" with both buttons inactive and no next-unit bar;
  outside taps close the confirmation, then the popup;
- a short offer shows its shortfall with Accept inactive; an overflowing offer lists every loss and
  accepts, leaving those warehouses exactly full; the receipt shows gains and losses and never blocks
  a tap; the accepted row fades out and the deadline stays;
- the instant refresh charges exactly its price, replaces the list and restarts the timer; short
  funds show the notice; an automatic refresh landing while it is open closes it, spending nothing;
- English and Ukrainian, switched at runtime; layout at 3:4, 9:16 and 9:20;
- sound and haptic counts on every confirmed path, through the logging stub services: one sound per
  action, one haptic per purchase, none on an automatic refresh.

---

## 9. Known gaps

| Gap | Status |
|---|---|
| No resource icons | Everything is wired; the six `TradeResourceDefinition`s have no sprite, so every slot is hidden. |
| Number grouping follows the device | `N0` uses the current culture, not the selected locale, so the separators can disagree with the language. |
| Lopsided offers | When one warehouse is far larger than its side's others, the proportional split gives "322 Metal + 2 Clay + 2 Leather". Working as specified; a balance question. |
| Overflow warnings are common for a once-a-day player | Warehouses are often full on return, so most offers warn. Consistent at every level; a balance question. |
| No currency header | The crystal balance is not on screen anywhere yet; it is planned as an always-visible header. |
| Ukrainian plurals | "{0} кристалів" uses one form for every count; the entries are plain `{0}` formats, not Smart Strings. |
| SFX ids are strings in `TradePresenter` | Blocked on the audio system; `StubAudioService` logs them. |
| Prefab is greybox | Functional and re-styleable; the disabled Accept tint is faint. No pixel art yet. |
| `link.xml` unverified | Only a real IL2CPP device build proves the `TradeStateDto` round trip. |
| No PlayMode tests | The scroll list and the confirmation flows would benefit. |
