# Trade module — architecture

> Implements **GDD.md §7 — Trading**. Read that first for *what* the mechanic does; this
> document covers *how it is built* and how to extend it safely.
>
> Project-wide rules live in `ARCHITECTURE.md`. This file only covers what is specific to
> Trade, and states the reasoning behind decisions that look arbitrary from the outside.

---

## 1. What the mechanic is, in one paragraph

Six resources sit in warehouses that refill from empty to full over 24 hours, scaled to each
warehouse's capacity. Ten trade offers are on screen at a time and are replaced every 20
minutes, or immediately for crystals. Each offer gives 2–3 resource types and asks for 2–3
different ones, and 30/45/25 of them are profitable/neutral/unprofitable. Warehouses upgrade
either for crystals or by spending 80% of a paired warehouse's capacity out of that resource's
stock.

---

## 2. Layers and dependency direction

```
                    ┌─────── outside the module · all in Systems/ ───────┐
                    │ Ledger                 Clock/Audio/Haptics Events  │
                    │  KingdomLedger          IClock             EventBus│
                    │  TradeResourceType      IAudioService              │
                    │  TradeResourceRegistry  IHapticService             │
                    └────────────────────────────────────────────────────┘
                                          ▲
 VIEW            TradeView ─────────► TradePresenter
 (MonoBehaviour) ├── WarehouseTileView ────► WarehouseDisplayData
                 ├── TradeOfferRowView ────► TradeOfferDisplayData
                 └── TradeConfirmPanelView ► TradeConfirmDisplayData
                                          ▲
 PRESENTER       TradePresenter ────► TradeManager, KingdomLedger, EventBus,
 (plain C#)                           IAudioService, IHapticService,
                                      ILocalizationService, TradeResourceRegistry
                                          ▲
 CLOCK           GameClock.Ticked ──► TradeManager.AdvanceTo  (Systems/Clock; Trade subscribes itself)
                                          ▲
 MODEL           TradeManager ──────► TradeOfferGenerator, OfferRefreshTimer,
 (plain C#)                           KingdomLedger, IClock, TradeConfig
                                      (TradeConfig also holds the two warehouse curves)
                                          ▲
 DOMAIN          TradeOfferGenerator        (pure, injectable Random)
 (pure C#)       OfferRefreshTimer          (pure, zero project dependencies)
                 TradeOfferAllocator        (pure, static)
                 TradeIssue / TradeOfferEvaluation / TradeAcceptResult
                                          ▲
 DATA (SO)       TradeConfig               (tuning only — no content assets)
```

**The Views never see a `TradeOffer` or any `Domain/` type.** Everything they render arrives as
a display struct built by the Presenter — already localized, icons already resolved.

**There are no content assets**, and `ScriptableObjects/Data/` stays empty. Offers have a
20-minute lifetime and no authored text; they are built from resource names and numbers. The
tempting wrong inference is "Laws has a content table, so Trade needs one" — it does not.
`SharedTable` (`resource.*`) plus `TradeUITable` covers every string on the screen, and the six
resource icons live in `Systems/Ledger/ScriptableObjects/TradeResources/` because they are
Ledger-owned shared display data (`ARCHITECTURE.md` §4.4).

---

## 3. Decisions that look arbitrary but are not

### 3.1 Overflow warns; it does not block

A full warehouse produces a **warning** the player may accept, forfeiting the excess. The trade
is refused **only** when the give side cannot be paid. `TradeIssue.BlocksAcceptance` encodes
this, and it is false for `WouldOverflowWarehouse`.

This is the single easiest thing here to undo by accident, because "the warehouse is full" reads
like a reason to refuse. Three things depend on it:

- `TradeAcceptResult.Received` legitimately reports **less** than the offer stated. The correct
  invariant is `received == min(requested, freeSpace)` — an `actual == requested` assertion
  would be wrong.
- `Forfeited` is what the player knowingly gave up. Before this, the return value of
  `AddTradeResource` was discarded and the excess vanished with nothing on screen.
- The Confirm button stays **enabled** with a warning showing.

### 3.2 The curve is a formula, not a table

`TradeConfig.WarehouseCapacityAt` and `WarehouseUpgradeCrystalCost` share one private formula that
mirrors `CharacteristicLevelingCurve.PointsRequired`, **including its banker's rounding** —
`100 × 1.5² = 225` yields **220**, not 230. Matching the sibling curve's expression matters more
than the one-unit difference; a test pins it. This is the opposite call from the offer-ratio
split, where the same `Math.Round` behaviour silently stole a slot and had to go.

It replaced a 6-entry capacity array and a 5-entry cost array whose fallbacks past the last entry
diverged: capacity grew exponentially while cost grew linearly, so a level-50 warehouse was
cheap.

### 3.3 The 30/45/25 ratio is a long-run average

45% of 10 is 4.5, so no integer split reaches it. Each batch floors to 3/4/2 and awards the two
leftover slots by weighted chance (`TradeOfferAllocator.ApportionStochastic`), giving an exact
expectation. The previous code used `Math.Round` per band and, because `Math.Round(2.5) == 2`,
shipped a permanent **30/50/20** at the one batch size the GDD specifies. Tested statistically
across 2000 batches, as `ARCHITECTURE.md` §8 asks.

Amounts use the *deterministic* allocator instead: a single offer is looked at individually and
nothing averages out across one of them.

### 3.4 Offers are persisted

Without this the offer list was regenerated in the constructor on every launch, so relaunching
the app was a free instant refresh that bypassed the crystal price charged for exactly that.
Schema v5 stores the offers and the refresh **deadline**.

### 3.5 Warehouse levels are authoritative for capacity

Capacity used to exist in three places: the ledger DTO, its regen rate, and the upgrade level.
Now the level is the stored fact, capacity is derived from it through the curve on load, and the
Ledger derives the regen rate from capacity. A retuned curve therefore reaches existing saves.

The loaded level is **clamped** to `TradeConfig.MaxWarehouseLevel` — a save-integrity guard,
not a design cap: the save is plain JSON on the device, and `100 × 1.5^9999` is `Infinity`, which
makes the regen rate infinite and every amount `NaN`.

### 3.6 The Manager holds no EventBus

It publishes nothing; the only subscriber to its state is this module's own Presenter, which
already holds it, so `TradeStateChanged` is a plain `event Action` (`ARCHITECTURE.md` §4.2 tier
3). The Ledger still publishes cross-module `TradeResourceChangedEvent` whenever this module moves
resources through it.

### 3.7 `AcceptOffer` and the upgrades accrue regen first, but do not tick

They call `AccruePassiveResourceRegen`, **not** `AdvanceTo`. Regen is the Ledger's and advances on
the game clock by itself, so this is the rule from `ARCHITECTURE.md` §4.5: whoever decides from an
amount, or changes a capacity, settles regen first. For `AcceptOffer`, a player who crossed the
affordability threshold a fraction of a second ago is honoured, and correctness stops depending
on tick timing — but the offer list cannot be refreshed out from under the trade being accepted.

### 3.8 Accepting by id, not index

The 20-minute auto-refresh can land between tapping a row and pressing Confirm. An index-based
accept would execute a *different* trade than the one on screen; an id turns that into a clean
`OfferUnavailable`.

### 3.9 `TradeUIText` is public and lists every key

Trade composes strings from arguments at runtime ("You need 12 more Stone"), which §2 says is
the moment the keys type appears. It is `public` and in its own file because `TradeTextTests`
lives in the separate test assembly and would otherwise have to re-type the literals. Its
`AllKeys` array is what that test walks, so **a new code-resolved key goes into `AllKeys` too**,
or nothing checks it. The screen's fixed chrome is not listed and not checked — it is on screen
whenever the Trade tab is. Laws has the same shape, `LawsUIText`, for the refill popup.

### 3.10 Five canvases, each for a reason

| Canvas | Why separate |
|---|---|
| `TradeScreen` (root) | Static chrome once the movers are split out. |
| `WarehousePanel` | Six `Image.fillAmount` bars moving continuously with regen; `fillAmount` dirties vertices. |
| `RefreshRow` | A countdown rewriting once a second. |
| `OfferListPanel` | A `ScrollRect` re-laying out on every drag frame. |
| `ConfirmPanelCanvas` | Animates over the rows it covers. `overrideSorting` 10 — same as Laws' `CardCanvas`, safely below the nav bar's 100. |

**Each needs its own `GraphicRaycaster`** or its buttons go silently dead. Verify with real
raycasts, not by invoking `onClick` — the latter bypasses the raycaster and passes either way.

### 3.11 The confirm panel must not close itself in `Awake`

`TradeConfirmPanelView`'s root is authored **inactive**, so `Awake` does not run until `Open`
activates it. An earlier version called `Close()` from `Awake`, which therefore fired *during*
the first `Open` and undid it: the very first tap on an offer flashed the panel and dismissed it,
while every later tap worked. `Open` now activates the root **first**, before assigning anything
that `Awake` could clobber. Found in Play mode; no EditMode test would have caught it.

### 3.12 No object pool for the offer rows

Rows are created once and repopulated; the surplus is parked with `SetActive(false)`. The count
is bounded by config and the list rebuilds every 20 minutes, so a "pool" of a fixed ten *is* the
set itself. `ARCHITECTURE.md` §9's rule targets law cards and floating popups — unbounded,
per-second. The trigger for a real pool is a paged offer list, or the `+50 Stone` floaters.

A parked row is inert by design: `Hide()` clears its offer id and `HandlePressed` early-returns
on an empty one, so tapping it does nothing rather than reopening whatever it last showed.

---

## 4. Data flow

### Startup
```
GameStateCoordinator.LoadOrInitialize()
  ├─ save exists → TradeManager.LoadFromDto(dto)
  │                  ├─ restore levels → SetWarehouseCapacity (Ledger derives regen rate)
  │                  ├─ restore offers + deadline
  │                  ├─ ledger.AccruePassiveResourceRegen(now)  ← offline regen, AFTER capacity is final
  │                  └─ AdvanceTo(now)                          ← offline refresh catch-up
  └─ no save     → TradeManager.InitializeNewGame()
```

Regen is deliberately **not** accrued by the Coordinator. It used to be, against the capacity the
ledger DTO happened to carry — which `LoadFromDto` then overwrote moments later, so the two
fought. Folding it into the module's own load makes "regen never accrues against a capacity that
is about to change" structural rather than a matter of call order.

### A tick
```
GameClock.Ticked(now)  (~4 Hz, unscaled; every subscriber gets the same now, in no required order)
  ├─ TradeManager.AdvanceTo(now)
  │    └─ refreshTimer.Advance() → RefreshOffers() → TradeStateChanged
  ├─ KingdomLedger.AccruePassiveResourceRegen(now)   ← wired in GameBootstrapper
  └─ LawsManager.AdvanceTo(now), Economy, Occurrences …
```

### Accepting an offer
```
TradeOfferRowView tapped → TradeView.HandleOfferPressed
  └─ TradePresenter.TryGetConfirmation → TradeConfirmPanelView.Open   (nothing mutates)
       └─ Confirm → TradePresenter.OnOfferAcceptRequested
            └─ TradeManager.AcceptOffer(id)
                 1. accrue regen
                 2. unknown id → OfferUnavailable, ledger untouched
                 3. Evaluate → any BLOCKING issue → refuse, spend nothing
                 4. spend gives
                 5. add receives, capturing actual amounts → Forfeited
                 6. remove offer, raise TradeStateChanged once
```

---

## 5. Tests

`Assets/Tests/EditMode/Modules/Trade/`

| File | Covers |
|---|---|
| `WarehouseUpgradeCurveTests` | Both curves on `TradeConfig`: monotonicity, the pinned `100×1.5² → 220`, the level clamp, never zero whatever the asset holds, crystal price ≥ 1 and finite |
| `OfferRefreshTimerTests` | One refresh per settle however long the absence, phase preserved, backwards clock, restore |
| `TradeOfferGeneratorTests` | The 30/45/25 ratio across 2000 seeded batches, per-batch floors, no overlap, every share ≥ 1 |
| `TradeDomainTests` | Pairing, upgrade pricing, the accept transaction, both upgrade paths, the level ceiling, save/load including a real JSON round-trip |
| `TradePresenterTests` | Display data, "never disable an offer", price shown == price charged ×3, audio gating, `Dispose` unsubscribing all three sources |
| `TradeTextTests` | Every message in `TradeUIText.AllKeys` exists in every locale (untranslated only warns) |

The Views have **no automated coverage** — MonoBehaviour plus DOTween is PlayMode territory.
Enter Play mode from **Bootstrap, never Main**: `Main` is loaded additively and only then are its
Views injected.

---

## 6. How to extend

**Retune the economy** — `TradeConfig`. Nothing hardcodes 10, 1200, or the curve coefficients.

**Add a resource** — add to `TradeResourceType`, create a `TradeResourceDefinition` in
`Systems/Ledger/ScriptableObjects/TradeResources/`, add it to `TradeResourceRegistry`, add a
`resource.<name>` row to `SharedTable`, add a pairing to `TradeResourcePair`, and author a
seventh tile. `LedgerDataTests` reports what is missing. The generator clamps
its per-side counts, so an odd resource count degrades rather than breaking.

**Add displayed text** — fixed chrome gets a `LocalizeStringEvent` on the prefab plus a key in
`TradeUIText.ChromeKeys`. Text with arguments gets a constant in `TradeUIText.AllKeys` and is
resolved in the Presenter. Never spell a table name or a key at a call site.

**Show resource icons on offer rows** — that is the trigger to replace the joined label in
`TradeOfferRowView` with a per-line sub-view, since an icon needs its own `Image`.

---

## 7. Known gaps

| Gap | Status |
|---|---|
| No resource icons | The registry, display structs, prefab slots and render path are wired and tested; the six `TradeResourceDefinition` assets just have no sprite. Tiles hide the slot until one lands. |
| Confirmation is a screen-local panel, not a popup | `Systems/Popups` landed separately, and its live-data `Ask(() => …, closeWhen: …)` already has the "re-read while open, self-close when stale" semantics `TradeView.RefreshOpenConfirmPanel` hand-rolls. Migrating is the obvious follow-up; deliberately not done in this pass. |
| Prefab is greybox | Functional and re-styleable; no pixel art. |
| SFX ids are magic strings in `TradePresenter.SfxIds` | Blocked on the audio system, same as Laws. |
| No offer-profitability badge | Deliberate — totals are visible and all resources are valued 1:1, so the arithmetic is the player's to do. A verdict label is a design change, not a UI detail. |
| `link.xml` unverified for the new DTOs | Newtonsoft + IL2CPP: only a real device build proves the `TradeOfferDto` round-trip. |
| No PlayMode tests | The scroll list and the confirm flow would benefit. |
