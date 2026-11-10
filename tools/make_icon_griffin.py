"""Quillforge icon, engraving style: head of Martin Schongauer's Griffin (c. 1485, The Met, CC0)
on a flat colour tile, with a quill held in the beak.
Source: https://commons.wikimedia.org/wiki/File:Griffin_MET_DP820017.jpg
usage: make_icon_griffin.py <source.jpg> <out.ico> <out.png> [R,G,B] [cx,cy,size]"""
import sys
import numpy as np, cv2
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage

src, out_ico, out_png = sys.argv[1:4]
bg = tuple(int(v) for v in (sys.argv[4] if len(sys.argv) > 4 else "232,96,44").split(","))
crop = [int(v) for v in (sys.argv[5] if len(sys.argv) > 5 else "1090,330,760").split(",")]
S = 1024

g = cv2.imread(src, cv2.IMREAD_GRAYSCALE)
cx, cy, sz = crop
x0, y0 = max(cx - sz // 2, 0), max(cy - sz // 2, 0)
g = g[y0:y0 + sz, x0:x0 + sz]
g = cv2.resize(g, (S, S), interpolation=cv2.INTER_CUBIC)

# paper -> 1.0, ink -> 0.0 (normalise the uneven paper tone)
paper = cv2.GaussianBlur(cv2.dilate(g, np.ones((15, 15), np.uint8)), (0, 0), 25)
norm = np.clip(g.astype(np.float32) / np.maximum(paper.astype(np.float32), 1), 0, 1)
ink = norm < 0.72

# silhouette = ink, closed and hole-filled, so the body is cream and the outside takes the tile colour
closed = cv2.morphologyEx(ink.astype(np.uint8), cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (13, 13)))
filled = ndimage.binary_fill_holes(closed)
lab, n = ndimage.label(filled)
if n:
    sizes = ndimage.sum(filled, lab, range(1, n + 1))
    keep = 1 + int(np.argmax(sizes))
    filled = lab == keep
filled = cv2.erode(filled.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5))).astype(bool) | (ink & filled)
sil = cv2.GaussianBlur(filled.astype(np.float32), (0, 0), 1.0)

# compose: tile colour, cream body, dark engraved lines
cream = np.array([244, 236, 218], np.float32)
inkc = np.array([22, 20, 24], np.float32)
tone = np.clip((norm - 0.35) / 0.55, 0, 1)[..., None]          # keep the hatching greys
body = inkc + (cream - inkc) * tone
tile = np.array(bg, np.float32)
rgb = tile * (1 - sil[..., None]) + body * sil[..., None]
img = Image.fromarray(rgb.astype(np.uint8)).convert("RGBA")

# ---- quill held in the beak, drawn as an engraving (cream body, ink outline and barbs) ----
import math
INK, CREAM = (22, 20, 24, 255), (244, 236, 218, 255)
mx, my = 572, 392            # where the shaft enters the mouth
ang = math.radians(float(sys.argv[6]) if len(sys.argv) > 6 else 24)   # downward slope towards the right
L = 440                       # visible length from the mouth
W = 92                        # half width of the vane
ux, uy = math.cos(ang), math.sin(ang)
nx, ny = -uy, ux              # normal (points down-left)
def P(t, w):                  # point at distance t along the shaft, w across it
    return (mx + ux * t + nx * w, my + uy * t + ny * w)
Q = Image.new("RGBA", (S, S), (0, 0, 0, 0)); qd = ImageDraw.Draw(Q)
f0, f1 = 70, L                # vane from f0 to the end
top, bot = [], []
for i in range(41):
    t = i / 40; d = f0 + (f1 - f0) * t
    w = W * math.sin(math.pi * min(t * 1.08, 1)) ** 0.75 * (0.45 + 0.55 * t)
    top.append(P(d, -w)); bot.append(P(d, w * 0.72))
vane = [P(f0, 0)] + top + [P(f1 + 18, 0)] + bot[::-1]
qd.polygon(vane, fill=CREAM)
# barbs: thin ink strokes from the shaft to the edges, slanting towards the tip
for i in range(1, 30):
    t = i / 30; d = f0 + (f1 - f0) * t
    w = W * math.sin(math.pi * min(t * 1.08, 1)) ** 0.75 * (0.45 + 0.55 * t)
    qd.line([P(d, 0), P(d + 34, -w * 0.97)], fill=INK, width=4)
    qd.line([P(d, 0), P(d + 26, w * 0.70)], fill=INK, width=4)
qd.line(vane + [vane[0]], fill=INK, width=8, joint="curve")
qd.line([P(-60, 0), P(f1 + 14, 0)], fill=INK, width=12)            # shaft, entering the mouth
qd.line([P(-60, 0), P(f1 + 14, 0)], fill=CREAM, width=4)
img = Image.alpha_composite(img, Q)

mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle((0, 0, S - 1, S - 1), radius=int(S * 0.22), fill=255)
final = Image.new("RGBA", (S, S), (0, 0, 0, 0))
final.paste(img, (0, 0), mask)
final.save(out_png)
final.save(out_ico, sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
print("ok")
