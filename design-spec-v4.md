# bSolitaire — Design Spec v4: Last-Mile Polish & the Loss Experience

**Version:** 4.0 — Nov 22, 2026 — proposal, written against the code as it stands after
PRs #23–#28 (master `d82334e`). Supersedes spec v2 (this file's previous version).
Numbered v4 because the repo's own as-built record already calls itself v3.0 — this is
the next proposal after it, not a rival claim to the number.
**Repo:** github.com/brandonchastain/bSolitaire — Blazor WebAssembly, C#/.NET 9, 2D canvas.
**Companion record:** the repo's own `design-spec.md` is now the as-built account of
v2's features (#27). This v4 does not re-litigate it; it starts from what shipped.
**Scope:** (1) finish the winnable-deals promise — it is the only mode, and it has one
known hole; (2) replace the "you lost" experience with the one the good solitaire games
ship; (3) a solver-backed hint, the one good-game feature the game still lacks.
Draw-3, timing/stats, and pass-aware stuck detection shipped in #24–#26 and are **not**
re-specified here.

---

## Part 1 — What spec v2 got wrong (the retrospective Brandon asked for)

Read against PRs #23–#28, v2's misses fall into three kinds.

### It solved the wrong problem

- **Feature 3 was three tiers of *detecting* a lost game sooner. The fix that shipped —
  #23, vetting deals before they're dealt — was not in the spec at all.** Brandon's own
  framing ("you should have prevented that situation") pointed at prevention; the spec
  still led with detection. Once #23 landed, Tier 2 (the unwinnable-position cache) and
  Tier 3 (the advisory "looks lost" offer) were dropped as consolation work for a problem
  removed at the source. Lesson carried into v4: when the complaint is about what the
  player is *handed*, look at the dealer before the referee.

### Three factual claims about the code were false

Each is struck through, with the correction, in the repo's as-built spec (#27). Summarized:

- **"The `Solver` consumes `LegalMoves`, so it inherits draw-3."** False. The solver has
  its own `Packed`/`Successors` search and models the deck as an unordered set — it is
  **draw-count blind**. Draw-3 needed no solver changes, and the solver's `Winnable`
  verdict under draw-3 is an *overclaim* (its model is more permissive than the real
  game). This is the direct cause of Feature A below.
- **"`Board.RecycleWaste()` is unchanged, so draw-3 grouping repeats identically."**
  False, and it hid a real bug: the stock draw wasn't turning the drawn packet over, so
  draw-3 showed the wrong card and regrouped every pass. v2 waved at the exact line
  where the bug lived and called it "no work."
- **"rAF suspends in hidden tabs, so background time excludes itself."** False.
  `Update`'s `elapsed` is the raw rAF timestamp; suspension stops frames, not the clock.
  #26 accumulates per-frame deltas and discards gaps over 250ms. v2's mechanism would
  have baked every hidden-tab minute into the recorded game times.

### It never thought about the seams

- **Startup ordering.** v2 put the draw setting on the persisted record and the deal in
  the dealer, and never asked which happens first. #23's `Begin()` dealt the first game
  before `LoadScore()` ran, so draw-one "didn't take" (#28) — the record arrived a
  moment after the deal it was supposed to govern.
- **The `CardRect` contract.** v2 said "the drawer keeps asking layout for rects" and
  missed that anchoring the waste fan to the *end* of the pile changed the meaning of a
  rect for every consumer. A defaulted `pileCount = 1` let four call sites compile while
  wrong: hover outline, drop-target ring, grab offset, flight origin (#28). A signature
  change with a default is a silent API break; the spec should have enumerated the
  consumers.
- **Its own rule's player-facing consequence.** "The setting takes effect next deal,
  never mid-deal" is correct — and reads as a broken button. The shipped fix asks and
  re-deals (#28). A spec that changes when a setting applies has to say what the player
  sees when they flip it.
- **Display real estate.** v2 proposed extending the bottom score line with averages;
  the line was already full and already overlaps the stats overlay on phones. Times went
  to the banner only. `FastestWinSeconds` was specified, built-ready, and dropped as a
  field nobody asked for.

---

## Part 2 — Where the code stands now (facts v4 is written against)

- **Deals are vetted, and only vetted deals are the game.** `DealVetter` shuffles
  candidates in the idle frames of the game in progress and keeps the first the `Solver`
  proves `Winnable` at `SolverBudget.Default` (measured: 63% of openings prove winnable,
  median 1ms; `Thorough` buys 68% at ~30× the p90 cost and was rejected). It consumes
  only the `Winnable` half of the verdict — anything else discards the candidate.
  `TakeDeal()` falls back, after 8 immediate attempts inside a 250ms ceiling, to dealing
  an **unvetted** shuffle, silently. Rare by construction (~4-in-10,000 by count alone),
  but it exists and nothing counts it.
- **A deal is a value.** `Dealer.Deal(position, order)` deals a stored
  `(Suit, Rank)[52]` order deterministically — "the same order always deals the same
  position." The order for the deal in play is currently *not retained* after dealing.
- **The solver returns a verdict, never a line.** `SolveResult` is
  `Searching`/`Winnable`/`Unwinnable`; `Solver` keeps no path. Any feature wanting "the
  move that wins" must either reconstruct one or search per candidate move.
- **The mid-game referee is still v2-era.** `Analyzer` runs a fresh `Solver` per
  `Board.Version`; on proof it calls `Board.MarkUnwinnable()`, and `DrawBanner`
  immediately dims the board: "This deal is lost" / "No line from here wins," with a
  single action — `layout.NewGameButton` is the banner's only button in `Controls`.
  `Stuck` ("No moves left") shares the same one-button banner.
- **Undo is snapshot-based** (cap 200) and snapshots carry `GameState`, so undoing off a
  terminal board is mechanically plausible — but no banner path offers it today.
- **Draw-3's vetting promise is weaker than draw-1's**, per Part 1: the vetter's solver
  cannot see draw grouping, so a kept draw-3 deal is proven winnable only in the
  permissive model. `Unwinnable` verdicts stay sound in both modes (if the permissive
  game can't win, the real one can't); only `Winnable` overclaims. The stats overlay
  displays the verdict without that distinction.
- **Invariants still hold** (repo spec, "Invariants"): game layer knows no Blazor/JS/
  canvas; `BoardLayout` is the only geometry; layout blends, never switches; the solver
  never guesses — `Unknown` is never presented as a verdict. Everything below is written
  inside those lines.

---

## Feature A — Winnable-only, finished: close the draw-3 hole and count the fallback

### Goal
The promise "you are never handed a deal that can't be won" should be equally true in
both draw modes, and the rare unvetted fallback should be visible to the developer
(Brandon), not silent.

### Design

**A1. Measure the leak before fixing it.** The size of the draw-3 overclaim is currently
unknown, and the fix's cost depends entirely on it. Add a test-side measurement, not
shipping code: sample N (≥ 500) `DealVetter`-kept draw-3 deals and check each with a
draw-aware exhaustive search (a test-only solver variant whose successors respect
draw-3 stock grouping — it can be slow; it runs offline). Report the share of kept
deals that are actually dead under real draw-3 rules.

- If the leak is small (order ~1–2%): document it in `DealVetter`'s summary next to the
  existing promise wording, adjust the promise's wording to match reality, and stop.
- If it is material: **A2**.

**A2. (Only if A1 says so) Vet draw-3 deals against a draw-aware model.** Give the
*vetter's* search — not the mid-game `Analyzer` — a successor model that respects draw
grouping, used only when the deal being vetted is a draw-3 deal. The mid-game solver
deliberately stays as it is: its `Unwinnable` is sound in both modes, and making it
draw-aware can only *shrink* what it can prove, later. Keep the change behind the
`Solver`'s existing successor seam if one exists; if it doesn't, a vetting-only search
class in the style of `DealVetter` itself (single-purpose, heavily commented with the
measurements that justified it). Budget rules follow #23's precedent: default budget,
keep-on-`Winnable`-only, discard on anything else, and re-measure yield and p90 before
shipping — a lower yield is acceptable (more candidates per deal), a slower frame is not.

**A3. Count the unvetted fallback.** `DealVetter.TakeDeal()` returning null currently
deals a plain shuffle with no trace. Increment a counter on `DealVetter` (alongside the
existing `Discarded`), surface it in the stats overlay next to `Discarded`, and — since
the fallback deal carries no promise — do **not** change player-facing wording for it.
This is a developer instrument: if the counter ever moves in real play, the 250ms
ceiling or the attempt count is wrong for that machine, and that's worth knowing.

**A4. Honest verdict display under draw-3.** Until/unless A2 ships, the stats overlay's
`Winnable` reading under draw-3 claims more than was proved. Label it for what the
solver actually established (the overlay already distinguishes verdict states; the
draw-3 `Winnable` case is the permissive-model one). Wording is Brandon's call; the
requirement is only that the overlay not state the strong claim in the mode where it
wasn't proved. `Unwinnable` needs no such label in either mode.

### Acceptance criteria
- A1's measurement exists as a repeatable test/tool with its N and result recorded in
  `DealVetter`'s documentation, whichever way it comes out.
- If A2 ships: sampled kept draw-3 deals show a dead-share at or near zero under the
  draw-aware check; vetting yield and p90 timings re-measured and recorded; draw-1
  vetting behavior unchanged.
- Forcing `TakeDeal()` to exhaust its attempts in a test increments the fallback
  counter and still deals a playable game.
- No change to `Solver`'s contract: `Unknown` is still never a verdict, anywhere.

---

## Feature B — The loss experience: a way back, not a verdict

### Problem, in this codebase's terms
Deals are now vetted, so a mid-game `Unwinnable` proof no longer means "the game dealt
you a dead deal." It means "**you** stepped off the winning line" — and the game responds
with the harshest artifact it has: the board dims, "This deal is lost," one button,
New Game. Two players feel this differently and both badly:

- A casual player (Brandon's mom) can step off the line in her first moves — the
  *correct*, normal moves, played in the other order — and be bannered almost
  immediately. The deal was winnable; the experience is identical to being handed a
  dead one.
- An experienced player (Dean) *wants* the early knowledge — playing out dead moves was
  his original complaint — but what he wants at that moment is to go back and choose
  differently, not to throw the deal away.

Microsoft's Klondike, and the good apps generally, resolve this the same way: **they
never declare a deal dead mid-game at all.** The loss screen waits for "no moves left,"
undo is always available, and in curated/winnable modes a loss means "restart this deal
and find the line." bSolitaire can do better than copying the silence — it *knows*
things those games don't — but the knowledge should arrive as an offer of a way back,
not as a verdict.

### Design

**B1. The `Unwinnable` proof stops raising the banner.** When `Analyzer` proves a
position unwinnable, the game no longer calls the modal. Instead:

- A quiet, non-modal notice appears in the same visual family as the FastForward offer
  ("Finish ▶▶" is the existing precedent: an overlay affordance, not a panel): wording
  to the effect of **"No winning line from here"** with a **"Back up"** action (B2).
  It does not dim the board, does not block input, and the player may keep playing —
  some players finish a lost deal on purpose, and the game has no business stopping them.
- The notice **withdraws** the moment the position is no longer proven unwinnable
  (an undo, typically — `RefreshState` already overwrites the verdict state on the next
  position change, and the `Analyzer` re-searches per `Version`; the notice keys off the
  same lifecycle).
- The full banner is thereafter reserved for `Stuck` — the position where there is
  genuinely nothing left to do — which is when the good games show theirs.

**B2. "Back up": return to the last position that could still be won.** The one-click
form of undo, and the centerpiece of this feature:

- `Solitaire` (or `Analyzer`) tracks the **anchor**: the most recent `Board.Version`
  at which the `Analyzer`'s verdict for that version was `Winnable`. The deal's opening
  position is the anchor's floor — it was proven winnable by the vetter before dealing
  (draw-1 exactly; draw-3 subject to Feature A's caveat, and the anchor logic does not
  care — it uses verdicts, not promises).
- "Back up" undoes, through the existing snapshot undo, until `Version` equals the
  anchor. Snapshot undo already restores pile contents, face-up flags, and `GameState`,
  so this is a loop over an existing mechanism, not a new state path. If no anchor
  exists (the solver stayed at `Unknown` for every earlier version — possible, since it
  restarts per move), the action degrades to a single undo step; if there is nothing to
  undo, it is not offered.
- Honesty rule, inherited from the invariants: the anchor is a position the solver
  *proved* winnable, never one it merely failed to prove unwinnable. `Unknown` versions
  are skipped over, not anchored on. The button's wording must not promise more:
  "Back up" / "Back to a winnable position" are both accurate; anything like "undo the
  mistake" is not (the solver doesn't know which move was the mistake — only that the
  anchor predates it).

**B3. The `Stuck` banner gains the recovery actions.** When the banner does appear
(no moves left), it offers, in this order:

1. **Back up** (B2), when an anchor exists — a stuck position is usually a few moves
   past the fork, not fifty.
2. **Replay this deal** (B4).
3. **New game** — the existing action, unchanged.

Plain **Undo** also remains reachable from the banner (the command exists; the banner
currently swallows input except its one button — `Controls.Down`'s `bannerShown` path
is the seam). `BoardLayout` gains the banner button rects next to `NewGameButton` —
all geometry stays in layout, interpolated like everything else, and the banner must
still fit phone widths; if three buttons crowd a narrow layout, stack them vertically
rather than shrinking text. `CanvasDrawer.DrawBanner`'s copy changes:

- `Stuck`: headline stays "No moves left." The detail becomes the recovery framing:
  back up and try a different way, or replay the deal. (The strong claim "this deal
  was winnable" is the vetter's claim in the permissive model only under draw-3 —
  keep the detail line mode-neutral until Feature A closes, per B3's caution.)
- `Unwinnable` no longer reaches the banner at all (B1), so its current copy —
  "This deal is lost" / "No line from here wins" — is retired from the banner and
  survives, softened, only in the B1 notice.

**B4. Replay this deal.** Retain the current deal's order and re-deal it:

- Whoever takes the vetted order from `DealVetter.TakeDeal()` keeps it as per-deal
  state on `Board`, alongside the draw count stored there by Feature 1 — call it the
  deal's identity. `Snapshot` does not need it (it cannot change mid-deal, same
  argument as the draw count).
- Replay = flush the current attempt through the existing abandoned-deal path in
  `ScoreKeeper` (played, not won, with its duration — the mechanism from spec v2's
  Feature 2, reused exactly), then deal the retained order again with the same draw
  mode, as a **new** `DealId` with a fresh timer. A replay is a new attempt at the
  same puzzle, and the record should say so.
- Replay must not consume the vetter's prepared next deal, and must not re-vet: the
  order was vetted once, before it was first dealt.
- Edge: the unvetted fallback deal (Feature A3) has an order too — replaying it is
  equally well-defined. No special case.

**B5. What does not change.** The solver's standard of proof; `MarkUnwinnable`'s
contract (B1 changes what the *presentation* does with the verdict — if the state
itself becomes presentation-only bookkeeping, renaming is allowed, but the verdict's
meaning is not); the once-per-deal `DealId` counting; `Stuck` detection (#24's
pass-aware check). Nothing auto-undoes, auto-deals, or force-ends a deal: every
recovery is a button the player presses.

### Acceptance criteria
- Deal a vetted game, deliberately play off the winning line until the solver proves
  `Unwinnable`: no banner, no dim; the notice appears; the board remains fully
  playable; continuing to play changes nothing about the notice except its withdrawal
  conditions.
- From the notice, "Back up" lands exactly on the last solver-proven-`Winnable`
  position (assert `Version` and pile contents in SessionTests), with the notice gone
  and the deal still in progress and countable exactly once.
- In a deal where the solver never resolves `Winnable` mid-game (stays `Unknown`),
  no anchor is claimed; the notice's action is a single undo or absent, per B2.
- `Stuck` banner shows Back up / Replay / New game; each works from touch and mouse;
  buttons do not overlap at phone width (LayoutTests on the banner rects).
- Replay deals the identical opening position (same order ⇒ same position, per
  `Dealer`'s contract), resets the timer, counts the abandoned attempt once, and the
  replayed attempt counts once when it ends. The vetter's next prepared deal is still
  the one dealt after a subsequent New game.
- Undo from the `Stuck` banner restores the pre-terminal position with `GameState`
  back to `Playing` (UndoTests case off a terminal snapshot).
- No regression: #24's stuck positions still banner; solver verdicts on sampled
  positions unchanged.

---

## Feature C — A hint that knows the winning line (stretch)

### Goal
The remaining good-game feature the game lacks. A hint here can be better than most
apps': the solver can certify the suggested move keeps the deal winnable, and says so
only when it can.

### Design
- The `Solver` returns no line (Part 2), so do **not** grow path storage into it for
  this. Instead, hint-by-verification, on demand: take the position's legal moves
  (progress moves first, in `LegalMoves`' existing sense), and for each candidate,
  apply it to a scratch position and run a fresh `Solver` at `SolverBudget.Default`;
  the first candidate whose successor proves `Winnable` is the hint. This reuses the
  `DealVetter` pattern (scratch position, keep-on-`Winnable`, discard otherwise) at
  interaction scale.
- It runs across frames like `Analyzer`/`DealVetter` do (4ms slices, paused during
  drags and animations) — a hint may take a few frames to arrive; show nothing until
  it resolves. Candidate count is small (legal moves, not the tree), and per-candidate
  cost is the vetter's measured cost: most openings resolve in ~1ms.
- Outcomes are three-valued and the UI must keep them distinct: a **certified** hint
  (highlight the card and its destination), **no certified move found** (every
  candidate exhausted budget at `Unknown`, or the position is proven `Unwinnable` —
  in which case the B1 notice is the honest answer, not a hint), and **still
  thinking**. Never present an uncertified move as a hint; a "best guess" mode is
  explicitly out.
- A hint button joins the existing corner controls, following the mute/stats pattern.
  No score or stats consequence; hint usage is not tracked (nobody asked, and the
  record stays O(1)).

### Acceptance criteria
- In an arranged position with exactly one win-preserving move, the hint names it.
- In a proven-`Unwinnable` position, no hint is offered and the B1 notice is what shows.
- In a position where all candidates exhaust budget, the hint resolves to "no certified
  move found" and suggests nothing.
- Requesting a hint mid-drag or mid-animation defers until the board is still.

---

## Cross-cutting polish (small, named so they stop being pre-existing)

- **Phone overlap:** the bottom score line overlaps the `ShowStats` overlay on phones
  (noted in #26's deviations, still open). Fix within the existing layout rules —
  geometry in `BoardLayout`, blending not switching — or move the overlay, not the
  score line (the drawer's redraw cache keys off the summary string).
- **Banner real estate:** B3's extra buttons are the second tenant of the banner;
  resolve both in one layout pass so the fixes don't fight.
- **First deal of a session** still goes through `TakeDeal()`'s 250ms immediate path
  when the vetter has nothing prepared; with B4's order retention, also confirm the
  session's first deal records its order (it is dealt by a different path than
  `Reset` — #28's lesson is that `Begin()` is exactly where ordering assumptions go
  to die).

## Out of scope
- Making the mid-game `Analyzer` draw-aware (Feature A2 vets at deal time only, and
  only if measured necessary).
- Difficulty tiers / graded deals (the vetter could select for solution length, but
  nobody has asked, and "winnable, period" is the household's actual want).
- Numbered/shareable deals — the order-as-value design makes them nearly free, and
  they are still a feature nobody requested. Noted only so the omission is a decision.
- Scoring, Vegas rules, redeal limits, cloud sync — as before.

## Ship order
1. **B1 + B3 copy/button changes** — smallest diff, removes the harshest artifact.
2. **B2 anchor + Back up** — the substance of the new loss experience.
3. **B4 replay** — needs B3's banner to live in, and the order retention.
4. **A1 measurement**, then A3/A4 (cheap), then A2 only if measured.
5. **C** last — it is the stretch, and B's notice gives its "no hint" case a home.
