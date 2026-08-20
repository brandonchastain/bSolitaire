// Bridges the browser's animation frame loop, pointer motion, and window resizes
// into Blazor. Game logic belongs in C# — keep this file boring.
window.bSolitaire = (() => {
    let dotNet = null;
    let canvas = null;
    let cache = null;
    let held = null;

    // Pointer motion is the one event that fires faster than the board can draw — a
    // gaming mouse reports several times per frame, and each report used to be its own
    // JS -> .NET call queued behind the last. Only the newest position can affect the
    // picture, so the moves are coalesced here and handed to the frame that will use
    // them. Everything else the player does is rare enough to stay a Blazor event.
    // What the canvas was last sized for, so repeated notifications about one change do
    // one piece of work.
    let lastWidth = 0;
    let lastHeight = 0;
    let lastDpr = 0;

    let moveX = 0;
    let moveY = 0;
    let hasMove = false;

    function resize() {
        // Back the canvas at device resolution but keep the drawing coordinate
        // system in CSS pixels, so the game never has to think about DPI.
        //
        // Measured off the board element rather than off the window: the board is inset by
        // the phone's safe areas in CSS, and on a mobile browser window.innerHeight includes
        // whatever the URL bar is currently covering. The element knows how much room it
        // actually has; the window only knows how big the screen is.
        const dpr = window.devicePixelRatio || 1;
        const width = Math.max(1, canvas.parentElement.clientWidth);
        const height = Math.max(1, canvas.parentElement.clientHeight);

        // Called from several places that can all fire for the same change. Laying the
        // board out again is cheap but not free — it throws away the cached picture of the
        // whole felt — so a resize that resizes nothing stops here.
        if (width === lastWidth && height === lastHeight && dpr === lastDpr) {
            return;
        }

        lastWidth = width;
        lastHeight = height;
        lastDpr = dpr;

        for (const el of [canvas, cache, held]) {
            el.width = Math.round(width * dpr);
            el.height = Math.round(height * dpr);
            el.style.width = width + 'px';
            el.style.height = height + 'px';
            el.getContext('2d').setTransform(dpr, 0, 0, dpr, 0, 0);
        }

        dotNet.invokeMethodAsync('OnResize', width, height, dpr);
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
        // The atlas of card faces is the one canvas whose size has nothing to do with the
        // window: it is a grid of card-sized cells, and only C# knows how big a card is.
        // Laid out as a grid rather than a strip because a strip of fifty-three cards is
        // over eight thousand pixels tall, and mobile browsers refuse canvases that big.
        sizeAtlas: (cssWidth, cssHeight) => {
            const dpr = window.devicePixelRatio || 1;
            const el = document.querySelector('#atlas canvas');

            el.width = Math.round(cssWidth * dpr);
            el.height = Math.round(cssHeight * dpr);
            el.style.width = cssWidth + 'px';
            el.style.height = cssHeight + 'px';
            el.getContext('2d').setTransform(dpr, 0, 0, dpr, 0, 0);
        },

        loadScore: () => localStorage.getItem(SCORE_KEY),

        saveScore: (json) => localStorage.setItem(SCORE_KEY, json),

        start: (dotNetRef) => {
            dotNet = dotNetRef;
            const board = document.getElementById('board');
            canvas = board.querySelector('canvas');
            cache = document.querySelector('#cache canvas');
            held = document.querySelector('#held canvas');

            // Audio can only be started from inside a user gesture, and Blazor's own
            // pointerdown handler is not one — it arrives over interop, after the event.
            // So the context is opened here, on the real event, once.
            board.addEventListener('pointerdown', () => window.bSolitaireAudio.unlock(), { once: true });

            // Capture on press, so a drag that leaves the board — off the edge of the
            // window, or over the browser's own furniture — still reports here and still
            // ends in a drop rather than in the cards springing back.
            board.addEventListener('pointerdown', (e) => {
                board.setPointerCapture(e.pointerId);
            });

            board.addEventListener('pointerup', (e) => {
                if (board.hasPointerCapture(e.pointerId)) {
                    board.releasePointerCapture(e.pointerId);
                }
            });

            board.addEventListener('pointermove', (e) => {
                moveX = e.offsetX;
                moveY = e.offsetY;
                hasMove = true;
            });

            resize();

            // The board's own size is the thing that matters, and on a phone it changes for
            // reasons no window event reports: the URL bar sliding away, the keyboard coming
            // up, a rotation, a safe area changing shape. Watching the element covers all of
            // them, including the plain window resize that used to be the only one handled.
            new ResizeObserver(resize).observe(board);

            // ...and the window events as well. The observer is the one that catches a URL
            // bar sliding away, but it is also the newest of these and the easiest for a
            // browser to be quiet about; the others cost nothing and the work is skipped
            // when the size has not actually changed.
            window.addEventListener('resize', resize);
            window.addEventListener('orientationchange', resize);
            window.visualViewport?.addEventListener('resize', resize);
            board.focus();
            requestAnimationFrame(frame);
        }
    };
})();
