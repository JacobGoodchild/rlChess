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

    const onDown = (e) => {
        if (e.button !== undefined && e.button !== 0) return;
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
            document.body.appendChild(g);
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

    const onUp = (e) => finish(e, false);
    const onCancel = (e) => finish(e, true);
    const onContext = (e) => e.preventDefault();

    boardEl.addEventListener("pointerdown", onDown);
    boardEl.addEventListener("pointermove", onMove);
    boardEl.addEventListener("pointerup", onUp);
    boardEl.addEventListener("pointercancel", onCancel);
    boardEl.addEventListener("contextmenu", onContext);

    return {
        dispose() {
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
