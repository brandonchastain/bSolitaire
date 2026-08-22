namespace BSolitaire.Game;

/// <summary>
/// Everything a drawer needs to paint a frame, and nothing else. <see cref="Solitaire"/> is
/// the only implementation; the point of naming the set is that it is a set — a renderer that
/// takes this cannot reach <see cref="Solitaire.Reset"/>, the pointer handlers, or anything
/// else that would move the game on while it is being drawn.
///
/// Read-only throughout. Painting a picture of a position must not be able to change it.
/// </summary>
internal interface ISolitaireView
{
    /// <summary>The cards and where they sit.</summary>
    Board Board { get; }

    /// <summary>Where everything goes at the current viewport size.</summary>
    BoardLayout Layout { get; }

    /// <summary>Whether the game is still going, and if not, how it ended.</summary>
    GameState State { get; }

    /// <summary>The stack currently held by the pointer, or null.</summary>
    DragState? Drag { get; }

    /// <summary>Piles that would accept the stack being dragged. Empty unless one is held.</summary>
    IReadOnlyList<Location> DropTargets { get; }

    /// <summary>The pile the pointer could pick up from right now, or null.</summary>
    Location? GrabbablePile { get; }

    /// <summary>Index of the lowest card that would come up from <see cref="GrabbablePile"/>.</summary>
    int GrabbableIndex { get; }

    /// <summary>Cards part-way between two piles, to be painted over the board.</summary>
    IReadOnlyList<CardInFlight> InFlight { get; }

    /// <summary>The index from which a pile must not be drawn, because the cards above it are
    /// still in the air on their way to it.</summary>
    int HiddenFrom(Location loc);

    /// <summary>Cards bouncing down the board after a win.</summary>
    IReadOnlyList<FallingCard> Falling { get; }

    /// <summary>Whether the undo button is drawn.</summary>
    bool CanUndo { get; }

    /// <summary>Whether the play-it-out button is drawn.</summary>
    bool CanFastForward { get; }

    /// <summary>Whether the end-of-game panel is up.</summary>
    bool ShowBanner { get; }

    /// <summary>Whether the draw-time overlay is up.</summary>
    bool ShowStats { get; }

    /// <summary>Whether the board is silent, for the speaker on the felt.</summary>
    bool Muted { get; }

    /// <summary>The line drawn along the bottom of the board.</summary>
    PlayerScore Score { get; }

    /// <summary>How the search on the current position is going.</summary>
    SolveResult Analysis { get; }

    /// <summary>Positions the search has examined on the current board.</summary>
    int AnalysisNodes { get; }

    /// <summary>Last unhandled exception, painted on the board. Null when all is well.</summary>
    string? Error { get; }
}
