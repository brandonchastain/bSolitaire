namespace BSolitaire.Game;

/// <summary>Where a hint has got to. The three are kept apart on purpose: a move the search
/// could not certify is not a hint, and must never be shown as one.</summary>
internal enum HintState
{
    /// <summary>Nobody has asked.</summary>
    Idle,

    /// <summary>Asked, and still working through the candidates.</summary>
    Looking,

    /// <summary>A move the search proved keeps the deal winnable.</summary>
    Found,

    /// <summary>Every candidate was tried and none could be proved. Not "there is no good
    /// move" — only that none of them could be certified inside the budget.</summary>
    NoneCertain
}

/// <summary>
/// Suggests a move, and only ever one it can prove keeps the deal winnable.
///
/// The <see cref="Solver"/> gives a verdict and not a line, so the line is not read out of
/// it — it is rebuilt one move at a time. Each legal move is played on a board of its own
/// and the position that results is handed to a fresh search; the first that comes back
/// winnable is the hint. That is <see cref="DealVetter"/>'s trick at the scale of a single
/// move: keep on <see cref="SolveResult.Winnable"/>, discard on anything else, and let the
/// cheap half of the verdict do the work.
///
/// The honesty rule is the same one the rest of the game is built on. A move the search ran
/// out of budget on is not a hint, and is not offered as one; the answer in that case is
/// that nothing could be certified, which is a different sentence from "there is no move".
///
/// Moves are tried best-first — cards going home, then cards moving between columns, then
/// turning the stock — so the usual case resolves on the first candidate rather than the
/// twentieth.
/// </summary>
internal sealed class Hinter
{
    /// <summary>Positions per frame, matching the other two searches.</summary>
    private const int SearchSlice = 8000;

    /// <summary>The slice of a frame a hint may have. The same four milliseconds the
    /// analyzer and the vetter take, for the same reason: one thread, one frame.</summary>
    private static readonly TimeSpan SearchBudget = TimeSpan.FromMilliseconds(4);

    private readonly Board board;

    /// <summary>A board of its own to try a move on. A real board and not a bare position,
    /// because playing a move means turning over whatever it uncovered, and the rule for
    /// that lives in <see cref="Board.MakeMove"/> and is not to be written twice.</summary>
    private readonly Board probe = new();

    private readonly List<Move> candidates = new();

    private int next;
    private int askedAtVersion = -1;
    private Solver? search;

    public Hinter(Board board) => this.board = board;

    public HintState State { get; private set; } = HintState.Idle;

    /// <summary>The move to suggest, set only while <see cref="State"/> is
    /// <see cref="HintState.Found"/>.</summary>
    public Move? Hint { get; private set; }

    /// <summary>Asks for a hint about the position as it stands. Asking again while one is
    /// already on screen puts it away, so the same button does both.</summary>
    public void Ask()
    {
        if (State != HintState.Idle)
        {
            Forget();
            return;
        }

        askedAtVersion = board.Version;
        State = HintState.Looking;
        Hint = null;
        search = null;
        next = 0;

        candidates.Clear();
        candidates.AddRange(Ordered(board.Position, board.DrawCount));
    }

    /// <summary>Puts away whatever was being shown or looked for.</summary>
    public void Forget()
    {
        State = HintState.Idle;
        Hint = null;
        search = null;
        candidates.Clear();
        next = 0;
    }

    /// <summary>
    /// Gives the hunt its slice of a frame, and reports whether what is on screen should
    /// change. Paused for the same things the other searches pause for.
    /// </summary>
    public bool Update(bool paused)
    {
        if (State is HintState.Idle or HintState.Found or HintState.NoneCertain)
        {
            // A move answers the question the hint was asked about, so the answer goes.
            if (State != HintState.Idle && askedAtVersion != board.Version)
            {
                Forget();
                return true;
            }

            return false;
        }

        if (askedAtVersion != board.Version)
        {
            Forget();
            return true;
        }

        if (paused)
        {
            return false;
        }

        if (search is null)
        {
            if (next >= candidates.Count)
            {
                State = HintState.NoneCertain;
                return true;
            }

            search = Try(candidates[next]);
        }

        if (!search.Step(SearchSlice, SearchBudget))
        {
            return false;
        }

        if (search.Result == SolveResult.Winnable)
        {
            Hint = candidates[next];
            State = HintState.Found;
            search = null;
            return true;
        }

        search = null;
        next++;
        return false;
    }

    /// <summary>
    /// The moves worth trying, in the order worth trying them: cards going home first,
    /// then cards moving between columns, and turning the stock last. The search does not
    /// care about the order, but the player waiting for it does — the move a hint usually
    /// wants is near the front.
    /// </summary>
    private static IEnumerable<Move> Ordered(Position position, int drawCount)
    {
        var moves = Rules.LegalMoves(position, drawCount).ToList();

        return moves
            .Where(move => move.To.Kind == PileKind.Foundation)
            .Concat(moves.Where(move => move.To.Kind == PileKind.Tableau))
            .Concat(moves.Where(move => move.From.Kind == PileKind.FaceDown));
    }

    /// <summary>Plays one candidate on the spare board and points a search at what it left.</summary>
    private Solver Try(Move candidate)
    {
        probe.Position.Strip();

        foreach (var kind in Position.AllKinds)
        {
            for (int i = 0; i < board.Position.PileCountOf(kind); i++)
            {
                var loc = new Location(kind, i);

                foreach (var card in board.Position.Pile(loc))
                {
                    var copy = new Card(card.Suit, card.Rank);
                    copy.SetFaceUp(card.IsFaceUp);
                    probe.Position.Place(loc, copy);
                }
            }
        }

        probe.Settle();
        probe.MakeMove(candidate);

        // The spare board is never drawn or listened to, and its queues are capped.
        probe.ClearSounds();
        probe.ClearMotions();

        return new Solver(probe.Position);
    }
}
