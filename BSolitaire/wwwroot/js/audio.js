// Every noise the board makes, synthesized. There are no audio files: a card sound is
// filtered noise with a very fast decay, and the foundation ping is a sine blip, so the
// whole sound set is cheaper than one download and every repeat can be detuned slightly
// instead of being the identical sample again. Game logic belongs in C# — the game names
// a sound (see Game/Sound.cs) and this file decides what that means.
window.bSolitaireAudio = (() => {
    // Indices, in the order Sound's members are declared. C# sends ints.
    const DEAL = 0, FLIP = 1, PLACE = 2, STOCK = 3, RECYCLE = 4, INVALID = 5, FOUNDATION = 6, WIN = 7,
          UNDO = 8;

    const VOLUME = 0.35;

    let ctx = null;
    let master = null;
    let noise = null;
    let muted = false;

    // Browsers refuse to start audio until the user has done something, and a context
    // created before that is born suspended and stays that way. So it is built on the
    // first press instead — by which time the player has certainly clicked the board.
    function ensure() {
        if (ctx) {
            if (ctx.state === 'suspended') {
                ctx.resume();
            }

            return true;
        }

        const Ctor = window.AudioContext || window.webkitAudioContext;
        if (!Ctor) {
            return false;
        }

        ctx = new Ctor();
        master = ctx.createGain();
        master.gain.value = muted ? 0 : VOLUME;
        master.connect(ctx.destination);

        // One second of white noise, reused by every card sound. Generating it per hit
        // would allocate a fresh buffer several times a second during a deal.
        noise = ctx.createBuffer(1, ctx.sampleRate, ctx.sampleRate);
        const data = noise.getChannelData(0);
        for (let i = 0; i < data.length; i++) {
            data[i] = Math.random() * 2 - 1;
        }

        return true;
    }

    // A card: a burst of noise, band-limited and gone in a few tens of milliseconds. The
    // filter frequency is what separates a crisp flip from a soft landing.
    function card(at, options) {
        const { freq = 2000, q = 0.7, gain = 0.5, decay = 0.055 } = options || {};

        const src = ctx.createBufferSource();
        src.buffer = noise;
        // A random window into the noise, so two cards in a row are never the same burst.
        const offset = Math.random() * (noise.duration - decay - 0.05);

        const filter = ctx.createBiquadFilter();
        filter.type = 'bandpass';
        filter.frequency.value = freq;
        filter.Q.value = q;

        const env = ctx.createGain();
        env.gain.setValueAtTime(0, at);
        env.gain.linearRampToValueAtTime(gain, at + 0.004);
        env.gain.exponentialRampToValueAtTime(0.0001, at + decay);

        src.connect(filter).connect(env).connect(master);
        src.start(at, offset, decay + 0.02);
        src.stop(at + decay + 0.02);
    }

    // A pitched blip: foundations, the win, and the body of the refusal thunk.
    function tone(at, freq, options) {
        const { type = 'sine', gain = 0.25, decay = 0.18 } = options || {};

        const osc = ctx.createOscillator();
        osc.type = type;
        osc.frequency.value = freq;

        const env = ctx.createGain();
        env.gain.setValueAtTime(0, at);
        env.gain.linearRampToValueAtTime(gain, at + 0.008);
        env.gain.exponentialRampToValueAtTime(0.0001, at + decay);

        osc.connect(env).connect(master);
        osc.start(at);
        osc.stop(at + decay + 0.02);
    }

    // A run of cards flicking past: the deal, and turning the waste back over.
    function riffle(at, count, spacing, freq) {
        for (let i = 0; i < count; i++) {
            card(at + i * spacing * (0.8 + Math.random() * 0.4), {
                freq: freq * (0.85 + Math.random() * 0.3),
                gain: 0.22,
                decay: 0.03
            });
        }
    }

    // Cards going home in a run should climb, the way the classic cascade does — but a
    // single card played by hand is just a ping. Rather than have C# track that, a
    // foundation hit close behind the last one continues the run and anything slower
    // starts the run over.
    let foundationStep = 0;
    let lastFoundation = -10;
    const RUN_GAP = 0.5;

    // Semitones above the root, held to a pentatonic run so a long cascade stays musical
    // instead of turning into a chromatic siren.
    const STEPS = [0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24];
    const ROOT = 523.25; // C5

    function foundation(at) {
        foundationStep = at - lastFoundation < RUN_GAP
            ? Math.min(foundationStep + 1, STEPS.length - 1)
            : 0;
        lastFoundation = at;

        tone(at, ROOT * Math.pow(2, STEPS[foundationStep] / 12), {
            type: 'triangle',
            gain: 0.18,
            decay: 0.22
        });
        card(at, { freq: 2600, gain: 0.3, decay: 0.035 });
    }

    // Two hits of the same kind inside this window are one event as far as the ear is
    // concerned, and stacking them only clips. The foundation run is exempt: rapid
    // repeats are the whole point of it.
    const MIN_GAP = 0.03;
    const lastPlayed = {};

    function play(sound, at) {
        if (sound !== FOUNDATION) {
            if (at - (lastPlayed[sound] === undefined ? -10 : lastPlayed[sound]) < MIN_GAP) {
                return;
            }

            lastPlayed[sound] = at;
        }

        switch (sound) {
            case DEAL:
                riffle(at, 14, 0.035, 2200);
                break;

            case FLIP:
                card(at, { freq: 3000, gain: 0.4, decay: 0.045 });
                break;

            case PLACE:
                card(at, { freq: 1400, q: 1.1, gain: 0.5, decay: 0.07 });
                break;

            case STOCK:
                card(at, { freq: 2400, gain: 0.42, decay: 0.05 });
                break;

            case RECYCLE:
                riffle(at, 20, 0.018, 1800);
                break;

            case INVALID:
                // Dull and low: the sound of a card not going anywhere.
                tone(at, 120, { type: 'sine', gain: 0.22, decay: 0.09 });
                card(at, { freq: 320, q: 1.4, gain: 0.3, decay: 0.06 });
                break;

            case FOUNDATION:
                foundation(at);
                break;

            case UNDO:
                // A place run backwards: the pitch falls instead of landing flat, which is
                // as close as a noise gets to saying "that did not happen".
                tone(at, 420, { type: 'triangle', gain: 0.16, decay: 0.12 });
                tone(at + 0.05, 300, { type: 'triangle', gain: 0.14, decay: 0.14 });
                card(at, { freq: 1200, q: 1.1, gain: 0.3, decay: 0.05 });
                break;

            case WIN:
                [0, 4, 7, 12, 16, 19, 24].forEach((semitone, i) => {
                    tone(at + i * 0.09, ROOT * Math.pow(2, semitone / 12), {
                        type: 'triangle',
                        gain: 0.2,
                        decay: 0.5
                    });
                });
                break;
        }
    }

    // What each noise feels like in the hand. A phone can say things the speaker cannot —
    // it works with the sound turned down, in a pocket, in a room where a solitaire game
    // making noises would be rude — and a card landing is exactly the sort of small
    // confirmation that is better felt than heard. Milliseconds; a list alternates buzz
    // and pause. Nothing here is longer than a card landing.
    const BUZZ = {
        [FLIP]: 8,
        [PLACE]: 12,
        [STOCK]: 8,
        [RECYCLE]: [12, 30, 12],
        [INVALID]: [22, 40, 22],
        [FOUNDATION]: [10, 25, 18],
        [UNDO]: [8, 30, 8],
        [WIN]: [30, 60, 30, 60, 90],
    };

    // Only the strongest of a frame's noises is felt. Several overlapping patterns queue up
    // in the vibration hardware and arrive as one long undifferentiated buzz.
    const BUZZ_RANK = [DEAL, FLIP, STOCK, PLACE, UNDO, RECYCLE, FOUNDATION, INVALID, WIN];

    function buzz(sounds) {
        if (!navigator.vibrate) {
            return;
        }

        let best = null;
        for (const sound of sounds) {
            if (BUZZ[sound] !== undefined &&
                (best === null || BUZZ_RANK.indexOf(sound) > BUZZ_RANK.indexOf(best))) {
                best = sound;
            }
        }

        if (best !== null) {
            navigator.vibrate(BUZZ[best]);
        }
    }

    return {
        // Called from the first pointer press, where a gesture is guaranteed.
        unlock: () => ensure(),

        setMuted: (value) => {
            muted = value;

            if (master) {
                // Ramped rather than set: a gain jump on a sounding note is a click.
                master.gain.setTargetAtTime(muted ? 0 : VOLUME, ctx.currentTime, 0.01);
            }
        },

        // One call per frame carrying everything the board asked for, so a deal is a
        // single interop hop rather than fourteen.
        play: (sounds) => {
            if (muted || !sounds || sounds.length === 0) {
                return;
            }

            // Felt before it is heard, and independently of it: the audio context can fail
            // to exist — an old browser, a refused unlock — and the board should still
            // answer in the hand when it does.
            buzz(sounds);

            if (!ensure()) {
                return;
            }

            // Scheduled a hair ahead of now: WebAudio drops anything scheduled in the
            // past, and this leaves room for the rest of the frame's work.
            const at = ctx.currentTime + 0.01;
            for (const sound of sounds) {
                play(sound, at);
            }
        }
    };
})();
