"""An icon from an old engraving or drawing: the motif, cut out by a hand-drawn outline, on a flat colour tile.
Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
Date: November 20, 2026
SPDX-License-Identifier: GPL-2.0-only

Used by tools/make_icon.py; the motif is described by a JSON spec beside it:
  src        the picture (public domain), relative to the spec
  crop       [x0, y0, x1, y1] in the picture
  shapes     outline of the motif, in crop coordinates: polygons ([[x, y], ...]) and rectangles
             ({"rect": [x0, y0, x1, y1]}); their union is the motif
  auto       true: the shapes only mark where to look, and the motif is the ink found there, closed, its holes
             filled and only its largest piece kept (an engraving on paper); with "threshold" and "close"
  lines      thin lines kept as ink (a flag pole): [[x0, y0, x1, y1, width], ...]
  pad        part of the icon left around the motif (default 0.13)
  gamma      contrast of the drawing inside the motif (default 1.0; below 1 is lighter)
  desaturate [hue_min, hue_max]: pixels of that hue (OpenCV 0-180; red smoke is [160, 15]) become paper
The motif is cream paper with ink lines and a thin ink edge; under it a soft shadow on the tile.
"""
import json
import pathlib

import cv2
import numpy as np

S = 1024
CREAM = np.array([246, 238, 220], np.float32)
INK = np.array([40, 30, 22], np.float32)


def load(spec_path):
    p = pathlib.Path(spec_path)
    spec = json.loads(p.read_text(encoding="utf-8"))
    spec["src"] = str((p.parent / spec["src"]).resolve())
    return spec


def motif(spec):
    """The motif at icon size: M (0..1, how much of it covers each pixel) and its colour (float RGB)."""
    img = cv2.imread(spec["src"], cv2.IMREAD_COLOR)
    if img is None:
        raise SystemExit("cannot read " + spec["src"])
    x0, y0, x1, y1 = spec["crop"]
    img = img[y0:y1, x0:x1]
    h, w = img.shape[:2]

    mask = np.zeros((h, w), np.uint8)
    for s in spec["shapes"]:
        if isinstance(s, dict):
            a, b, c, d = s["rect"]
            cv2.rectangle(mask, (int(a), int(b)), (int(c), int(d)), 255, -1)
        else:
            cv2.fillPoly(mask, [np.array(s, np.int32)], 255)
    for ln in spec.get("lines", []):
        cv2.line(mask, (int(ln[0]), int(ln[1])), (int(ln[2]), int(ln[3])), 255, int(ln[4]))

    if spec.get("auto"):
        gg = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        paper = cv2.GaussianBlur(cv2.dilate(gg, np.ones((9, 9), np.uint8)), (0, 0), 9)
        norm = np.clip(gg.astype(np.float32) / np.maximum(paper.astype(np.float32), 1), 0, 1)
        ink = ((norm < spec.get("threshold", 0.78)) & (mask > 0)).astype(np.uint8)
        k = spec.get("close", 5)
        closed = cv2.morphologyEx(ink, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k, k)))
        ff = closed * 255
        cv2.floodFill(ff, np.zeros((h + 2, w + 2), np.uint8), (0, 0), 128)
        sil = (ff != 128).astype(np.uint8)
        sil = cv2.morphologyEx(sil, cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3)))
        n, lab, stats, _ = cv2.connectedComponentsWithStats(sil)
        big = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
        mask = (lab == big).astype(np.uint8) * 255

    g = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY).astype(np.float32)
    if "desaturate" in spec:
        hsv = cv2.cvtColor(img, cv2.COLOR_BGR2HSV)
        lo, hi = spec["desaturate"]
        hue, sat = hsv[..., 0], hsv[..., 1]
        tint = ((hue >= lo) | (hue <= hi)) if lo > hi else ((hue >= lo) & (hue <= hi))
        tint &= sat > 40
        g[tint] = np.maximum(g[tint], 225)
    inside = mask > 0
    lo_v, hi_v = np.percentile(g[inside], 2), np.percentile(g[inside], 98)
    tone = np.clip((g - lo_v) / max(hi_v - lo_v, 1), 0, 1) ** spec.get("gamma", 1.0)

    ys, xs = np.nonzero(mask)
    bx0, bx1, by0, by1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    box = int(S * (1 - 2 * spec.get("pad", 0.13)))
    scale = box / max(bx1 - bx0, by1 - by0)
    nw, nh = max(1, round((bx1 - bx0) * scale)), max(1, round((by1 - by0) * scale))
    t_s = cv2.resize(tone[by0:by1, bx0:bx1], (nw, nh), interpolation=cv2.INTER_CUBIC)
    m_s = cv2.resize(mask[by0:by1, bx0:bx1].astype(np.float32) / 255, (nw, nh), interpolation=cv2.INTER_LINEAR)
    ox, oy = (S - nw) // 2, (S - nh) // 2
    T = np.zeros((S, S), np.float32); T[oy:oy + nh, ox:ox + nw] = np.clip(t_s, 0, 1)
    M = np.zeros((S, S), np.float32); M[oy:oy + nh, ox:ox + nw] = m_s
    M = cv2.GaussianBlur(M, (0, 0), 0.8)

    edge = np.clip(M - cv2.erode(M, np.ones((5, 5), np.uint8)), 0, 1)
    col = CREAM[None, None, :] * T[..., None] + INK[None, None, :] * (1 - T[..., None])
    col = col * (1 - edge[..., None]) + INK[None, None, :] * edge[..., None]
    return M, col


def compose(M, col, bg):
    """The motif over a plain background of colour bg (RGB), with its soft shadow (float RGB, S x S)."""
    img = np.empty((S, S, 3), np.float32)
    img[:] = np.array(bg, np.float32)
    shadow = cv2.GaussianBlur(np.roll(np.roll(M, 10, 0), 6, 1), (0, 0), 14) * 0.35
    img *= (1 - shadow[..., None])
    return img * (1 - M[..., None]) + col * M[..., None]


def rounded_tile(radius=0.22):
    """The rounded square of the icon (0..1), anti-aliased."""
    from PIL import Image, ImageDraw
    m = Image.new("L", (S * 4, S * 4), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, S * 4 - 1, S * 4 - 1), radius=int(S * 4 * radius), fill=255)
    return np.array(m.resize((S, S), Image.LANCZOS), np.float32) / 255


def icon(spec, bg, radius=0.22):
    """The finished icon as an RGBA PIL image, 1024 px."""
    from PIL import Image
    M, col = motif(spec)
    rgb = compose(M, col, bg)
    a = rounded_tile(radius)
    return Image.fromarray(np.dstack([np.clip(rgb, 0, 255), a * 255]).round().astype(np.uint8), "RGBA")
