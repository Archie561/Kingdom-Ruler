# AGENTS.md — Working rules for Claude Code on this project

This is a Unity 2D mobile game (portrait, pixel-art kingdom sim). Before doing anything
else in a new session, read:

- `docs/GDD.md` — what the game is and how each mechanic is supposed to work.
- `docs/ARCHITECTURE.md` — how the codebase is organized and why.

Treat both as living references, not one-time context. If a task seems to require a
mechanic or a system not described in either document, stop and flag it rather than
inventing one — see §3.

## 1. What this project cares about, in priority order

1. **Correctness of the resource ledger.** Gold, crystals, characteristics, and trade
   resources all move through one source of truth (`KingdomLedger`, see `ARCHITECTURE.md`
   §4.3). A bug here is a save-corrupting or economy-breaking bug, not a cosmetic one.
2. **Save integrity.** Nothing should ship that can silently corrupt or lose a player's
   save. When in doubt, write the defensive check.
3. **Feel.** Every player action needs its animation/sound/haptic hook (see `GDD.md` §3). A
   feature isn't "done" if it's functionally correct but silent and static — see the
   definition of done in §5.
4. **Simplicity.** This game has six interlocking systems already; don't add a seventh
   abstraction layer to solve a problem that hasn't shown up yet. If you notice yourself
   reaching for a design pattern "in case we need it later," don't — flag it as a
   suggestion instead and let the human decide.

## 2. Autonomy tiers

The human has asked for a **mixed** working style: move fast on the routine stuff, slow down
and check in on anything load-bearing. Concretely:

### 🟢 Proceed autonomously (build it, then summarize what you did)
- UI layout and styling within an existing screen.
- New content entries that follow an existing data schema (a new law card, a new business,
  a new city) — as long as the schema itself isn't changing.
- Bug fixes with a clear, reproducible cause.
- Writing or extending tests.
- Refactors that don't change any public API or file layout described in
  `ARCHITECTURE.md`.
- Boilerplate: a new MonoBehaviour view for an existing manager, a new UI prefab wiring.

### 🟡 Propose a short plan first, then implement after a go-ahead
- Adding a new manager/system, or a new ScriptableObject data type.
- Any change to the `KingdomLedger` public API or to how any of the 6 characteristics/6
  trade resources/gold/crystals are read or written.
- Changes to the save DTO shape (even additive ones — bump `schemaVersion` and say so).
- Folder structure changes beyond what `ARCHITECTURE.md` §3 already describes.
- Adding a new VContainer `LifetimeScope` (child scopes beyond the single root scope) or
  changing how something is registered/resolved.
- Anything that touches offline/idle-accrual timing math (§4.5 in `ARCHITECTURE.md`) — this
  is easy to get subtly wrong and hard to notice until a player reports a bug.

### 🔴 Stop and ask explicitly — do not proceed without a direct answer
- Anything touching `IPurchasingService` or the eventual real-money IAP wiring, even in its
  current mock form. Monetization code gets a human's eyes before it merges, always.
- Deleting or bulk-overwriting scenes, prefabs, or ScriptableObject assets.
- Changing the save file format in a way that would break existing local saves without a
  migration path.
- Rewriting git history, force-pushing, or anything that discards work.
- Adding a new third-party package/dependency.
- Changing Unity version, render pipeline, or other project-wide settings.

If a task spans tiers (e.g. "add a new city" is 🟢 content but touches the region-completion
bonus which might be 🟡), do the 🟢 part and flag the 🟡 part separately rather than
blocking the whole task.

## 3. When the GDD or Architecture doc doesn't cover something

Say so explicitly, propose the smallest addition that would cover it, and ask whether to
add it to the relevant doc before or after implementing. Don't quietly improvise a new
mechanic, currency, or sink/source and let it exist only in code.

## 4. Unity-specific hazards (these cause real damage if ignored)

- **`.meta` files carry GUIDs that other assets reference by ID.** Never hand-edit a `.meta`
  file. If you rename or move an asset from outside the Editor (via bash/file tools), its
  `.meta` file must move/rename with it, or references break silently. When a rename touches
  anything with a `.meta` file (basically anything under `Assets/`), prefer to describe the
  rename and let the human do it inside the Editor, or move both files together explicitly
  and say so.
- **Scenes and prefabs are YAML.** They're diff-hostile and easy to corrupt with a
  hand-edit. Prefer expressing changes as C# code (a script that configures itself at
  runtime, or an editor script) over hand-editing scene/prefab YAML. If a YAML edit is
  unavoidable, keep it minimal and call out exactly what changed.
- **Don't assume the Editor is closed.** Unity locks some files while running. If a build or
  test command fails mysteriously, check whether that's the cause before debugging the
  actual code.
- **Newtonsoft.Json + IL2CPP.** Save/load can work perfectly in the Editor and silently fail
  on a real device build, because IL2CPP's code stripping can remove reflection members
  Newtonsoft needs. If you touch save code, check a `link.xml` is protecting the
  Newtonsoft.Json assembly, and flag that on-device testing (not just Editor testing) is
  needed before this is considered verified.
- **Canvases are split on purpose** (one per screen, plus separate canvases for anything
  that animates frequently — see `ARCHITECTURE.md` §2/§9). Don't merge UI back onto a single
  Canvas "to simplify" — that undoes a deliberate mobile-performance decision.
- **One agent/person per scene at a time in practice** — scene merge conflicts aren't
  meaningfully resolvable by a text-based merge tool. If a task would touch a scene another
  change is also touching, flag the collision instead of proceeding blind.

## 5. Definition of done

A task isn't complete until:

- [ ] It compiles with no new warnings introduced.
- [ ] Relevant EditMode tests exist and pass (domain logic changes should come with tests,
      per `ARCHITECTURE.md` §8).
- [ ] New code lives in the right place per `ARCHITECTURE.md` §3 — inside its module
      (`Modules/<X>/Scripts/{Domain,Presenters,Views}` + the module's `Manager`) or, if it's
      cross-cutting, under `Shared/` or `Core/`, not invented as a new top-level folder.
- [ ] Any new user-facing text goes through an Unity Localization String Table key, not a
      hardcoded string.
- [ ] Any player-facing action has its feel hooks — animation, sound, haptic where the GDD
      calls for one — at least stubbed, even if final art/audio lands later.
- [ ] `GDD.md` / `ARCHITECTURE.md` are updated if the task changed what they describe.
- [ ] A short summary of what changed and why is given at the end — don't just say "done."

## 6. Communication style

- State assumptions explicitly and proceed for 🟢-tier ambiguity (e.g. "I'll place this in
  `Domain/Economy/` since it's pure cost-curve math, following the pattern in
  `ARCHITECTURE.md` §3").
- For 🟡/🔴-tier ambiguity, ask — don't guess and hope. A wrong guess in the ledger or save
  system is expensive to unwind later.
- If something in `GDD.md` looks internally inconsistent or unbuildable as written, say so
  and propose a fix rather than silently picking an interpretation.

## 7. Explicit "never do" list

- Never write real store billing code without being asked — the current `IPurchasingService`
  mock is intentional (see `ARCHITECTURE.md` §6, `GDD.md` §12).
- Never introduce a new currency, sink, or source not described in `GDD.md` without flagging
  it as a proposal first.
- Never commit `Library/`, `Temp/`, or other generated Unity folders (see `.gitignore` in
  `ARCHITECTURE.md` §11).
- Never add ECS/DOTS or Addressables speculatively — `ARCHITECTURE.md` §1.8 explains why, and
  when those would become worth reconsidering. (VContainer is the one DI framework this
  project uses, deliberately — don't add a second one, and don't nest child `LifetimeScope`s
  beyond the root scope without a concrete need, see §2 above.)
- Never use UI Toolkit for gameplay UI — this project is UGUI + TextMeshPro only, by
  explicit decision.
- Never construct a `Tween` or animation directly inside a Manager or Presenter — DOTween
  usage belongs in the View layer only (`ARCHITECTURE.md` §2, §4.4).
- Never use `JsonUtility` for save data — this project standardizes on Newtonsoft.Json (see
  the IL2CPP hazard note above).
- Never cram a module's content list (all its cities, businesses, cards, or events) into one
  big Config SO — content items are one-asset-each under that module's `Data/` folder;
  `Config/` is for tunable parameters only (`ARCHITECTURE.md` §7).
