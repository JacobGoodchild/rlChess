// Browser-side helpers for the board: pointer handling (drag and click), sounds, clipboard, keys.
// All the chess logic lives in C#; this file only reports which squares were pressed/released.

const DRAG_THRESHOLD = 4; // pixels before a press becomes a drag

export function attachBoard(boardEl, dotnet) {
    let press = null; // { sq, x, y, pieceEl, ghost, dragging, pointerId }

    const squareAt = (clientX, clientY) => {
        const r = boardEl.getBoundingClientRect();
        const fx = (clientX - r.left) / r.width, fy = (clientY - r.top) / r.height;
        if (fx < 0 || fx >= 1 || fy < 0 || fy >= 1) return -1;
        let file = Math.floor(fx * 8), rankFromTop = Math.floor(fy * 8);
        const flipped = boardEl.dataset.orientation === "black";
        if (flipped) file = 7 - file;
        const rank = flipped ? rankFromTop : 7 - rankFromTop;
        return rank * 8 + file;
    };

    const clearHover = () => boardEl.querySelectorAll(".drag-over").forEach(e => e.remove());

    const showHover = (sq) => {
        clearHover();
        if (sq < 0) return;
        const flipped = boardEl.dataset.orientation === "black";
        const file = sq % 8, rank = Math.floor(sq / 8);
        const x = flipped ? 7 - file : file, y = flipped ? rank : 7 - rank;
        const el = document.createElement("div");
        el.className = "drag-over";
        el.style.transform = `translate(${x * 100}%, ${y * 100}%)`;
        boardEl.appendChild(el);
    };

    const moveGhost = (e) => {
        const g = press.ghost;
        const size = boardEl.getBoundingClientRect().width / 8;
        g.style.width = g.style.height = size + "px";
        g.style.transform = `translate(${e.clientX - size / 2}px, ${e.clientY - size / 2}px)`;
    };

    // ---- right-click annotations: drag for an arrow, click for a circle (Shift = red, Alt = blue)
    const shapes = new Map(); // key -> {from, to, color}
    let rightFrom = -1;
    const svgNs = "http://www.w3.org/2000/svg";
    const layer = document.createElementNS(svgNs, "svg");
    layer.setAttribute("class", "shapes");
    layer.setAttribute("viewBox", "0 0 8 8");
    layer.innerHTML = `<defs>${["green", "red", "blue"].map(c =>
        `<marker id="ah-${c}" orient="auto" markerWidth="4" markerHeight="4" refX="2.05" refY="2"><path d="M0,0 V4 L3,2 Z" class="${c}"/></marker>`).join("")}</defs><g></g>`;
    boardEl.appendChild(layer);
    const group = layer.querySelector("g");

    const centre = (sq) => {
        const flipped = boardEl.dataset.orientation === "black";
        const f = sq % 8, r = Math.floor(sq / 8);
        return [(flipped ? 7 - f : f) + 0.5, (flipped ? r : 7 - r) + 0.5];
    };

    const drawShapes = () => {
        let html = "";
        for (const s of shapes.values()) {
            const [x1, y1] = centre(s.from);
            if (s.from === s.to) {
                html += `<circle cx="${x1}" cy="${y1}" r="0.46" class="${s.color}" fill="none" stroke-width="0.07"/>`;
            } else {
                const [x2, y2] = centre(s.to);
                const len = Math.hypot(x2 - x1, y2 - y1), cut = 0.3;
                const ex = x2 - (x2 - x1) / len * cut, ey = y2 - (y2 - y1) / len * cut;
                html += `<line x1="${x1}" y1="${y1}" x2="${ex}" y2="${ey}" class="${s.color}" stroke-width="0.16" stroke-linecap="round" marker-end="url(#ah-${s.color})"/>`;
            }
        }
        group.innerHTML = html;
    };

    const clearShapes = () => { if (shapes.size) { shapes.clear(); drawShapes(); } };

    const onDown = (e) => {
        if (e.button === 2) {
            rightFrom = squareAt(e.clientX, e.clientY);
            e.preventDefault();
            dotnet.invokeMethodAsync("OnRightClick"); // e.g. cancels a premove
            return;
        }
        if (e.button !== undefined && e.button !== 0) return;
        clearShapes();
        if (e.target.closest(".promotion")) return; // the promotion picker handles its own clicks
        const sq = squareAt(e.clientX, e.clientY);
        if (sq < 0) return;
        e.preventDefault();
        const pieceEl = boardEl.querySelector(`.piece[data-sq="${sq}"]`);
        press = { sq, x: e.clientX, y: e.clientY, pieceEl, ghost: null, dragging: false, pointerId: e.pointerId };
        boardEl.setPointerCapture?.(e.pointerId);
        dotnet.invokeMethodAsync("OnSquareDown", sq);
    };

    const onMove = (e) => {
        if (!press || e.pointerId !== press.pointerId) return;
        if (!press.dragging) {
            const canDrag = press.pieceEl && press.pieceEl.classList.contains("mine");
            if (!canDrag || Math.hypot(e.clientX - press.x, e.clientY - press.y) < DRAG_THRESHOLD) return;
            press.dragging = true;
            const g = press.pieceEl.cloneNode(false);
            g.className = press.pieceEl.className.replace(/\bmine\b/, "") + " ghost";
            g.removeAttribute("style");
            (boardEl.closest(".app") ?? document.body).appendChild(g); // inside .app so the piece-set CSS applies
            press.ghost = g;
            press.pieceEl.classList.add("drag-origin");
        }
        moveGhost(e);
        showHover(squareAt(e.clientX, e.clientY));
    };

    const finish = (e, cancelled) => {
        if (!press || e.pointerId !== press.pointerId) return;
        const p = press;
        press = null;
        clearHover();
        if (p.ghost) p.ghost.remove();
        if (p.pieceEl) p.pieceEl.classList.remove("drag-origin");
        const to = cancelled ? -1 : squareAt(e.clientX, e.clientY);
        dotnet.invokeMethodAsync("OnSquareUp", p.sq, p.dragging ? to : p.sq, p.dragging);
    };

    const onUp = (e) => {
        if (e.button === 2) {
            const to = squareAt(e.clientX, e.clientY);
            if (rightFrom >= 0 && to >= 0) {
                const color = e.shiftKey ? "red" : (e.altKey || e.ctrlKey || e.metaKey) ? "blue" : "green";
                const key = rightFrom + "-" + to;
                const existing = shapes.get(key);
                if (existing && existing.color === color) shapes.delete(key);
                else shapes.set(key, { from: rightFrom, to, color });
                drawShapes();
            }
            rightFrom = -1;
            return;
        }
        finish(e, false);
    };
    const onCancel = (e) => finish(e, true);
    const onContext = (e) => e.preventDefault();

    boardEl.addEventListener("pointerdown", onDown);
    boardEl.addEventListener("pointermove", onMove);
    boardEl.addEventListener("pointerup", onUp);
    boardEl.addEventListener("pointercancel", onCancel);
    boardEl.addEventListener("contextmenu", onContext);

    return {
        clearShapes,
        redraw: drawShapes, // e.g. after the board is flipped
        dispose() {
            layer.remove();
            boardEl.removeEventListener("pointerdown", onDown);
            boardEl.removeEventListener("pointermove", onMove);
            boardEl.removeEventListener("pointerup", onUp);
            boardEl.removeEventListener("pointercancel", onCancel);
            boardEl.removeEventListener("contextmenu", onContext);
        }
    };
}

// ---------------------------------------------------------------- sounds

const sounds = {};
export function playSound(name) {
    try {
        let a = sounds[name];
        if (!a) {
            a = sounds[name] = new Audio(`sounds/${name}.wav`);
            a.preload = "auto";
        }
        const clip = a.paused ? a : a.cloneNode();
        clip.volume = 0.8;
        clip.currentTime = 0;
        clip.play().catch(() => { /* browsers block audio until the first tap; ignore */ });
    } catch { }
}

export function preloadSounds(names) {
    for (const n of names) {
        if (!sounds[n]) { sounds[n] = new Audio(`sounds/${n}.wav`); sounds[n].preload = "auto"; }
    }
}

// ---------------------------------------------------------------- misc

export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        // Fallback for older browsers / non-secure contexts.
        const ta = document.createElement("textarea");
        ta.value = text;
        ta.style.position = "fixed";
        ta.style.opacity = "0";
        document.body.appendChild(ta);
        ta.select();
        let ok = false;
        try { ok = document.execCommand("copy"); } catch { }
        ta.remove();
        return ok;
    }
}

export function downloadText(filename, text) {
    const url = URL.createObjectURL(new Blob([text], { type: "application/x-chess-pgn" }));
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export function getSetting(key) {
    try { return localStorage.getItem("rlchess." + key); } catch { return null; }
}

export function setSetting(key, value) {
    try { localStorage.setItem("rlchess." + key, value); } catch { }
}

export function attachKeys(dotnet) {
    const handler = (e) => {
        if (e.target.closest("input, textarea, select")) return;
        const keys = ["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End", "f", "Escape"];
        if (!keys.includes(e.key)) return;
        e.preventDefault();
        dotnet.invokeMethodAsync("OnKey", e.key);
    };
    window.addEventListener("keydown", handler);
    return { dispose() { window.removeEventListener("keydown", handler); } };
}

export function scrollMoveIntoView(container) {
    const el = container?.querySelector(".active");
    if (!el) { if (container) container.scrollTop = container.scrollHeight; return; }
    // Vertical list on desktop, horizontal strip on phones.
    el.scrollIntoView({ block: "nearest", inline: "nearest" });
}
