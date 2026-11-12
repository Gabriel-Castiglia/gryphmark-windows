"""Builds the colour variants of the Gryphmark icon used by the theme catalogue (View -> Theme...).

Only the coloured background is recoloured; the cream engraving and its black lines stay as they are.
Each pixel's weight is its saturation (the background is the only saturated area), and the new colour keeps
the original pixel's brightness, so anti-aliased edges and texture survive.

Output: src/Gryphmark/icons/gryphmark_<key>.ico (16-256 px), docs/img/theme_icons.png (contact sheet), and
src/Gryphmark/Gryphmark.ico = the default (gold) variant: exe, installer and shortcuts; assets/gryphmark.png = the
same variant at full size, for the README, the manuals and the GitHub profile.
Keep the keys and colours in sync with Accents.cs.
Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
Date: November 10, 2026
SPDX-License-Identifier: GPL-2.0-only
"""
import pathlib
import numpy as np
from PIL import Image

root = pathlib.Path(__file__).resolve().parents[1]
src = root / "assets" / "gryphmark_base.png"   # the drawn icon, in the original orange
out = root / "src" / "Gryphmark" / "icons"
out.mkdir(exist_ok=True)

ACCENTS = {               # key: icon background
    "gold":   "#C8961E",  # dark gold: the default since 1.6.0
    "purple": "#5B2A86",  # Advent purple (default in 1.5)
    "sky":    "#3FA9E6",  # celeste
    "ember":  "#E8602C",  # the original orange (until 1.4.0)
    "red":    "#C8102E",
    "yellow": "#F2B705",
    "green":  "#1E9E4A",
    "teal":   "#00897B",
    "blue":   "#1F5FD1",
    "pink":   "#D81B79",
    "black":  "#1E1E20",
    "white":  "#F7F7F5",
}

im = Image.open(src).convert("RGBA")
a = np.asarray(im).astype(np.float32) / 255
rgb = a[..., :3]
mx, mn = rgb.max(-1), rgb.min(-1)
sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
w = np.clip((sat - 0.25) / 0.35, 0, 1)[..., None]
bg_mx = float(np.median(mx[(sat > 0.6) & (a[..., 3] > 0.99)]))   # brightness of the plain background
shade = (mx / bg_mx)[..., None]

sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
tiles = []
for key, hexc in ACCENTS.items():
    target = np.array([int(hexc[i:i + 2], 16) / 255 for i in (1, 3, 5)], dtype=np.float32)
    new = np.clip(target * shade, 0, 1)
    res = rgb * (1 - w) + new * w
    img = Image.fromarray((np.concatenate([res, a[..., 3:]], -1) * 255).round().astype(np.uint8), "RGBA")
    img.save(out / f"gryphmark_{key}.ico", sizes=sizes)
    if key == "gold":
        img.save(root / "assets" / "gryphmark.png", optimize=True)
    tiles.append(img.resize((160, 160), Image.LANCZOS))
    print("ok", key)

import shutil
shutil.copyfile(out / "gryphmark_gold.ico", root / "src" / "Gryphmark" / "Gryphmark.ico")

sheet = Image.new("RGBA", (len(tiles) * 176 + 16, 192), (128, 128, 128, 255))
for i, t in enumerate(tiles):
    sheet.paste(t, (16 + i * 176, 16), t)
sheet.save(root / "docs" / "img" / "theme_icons.png")
