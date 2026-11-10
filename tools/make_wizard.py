"""Inno Setup wizard bitmaps from the icon: big side panel (forge gradient) and small top-right logo."""
import pathlib
from PIL import Image, ImageDraw, ImageFont

root = pathlib.Path(__file__).resolve().parents[1]
icon = Image.open(root / "assets" / "quillforge.png").convert("RGBA")

def gradient(w, h):
    im = Image.new("RGB", (w, h)); px = im.load()
    for y in range(h):
        t = y / h
        c = tuple(int(a + (b - a) * t) for a, b in zip((21, 23, 42), (122, 52, 18)))
        for x in range(w): px[x, y] = c
    return im

# 164x314 at 100% scale; Inno also accepts larger for high DPI, use 2x
W, H = 328, 628
big = gradient(W, H)
ic = icon.resize((220, 220), Image.LANCZOS)
big.paste(ic, ((W - 220) // 2, 150), ic)
d = ImageDraw.Draw(big)
try:
    f = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", 46)
    f2 = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 20)
except OSError:
    f = f2 = ImageFont.load_default()
for text, font, y, col in (("Quillforge", f, 400, (255, 255, 255)), ("Markdown · GitHub", f2, 462, (255, 207, 138))):
    w = d.textlength(text, font=font)
    d.text(((W - w) / 2, y), text, font=font, fill=col)
big.save(root / "installer" / "wizard.bmp")

small = Image.new("RGB", (110, 116), (255, 255, 255))
ic = icon.resize((104, 104), Image.LANCZOS)
small.paste(ic, (3, 6), ic)
small.save(root / "installer" / "wizard-small.bmp")
print("ok")
