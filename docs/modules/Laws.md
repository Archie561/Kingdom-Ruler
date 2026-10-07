# Laws module — architecture

> Implements **GDD.md §6 — Law Enactment**. Read that first for *what* the mechanic does;
> this document covers *how it is built* and how to extend it safely.
>
> Project-wide rules live in `ARCHITECTURE.md`. This file only covers what is specific to
> Laws, and states the reasoning behind decisions that look arbitrary from the outside.

---

## 1. What the mechanic is, in one paragraph

The player holds up to 8 law cards but sees exactly one at a time. Swiping right accepts
it, left rejects it; **both apply effects** to 1–3 of the 6 characteristics — and the card
**never states which ones**, so the player judges it from the writing and finds out when the
bars move (§6.11). A resolved card frees a slot, which refills on a 2-minute real-time timer
that keeps running while the app is closed. Crystals can instantly refill pending slots, or
finish a characteristic's current level.

---

## 2. Layers and dependency direction

Dependencies point **downward only**. Nothing in `Domain/` references a Presenter or a View —
this is worth preserving, and is easy to verify with a grep.

```
                    ┌─────── outside the module · all in Systems/ ───────┐
                    │ Ledger           Clock/Audio/Haptics Events        │
                    │  KingdomLedger    IClock             EventBus      │
                    │  CharacteristicType                                │
                    │                   IAudioService                    │
                    │                   IHapticService                   │
                    └────────────────────────────────────────────────────┘
                                          ▲
 VIEW            LawsView ────────► LawsPresenter
 (MonoBehaviour) ├── LawCardView ──────► LawCardDisplayData
                 └── CharacteristicBarView ► CharacteristicDisplayData
                                          ▲
 PRESENTER       LawsPresenter ────► LawsManager, KingdomLedger, EventBus,
 (plain C#)                          IAudioService, IHapticService
                                          ▲
 DRIVER          AccrualDriver ────► LawsManager          (Core/Bootstrap, ITickable)
                                          ▲
 MODEL           LawsManager ──────► ShuffleBagDeck, ReplenishmentSlots,
 (plain C#)                          LawCardEffectApplier, CrystalBuyUpCalculator,
                                     KingdomLedger, IClock, LawsConfig
                                          ▲
 DOMAIN          ShuffleBagDeck            (pure, holds LawCardDefinition)
 (pure C#)       ReplenishmentSlots        (pure, zero project dependencies)
                 LawCardEffectApplier      (pure, writes to KingdomLedger)
                 CrystalBuyUpCalculator    (pure, zero project dependencies)
                                          ▲
 DATA (SO)       LawsConfig ──────► LawCardDefinition ──► LawCardEffect
```

**The Views never see a `LawCardDefinition` or any `Domain/` type.** Everything they render
arrives as a display struct built by the Presenter — already localized. That is the seam
localization hooks into, and it is why `LawCardView` has no dependency on `Domain`.

---

## 3. Class responsibilities

### Data (ScriptableObjects)

| Class | Responsibility |
|---|---|
| `LawsConfig` | Tunable parameters: queue cap, replenish interval, crystal costs. Also carries `AllCards`, the registry the deck is built from. **Not** the leveling curve — see §6.1. |
| `LawCardDefinition` | One card: its `CardId` and the accept/reject effect arrays. No text and no key fields — the id *is* the localization key (§6.12). One asset per card in `ScriptableObjects/LawCards/`. |

### Domain (pure C#, directly unit-testable)

| Class | Responsibility | Notes |
|---|---|---|
| `ShuffleBagDeck` | Draw order. Every card appears once per cycle before any repeat; reshuffles when empty; never repeats across a cycle boundary. | Takes an injectable `Random` so tests can pin the order. The only Domain class holding ScriptableObjects. |
| `ReplenishmentSlots` | The timers: how many slots are counting down and when the next matures. | Stores an **absolute deadline**, not a remaining duration — see §6.2. Handed the current time rather than reading a clock. |
| `LawCardEffectApplier` | Translates a card's signed effect values into Ledger calls. | The sign convention is the whole job: cards author one signed number; the Ledger splits gains and losses into separate methods. |
| `CrystalBuyUpCalculator` | Price of finishing a characteristic's current level. | Laws-owned, not shared — see §6.1. Returns 0 when nothing remains. |
| `LawCardEffect` | One `(CharacteristicType, float Points)` pair. Serializable struct. | |

### Model

**`LawsManager`** — owns the card queue and orchestrates the Domain pieces. Holds only the
state that spans them (`_activeCard`) plus the Ledger/config wiring.

```csharp
// Queue state
LawCardDefinition ActiveCard;  bool HasActiveCard;
int AvailableCardCount;        int CardsReplenishing;    int MaxCardCount;
event Action QueueChanged;

// Commands
void  InitializeCardPool();                      // fresh game
void  ProcessReplenishment();                    // settle timers (called by the driver)
bool  ResolveCard(string cardId, bool accept);   // swipe
bool  RefillWithCrystals();
bool  BuyUpCharacteristic(CharacteristicType t);

// Prices — owned here, next to the transactions that charge them (§6.3)
int   RefillCost;
int   GetBuyUpCost(CharacteristicType t);

// Persistence
LawsStateDto ToDto();   void LoadFromDto(LawsStateDto dto);
```

**`AccrualDriver`** (`Core/Bootstrap`, `ITickable`) — pumps `ProcessReplenishment()` at ~4 Hz.
Exists so the timer does not depend on any View being alive (§6.4). It replaced the
module-local `LawsTickDriver` when Trade arrived as the second module needing a timer, which is
the trigger that driver's own comment named; it now pumps Ledger regen and Trade's offer refresh
too, in a documented order (`ARCHITECTURE.md` §4.5).

### Presenter

**`LawsPresenter`** — turns model state into render-ready values and routes user intent
back. Plain C#, no MonoBehaviour, no DOTween, fully testable.

```csharp
event Action OnStateChanged;                          // "re-render everything"
event Action<CharacteristicType> OnCharacteristicLeveledUp;

bool TryGetActiveCardDisplay(out LawCardDisplayData);
CharacteristicDisplayData GetCharacteristicDisplay(CharacteristicType);
int  AvailableCardCount, MaxCardCount, RefillCost;
bool HasActiveCard, IsWaiting, IsReplenishing, CanAffordRefill;
float SecondsUntilNextCard;

bool OnSwipeAccepted(), OnSwipeRejected(), OnCrystalRefillRequested();
bool OnBuyUpRequested(CharacteristicType);
void SetScreenVisible(bool);                          // gates audio only
```

`LawCardDisplayData` and `CharacteristicDisplayData` are readonly view-model structs. Adding
a field a View needs to draw belongs here, not on the ScriptableObject.

The Presenter's constructor takes `CharacteristicRegistry` alongside `ILocalizationService`:
characteristic names and icons are Ledger-owned shared data, not Laws content
(`ARCHITECTURE.md` §4.4). It is a `UnityEngine.Object`, so its null-check is an explicit
`== null` rather than `??` — the null-coalescing operator bypasses Unity's overloaded equality
and would let a *destroyed* asset through.

### Views (MonoBehaviour — layout, DOTween, input)

| Class | Responsibility |
|---|---|
| `LawsView` | Root coordinator. Receives the Presenter via `[Inject]`, subscribes to its events, drives sub-views, forwards intents. Renders the countdown but **never advances it**. |
| `LawCardView` | One card: text plus the drag-to-swipe gesture and its DOTween animations. |
| `CharacteristicBarView` | One characteristic dial: circular progress ring, icon in the middle, level on a nameplate below. The whole dial is the tap target. |

---

## 4. The queue model

The player holds up to `MaxCardCount` (8) cards but only ever sees one. The queue is
described by **two numbers**, of which only one is stored:

```
AvailableCardCount  +  CardsReplenishing  ==  MaxCardCount
        │                      │
        │                      └── stored — ReplenishmentSlots.Count
        └── derived: MaxCardCount − CardsReplenishing   → the "6/8" readout
```

- **`AvailableCardCount`** — cards the player has, *including the one on screen*
- **`CardsReplenishing`** — timers still counting down

Two numbers that must sum to 8 would be two chances to disagree. Storing one and deriving the
other makes the invariant unbreakable.

### Why "available" can exceed 1

Only one card is ever displayed, so when a timer matures while a card is already on screen,
that card is available but not shown. `AvailableCardCount` counts it; `HasActiveCard` does not.

> **Don't reintroduce a third count.** An earlier version modelled those as a separate "ready"
> state with its own `ReadyCardCount` property plus an `OccupiedSlots` helper. Both were
> removed. The distinction is consulted in exactly one place — `TryFillActiveSlot` — which runs
> *after* establishing that no card is active, and at that moment "ready" and "available" are
> the same number. The extra count bought nothing and carried an ordering trap in `ResolveCard`
> (read the count before opening the freed slot and it was off by one) that needed a comment to
> defend. That trap no longer exists, because `AvailableCardCount` depends only on
> `ReplenishmentSlots.Count`.

**Cards behind the active one have no identity.** They are drawn when *shown*, not when their
timer matures — which is why the save holds one active id plus the remaining cycle, not eight ids.

---

## 5. Data flow

### Startup
```
GameBootstrapper.Configure()          registers everything (lazily)
GameEntryPoint.Start()
  └─ GameStateCoordinator.LoadOrInitialize()
        ├─ save exists → LawsManager.LoadFromDto(dto) → ProcessReplenishment()  (offline catch-up)
        └─ no save     → LawsManager.InitializeCardPool()
  └─ ILocalizationService.WhenReady(...)        ← waits for the String Tables (§6.12)
        └─ SceneManager.LoadScene("Main", Additive)
              └─ sceneLoaded → resolver.InjectGameObject(root) → LawsView.Construct(presenter)
                    └─ LawsView.Start() → Refresh()
```

Hydration **and** localization always complete before any View renders. Both orderings are
load-bearing and both fail silently if broken — see §6.12.

### A swipe
```
LawCardView drag threshold crossed
  └─ OnSwiped(accepted)                    ← fires IMMEDIATELY, before any animation
       └─ LawsView.OnCardSwiped
            └─ LawsPresenter.OnSwipeAccepted()
                 ├─ LawsManager.ResolveCard(id, accept)
                 │    ├─ LawCardEffectApplier.Apply(ledger, effects)   → Ledger mutates
                 │    ├─ _activeCard = null; slots.AddOne(...)
                 │    ├─ TryFillActiveSlot()                           → deck.Draw()
                 │    └─ QueueChanged                                  → Presenter → OnStateChanged → Refresh()
                 └─ audio + haptics
  └─ swipe-off animation plays (purely cosmetic)
       └─ OnSwipeAnimationComplete → LawsView reveals the next card
```

**Business logic never waits on an animation.** This is the module's most important runtime
contract: the Ledger has already been mutated and the state change already announced before
the first tween frame runs.

### A timer maturing
```
AccrualDriver.Tick()  (~4 Hz)
  └─ LawsManager.ProcessReplenishment()
       ├─ slots.Advance(now, interval) → how many matured
       ├─ TryFillActiveSlot()          → draws only if the active slot is empty
       └─ QueueChanged                 → Presenter → arrive SFX (if visible) + OnStateChanged
```

### Save / load
`GameStateCoordinator` calls `ToDto()` / `LoadFromDto()`. The DTO
(`Systems/Save/Scripts/Dto/LawsStateDto`) holds five fields, each restoring one
guarantee:

| Field | Restores |
|---|---|
| `activeCardId` | The card on screen |
| `cardsReplenishing` | How many timers are running |
| `nextReplenishDueUtc` | *When* the next card lands — offline accrual depends on it |
| `remainingDeckCardIds` | The shuffle bag's position in its cycle |
| `lastDrawnCardId` | No repeat across a cycle boundary |

---

## 6. Decisions that look arbitrary but are not

These are the ones a future change is most likely to undo by accident.

### 6.1 The leveling curve is Ledger-owned; buy-up pricing is Laws-owned
Two mechanics award characteristic points (Laws, and Random Occurrences per GDD §10), so the
**rate must match** — the curve lives in `Systems/Ledger` and callers cannot supply their own.
This was a live bug once: `LawsManager` used a config array while the occurrences module
passed `level => 100f`, so the same characteristic levelled at two different rates.

Buy-up *pricing* is the opposite: only Laws prices a buy-up, and per GDD §10 occurrences can
never touch crystals, so it can't acquire a second consumer. It stays in `Modules/Laws/Domain`.

**The test:** *if a second mechanic did this, would the result have to match?* Yes → `Systems/Ledger`.
No → the module's own `Domain/`. (`ARCHITECTURE.md` §4.3.)

### 6.2 The timer stores a deadline, not a remaining duration
A countdown that decrements needs something running to decrement it, so it cannot survive the
app closing. A wall-clock deadline can: subtract `now` whenever you next look and the answer
is right regardless of how long you were away. A 10-hour absence resolves in one call, not
10 hours of simulated ticks (`ARCHITECTURE.md` §4.5).

Corollary: `AddOne` only starts a countdown when the count goes 0 → 1. A second queued slot
must **not** reset a deadline the player has already partly waited out.

### 6.3 Prices live on the Manager, not the Presenter
The Presenter used to compute display prices from `LawsConfig` — a byte-identical duplicate of
the formulas the Manager charged with. They agreed, but nothing *made* them agree, and a price
shown that drifts from the price charged is a monetization bug. `RefillCost` and
`GetBuyUpCost` now live next to the transactions, and the Presenter reads them.

Tests assert the property directly: **crystals quoted == crystals deducted.**

### 6.4 The View is display-only
The replenishment timer used to be pumped from `LawsView.Update()`, which tied the mechanic's
progress to whether the screen happened to exist. `AccrualDriver` owns it now. A View may
*read* remaining time to draw a countdown; it must never be what makes the countdown advance.

Audio is the one thing gated on visibility (`SetScreenVisible`): the queue keeps advancing on
other tabs — so the screen is correct on return — but a card arriving unseen shouldn't make a
noise.

### 6.5 `QueueChanged` is a plain event, not a bus message
Its only subscriber is this module's own Presenter, which already holds the Manager. A plain
`event Action` is simpler and typed (`ARCHITECTURE.md` §4.2 tier 3). The trigger to promote it
to a cross-module bus event would be a subscriber outside Laws — a bottom-nav badge, say.

### 6.6 Bars are bound by type, not array order
`LawsView` builds a `Dictionary<CharacteristicType, CharacteristicBarView>` from each bar's own
declared `Type`. Reordering the serialized array is therefore harmless. Do not reintroduce
index-based binding: it silently renders one characteristic's data in another's row while
level-up celebrations still land correctly — nearly impossible to spot.

### 6.7 Four canvases on purpose

A Canvas is the unit of *rebuild*: dirty one Graphic and every Graphic sharing that canvas
re-batches. So anything that changes at runtime is split off from the static rest
(`ARCHITECTURE.md` §2/§9).

| Canvas | Graphics | Why it is separate |
|---|---|---|
| `LawsScreen` (root) | 3 | what is left once the movers are split out |
| `CharacteristicsPanel` | 43 | `Image.fillAmount` dirties vertices, and DOTween drives it **every frame** during a fill or level-up |
| `CardCanvas` (`overrideSorting`) | 9 | the card animates constantly, and must draw above its siblings |
| `TimerRow` | 6 | the countdown rewrites once a second — the "ticking counter" case in §2 |

Before this split all 52 non-card graphics rebuilt together, so a single ring tween re-batched
the whole screen every frame. Do not merge them back.

**A nested Canvas needs its own `GraphicRaycaster`.** Graphics register to the nearest Canvas,
so the parent's raycaster cannot see them — without one, every button inside goes silently
dead. Verified here by raycasting at the refill button, a dial and the card and confirming
each is hit, then clicking refill through `ExecuteEvents` (2/8 → 8/8, crystals 1131 → 1119).

`overrideSorting` is *not* needed for rebuild isolation — only `CardCanvas` sets it, because
it alone needs to draw above its siblings. The countdown label is still rewritten only when
its whole-second value changes; the canvas split and the throttle solve different halves of
the same problem.

### 6.8 `LawsManager` is registered with an explicit factory
It has a second constructor taking a `System.Random` for deterministic tests. VContainer picks
the constructor with the **most** parameters, so left alone it selects that one and fails to
resolve `System.Random` at startup. The factory in `GameBootstrapper` pins the 3-arg one.
`KingdomLedger` and `LocalJsonSaveService` are registered the same way for the same reason.

### 6.9 The characteristic dials show level, not points

Six dials across the top: a radial ring for progress inside the current level, the
characteristic's icon in the middle, and the **level** on a nameplate below. Exact point
totals are deliberately absent — they belong in the detail panel, so six dials stay readable
at a glance.

**One dial, six instances.** `Prefabs/CharacteristicDial.prefab` is the single source; the
screen holds six nested instances that override only `_type`. Edit the dial once and all six
follow. Do not "duplicate and tweak" a dial back into the screen prefab — that was the
original arrangement and it meant every visual change had to be repeated six times.

**Placement is a `HorizontalLayoutGroup`, not anchors.** The panel lays the dials out and each
carries a `LayoutElement` with `flexibleWidth = 1`, so they divide the row evenly at any width
and a seventh characteristic would need no repositioning. `childForceExpandWidth` is
deliberately **off**: it overrides `flexibleWidth` and splits slack equally among children,
which is what made the refill button in `TimerRow` balloon to twice its intended size before
it was caught.

Three things about the prefab that are easy to undo by accident:

- **The ring is three stacked Images** — `RingBg`, `RingFill`, `RingHole` — then `Icon`, then
  `Nameplate`. The hole is what turns a filled circle into a ring, because the stub sprite is
  solid; without it the fill covers the icon. If art later supplies a real ring sprite, delete
  `RingHole` rather than leaving it under the icon.
- **`CharacteristicBarView` never knows the dial is circular.** It sets `fillAmount`, which
  behaves identically for a bar or a ring — which is why the fill and level-up animations
  survived the redesign untouched. Keep it that way: shape is prefab configuration
  (`type = Filled`, `fillMethod = Radial360`), not code.
- **There is no name label.** A characteristic is identified by its icon.
  `CharacteristicDisplayData.Name` is still resolved, for the detail panel and other modules.

A note for whoever builds the next mechanic that awards characteristic points: `LawsPresenter`
re-renders on `QueueChanged` and `CharacteristicLeveledUp`, so points added without a level-up
and outside a Laws action will not move these rings on their own. Publish an event when
Random Occurrences lands and subscribe here — the seam is `NotifyStateChanged`.

All six rings are the same green, by decision. If per-characteristic colour is ever wanted it
is presentation data *about a characteristic*, so it belongs on `CharacteristicDefinition`
beside the icon — not as six values scattered across prefab instances.

**The dial is the tap target, and it is deliberately inert.** `CharacteristicBarView` raises
`OnDialPressed`; `LawsView` does not subscribe. The dial briefly ran the crystal buy-up
directly, which stopped being acceptable the moment the whole dial became the hit area and the
price label went away — a large, easy-to-hit control spending a premium currency with nothing
shown first. The purchase moves into the characteristic detail panel. When that panel exists:
subscribe to `OnDialPressed` in `LawsView` to open it, and let the panel call
`LawsPresenter.OnBuyUpRequested` behind its own confirmation.

That leaves `OnBuyUpRequested`, `BuyUpCost` and `CanAffordBuyUp` with no caller in the UI
today. They are intact and still covered by tests — including "price shown == price charged" —
because the panel is what will consume them. Do not delete them as dead code.

### 6.10 The screen's vertical layout is anchored top-down, not centre-out

`CharacteristicsPanel` and `TimerRow` are top-anchored at fixed heights; `CardCanvas` then
**stretches** into whatever is left, from just under the row down to the safe-area floor
(which coincides with the top of the bottom nav bar).

This replaced a centre-anchored, fixed-height card, and the reason matters. With the header
top-anchored and the card centre-anchored, the gap between them was
`safeAreaHeight / 2 − 230` — a *function of screen height* rather than a designed constant.
Measured across aspect ratios that gave a 49-unit gap on a 3:4 tablet and a 529-unit gap on a
9:20 phone, with the card's canvas overflowing the safe area by 60 units at the squat end.
Anchoring everything top-down makes the gap a constant 24 units everywhere and lets the
leftover space land in one place.

Verified by driving the Game view to 1080×1440, 1080×1920 and 1080×2400 and measuring: header
and row identical at all three, card region absorbing the difference, nothing overflowing the
safe area. Those three sizes are saved in the Game view as `KR 3:4`, `KR 9:16`, `KR 9:20` —
use them when changing this screen.

**Check a layout change from `Bootstrap`, never `Main`.** A pass of this was nearly signed off
on a screenshot that turned out to be unbound prefab defaults, because the play session had
started in `Main` and no View had been injected. Transform measurements stay valid either way,
which is exactly what makes it easy to miss — the layout looks right while the content is
fake.

**The card scales inside that region** via an `AspectRatioFitter` (FitInParent, 820∶860). It
grows to whatever the region allows while keeping its shape: height-limited on a 3:4 tablet
(830×871), width-limited on a 9:20 phone (900×944). The region is inset 90 units horizontally
so the fitter cannot run the card to the screen edges — without that inset it filled the full
width on tall screens and read as a slab rather than a card.

**The fitter and the swipe tween share a RectTransform**, which is worth knowing before
touching either. That looked like a conflict — `AspectRatioFitter` is a layout controller and
DOTween drives `anchoredPosition` — so it was tested rather than assumed: drag, forced
`LayoutRebuilder.ForceRebuildLayoutImmediate` mid-drag, threshold crossing, and settle. The
position survived the rebuild, the swipe committed (card count decremented and the next card
loaded *before* the animation, per §6.4), and the card returned to rest at (0,0) with rotation
and scale reset. Note the fitter sets stretch anchors (0,0)–(1,1); `anchoredPosition` still
behaves, but do not assume a centred-anchor layout when editing this prefab.

### 6.11 A card shows title and flavor only — never its effects

`LawCardDisplayData` carries `CardId`, `Title` and `FlavorText`. It deliberately has **no**
effect summary, and the card prefab has no label for one. Inferring what a law will do from
how it reads is the mechanic (GDD §6); printing "Army: +10" turns every swipe into arithmetic.

This is easy to undo by accident, because "the player can't see what this does" reads like a
missing feature. Two things guard it:

- Everything on that struct is rendered **before** the player commits, so any field naming a
  characteristic or a point value leaks the answer. `CardDisplay_RevealsNothingAboutItsEffects_BeforeTheSwipe`
  asserts the struct's *shape*, not just today's values, and fails if a field is re-added.
- Feedback belongs **after** resolution, on the bars — which already animate the change and
  celebrate a level-up. The planned addition there is particles carrying characteristic icons
  off the enacted card (GDD §6); not built, and explicitly not a pre-swipe readout.

The Presenter kept `ResolveCharacteristicName` after this change: the bars still need names.
Only the per-card summary and its `effect.line` Smart String were removed.

### 6.12 Localization: two paths, and three traps that cost real time

Card text lives at `{CardId}.title` / `{CardId}.flavor` in `LawCardsTable`. **Keys are derived
from data — `LawCardDefinition` carries no key fields at all.** Adding a card means adding two
table entries named after its id, and `LawsContentTests` fails if one exists in no locale
and warns if one is untranslated.

**Nothing else spells those strings.** `LawCardDefinition` owns `StringTable`, `TitleKey`,
`FlavorKey` and the static `BuildTitleKey`/`BuildFlavorKey`; the Presenter resolves through
them and `LawsContentTests` checks through them. That is not tidiness — a test holding its own
`id + ".title"` would keep passing while the game asked for something else, giving a green
test and blank text on screen. Verified by temporarily changing the derivation and
watching the Presenter's output change with it, which is what proves no second copy survives.

Characteristic names and icons are **not Laws-owned**: they come from `CharacteristicRegistry`
(`Systems/Ledger/`), which the Presenter injects and which owns both the table name and the key
derivation (`ARCHITECTURE.md` §4.4). Laws is just its first consumer — Cities and Random
Occurrences will resolve the identical strings and sprites through the same object. Anything
wrong there is reported by `LedgerDataTests`, deliberately not a Laws test: a Laws-owned check
of Ledger-owned keys was an earlier arrangement, and it would have left those keys unchecked
the moment a second module needed them.

Fixed chrome (ACCEPT, REJECT, the waiting line) uses `LocalizeStringEvent` components in the
prefab. Everything data-derived is resolved in `LawsPresenter` and delivered through the
display structs, because the entry to fetch is not known until a card is drawn — and pointing
a component at a card title would hand the View back the data reference §6.4 exists to remove.

The characteristic **icon** travels the same road: `CharacteristicDisplayData.Icon` carries a
`Sprite` the Presenter looked up. That is the one `UnityEngine.Object` on a display struct, and
it is deliberate — the alternative is `CharacteristicBarView` reading the registry itself,
which is exactly the View→data coupling §6.4 removes. A null icon is legitimate while art is
outstanding; the View disables the `Image` rather than leaving it spriteless, because a UGUI
`Image` with no sprite draws a filled white rectangle.

Three things that were each diagnosed the slow way, all of them silent failures:

1. **Localization initialises asynchronously and neither path recovers from rendering too
   early.** Not a first-frame flash — a screen built before the tables load keeps its
   placeholders for the whole session. `GameEntryPoint` waits on
   `ILocalizationService.WhenReady` before loading `Main`. Verified by deleting the wait and
   watching every string stay wrong 1200 frames later.
2. **Subscribing to `SelectedLocaleChanged` during construction silently does nothing.** The
   handler is never invoked; one attached after initialisation completes works normally.
   `UnityLocalizationService` therefore subscribes in `OnInitialisationComplete`. The symptom
   is deceptive: the chrome switches language correctly (it listens on its own) while every
   Presenter-built string stays in the previous language, which points suspicion at the
   Presenter — the one part that is fine.
3. **A missing entry renders as Unity's full diagnostic sentence,** which in a card title
   wraps and reads as a layout bug. The service sets `NoTranslationFoundMessage` to
   `[{key}]` on the database — not just in `Resolve` — because the chrome components never
   call the service and would otherwise still show the sentence.

None of this is reachable from EditMode tests; it is Unity-runtime behaviour, which is why
the verification for it is a Play-mode pass in both locales.

---

## 7. How to extend

**Add a law card** — create a `LawCardDefinition` in `ScriptableObjects/LawCards/`, fill in its
`CardId` and effect arrays, and add it to `LawsConfig.AllCards`. Then add **two entries per
locale** to `LawCardsTable`, named `{CardId}.title` and `{CardId}.flavor` — the asset holds no
text and no keys, the id *is* the key. Run the EditMode tests: `LawsContentTests` confirms
the id is unique and its text exists. No code change.

**Change queue size or timing** — `LawsConfig`. Nothing hardcodes 8 or 120.

**Add something the card displays** — add a field to `LawCardDisplayData`, populate it in
`LawsPresenter.TryGetActiveCardDisplay`, render it in `LawCardView`. Do *not* pass the
`LawCardDefinition` to the View.

**Add a new crystal spend** — put the price on `LawsManager` next to the method that charges
it, and have the Presenter read it (§6.3).

**Add queue behaviour** (e.g. a "peek at next card") — extend `ShuffleBagDeck` or
`ReplenishmentSlots` in `Domain/` and keep `LawsManager` as orchestration. If it needs no
Unity types, it belongs in `Domain/` and gets direct unit tests.

**Add a new language** — add the locale, then fill the three tables. No code change: every key
is derived, so a new locale is pure content. Check the TMP font asset actually has the glyphs.

**Give a characteristic its icon** — drop a sprite on its `CharacteristicDefinition` in
`Systems/Ledger/ScriptableObjects/Characteristics/`. No code, no prefab edit, and every module
that displays that characteristic picks it up. `LedgerDataTests` lists which ones are still
missing art, as a warning.

**Add new displayed text** — if it is fixed chrome, add a `LocalizeStringEvent` in the prefab
pointing at a new `LawsUITable` entry; nothing checks it, because it is on screen whenever the
Laws tab is. If it is built in code (a popup message), add the key to `LawsUIText` and require
it in `LawsContentTests`. If it is data-derived, put the table name and the key derivation on
the **data type** it belongs to — the way `LawCardDefinition` does — then resolve through those
members from `LawsPresenter` and carry the result on a display struct (§6.12). Do not spell a
table name or a key suffix in the Presenter or a test.

**Show several cards at once** — `LawsManager._activeCard` is the field that becomes a
collection, and the save gains an id per displayed card instead of just one.

---

## 8. Tests

`Assets/Tests/EditMode/Modules/Laws/`

| File | Covers |
|---|---|
| `ReplenishmentSlotsTests` | Timer arithmetic: maturing, remainder carry, backwards clocks, long absences, restore |
| `ShuffleBagDeckTests` | Cycle completeness, reshuffle, no back-to-back across 50 seeds, save round-trip, deleted cards |
| `LawCardEffectApplierTests` | The sign convention; level floor |
| `CrystalBuyUpCalculatorTests` | Rounding, the minimum of 1, and zero-when-nothing-remains |
| `LawsDomainTests` | `LawsManager` orchestration, queue arithmetic, save/load |
| `LawsPresenterTests` | View-facing state, visibility gating, **price shown == price charged**, per-row icon binding |
| `LawsContentTests` | The **real** cards: every `CardId` present and unique, every title and flavor plus the refill text in every locale (untranslated only warns) |

Ledger data used by this module is covered next door in
`Tests/EditMode/Systems/Ledger/CharacteristicRegistryTests` — lookup, gaps, duplicates, null
entries, and that the registry's key agrees with the static derivation the content tests check.

The Views have **no automated coverage** — MonoBehaviour + DOTween is PlayMode territory.
Swipe handling, the level-up fill sequence and the timer throttle are verified by playing the
scene. **Enter Play mode from `Bootstrap`, never `Main`**: `Main` is loaded additively and only
then are its Views injected. (`LawsView` detects this and says so rather than null-reffing.)

---

## 9. Known gaps

| Gap | Status |
|---|---|
| Ukrainian card text is placeholder | Structurally complete and verified on screen, but "Закон 1" is a stand-in — the three cards need real copy in both locales. |
| No characteristic icons yet | The registry, the display struct, the prefab slot and the render path are all wired and tested; the six `CharacteristicDefinition` assets just have no sprite. Dials hide the slot until one lands. |
| No characteristic detail panel | The dials show level only, so point totals and the crystal buy-up have nowhere to live. **Buy-up is currently unreachable from the UI** — `OnDialPressed` is raised but unsubscribed (§6.9). The Presenter API and its tests are intact, waiting for the panel. This is the next piece of the screen. |
| No post-swipe effect feedback | Now that the card states nothing (§6.11), the bars are the *only* signal of what a law did. They animate and celebrate level-ups, but the planned icon particles off the enacted card (GDD §6) are not built — worth doing before the mechanic is judged on feel. |
| Card copy carries the gameplay signal | The three placeholder cards read "This is law 1 description", which telegraphs nothing. Real copy is now a design dependency, not flavor polish. |
| Only Laws is localized | The other four modules have no content or Views yet; they adopt the same rules (`ARCHITECTURE.md` §2) when they do. |
| No in-game language switcher | Locale follows the OS (`SystemLocaleSelector`), English as fallback. Switching at runtime works and is verified; there is just no UI for it. |
| SFX ids are magic strings in `LawsPresenter.SfxIds` | Blocked on the audio system; `StubAudioService` no-ops. |
| Prefab is greybox | Functional and re-styleable; no pixel art yet. |
| `link.xml` unverified | Written, but only a real IL2CPP device build proves it. |
| No PlayMode tests | The swipe gesture in particular would benefit from one. |
