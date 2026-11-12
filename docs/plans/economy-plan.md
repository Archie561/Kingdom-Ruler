# Economy — implementation plan

**Progress:** all eight steps done (2026-11-12). The Economy mechanic is built; `docs/modules/Economy.md` is its reference.

> Implements **GDD §8 — Economy**. The brief behind this plan is `docs/plans/economy-brief.md`.
> Each step below is planned in detail and built in its own session, one step per prompt, and only
> when the user asks for it. When a step is finished it is marked **✔** here, with a short note of
> anything that turned out differently, and this progress line is updated.
>
> Baseline at the start: **469 EditMode tests, all passing** (run via Unity MCP while writing this plan).
>
> The user answered the open questions on 2026-11-11 (§11). One spec change came with them:
> **Collect is also on each business's row**, as well as in the business panel. The GDD §8 text is
> updated in step 7, where the button is built.

---

## 1. Summary

**Goal.** Build the Economy screen and mechanic (GDD §8). Gold-only businesses produce gold into one
storage per business type, and the player collects it by hand, one type at a time, from the
business's row on the screen or from its panel. More
units of a type are bought for gold at a flat price, with no confirmation. When the player is short
of gold, crystals can cover exactly the missing amount, in one confirmed transaction.

**In scope.**
- **Six placeholder business types, all available from the start.** Each has a base rate and a flat
  unit price. *n* units produce *n* × the rate, and a storage holds 24 h of the type's production.
- **The business row.** One per type on the screen: artwork, name, units owned, the storage fill bar,
  and a **Collect** button while the storage holds gold. A tap anywhere else on the row opens the
  panel.
- **The business panel.** It shows the artwork, flavor text, units owned, production per hour and the
  storage fill, with **Collect** and **Buy ×1 / ×10 / Max**. There is no next-coin progress bar;
  the storage fill bar is enough.
- **Buying.**
  - A gold purchase happens on the tap, with sound, animation and a haptic.
  - The crystal top-up costs `ceil(missing ÷ gold-per-crystal)`, at least 1 crystal. It is confirmed,
    and with Max and nothing affordable it offers a single unit.
- **A new game starts with exactly the price of the cheapest business**, derived from the content.
- **Big-number abbreviation** (localized suffixes), and **hard ceilings** on the gold balance and on
  units owned per type.
- **A temporary total-gold widget** on the Economy screen, built to be trivially removable.
- **The "business full" sound**, played only while the screen is visible.
- **One shared whole-unit accrual helper in `Shared/`**, used by both the Ledger's warehouse regen and
  Economy's storages, with arithmetic that stays exact up to the gold ceiling.
- **Save support** (`schemaVersion` 8 → 9), and the Economy tab made live in `Main`.

**Out of scope (GDD §8 "kept for later", and the brief §3.5).** Do not build any of these:
- a price that grows with units owned;
- milestone bonuses;
- "collect all";
- unlocks;
- Random Occurrences acting on one business or its storage;
- a nav-tab badge;
- production multipliers from region bonuses (§9) or Shop boosters (§11);
- the global currency header;
- selling businesses, ads and real-money gold;
- Random Occurrences themselves.

**One seam is kept open on purpose.** `BusinessProduction.GoldPerHour(business, unitsOwned)` is the
**only** place a production rate is computed. Storage capacity, the per-hour readout and the accrual
rate all derive from it. A future multiplier is applied there and nowhere else. A multiplier owned
by another module (a Cities region bonus, a Shop booster) cannot be called from Economy, because
modules never reference each other. It will need a shared home in `Systems/`, which Cities or Shop
writes and Economy reads. The change of rate it causes must also settle Economy's accrual first, so
it will need a notification Economy reacts to. Neither exists yet; both are that later feature's
design.

**No deviation from the standard module shape is needed.** Economy fits `Data/` · `Model/` · `UI/` as
it is.

---

## 2. Target structure

```
Assets/_Game/
  Shared/                                   assembly KingdomRuler.Shared (references nothing)
    Accrual/                                NEW 🟡
      WholeUnitAccrual.cs                   A store that fills from empty to full over a fixed period, one
                                            whole unit at a time. The partial unit lives in a timestamp.
                                            Pure and static: settle, progress toward the next unit, time
                                            until full. Exact up to the gold ceiling.
    Text/
      NumberFormat.cs                       NEW. Abbreviated display of big whole numbers (1.23K … 999T).
                                            Owns the suffix keys and their table name (SharedTable). The
                                            caller resolves the suffix formats and passes them in, so it
                                            stays free of any reference to the localization system.
      TimeFormat.cs                         unchanged; reused for "full in h:mm:ss"
  Systems/
    KingdomRuler.Systems.asmdef             + reference to KingdomRuler.Shared 🟡
    Ledger/Scripts/
      KingdomLedger.cs                      + MaxGold. AddGold clamps to it and reports what fitted;
                                            load clamps to it. 🟡
      TradeResources/TradeResourceState.cs  keeps its API; its regen math delegates to WholeUnitAccrual
    Save/Scripts/
      Dto/GameStateDto.cs                   + economy section; CurrentSchemaVersion 9 🟡
      Dto/EconomyStateDto.cs                NEW. businessId → BusinessStateDto
      Dto/BusinessStateDto.cs               NEW. units owned, gold stored, when the coin in progress started
      (a long counterpart of WholeNumberJsonConverter, since gold amounts are long)
  Modules/Economy/                          NEW module 🟡. Assembly KingdomRuler.Modules.Economy
    Scripts/
      Data/                                 namespace KingdomRuler.Modules.Economy
        BusinessDefinition.cs               One business type: BusinessId, artwork sprite, gold per hour per
                                            unit, unit price. No text: it derives its name and flavor keys
                                            from the id, and owns the table name.
        EconomyConfig.cs                    Tunables: storage hours (24), gold per crystal. Also the list of
                                            business assets, as LawsConfig.AllCards lists the cards.
      Model/                                namespace KingdomRuler.Modules.Economy
        EconomyManager.cs                   Entry point: lifecycle, clock, and every Ledger transaction
                                            (buy, crystal top-up, collect), each price beside its charge.
                                            The only class that touches the Ledger or the clock.
        BusinessHolding.cs                  What the player holds of one type: units owned and its storage.
                                            Pure; handed `now`.
        BusinessProduction.cs               The rate seam: gold per hour and storage capacity from the
                                            definition and units owned, clamped to the ceilings.
        BusinessPricing.cs                  Price of n units, how many the player can afford, crystals to
                                            cover a shortfall. Overflow-safe and pure.
      UI/                                   namespace KingdomRuler.Modules.Economy.UI
        EconomyPresenter.cs                 Model ↔ view: view data, buy/collect/top-up flows, feel hooks,
                                            visibility gating, locale re-render.
        EconomyScreenView.cs                The screen root: polls the rows and the gold total, forwards taps.
        EconomyUIText.cs                    String Table keys for text built in code, with AllKeys.
        BusinessRow/                        BusinessRowView, BusinessRowViewData (string-free, read every
                                            frame). Two tap targets: Collect, and the rest of the row,
                                            which opens the panel.
        BusinessPopup/                      BusinessPopup, BusinessPopupData (IEquatable),
                                            BusinessPopupChoice (Collect, BuyOne, BuyTen, BuyMax)
        GoldTotal/                          GoldTotalView. TEMPORARY until the header exists: delete this
                                            folder, one presenter member and one prefab object.
    ScriptableObjects/
      Config/EconomyConfig.asset
      Businesses/                           one BusinessDefinition asset per business type (six)
    Prefabs/                                EconomyScreen, BusinessRow, BusinessPopup
    Localization/                           EconomyUITable (chrome + code-built text),
                                            BusinessesTable (names and flavor, keyed by id)
    Images/                                 BarFill.png, a copy of Trade's. All textures get replaced later.
  Core/Bootstrap/
    GameBootstrapper.cs                     + EconomyConfig, EconomyManager, EconomyPresenter
    GameStateCoordinator.cs                 + economy load / new game / save
    KingdomRuler.Bootstrap.asmdef           + KingdomRuler.Modules.Economy
Tests/EditMode/
  Shared/Accrual/WholeUnitAccrualTests.cs   NEW
  Shared/Text/NumberFormatTests.cs          NEW
  Modules/Economy/                          NEW: EconomyManagerTests, BusinessPricingTests,
                                            BusinessProductionTests, EconomyPresenterTests,
                                            EconomyContentTests
  KingdomRuler.Tests.EditMode.asmdef        + KingdomRuler.Modules.Economy
```

**Where things live, and why.**

- **The storages are module state, not Ledger state.** Gold sits in a storage, not in the balance
  (GDD §8), and only Economy has storages. By the ownership test (`ARCHITECTURE.md` §4.3), that makes
  them Economy's. Only the balance is the Ledger's.
- **The accrual *algorithm* is shared**, in `Shared/`, so a warehouse and a storage fill by identical
  math.
- **`WholeUnitAccrual` keeps the proposed name.**
  - "Accrual" is already the project's word for this (`ARCHITECTURE.md` §4.5).
  - "Whole unit" is what makes it different from a rate × time float.
  - It is a building block with no state of its own, called statically. That matches `Shared/`'s
    test.
- **The business list stays on `EconomyConfig`,** following `LawsConfig.AllCards`. It is a list of
  references; every business stays its own asset under `ScriptableObjects/Businesses/`.

---

## 3. Steps

Every checkpoint leaves the project compiling with no new warnings and every EditMode test passing,
**confirmed via Unity MCP in that session**:
- compile errors are read from `Editor.log`, not `read_console`;
- the test count is checked against the expected count, so a stale assembly cannot "pass".

**Docs are updated in each step, not only at the end.** A step updates the doc lines its change makes
false, such as `ARCHITECTURE.md` §4.3's "exact in `long` while capacity stays under ~10.6 million" in
step 1. Otherwise the docs would be wrong between steps (`CLAUDE.md` §5). Step 8 writes
`docs/modules/Economy.md` and does the full pass. This is the one change to the brief's suggested
order, and it is why the ordering otherwise stands.

### Step 1 — The shared accrual helper, and the Ledger moving onto it ✔

> **Done 2026-11-12.** 498 EditMode tests pass (469 + 29 new). What turned out differently:
> - **The arithmetic is a hand-written 128-bit multiply-divide**, not `decimal`. Every product
>   that fits in `long` takes the plain path, so every warehouse computes exactly as before.
> - **Rounding is exact to the tick, not to the unit.** Today's floored start lets each settle
>   recount under one tick, in the player's favour. At warehouse sizes, where a unit spans 86,400+
>   ticks, that rounds to nothing. Once a tick holds whole units (from ~10⁷ gold per second, near
>   the ceiling only), a store settled at 4 Hz runs a few units ahead of one settled once: under one
>   part in 2.5 million. The tests assert that bound there, not strict equality. Strict equality
>   would need a stored sub-tick fraction, which is new save state for a scale far beyond play.
> - **Capacity ≤ 0** produces nothing and counts as full. `TimePerUnit` is then `TimeSpan.MaxValue`.


- **Goal.** Add `Shared/Accrual/WholeUnitAccrual`, the whole-unit algorithm now inside
  `TradeResourceState`:
  - settle;
  - progress toward the next unit;
  - time until full.

  Then make `TradeResourceState` delegate to it, with no change in behaviour. The helper is
  parameterised by the period, which is one day for warehouses and `StorageHours` for storages.
- **Arithmetic.**
  - Today's `elapsed ticks × capacity` overflows `long` above ~10.6 M capacity.
  - The helper uses an **exact wider intermediate**: a small, allocation-free 128-bit
    multiply-divide, or `decimal` with a remainder correction. The choice is made in the step.
  - Not `double`, which is inexact. Not `BigInteger`, which allocates on every 4 Hz tick.
  - It must keep today's exact rounding: the baseline is floored, so it lands at most one tick per
    unit early.
- **Capacity 0 is defined.** It means no production, with no division by zero. A business type with
  no units owned has capacity 0.
- **Touches.**
  - New: `Shared/Accrual/` and its tests.
  - Changed: `TradeResourceState.cs`, the `KingdomRuler.Systems.asmdef` reference, and the
    `ARCHITECTURE.md` §3 (Systems → Shared) and §4.3/§4.5 sentences that describe the arithmetic.
- **`MaxWarehouseCapacity` stays** (behaviour-preserving). Only its stated reason changes: it is a
  tuning sanity ceiling, no longer an arithmetic limit (decided, §11).
- **Tiers.** 🟡 new `Shared/` folder; 🟡 asmdef reference.
- **Done.**
  - The helper is tested directly. Its tests cover:
    - exactly one period to fill at capacities from 1 up to the gold ceiling (§5);
    - many 250 ms ticks giving the same result as one absence;
    - capacity 0;
    - progress kept across a capacity change;
    - a backwards clock;
    - a full store banking nothing;
    - the read-outs at the ceiling.
  - Every existing `TradeResourceRegenTests` case passes **unchanged**, and `TradeManagerTests`
    passes too.
- **Verify.**
  - Compile clean.
  - EditMode: 469 + the new helper tests, all passing.
  - No Play mode needed (no UI change). An optional smoke from Bootstrap would show the Trade tiles
    still filling.

### Step 2 — The gold ceiling ✔

> **Done 2026-11-12.** 508 EditMode tests pass (498 + 10 new). As planned, with one detail:
> `AddGold` of nothing — zero, or at the ceiling — now publishes no `GoldChangedEvent`, as
> `AddTradeResource` already did. No game code subscribed to the event, so nothing depended on the
> empty message. `WholeUnitAccrualTests` now take their ceiling from `KingdomLedger.MaxGold`.


- **Goal.** Add `KingdomLedger.MaxGold`, so the balance can never overflow and so Economy's storages
  have a known upper bound.
  - `AddGold` clamps at the ceiling and returns how much fitted, mirroring `AddTradeResource`.
  - `LoadFromDto` clamps a saved balance to it.
  - `SetGold` validates against it.
  - Crystals stay as they are.
- **Touches.** `KingdomLedger.cs`, `KingdomLedgerTests`, `KingdomLedgerSaveTests`, and
  `ARCHITECTURE.md` §4.3.
- **Tiers.** 🟡 `KingdomLedger` public API change (`AddGold`'s return value, the new constant).
- **Done.**
  - Tests cover:
    - adding up to the ceiling, past it, and at it;
    - the event carrying the real delta;
    - a saved balance above the ceiling, or negative, loading clamped.
  - Existing gold tests are unchanged.
- **Verify.** Compile clean; EditMode count +N, all passing.

### Step 3 — Data types, content assets and String Tables ✔

> **Done 2026-11-12.** 516 EditMode tests pass (508 + 8 new). What turned out differently:
> - **`MaxUnitsPerType` is a constant on `EconomyConfig`**, not on a model class. The content test
>   needs it now, and it bounds the authored rates and `StorageHours` beside it, the way
>   `TradeConfig` holds its share bounds. It is documented as a safety ceiling, not a tunable.
> - **The Bootstrap asmdef reference waits for step 4**, the first step that uses the module from
>   there. Only the EditMode test asmdef references Economy now.
> - **One more content check:** every `BusinessDefinition` asset must be listed on the config,
>   since an unlisted one never appears in the game.
> - Rates and prices are `long`, clamped to ≥ 1 in `OnValidate`, because `[Min]` does not apply
>   to `long`.
> - **The ordering test was proven to fail.** A tavern saved at 50 gold failed it with a readable
>   message; the asset was then restored byte-identical.
> - `EconomyUITable` exists with no entries yet. Step 6 fills it.


- **Goal.**
  - Create the Economy module skeleton: folders, the `KingdomRuler.Modules.Economy` asmdef, and the
    references from the Bootstrap and EditMode test asmdefs.
  - Write `BusinessDefinition` and `EconomyConfig`.
  - Author `EconomyConfig.asset` and the six business assets (§5).
  - Create `BusinessesTable` and `EconomyUITable` in `en` and `uk`, the latter empty for now.
  - Add `EconomyContentTests`.
- **Keys** are derived from the id: `{BusinessId}.name` and `{BusinessId}.flavor` in
  `BusinessesTable`. `BusinessDefinition` owns the table name and the derivation, exactly as
  `LawCardDefinition` does. **`BusinessId` is a save key**, so renaming it orphans both the text and
  the player's units; its tooltip says so.
- **Touches.** Only the new module folder, the two asmdefs, and String Tables (through
  `LocalizationEditorSettings`, not YAML).
- **Tiers.** 🟡 new module and new ScriptableObject types.
- **Done.** `EconomyContentTests` loads the real assets and checks:
  - unique non-empty ids;
  - no empty slots;
  - name and flavor present in every locale;
  - price ≥ 1 and rate ≥ 1;
  - `rate × storage hours × MaxUnitsPerType` ≤ `MaxGold`, so no authored business can exceed the
    ceiling;
  - price ≤ `MaxGold`;
  - the authored list in ascending price, which is the display order;
  - missing artwork reported as one grouped warning, not a failure (`ARCHITECTURE.md` §8).
- **Verify.** Compile clean; EditMode count +N, all passing.

### Step 4 — Model, save and bootstrap wiring, with tests ✔

> **Done 2026-11-12.** 604 EditMode tests pass (516 + 88 new), plus a Play-mode smoke from
> Bootstrap. What turned out differently:
> - **Every storage settles on every tick**, like warehouse regen, rather than only on load, buy
>   and collect. It is simpler, it resynchronises a device clock moved back mid-session, and step 1's
>   rounding bound (accepted) covers the drift.
> - **`StoragesFilled` is a no-argument event raised at most once per tick.** The coalescing lives
>   in the Manager, so the Presenter only plays the sound. The rows read "full" by polling.
> - **The top-up skips the gold spend when the balance is already 0**, so it never publishes an
>   empty gold change. A saved id that no longer exists logs a warning.
> - **The converter is `WholeNumberLongJsonConverter`.** Beyond decimals it saturates numbers
>   outside `long` (a `BigInteger` or `1e30`), and each field's load then clamps them.
> - **`Bootstrap.unity` gained one line**: `GameBootstrapper._economyConfig`, assigned through
>   MCP. The Bootstrap asmdef now references Economy. The Presenter is registered in step 6.
> - **Play mode:**
>   - Your schema-8 save loaded with no errors or warnings; Economy started on top of it with
>     100 gold added and the crystals untouched.
>   - On exit the file had `schemaVersion` 9 and an `economy` section for all six businesses.
>   - A second boot from that save granted nothing more.
>   - The save was then restored byte-identical, and `ProjectSettings/` is unchanged.
> - **Two follow-ups the user approved, done the same day.** 619 EditMode tests pass (604 + 15).
>   - `WholeNumberJsonConverter` now clamps an integer beyond `int` instead of throwing, which
>     set the whole save aside. `WholeNumberJsonConverterTests` covers it, plus a whole Ledger
>     section with one oversized amount.
>   - The three identical per-module `FakeClock` copies are now one, in `TestServiceFakes`.


- **Goal.** Build the mechanic in plain C#:
  - `BusinessProduction` (the seam);
  - `BusinessPricing`;
  - `BusinessHolding`, built on `WholeUnitAccrual`;
  - `EconomyManager`.

  Then add the save DTOs and the schema bump, and wire both into `GameBootstrapper` and
  `GameStateCoordinator`.
- **What the Manager owns.**
  - **Lifecycle.** `InitializeNewGame` grants exactly the cheapest business's price, read from the
    content; `LoadFromDto` and `ToDto` handle the save.
  - **The clock.** Its constructor subscribes `AdvanceTo` to `Ticked`, and raises a plain
    `StorageFilled` event when a storage crosses to full. Prefer detecting that crossing from each
    storage's time-until-full over settling every storage on every tick. Settling only on load, buy
    and collect keeps step 1's rounding sliver per player action instead of per tick. Decide in
    the step.
  - **Every transaction, each price beside its charge:**
    - buy *n* for gold;
    - Max, as many as affordable, clamped to the units ceiling;
    - the crystal top-up;
    - collect.
- **Rules.**
  - **Buying** settles first, then raises the rate and capacity. Elapsed time toward the coin in
    progress carries over.
  - **Collecting** settles first, then moves what fits under `MaxGold`. Anything above the ceiling
    stays in the storage, so nothing is silently lost.
  - **The crystal top-up is one transaction.** It is recommended to spend the player's whole gold
    balance plus the crystals, which is exactly equivalent to "add the missing gold, then pay the
    price".
    - This publishes one `GoldChangedEvent`, not two. No subscriber sees the intermediate balance.
    - It cannot touch the ceiling.
    - The crystal price is computed in `long`, so a price above `int.MaxValue` is simply
      unaffordable.
    - A refused transaction changes nothing.
- **Save.** See §7. `EconomyStateDto` and `BusinessStateDto` are added, `GameStateDto` gains an
  `economy` section, and `CurrentSchemaVersion` goes from 8 to 9. A long whole-number converter is
  added and applied to the new long fields and to `LedgerStateDto.Gold`. No decimal gold is written
  today, so this only guards hand-edited or damaged files. It costs one attribute and makes gold load
  as defensively as every other number. Check that `link.xml` and `[Preserve]` cover the new
  converter.
- **Touches.**
  - The module's `Model/`.
  - `Systems/Save/Scripts/Dto/*` and the converter.
  - `GameBootstrapper` and `GameStateCoordinator`.
  - Tests.
  - `ARCHITECTURE.md` §4.5: Economy joins the tick table, and the "settles first" rule now names
    accrual generally.
- **Tiers.**
  - 🟡 the save DTO shape change and `schemaVersion` bump.
  - 🟡 composition-root registration of the config, Manager and Presenter (the Presenter is
    registered in step 6).
- **Done.** `EconomyManagerTests`, `BusinessPricingTests` and `BusinessProductionTests` are written
  and pass (§8).
- **Verify.**
  - Compile clean; EditMode count +N, all passing.
  - **A Play-mode smoke from Bootstrap**, because the save and boot paths changed:
    - back up the save first;
    - the existing schema-8 save loads with no errors;
    - after a focus loss the save file has `schemaVersion` 9 and an `economy` section;
    - restore the backup byte for byte;
    - reset `runInBackground`, and confirm `git diff ProjectSettings/` is empty.

### Step 5 — Number abbreviation ✔

> **Done 2026-11-12.** 655 EditMode tests pass (619 + 36 new). What turned out differently:
> - **Two types, not one.** `NumberSuffixes` holds the four localized formats and owns their table
>   and keys. `NumberFormat` holds `Abbreviate(value, suffixes, culture)` and
>   `DisplayedValue(value)`. A test checks across 5,000 random pairs that equal `DisplayedValue`
>   means equal text.
> - **Trailing zeros are dropped:** `10K`, `1.2M`, not `10.0K`, `1.20M`.
> - **Negatives truncate toward zero** (`-12.3K`), and `long.MinValue` cannot overflow.
> - **Past 999T a number still shows, grouped** (`9,223,372T`). Only values beyond the gold
>   ceiling reach that.
> - **Keys:** `number.suffix.{thousand,million,billion,trillion}` in `SharedTable`, plain strings
>   (not Smart). English `{0}K/M/B/T`; Ukrainian `{0} тис.`, `{0} млн`, `{0} млрд`, `{0} трлн`.
> - **A suffix without `{0}`** (a missing entry's `[key]`) still shows the number. A test also
>   fails any locale whose suffix lacks `{0}`.


- **Goal.** Add `Shared/Text/NumberFormat`: whole numbers formatted for display.
  - Below 10,000 they are shown exactly, with grouping.
  - From 10,000 they are abbreviated to three significant digits, **truncated, never rounded up**,
    so a displayed amount never overstates what the player has: 12.3K, 123K, 1.23M … 999T.
  - The decimal separator follows the culture, and the suffix *formats* (`{0}K`, `{0} тис.`) are
    passed in, already localized.
  - It also exposes a cheap way to tell whether two values display the same, so views can skip
    re-resolving text (§8, risk 6).
- **Touches.**
  - `Shared/Text/`.
  - The suffix entries in **`SharedTable`** (`Systems/Localization/Tables/`), in `en` and `uk`. The
    header and Cities will need the same words.
  - `NumberFormatTests`, plus a check that every suffix key exists in every locale.
  - The `ARCHITECTURE.md` §2 table row for `SharedTable`.
- **Tiers.** 🟢. A new file in an existing `Shared/` folder, and new entries in an existing table.
- **Done.** Tests cover:
  - every boundary (9,999 / 10,000, each suffix change);
  - truncation (`1,999,999` → `1.99M`);
  - the ceiling;
  - 0;
  - two cultures;
  - suffixes with and without a space.

  Trade keeps its `N0` numbers.
- **Verify.** Compile clean; EditMode count +N, all passing.

### Step 6 — Presenter and view data, with tests ✔

> **Done 2026-11-12.** 682 EditMode tests pass (655 + 27 new). What turned out differently:
> - **Opening the panel moves to step 7.** `OpenBusiness` (the `Show` + `WaitForChoice` loop) needs
>   the `BusinessPopup` view class, and that view and its `BusinessPopupChoice` belong with the
>   prefab. It opens a popup, so EditMode could not test it either way. Everything else is here:
>   `GetRow`, `GetBusiness`, `Collect`, `Buy`, the top-up confirmation, sounds, visibility.
> - **`Buy(id, BuyAmount)` returns a `UniTask`**, so the panel loop can await a top-up
>   confirmation before its next press. `BuyAmount` is `One / Ten / Max`.
> - **Views get text as the screen needs it.** `Amount(long)`, `BusinessName(id)` and
>   `UnitsOwnedText(int)`, plus `Gold` for the temporary total. The storage line is
>   `Amount(stored) + " / " + Amount(capacity)`, composed in the view, as Trade's tiles do.
> - **The suffixes are resolved once and cached**, then dropped on `LocaleChanged`, which the
>   localization service also raises once at startup. A test pins that the startup placeholders
>   do not stick.
> - **The panel's buy buttons:**
>   - An option past the units ceiling is inactive.
>   - An unaffordable one stays tappable, because it offers the top-up, and is flagged
>     (`AffordsOne/Ten/Max`) to be drawn muted.
>   - At the ceiling, "Maximum owned" replaces the prices.
> - **`EconomyUITable` keys:** `ui.units_owned`, `ui.rate`, `ui.buy_max`, `ui.maxed`,
>   `ui.topup.title`, `ui.topup.body`, `ui.not_enough_crystals`, in `en` and `uk`. The Ukrainian
>   top-up line avoids a number-then-noun plural.
> - **Docs updated now, not in step 8:** `ARCHITECTURE.md` §4.7 states the confirmation rule. In
>   `Popups.md`, §1's business-panel example confirms only the crystal top-up, and §5 is scoped
>   the same way.
> - **A Play-mode check** resolved the registered Presenter from the real container: six rows, and
>   the Ukrainian name, owned and rate text all rendered. The save was restored byte-identical.


- **Goal.** Build `EconomyPresenter`, `EconomyUIText` (with `AllKeys`) and the view-data types:
  - `BusinessRowViewData`: string-free, read every frame;
  - `BusinessPopupData`: `IEquatable`, re-read at 4 Hz;
  - the gold-total read-out.

  Register the Presenter in `GameBootstrapper`.
- **Flows.**
  - **Open a business.** `BusinessPopup.Show(() => GetBusiness(id))` and a `WaitForChoice` loop
    (`Popups.md` §1): Collect, Buy ×1, Buy ×10, Max.
  - **Buy for gold.** On the tap, with no confirmation:
    - the Manager charges;
    - one sound and one haptic;
    - a `BusinessBought` event for the views to animate.
  - **Short of gold.** `ConfirmPopup.Ask` with the live crystal price and the missing gold.
    - The notice is shown and Confirm is inactive when crystals are short.
    - `closeWhen` fires when the shortfall disappears.
    - On Confirm, the Manager re-reads the price.
  - **Collect.** One `Collect(id)` command, called from both the row's button and the panel's.
    One sound, no haptic, and a `Collected(id, amount)` event for whichever view animates it. A
    collect from an empty storage does nothing and plays nothing.
  - **`StorageFilled`.** The full sound plays only while the screen is visible, at most once per
    tick even if several storages fill together.
  - **Language change.** Re-render on `LocaleChanged`.
- **Touches.** The module's `UI/` (no Views yet), `EconomyUIText` entries in `EconomyUITable`
  (`en` and `uk`), `GameBootstrapper`, and tests.
- **Tiers.** 🟢 within the established pattern; the registration was already approved with step 4.
- **Done.**
  - `EconomyPresenterTests` cover row data, popup data and its equality, and buy-on-tap. The
    gold-buy path opens no popup, so it **is** EditMode-testable: its sound, haptic and the Ledger
    change are all checked.
  - They also cover:
    - collect feel, which is the same from the row and from the panel;
    - the row data saying whether there is anything to collect;
    - visibility gating, the burst coalescing and locale re-render;
    - "opening anything spends nothing".
  - `EconomyContentTests` walks `EconomyUIText.AllKeys` in every locale.
- **Verify.** Compile clean; EditMode count +N, all passing. The crystal top-up waits on a popup
  answer, so it is verified in step 7's Play-mode pass.

### Step 7 — Prefabs, the screen in `Main`, the nav tab, and a full Play-mode pass ✔

> **Done 2026-11-12.** 683 EditMode tests pass (682 + 1), and a full Play-mode pass from Bootstrap
> succeeded, with every tap a real `EventSystem.RaycastAll` + `ExecuteEvents`.
>
> **Built:**
> - Views: `EconomyScreenView`, `BusinessRowView`, `BusinessPopup` (+ `BusinessPopupChoice`), and
>   `GoldTotalView`, which is temporary.
> - `EconomyPresenter.OpenBusiness`: the panel loop that awaits `Buy`.
> - Prefabs: `EconomyScreen` (six nested `BusinessRow` instances, bound by id; its own canvases for
>   the gold total and the scrolling list), `BusinessRow` and `BusinessPopup`.
> - Five chrome keys in `EconomyUITable`: `ui.collect`, `ui.buy_one`, `ui.buy_ten`, `ui.full`,
>   `ui.gold`.
> - `Images/BarFill.png`, copied with its own GUID.
> - `BusinessPopup` in `PopupRegistry.asset` (4 → 5).
> - `Main.unity`: the screen instance and the Economy tab's `_screen`, 127 lines added and none
>   removed. The stale `_screens` overrides on the nav bar were left alone.
>
> **Verified in Play mode** (Ukrainian, 9:16, then 3:4 and 9:20):
> - The tab opens the screen.
> - A row tap opens the panel.
> - Buy ×1 charged on the tap, with one sound and one haptic.
> - With 0 gold, Buy ×1 opened the top-up for 1 crystal. Confirm charged exactly that, bought the
>   unit, closed only the confirmation and played one sound and one haptic.
> - Short of crystals: the notice shows and Confirm is inactive (pressing it does nothing); a tap
>   outside closed only the confirmation.
> - Max bought as many as affordable, with no question.
> - Collect in the panel settled to the moment and floated the amount: sound, no haptic.
> - A tap outside closed the panel.
> - The row's Collect collected without opening the panel.
> - A storage filling played the full sound and showed the marker while visible, and was silent on
>   the Trade tab.
> - The language switched at runtime: chrome, code-built text and suffixes together.
> - The layout fits at all three ratios.
> - There were no errors or warnings, and the sound and haptic log matches §6.
> - The save was restored byte-identical, `runInBackground` reset, and the Game view left on 9:16.
>
> **What turned out differently:**
> - **A defect, fixed:** the Max caption showed its count in full ("Макс. ×144130"), which
>   overflowed the button. Unit counts in `ui.buy_max` and `ui.topup.body` now go through
>   `Amount()`, with a test.
> - **The tick:** `EconomyManager.AdvanceTo` costs ~0.6 µs, measured directly with a 10,000-unit
>   storage landing coins every call. Everything allocates nothing. `ARCHITECTURE.md` §4.5 is
>   updated.
> - **The gold total punches on a rise**, rather than counting up, which would format a string
>   every frame of the count.
> - **Seen, not changed** (for step 8's known gaps):
>   - The decimal separator follows the device culture, so the English UI shows "14,4M" on a
>     Ukrainian machine (risk 10).
>   - A business with no units shows its storage as "0 / 0".
>   - The disabled Collect tint in the panel is subtle.
> - **A trap when testing:** a popup is not raycastable in the frame it opens. A tap sent in the
>   same frame falls through to whatever is underneath, so taps must wait a frame.


- **Goal.** Build the Views and prefabs: `EconomyScreenView`, `BusinessRowView`, `BusinessPopup`
  (started as a copy of `ConfirmPopup.prefab`) and `GoldTotalView`.
  - Add `BusinessPopupChoice` (Collect, BuyOne, BuyTen, BuyMax) and `EconomyPresenter.OpenBusiness`,
    the panel's `Show` + `WaitForChoice` loop that awaits `Buy` (moved here from step 6).
  - Add `BusinessPopup` to `PopupRegistry.asset`.
  - Place an `EconomyScreen` instance in `Main` and assign the Economy tab's `NavTabButton._screen`.
  - Wire the fixed chrome through `LocalizeStringEvent`, and the DOTween feel (§6).
- **Layout.**

  ```
  EconomyScreen (Canvas)
  ├── Background            full-bleed
  └── SafeArea              SafeAreaFitter, padding.bottom 160
      ├── GoldTotal          own Canvas + GraphicRaycaster: it ticks up on every collect
      └── BusinessList       own Canvas + GraphicRaycaster: ScrollRect; the row fills change all the time
          └── BusinessRow ×6 art, name, units owned, storage fill bar, a "full" marker,
                             a Collect button (shown while the storage holds gold),
                             and the row button behind it, which opens the panel
  ```

  - Rows are matched to businesses **by id**, never by array order.
  - **The Collect button sits above the row's own button**, so a tap on it reaches only Collect and
    never opens the panel. Verified with a real raycast.
  - Every fill bar uses Economy's own copy of `BarFill.png`.
  - A missing artwork hides its slot.
- **Touches.**
  - The module's `Prefabs/` and `UI/` Views.
  - `PopupRegistry.asset`.
  - **`Main.unity`**: one scene edit, made through MCP's scene tools.
  - `EconomyUITable` chrome entries.
  - `Modules/Economy/Images/BarFill.png`, copied through the AssetDatabase so it gets a GUID of its
    own. Trade's copy is untouched.
  - **GDD §8**: "Collecting … happens in the business panel" becomes "on the business's row and in
    its panel".
  - GDD §13 build status, where Economy becomes a built screen.
- **Tiers.**
  - Scene edit: coordinate first, one person per scene. Confirm nobody has `Main` open.
  - The BottomNavBar instance in `Main` carries stale `_screens.Array…` overrides for a field that no
    longer exists. *Remove Unused Overrides* would clear them, but it is a scene edit beyond the
    task, so it waits for the user's say-so.
- **Done.** The whole mechanic is playable from Bootstrap, and the verification list in §8 is
  ticked.
- **Verify.**
  - Compile clean; EditMode all passing.
  - **A full Play-mode pass:**
    - back up the save and restore it afterwards;
    - `runInBackground` reset afterwards;
    - every tap through `EventSystem.RaycastAll` and `ExecuteEvents`;
    - sound and haptic counts read from the stub services' `Editor.log` lines;
    - layout checked at `KR 3:4`, `KR 9:16` and `KR 9:20`, and left on 9:16;
    - both locales, switched at runtime;
    - the full clock tick re-measured with Economy's subscriber, for time and allocation, so the
      `ARCHITECTURE.md` §4.5 figure can be updated.

### Step 8 — Docs ✔

> **Done 2026-11-12.** No code changed; 683 EditMode tests still pass.
> - **`docs/modules/Economy.md`** follows `Trade.md`'s outline.
> - **Its known gaps (§9) record the three points the user chose not to fix yet:**
>   - the decimal separator following the device rather than the selected language;
>   - "0 / 0" on an unowned business;
>   - the subtle disabled Collect tint.
>
>   Each says what fixing it would take.
> - **The consistency pass:**
>   - `ARCHITECTURE.md` §2 (an Economy row in the table list), §3 (the module's tree line, and
>     `Economy.md` in the docs list), and §4.3 (Economy's pricing and rate math are module-owned;
>     the fill is shared).
>   - GDD §8: the placeholders point to `Economy.md` §4, and the panel's Collect is "active when
>     it holds gold".
>   - The earlier steps had already updated the rest of §9's table.
> - **Verify:** a script checked every code identifier named in `Economy.md` against the project,
>   and all exist.
> - **Not Economy's, done with the user's go-ahead:**
>   - *Remove Unused Overrides* on the `BottomNavBar` instance in `Main.unity` dropped exactly the
>     four stale `_screens.Array…` overrides.
>   - It exposed a second problem: `BottomNavBar.prefab`'s `SafeArea` held stale anchors
>     (0, 0.21)–(1.83, 4.98). The Edit-mode `SafeAreaFitter` re-created (0,0)–(1,1) overrides
>     every time `Main` was opened. The prefab now holds (0,0)–(1,1) and the overrides are gone;
>     a fresh open of `Main` stays clean.
>   - The rule is now in `ARCHITECTURE.md` §4.6.


- **Goal.** Write `docs/modules/Economy.md` in the `Laws.md`/`Trade.md` outline:
  1. the mechanic in one paragraph;
  2. structure;
  3. classes;
  4. the storage model and its numbers;
  5. data flow (startup, a tick, buy, top-up, collect, save/load);
  6. rules that must hold;
  7. how to extend;
  8. tests and the Play-mode record;
  9. known gaps.

  Then do the full consistency pass on the other docs (§9).
- **Touches.** Docs only.
- **Tiers.** 🟢.
- **Done.** The docs describe the code as it is, with no history, and the confirmation rule is
  explicit everywhere it is stated.
- **Verify.** Read-through. Every class, file and rule named in the docs exists as named, checked by
  grep.

---

## 4. Tier flags at a glance

| Step | Flag | What needs a go-ahead |
|---|---|---|
| 1 | 🟡 | new `Shared/Accrual/` folder; `KingdomRuler.Systems` → `KingdomRuler.Shared` asmdef reference |
| 2 | 🟡 | `KingdomLedger` API: `MaxGold`, `AddGold` returning what fitted |
| 3 | 🟡 | new module `Modules/Economy`; new ScriptableObject types `BusinessDefinition`, `EconomyConfig` |
| 4 | 🟡 | save DTO shape + `schemaVersion` 8 → 9; new converter; composition-root registrations |
| 7 | scene | `Main.unity` edit (screen instance, the nav tab); `PopupRegistry.asset` |

Nothing here touches `IPurchasingService` (🔴). The crystal top-up spends crystals through the
Ledger only. No new package is needed.

---

## 5. Defaults — accepted as placeholders, to be tuned in playtest

The user accepted these on 2026-11-11. They stay **[ASSUMED — CONFIRM]** in the GDD until playtest
settles them.

### Businesses

The numbers are tuned so that a business's payback shortens slightly as it gets more expensive.
With flat prices, that is the only thing that makes the higher tiers worth buying (see risk 1).

| # | `BusinessId` | Price per unit | Gold / hour per unit | Payback | Storage, 1 unit (24 h) |
|---|---|---:|---:|---:|---:|
| 1 | `business.bakery`   | 100        | 60         | 1 h 40 m | 1,440 |
| 2 | `business.tavern`   | 1,500      | 1,000      | 1 h 30 m | 24,000 |
| 3 | `business.tailor`   | 20,000     | 15,000     | 1 h 20 m | 360,000 |
| 4 | `business.market`   | 250,000    | 210,000    | ≈ 1 h 11 m | 5,040,000 |
| 5 | `business.jeweller` | 3,000,000  | 2,800,000  | ≈ 1 h 04 m | 67,200,000 |
| 6 | `business.bank`     | 40,000,000 | 40,000,000 | 1 h 00 m | 960,000,000 |

### Config and ceilings

- **`EconomyConfig.StorageHours` = 24** (GDD §8). This is the helper's period.
- **`EconomyConfig.GoldPerCrystal` = 100.**
- **Ceilings:**
  - `KingdomLedger.MaxGold` = **999,999,999,999,999**, just under 10¹⁵, so `T` is the largest suffix
    ever needed;
  - `MaxUnitsPerType` = **1,000,000**, an Economy constant.
  - At the units ceiling the largest storage is a bank's: 40 M × 24 × 10⁶ = 9.6 × 10¹⁴, under
    `MaxGold`. The content test enforces this for every business.
  - The helper is then exact for elapsed ≤ 24 h × capacity ≤ 10¹⁵. That is ≈ 8.6 × 10²⁶ in ticks,
    which needs the wider intermediate from step 1. `decimal` holds 7.9 × 10²⁸.

### Abbreviation scheme

| Value | Shows | Value | Shows |
|---|---|---|---|
| 0 – 9,999 | exact, grouped (`9,999`) | 10⁹ – 10¹² | `1.23B` / `12.3B` / `123B` |
| 10,000 – 999,999 | `12.3K` / `123K` | 10¹² – MaxGold | `1.23T` … `999T` |
| 10⁶ – 10⁹ | `1.23M` / `12.3M` / `123M` | | |

- Values are truncated, never rounded up.
- Ukrainian suffixes are proposed as `тис.`, `млн`, `млрд` and `трлн`.

### The arithmetic

- **First purchase.**
  - A new game starts with 100 gold.
  - The player buys a bakery at once: 0 gold, 60 gold an hour, one coin a minute.
  - A 5-minute first session sees about 5 coins land in the storage.
- **An hour away earns 60 gold**, short of a second bakery. The second bakery is affordable
  **1 h 40 m** after the first. Taking it 40 minutes early costs `ceil(40 ÷ 100)` = **1 crystal**.
- **A first day of four sessions** (start, +4 h, +8 h, then 12 h overnight):

  | Session | Collected | Spent on | Result |
  |---|---:|---|---|
  | +4 h | 240 | 2 bakeries | 3 bakeries, 180/h, 40 left |
  | +8 h | 720 | 7 bakeries | 10 bakeries, 600/h, 60 left |
  | overnight | 7,200 | 4 taverns (6,000) | the **second business type on the first morning** |

- **Crystal top-up examples.**
  - ×10 bakeries cost 1,000. Holding 640, the player is 360 short → `ceil(3.6)` = **4 crystals**.
  - Max holding 40 gold offers one bakery: 60 short → **1 crystal**.
- **Late game.** At a fixed 100 gold a crystal, a bank-sized shortfall of 10 M is 100,000 crystals.
  The top-up is effectively an early- and mid-game convenience (risk 9).

---

## 6. Feel

One haptic per action, never stacked. Passive sounds play only while the Economy screen is visible.
DOTween lives in the Views only; the Presenter raises sound and haptics.

| Action | Sound | Animation | Haptic |
|---|---|---|---|
| Collect, from the panel | `sfx_economy_collect` | the panel's storage bar drains; a "+1.23K" float rises from it; the gold total ticks up (its own canvas) | **none** |
| Collect, from the row | `sfx_economy_collect` | the row's bar drains, the float rises from the row, the gold total ticks up, the Collect button hides | **none** |
| Buy ×1 / ×10 / Max, for gold, on the tap | `sfx_economy_buy` | the owned count punches; the rate and the storage capacity tween to their new values | 1 |
| Crystal top-up confirmed | `sfx_economy_buy_crystals`: one sound, not buy + crystal | as a buy, after the confirmation closes | 1 |
| Not enough gold | — beyond `PopupSystem`'s open sound | the top-up confirmation opens | — |
| Short of crystals in the top-up | — | the notice line; Confirm inactive | — |
| Refused at confirm (a race: the price or balance changed in the last instant) | `sfx_economy_refused` | — | — |
| At the units ceiling | — | buy buttons inactive, "maximum owned" | — |
| A storage fills (passive) | `sfx_economy_business_full`, at most once per tick | the row's "full" marker pops in | — |
| Opening the panel / any popup | `PopupSystem`'s | panel scale-in | — |

- SFX ids are strings in the Presenter, like Laws and Trade, until the audio system exists.
- Every tween runs on unscaled time and is killed in `OnDisable`/`OnDestroy`, so a tab switch never
  leaves a bar half-drained.

---

## 7. Save

**Per business**, keyed by `BusinessId` in `EconomyStateDto`:

| Field | Type | Restores |
|---|---|---|
| `owned` | int | units owned. Capacity and rate are **derived** from it through the current content, so a retuned rate reaches existing saves (like Trade's levels). |
| `stored` | long | gold in the storage |
| `lastUpdatedUtc` | ISO-8601 string | when the coin in progress started. Offline accrual depends on it. |

- **`schemaVersion` 8 → 9**, recorded per `ARCHITECTURE.md` §5. There are no migrations before the
  first external build.
- **A schema-8 save still loads.** It has no `economy` section, so Economy starts as a new game for
  that player and grants the starting gold. This is wanted: the existing save gets the cheapest
  business's price to try the screen with.

**Defensive load.** Never fail the whole save:
- an unknown `BusinessId` is skipped, and a business absent from the save has 0 owned;
- `owned` is clamped to `0..MaxUnitsPerType`;
- `stored` is clamped to `0..capacity`, so a retuned smaller rate or a hand-edited value cannot
  exceed the storage;
- decimals like `12.0` are read as whole numbers by the long converter; `NaN` or garbage becomes 0;
- a missing or unreadable timestamp becomes `now`, which loses at most the coin in progress and never
  grants gold;
- a future timestamp resynchronises to `now`, as warehouse regen does;
- the gold balance is clamped to `MaxGold` (step 2);
- after restoring, the Manager settles offline accrual once. Storages already full on load raise no
  `StorageFilled`, and no sound plays at startup.

**IL2CPP.** The new converter needs `[Preserve]`, like `WholeNumberJsonConverter`. On-device
save/load is flagged as unverified until a real device build (an existing known gap).

---

## 8. Tests

| Step | Adds | Covers |
|---|---|---|
| 1 | `WholeUnitAccrualTests` | exact fill at 1…10¹⁵; many ticks equal one absence; capacity 0; capacity change keeps progress; backwards clock; full banks nothing; read-outs at the ceiling. All `TradeResourceRegenTests` unchanged. |
| 2 | `KingdomLedgerTests`, `KingdomLedgerSaveTests` additions | `AddGold` at, past and up to the ceiling; event delta; load clamps |
| 3 | `EconomyContentTests` | the real assets: ids, text in every locale, positive numbers, ceilings respected, price order, artwork (warning) |
| 4 | `EconomyManagerTests`, `BusinessPricingTests`, `BusinessProductionTests` | see below |
| 5 | `NumberFormatTests` | boundaries, truncation, every suffix, the ceiling, cultures, suffix formats |
| 6 | `EconomyPresenterTests`, + `EconomyUIText.AllKeys` in `EconomyContentTests` | view data, equality, buy-on-tap feel and Ledger effect, collect feel, visibility gating, burst coalescing, locale re-render, nothing spent on open |

**What step 4's tests cover:**
- a new game grants exactly the cheapest price;
- **quoted == charged** for ×1, ×10 and Max, and for the crystal top-up;
- the Max count, the units ceiling, and Max with nothing affordable resolving to 1;
- settle-before-buy, and the partial coin carrying over;
- collect empties and credits;
- collect at the gold ceiling leaves the rest in storage;
- a full storage stops;
- `StorageFilled` is raised once per transition;
- crystal pricing: ceil, minimum 1, long-safe;
- a refused top-up changes nothing; the net Ledger effect and the event count of the top-up;
- the clock subscription;
- the save round trip through real JSON;
- offline accrual on load;
- every defensive-load case in §7.

**Only Play mode can verify** (MonoBehaviour, DOTween and popup answers; step 7):
- a business tap resolves through the raycaster and opens the right panel;
- the row's Collect button collects without opening the panel. It is hidden while the storage is
  empty, and reappears as gold lands;
- Collect in the panel drains the storage, and the gold total ticks up;
- buy ×1, ×10 and Max charge on the tap with one sound and one haptic each;
- the short-of-gold flow:
  - the confirmation shows the right crystal price and missing gold;
  - Confirm charges exactly that and completes the purchase;
  - opening it or tapping outside charges nothing;
  - short of crystals shows the notice with Confirm inactive;
- the panel stays open and redraws across purchases and collections;
- outside taps close the confirmation first, then the panel;
- the full sound plays on the visible screen and not on another tab;
- the numbers abbreviate correctly in both locales, switched at runtime;
- layout at all three aspect ratios;
- the existing save loads; time away accrues while the app is closed.

---

## 9. Docs

**New.** `docs/modules/Economy.md`, in the `Laws.md`/`Trade.md` outline (step 8). Its key sections:
- the storage model, with the shared helper;
- the rate seam;
- the confirmation rule;
- the top-up transaction;
- the ceilings;
- the Play-mode record;
- known gaps: placeholder art, the temporary gold widget, no header, culture vs locale separators,
  Ukrainian plurals.

**Updated**, each in the step that changes it:

| Doc | Section | Change |
|---|---|---|
| `ARCHITECTURE.md` | §3 | Economy built, with its tree; `Shared/Accrual/`; `NumberFormat`; "`KingdomRuler.Systems` references only packages" becomes "packages and Shared"; `docs/modules/Economy.md` listed |
| | §2 | `EconomyUITable` / `BusinessesTable` in the table list; the number suffixes in `SharedTable`; `EconomyContentTests` named |
| | §4.3 | `MaxGold`; regen arithmetic via `WholeUnitAccrual`; `MaxWarehouseCapacity`'s reason |
| | §4.4 | Economy among the Managers |
| | §4.5 | `EconomyManager.AdvanceTo` in the tick table; "whoever decides from an accruing amount, or changes a rate, settles accrual first" generalised beyond trade amounts |
| | §4.6 | which screens are built |
| | §4.7 | **the confirmation rule.** "The tap spends nothing" becomes: crystal purchases are always confirmed and charged after Confirm; gold purchases of businesses happen on the tap |
| | §8 | content tests list |
| `Popups.md` | §1 | the business-panel sketch currently confirms a *gold* buy, which contradicts GDD §8. Rewrite it to buy on the tap and confirm only the crystal top-up |
| | §3 | `BusinessPopup` among module popups |
| | §5 | "Money waits for the choice" scoped to crystal and Trade purchases |
| `Trade.md` | §4, §6.1, §8 | regen arithmetic now in the shared helper; the tests list. Trade's own "every purchase is confirmed" stays: it is true of Trade |
| `GDD.md` | §8 | Collect is on the row as well as in the panel (step 7) |
| | §13 | build status: Economy is built (greybox) |
| | §8 | checked against what was built, and the [ASSUMED] numbers updated to the final ones |

---

## 10. Risks

1. **Flat prices make growth exponential — the biggest balance risk.** This is not a reason to change
   the decision now, but it should be known before Cities' gold costs are authored.
   - With a flat price and linear production, reinvested income compounds at the best affordable
     payback rate. Each absence of *g* hours multiplies income by up to `1 + g ÷ payback`.
   - The higher tiers are only worth buying if their payback is *shorter*, so growth accelerates as
     the player climbs.
   - With the §5 numbers, a player checking in four times a day multiplies income by roughly
     ×100–×1,000 a day once reinvesting. They would reach the bank in about 3 days and the ceilings
     in about a week.
   - Longer paybacks only slow the base. A lively first hour and a long tail pull against each other.
   - The ×1.15 growing price (GDD §8, kept for later) is the documented lever. **Decided:** playtest
     the flat price first, and bring the multiplier back if needed (§11).
2. **Accrual overflow at gold scale.** This is mitigated by step 1's exact wide intermediate and its
   tests at 10¹⁵. The trap is a "fix" to plain `long` or `double` later; the helper's doc comment
   says why not.
3. **Behaviour drift in warehouse regen.** This is mitigated by keeping today's exact flooring and by
   every `TradeResourceRegenTests` case passing **unchanged**. No test is edited to fit.
4. **A sound burst on resume.** Several storages that filled while the app was backgrounded cross to
   full on the same first tick. They are coalesced to one sound per tick (step 6).
5. **Startup sounds.** A storage already full at load must not count as "became full". This is
   handled in `LoadFromDto` and tested.
6. **Allocation in steady polling.** Abbreviated text allocates. A bank storage changes every frame,
   so views must re-resolve text only when its *displayed* form changes (step 5's comparison).
   `BusinessRowViewData` stays string-free.
7. **Scene collision.** Step 7 edits `Main.unity`. Coordinate first; the stale `_screens` overrides
   on the nav bar are noted, not silently cleaned.
8. **Two tap targets on one row.** The Collect button sits on the row that opens the panel.
   - A mis-tap opens the panel instead of collecting, or the other way round. The button needs a
     comfortable phone-sized target and a clear gap from the row's edges.
   - The button must take the raycast before the row does.
   - Both are checked in Play mode with real raycasts (step 7).
9. **A fixed gold-per-crystal rate.** One crystal covers 100 gold whatever the stage, which is
   generous early and meaningless late. It is a balance point for playtesting; a scaling rate would
   be a GDD change.
10. **Culture vs locale.** Separators follow the device culture, not the selected language. This is
    an existing gap shared with Trade.
11. **IL2CPP.** The new DTOs and converter work in the Editor but are unverified on device until a
    real build.

---

## 11. Decisions (2026-11-11)

| # | Question | Decision | Where it lands |
|---|---|---|---|
| 1 | Flat-price growth (risk 1) | Playtest the flat price first; bring the ×1.15 growth back if needed | No step; risk 1 stays recorded |
| 2 | Abbreviation suffixes | In `SharedTable` now. The key constants are owned by `NumberFormat` | Step 5 |
| 3 | Starting gold for a save without an Economy section | Grant it: the existing save gets the cheapest price | Step 4, §7 |
| 4 | Fill-bar sprite | Copy `BarFill.png` into `Modules/Economy/Images/`; all textures get replaced later anyway | Step 7 |
| 5 | List order | As authored on `EconomyConfig`, with a content test requiring ascending price | Step 3 |
| 6 | Max | Buys as many as affordable without asking; the crystal offer only when none are affordable, for one unit | Step 4 |
| 7 | `MaxWarehouseCapacity` | Kept for now, as a sanity ceiling on Trade tuning | Step 1 |
| 8 | The §5 numbers | Accepted as placeholders | Steps 2–5 |
| 9 | Next-coin progress bar in the panel | Not built; the storage fill bar is enough | Step 7 |
| 10 | Decimal-tolerant gold in the Ledger save | Yes. No decimal gold is written today, but the converter the new fields need anyway costs one attribute on `LedgerStateDto.Gold` and closes the one number that would set the whole save aside | Step 4 |
| — | **New:** collect from the row | A Collect button on each business row, as well as in the panel. It collects without opening the panel | Steps 6–7; GDD §8 updated in step 7 |
