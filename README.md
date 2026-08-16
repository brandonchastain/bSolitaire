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
| `BSolitaire/wwwroot/js/game.js` | `requestAnimationFrame` loop + DPI-aware canvas sizing. |

## How a frame works

1. `game.js` calls `Home.OnFrame(timestamp)` once per animation frame.
2. `Home` calls `Solitaire.Update(elapsed)`, then `IGameDrawer.Draw(game)`.
3. The next frame is requested only after that round trip completes, so a slow
   frame delays the next one rather than queueing interop calls behind it.

Input flows the same direction: `Home` forwards clicks and key presses to
`Solitaire.OnClick(x, y)` / `OnKeyDown(code)`. Coordinates are CSS pixels with
the origin at the top-left of the board — the canvas is scaled for device pixel
ratio in JS, so the game never deals with DPI.
