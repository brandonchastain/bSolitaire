# bSolitaire

A custom solitaire game in Blazor WebAssembly, rendered to a 2D canvas.

## Run

```bash
dotnet run --project BSolitaire
```

Then open the URL it prints (http://localhost:5080).

## Layout

| Path | Purpose |
| --- | --- |
| `BSolitaire/Game/Solitaire.cs` | The game. Plain C#, no Blazor or JS — write the rules here. |
| `BSolitaire/Game/IGameDrawer.cs` | Rendering seam, so the game never touches a canvas. |
| `BSolitaire/Game/CanvasDrawer.cs` | Draws the game to a 2D canvas. Placeholder for now. |
| `BSolitaire/Pages/Home.razor` | Host component: wires the frame loop and input to the game. |
| `BSolitaire/Game/Sound.cs` | Sound seam: the game names a noise, it never makes one. |
| `BSolitaire/Game/Motion.cs` | Motion seam: the board names a card that moved, it never animates one. |
| `BSolitaire/Game/Animator.cs` | Turns motions into cards crossing the felt. |
| `BSolitaire/Game/WinCascade.cs` | The fountain of cards a won deal throws down the board. |
| `BSolitaire/wwwroot/js/game.js` | `requestAnimationFrame` loop + DPI-aware canvas sizing. |
| `BSolitaire/wwwroot/js/audio.js` | Synthesizes every sound in WebAudio. No audio files. |

## How a frame works

1. `game.js` calls `Home.OnFrame(timestamp)` once per animation frame.
2. `Home` calls `Solitaire.Update(elapsed)`, then `IGameDrawer.Draw(game)`.
3. The next frame is requested only after that round trip completes, so a slow
   frame delays the next one rather than queueing interop calls behind it.

Sound flows out the same way the picture does: the board appends to a list of
`Sound` values as it moves cards, `Home` hands the whole list to `audio.js` once
per frame, and the browser decides what a "place" or a "flip" actually sounds
like. Nothing is downloaded — a card is a burst of filtered noise, a foundation
is a brighter, shorter one, and the win is the one flourish with a tune to it. The
speaker in the bottom-right corner mutes, as does `M`, and the setting is saved
with the score.

## Moving pictures

A move lands instantly. That is what keeps the rules, the tests, and the solver simple — they
all see a position change in one step — and it is why the animation is a separate thing that
runs a step behind. The board appends to a list of `Motion` values as it moves cards, the same
way it appends to a list of `Sound` values, and `Animator` is what knows the geometry and so
what knows a move takes a hundred and fifty milliseconds. A card in the air is held out of the
pile it is flying to, so it is drawn once rather than twice.

The one exception is the win: `WinCascade` throws the foundations down the board, and its
trail is painted *into* the cached board rather than over it, so every frame of it stays where
it fell.

## Why it is fast

Every canvas call is a hop into JS, so what costs is the number of calls, not the number of
pixels. A printed pip layout is a hundred-odd of them. The whole of the drawing strategy is
therefore one idea applied four times: find the thing that is not changing, draw it once, and
blit it afterwards.

| Off-screen canvas | Holds | Redrawn when |
| --- | --- | --- |
| `#cache` | The felt, every settled pile, the corner controls, the drop-target rings | A pile changes, or a control has something new to say |
| `#held` | The stack under the pointer | It is picked up |
| `#atlas` | All fifty-two faces and the back, at the current card size | The card size changes — a few faces per frame, in the gaps between moves |

The atlas is the one that matters most, because it makes a card cost one call to draw
wherever it is going: in flight, in a pile, held, or bouncing off the bottom of the screen
after a win. Before it, a deal had nine faces in the air at once and drew every one of them
from scratch on every frame.

Press `F` for the draw time. A deal went from 155ms a frame to 2.8; a drag from 9.2ms to
under 3.

## Mobile

Seven tableau columns have to fit across the window, so on a phone the gutters between them
are what the card size is competing with — a desktop board spends an eighth of its width on
gaps, and a phone cannot afford that. `BoardLayout.Compactness` runs from 1 on a handset to 0
on a window, and the gutters, the margins, and the fan are interpolated across it rather than
switched at a threshold: a threshold made the card jump nine per cent, *downwards*, as the
window got wider, because a board that has just started paying for desktop gutters has less
left over for card. Widening a window must never take card away.

The face is a separate question, and it is one about the card rather than about the screen:
below 64px `BoardLayout.SmallCards` asks for a jumbo index and one large suit instead of a
full pip layout, because a card fifty pixels wide with its face scaled to match is a card
nobody can read — and that is as true of a desktop window dragged narrow as it is of a phone.

The rest of it is about the difference between a fingertip and a pointer. A touched stack
rides above the finger rather than under it, and the drop is probed from where the card is
drawn — aim the card, not the hand. There is no hover on a touch screen, so a held stack
lights up every pile that would take it. A second tap on a selected card sends it home, which
is the double-click shortcut spelt as something a phone can reliably report. `navigator.vibrate`
answers in the hand alongside the speaker, out of the same list of sounds. And there is an
undo, on the felt as well as on `Z`, because the device most likely to need one has no
keyboard.

Input flows the same direction: `Home` forwards clicks and key presses to
`Solitaire.OnClick(x, y)` / `OnKeyDown(code)`. Coordinates are CSS pixels with
the origin at the top-left of the board — the canvas is scaled for device pixel
ratio in JS, so the game never deals with DPI.
