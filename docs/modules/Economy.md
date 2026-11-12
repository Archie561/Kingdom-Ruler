# Economy module — architecture

> Implements **GDD.md §8 — Economy**. The GDD says *what* the mechanic does; this document says
> *how it is built* and which decisions must survive future changes. Project-wide rules live in
> `ARCHITECTURE.md`.

---

## 1. The mechanic in one paragraph

Six gold-only business types are on sale from the start, each at a flat price per unit. Every
unit produces the type's base rate into **one storage per type**, which holds 24 hours of that
production and fills one whole coin at a time — while the app is closed too — then stops. The
player collects a type's storage into the gold balance by hand, from a **Collect** button on its
row or from its panel. Buying units is the core loop, so a buy the gold covers happens on the tap;
one it does not cover opens a confirmation offering to cover exactly the missing gold with
crystals. A new game starts with exactly the cheapest business's price.

---

## 2. Structure

```
Modules/Economy/
  Scripts/                              assembly KingdomRuler.Modules.Economy
    Data/
      BusinessDefinition.cs             one business type: id, artwork, rate, flat price
      EconomyConfig.cs                  tunables + the business list (references)
    Model/                              the mechanic — plain C#
      EconomyManager.cs                 entry point: lifecycle, clock, every Ledger transaction
      BusinessHolding.cs                one type's units owned and its storage
      BusinessProduction.cs             the rate seam: gold per hour and storage capacity
      BusinessPricing.cs                flat prices, "Max", the crystals that cover a shortfall
    UI/
      EconomyPresenter.cs               model ↔ view; buy, top-up and collect flows; feel hooks
      EconomyScreenView.cs              the screen root: polls the rows and the gold total
      EconomyUIText.cs                  String Table keys for text built in code
      BusinessRow/                      BusinessRowView, BusinessRowViewData — one business on the screen
      BusinessPopup/                    BusinessPopup, …Data, …Choice, BuyAmount — the business panel
      GoldTotal/                        GoldTotalView — TEMPORARY, until the currency header exists
  ScriptableObjects/
    Config/EconomyConfig.asset
    Businesses/                         one BusinessDefinition asset per business (six)
  Prefabs/                              EconomyScreen, BusinessRow, BusinessPopup
  Localization/                         EconomyUITable (chrome + code-built text),
                                        BusinessesTable (names and flavor, keyed by id)
  Images/                               BarFill.png — the sprite every fill bar uses (§6.10)
```

Outside the module, Economy uses three building blocks it shares with others:
`Shared/Accrual/WholeUnitAccrual` (the storage fill, also Trade's warehouse regen),
`Shared/Text/NumberFormat` + `NumberSuffixes` (big numbers, suffixes in `SharedTable`), and
`KingdomLedger.MaxGold` (the gold ceiling).

**Namespaces.** `Data/` and `Model/` share `KingdomRuler.Modules.Economy`; `UI/` is
`KingdomRuler.Modules.Economy.UI`, so a model file reaching for the UI is easy to spot.

**Dependencies point one way:** `UI → Model → Data`, and everything may use `Systems/` and `Shared/`.

```
 UI      EconomyScreenView ──► EconomyPresenter ──► EconomyManager, KingdomLedger (reads only),
         ├─ BusinessRowView ×6                     PopupSystem, IAudioService, IHapticService,
         ├─ GoldTotalView                          ILocalizationService, NumberFormat
         └─ BusinessPopup, ConfirmPopup (via PopupSystem)
                                                   │
 Model   EconomyManager ──► BusinessHolding ──► WholeUnitAccrual (Shared)
            │          └──► BusinessProduction, BusinessPricing
            └──► KingdomLedger (writes), IClock
                                                   │
 Data    EconomyConfig ──► BusinessDefinition
```

**Registration.** The composition root registers `EconomyConfig`, `EconomyManager` and
`EconomyPresenter` — nothing else. `EconomyManager` builds one `BusinessHolding` per listed business.

---

## 3. Classes

### Data

| Class | Responsibility |
|---|---|
| `BusinessDefinition` | One business type: `BusinessId`, `Artwork`, `GoldPerHour` (per unit), `UnitPrice` (flat). Holds no text — `{BusinessId}.name` / `.flavor` in `BusinessesTable`, derived by `NameKey`/`FlavorKey` (§6.12). `BusinessId` is also the save key. Rate and price are `long`, clamped to ≥ 1 in `OnValidate` (`[Min]` does not apply to `long`). |
| `EconomyConfig` | `StorageHours` (24), `GoldPerCrystal` (100), and `Businesses` — the business assets in display order, cheapest first. `MaxUnitsPerType` (1,000,000) is a constant here: a safety ceiling, not a tunable, kept beside the numbers it bounds (§6.7). |

### Model

| Class | Responsibility |
|---|---|
| `EconomyManager` | The mechanic's entry point and **the only class in the module that touches the Ledger or the clock** (§6.1). Lifecycle (`InitializeNewGame`, `LoadFromDto`, `ToDto`), the clock subscription, and every transaction — buying for gold, the crystal top-up, collecting — each price beside the method that charges it. Addresses businesses by `BusinessId`; an unknown id reads as zero and every `Try…` refuses. |
| `BusinessHolding` | One type's `UnitsOwned`, `Stored` gold and `StartedUtc` (when the coin in progress started). Settles inside every mutation (`AddUnits`, `TakeStored`). `Restore` clamps a saved state to the current content. Pure — handed `now`. |
| `BusinessProduction` | **The rate seam** (§6.6): `GoldPerHour(business, units)`, `StorageCapacity(…)` = rate × `StorageHours`, `StoragePeriod(config)`. Saturates at `MaxGold` rather than overflowing. |
| `BusinessPricing` | `PriceOf(unitPrice, count)` (flat, saturating), `AffordableCount(gold, unitPrice, room)` ("Max"), `CrystalsToCover(missing, goldPerCrystal)` = ceil, at least 1, 0 when nothing is missing. Overflow-safe; the crystal price is a `long`. |

```csharp
// EconomyManager
void InitializeNewGame();   void LoadFromDto(EconomyStateDto);   EconomyStateDto ToDto();
void AdvanceTo(DateTime now);                                     // subscribed to IClock.Ticked
event Action StoragesFilled;                                      // at most once per tick
IReadOnlyList<BusinessDefinition> Businesses;   long StartingGold;

int UnitsOwned(id);  long GoldPerHour(id), StorageCapacity(id), Stored(id);  bool IsStorageFull(id);  int RoomFor(id);

long PriceOf(id, count);           bool CanAfford(id, count);              bool TryBuy(id, count);
int  MaxAffordable(id);
long GoldShortfall(id, count);     long CrystalTopUpCost(id, count);
bool CanAffordCrystalTopUp(id, count);                                     bool TryBuyWithCrystalTopUp(id, count);
long Collect(id);
```

### UI

| Class | Responsibility |
|---|---|
| `EconomyPresenter` | Turns model state into abbreviated, localized view data; runs the panel (`OpenBusiness`), the buy-on-tap and top-up flows, and collect; owns sound and haptics; gates the passive sound on visibility; caches the number suffixes and drops them on `LocaleChanged`. Plain C#, no Unity UI types. |
| `EconomyScreenView` | The screen root. Polls every row and the gold total every frame and writes only what changed; asks for text only when its `NumberFormat.DisplayedValue` moves. Binds rows by id and logs a missing, unknown or doubled row. Forwards row taps and Collect taps. |
| `BusinessRowView` | One business: artwork, name, owned, storage bar and text, "Full" marker, and a Collect button shown while the storage holds gold, drawn over the row's own button (§6.9). Plays the collect float, the bought punch and the full pop. Declares its own `BusinessId`. |
| `BusinessPopup` | `Popup<BusinessPopupData, BusinessPopupChoice>`: artwork, name, owned, rate, flavor, storage with Collect, Buy ×1 / ×10 / Max with prices. Its choice is the button pressed. `PlayCollected` / `PlayBought` are called by the Presenter that opened it. |
| `GoldTotalView` | **Temporary** (§6.14). The balance, punching on a rise. |
| `BusinessRowViewData` | What a row draws, read every frame: **no strings** (a test pins it). |
| `BusinessPopupData` | What the panel draws, already localized and abbreviated; `IEquatable`, field by field, because the panel re-reads it at 4 Hz. |
| `BusinessPopupChoice`, `BuyAmount` | Collect / BuyOne / BuyTen / BuyMax; One / Ten / Max. |

```csharp
// EconomyPresenter
event Action StateChanged;   event Action<string, long> Collected;   event Action<string> BusinessBought;

IReadOnlyList<string> BusinessIds;   BusinessRowViewData GetRow(id);   long Gold;
string Amount(long);   string BusinessName(id);   string UnitsOwnedText(int);
BusinessPopupData GetBusiness(id);

UniTaskVoid OpenBusiness(id);   UniTask Buy(id, BuyAmount);   void Collect(id);   void SetScreenVisible(bool);
```

**When a UI element gets its own View class:** when it repeats (the row ×6) or has behaviour of its
own (the panel; the gold total's punch). Labels and buttons inside them are serialized fields.

---

## 4. The storage model

### Whole coins, one shared algorithm

A storage is the same thing as a Trade warehouse: a store that fills from empty to full over a fixed
period, one whole unit at a time, with the partial unit kept in a timestamp. Both run on
`Shared/Accrual/WholeUnitAccrual` (`ARCHITECTURE.md` §4.5), so they cannot drift apart. Here the
period is `StorageHours`:

```
GoldPerHour  = units × business.GoldPerHour                    (BusinessProduction — the seam)
capacity     = GoldPerHour × StorageHours                      an empty storage is full in exactly StorageHours
one coin     = every StorageHours ÷ capacity                   at any number of units
StartedUtc   = when the coin now accruing started              the partial coin lives here, not in a fraction
```

- **Buying settles first**, at the old rate, then raises rate and capacity together; the time already
  spent toward the coin in progress carries over (tested).
- **A full storage banks nothing**: its start follows the clock, so it does not burst-refill after a
  collect.
- **A type with no units has capacity 0**: it produces nothing and is never "full".
- **Exact at any capacity.** Products that would leave `long` go through the helper's 128-bit
  multiply-divide. Its start is floored to a whole tick, so rounding favours the player: a storage
  settled at 4 Hz runs ahead of one settled once by under one tick of production per settle — under
  one part in 2.5 million, visible only near the gold ceiling, where a tick holds whole coins.

### The storages are module state

Gold sits in the storage, not the balance (GDD §8), and only Economy has storages, so by the
ownership test (`ARCHITECTURE.md` §4.3) they are Economy's. Only the balance is the Ledger's.

### The numbers

Placeholders, accepted for playtest (GDD §8 keeps them **[ASSUMED — CONFIRM]**):

| `BusinessId` | Price | Gold / hour per unit | Payback | Storage, 1 unit |
|---|---:|---:|---:|---:|
| `business.bakery`   | 100        | 60         | 1 h 40 m | 1,440 |
| `business.tavern`   | 1,500      | 1,000      | 1 h 30 m | 24,000 |
| `business.tailor`   | 20,000     | 15,000     | 1 h 20 m | 360,000 |
| `business.market`   | 250,000    | 210,000    | ≈ 1 h 11 m | 5,040,000 |
| `business.jeweller` | 3,000,000  | 2,800,000  | ≈ 1 h 04 m | 67,200,000 |
| `business.bank`     | 40,000,000 | 40,000,000 | 1 h 00 m | 960,000,000 |

`StorageHours` 24, `GoldPerCrystal` 100. Payback shortens slightly with price — with flat prices it
is the only thing that makes a dearer business worth buying (§9, balance).

---

## 5. Data flow

### Startup
```
GameStateCoordinator.LoadOrInitialize()
  ├─ KingdomLedger.LoadFromDto(state.Ledger)                       gold clamped to MaxGold
  ├─ save has Economy → EconomyManager.LoadFromDto(dto)
  │                       1. each holding: Restore(units, stored, timestamp) — clamped to current content
  │                       2. unknown ids → warning, skipped; unmentioned businesses own nothing
  │                       3. each holding: Settle(now)               ← offline accrual, once; raises nothing
  └─ otherwise         → EconomyManager.InitializeNewGame()         nothing owned, + StartingGold
                         (a new game, or a save from before Economy existed)
```

### A tick (4 Hz)
```
GameClock.Ticked(now)
  └─ EconomyManager.AdvanceTo(now) → every holding settles → any crossed to full? → StoragesFilled (once)
       └─ Presenter: "business full" sound, if the screen is visible

EconomyScreenView.Update()       (every frame, independently)
  ├─ each row: GetRow(id) → Render (bar, Collect shown, Full marker); owned / storage text on change
  └─ gold total: text when DisplayedValue(Gold) changes; punch on a rise
```

### Opening a business and buying
```
Row tapped → EconomyPresenter.OpenBusiness(id)
  └─ BusinessPopup.Show(() => GetBusiness(id))             stays open, re-reads at 4 Hz
       loop: WaitForChoice()
         Collect      → Collect(id)                         see below
         BuyOne/Ten/Max → await Buy(id, amount)
              count = 1 / 10 / max(1, MaxAffordable)        past the units ceiling: nothing
              gold covers it → TryBuy: settle, spend, units + count     ← on the tap, unconfirmed
                               → buy sound + haptic → BusinessBought (row and panel punch)
              otherwise      → ConfirmPopup.Ask(shortfall, crystal price, canConfirm,
                                                closeWhen: shortfall gone or no room)
                   Confirm → TryBuyWithCrystalTopUp: re-reads both prices, spends the crystals and
                             the whole gold balance, units + count
                             → buy-with-crystals sound + haptic → BusinessBought
       tap outside → closes the top popup only: the confirmation first, then the panel
```

### Collecting
```
Row's Collect button, or the panel's → EconomyPresenter.Collect(id)
  └─ EconomyManager.Collect: settle → take up to (MaxGold − balance) → AddGold
       └─ > 0: collect sound (no haptic) → Collected(id, amount) → "+1.23K" float on the row (and the panel)
```

### Save / load

`GameStateCoordinator` calls `ToDto()` / `LoadFromDto()`. `EconomyStateDto` (`Systems/Save/Scripts/Dto/`)
maps `BusinessId` → `BusinessStateDto`:

| Field | Restores |
|---|---|
| `unitsOwned` | units owned — capacity and rate are **derived** from it through the current content, so a retuned rate reaches existing saves |
| `stored` | gold in the storage, uncollected |
| `lastUpdatedUtc` | when the coin in progress started — offline accrual depends on it |

Load is defensive, because the save is plain JSON on the device:
- units are clamped to `0..MaxUnitsPerType`, then stored gold to `0..capacity` — so a retuned smaller
  rate or an edited file never holds more than the storage can;
- whole numbers read through `WholeNumberJsonConverter` / `WholeNumberLongJsonConverter`: `12.0` loads,
  a number beyond its type saturates, and the clamp above does the rest — one bad number never sets
  the whole save aside;
- a missing or unreadable timestamp becomes now: at most the coin in progress is lost, never gold granted;
- a future timestamp resynchronises to now instead of stalling;
- a save with no `economy` section starts Economy fresh, adding the starting gold to its balance.

`GameStateDto.CurrentSchemaVersion` is 9.

---

## 6. Rules that must hold

### 6.1 Only `EconomyManager` touches the Ledger and the clock
Every Ledger write Economy makes is in one file. `BusinessHolding`, `BusinessProduction` and
`BusinessPricing` are pure. A new transaction goes on the manager.

### 6.2 Gold purchases are not confirmed; crystal purchases always are
Buying units is the core loop (GDD §8), so a buy the gold covers is charged on the tap. Only a
shortfall opens a confirmation, and that one is a crystal purchase. This is the project rule
(`ARCHITECTURE.md` §4.7) — Trade, where every purchase is confirmed, is the other case of it. It is
easy to "fix" by accident by adding a confirmation to the gold path; don't.

### 6.3 The price quoted is the price charged — and the top-up is one transaction
Every price and its check sit on the manager beside the `Try…` that charges it; the presenter only
reads them, and each `Try…` re-reads the price when it charges. Tests assert **quoted == charged**
for ×1, ×10, Max and the top-up.

The top-up **spends the crystals, then the player's whole gold balance**. That is exactly "add the
missing gold, then pay the price", but as one gold change: no subscriber sees the topped-up balance,
and nothing can meet the gold ceiling on the way. Crystals go first because they are the part that can
be refused; a refused top-up changes nothing. With a balance of 0 the gold spend is skipped, so no
empty gold change is published.

### 6.4 Settle first, and every tick
Whoever decides from a storage, or changes a rate, settles it first (`ARCHITECTURE.md` §4.5):
`BusinessHolding` settles inside every mutation. The clock also settles every storage on every tick,
like warehouse regen — simple, and it resynchronises a device clock moved back mid-session.

### 6.5 The storages are Economy's; the algorithm is shared
State in the module, math in `Shared/Accrual` (§4). Do not move storages into the Ledger, and do not
fork the fill math into the module.

### 6.6 The rate seam
`BusinessProduction.GoldPerHour` is **the only place a production rate is computed**; capacity, the
per-hour read-out and accrual all derive from it. A production multiplier (a region bonus, GDD §9; a
Shop booster, §11) is applied there. One owned by another module needs a shared home in `Systems/`,
since modules cannot call each other, and a notification so Economy settles its storages before the
rate changes (§6.4).

### 6.7 Ceilings
- **Gold**: `KingdomLedger.MaxGold` (999,999,999,999,999). A collect takes only what fits; the rest
  stays in the storage — nothing is silently lost.
- **Units per type**: `EconomyConfig.MaxUnitsPerType` (1,000,000). "Max" is clamped to the room left;
  a buy past it is refused; at the ceiling the panel shows "Maximum owned" instead of prices.
- **Content**: `EconomyContentTests` fails any business whose storage at the units ceiling would exceed
  `MaxGold`, or whose price would. Production and pricing saturate rather than overflow regardless.

### 6.8 Polled, not pushed
Storages fill continuously and publish nothing, so the rows, the gold total and an open panel re-read.
`BusinessRowViewData` carries no strings, and text is asked for only when its displayed value changes,
so steady polling formats and allocates nothing. The one push is `StoragesFilled` — a plain event
(its only subscriber holds the manager, `ARCHITECTURE.md` §4.2), raised **at most once per tick**, so
storages that filled together while the app was away make one sound on resume, and never raised at
load, so nothing sounds at startup.

### 6.9 Rows are keyed by id; one row, two tap targets
Each `BusinessRowView` declares its `BusinessId`; the screen binds by it and logs a missing, unknown or
doubled row — reordering the rows is harmless. The row's Collect button is a child drawn **over** the
row's own button, so a tap on it reaches only Collect (the click handler is the nearest one above the
graphic hit) and never opens the panel. Verified with real raycasts.

### 6.10 Canvases, bars and layout
| Canvas | Why it is separate |
|---|---|
| `EconomyScreen` (root) | the static rest |
| `GoldTotal` | changes on every collect and purchase |
| `BusinessList` (+ `GraphicRaycaster`) | the bars change constantly (`fillAmount` dirties vertices) and the `ScrollRect` re-lays out on drag |

- **Every fill bar has a sprite** (`Images/BarFill.png`): a UGUI `Image` with none ignores `Filled`.
  A coin landing sets the fill; a collect or purchase (a jump of 2 % or more) tweens it.
- **A missing artwork hides its slot**: an `Image` with no sprite draws a white box.
- **Top-down layout**: the gold total is fixed at the top of the safe area; the list stretches to the
  safe-area floor (padding 160 for the nav bar). Verified at `KR 3:4`, `KR 9:16`, `KR 9:20`.

### 6.11 One haptic per action
| Action | Sound | Haptic |
|---|---|---|
| Buy for gold (×1, ×10, Max) | `sfx_economy_buy` | 1 |
| Crystal top-up confirmed | `sfx_economy_buy_crystals` — one sound | 1 |
| Refused at the charge (a race) | `sfx_economy_refused` | — |
| Collect, from the row or the panel | `sfx_economy_collect` | — |
| A storage fills, screen visible | `sfx_economy_business_full`, once per tick | — |
| A storage fills, screen hidden | — | — |
| Opening or closing a popup | `PopupSystem`'s | — |

Animation: bars tween on jumps; "+1.23K" floats up from the bar on a collect (row and panel); the owned
count punches on a purchase; the "Full" marker pops in; the gold total punches on a rise. Every tween
runs on unscaled time and is killed in `OnDisable`/`OnDestroy`.

### 6.12 Localization
- **Data-derived**: business names and flavor at `{BusinessId}.name` / `.flavor` in `BusinessesTable`,
  through `BusinessDefinition.NameKey`/`FlavorKey` — the presenter and the content test use the same
  members.
- **Built in code**: `EconomyUIText` (owned, rate, Max, maxed, the top-up's title, body and notice),
  every key in `AllKeys`, checked in every locale by `EconomyContentTests`.
- **Fixed chrome**: `LocalizeStringEvent` on the prefabs — Collect, Buy ×1, Buy ×10, Full, Gold.
- **Numbers**: every number on screen — amounts, counts, prices — goes through
  `EconomyPresenter.Amount` (`NumberFormat`, truncated, suffixes from `SharedTable`). The presenter
  caches the four suffixes and drops them on `LocaleChanged`, which the localization service also
  raises once when it finishes starting up, so placeholders read before then do not stick (tested).
- The Ukrainian top-up line avoids a number before a plural noun (`ui.topup.body`).

### 6.13 The starting gold
A new game starts with exactly `StartingGold` — the cheapest listed business's price, derived from the
content, never a second number that could drift. `InitializeNewGame` adds it to the balance, so a save
from before Economy existed keeps its gold and gains the start.

### 6.14 The gold total is temporary
`GoldTotalView` lives on this screen only until the always-visible currency header exists, as a
`Systems/` entry. To remove it: delete `UI/GoldTotal/`, the `GoldTotal` object in `EconomyScreen.prefab`,
and `_goldTotal` with its two uses in `EconomyScreenView`. `EconomyPresenter.Gold` then has no caller.

---

## 7. How to extend

**Add a business** — create a `BusinessDefinition` in `ScriptableObjects/Businesses/` with a new
`BusinessId`, rate and price; add it to `EconomyConfig.Businesses` in price order; add
`{BusinessId}.name` and `.flavor` to `BusinessesTable` in every locale; add a `BusinessRow` instance to
`EconomyScreen.prefab` with that id in its `_businessId`. `EconomyContentTests` checks the id, the text,
the order and the ceiling; `EconomyScreenView` logs a missing row. **Never rename a `BusinessId`**: it
is the save key — renaming it loses every player's units of that business.

**Retune** — rates and prices on the business assets; `StorageHours` and `GoldPerCrystal` on
`EconomyConfig`. A retuned rate reaches existing saves (capacity is derived), and a smaller one clamps
stored gold on load.

**Add a production multiplier** — in `BusinessProduction.GoldPerHour` only (§6.6), with the settle-first
notification if its source lives outside the module.

**Add a purchase** — a price, an affordability check and a `Try…` side by side on `EconomyManager`
(§6.3); the flow in the presenter; one sound and one haptic.

**Show something more on a row or the panel** — a field on its view data, filled in the presenter,
drawn by the view. A row's data stays string-free; the panel's keeps its field-by-field equality.

**Add displayed text** — fixed chrome: a `LocalizeStringEvent` pointing at a new `EconomyUITable`
entry. Built in code: a constant on `EconomyUIText`, added to `AllKeys`.

**Give a business its art** — drop a sprite on its `BusinessDefinition.Artwork`. No code; the row and
the panel pick it up, and `EconomyContentTests` stops listing it.

---

## 8. Tests

`Assets/Tests/EditMode/Modules/Economy/`

| File | Covers |
|---|---|
| `EconomyManagerTests` (48) | The starting gold (cheapest, added to an existing balance, none without businesses); flat prices and **quoted == charged** for ×1, ×10, Max; Max and the units ceiling; settle-before-buy and the coin in progress carrying over; filling in exactly 24 h and stopping; the clock alone settling; `StoragesFilled` once per crossing, once per tick, never at load; collect (settle first, one gold change, at the gold ceiling); the top-up (ceil, minimum 1, exact charge, one change to each currency, short of crystals, nothing missing, the ceiling, a price beyond `int`); save/load through real JSON and every defensive case in §5 |
| `EconomyPresenterTests` (27) | Rows (numbers, no strings, Collect shown, Full); the gold total; suffixes (shared, per language, resolved once, not stuck from startup); the panel's data (every field, Max with nothing affordable, a large Max count abbreviated, near and at the ceiling, field-by-field equality); reading spends nothing; collect (sound, no haptic, silent when empty); buy on the tap (one sound, one haptic, exact charge; Max; nothing past the ceiling); the full sound gated on visibility and once for a burst; visibility; `Dispose` |
| `BusinessPricingTests` (16), `BusinessProductionTests` (8) | Flat prices, "Max", the ceil rule, saturation; linear rate, `StorageHours` of capacity, saturation at `MaxGold` |
| `EconomyContentTests` (9) | The real assets: one config, no empty slots, unique ids, every business listed, name and flavor in every locale, positive rate and price, every storage under `MaxGold` at the units ceiling, cheapest first, every `EconomyUIText` key in every locale; missing artwork warns |

Also `Shared/Accrual/WholeUnitAccrualTests` (the fill, up to the top of `long`),
`Shared/Text/NumberFormatTests` (the scheme and the real suffixes),
`Systems/Save/WholeNumberLongJsonConverterTests`, `Systems/Ledger/KingdomLedgerTests` and
`KingdomLedgerSaveTests` (the gold ceiling).

**What EditMode cannot cover:** the top-up's confirmed path (it waits on a popup answer), the panel
loop, and everything MonoBehaviour + DOTween. **Play mode**, always from `Bootstrap`, with every tap a
real `EventSystem` raycast, verified:
- an existing schema-8 save loads and gains exactly the starting gold, once; the file is written as
  schema 9 with an `economy` section;
- the Economy tab is live; the screen shows the gold total and six rows in Ukrainian and English;
- a row tap opens the panel; Buy ×1 charges on the tap with one sound and one haptic, and the panel
  stays open and redraws;
- with no gold, Buy ×1 opens the top-up with the right shortfall and crystal price; opening it spends
  nothing; Confirm charges exactly that, buys the unit and closes only the confirmation; short of
  crystals the notice shows and Confirm is inactive and does nothing; a tap outside closes only the
  confirmation, a second tap closes the panel;
- Max buys as many as affordable without asking;
- Collect in the panel settles to the moment and floats the amount; the row's Collect collects without
  opening the panel; neither triggers a haptic;
- a storage filling plays the full sound and shows the marker while the screen is visible, and is
  silent on another tab;
- the language switches at runtime — chrome, code-built text and suffixes together;
- layout at 3:4, 9:16 and 9:20;
- no errors or warnings; the sound and haptic log matches §6.11;
- the tick: `EconomyManager.AdvanceTo` ~0.6 µs with a 10,000-unit storage landing coins every call,
  nothing allocated (`ARCHITECTURE.md` §4.5).

A trap when driving it from MCP: a popup is not raycastable in the frame it opens, so a tap sent in
that frame falls through to whatever is beneath. Wait a frame between opening a popup and tapping it.

---

## 9. Known gaps

| Gap | Status |
|---|---|
| Decimal separator follows the device | `NumberFormat` is given `CultureInfo.CurrentCulture`, the device's culture, not the selected language — the English UI shows "14,4M" on a Ukrainian device. Shared with Trade's `N0` numbers. The fix is to take the culture from the selected locale (a small addition to `ILocalizationService`), which Trade would use too. **Deliberately not fixed yet.** |
| "0 / 0" on an unowned business | A business with no units shows its empty storage as "0 / 0". Hiding the bar and its text until the first unit would read better. **Deliberately not changed yet.** |
| Disabled Collect looks enabled | In the panel, Collect is inactive while the storage is empty, but the default tint barely dims it. Greybox styling; the art pass replaces it. **Deliberately not changed yet.** |
| No business artwork | Everything is wired; the six `BusinessDefinition`s have no sprite, so every slot is hidden. |
| The gold total is temporary | Moves to the always-visible currency header (§6.14). |
| Flat prices compound | Reinvested income grows exponentially at the best payback rate, and the dearer businesses pay back faster, so growth accelerates: a player checking in four times a day reaches the bank in about three days. A balance question for playtest; the ×1.15 growing price (GDD §8, kept for later) is the lever. |
| Fixed gold-per-crystal rate | One crystal covers 100 gold at every stage — generous early, negligible late. A balance point for playtest. |
| Ukrainian plurals | Entries are plain `{0}` formats, not Smart Strings; the Economy lines are worded to avoid a number before a plural noun. |
| SFX ids are strings in `EconomyPresenter` | Blocked on the audio system; `StubAudioService` logs them. |
| Prefabs are greybox | Functional and re-styleable; no pixel art yet. |
| `link.xml` unverified | Only a real IL2CPP device build proves the `EconomyStateDto` round trip and `WholeNumberLongJsonConverter`. |
| No PlayMode tests | The panel loop and the top-up confirmation would benefit. |
