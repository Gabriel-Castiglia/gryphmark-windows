"""Builds a file:// test page from wwwroot so the editor can be screenshotted with headless Edge.
Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
Date: November 10, 2026
SPDX-License-Identifier: GPL-2.0-only
"""
import sys, json, pathlib
root = pathlib.Path(__file__).resolve().parents[1]
www = root / "src" / "Gryphmark" / "wwwroot"
mode, theme, lang, out = sys.argv[1], sys.argv[2], sys.argv[3], pathlib.Path(sys.argv[4])
html = (www / "index.html").read_text(encoding="utf-8").replace("https://app.local/", www.as_uri() + "/")
js = (www / "app.js").read_text(encoding="utf-8").replace("https://app.local/", www.as_uri() + "/")
md = (root / "tools" / "sample.md").read_text(encoding="utf-8")
html = html.replace('<script src="' + www.as_uri() + '/app.js"></script>',
                    "<script>" + js + "</script><script>mdApp.init(%s,%s,%s);mdApp.load(%s,null);</script>"
                    % (json.dumps(lang), json.dumps(theme), json.dumps(mode), json.dumps(md)))
out.write_text(html, encoding="utf-8")
print(out.as_uri())
