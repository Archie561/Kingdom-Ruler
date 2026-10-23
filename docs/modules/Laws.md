# Laws module — architecture

> Implements **GDD.md §6 — Law Enactment**. The GDD says *what* the mechanic does; this document
> says *how it is built* and which decisions must survive future changes. Project-wide rules live
> in `ARCHITECTURE.md`.

---

## 1. The mechanic in one paragraph

The player holds up to 8 law cards but sees exactly one at a time. Swiping right accepts it, left
rejects it; **both apply effects** to 1–3 of the 6 characteristics, and the card **never states
which** — the player judges it from the writing and finds out when the dials move (§6.7). A resolved
card frees a slot, which refills on a 2-minute real-time timer that keeps running while the app is
closed. Crystals can instantly refill every pending slot, or finish a characteristic's current level.

---

## 2. Structure

```
Modules/Laws/
  Scripts/                              assembly KingdomRuler.Modules.Laws
    Data/                               what designers author
      LawCardDefinition.cs              one card: id + accept/reject effects
      LawCardEffect.cs                  (characteristic, signed points)
      LawDecision.cs                    Accept / Reject
      LawsConfig.cs                     tunable parameters + the card list
    Model/                              the mechanic — plain C#
      LawsManager.cs                    entry point: lifecycle, clock, every Ledger transaction
      LawQueue.cs                       the player's hand
      LawDeck.cs                        draw order (shuffle bag)
      ReplenishmentTimer.cs             one card per interval, as a deadline
    UI/
      LawsPresenter.cs                  model ↔ view; feel hooks
      LawsScreenView.cs                 the screen root and its simple widgets
      LawsUIText.cs                     String Table keys for text built in code
      Card/
        LawCardView.cs                  the swipeable card
        LawCardViewData.cs              what it draws
      CharacteristicDial/
        CharacteristicDialView.cs       one dial
        CharacteristicDialViewData.cs   what it draws
  ScriptableObjects/
    Config/LawsConfig.asset
    LawCards/                           one LawCardDefinition asset per card
  Prefabs/                              LawsScreen, CharacteristicDial
  Localization/                         LawsUITable (chrome), LawCardsTable (card text)
  Images/
```

**Namespaces.** `Data/` and `Model/` share `KingdomRuler.Modules.Laws`; `UI/` is
`KingdomRuler.Modules.Laws.UI`. The split exists for one rule: **the model never references the
UI**, and a stray `using …Laws.UI` in a model file is easy to spot.

**Dependencies point one way:** `UI → Model → Data`, and everything may use `Systems/`.

```
 UI      LawsScreenView ──► LawsPresenter ──► LawsManager, KingdomLedger (reads only),
         ├─ LawCardView                         CharacteristicRegistry, PopupSystem,
         └─ CharacteristicDialView              IAudioService, IHapticService, ILocalizationService
                                                 │
 Model   LawsManager ──► LawQueue ──► LawDeck, ReplenishmentTimer
            │
            └──► KingdomLedger (writes), IClock
                                                 │
 Data    LawsConfig ──► LawCardDefinition ──► LawCardEffect, LawDecision
```

**Registration.** The composition root registers the module's config, its Manager and its
Presenter — nothing else. `LawsManager` builds `LawQueue`, which builds `LawDeck` and
`ReplenishmentTimer`.

---

## 3. Classes

### Data

| Class | Responsibility |
|---|---|
| `LawCardDefinition` | One card: `CardId` and the accept/reject effect arrays. Holds no text — the id is the localization key (§6.13). `EffectsOf(decision)` picks the array. |
| `LawCardEffect` | One `(CharacteristicType, float Points)` pair. Positive adds progress, negative removes it. |
| `LawDecision` | `Accept` / `Reject`. Used from the swipe all the way to the effects. |
| `LawsConfig` | Queue cap, replenish interval, crystal costs, and `AllCards`. **Not** the leveling curve (§6.2). |

### Model

| Class | Responsibility |
|---|---|
| `LawsManager` | The mechanic's entry point and **the only class in the module that touches the Ledger or the clock** (§6.1). Lifecycle (`InitializeNewGame`, `LoadFromDto`, `ToDto`), the clock subscription, and every transaction: resolving a card, refilling, buying up. Each price sits beside the transaction that charges it. |
| `LawQueue` | The hand: the shown card, held/replenishing counts, taking the shown card, completing replenishment. Pure — handed `now`. |
| `LawDeck` | Draw order: every card once per cycle, reshuffle when empty, never the same card across a cycle boundary. Takes a `System.Random` so tests can seed it. |
| `ReplenishmentTimer` | How many cards are pending and when the next matures. Stores a **deadline** (§6.3). |

```csharp
// LawsManager
void InitializeNewGame();   void LoadFromDto(LawsStateDto);   LawsStateDto ToDto();
void AdvanceTo(DateTime now);                       // subscribed to IClock.Ticked
event Action QueueChanged;

LawCardDefinition ActiveCard;   int HeldCardCount, ReplenishingCardCount, MaxCardCount;
float SecondsUntilNextCard, SecondsUntilQueueFull;

bool ResolveActiveCard(LawDecision);
int  RefillCost;                bool CanAffordRefill;              bool TryRefill();
int  BuyUpCost(type);           bool CanAffordBuyUp(type);         bool TryBuyUp(type);
```

`LawsManager` has two constructors: the production one, marked `[Inject]` so VContainer picks it,
and one taking a `System.Random` for tests.

### UI

| Class | Responsibility |
|---|---|
| `LawsPresenter` | Turns model state into localized view data, carries player actions to the manager, owns sound and haptics, gates passive sound on screen visibility. Plain C#, no Unity UI types. One per screen. |
| `LawsScreenView` | The screen root. Renders the presenter's state onto its components and simple widgets (count, timer, refill button, waiting overlay), and forwards input. Displays the countdown but never advances it (§6.5). |
| `LawCardView` | The card: its text, the drag-to-swipe gesture, its animations, and when to reveal the next card (§6.6). |
| `CharacteristicDialView` | One dial: progress ring, icon, level, level-up animation (§6.11). |
| `LawCardViewData`, `CharacteristicDialViewData` | Exactly what each view draws, already localized. Views never see a `LawCardDefinition` or the registry. |

**When a UI element gets its own View class:** when it repeats (the dial ×6) or has behaviour of
its own (the card's gesture and reveal). Everything else — a label, a button — is a serialized field
on `LawsScreenView`, which forwards its clicks to the presenter. The presenter never holds a Unity
UI component.

```csharp
// LawsPresenter
event Action StateChanged;   event Action CardArrived;   event Action<CharacteristicType> CharacteristicLeveledUp;

bool HasActiveCard, IsWaiting, IsReplenishing, CanAffordRefill;
int  HeldCardCount, MaxCardCount, RefillCost;   float SecondsUntilNextCard;
bool TryGetActiveCard(out LawCardViewData);     CharacteristicDialViewData GetDial(type);

bool ResolveCard(LawDecision);   UniTaskVoid RequestRefill();   void SetScreenVisible(bool);
int  BuyUpCost(type);   bool CanAffordBuyUp(type);   bool BuyUp(type);   // for the detail panel (§9)
```

---

## 4. The queue model

```
HeldCount  +  ReplenishingCount  ==  MaxCount (8)
    │                 │
    │                 └── stored — ReplenishmentTimer.PendingCount
    └── derived — the "6/8" readout, the shown card included
```

Two numbers that must sum to 8 would be two chances to disagree, so only one is stored.

**Held can exceed 1.** Only one card is shown; when a timer matures while a card is already shown,
the new card is held but not shown. `HeldCount` counts it; `ActiveCard` does not change.

**Cards behind the shown one have no identity.** A card is drawn from the deck only at the moment
it is shown. That is why the save holds one active id plus the deck's remaining cycle, not eight ids.

**A resolved card's slot starts replenishing**, capped so the queue never exceeds the maximum, and the
next held card (if any) is shown immediately.

---

## 5. Data flow

### Startup
```
GameEntryPoint.Start()
  └─ GameStateCoordinator.LoadOrInitialize()
        ├─ save exists → LawsManager.LoadFromDto(dto) → AdvanceTo(now)   (settles time away)
        └─ no save     → LawsManager.InitializeNewGame()
  └─ ILocalizationService.WhenReady(...)                                 (§6.13)
        └─ load Main additively → inject its roots → LawsScreenView.Construct(presenter)
              └─ LawsScreenView.Start() → Refresh()
```

State and localization are both ready before any View renders; both orderings fail silently if
broken.

### A swipe
```
LawCardView: drag crosses the threshold
  └─ Swiped(decision)                           ← immediately, before any animation
       └─ LawsPresenter.ResolveCard(decision)
            ├─ LawsManager.ResolveActiveCard(decision)
            │    ├─ LawQueue.TakeActiveCard(now)  → slot starts replenishing, next card shown
            │    ├─ apply the card's effects      → Ledger mutates (level-up event if any)
            │    └─ QueueChanged → StateChanged → LawsScreenView.Refresh() → LawCardView.Show/Hide
            │                                      (held back while the swiped card flies off)
            └─ sound + haptic
  └─ swipe-off animation finishes → LawCardView reveals the held-back content and scales it in
```

### A timer maturing
```
GameClock.Ticked(now)  (~4 Hz)
  └─ LawsManager.AdvanceTo(now)
       └─ LawQueue.Advance(now) → if any matured: QueueChanged
            └─ Presenter: arrival sound (if visible), StateChanged, CardArrived
```

### A refill
```
Refill button → LawsPresenter.RequestRefill()
  └─ ConfirmPopup.Ask(...)          live price and countdown; closes itself if the queue refills on its own
       └─ Confirm → LawsManager.TryRefill()  charges RefillCost as it is now → QueueChanged
```

### Save / load
`GameStateCoordinator` calls `ToDto()` / `LoadFromDto()`. The DTO (`Systems/Save/Scripts/Dto/LawsStateDto`)
holds five fields, each restoring one guarantee:

| Field | Restores |
|---|---|
| `activeCardId` | The shown card |
| `cardsReplenishing` | How many timers are running |
| `nextReplenishDueUtc` | *When* the next card lands — offline progress depends on it |
| `remainingDeckCardIds` | The deck's position in its cycle |
| `lastDrawnCardId` | No repeat across a cycle boundary |

A missing or unreadable deadline restarts the countdown from now rather than dropping the queue. A
saved card id that no longer exists is skipped; if that leaves the shown slot empty while cards are
held, the next held card is shown in its place — otherwise a full hand would never draw again.

---

## 6. Rules that must hold

### 6.1 Only `LawsManager` touches the Ledger and the clock
Every Ledger write the mechanic makes — card effects, the refill charge, the buy-up — is in one file.
`LawQueue`, `LawDeck` and `ReplenishmentTimer` are pure: they are handed the time and return results.
A new transaction goes on the manager; new queue behaviour goes on `LawQueue`.

### 6.2 The leveling curve is Ledger-owned; buy-up pricing is Laws-owned
Two mechanics award characteristic points (Laws, and Random Occurrences per GDD §10), so the rate must
match — the curve lives in `Systems/Ledger` and no caller can supply its own. Buy-up *pricing* is the
opposite: only Laws prices a buy-up, and per GDD §10 occurrences never touch crystals, so it stays in
`LawsManager`. The test: *if a second mechanic did this, would the result have to match?*
(`ARCHITECTURE.md` §4.3).

### 6.3 The timer stores a deadline
A countdown that decrements needs something running to decrement it; a wall-clock deadline does not.
Whenever the timer is next advanced it compares against `now`, so a 10-hour absence settles in one call.
The deadline advances in whole intervals, so partial progress is never lost, and queuing another card
never resets a countdown already in progress.

**The deadline is never more than one interval away.** If it is — the device clock was moved back, or
the save was edited — `Advance` pulls it in to `now + interval`. In normal play it never triggers;
without it, a clock moved back a day would stall the queue for a day. It runs on load and on every tick.

### 6.4 Prices and affordability live next to the charge
`RefillCost`/`CanAffordRefill` and `BuyUpCost`/`CanAffordBuyUp` are on the manager, beside the methods
that charge them; the presenter only reads them. The refill confirmation re-reads the price when the
player confirms rather than using what the popup displayed. Tests assert **crystals quoted == crystals
deducted** at every buy-up level.

### 6.5 Views display; they never drive the mechanic
The replenishment timer advances on the game clock (`ARCHITECTURE.md` §4.5), so the queue is correct
whether or not the screen exists. A view may read remaining time to draw a countdown — never advance it.
Sound is the one thing gated on visibility (`SetScreenVisible`): a card arriving on another tab updates
the queue silently.

### 6.6 A decision never waits on an animation
`LawCardView.Swiped` fires the moment the drag crosses the threshold; the Ledger has changed before the
first tween frame. What *does* wait is the visual: while the swiped card flies off, `Show`/`Hide` are
held and applied once it has left, so the departing card keeps its text and the next one scales in
after it. `LawCardView` owns this entirely — the screen view just says what should be shown.

Nothing can leave the card stuck mid-swipe: the swipe-off runs even if handling the decision throws,
its tweens run on unscaled time, and `OnDisable` (a tab switch mid-flight) kills them and resets the
card to rest; the next refresh then shows the current state.

### 6.7 A card shows title and flavor only
`LawCardViewData` carries `Title` and `FlavorText`, nothing else. Inferring what a law does from how it
reads is the mechanic (GDD §6); printing "Army: +10" turns every swipe into arithmetic. Everything on that
struct is rendered before the player commits, so a test asserts its **shape** and fails if a field is
added. Feedback belongs after resolution, on the dials.

### 6.8 `QueueChanged` is a plain event, not a bus message
Its only subscriber is this module's presenter, which already holds the manager (`ARCHITECTURE.md` §4.2).
Promote it to a tier-2 bus event when something outside Laws needs it — a bottom-nav badge, say.

### 6.9 Dials are matched by type, never by array order
Each dial declares its `CharacteristicType`; the screen renders and celebrates by that, so reordering the
serialized array is harmless. `LawsScreenView` logs an error at start unless every characteristic has
exactly one dial.

### 6.10 Four canvases
A Canvas is the unit of rebuild: dirty one Graphic and everything on that canvas re-batches. Anything
that changes at runtime sits on its own (`ARCHITECTURE.md` §2):

| Canvas | Why it is separate |
|---|---|
| `LawsScreen` (root) | the static rest |
| `CharacteristicsPanel` | `Image.fillAmount` dirties vertices, and DOTween drives it every frame during a fill |
| `CardCanvas` (`overrideSorting`) | the card animates constantly and must draw above its siblings |
| `TimerRow` | the countdown rewrites once a second |

Each nested canvas has its own `GraphicRaycaster`, or every button inside it is silently dead. The
countdown label is also only rewritten when its whole-second value changes.

### 6.11 The dial prefab
- **One prefab, six instances.** `Prefabs/CharacteristicDial.prefab` is the source; the screen holds six
  nested instances that override only `_type`.
- **Placement is a `HorizontalLayoutGroup`.** Each dial has `flexibleWidth = 1`; `childForceExpandWidth` is
  off, because it overrides `flexibleWidth`.
- **The ring is three stacked Images** — `RingBg`, `RingFill`, `RingHole` — then `Icon`, then `Nameplate`.
  The hole turns the solid stub sprite into a ring; delete it when art supplies a real ring sprite.
- **Shape is prefab configuration** (`type = Filled`, `fillMethod = Radial360`); the view only sets
  `fillAmount`.
- **The dial shows level, not points**, and has no name label — it is identified by its icon.
- **A missing icon hides the slot**: a UGUI `Image` with no sprite draws a white box.
- **A level-up fills to full, resets, then fills the carried-over progress**, so the ring never runs
  backwards at the moment the player earned something.
- **The dial is the tap target, and it is deliberately inert.** `CharacteristicDialView.Pressed` has no
  subscriber: a large control must not spend crystals with nothing shown first. Its first listener is the
  characteristic detail panel (§9), which then calls `LawsPresenter.BuyUp` behind its own confirmation.

All six rings are the same green. Per-characteristic colour would be data about a characteristic, so it
belongs on `CharacteristicDefinition` beside the icon.

### 6.12 The screen is laid out top-down
`CharacteristicsPanel` and `TimerRow` are top-anchored at fixed heights; `CardCanvas` stretches into
whatever is left down to the safe-area floor (the top of the nav bar). That keeps the gap under the header
a constant 24 units at every aspect ratio and puts all spare space in the card region.

The card scales inside that region with an `AspectRatioFitter` (FitInParent, 820∶860). The region is inset
90 units horizontally so the card never runs to the screen edges. The fitter sets stretch anchors and
shares the card's RectTransform with the swipe tween; drag, a forced layout rebuild mid-drag, commit and
settle were all verified to behave.

Verified at 1080×1440, 1080×1920 and 1080×2400 — saved in the Game view as `KR 3:4`, `KR 9:16`, `KR 9:20`.
**Check layout from `Bootstrap`, never `Main`**: started from `Main`, no View is injected and the screen
shows prefab defaults while the transforms still measure correctly.

### 6.13 Localization
**Keys are derived from data.** A card's text lives at `{CardId}.title` / `{CardId}.flavor` in
`LawCardsTable`. `LawCardDefinition` owns `StringTable`, `TitleKey`/`FlavorKey` and the static
`BuildTitleKey`/`BuildFlavorKey`; the presenter and `LawsContentTests` both resolve through them, so a test
cannot pass while the game asks for a different key.

Characteristic icons come from `CharacteristicRegistry` (`Systems/Ledger`), which other modules share; the
presenter looks them up and passes the `Sprite` on `CharacteristicDialViewData` — the one
`UnityEngine.Object` on a view-data struct, so the view never holds the registry.

**Two paths.** Fixed chrome (ACCEPT, REJECT, the waiting line) is a `LocalizeStringEvent` on the prefab.
Data-derived text is resolved in the presenter and delivered on the view-data structs; the presenter
re-renders on `ILocalizationService.LocaleChanged`.

**Three silent traps:**
1. **Localization initialises asynchronously**, and neither path recovers from rendering too early — the
   placeholders stay for the session. `GameEntryPoint` waits on `ILocalizationService.WhenReady`.
2. **`SelectedLocaleChanged` subscribed during construction is never invoked.** `UnityLocalizationService`
   subscribes after initialisation completes. Symptom: the chrome switches language while presenter-built
   strings do not.
3. **A missing entry renders as Unity's full diagnostic sentence**, which reads as a layout bug. The
   service sets `NoTranslationFoundMessage` to `[{key}]` on the database so the chrome gets it too.

None of this is reachable from EditMode tests; it is verified by a Play-mode pass in both locales.

---

## 7. How to extend

**Add a law card** — create a `LawCardDefinition` in `ScriptableObjects/LawCards/`, set its `CardId` and
effects, add it to `LawsConfig.AllCards`, and add `{CardId}.title` and `{CardId}.flavor` to
`LawCardsTable` in every locale. `LawsContentTests` checks the id is unique and the text exists. No code.

**Change queue size or timing** — `LawsConfig`. Nothing hardcodes 8 or 120.

**Add a crystal purchase** — a price property, an affordability check and a `Try…` method side by side on
`LawsManager`; the presenter reads and calls them (§6.4).

**Add queue behaviour** (e.g. peeking at the next card) — on `LawQueue` (or `LawDeck`), pure and directly
tested; expose it through the manager.

**Show several cards at once** — `LawQueue.ActiveCard` becomes a collection, and the save gains an id per
shown card.

**Add something the card displays** — a field on `LawCardViewData`, filled in
`LawsPresenter.TryGetActiveCard`, drawn in `LawCardView`. Never pass the `LawCardDefinition` to a view, and
never add anything that reveals the effects (§6.7).

**Add a UI element** — a plain widget is a serialized field on `LawsScreenView`; a repeated or behaving
element gets its own folder under `UI/` holding its view and its view data (§3).

**Add displayed text** — fixed chrome: a `LocalizeStringEvent` pointing at a new `LawsUITable` entry.
Built in code: a key on `LawsUIText`, required in `LawsContentTests`. Data-derived: the table name and key
derivation on the data type, the way `LawCardDefinition` does it (§6.13).

**Add a language** — add the locale and fill the three tables. No code. Check the TMP font has the glyphs.

**Give a characteristic its icon** — drop a sprite on its `CharacteristicDefinition` in
`Systems/Ledger/ScriptableObjects/Characteristics/`. No code; `LedgerDataTests` lists those still missing.

---

## 8. Tests

`Assets/Tests/EditMode/Modules/Laws/`

| File | Covers |
|---|---|
| `LawsManagerTests` | Effects and their sign convention, the level floor, the clock subscription, `QueueChanged`, refill and buy-up pricing (**quoted == deducted** at every level), save round trip, offline catch-up, damaged deadlines |
| `LawQueueTests` | The held/replenishing invariant, draw-on-show, the cap, taking the shown card, restore |
| `LawDeckTests` | One card per cycle, reshuffle, no back-to-back repeat across 50 seeds, restore, deleted cards |
| `ReplenishmentTimerTests` | Maturing, remainder carry, backwards clocks, long absences, restore |
| `LawsPresenterTests` | View data (including the card's shape), feel hooks, visibility gating, refill confirmation never spending on open, locale refresh |
| `LawsContentTests` | The real cards: unique ids, title and flavor plus the refill text in every locale |

**Play mode** (MonoBehaviour + DOTween), always entered from `Bootstrap`; `LawsScreenView` logs an error
and disables itself if started from `Main`. Verified: an existing save loads; a swipe applies immediately
while the departing card keeps its text and the next scales in after; the last card flies off and then
hides; the refill confirmation shows the live price, charges exactly that on confirm and nothing on open;
a buy-up levels the dial with its fill and punch animations.

---

## 9. Known gaps

| Gap | Status |
|---|---|
| No characteristic detail panel | Point totals and the crystal buy-up have nowhere to live; buy-up is unreachable from the UI. `CharacteristicDialView.Pressed` and the presenter's buy-up members are its hooks, and are tested. The next piece of this screen. |
| No post-swipe effect feedback | The dials are the only signal of what a law did. The planned icon particles off the enacted card (GDD §6) are not built. |
| Card copy carries the gameplay signal | The three placeholder cards telegraph nothing, and the Ukrainian text is a stand-in. Real copy is a design dependency. |
| No characteristic icons | Everything is wired; the six definitions have no sprite. |
| No in-game language switcher | Locale follows the OS, English as fallback. Runtime switching works; there is no UI for it. |
| SFX ids are strings in `LawsPresenter` | Blocked on the audio system; `StubAudioService` no-ops. |
| Prefab is greybox | Functional and re-styleable; no pixel art yet. |
| `link.xml` unverified | Only a real IL2CPP device build proves it. |
| No PlayMode tests | The swipe gesture in particular would benefit from one. |
