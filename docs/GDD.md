# Game Design Document — Kingdom Ruler (working title)

> **Status:** v1.0 draft. This is the source of truth for what the finished game does.
> Any code that implements a mechanic differently from this document is a bug, unless this
> document is updated first. Sections marked **[ASSUMED — CONFIRM]** are defaults chosen to
> unblock development; revise the numbers freely, the *shape* of the system is the part
> that matters most.

---

## 1. One-liner

A cozy, portrait-mode, pixel-art kingdom management sim for mobile. You rule by passing or
rejecting laws, trading with neighboring kingdoms, building an economy, and spending all of
it to expand your kingdom on a region map. ~80 hours to fully complete.

## 2. Pillars

1. **Cozy, not punishing.** No fail states, no timers that punish the player for closing the
   app, no aggressive monetization pressure. Idle systems should feel generous while the app
   is closed and satisfying when reopened.
2. **Every screen is a resource valve.** Laws, Trade, Economy each produce or consume one
   piece of the puzzle; the Map is where it all gets spent. The player should always be able
   to answer "why am I doing this mechanic right now" with "to afford the next city."
3. **Crisp, tactile feedback.** Every player action gets a visual + audio + haptic response.
   Nothing feels like it happened only because a number changed in a corner.
4. **Simple algorithms, not simulations.** Trade offers, random occurrences, and pricing use readable
   weighted-random and curve formulas — not emergent systems that are hard to balance or
   explain.

## 3. Platform & technical targets

- **Orientation:** Portrait only.
- **Aspect ratios:** Design and test against 19.5:9 (modern iPhone) down to 16:9 (older
  Android) — safe area must be respected for notches and gesture bars.
- **Art style:** Pixel art, cozy medieval-fantasy. Recommend a fixed base resolution (e.g.
  a 1x pixel grid at a defined PPU) with pixel-perfect camera — see `ARCHITECTURE.md`.
- **Performance budget:** Mid-tier phones from the last ~4 years, 60fps target, low battery
  draw (this is a game people leave installed and check a few times a day, not a
  twitch-reflex game — there is no excuse for it draining a battery).
- **Feel requirements (non-negotiable, apply everywhere):**
  - Animation on every state change (card swipe, number tick-up, screen transition).
  - Sound effect on every confirmed player action and every notable passive moment
    (level-up, business full, occurrence arrived).
  - Haptic feedback (light impact) on swipe-decision, purchase confirmation, and
    level-up; do not overuse haptics elsewhere or it becomes noise.

## 4. Core loop

**Session loop (2–5 minutes, several times a day):**
Open app → check mailbox for a random occurrence → collect gold from ready businesses → review
any waiting law cards → check trade offers, take good ones → see if a city/village is now
affordable → buy it if so → close app, resources keep regenerating.

**Meta loop (weeks):** Characteristics slowly rise (gated by law decisions), trade resources
accumulate (gated by warehouse size and offer luck), gold accumulates (gated by business
tier), all three gate city purchases. Buying every city/village in a region grants a
passive bonus, encouraging regional completion over random purchases.

## 5. Currencies & resources glossary

| Resource | Type | Source | Sink |
|---|---|---|---|
| **Crystals** | Premium (real-money eventually, see §9) | Shop (mock IAP for now), ads, rare occurrence rewards | Law refill, instant trade refresh, warehouse capacity upgrade (alt path) |
| **Gold** | Soft | Businesses (Economy screen) | Business purchases, city/village purchases, some occurrence choices |
| **Characteristics (×6):** Medicine, Education, Army, Science, Infrastructure, Welfare | Soft, leveled stat | Law decisions, some occurrences | City/village purchase requirements (never spent down, only gate checks) |
| **Trade resources (×6):** Stone, Wood, Metal, Minerals, Leather, Clay | Soft, stored with capacity | Passive regen, trade offers, some occurrences | Trade offers, warehouse upgrades (cross-resource cost), city/village purchases |

## 6. Mechanic 1 — Law Enactment

**Screen:** Law tab. Shows the current law card (Tinder-style swipe: right = accept,
left = reject) plus a compact readout of all 6 characteristic bars/levels.

- Player holds up to **8 law cards** at once. A card is **short flavor text and nothing else** —
  no readout of which characteristics it affects, and no numbers.
- **The player is not told what a law does before deciding.** Reading the writing and inferring
  the likely consequence *is* the mechanic; a printed summary would reduce every swipe to
  arithmetic. The player learns the actual outcome only once the law is enacted, from the
  characteristic bars moving.
  - This means **card copy carries the whole gameplay signal.** A law whose text gives no hint
    of its domain is a bad card, not a hard one. Writing it is a design task, not flavor.
  - *Planned, not built:* particles rising from the enacted card carrying the icons of the
    characteristics that went up or down — feedback **after** the decision, never before.
- **Draw order:** cards are drawn at random from the full card pool, with no repeats until
  every card has appeared once (a "shuffle bag") — then the pool reshuffles and the cycle
  continues. Plain sequential or fixed-order cycling is explicitly not the intended feel.
- Accepting or rejecting a law both apply *some* effect (this is the point — every law
  matters, there's no "safe" choice) to 1–3 of the 6 characteristics.
- Characteristics level up via an accumulating point total. **Level floor is permanent** —
  once a characteristic reaches level *N*, no card can ever push it back below *N*'s
  threshold, only slow further gain or (rarely) reduce progress *within* the current level.
- **Points required to reach level *N* is a formula, not a lookup table:**
  `required(N) = round(base × growth^(N-1) / roundTo) × roundTo`, with
  **[ASSUMED — CONFIRM]** `base = 100`, `growth = 1.35`, `roundTo = 10`. The three coefficients are
  designer-editable on a config asset so the curve can be retuned without a code change; there is
  deliberately **no** per-level value array or `AnimationCurve` — a formula keeps every level
  defined, including ones no designer has reached yet. Per-characteristic coefficient overrides can
  be layered on later if the curve needs to differ per stat.
- **The curve is owned by the Ledger, not by Laws.** Every mechanic that awards characteristic
  points (Laws today, Random Occurrences per §10) must level a given characteristic at the same
  rate — see `ARCHITECTURE.md` §4.3.
- **Crystal buy-up:** player may spend crystals to instantly fill the remaining points needed
  for the next level. **[ASSUMED — CONFIRM]** Cost = `ceil(points_remaining / 20)` crystals,
  minimum 1.
- **Replenishment:** when a card is resolved, a new one begins queuing, taking **2 minutes**
  per card (this timer runs in real time, including while the app is closed — see offline
  accrual note in `ARCHITECTURE.md`). If all 8 slots are empty and none are queued, show a
  friendly "the council is preparing new decrees" waiting state instead of an empty card.
- **Instant refill:** if below the 8-card cap, player may spend **2 crystals per missing
  card** to refill to the cap immediately.

## 7. Mechanic 2 — Trading

**Screen:** Trade tab. Warehouse levels for all 6 resources at the top, scrollable list of
current trade offers below.

- 6 resources, each with its own warehouse **capacity**. Resources **regenerate passively**
  at a rate that fills an *empty* warehouse to 100% over **24 hours**, scaled to that
  warehouse's current capacity (bigger warehouse = more resource per hour, same fill *time*).
- **Warehouse upgrade**, two payment paths:
  1. Crystals (flat premium option, cost scales with upgrade tier).
  2. Spend **80% of the current capacity** of a *paired* resource's warehouse.
     **[ASSUMED — CONFIRM pairing]:** Stone↔Wood, Metal↔Minerals, Leather↔Clay. (i.e.
     upgrading Wood consumes 80% of Stone's warehouse *limit*, not stored amount — read
     "spend 80% of the limit" literally; if you actually want it to cost 80% of currently
     *stored* resource instead, that's a one-line change, flag it.)
  - Each subsequent upgrade costs more than the last (same style of curve as §6, own
    designer-editable table per resource).
- **Trade offer list:** 10 offers at a time, refresh **every 20 minutes**, or instantly for
  crystals. Refreshing discards all unused offers.
- **Offer generation ratio (of the 10 offers):**
  - 30% profitable (player receives more total units than they give)
  - 45% neutral (roughly even) **[ASSUMED — CONFIRM exact split; brief said 40–50%]**
  - 25% unprofitable (player gives more than they receive)
  - All 6 resources are valued identically (1 unit = 1 unit) for this calculation.
- Each offer gives 2–3 resource types and asks for 2–3 different types; **give and receive
  sets never overlap** within one offer.
- Tapping an offer opens a confirmation panel. If the player can't afford the ask, or a
  received resource would overflow warehouse capacity, show a clear inline message
  explaining which resource is the blocker — never silently disable the offer.

## 8. Mechanic 3 — Economy

**Screen:** Economy tab. Grid/list of business types (quarry, forge, sawmill, tavern, mine,
market, etc. — content list to be expanded during production).

- Player earns gold per minute passively per owned business, but **must manually collect**
  — gold sits in a business's own storage, capped at **24 hours worth of production**, after
  which it stops accumulating until collected.
- Buying more of the same business type is **uncapped**; each additional unit costs more and
  produces more per minute. **[ASSUMED — CONFIRM]** Cost curve:
  `cost(n) = baseCost × 1.15^n` (n = number already owned), a common idle-game curve chosen
  to keep early purchases cheap and late purchases meaningfully expensive.
- Tapping a business type opens a detail panel: artwork, flavor text, count owned, current
  storage fill (with a "Collect" action when nonzero), and a "Buy another" action.

## 9. Mechanic 4 — Village & City Purchasing (the spine of progression)

**Screen:** Kingdom/Map tab — this is the **home screen**. Shows kingdom population (derived
from cities/villages owned, and it determines the kingdom's overall level) and an entry
point into a scrollable region map. The mailbox button for Random Occurrences (§10) also lives
here.

- The map is divided into **regions** (e.g. Forest Realm, Mountain Ranges, Desert — final
  list TBD in content pass). Each region contains several cities/villages, each shown as a
  tappable icon, each individually purchasable.
- Buying a city/village **unlocks it visually on the map and increases population**.
  Completing every purchase in a region grants a **passive global bonus** (bonus type TBD —
  candidates: passive rate boost to one resource category, discount on that region's
  remaining costs, or a cosmetic-plus-small-buff reward).
- **Each purchase costs a combination of:**
  1. A minimum **level** in one or more of the 6 characteristics (gate check, not spent).
  2. A specific **amount** of one or more trade resources (spent).
  3. An amount of **gold** (spent).
- Costs are **authored per city**, not generated by a single formula — city/village data is
  content (name, description, artwork, region, unlock requirements), so it belongs in
  designer-facing data assets (see `ARCHITECTURE.md` §Data), not code. The overall *curve*
  of increasing cost across ~all cities should be tuned to hit the **~80 hour full
  completion target**; that pacing pass happens once enough content exists to simulate it.
- Tapping a locked city shows price breakdown, description, and artwork before purchase;
  tapping an owned one can show flavor/stats (nice-to-have, not core).

## 10. Mechanic 5 — Random Occurrences

> **Naming.** This mechanic is called *Random Occurrences*, not "Random Events", throughout the
> codebase and these docs. "Event" is reserved for messages on the `Core/EventBus` pub/sub
> (`CharacteristicLeveledUp`, `GoldChanged`, …) — see `ARCHITECTURE.md` §4.2. The two meanings
> collided constantly in code (`EventsManager` vs. event-bus events), so the mechanic gets the
> distinct word.

**Delivery:** a mailbox icon/badge on the home (Map) screen. Opening it shows pending occurrence
letters. Each occurrence presents **a choice between two outcomes**, not a flat notification —
e.g. *"A flood threatens the riverbanks. Reinforce them for 5,000 gold, or accept losing 30%
of your stored Wood?"*

- Occurrences can affect **any resource except crystals** (characteristics, trade resources,
  gold) — never the premium currency, to keep it monetization-safe.
- Because occurrences reach across into other mechanics' state, they go through the shared Ledger
  and the shared cross-module events, never through another module's Manager directly
  (`ARCHITECTURE.md` §4.2). In particular, an occurrence that awards characteristic points levels
  that characteristic at exactly the same rate Laws does — the curve is Ledger-owned (§6).
- **v1 approach: pure weighted-random** occurrence table (simplest to ship, matches the brief's
  fallback suggestion). Each occurrence has independent trigger odds and a pool of 2 outcomes.
- **v2 (later, optional):** layer in dynamic pacing — track the player's progress rate (e.g.
  population growth vs. an expected curve) and bias the occurrence table toward
  slowdown-flavored occurrences when the player is ahead of pace, boost-flavored ones when
  behind. Do not build this until v1 is shipped and there's real data on player pacing —
  building it earlier is guessing at a curve nobody has measured yet.

## 11. Mechanic 6 — Shop

**Screen:** Shop tab. Sells crystals (mock IAP for now, see §12), may sell direct resource
bundles, rewarded-ad placements ("watch an ad for N crystals" / "watch an ad to instantly
finish this timer"), and boosters (content TBD, e.g. temporary production multipliers).

## 12. Monetization posture (current phase)

- **IAP is stubbed.** All crystal purchases in the Shop currently route through a mock
  purchasing service that instantly "succeeds" and credits crystals — see
  `ARCHITECTURE.md` §IAP Abstraction. No store integration, no receipt validation yet.
  This is intentional: build and balance the crystal economy first, wire real payment
  later without touching gameplay code.
- **Save is local-only for now**, by explicit decision — no server-authoritative economy,
  no anti-cheat validation of currency yet. Acceptable while there's no real money in the
  loop. **Revisit this before real IAP ships** — a local save that can be edited to spawn
  crystals is a non-issue today and a real problem the day crystals cost money.
- Crystal *sinks* already defined by the brief: law refill, instant law queue refill,
  instant trade refresh, warehouse upgrade (alt path), level buy-up. Crystal *sources*
  today: Shop (mock), rewarded ads, occasional occurrence rewards. Keep sinks and sources
  roughly matched as content grows — this is a balance concern for playtesting, not
  something to solve up front.

## 13. Screens & navigation

Bottom navigation, 5 tabs:

1. **Laws** (§6)
2. **Trade** (§7)
3. **Economy** (§8)
4. **Kingdom** (home — map + population + Random Occurrences mailbox) (§9, §10)
5. **Shop** (§11)

## 14. Explicit non-goals (v1)

- No real-time multiplayer or player-to-player trading — "other kingdoms" in Trade are
  generated offers, not live players.
- No PvP, no leaderboards (unless you want to add this later — not in the brief).
- No procedurally generated map layout — regions and city placements are authored content.
- No server backend yet (see §12) — do not build cloud save speculatively.

## 15. Open questions for you to resolve during production

- Final list and count of businesses, cities/villages, and regions (content, not
  architecture — can grow after the code framework exists).
- Exact regional completion bonus per region.
- Whether "watch an ad" actions exist at launch or are a fast-follow.
- Whether random occurrences need the DDA layer (§10) or ship as pure RNG indefinitely.
