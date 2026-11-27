// Stockfish in a background worker (so the page never freezes).
//
// Stockfish is GPLv3 software by the Stockfish developers (https://stockfishchess.org), compiled to
// WebAssembly by Nathan Rugg (https://github.com/nmrugg/stockfish.js). It is NOT part of this
// repository: the browser downloads the official npm build from unpkg, and only when it is
// needed (analysis, or Stockfish as an opponent). This page talks to it with plain-text UCI commands, the same
// way any chess GUI talks to a separate engine program.

// Where the official npm build is downloaded from, tried in order. (jsDelivr refuses this package:
// the npm package is bigger than its 150 MB limit, so it's only a last resort.)
export const STOCKFISH_BASES = [
    "https://unpkg.com/stockfish@19.0.1/bin/stockfish-19-lite-single",
    "https://cdn.jsdelivr.net/npm/stockfish@19.0.1/bin/stockfish-19-lite-single",
];

function startWorker(base) {
    // A worker must come from our own site, so it is a one-line script that loads the engine from the CDN.
    // The engine reads the location of its .wasm file from the part after "#".
    const source = `importScripts(${JSON.stringify(base + ".js")});`;
    const url = URL.createObjectURL(new Blob([source], { type: "application/javascript" }));
    return { worker: new Worker(url + "#" + encodeURIComponent(base + ".wasm")), url };
}

export function createStockfish(dotnet, base) {
    const bases = base ? [base] : STOCKFISH_BASES;
    let index = 0, current = null, started = false;
    const pending = []; // commands sent before a worker is running

    const launch = () => {
        current = startWorker(bases[index]);
        current.worker.onmessage = (e) => {
            started = true;
            const line = String(e.data);
            // Only pass on what the page uses (Stockfish also prints a line for every move it looks at).
            if (line.startsWith("info") && !line.includes(" pv ")) return;
            dotnet.invokeMethodAsync("OnEngineLine", line);
        };
        current.worker.onerror = (e) => {
            e.preventDefault?.();
            current.worker.terminate();
            URL.revokeObjectURL(current.url);
            if (!started && index + 1 < bases.length) {
                index++;            // that download failed: try the next place
                launch();
                return;
            }
            dotnet.invokeMethodAsync("OnEngineError", e.message || "Stockfish couldn't be loaded");
        };
        for (const cmd of pending) current.worker.postMessage(cmd);
    };
    launch();

    return {
        send: (cmd) => {
            if (!started) pending.push(cmd); // replayed if we have to switch to another download
            current.worker.postMessage(cmd);
        },
        terminate: () => { current.worker.terminate(); URL.revokeObjectURL(current.url); },
    };
}
