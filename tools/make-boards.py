"""Generates the textured board images (wood, marble) in src/RlChess.Web/wwwroot/boards.

They're made from scratch with random noise (no third-party images), so there are no licence
questions. Run: python3 tools/make-boards.py   (needs numpy and Pillow)
"""
import os
import numpy as np
from PIL import Image

SIZE = 640          # pixels; the board is scaled to fit by the browser
SQ = SIZE // 8
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "RlChess.Web", "wwwroot", "boards")


def smooth_noise(h, w, scale, rng):
    """Value noise: random grid, upscaled smoothly."""
    gh, gw = max(2, h // scale + 2), max(2, w // scale + 2)
    grid = rng.random((gh, gw))
    img = Image.fromarray((grid * 255).astype(np.uint8)).resize((w + scale, h + scale), Image.BICUBIC)
    a = np.asarray(img, dtype=np.float32)[:h, :w] / 255.0
    return a


def fbm(h, w, rng, scales=(64, 32, 16, 8)):
    total, amp, norm = np.zeros((h, w), np.float32), 1.0, 0.0
    for s in scales:
        total += amp * smooth_noise(h, w, s, rng)
        norm += amp
        amp *= 0.5
    return total / norm


def mix(c1, c2, t):
    c1, c2 = np.array(c1, np.float32), np.array(c2, np.float32)
    return c1 + (c2 - c1) * t[..., None]


def stretched(h, w, rng, along_x, factor=8, scales=(32, 16, 8)):
    """Noise stretched along the grain, so the grain lines run long and straight-ish."""
    if along_x:
        n = fbm(h, max(8, w // factor), rng, scales=scales)
        return np.asarray(Image.fromarray((n * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), np.float32) / 255
    n = fbm(max(8, h // factor), w, rng, scales=scales)
    return np.asarray(Image.fromarray((n * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), np.float32) / 255


def wood_square(rng, light, dark, vertical):
    h = w = SQ
    n = stretched(h, w, rng, along_x=not vertical)
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    across = xs if vertical else ys
    phase = rng.random() * 10
    rings = np.sin(across * 0.22 + n * 5.0 + phase) * 0.5 + 0.5
    rings = rings ** 3
    fine = stretched(h, w, rng, along_x=not vertical, factor=12, scales=(4, 2)) * 0.22
    t = np.clip(rings * 0.5 + fine + n * 0.3, 0, 1)
    return mix(light, dark, t)


def marble_square(rng, base, vein, veins=1.0):
    h = w = SQ
    n = fbm(h, w, rng, scales=(80, 40, 20, 10))
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    angle = 0.6 + rng.random() * 0.5          # veins run roughly diagonally across the board
    d = xs * np.cos(angle) + ys * np.sin(angle)
    v = np.abs(np.sin(d * 0.035 + n * 3.2 + rng.random() * 6))
    v = (1 - np.clip(v * 3.0, 0, 1)) ** 1.5   # soft lines where the wave crosses zero
    cloud = fbm(h, w, rng, scales=(60, 30)) * 0.35
    t = np.clip(v * 0.8 * veins + cloud, 0, 1)
    return mix(base, vein, t)


def board(make_light, make_dark, seed):
    rng = np.random.default_rng(seed)
    img = np.zeros((SIZE, SIZE, 3), np.float32)
    for r in range(8):
        for f in range(8):
            light = (r + f) % 2 == 0   # a8 (top-left) is light
            tile = make_light(rng, r, f) if light else make_dark(rng, r, f)
            img[r * SQ:(r + 1) * SQ, f * SQ:(f + 1) * SQ] = tile
    return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))


def main():
    os.makedirs(OUT, exist_ok=True)
    wood = board(
        lambda rng, r, f: wood_square(rng, (236, 207, 160), (205, 166, 113), vertical=(r + f) % 4 == 0),
        lambda rng, r, f: wood_square(rng, (161, 108, 66), (112, 68, 37), vertical=(r + f) % 4 == 1),
        seed=7)
    wood.save(os.path.join(OUT, "wood.jpg"), quality=86, optimize=True)
    marble = board(
        lambda rng, r, f: marble_square(rng, (234, 232, 226), (168, 166, 160)),
        lambda rng, r, f: marble_square(rng, (122, 138, 131), (196, 206, 200), veins=0.8),
        seed=11)
    marble.save(os.path.join(OUT, "marble.jpg"), quality=86, optimize=True)
    for name in ("wood", "marble"):
        print(name, os.path.getsize(os.path.join(OUT, name + ".jpg")) // 1024, "KB")


if __name__ == "__main__":
    main()
