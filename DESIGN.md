# bSolitaire — design

Status: the starter template exists; the game does not. This document records the
analysis of the template and the proposed design for the game, so the reasoning
behind each decision survives past the conversation that produced it.

---

## Part 1 — The starter template

### Layering

```
game.js          browser edge: requestAnimationFrame, DPI, resize
   |  interop
Home.razor       adapter: owns the loop, forwards input, owns the drawer
   |                |
Solitaire        IGameDrawer -> CanvasDrawer
(pure C#)                |  reads the game
```

### What holds up

- `Solitaire` references no Blazor, JS, or canvas type. This is the property worth
  protecting above all others: it lets the rules be tested in milliseconds instead
  of by driving a browser.
- Nothing above knows about anything below it. `Solitaire` doesn't know a drawer
  exists; `CanvasDrawer` doesn't know a frame loop exists.
- DPI is normalized once at the JS edge, so there is exactly one coordinate system
  (CSS pixels) everywhere above it. Input coordinates and drawing coordinates are
  the same numbers.

### What it doesn't answer yet

1. **Nobody owns geometry.** The drawer needs to know where pile 3 is; the click
   handler needs the same answer in reverse. If each computes it independently
   they drift, and cards render correctly while the hit box sits 8px off. This is
   the central decision for a canvas card game and it is currently unassigned.
2. **`IGameDrawer.Draw(Solitaire)` decouples technology, not shape.** Swapping
   canvas for WebGL is free; changing how piles are modelled still ripples into
   the drawer, because the drawer must read the model's structure. That is fine
   and normal — just don't mistake the seam for more isolation than it gives.
3. **One `OnClick` cannot express a drag.** Click-to-select-then-click-to-place
   works with what's there. Drag-and-drop needs down/move/up.
4. **`Update(elapsed)` currently earns nothing.** Solitaire is event-driven; the
   parameter starts paying for itself only when card-flight animations exist.
   Keep it — it will be wanted — but know it is speculative today.

### One thing the template got wrong

`BeginBatchAsync`/`EndBatchAsync` was stripped out of the drawer as bTetris cruft.
For tetris it nearly was. For solitaire it isn't. Every `SetFillStyleAsync` /
`FillRectAsync` in this package is a separate JS interop round trip; a
procedurally drawn card costs roughly 8 of them, and a dealt Klondike board shows
30–45 cards. That is ~300 interop calls per frame, 18k/sec at 60fps. Batching
collapses each frame into a single `callBatch`. Put it back.

---

## Part 2 — Proposed design

### The organizing decision: geometry is its own layer

Three layers instead of two, split by what kind of thing each knows about:

| Layer | Knows about | Never knows about |
| --- | --- | --- |
| `Board` + `Rules` | cards, piles, legality | pixels, time, input |
| `Solitaire` (session) | pixels, drag state, animation, undo | canvas API, Blazor |
| `CanvasDrawer` | canvas API | rules |

`BoardLayout` is the single source of truth for geometry, consumed by **both** the
drawer and hit-testing. That kills the drift bug in open question 1 structurally
rather than by discipline.

### Card

```csharp
public enum Suit : byte { Clubs, Diamonds, Hearts, Spades }
public enum Rank : byte { Ace = 1, Two, /* ... */ King = 13 }

public readonly record struct Card(Rank Rank, Suit Suit)
{
    public bool IsRed => Suit is Suit.Diamonds or Suit.Hearts;
}
```

Why each part of `readonly record struct`:

- **`struct` — snapshot undo becomes free.** The design clones the board before
  every move. If `Card` were a class, copying a `List<Card>` would copy
  *references*, so every snapshot would alias the same 52 objects and `Clone()`
  would need a real deep copy to be safe. With a value type, copying the list
  copies the cards; snapshots cannot alias. This is what makes "just clone the
  board" a defensible undo strategy rather than a trap.
- **`record` — cards get compared constantly.** `record struct` generates
  `Equals`, `GetHashCode`, and `==` comparing fields. A plain struct defines no
  `==` at all and falls back to the slow `ValueType.Equals` path; a class gives
  reference equality, which is silently wrong — two Ace-of-Spades values should
  be equal.
- **`readonly`** — states the intent, and avoids defensive copies when the
  compiler passes the value by `in` or reads it from a readonly field.
- **`: byte` on the enums** — enums default to `int`, making `Card` 8 bytes;
  byte-backed makes it 2, so a whole deck is ~104 bytes. Irrelevant to framerate,
  but it is what makes cloning the board on every move not worth thinking about.

It also buys pattern matching, which reads well in rules code:

```csharp
if (card is { Rank: Rank.Ace }) ...
var (rank, suit) = card;
```

**The one real cost:** `default(Card)` is representable and invalid — `(Rank)0`
is not a rank, since `Ace = 1`. A struct cannot prevent `default`. Two
mitigations, both already in the design: never store a `default` (cards only come
from a deal), and use `Card?` for "there may be no card here" — which is why
`CanFound` below takes `Card? top` rather than a sentinel. `Nullable<Card>` over
a 2-byte struct is still cheap.

The counterargument to a struct would be wanting per-card mutable state
(selected, animating). That state belongs to the drag/animation layer keyed by
location, not to the card, so it never comes up.

### Piles: make face-up-ness structural

```csharp
public sealed class TableauPile
{
    public List<Card> Down { get; } = new();
    public List<Card> Up   { get; } = new();
}
```

No `FaceUp` flag on the card. With a flag, "flip the newly exposed card" is a
rule enforced by vigilance in several places, and forgetting it in one of them is
the classic Klondike bug. With two lists it is one line in one place — flip if
`Up` is empty and `Down` isn't — and an inconsistent state is unrepresentable.

Stock is all face-down, waste and foundations are all face-up, so no other pile
needs the concept at all.

### Everything is a Move

```csharp
public enum PileKind { Stock, Waste, Foundation, Tableau }
public readonly record struct Location(PileKind Kind, int Index);
public readonly record struct Move(Location From, Location To, int Count);
```

Drawing from the stock is `Move(Stock/0 -> Waste/0, 1)`. Recycling the waste is
`Move(Waste/0 -> Stock/0, all)`. Making every action a `Move` means legality,
application, undo, hints, and auto-finish each have exactly one implementation
instead of one per action type.

```csharp
public static class Rules
{
    public static bool CanStack(Card moving, Card onto);   // alternating colour, descending
    public static bool CanFound(Card moving, Card? top);   // same suit, ascending, Ace on empty
    public static bool IsLegal(Board board, Move move);
    public static IEnumerable<Move> CandidateMoves(Board board);
}
```

`CandidateMoves` looks like a luxury. It isn't — one function gives you the hint
button, auto-finish ("send everything home"), stuck-game detection, and a solver
you can run over thousands of seeds to check the deal is fair. Build it early.

### Undo by snapshot, not by inverse

Push `board.Clone()` before every `Apply`; undo is `history.Pop()`.

The tempting alternative — an `Undo(Move)` that reverses each move type — is
where solitaire codebases rot, because the inverse must remember incidental
effects: did this move cause a flip? was the waste recycled? A `Board` is 52
cards; cloning it is free at human timescales. Keep a `Stack<Board>` and pop.

(A fully immutable `Board` whose `Apply` returns a new instance gets the same
benefit with every rule pure, at the cost of slightly more ceremony. Equally
valid; mutable-plus-clone is the lower-friction version of the same idea.)

### Geometry

```csharp
public readonly record struct Rect(double X, double Y, double W, double H);

public sealed class BoardLayout
{
    public BoardLayout(double width, double height);   // recompute on resize
    public double CardWidth  { get; }
    public double CardHeight { get; }

    public Rect CardRect(Location loc, int indexInPile);   // fan offset applied
    public Rect EmptySlot(Location loc);
    public bool TryHitTest(double x, double y, out Location loc, out int index);
}
```

One rule for `TryHitTest`: iterate **top-most first** — later piles before
earlier, and within a fanned tableau the last card before the ones underneath it.
Overlapping cards mean the first match in draw order is the wrong answer.

### Interaction as an explicit state machine

Drag state is pixels, so it lives in `Solitaire`, never in `Board`:

```csharp
private sealed record Drag(Location From, int Index, List<Card> Cards,
                           double GrabOffsetX, double GrabOffsetY,
                           double X, double Y);
```

`Idle` -> `PointerDown` (hit-test; is the grab legal?) -> `Dragging` ->
`PointerUp` (hit-test the drop target; `Rules.IsLegal` ? apply : snap back) ->
`Idle`.

This replaces `OnClick` in `Home.razor` with `@onpointerdown` / `@onpointermove`
/ `@onpointerup`, all of which supply `PointerEventArgs.OffsetX/OffsetY`. **No
`game.js` change is needed** — drag is pure Blazor. One caveat: if the pointer
leaves the board mid-drag, either handle `@onpointerleave` to cancel or add a
small JS `setPointerCapture` call so the drag survives.

### Rendering

Draw order: pile backgrounds -> stock/waste/foundations -> tableau piles -> the
dragged stack -> in-flight animations. Dragged and flying cards render last so
they float above everything.

Two performance moves, in order of value:

1. **Dirty flag.** A solitaire board is static most of the time.
   `Solitaire.NeedsRedraw`, set by any mutation or drag movement, cleared by
   `Home` after `Draw`, forced true while an animation runs. Idle cost drops to
   zero draws per second. This matters more than anything else here.
2. **Batch.** Wrap `CanvasDrawer.Draw` in `BeginBatchAsync` / `EndBatchAsync`.

If more headroom is wanted later, the package has `DrawImageAsync`: pre-render
the 52 faces once onto a hidden second `<BECanvas>` and blit them, turning ~8
calls per card into 1. Don't do this until it has been measured — start
procedural, since it costs zero assets.

### Files

```
Game/
  Card.cs          Suit, Rank, Card
  Location.cs      PileKind, Location, Move
  Board.cs         piles, Deal(seed), Apply(Move), Clone()
  Rules.cs         CanStack, CanFound, IsLegal, CandidateMoves, IsStuck
  BoardLayout.cs   Rect, CardRect, EmptySlot, TryHitTest
  Solitaire.cs     session: board + layout + drag + undo + animation
  IGameDrawer.cs   (unchanged)
  CanvasDrawer.cs  batched draw
```

### Build order

1. **`Board` + `Rules` + `Deal`, with an xUnit project, before drawing a single
   card.** This is the payoff of keeping `Solitaire` free of Blazor: correct,
   tested Klondike rules with no rendering at all, so the visual work is never
   debugging two things at once.
2. Static draw of a dealt board.
3. Click-to-move (no drag).
4. Drag.
5. Undo, win detection, auto-finish, hint.
6. Animations, then the batching/dirty pass.

### The seam for variants

This designs Klondike concretely rather than building a rules engine; variant
frameworks written before one variant works are almost always wrong. But note
where the seam would go: `Deal(Random)` and `IsLegal(Board, Move)` are the only
two functions that encode which game it is. Everything else — layout, drag, undo,
drawing — is variant-agnostic. Pull those two behind an interface when a second
variant exists, not before.

---

## Appendix — notes on the toolchain

Facts established while building the template, recorded so they don't have to be
rediscovered.

**`Blazor.Extensions.Canvas` 1.1.1** targets netstandard2.1 and dates from 2020,
but works on net9 — verified by driving a frame and reading back canvas pixels.
Available: `BeginBatchAsync`/`EndBatchAsync`, `DrawImageAsync`,
`MeasureTextAsync`, `SaveAsync`/`RestoreAsync`, `TranslateAsync`/`RotateAsync`/
`ScaleAsync`, `ClipAsync`, `ArcAsync`/`ArcToAsync`, `BezierCurveToAsync`/
`QuadraticCurveToAsync`, `SetShadow*Async`, `SetLineDashAsync`,
`SetTextAlignAsync`/`SetTextBaselineAsync`. **Not** available: `RoundRect` — the
package predates it, so rounded card corners need a manual `ArcTo` path, or plain
`FillRect`/`StrokeRect` to start.

**`_Imports.razor` needs both `@using Blazor.Extensions` and `@using
Blazor.Extensions.Canvas`.** With only the first, `BECanvasComponent` resolves in
C# but the `<BECanvas>` tag silently degrades to a plain HTML element, and `@ref`
then fails to compile with a confusing `ElementReference` conversion error.

**`requestAnimationFrame` is suspended while the browser tab or preview pane is
hidden.** An apparently dead game loop and a blank canvas may just mean nothing is
compositing. To verify rendering headlessly, drive one frame directly via
`dotNet.invokeMethodAsync('OnFrame', 0)` and read pixels back with `getImageData`.

**DPI is handled once**, in `game.js`, via `setTransform(dpr, 0, 0, dpr, 0, 0)`.
Everything above it works in CSS pixels. Don't reintroduce device-pixel maths in
the drawer.
