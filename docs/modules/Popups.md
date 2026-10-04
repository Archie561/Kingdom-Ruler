# Popups system — architecture

> A shared system: it belongs to no mechanic and every mechanic uses it.
> Project-wide rules are in `ARCHITECTURE.md`; §4.7 there is the short version of this file.

---

## 1. How it is used

Two steps, always chained:

1. **`_popups.Create<T>()`** makes a new popup of that type — **hidden, and with no data**.
   Nothing is on screen yet: no popup, no backdrop, no sound.
2. **`Show(data)`** or **`Ask(data)`** on the popup it returns **fills it with data and puts it
   on screen**. They differ in what happens next:

| | Fills the popup with data and shows it | Then |
|---|---|---|
| `Ask(data)` | yes | waits for **one** choice, closes the popup, returns the choice — a question |
| `Show(data)` | yes | leaves it open; read presses with `WaitForChoice()` until it closes — a panel |

Despite its name, `Ask` does not only ask: it is `Show` + `WaitForChoice` + `Close` in one call.

**Fixed data** — the common case:

```csharp
var choice = await _popups.Create<ConfirmPopup>().Ask(new ConfirmPopupData(title, body));

if (choice == ConfirmPopupChoice.Confirm)
    UpgradeWarehouse();
```

**Live data** — pass a function instead. The popup re-reads it while it is open, so a countdown
ticks and a price follows the game without the caller pushing anything (`LawsPresenter`'s refill):

```csharp
var choice = await _popups.Create<ConfirmPopup>().Ask(
    () => new ConfirmPopupData(
        _localization.Resolve(LawsUIText.StringTable, LawsUIText.RefillTitle),
        DescribeRefill(),
        canConfirm: CanAffordRefill),
    closeWhen: () => RefillCost <= 0);   // every law came back on its own meanwhile

if (choice == ConfirmPopupChoice.Confirm) CompleteRefill();
```

**A panel that stays open** across several presses, with another popup opening on top of it —
use `Show` and loop while it is open (no caller does this yet; the GDD §8 business panel is the
first planned one):

```csharp
var panel = _popups.Create<BusinessPopup>().Show(() => BuildBusinessData(id));

while (panel.IsOpen)
{
    var choice = await panel.WaitForChoice();

    if (choice == BusinessChoice.Collect)
        _manager.Collect(id);                        // the panel redraws on its next refresh

    if (choice == BusinessChoice.BuyAnother)
    {
        // A question on top of the panel. The panel stays open underneath, dimmed.
        var answer = await _popups.Create<ConfirmPopup>().Ask(() => new ConfirmPopupData(
            Resolve(EconomyUIText.BuyTitle),
            Resolve(EconomyUIText.BuyBody, _manager.NextUnitCost(id)),
            canConfirm: _manager.CanAffordNextUnit(id)));

        if (answer == ConfirmPopupChoice.Confirm)
            _manager.BuyAnother(id);                 // re-validates the price at this moment
    }
}
```

The loop needs no exit code of its own: when the panel closes (a backdrop tap, its own X button
calling `Close()`), `WaitForChoice` gives `null`, which matches neither `if`, and `IsOpen` is now
false. What is on screen at each step:

| Step | Stack (bottom → top) | Player can tap |
|---|---|---|
| Panel shown | Backdrop, **Panel** | the panel |
| "Buy another" | Panel, Backdrop, **Confirm** | only the confirmation — the backdrop covers the panel |
| Confirmation answered | Backdrop, **Panel** | the panel again |
| Backdrop tap | (empty) | the screen; the loop ends |

Nesting goes as deep as needed — a third level is one more `Create<…>().Ask(…)` inside the
second — and the backdrop always sits directly under whichever popup is on top.

The popup being open is just a pause in the middle of the calling method.

**Closing without pressing a button gives `null`** — the backdrop, `closeWhen`, or the popup
being destroyed. `null` is "no response": it never equals a real choice, so the caller needs no
branch for it.

---

## 2. How it works

| Class | Does | Written |
|---|---|---|
| `PopupManager` | creates popups (hidden); for the shown ones, keeps the stack, keeps the backdrop behind the top one, closes the top one on a backdrop tap, plays the open/close sounds | once |
| `Popup<TData, TChoice>` | appears when shown, animates, re-reads the data and redraws on change, checks `closeWhen`, hands the choice to the caller, destroys itself | once |
| `ConfirmPopup` and the like | draws its data, wires its buttons to `Choose(...)` | per kind of popup |
| the caller | what to show, and what to do with the choice | per use |

One open, start to finish:

1. `_popups.Create<ConfirmPopup>()` — the manager finds the prefab in the `PopupRegistry` and
   instantiates it on the popup canvas, **inactive**. Nothing is visible and nothing is stacked.
2. `.Ask(data, closeWhen)` — the popup activates itself and raises `Shown`. The manager, reacting
   to it, puts the popup on top of the stack, moves the backdrop directly behind it and plays the
   open sound. The popup then reads `data` once, draws it and scales in — all in the same frame,
   so the player never sees it empty. The caller is now suspended at `await`.
3. While open — every 0.25 s (`RefreshIntervalSeconds`, one constant for every popup) it checks
   `closeWhen`, re-reads `data`, and calls `Render` **only if the result differs** from what is on
   screen.
4. A button calls `Choose(ConfirmPopupChoice.Confirm)` — the caller resumes **on that tap**, with
   the choice. `Ask` then closes the popup.
5. Closing — the manager is told first: it drops the popup from the stack and moves the backdrop
   behind the next one (or fades it out). Then the caller is released. The popup animates out on
   top of everything and destroys itself.

---

## 3. Layout

```
Shared/Popups/
  Scripts/
    Core/                the mechanism — nothing here knows any specific popup
      PopupManager         opens popups, owns the stack and the backdrop
      Popup                the non-generic base: animation, closing (Popup.cs)
      Popup<TData,TChoice> the base every popup derives from (Popup.Generic.cs)
      PopupRegistry        every popup prefab in the game
    ConfirmPopup/        one kind of popup: ConfirmPopup, ConfirmPopupData, ConfirmPopupChoice
  Prefabs/               ConfirmPopup.prefab
  ScriptableObjects/     PopupRegistry.asset
```

**Delete a kind's script folder and `Core/` must still compile.** That is the test for whether
something belongs in `Core/`.

A module's own popups (a Trade offer, an occurrence letter) live in that module —
`Modules/<X>/Scripts/Views/` and `Modules/<X>/Prefabs/` — and are added to the same shared
registry. A serialized reference crosses assemblies freely, so this creates no asmdef cycle.

---

## 4. Adding a popup

1. A **data** struct — `XPopupData`, a `readonly struct` of display values, already localized.
2. A **choice** enum — `XPopupChoice`, one member per button. Every button is a choice, Cancel
   included; "closed without choosing" is `null` and needs no member.
3. A **view** — `XPopup : Popup<XPopupData, XPopupChoice>`: implement `Render(data)`, and in
   `Awake` wire each button to `Choose(...)`.
4. A **prefab**: the view on a full-screen root, with a `Panel` child that scales in. No
   backdrop — the manager owns the only one.
5. Add the prefab to `PopupRegistry.asset`.

Fixed labels (button text) are `LocalizeStringEvent` components on the prefab; text built from
data is resolved by the caller and passed in the data (`ARCHITECTURE.md` §2).

---

## 5. Rules

**The data function only reads.** It runs several times a second, so a side effect inside it —
spending, saving, logging — would repeat. Short lambdas are fine inline; move one into a named
method only when it gets too long to read comfortably.

**Capture ids, re-read the model.** A lambda that captures a value which later changes shows
stale data. Capture the id of the thing (`() => BuildBusinessData(id)`) and read the rest fresh.

**Money waits for the choice.** Opening a purchase confirmation spends nothing. The charge happens
after `Confirm`, through the Manager, which re-reads and re-validates the price at that moment —
never a price captured when the popup opened.

**One stack, one backdrop.** `PopupManager`'s list is the only record of what is open, and its
single dimmer sits directly behind the topmost popup, so older popups are dimmed and unreachable.

**No haptic on open.** `GDD.md` §3 limits haptics to swipes, purchase confirmations and level-ups.
The purchase that follows a `Confirm` triggers its own.

---

## 6. Decisions that look arbitrary but are not

**Polled, not pushed.** The previous design was event-driven: the caller held the popup's data
object and pushed changes into it. That made the caller know every event that could change what
the popup showed, forward each one, run its own timer anyway (time has no event), and unwire it
all on close — about 70 lines in `LawsPresenter` for one question, and a missed event left the
popup silently stale (its title never followed a locale change). Re-reading one function at 4 Hz
while a popup is open costs nothing noticeable and cannot miss a change. It is the same approach
the Laws countdown and the warehouse bars already use (`ARCHITECTURE.md` §4.5).

**Redraw only on change.** `Render` runs when the new data differs from the last drawn data,
compared with `EqualityComparer<TData>.Default` — field by field for a struct, so strings compare
by value. A data struct holding a list compares the list by reference and so redraws on every
refresh; give it `IEquatable` if that ever matters.

**No "declined vs dismissed".** Every caller treated both as "don't do it". Closing without a
choice is simply `null`; a popup that needs an explicit "later" option makes it a button.

**`Create`, not `Open`, and hidden until shown.** The manager's method used to be `Open`, and it
already put the prefab on screen — dimmed, with a sound — before it had any data, so a forgotten
`Show` left a blank popup over a dark screen. Now the name says what happens and the behaviour
matches it: `Create` only builds the popup, inactive; `Show` / `Ask` are what display it. The
backdrop, the stack and the sounds stay the manager's alone — the popup merely raises `Shown`, as
it already raised `Closed`, and the manager reacts. A popup created and never shown is invisible
and is never stacked, so the worst a forgotten `Show` can do is leave an inert object behind.
`GetPopup` was considered and rejected: "get" suggests fetching one that exists, while every call
makes a new one.

**`Ask` keeps its short name.** It also fills and shows the popup, and also closes it — but the
complete name, `ShowAskAndClose`, is too long, and `AskAndClose` is no more complete than `Ask`.
Closing is part of asking: a question is over once it is answered. The contrast that matters is
`Ask` (one answer, done) against `Show` (stays open), and the doc comments spell out the rest.

**One expression, `Create<T>().Ask(data)`, rather than `Create<T>(data)`.** Passing the data to
the manager would need its type, which C# cannot infer from `T`: the call would spell out
`Create<ConfirmPopup, ConfirmPopupData, ConfirmPopupChoice>(…)`, or the data would degrade to
`object`, or the data type would have to name its choice type again. Fixing `T` first lets `Ask`
know the exact data and choice types, all checked at compile time.

**Popups are found by their own type** — `Create<ConfirmPopup>()` names what appears. The
registry field is typed `Popup[]`, so the Inspector refuses a prefab without one, and it warns
about two prefabs of the same type.

**A missing popup throws** (`InvalidOperationException`) rather than quietly giving `null`. It is
a setup mistake and should be loud; whatever the caller would do after a "yes" never runs either
way, so it still fails closed.

**The choice arrives on the tap**, before the close animation — so the purchase, its sound and its
haptic are not delayed by ~130 ms of tween.

**The manager hears about a close before the caller resumes**, so a caller that opens the next
popup straight away gets it on top of a stack that is already correct.

**A press with nobody waiting is ignored.** Between one `WaitForChoice` and the next the caller is
still handling the last press, so a fast double tap cannot buy twice. `Ask` needs no guard against
a second tap on the button that opened it either: the popup and its backdrop cover that button
from the next frame, and the purchase re-validates regardless.

**A popup that cannot draw itself closes.** An exception from the data function, `closeWhen` or
`Render` is logged and the popup closes with no choice — so nothing is bought from a broken popup,
and no orphaned popup stays on screen.

**`Update` and `OnDestroy` are `protected virtual` on the base.** Unity calls only the most-derived
declaration of a message method, so a popup declaring its own would silently switch off the
refresh clock or the "destroyed means closed" guarantee. Making them virtual turns that mistake
into a compiler warning; override and call the base.

**The caller holds a View.** A Presenter calls `Ask` on a popup MonoBehaviour directly. It
already depended on the `PopupManager` MonoBehaviour, popup flows were never EditMode-testable,
and an interface invented only for a fake is what `ARCHITECTURE.md` §8 rules out.

**No `CancellationToken` parameter.** `closeWhen` covers "the question stopped making sense", the
only out-of-band close any caller needs. Add a token when a caller has a lifetime to tie a popup
to.

**`Popup.Generic.cs`.** `Popup` and `Popup<TData, TChoice>` share a name, like `Task` and
`Task<T>`, and two files cannot both be `Popup.cs`.

**The manager is a MonoBehaviour in the Bootstrap scene**, so anything constructed at startup can
be given it directly. Elsewhere "Manager" means a plain-C# module Model; this one owns a canvas.

---

## 7. Verification

Popups are MonoBehaviour and DOTween, so they are verified in Play mode rather than by EditMode
tests (`ARCHITECTURE.md` §8). Clicks go through the real `EventSystem` raycast — invoking
`onClick` directly would bypass the raycaster and pass either way.

Verified on 2026-10-02, after the rewrite:

- Live data redraws while open (a per-second body text; the refill countdown 4:00 → 3:47).
- Confirm and Cancel each give their choice, on the tap; an immediate second tap is ignored.
- Two popups stack as `older, backdrop, newer`; a tap on the older popup's button lands on the
  backdrop and closes only the newer one, with `null`.
- A closing popup stays drawn above the backdrop while it animates out.
- `closeWhen` turning true closes the popup with `null`.
- A data function that throws logs and closes the popup with `null`.
- Refill: the price shown (4) is the price charged, on Confirm; a backdrop tap charges nothing.
- A popup cleans up after itself: destroyed, backdrop hidden once the stack is empty.

Re-verified on 2026-10-03, after `Open` became `Create` and popups started hidden:

- A created, never-shown popup is inactive, unstacked and leaves the backdrop off; closing it
  destroys it at once.
- A popup whose `closeWhen` is already true at `Show` closes on its first refresh with `null`.
- A panel opened with `Show` stays open across two Confirm presses, redraws its press count, and
  its `while (panel.IsOpen)` loop ends when a backdrop tap closes it.
- A question stacked on that panel covers it — a tap on the panel's button lands on the backdrop
  and closes only the question — and the backdrop returns beneath the panel afterwards.
- Refill end to end through `LawsPresenter`: countdown 4:00 → 3:53, Confirm charged the 4 shown.

Two traps when driving this from MCP: enter Play mode from **Bootstrap**, never Main, or nothing
is injected; and with *Run In Background* off the player loop stops while the Editor is
unfocused, so nothing animates and no graphic is raycastable (`depth == -1`) until it renders.

---

## 8. Known gaps

| Gap | Note |
|---|---|
| Input during the open animation | A very fast tap can answer a popup that is still scaling in. |
| Up to 0.25 s stale buttons | `CanConfirm` follows the data at the refresh rate; harmless because the purchase re-validates. |
| Custom button labels | Labels come from the prefab; a popup needing different ones carries them in its data. |
| Android back | Not wired. Should close the top popup through `PopupManager`. |
| No pooling | Instantiate and destroy per popup — fine at this frequency. |
| Trade's confirm panel | Still a screen-local panel (`docs/modules/Trade.md`); moving it to a `TradeOfferPopup` is the next migration. |
