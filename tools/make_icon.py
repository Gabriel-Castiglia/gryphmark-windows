"""Quillforge icon: a glowing quill nib forged over an anvil, on a dark ember tile."""
import math, sys
from PIL import Image, ImageDraw, ImageFilter

S = 1024
out_ico, out_png = sys.argv[1], sys.argv[2]

def lerp(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))

# --- tile: deep night gradient with a warm forge glow at the bottom ---
tile = Image.new("RGBA", (S, S))
px = tile.load()
top, bot = (22, 24, 38), (46, 20, 24)
for y in range(S):
    c = lerp(top, bot, y / S)
    for x in range(S):
        px[x, y] = c + (255,)
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gd = ImageDraw.Draw(glow)
gd.ellipse((S*0.12, S*0.52, S*0.88, S*1.15), fill=(255, 120, 30, 150))
glow = glow.filter(ImageFilter.GaussianBlur(110))
tile = Image.alpha_composite(tile, glow)

mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle((0, 0, S - 1, S - 1), radius=int(S * 0.22), fill=255)

art = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(art)

# --- anvil (steel, bottom) ---
steel_hi, steel_lo = (176, 184, 200), (92, 98, 116)
anvil = [(S*0.20, S*0.70), (S*0.80, S*0.70), (S*0.74, S*0.76), (S*0.60, S*0.78),
         (S*0.62, S*0.86), (S*0.70, S*0.88), (S*0.30, S*0.88), (S*0.38, S*0.86),
         (S*0.40, S*0.78), (S*0.26, S*0.76)]
d.polygon(anvil, fill=steel_lo)
d.polygon([(S*0.20, S*0.70), (S*0.80, S*0.70), (S*0.76, S*0.735), (S*0.24, S*0.735)], fill=steel_hi)
# horn
d.polygon([(S*0.20, S*0.70), (S*0.08, S*0.705), (S*0.24, S*0.75)], fill=steel_hi)

# --- quill nib, molten gold, pointing down onto the anvil ---
cx, tipy, topy, half = S*0.5, S*0.68, S*0.14, S*0.20
def side(sg):
    pts = []
    for i in range(0, 21):  # tip -> widest belly (quadratic curve)
        t = i / 20
        x0, y0, x1, y1, x2, y2 = 0, tipy, S*0.19, S*0.52, S*0.17, S*0.36
        x = (1-t)**2*x0 + 2*(1-t)*t*x1 + t*t*x2; y = (1-t)**2*y0 + 2*(1-t)*t*y1 + t*t*y2
        pts.append((cx + sg*x, y))
    for i in range(1, 11):  # belly -> neck
        t = i / 10
        x0, y0, x1, y1, x2, y2 = S*0.17, S*0.36, S*0.16, S*0.26, S*0.10, S*0.22
        x = (1-t)**2*x0 + 2*(1-t)*t*x1 + t*t*x2; y = (1-t)**2*y0 + 2*(1-t)*t*y1 + t*t*y2
        pts.append((cx + sg*x, y))
    pts.append((cx + sg*S*0.10, topy))
    return pts
L = side(-1); R = side(1)
nib = L + R[::-1]
nib_layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
nd = ImageDraw.Draw(nib_layer)
nd.polygon(nib, fill=(255, 255, 255, 255))
# vertical molten gradient for the nib
grad = Image.new("RGBA", (S, S))
gp = grad.load()
for y in range(S):
    t = min(max((y - topy) / (tipy - topy), 0), 1)
    c = lerp((255, 236, 170), (255, 120, 20), t)
    for x in range(S):
        gp[x, y] = c + (255,)
nib_col = Image.composite(grad, Image.new("RGBA", (S, S), (0, 0, 0, 0)), nib_layer.split()[3])
# outer heat glow
halo = nib_layer.copy().filter(ImageFilter.GaussianBlur(40))
halo = Image.composite(Image.new("RGBA", (S, S), (255, 150, 40, 255)), Image.new("RGBA", (S, S), (0, 0, 0, 0)), halo.split()[3].point(lambda v: int(v * 0.85)))
art = Image.alpha_composite(art, halo)
art = Image.alpha_composite(art, nib_col)
d = ImageDraw.Draw(art)
# slit and breather hole
dark = (60, 22, 10, 255)
d.line([(cx, tipy - S*0.01), (cx, S*0.36)], fill=dark, width=int(S*0.018))
r = S*0.035
d.ellipse((cx - r, S*0.33 - r, cx + r, S*0.33 + r), fill=dark)
# neck band
d.rectangle((cx - S*0.10, S*0.205, cx + S*0.10, S*0.225), fill=(150, 60, 15, 255))

# --- sparks from the strike point ---
import random
random.seed(7)
spark = Image.new("RGBA", (S, S), (0, 0, 0, 0))
sd = ImageDraw.Draw(spark)
for i in range(22):
    a = math.radians(random.choice([random.uniform(195, 240), random.uniform(300, 345)]))
    l = random.uniform(S*0.05, S*0.16)
    x0, y0 = cx + math.cos(a)*S*0.05, tipy + math.sin(a)*S*0.02
    x1, y1 = x0 + math.cos(a)*l, y0 + math.sin(a)*l
    sd.line([(x0, y0), (x1, y1)], fill=(255, 200, 90, 230), width=int(S*0.007))
spark = Image.alpha_composite(spark.filter(ImageFilter.GaussianBlur(6)), spark)
art = Image.alpha_composite(art, spark)

icon = Image.alpha_composite(tile, art)
final = Image.new("RGBA", (S, S), (0, 0, 0, 0))
final.paste(icon, (0, 0), mask)
final.save(out_png)
final.save(out_ico, sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
print("ok")
