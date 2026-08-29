# bSolitaire — design

Klondike, in Blazor WebAssembly, drawn to a 2D canvas. This document is about the
shape of the code and why it is that shape. The README covers how to run it and
how a frame works; this covers what is separated from what.

---

## The layering

```
game.js            browser edge: requestAnimationFrame, DPI, canvas size
   |  interop
Home.razor         host: owns the loop, forwards input, owns the drawer
   |                    |
Solitaire  ---------> ISolitaireView -> CanvasDrawer
(pure C#)                                (reads the game, cannot play it)
```

Two rules hold the whole thing up:

- **Nothing above knows about anything below it.** `Solitaire` references no
  Blazor, JS, or canvas type, so the rules are tested in milliseconds instead of
  by driving a browser. Almost all of the test project is possible because of it.
- **DPI is normalized once**, at the JS edge, via `setTransform(dpr, 0, ...)`.
  There is exactly one coordinate system above it — CSS pixels — so input
  coordinates and drawing coordinates are the same numbers. Don't reintroduce
  device-pixel maths in the drawer.

`ISolitaireView` is the narrower half of that seam. The drawer is handed the game
as a read-only view, so painting a picture of a position provably cannot change
it, and a renderer cannot reach `Reset` or the pointer handlers.

## The three layers, by what each knows about

| Layer | Knows about | Never knows about |
| --- | --- | --- |
| `Position`, `Rules`, `Board` | cards, piles, legality | pixels, time, input |
| `Solitaire` and its parts | pixels, drag, animation, frames | canvas API, Blazor |
| `CanvasDrawer` | canvas API | rules |

`BoardLayout` is the single source of truth for geometry, consumed by **both**
the drawer and hit-testing. That is what keeps cards from rendering correctly
while the hit box sits 8px off: the two answers cannot drift because there is
only one.

## Position and Board: cards, and the bookkeeping around them

`Position` holds which cards are in which piles, in what order, and nothing else
— it does not know a king from a two. Its lists are private and handed out
read-only, so cards change hands only through `Take` and `Place`. That keeps the
splice separable from everything `Board.MakeMove` has to do alongside it.

`Board` is the game in progress: it makes moves, and owns every consequence of
one — flipping the card a move uncovered, marking piles dirty, naming a sound,
naming a motion, recomputing won/stuck, pushing undo.

`Rules` is static and takes a `Position`. It answers `CanStack`, `CanFound`,
`IsLegal`, `CanLift`, `LegalMoves`, `IsStuck`, `FoundationFor`. `LegalMoves`
looks like a luxury; it is not — one function underwrites stuck detection,
playing the rest of the game out, and the solver.

**Piles are named by what is showing, not by their role.** `PileKind` is
`FaceDown`, `FaceUp`, `Foundation`, `Tableau` — the stock is the face-down pile
and the waste is the face-up one. Face-up-ness is a flag on `Card`, and the
flip-on-uncover rule lives in exactly one place inside `MakeMove`, which is the
only reason a flag is safe: forgetting it in a second place is the classic
Klondike bug, and there is no second place.

`Card` is a mutable class rather than a value type, precisely because that flag
is per-card state that has to survive being copied around in lists. Undo is what
pays for it — see below.

**Everything the rules judge is a `Move`.** `Move(Location From, Location To,
int Count)`; turning a card off the stock is `FaceDown/0 -> FaceUp/0, 1`. One
representation means legality, application, undo, and the solver each have one
implementation rather than one per action type. Recycling the waste is the
deliberate exception: it touches every card at once and there is no legality
question to ask, so it is its own method.

## Undo by snapshot

Push a snapshot before every move; undo pops it. The alternative — an `Undo(Move)`
that inverts each kind of move — is where solitaire codebases rot, because the
inverse has to remember incidental effects: did this move flip a card? was the
waste recycled? Not working out what the consequences were is cheaper and safer
than working them out correctly.

The snapshot holds cards **by reference** — they are the same fifty-two objects
for the life of a deal — but copies each card's face-up-ness into a parallel
array, because that is exactly the state a move changes behind the player's back.
Restoring calls `SetFaceUp(value)`, not `Flip()`: putting a position back means
saying what was true then, not counting turns since.

History is capped at 200 so a very long game cannot grow it without bound.

## Frames, and who gets a slice of one

`Solitaire` is a session: it owns the pieces and hands each one its slice of a
frame. It holds no game logic of its own beyond carrying out the handful of
commands a player can ask for.

| Piece | Holds |
| --- | --- |
| `Controls` | the key map and the corner buttons; turns input into a `PlayerCommand` |
| `PointerInput` | grab / drag / drop / tap-to-select, and the drop-target set |
| `BoardLayout` | card size, pile positions, hit testing |
| `Animator` | `Motion` values from the board become cards crossing the felt |
| `WinCascade` | the fountain a won deal throws down the board |
| `FastForward` | paces playing the rest of the game out, a card at a time |
| `Analyzer` | gives the `Solver` a slice of each frame |
| `ScoreKeeper` | the running record, mute, and the stats overlay |

Everything is on one thread, so a slice and a draw are strictly in series. That
is why the `Analyzer` has a 4ms per-frame budget and pauses entirely while a
stack is held: a drag is the one time the board animates continuously, and the
search is the one thing big enough to be felt inside a frame.

`NeedsRedraw` is the other half of that economy. A solitaire board is static
almost all the time, so the host skips drawing until something changes — the
canvas keeps the last frame either way.

## Moves land instantly; animation runs a step behind

A move changes the position in one step. That is what keeps the rules, the tests,
and the solver simple — they all see a single transition — and it is why
animation is a separate thing.

The board appends `Motion` values as it moves cards, the same way it appends
`Sound` values, and something that knows the geometry decides what those look
like and how long they take. A motion is a description of a change that has
*already happened*, not a request for one. A card in flight is held out of the
pile it is flying to (`HiddenFrom`), so it is drawn once rather than twice.

Both queues are capped, because they are drained only by a running frame loop: if
drawing stops, they stop growing rather than growing for the rest of the session.

## Search

`Solver` asks the full-information question — is this deal still winnable by
someone who can see the face-down cards — which is decidable, unlike the question
the player faces. Positions are packed into a fixed byte layout and every
expanded position is remembered, so the search terminates on its own; the budget
caps how long and how much memory that takes, not whether it stops.

It is resumable: `Step` does a slice and returns, so the search rides the frame
loop instead of freezing the board. Running out of budget yields `Unknown`, never
a wrong answer — `Unwinnable` is reported only after the entire tree below the
position has been examined. `Board.Version` is bumped by every position change so
the search notices the board moving out from under it and restarts.

## Drawing: find what isn't changing, draw it once, blit it

Every canvas call is a hop into JS, so what costs is the number of calls, not the
number of pixels. The whole drawing strategy is one idea applied to three
off-screen canvases — a cache of the settled board, the held stack, and an atlas
of all fifty-two faces at the current card size. The README has the table and the
measurements.

The atlas is the one that matters: it makes a card cost one call wherever it is
going — in flight, in a pile, held, or bouncing off the bottom of the screen.

Draw order is backgrounds, settled piles, the held stack, cards in flight, then
the cascade. `TryHitTest` runs the other way — top-most first, later piles before
earlier ones and the last card of a fanned column before those under it — because
with overlapping cards the first match in draw order is the wrong answer.

## Layout blends; it does not switch

Seven columns and their gutters must fit across the window. `Compactness` runs
from 1 on a handset to 0 on a window, and gutters, margins, and fan are
interpolated across it. Nothing here is allowed to be a step: a threshold made
the card jump nine per cent *downwards* as the window got wider, because a board
that has just started paying for desktop gutters has less left over for card.
**Widening a window must never take card away.**

The face is a separate question, about the card rather than the screen: below
64px a card gets a jumbo index and one large suit instead of a pip layout, and
that is as true of a desktop window dragged narrow as it is of a phone.

## Where a variant would go

This is Klondike concretely, not a rules engine; variant frameworks written
before one variant works are almost always wrong. But the seam is visible:
`Dealer.Deal` and `Rules.IsLegal` are the only two things that encode which game
this is. Layout, drag, undo, animation, and drawing are all variant-agnostic.
Pull those two behind an interface when a second variant exists, not before.

---

## Appendix — toolchain notes

Facts established while building, recorded so they don't have to be rediscovered.

**`Blazor.Extensions.Canvas` 1.1.1** targets netstandard2.1 and dates from 2020,
but works on net9. It has `BeginBatchAsync`/`EndBatchAsync`, `DrawImageAsync`,
`MeasureTextAsync`, save/restore, transforms, clipping, arcs, curves, shadows,
line dashes, and text alignment. It does **not** have `RoundRect` — it predates
it, so rounded corners are a manual `ArcTo` path.

**`_Imports.razor` needs both `@using Blazor.Extensions` and `@using
Blazor.Extensions.Canvas`.** With only the first, `<BECanvas>` silently degrades
to a plain HTML element and `@ref` fails to compile with a confusing
`ElementReference` conversion error.

**`requestAnimationFrame` is suspended while the tab or preview pane is hidden.**
An apparently dead loop and a blank canvas may just mean nothing is compositing.
To verify rendering headlessly, drive one frame via
`dotNet.invokeMethodAsync('OnFrame', 0)` and read pixels back with `getImageData`.

**The game assembly is `internal` throughout,** with `InternalsVisibleTo` for the
test project. This is an application, not a library — that is what lets members
be `public` where an interface requires it without any of them becoming API, and
it lets the tests arrange positions by hand.
