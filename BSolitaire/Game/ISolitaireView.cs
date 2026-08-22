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
    Board Board { get; }

    BoardLayout Layout { get; }

    GameState State { get; }

    DragState? Drag { get; }

    IReadOnlyList<Location> DropTargets { get; }

    Location? GrabbablePile { get; }

    int GrabbableIndex { get; }

    IReadOnlyList<CardInFlight> InFlight { get; }

    IReadOnlyList<FallingCard> Falling { get; }

    bool CanUndo { get; }

    bool CanFastForward { get; }

    bool ShowBanner { get; }

    bool ShowStats { get; }

    bool Muted { get; }

    PlayerScore Score { get; }

    SolveResult Analysis { get; }

    int AnalysisNodes { get; }

    string? Error { get; }

    int HiddenFrom(Location loc);
}
