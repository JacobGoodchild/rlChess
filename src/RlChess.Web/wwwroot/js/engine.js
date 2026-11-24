// Stockfish in a background worker (so the page never freezes).
//
// Stockfish is GPLv3 software by the Stockfish developers (https://stockfishchess.org), compiled to
// WebAssembly by Nathan Rugg (https://github.com/nmrugg/stockfish.js). It is NOT part of this
// repository: the browser downloads the official npm build from jsDelivr, and only when an
// analysis feature is switched on. This page talks to it with plain-text UCI commands, the same
// way any chess GUI talks to a separate engine program.

export const STOCKFISH_BASE = "https://cdn.jsdelivr.net/npm/stockfish@19.0.1/bin/stockfish-19-lite-single";

export function createStockfish(dotnet, base) {
    base = base || STOCKFISH_BASE;
    // A worker must come from our own site, so it is a one-line script that loads the engine from the CDN.
    // The engine reads the location of its .wasm file from the part after "#".
    const source = `importScripts(${JSON.stringify(base + ".js")});`;
    const url = URL.createObjectURL(new Blob([source], { type: "application/javascript" }));
    const worker = new Worker(url + "#" + encodeURIComponent(base + ".wasm"));
    worker.onmessage = (e) => {
        const line = String(e.data);
        // Only pass on what the page uses (Stockfish also prints a line for every move it looks at).
        if (line.startsWith("info") && !line.includes(" pv ")) return;
        dotnet.invokeMethodAsync("OnEngineLine", line);
    };
    worker.onerror = (e) => {
        e.preventDefault?.();
        dotnet.invokeMethodAsync("OnEngineError", e.message || "Stockfish couldn't be loaded");
    };
    return {
        send: (cmd) => worker.postMessage(cmd),
        terminate: () => { worker.terminate(); URL.revokeObjectURL(url); },
    };
}
