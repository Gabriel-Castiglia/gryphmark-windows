"""Vigía icon: the lighthouse of Debenne's design (c. 1815, The Met, CC0) on a flat colour tile.
The outline and the source are in assets/icon_vigia.json; the drawing is done by tools/engraving.py.
usage: make_icon.py <out.ico> <out_base.png> [R,G,B]
Writes the icon in the tile colour given (the base one is the original orange, 232,96,44, which
tools/make_theme_icons.py recolours to the twelve accents) as a 1024 px PNG and a 16-256 px .ico.
Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
Date: November 20, 2026
SPDX-License-Identifier: GPL-2.0-only
"""
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import engraving  # noqa: E402

root = pathlib.Path(__file__).resolve().parents[1]
out_ico, out_png = sys.argv[1], sys.argv[2]
bg = tuple(int(v) for v in (sys.argv[3] if len(sys.argv) > 3 else "232,96,44").split(","))
img = engraving.icon(engraving.load(root / "assets" / "icon_vigia.json"), bg)
img.save(out_png)
img.save(out_ico, sizes=[(z, z) for z in (256, 128, 64, 48, 40, 32, 24, 20, 16)])
print("icon:", out_ico, out_png)
