"""Headless check of app.js logic: formatting preservation, find, GitHub extras. Prints the JSON result.
Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
Date: November 10, 2026
SPDX-License-Identifier: GPL-2.0-only
"""
import json, pathlib, subprocess, sys, tempfile
root = pathlib.Path(__file__).resolve().parents[1]
www = root / "src" / "Gryphmark" / "wwwroot"
import os
edge = next((p for p in (os.path.expandvars(x) for x in (
    r"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe", r"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe",
    r"%ProgramFiles%\Google\Chrome\Application\chrome.exe", r"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe",
    r"%ProgramFiles%\BraveSoftware\Brave-Browser\Application\brave.exe", r"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\Application\brave.exe"))
    if os.path.exists(p)), None)   # any Chromium browser
if not edge:
    sys.exit("No Chromium browser (Edge, Chrome or Brave) found")

original = """# Title

* item one
* item two

Some __bold__ text and a [link](http://x.y).

Paragraph to edit.

| a | b |
|:--|--:|
| 1 | 2 |

> [!NOTE]
> Alert body

Footnote here[^1] :rocket: and $E=mc^2$.

[^1]: The note.
"""

test = """
<script>
(async () => {
  const out = {};
  try {
    mdApp.init('en','light','visual',true);
    mdApp.load(%s, null);
    out.unchanged_identical = mdApp.getMarkdownPreserving() === %s.replace(/\\r\\n/g,'\\n');
    // edit only "Paragraph to edit." through ProseMirror
    const view = ToastEditor && document.querySelector('.ProseMirror') && null;
    const ed = window.__ed;
    out.ok = true;
  } catch (e) { out.err = String(e && e.stack || e); }
  document.getElementById('out').textContent = JSON.stringify(out);
})();
</script>
"""

def page(extra_js):
    html = (www / "index.html").read_text(encoding="utf-8").replace("https://app.local/", www.as_uri() + "/")
    js = (www / "app.js").read_text(encoding="utf-8").replace("https://app.local/", www.as_uri() + "/")
    fake = "<script>window.chrome={webview:{postMessage:(m)=>{if(m&&m.type==='find')window.__lastFind=m;}}};</script>"
    html = html.replace('<script src="' + www.as_uri() + '/lib/bundle.js"></script>', fake + '<script src="' + www.as_uri() + '/lib/bundle.js"></script>')
    html = html.replace('<script src="' + www.as_uri() + '/app.js"></script>', "<script>" + js + "</script><pre id='out'></pre>" + extra_js)
    f = pathlib.Path(tempfile.gettempdir()) / "gm_jstest.html"
    f.write_text(html, encoding="utf-8")
    r = subprocess.run([edge, "--headless=new", "--disable-gpu", "--no-first-run",
                        "--user-data-dir=" + str(pathlib.Path(tempfile.gettempdir()) / "gm_jstest_profile"), "--allow-file-access-from-files", "--virtual-time-budget=6000", "--dump-dom", f.as_uri()],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    s = r.stdout
    i = s.find("<pre id=\"out\">"); j = s.find("</pre>", i)
    return s[i + 14:j] if i >= 0 else s[-2000:]

js = """
<script>
(async () => {
  const out = {};
  try {
    const ORIG = %(orig)s;
    mdApp.init('en','light','visual',true);
    mdApp.load(ORIG, null);
    out.unchanged_identical = mdApp.getMarkdownPreserving() === ORIG;
    out.normalised_differs = mdApp.getMarkdown() !== ORIG;
    // edit one paragraph via ProseMirror
    const m = toastView();
    let target = null;
    m.state.doc.descendants((n, pos) => { if (n.isTextblock && n.textContent === 'Paragraph to edit.') target = pos; return !target; });
    m.dispatch(m.state.tr.insertText(' EDITED', target + 1 + 'Paragraph to edit.'.length));
    const saved = mdApp.getMarkdownPreserving();
    out.edit_applied = saved.includes('Paragraph to edit. EDITED');
    out.kept_star_list = saved.includes('* item one') && saved.includes('* item two');
    out.kept_underscore_bold = saved.includes('__bold__');
    out.kept_table_align = saved.includes('|:--|--:|');
    out.saved_lines_changed = (() => { const a = ORIG.split('\\n'), b = saved.split('\\n'); let c = 0; for (let i = 0; i < Math.max(a.length,b.length); i++) if (a[i] !== b[i]) c++; return c; })();
    // find
    mdApp.find('item', false, 1);
    out.find_total = window.__lastFind && window.__lastFind.total;
    // GitHub extras
    mdApp.setMode('preview');
    await new Promise(r => setTimeout(r, 300));
    const html = document.getElementById('preview').innerHTML;
    out.alert = html.includes('markdown-alert-note');
    out.footnote = /footnote/.test(html);
    out.emoji = html.includes('\\u{1F680}');
    out.katex = html.includes('class="katex');
    // sanitising: nothing in a document may run script or reach the host
    window.__xss = 0;
    const EVIL = '<img src=x onerror="window.__xss=1">\\n\\n<script>window.__xss=2<\\/script>\\n\\n' +
      '[x](javascript:window.__xss=3)\\n\\n<svg><script>window.__xss=4<\\/script></svg>\\n\\n<a href="#" onclick="window.__xss=5">y</a>\\n';
    mdApp.load(EVIL, null); mdApp.setMode('preview');
    await new Promise(r => setTimeout(r, 600));
    out.xss_blocked = window.__xss === 0;
    out.xss_markup_gone = !/onerror|<script|javascript:|onclick/i.test(document.getElementById('preview').innerHTML);
    // a change on disk keeps the reader's place and shows the new text
    const LONG = Array.from({ length: 400 }, (_, i) => 'Line ' + i).join('\\n\\n');
    mdApp.load(LONG, null); mdApp.setMode('preview');
    await new Promise(r => setTimeout(r, 400));
    const host = document.getElementById('previewHost'); host.scrollTop = 3000;
    await mdApp.reload(LONG + '\\n\\nAdded at the end');
    await new Promise(r => setTimeout(r, 400));
    out.reload_scroll = host.scrollTop;
    out.reload_keeps_place = Math.abs(host.scrollTop - 3000) < 5;
    out.reload_new_text = document.getElementById('preview').textContent.includes('Added at the end');
    // typing right after a reload still marks the document modified (the next reload must ask)
    mdApp.setMode('visual');
    window.__dirty = 0;
    const realHost = window.chrome.webview.postMessage;
    window.chrome.webview.postMessage = (m) => { if (m && m.type === 'dirty') window.__dirty++; realHost(m); };
    await mdApp.reload('# Fresh\\n\\ntext\\n');
    mdApp.focus(); mdApp.insertText('typed ');
    await new Promise(r => setTimeout(r, 200));
    out.typing_after_reload_dirty = window.__dirty > 0;
    window.__dirty = 0;
    await mdApp.reload('# Again\\n');
    await new Promise(r => setTimeout(r, 200));
    out.reload_itself_not_dirty = window.__dirty === 0;
    window.chrome.webview.postMessage = realHost;
  } catch (e) { out.err = String(e && e.stack || e); }
  document.getElementById('out').textContent = JSON.stringify(out);
})();
function toastView() {
  // the editor instance is private to app.js; reach the ProseMirror view through the DOM
  return mdApp._view();

}
</script>
""" % {"orig": json.dumps(original)}

# capture 'find' messages: chrome.webview is absent, so hook post via a fake host
fake = "<script>window.chrome={webview:{postMessage:(m)=>{if(m&&m.type==='find')window.__lastFind=m;}}};</script>"
print(page(fake.replace("</script>", "</script>") + js) if False else "")
html_inject = fake + js
res = page(js)
print(res)
