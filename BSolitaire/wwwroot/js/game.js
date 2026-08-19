// Bridges the browser's animation frame loop, pointer motion, and window resizes
// into Blazor. Game logic belongs in C# — keep this file boring.
window.bSolitaire = (() => {
    let dotNet = null;
    let canvas = null;
    let cache = null;

    // Pointer motion is the one event that fires faster than the board can draw — a
    // gaming mouse reports several times per frame, and each report used to be its own
    // JS -> .NET call queued behind the last. Only the newest position can affect the
    // picture, so the moves are coalesced here and handed to the frame that will use
    // them. Everything else the player does is rare enough to stay a Blazor event.
    let moveX = 0;
    let moveY = 0;
    let hasMove = false;

    function resize() {
        // Back the canvas at device resolution but keep the drawing coordinate
        // system in CSS pixels, so the game never has to think about DPI.
        const dpr = window.devicePixelRatio || 1;
        const width = window.innerWidth;
        const height = window.innerHeight;

        for (const el of [canvas, cache]) {
            el.width = Math.round(width * dpr);
            el.height = Math.round(height * dpr);
            el.style.width = width + 'px';
            el.style.height = height + 'px';
            el.getContext('2d').setTransform(dpr, 0, 0, dpr, 0, 0);
        }

        dotNet.invokeMethodAsync('OnResize', width, height);
    }

    function frame(timestamp) {
        const x = moveX;
        const y = moveY;
        const moved = hasMove;
        hasMove = false;

        // Chained rather than fire-and-forget: a slow frame delays the next
        // one instead of queueing up interop calls behind it.
        dotNet.invokeMethodAsync('OnFrame', timestamp, moved, x, y)
            .then(() => requestAnimationFrame(frame));
    }

    // The score record lives in this browser and nowhere else, so localStorage is the whole
    // of the persistence layer. Shape is decided by the C# record it round-trips through.
    const SCORE_KEY = 'bSolitaire.score';

    return {
        loadScore: () => localStorage.getItem(SCORE_KEY),

        saveScore: (json) => localStorage.setItem(SCORE_KEY, json),

        start: (dotNetRef) => {
            dotNet = dotNetRef;
            const board = document.getElementById('board');
            canvas = board.querySelector('canvas');
            cache = document.querySelector('#cache canvas');

            // Audio can only be started from inside a user gesture, and Blazor's own
            // pointerdown handler is not one — it arrives over interop, after the event.
            // So the context is opened here, on the real event, once.
            board.addEventListener('pointerdown', () => window.bSolitaireAudio.unlock(), { once: true });

            board.addEventListener('pointermove', (e) => {
                moveX = e.offsetX;
                moveY = e.offsetY;
                hasMove = true;
            });

            resize();
            window.addEventListener('resize', resize);
            board.focus();
            requestAnimationFrame(frame);
        }
    };
})();
