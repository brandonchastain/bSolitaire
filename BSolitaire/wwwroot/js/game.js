// Bridges the browser's animation frame loop and window resizes into Blazor.
// Game logic belongs in C# — keep this file boring.
window.bSolitaire = (() => {
    let dotNet = null;
    let canvas = null;

    function resize() {
        // Back the canvas at device resolution but keep the drawing coordinate
        // system in CSS pixels, so the game never has to think about DPI.
        const dpr = window.devicePixelRatio || 1;
        const width = window.innerWidth;
        const height = window.innerHeight;

        canvas.width = Math.round(width * dpr);
        canvas.height = Math.round(height * dpr);
        canvas.style.width = width + 'px';
        canvas.style.height = height + 'px';
        canvas.getContext('2d').setTransform(dpr, 0, 0, dpr, 0, 0);

        dotNet.invokeMethodAsync('OnResize', width, height);
    }

    function frame(timestamp) {
        // Chained rather than fire-and-forget: a slow frame delays the next
        // one instead of queueing up interop calls behind it.
        dotNet.invokeMethodAsync('OnFrame', timestamp)
            .then(() => requestAnimationFrame(frame));
    }

    return {
        start: (dotNetRef) => {
            dotNet = dotNetRef;
            const board = document.getElementById('board');
            canvas = board.querySelector('canvas');

            resize();
            window.addEventListener('resize', resize);
            board.focus();
            requestAnimationFrame(frame);
        }
    };
})();
