"""Quillforge icon: a document page with the Markdown mark (M + down arrow) and a glowing quill writing on it."""
import math, sys
from PIL import Image, ImageDraw, ImageFilter

S = 1024
out_ico, out_png = sys.argv[1], sys.argv[2]

def lerp(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))

def vgrad(w, h, c0, c1):
    g = Image.new("RGBA", (w, h)); p = g.load()
    for y in range(h):
        c = lerp(c0, c1, y / max(h - 1, 1)) + (255,)
        for x in range(w): p[x, y] = c
    return g

# ---- tile: night blue to ember ----
tile = vgrad(S, S, (24, 28, 52), (120, 44, 16))
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse((S*0.30, S*0.55, S*1.10, S*1.20), fill=(255, 130, 40, 140))
tile = Image.alpha_composite(tile, glow.filter(ImageFilter.GaussianBlur(120)))
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle((0, 0, S - 1, S - 1), radius=int(S * 0.22), fill=255)

# ---- document page (drawn upright on its own layer, then tilted) ----
P = Image.new("RGBA", (S, S), (0, 0, 0, 0))
pd = ImageDraw.Draw(P)
x0, y0, x1, y1, fold = S*0.20, S*0.14, S*0.74, S*0.86, S*0.13
page = [(x0, y0), (x1 - fold, y0), (x1, y0 + fold), (x1, y1), (x0, y1)]
# shadow
sh = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(sh).polygon([(x + S*0.02, y + S*0.03) for x, y in page], fill=(0, 0, 0, 150))
P = Image.alpha_composite(P, sh.filter(ImageFilter.GaussianBlur(22)))
pd = ImageDraw.Draw(P)
pd.polygon(page, fill=(250, 246, 238, 255))
pd.polygon([(x1 - fold, y0), (x1 - fold, y0 + fold), (x1, y0 + fold)], fill=(222, 210, 192, 255))

# Markdown mark: rounded frame + "M" + down arrow
mx0, my0, mx1, my1 = x0 + S*0.06, S*0.40, x1 - S*0.06, S*0.66
ink = (30, 32, 48, 255)
pd.rounded_rectangle((mx0, my0, mx1, my1), radius=int(S*0.035), outline=ink, width=int(S*0.028))
h = my1 - my0; pad = h*0.24; lw = int(S*0.045)
mL, mR = mx0 + pad*1.05, mx0 + (mx1 - mx0)*0.56
top, bot = my0 + pad, my1 - pad
pd.line([(mL, bot), (mL, top), ((mL + mR)/2, top + (bot - top)*0.55), (mR, top), (mR, bot)], fill=ink, width=lw, joint="curve")
ax = mx0 + (mx1 - mx0)*0.79
pd.line([(ax, top), (ax, bot - S*0.05)], fill=ink, width=lw)
aw = S*0.065
pd.polygon([(ax - aw, bot - S*0.075), (ax + aw, bot - S*0.075), (ax, bot + S*0.01)], fill=ink)
# writing lines
for i, yy in enumerate((S*0.24, S*0.30, S*0.72, S*0.78)):
    ww = (x1 - x0 - S*0.12) * (0.55 if i in (1, 3) else 0.85)
    pd.rounded_rectangle((x0 + S*0.06, yy, x0 + S*0.06 + ww, yy + S*0.022), radius=int(S*0.011), fill=(196, 186, 172, 255))
P = P.rotate(6, resample=Image.BICUBIC, center=(S*0.47, S*0.5))

# ---- quill feather, diagonal, nib touching the page ----
Q = Image.new("RGBA", (S, S), (0, 0, 0, 0))
qd = ImageDraw.Draw(Q)
# feather built along the x axis then rotated: tip at the left
L0, L1, cy = S*0.10, S*0.74, S*0.5
def vane(sign, width):
    pts = []
    for i in range(41):
        t = i / 40
        x = L0 + S*0.18 + (L1 - L0 - S*0.18) * t
        w = width * math.sin(math.pi * min(t * 1.05, 1)) ** 0.8 * (0.35 + 0.65 * t)
        pts.append((x, cy + sign * w))
    return pts
up, dn = vane(-1, S*0.085), vane(1, S*0.060)
feather = [(L0 + S*0.18, cy)] + up + [(L1, cy)] + dn[::-1]
fg = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(fg).polygon(feather, fill=(255, 255, 255, 255))
fcol = Image.new("RGBA", (S, S)); fp = fcol.load()
for x in range(S):
    t = min(max((x - L0) / (L1 - L0), 0), 1)
    c = lerp((255, 214, 120), (255, 120, 30), t) + (255,)
    for y in range(S): fp[x, y] = c
Q = Image.composite(fcol, Q, fg.split()[3])
qd = ImageDraw.Draw(Q)
for i in range(1, 15):  # barb notches
    x = L0 + S*0.24 + i * (L1 - L0 - S*0.26) / 15
    qd.line([(x, cy), (x + S*0.03, cy - S*0.045)], fill=(200, 90, 20, 160), width=int(S*0.006))
    qd.line([(x, cy), (x + S*0.025, cy + S*0.035)], fill=(200, 90, 20, 130), width=int(S*0.006))
qd.line([(L0 + S*0.12, cy), (L1, cy)], fill=(120, 50, 14, 255), width=int(S*0.014))  # shaft
qd.polygon([(L0, cy), (L0 + S*0.13, cy - S*0.022), (L0 + S*0.13, cy + S*0.022)], fill=(45, 30, 30, 255))  # nib
Q = Q.rotate(45, resample=Image.BICUBIC, center=(L0, cy))
# place: nib at the end of the last writing line (right-lower area of the page)
qx, qy = S*0.50 - L0, S*0.79 - cy
Q2 = Image.new("RGBA", (S, S), (0, 0, 0, 0)); Q2.paste(Q, (int(qx), int(qy)), Q)
halo = Q2.split()[3].filter(ImageFilter.GaussianBlur(28)).point(lambda v: int(v * 0.7))
halo_img = Image.composite(Image.new("RGBA", (S, S), (255, 150, 50, 255)), Image.new("RGBA", (S, S), (0, 0, 0, 0)), halo)
# spark where the nib writes
sp = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(sp).ellipse((S*0.485, S*0.775, S*0.515, S*0.805), fill=(255, 220, 140, 200))
sp = Image.alpha_composite(sp.filter(ImageFilter.GaussianBlur(14)), sp.filter(ImageFilter.GaussianBlur(3)))

icon = tile
for layer in (P, halo_img, Q2, sp):
    icon = Image.alpha_composite(icon, layer)
final = Image.new("RGBA", (S, S), (0, 0, 0, 0))
final.paste(icon, (0, 0), mask)
final.save(out_png)
final.save(out_ico, sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
print("ok")
