# bSolitaire — Feature Design Spec: Draw-3, Game Timing, Earlier Lost-Game Detection

**Version:** 3.0 — Nov 22, 2026 — **as built**. Everything below shipped in #23, #24, #25
and #26, except where "What actually shipped" says otherwise. Read that section before
trusting any sentence in this one: a plan written before the code was read got several
things wrong, and this document is only useful now if it says which.
**Repo:** github.com/brandonchastain/bSolitaire — Blazor WebAssembly, C#/.NET 9, 2D canvas.
**Scope:** (1) draw three cards at a time, (2) time each game and keep stats (average time per game, average time per win), (3) surface a lost game sooner than the game currently does.

## What actually shipped

Four changes, in this order, each merged green and deployed:

| | |
| --- | --- |
| #23 | **Deal only the games that can be won.** New. Not in the plan below. |
| #24 | Know a dead deal at the end of the pass — Feature 3, Tier 1 only. |
| #25 | Draw three cards, with a fanned waste — Feature 1. |
| #26 | Time every deal — Feature 2. |

### Feature 3 was reframed, and most of it dropped

The plan's three tiers all *detect* a lost deal sooner. Asked which mattered, Brandon said
the problem was being handed a deal that was never winnable at all — and that other
solitaire games do not do this. They do not, because they deal only winnable deals, and the
repo already had a solver that could say so. So #23 vets deals before the player sees them,
and the question the tiers were answering mostly stops arising.

Tier 1 shipped anyway, because the pass-aware check is worth having on its own. **Tier 2**
(a cross-move cache of proven-unwinnable positions) and **Tier 3** (the advisory "looks
lost" button) were dropped: both are latency and consolation work for a problem #23 removes
at the source.

What #23 promises is that a win exists for someone who can see the face-down cards. Not
that the player will find it. Under draw-three the solver's model is more permissive than
the real game, so that promise is weaker there — it still removes every provably dead deal.

### Three claims below turned out to be false

- **"The `Solver` consumes `LegalMoves`, so it inherits draw-3 automatically."** It does
  not. The solver has its own `Packed`/`Successors` search and models the deck as an
  unordered set, so it is draw-count blind. Draw-three needed **no solver changes**, and
  resolution rates are unchanged rather than "typically worse". `LegalMoves` has exactly one
  production caller, `Rules.IsStuck`. `DESIGN.md` said otherwise too, and was corrected in
  #24.
- **"`Board.RecycleWaste()` is unchanged, so the draw-3 grouping repeats identically each
  pass."** The grouping did not repeat, because the stock was not turning the drawn packet
  over — so draw-three showed the card off the top of the stock instead of the third one,
  and every pass dealt a different grouping. #25 turns the packet, which makes both true.
  A test written to assert this sentence is what caught it.
- **"rAF is suspended while the tab is hidden, so a clock accumulated from `elapsed`
  excludes background time for free."** Suspension stops the frames, not the clock:
  `performance.now()` keeps advancing, and `Update`'s `elapsed` parameter is that clock, not
  a per-frame delta. #26 accumulates deltas and discards any gap over 250ms. Measured in a
  throttled browser pane: 981ms between frames, against 16ms with the page active.

### Smaller deviations

- The draw count reaches `Rules` as a parameter, but the set of deck cards a bigger draw can
  reach is **deliberately left as every card** — the same over-approximation as before. It
  is not the obvious every-nth card, because turning the waste back over reverses it. The
  wrong narrowing here would declare winnable deals dead, so it was not attempted.
- `FastForwardStep` deals one card at a time whatever the setting, so `CanFastForward`'s
  reachability argument holds unchanged.
- The times are on the end-of-game banner and nowhere else. The bottom score line is full,
  and it already overlaps the `ShowStats` overlay on a phone — pre-existing, still there.
- `FastestWinSeconds` was dropped as a field nobody asked for.

---

## Codebase facts this spec is written against

Every feature below hooks into machinery that already exists. The facts are split three
ways on purpose: **invariants** constrain any design and are not negotiable; **mechanisms**
are the call sites each feature actually touches; **landmines** are the four places where a
plausible implementation compiles, passes review, and is wrong.

### Where the code is

| File | Holds | Needed by |
| --- | --- | --- |
| `Game/Position.cs` | pile contents; `Take`/`Place` are the only way cards change hands | 1 |
| `Game/Rules.cs` | `IsLegal`, `CanLift`, `LegalMoves`, `IsStuck`, `FoundationFor` | 1, 3 |
| `Game/Board.cs` | `MakeMove`, `DealFromStock`, `RecycleWaste`, `RefreshState`, snapshot undo | 1, 3 |
| `Game/BoardLayout.cs` | all geometry, for drawer and hit-testing alike | 1 |
| `Game/CanvasDrawer.cs` | `DrawPile`, `DrawBanner`, the score line | 1, 2, 3 |
| `Game/Solitaire.cs` | the session: `Update`, `Reset`, command dispatch, frame slices | 2, 3 |
| `Game/ScoreKeeper.cs`, `Game/PlayerScore.cs` | the record, its JSON, and the two prefs riding along | 1, 2, 3 |
| `Game/Analyzer.cs`, `Game/Solver.cs` | the frame-sliced search and its budget | 3 |
| `Game/Controls.cs` | key map, corner buttons, `PlayerCommand` | 1, 3 |
| `Pages/Home.razor`, `wwwroot/js/game.js` | the frame loop and localStorage | 2 |

### Invariants — a feature that breaks one of these is wrong even when it works

1. **Nothing in the game layer references Blazor, JS, or canvas.** This is what makes the
   test project possible; it is the rule the whole design rests on.
2. **`Position`/`Rules`/`Board` know cards and legality — never pixels, time, or input.**
   A clock reached from `Board` or `Rules` is a layering break, which is the whole reason
   Feature 2's clock lives where it does.
3. **`BoardLayout` is the single source of geometry**, consumed by *both* the drawer and
   hit-testing, so a card's picture and its hit box cannot drift apart.
4. **Layout blends; it never switches.** Everything is interpolated across `Compactness`,
   and **widening a window must never take card away** — a past threshold made the card
   jump 9% *downwards* as the window grew.
5. **The solver never guesses.** `Unwinnable` is reported only after the entire tree below
   a position has been examined; running out of budget yields `Unknown`. Nothing anywhere
   may present `Unknown` as a verdict. This is why the banner is trustworthy.
6. **Everything the rules judge is a `Move(From, To, Count)`** — one representation, so
   legality, application, undo, and the solver have one implementation each. Recycling the
   waste is the one deliberate exception: it touches every card and asks no legality
   question, so it is its own method.
7. **Piles are named by what is showing, not by role:** stock = `PileKind.FaceDown`,
   waste = `PileKind.FaceUp`. A stock draw is `Move(FaceDown/0 → FaceUp/0, Count)`.

### Mechanisms — Feature 1 (stock, waste, geometry)

- **Draw-1 lives in exactly three places**, and they must change together:
  `Board.DealFromStock()` issues the move with `Count = 1`; `Rules.LegalMoves` yields the
  same count-1 stock move; `Board.MakeMove`'s stock branch flips `Position.FaceUpPile[^1]`
  — **one card**, whatever the count.
- **`Rules.IsLegal` already admits a multi-card stock move**, so `Count = 3` needs no
  legality change (verify with a test rather than trusting this): a `FaceDown` source is
  the documented exception to the face-up check; `To.Kind == FaceUp` is legal precisely
  when `From.Kind == FaceDown`; and the count-1 restriction applies only to *foundation*
  destinations.
- **Only the waste top is liftable** — `Rules.CanLift` returns `index == pile.Count - 1`
  for `PileKind.FaceUp`, and all three pointer paths (press, hover, tap) ask `CanLift`.
  Input therefore needs no change for draw-3.
- **Undo is by snapshot**, cap 200. A snapshot holds pile contents (cards by reference),
  a parallel array of per-card face-up flags, `GameState`, and `CanFastForward` — so
  multi-card moves and their incidental flips restore correctly with no new undo code,
  *provided every piece of state a move changes is in that list*.
- **One line makes every non-tableau pile draw a single card**, in `CanvasDrawer.DrawPile`:
  `int firstVisible = location.Kind == PileKind.Tableau ? 0 : visibleCount - 1;`
  A waste fan is an extension of that condition, with rects still coming from
  `BoardLayout.CardRect(location, index)` rather than being computed in the drawer.
- **The top row has a deliberate gap, and it is the room a fan has.** `CardRect` puts the
  stock at `ColumnX(0)`, the waste at `ColumnX(1)`, and the four foundations at
  `ColumnX(3..6)` — column 2 is empty. Clear space to the right of the waste card is
  therefore `CardWidth + 2 × gutter`, so a 3-card fan has up to about half a card width
  per offset before it reaches the first foundation. That is generous; the constraint is
  real but not tight. (Tableau `FanOffset` is vertical and derived from `CardHeight`;
  the waste needs its own horizontal offset, interpolated across `Compactness` like
  everything else.)
- **`Board.RecycleWaste()` needs no change** — but the draw did. ~~It flips the whole waste
  back in order, so draw-3 grouping repeats identically on each pass~~: it did not, until
  #25 made the stock turn the drawn packet over. Redeals stay unlimited.
  `FastForwardStep()` reaches a playable card via `DealFromStock()`/`RecycleWaste()`, so it
  works unchanged — just in steps of 3.

### Mechanisms — Feature 2 (the record, and the frame clock)

- **`PlayerScore` is a flat value the game owns and the host persists.** `Nickname`,
  `Games`, `Wins`, plus the preferences `Muted`/`ShowStats`, serialized by
  `ScoreKeeper.ToJson()`. `Home.razor` calls the JS helpers `bSolitaire.saveScore` /
  `bSolitaire.loadScore`, which keep it under the localStorage key **`bSolitaire.score`**,
  and load it before the first frame. A record that will not parse is a record we start
  over, not an exception.
- **`ScoreKeeper.Update()` counts a deal exactly once**, when `Board.State != Playing`,
  keyed on `Board.DealId` — so undoing off a finished board cannot double-count. `DealId`
  is bumped only by `Board.Reset()`; `Version` counts positions.
- **The score object is already on `ISolitaireView`** (`PlayerScore Score { get; }`), so
  persisted averages need no new view member. Only the *running* or *just-finished*
  duration does, since the drawer cannot reach `ScoreKeeper`.
- **`PlayerScore.Summary` is drawn bottom-right** by `CanvasDrawer`, and the drawer caches
  the board against that exact string — a changing summary is already a redraw trigger.
- **There is exactly one way a new deal starts.** `PlayerCommand.NewGame` — from the `R`
  key *and* from the banner's button — dispatches to `Solitaire.Reset()`, which stops
  fast-forward and the cascade, clears the animator, and calls `Board.Reset()`. Nothing
  else in the shipping game calls either `Reset`. Feature 2's abandoned-deal flush
  therefore needs **one** hook, not two (see landmine 4).
- **The frame clock, precisely.** `Solitaire.Update(TimeSpan elapsed, …)` is called once
  per rAF frame from `Home.OnFrame`, which passes
  `TimeSpan.FromMilliseconds(timestampMs)` — the **raw rAF timestamp**, forwarded
  unchanged from `game.js`. The parameter is named `elapsed` but is a monotonic clock, not
  a per-frame delta: `Animator` consumes it as `nowMs` and compares it against absolute
  flight start/end times. Any per-deal timer must derive its own delta, and see landmine 2
  for what that costs.

### Mechanisms — Feature 3 (the two verdicts)

- **`Board.RefreshState()` recomputes the verdict after every move.** It returns `Won` only
  when all four foundations are full; otherwise it sets `Stuck` or `Playing` from
  `Rules.IsStuck(Position)` and recomputes `CanFastForward`. So **`IsStuck` already runs on
  every single move** — that is Tier 1's cost site, and it is a hot one.
- **`GameState.Unwinnable` arrives only from outside.** `Board.MarkUnwinnable()` sets it
  (and only from `Playing`), called by `Analyzer` when the `Solver` proves it. Note that
  `RefreshState` overwrites `State` on the next position change, so the verdict does not
  survive a move — the search is expected to re-establish it.
- **`Rules.IsStuck` today** is true when no `LegalMoves` entry `IsProgress` — stock turns
  and whole-face-up-column-to-empty-column relocations are explicitly not progress — **and**
  no card anywhere in the stock or waste `HasHome`, where `HasHome` means a legal landing on
  *either* a foundation or a tableau pile, against the position exactly as it stands.
- **`Analyzer` restarts the search from scratch on every `Board.Version` change** (so every
  move), discarding the solver and its `Seen` set, and feeds it `8000` positions or `4ms`
  per frame, whichever comes first. It is paused entirely while
  `Drag != null || fastForward.IsRunning || animator.Busy || cascade.IsRunning`.
- **The budget and what it buys.** `SolverBudget.Default = (60_000 states, 400_000 nodes)`.
  Measured over 300 positions sampled from played-out deals: **71% of openings resolve,
  95% by eight cards home, 100% by twelve.** A `SolverBudget.Thorough`
  `(2_000_000, 20_000_000)` exists and is currently unused — an available lever, and the
  numbers above are the ones any budget change has to re-measure.
- **The banner words the two losses differently** in `DrawBanner`'s `state switch`:
  Won → "You win!" / "All 52 cards are home."; `Stuck` → "No moves left" / "Nothing on the
  board can move."; otherwise → "This deal is lost" / "No line from here wins." It dims the
  board and offers New Game, which is why Tier 3 must not use it.

### Landmines

1. **`PlayerScore.CopyFrom` is field-by-field, and its own comment records that a field was
   once forgotten there.** A loaded record is copied into the live object rather than
   swapped for it, because the game, the host, and the board all hold that same reference.
   **Every new field on `PlayerScore` must be added to `CopyFrom`** or it silently resets on
   load. Features 1 and 2 both add fields. Cover it with a test that loads a populated
   record and asserts the new fields survive.
2. **`Update`'s `elapsed` is not elapsed, and hidden-tab time is not excluded for free.**
   It is the rAF timestamp (see above). DESIGN.md's appendix is right that rAF is suspended
   while the tab is hidden — but suspension means *no frames fire*, not that the clock
   stops: `performance.now()` keeps advancing, so `end − start` across a hidden period
   includes it, and accumulating `now − lastNow` lands all of it in the single long delta on
   resume. Excluding background time means accumulating per-frame deltas **and discarding or
   clamping any delta larger than a plausible frame** (a gap of ~250ms or more means the tab
   was hidden or the loop stalled). The advice to avoid `DateTime.UtcNow` still holds, for a
   different reason than v1 gave: tests drive `Update` with timestamps they choose, and
   wall-clock math would make the timer untestable.
3. **`IsStuck`'s whole-deck scan is a theorem about draw-1, not a general one.** It is sound
   today only because turning the stock is always available and recycling is unlimited, so
   *every* stock or waste card eventually surfaces as the waste top. Under draw-3 only the
   top of each drawn group is reachable, so a buried card with a home may never surface —
   the scan then keeps a dead deal alive. Feature 1 must not ship with the old scan silently
   applying to draw-3 deals; Feature 3's Tier 1 is the mode-aware replacement.
4. **`ScoreKeeper.Update`'s comment claims the banner's button "resets the board itself",
   bypassing the game's reset. That is stale** — both paths go through `Solitaire.Reset()`
   (verified: no other `Reset()` call site exists outside the tests and `Solver`'s internal
   frame pooling). The once-per-deal `DealId` keying it describes is still correct and still
   load-bearing; only the second reset path is gone. Fix the comment while implementing
   Feature 2, and do not design a second flush hook around it.

### Tests

`BSolitaire.Tests` arranges positions by hand against the pure-C# layer:
`RulesTests`, `BoardTests`, `SolverTests`, `SessionTests`, `UndoTests`, `LayoutTests`,
`PointerTests`, `TouchTests`, `AnimationTests`, `PiecesTests`, with shared fixtures in
`Positions.cs`. New behaviour lands with tests in the matching file; the acceptance criteria
under each feature below name which.

---

## Feature 1 — Draw Three Cards at a Time

### Goal
Stock draws turn over 3 cards instead of 1, as the default way the game plays, with draw-1 kept as a player setting.

### Design

**Where the count lives.** Introduce the draw count as a game-level setting, not a constant scattered across call sites:

- Add `DrawMode` (enum `DrawOne`/`DrawThree`) to `PlayerScore`, persisted with the record exactly like `Muted`/`ShowStats`, with a `ScoreKeeper` toggle following the existing `ToggleMute`/`ToggleStats` pattern, surfaced through `Solitaire` and `Controls` (a corner control and/or key, consistent with how mute/stats are exposed). **Add the field to `PlayerScore.CopyFrom`.**
- The setting takes effect on the next deal, never mid-deal — same spirit as the existing rule that a deal's identity (`DealId`) is stable. `Board` needs the active deal's draw count: pass it in at `Reset`/deal time and store it on `Board` (it is per-deal state, and `Snapshot` does not need it since it can't change mid-deal).

**The draw itself.**
- `Board.DealFromStock()` issues `Move(FaceDown/0 → FaceUp/0, min(drawCount, stock.Count))`. `Rules.IsLegal` already validates a multi-card stock move (the FaceDown source is the documented exception to the face-up check; foundations' count-1 rule doesn't apply to a FaceUp destination). No legality changes expected — verify with a test.
- `Rules.LegalMoves` must yield the stock move with the same count, since `IsStuck` consumes `LegalMoves`. (~~`FastForward` and the `Solver`~~ do not: the fast-forward asks `FoundationFor` directly and the solver generates its own successors.) The draw count therefore has to be visible at the `Rules` layer for the active deal — pass it as a parameter or read it from a property on `Position`. Do **not** hardcode 3 in `Rules`; the solver and stuck checks must evaluate the deal actually being played.
- **Flip all drawn cards.** `Board.MakeMove`'s stock branch currently flips only `FaceUpPile[^1]`. For a draw of *n*, flip each of the *n* moved cards (they land face-down-flagged and all become showing in the waste). The `Motion` list already emits one motion per moved card with `reveals` set — confirm the animator/atlas path handles 3 simultaneous reveals; the deal animation already flies 28 cards, so this should be well within capacity.
- ~~`Board.RecycleWaste()` is unchanged: it flips the whole waste back in order, so the draw-3 grouping repeats identically each pass.~~ **Wrong when written.** The grouping repeated only once #25 made the stock turn the drawn packet over, which also fixed draw-three showing the wrong card of the three. Unlimited redeals stay.
- `FastForwardStep()` calls `DealFromStock()`/`RecycleWaste()` to cycle to a playable card — it works unchanged, just in steps of 3.

**Waste display (the real work in this feature).**
- Only the waste **top** card is playable — `Rules.CanLift` already enforces this (`FaceUp` ⇒ top index only), and hit-testing follows `CanLift`, so input needs no change.
- Draw the waste as a **horizontal fan of the last 3 cards** (fewer if the waste holds fewer), each offset right, top card last/on top — the standard draw-3 presentation, and the player needs to see which cards are buried under the top because in draw-3 they are one play away from surfacing.
  - `BoardLayout`: add waste fan geometry — a fan offset for the waste analogous to the tableau fan, interpolated with `Compactness` like everything else. The waste sits at `ColumnX(1)` and the foundations start at `ColumnX(3)`, so column 2 is free: there is `CardWidth + 2 × gutter` of clear space, which allows an offset of up to roughly half a card width for a 3-card fan. The fan must fit in that without shrinking cards and without reaching the first foundation at any `Compactness`. If the fan cannot fit at small card sizes, degrade gracefully (smaller offset), never a step/threshold switch (DESIGN.md: layout blends, it does not switch).
  - `CanvasDrawer.DrawPile`: the "only the tableau fans" shortcut must be extended for `PileKind.FaceUp`: draw the last up-to-3 cards at their fanned rects. `BoardLayout.CardRect(location, index)` is the seam — the drawer should keep asking layout for rects rather than computing offsets itself. Mind the cached-board economy: the waste region is already dirtied on every draw via `MarkDirty(move.To)`.
  - Drag/hidden bookkeeping: `DrawPile`'s `visibleCount`/`HiddenFrom` logic must keep working when the top waste card is held or in flight (the fan then shows the cards beneath, with the new top playable once it lands — it already is, by the rules).
- Empty waste still draws the empty-slot outline at the waste position.

**Solver & stuck-check impact (do not skip).**
- Draw-3 changes which stock cards can ever surface in a pass: only the card at the top of each drawn group is playable, and playing it shifts the grouping for the rest of the pass. `Rules.IsStuck`'s whole-deck `HasHome` scan becomes **unsound** under draw-3 (a buried card with a home may never surface). See Feature 3 — the pass-aware stuck check specified there is written to be correct for both modes, and Feature 1 must not ship with the old scan silently applying to draw-3 deals. Minimum viable guard if Feature 3 lands later: under draw-3, `IsStuck` may only count a stock/waste card if it can surface given the current grouping (its position from the top of the combined stock+waste sequence is reachable in steps of the draw count, accounting for cards already played from the waste this pass). When in doubt, stay conservative in the direction the current code already chose: `IsStuck` returning false (game continues) is an annoyance; returning true on a live game is a correctness bug.
- ~~The `Solver` consumes `LegalMoves`, so it inherits draw-3 automatically once `LegalMoves` is mode-aware. Expect different (typically worse) resolution rates under draw-3.~~ **Wrong.** The solver reads nothing from `Rules`: it packs the position itself and models the deck as an unordered set, so it is draw-count blind and needed no changes. That model is more permissive than draw-three rather than less, so `Unwinnable` stays sound while `Winnable` becomes an overclaim — which only reaches the stats overlay, and weakens #23's guarantee under draw-three.

### Acceptance criteria
- Fresh deal, draw repeatedly: waste tops cycle in groups of 3; after a recycle, the same groups recur in the same order; a final partial group of 1–2 draws correctly.
- At no point can a non-top waste card be lifted, dragged, or double-tap-sent home.
- Undo across draws, plays from the waste, and a recycle restores exact pile contents, order, and face-up flags (snapshot undo — add an UndoTests case with a 3-card draw).
- The waste fan renders the last 3 cards distinctly at desktop and phone sizes, with no overlap into the foundation slots and no card-size reduction versus draw-1 at the same window size.
- Toggling the setting mid-deal changes nothing until the next deal; the setting survives a reload (localStorage round-trip, including `CopyFrom`).
- Existing test suites pass; new RulesTests cover the count-3 stock move's legality and `LegalMoves` under both modes.

---

## Feature 2 — Game Timing and Stats

### Goal
Time every deal, persist the results, and show at minimum **average time per game** and **average time per win**. (Requested by Brandon; his dad plays 1,000+ games, so the record has to stay cheap and bounded.)

### Design

**Where the clock lives.** Time is a session concern, not a rules concern — `Board`/`Rules` must not learn what a second is (DESIGN.md layering). The clock belongs with `ScoreKeeper`, which already watches the board finish and owns the record:

- `ScoreKeeper` gains a per-deal timer fed from `Solitaire.Update`, which already hands each frame's timestamp to the pieces that need one (`animator.Capture(elapsed.TotalMilliseconds)`, `animator.Tick(…)`). Add `scores.Tick(nowMs)` (name to taste) in `Update`, next to `scores.Update()`. Pass the timestamp, not a delta — there is no delta to pass (Landmine 2): `ScoreKeeper` keeps the previous frame's timestamp and derives its own, exactly as `Animator` does.
- **Start:** on the deal's first move — detectable as the first frame where the deal's `Board.Version` exceeds the version at deal start, or more simply the first `Tick` after `DealId` changes in which a move has been made. Do not start at deal time; pre-first-move thinking is excluded.
- **Accumulate only while `Board.State == GameState.Playing`** and the deal has started, as a sum of per-frame deltas (`now - lastNow`).
- **Discard implausible deltas.** A gap beyond about 250ms means the tab was hidden or the loop stalled, not that the player spent that time playing — so clamp it to zero rather than adding it. This is what actually excludes background time; rAF's suspension while hidden stops the *frames*, not the clock, so without this the whole hidden period lands in one delta on resume (Landmine 2). Put the threshold in one named constant next to the timer, with that reasoning on it. Do **not** reach for `DateTime.UtcNow`: tests drive `Update` with timestamps they choose, and wall-clock math would make the timer untestable.
- **Stop & record:** when `ScoreKeeper.Update()` counts the deal (it already fires exactly once per deal, keyed by `DealId`), it captures the accumulated duration alongside the win/loss outcome it already records.

**Abandoned deals.** Today, pressing New Game mid-deal drops the old deal uncounted (`Board.Reset()` just deals again; `ScoreKeeper` never sees a terminal state). For honest averages, an abandoned deal that had started (≥1 move) should be recorded as a played game with its duration and no win. Implementation: `Solitaire.Reset()` must give `ScoreKeeper` a chance to flush the in-progress deal as `abandoned` **before** `DealId` increments. That is the only hook needed — every new deal in the shipping game comes through it, including the banner's button, despite what `ScoreKeeper.Update`'s comment says (Landmine 4; correct the comment while you are there). Keep the once-per-deal guarantee: flush marks the deal recorded so the later `Update()` can't recount it. A deal abandoned before its first move records nothing.

**The record (`PlayerScore`).** Averages need sums and counts, not a history list — keep the record O(1) in size, matching how Games/Wins already work:

- `TimedGames` (int) — deals with a recorded duration (won + lost + abandoned-after-first-move).
- `TotalGameSeconds` (long) — sum of durations over `TimedGames`.
- `TimedWins` (int), `TotalWinSeconds` (long) — the same, over wins only.
- `FastestWinSeconds` (int, nullable or 0 = none) — nice-to-have, cheap.
- Averages are computed properties: `AverageGameSeconds = TotalGameSeconds / TimedGames`, `AverageWinSeconds = TotalWinSeconds / TimedWins`, guarding divide-by-zero (display "—" when there are no timed wins).
- ⚠️ Add **every** new field to `PlayerScore.CopyFrom`, and confirm the JSON round-trip keeps them (old saves without the fields must load with zeros — `System.Text.Json` defaults handle this; add a SessionTests/ScoreKeeper load test with a legacy JSON payload).
- Duration granularity: whole seconds are fine; store as `long` seconds (accumulate `TimeSpan` internally, convert at record time).

**Display.**
- The score line: `PlayerScore.Summary` is drawn at the bottom-right by `CanvasDrawer` and already shows Games/Wins. Extend it (or add a second line) with the averages, e.g. `Games: 1,204   Wins: 187   Avg game: 3:42   Avg win: 4:15`. Mind the bottom-right real estate on phone layouts — if it doesn't fit, the averages can ride the end-of-game banner instead; pick one home, don't duplicate.
- The end-of-game banner (`DrawBanner`) detail line is the natural place for the just-finished deal's time: e.g. Won → "All 52 cards are home — 4:15."; Stuck/Unwinnable → append the duration similarly. The banner text is chosen by a `state switch` in `DrawBanner`; the duration has to reach the drawer through `ISolitaireView` (the drawer cannot read `ScoreKeeper` directly) — expose e.g. `LastGameSeconds` / the running `Elapsed` on the view.
- Optional: show the running clock during play (small, near the score line). Not required by the request; the recorded stats are the requirement. If added, it must not dirty the board every frame — the canvas redraw economy (`NeedsRedraw`, cached board) means a per-second text update should dirty only when the displayed second changes.

### Acceptance criteria
- Win a deal: the record gains one timed game and one timed win; the banner shows the duration; the averages equal hand-computed values from the stored fields.
- Lose (Stuck/Unwinnable) and abandon-after-first-move deals each record exactly once, with durations; abandoning before the first move records nothing; undoing off a terminal board and re-finishing does not double-record (existing `DealId` keying preserved).
- Hide the tab for a minute mid-deal: the recorded duration excludes it.
- A legacy save (JSON with only the current fields) loads, plays, and saves back with the new fields present and correct.
- `CopyFrom` covers every new field — cover with a test that loads a populated record and asserts the stats survive.

---

## Feature 3 — Surface a Lost Game Sooner

### Problem, in this codebase's terms
The game already has two lost-game verdicts, and the player's complaint ("I can tell it's lost, but I have to keep making moves before the game blocks me and lets me start a new one") is about the gap between them:

- `Rules.IsStuck` is cheap and instant, but deliberately conservative: a single stock/waste card that `HasHome` against the current position — or one progress move, however pointless — keeps the deal "alive," even when playing it unlocks nothing and the player must cycle the deck to reach it, play it, and repeat.
- The `Solver` gives the true verdict, but only within budget: it returns `Unknown` on many mid-game positions, restarts from zero after **every** move, and is paused during drags and animations. A deal can be visibly dead to an experienced player for dozens of moves without either verdict firing.

The fix is **not** a new solver. It is to close the gap from both ends: a stronger cheap check for the player-visible dead position, and better reach for the existing search — plus an honest advisory tier in between, so the player is offered an exit at the moment they already know, without the game ever claiming a proof it doesn't have.

### Tier 1 — Pass-aware dead-position check (deterministic, in `Rules`)
Extend the `IsStuck` idea from a static scan to a **pass simulation**, respecting the active draw mode:

- Simulate one full pass through the stock from the current position: repeatedly take the next surfacing waste top (draw-1: every card; draw-3: the top of each drawn group, with grouping shifting as cards are played), and whenever the surfaced card has a legal play (foundation or tableau) **at the moment it surfaces**, apply the best such play virtually and continue. Tableau/foundation plays available without the stock are already covered by the existing progress-move scan.
- If a complete pass (stock exhausted and recycled to the identical sequence) yields **zero plays**, and no progress move exists on the board, the position is dead in the only sense the player can act on: nothing they can do changes anything. Report it through the existing `GameState.Stuck` path (it is the same claim the banner already makes — "No moves left" — now actually true at the pass level rather than only the single-position level).
- Soundness rules, non-negotiable:
  - The simulation must only ever *find* plays, never invent them: every virtual play must be legal under `Rules.IsLegal` at its virtual position.
  - Virtual plays can only unlock more plays, so greedily applying a surfaced card's play is safe for a *deadness* claim **only if** the choice of play doesn't matter — a card playable to both tableau and foundation could differ. Where a surfaced card has more than one distinct legal destination, treat the position as **not proven dead** by this tier (fall through to Tier 2/3) rather than guessing. Simplicity and soundness beat coverage here.
  - Face-down tableau cards: a virtual play that would uncover one makes the position not-dead for this tier (the uncovered card is new information the simulation must not peek at — this tier is a *player-information* check, unlike the solver).
  - Cost: bounded by one pass (≤ 52 surfacings × a `LegalMoves`-scale scan). It runs on move completion inside `RefreshState`'s existing `IsStuck` call site — keep it allocation-light and, if measurable, still far under the 4ms frame slice the `Analyzer` lives by. Cache nothing across moves; positions change every move.
- This tier also **fixes the draw-3 unsoundness** flagged in Feature 1: `HasHome` over the whole deck is replaced, for both modes, by "can this card surface, and would it have a play when it does."

### Tier 2 — Give the existing `Solver` more reach (no new search)
- **Persist proven-unwinnable positions across moves.** `Analyzer` currently discards the `Solver` (and its `Seen` set) on every `Version` change. A position *proven* unwinnable is unwinnable forever — that fact is a property of the position, not of the search. Keep a session-level cache (packed position hash → proven unwinnable) that survives restarts; when a new search's expansion reaches a cached position, it can prune it as a dead subtree. Sound because `Unwinnable` is only ever recorded after exhaustive proof (the solver's existing contract). Bound the cache (e.g., an LRU or a simple cap with full clear) so a long session can't grow it without limit — the codebase already caps undo (200), motion queues, and sound queues for exactly this reason.
- **Don't restart on cosmetic version bumps if avoidable:** if the only change since the last search is a stock draw/recycle (position changed but the deal's *reachable tree* overlaps heavily), the cache above already captures most of the value; a full incremental search is out of scope.
- **Budget:** leave `SolverBudget.Default` alone unless measurements (SolverTests-style sampling over played deals) show Tier 1 + cache still leave the player's dead positions unresolved. The budget comment in `Solver.cs` documents the trade-off precisely; any change must re-run that measurement and update the comment with new numbers.

### Tier 3 — Advisory "looks lost" offer (player-facing, honest wording)
For positions that are neither proven dead (Tier 1) nor proven unwinnable (Tier 2) but where the player has **no progress moves** (per the existing `IsProgress` definition) and the solver has finished its budget at `Unknown` — offer, don't declare:

- A non-blocking offer in the spirit of the existing FastForward button ("Finish ▶▶" is drawn as an overlay button, not a modal): e.g. a **"New game?"** button appearing when (a) no progress move exists, (b) at least one full stock pass has completed with no progress move made, and (c) the solver is at `Unknown`/`Searching` past its budget for the current position.
- It must never use the banner, never dim the board, never block input, and never be worded as a verdict. If ignored, it stays available but silent; if the position gains a progress move, it withdraws.
- Taking it deals a new game. For the record (Feature 2), a deal ended this way counts as a loss with its duration — this needs a distinct route into `ScoreKeeper`, since the deal ends from `Playing`, not from a terminal `GameState`. Options: a `GameState.Conceded` (banner never shows for it because the deal is immediately replaced; `ScoreKeeper.Update` counts any non-`Playing` state, and `DrawBanner`'s switch would need an arm or the state must transition straight through) — or a `ScoreKeeper.Concede()` flush mirroring Feature 2's abandoned-deal flush. Prefer the explicit flush: fewer states, and the banner's two-loss wording stays clean.

### What not to do
- Do not lower the solver's standard of proof, and do not present `Unknown` as `Unwinnable` anywhere. The current contract — "Unwinnable is reported only after the entire tree has been examined" — is why the banner's "No line from here wins" is trustworthy.
- Do not make any tier force-end a deal or auto-deal. Even Tier 1 keeps today's behavior: the banner offers New Game; the player presses it.
- Do not peek at face-down cards in Tier 1/Tier 3. The full-information question is the solver's alone.

### Acceptance criteria
- An arranged position where one stock card has a home but playing it unlocks nothing and the rest of the pass is dead: today's code plays on; with Tier 1, the deal is declared once the pass completes — with no further player moves required beyond the pass itself. (RulesTests, arranged by hand.)
- An arranged position with exactly one productive line remaining: no tier fires; the deal plays on.
- A position where a surfaced card has two distinct legal destinations: Tier 1 abstains (no false Stuck), regardless of whether the line wins.
- Draw-3: a position whose only "home" card can never surface under draw-3 grouping is detected dead by Tier 1; the same deck under draw-1 is not declared dead while the card can still surface and play.
- Solver cache: a deal proven unwinnable, followed by undo and a different move order reaching the same position, resolves from the cache without a full re-search (SolverTests/Analyzer-level test); the cache stays within its cap.
- Tier 3: in an arranged no-progress position with the solver at `Unknown`, the offer appears after one full pass, blocks nothing, withdraws when a progress move appears, and taking it records a loss with duration in the record.
- No regression: existing SolverTests verdicts on sampled positions are unchanged (the cache must never alter a verdict, only its latency).

---

## Cross-Feature Interactions

- **Feature 1 × Feature 3:** draw-3 makes the current `IsStuck` whole-deck scan unsound; Tier 1's pass simulation is written mode-aware to replace it. Ship order matters: if Feature 1 lands first, include the minimum surfacing guard described there; Tier 1 then supersedes it.
- **Feature 2 × Feature 3:** Tier 3's concede path and Feature 2's abandoned-deal flush are the same mechanism — implement once, in `ScoreKeeper`, and use it from both `Solitaire.Reset()` and the Tier 3 offer.
- **Feature 1 × Feature 2:** if stats are ever compared across modes, `TimedGames` aggregates would need a per-mode split — **out of scope** unless Brandon asks; the aggregates above intentionally pool both modes, matching how Games/Wins already pool everything.
- All three features preserve the architecture rules: no Blazor/JS types in the game layer, geometry only in `BoardLayout`, time only in the session/record layer, verdicts only from `Rules`/`Solver`.

## Out of Scope

- Full player-information solvability solving (proving a position with progress moves remaining can never be won, from the player's limited view). The solver's full-information proof plus Tier 1/Tier 3 cover the player's actual complaint.
- Hints or move suggestions.
- Cloud sync or cross-device stats; the record stays in localStorage with the existing score.
- Scoring systems, Vegas rules, timed-move bonuses.
- Draw-3 redeal limits (redeals stay unlimited, as today).

---

## Fixes after release (#28)

Two bugs reported from play once all four changes were live.

### The hover outline landed on the buried card

Hovering the waste's three-card fan highlighted the card at the back of it instead of the
one under the pointer.

`CardRect` took a `pileCount` in #25, because the waste's fan is anchored to the end of the
pile rather than the start. It was given a **default of 1**, so that call sites which do not
care about the waste — the tableau, the foundations, the stock — would not all have to be
touched. Four that *did* care were missed, and every one of them compiled: the hover
outline, the drop-target ring, the grab offset in `PointerInput`, and the flight origin in
`Animator`. Each asked for a rect without the pile's length and got back the fan's first
place, which is where the buried card sits.

The hover outline is the one a player sees. The grab offset is the one a player feels: the
held stack leapt sideways by the width of the fan as it was picked up. A test now presses
the top of a fan and asserts the drag offset is measured from the card under the pointer —
it fails by 53 pixels against the old code, which is exactly two fan offsets.

The lesson is about the default, not the call sites. A defaulted parameter turned "every
caller must now say how long the pile is" into something the compiler would not ask about.

### Draw-one did not take effect

The setting was saved, the button on the felt showed `1`, and the stock kept turning three.

`Begin()` — the first deal of a session, added in #23 — ran **before** `LoadScore()`. The
host had deliberately started the frame loop before loading the record, so that a record
that would not load could never cost the player their game. #23 put the first deal in that
same window, so it was dealt under the default rules, and the record arrived a moment later
and lit up a button describing a game the board was not playing.

The record now loads first, with the failure handled where it happens rather than by
ordering: a record that will not read is logged and skipped, and the game still starts. The
ordering had been carrying a guarantee it no longer deserved once a deal depended on it.

### Changing the draw now asks

A deal keeps the rules it was dealt with, so the setting could only ever reach the next
deal. Pressing the button therefore did nothing a player could see, which reads as broken
even when it is working.

The toggle now asks — "Turn one card off the stock instead of three? The deal you are on
keeps its own rules, so this starts a new game" — and deals on confirmation. The abandoned
deal is counted as played and not won, like any other. A DOM overlay rather than a canvas
panel, for the same reason the nickname prompt is one: buttons that a keyboard can reach
have to be real markup.
