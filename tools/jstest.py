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
                        "--user-data-dir=" + str(pathlib.Path(tempfile.gettempdir()) / "gm_jstest_profile"), "--allow-file-access-from-files", "--window-size=1600,900", "--virtual-time-budget=15000", "--dump-dom", f.as_uri()],
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
    // dark visual view: lists and table headers as light as the rest (1.8.0 left them almost black)
    mdApp.setTheme('dark'); mdApp.load(ORIG, null); mdApp.setMode('visual');
    await new Promise(r => setTimeout(r, 300));
    const root = document.querySelector('.toastui-editor-ww-container .toastui-editor-contents');
    const dim = [];
    for (const t of ['p', 'li', 'th', 'td', 'h1']) {
      const el = root && root.querySelector(t);
      if (!el) { dim.push(t + ' missing'); continue; }
      const c = getComputedStyle(el).color.match(/[0-9.]+/g).map(Number);
      if ((0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]) / 255 < 0.6) dim.push(t + ' ' + getComputedStyle(el).color);
    }
    out.dark_text_light = dim.length === 0;
    if (dim.length) out.dark_dim = dim.join(', ');
    // a table with an empty <tbody> (pandoc writes one for every one-row table) opens in every view;
    // 1.8.1 threw inside TOAST UI and showed a blank page
    const EMPTY = '# Doc\\n\\n<table>\\n<colgroup>\\n<col style="width: 100%%" />\\n</colgroup>\\n<thead>\\n<tr>\\n<th>solo</th>\\n</tr>\\n</thead>\\n<tbody>\\n</tbody>\\n</table>\\n\\nafter\\n';
    mdApp.setTheme('light');
    const seen = {};
    for (const mode of ['preview', 'visual', 'source']) {
      mdApp.setMode(mode); mdApp.load(EMPTY, null);
      await new Promise(r => setTimeout(r, 300));
      const sel = { preview: '#preview', visual: '.toastui-editor-ww-container .ProseMirror', source: '.toastui-editor-md-container .ProseMirror' }[mode];
      const el = document.querySelector(sel);
      seen[mode] = !!(el && el.textContent.includes('solo')) && !mdApp.editorBroken() && mdApp.getMarkdownPreserving() === EMPTY;
    }
    out.empty_tbody_all_views = seen.preview && seen.visual && seen.source;
    if (!out.empty_tbody_all_views) out.empty_tbody_seen = JSON.stringify(seen);
    // if the editor library throws on a text, the GitHub view still shows it, the editor views say why,
    // and saving gives back the text read from disk (never an empty document)
    const RealEditor = window.ToastEditor;
    window.ToastEditor = function () { throw new TypeError('forced editor failure'); };
    mdApp.setMode('preview'); mdApp.load('# Kept\\n\\nbody text\\n', null);
    await new Promise(r => setTimeout(r, 300));
    out.broken_editor_preview_shows = document.getElementById('preview').textContent.includes('body text');
    out.broken_editor_save_keeps_file = mdApp.getMarkdownPreserving() === '# Kept\\n\\nbody text\\n';
    mdApp.setMode('visual');
    const note = document.getElementById('editorError');
    out.broken_editor_notice = !!note && getComputedStyle(note).display !== 'none' && note.textContent.length > 20;
    window.ToastEditor = RealEditor;
    mdApp.load('# Fine again\\n', null);
    out.editor_recovers = !mdApp.editorBroken() && getComputedStyle(note).display === 'none';
    // Convert to TXT: the text as it reads in the GitHub view, without the Markdown marks
    out.plain_text = mdApp.plainText('# Title\\n\\n- one\\n- **two**\\n\\n| a | b |\\n|---|---|\\n| 1 | 2 |\\n')
      === 'Title\\n=====\\n\\n\u2022 one\\n\u2022 two\\n\\na  b\\n-  -\\n1  2\\n';
    // a save takes the text and marks it saved in one step (takeForSave): an edit typed after it is
    // not in that text, keeps the document modified (a higher seq) and is in the next save
    mdApp.setMode('visual'); mdApp.load('# Save\\n\\none\\n', null);
    await new Promise(r => setTimeout(r, 200));
    let lastSeq = 0;
    const saveHost = window.chrome.webview.postMessage;
    window.chrome.webview.postMessage = (m) => { if (m && m.type === 'dirty') lastSeq = m.seq; saveHost(m); };
    mdApp.focus(); mdApp.insertText('first ');
    await new Promise(r => setTimeout(r, 100));
    const s1 = mdApp.takeForSave();
    mdApp.insertText('second ');
    await new Promise(r => setTimeout(r, 100));
    const s2 = mdApp.takeForSave();
    window.chrome.webview.postMessage = saveHost;
    out.save_seq_marks_later_edit = s1.seq > 0 && lastSeq > s1.seq && s2.seq === lastSeq;
    out.save_keeps_later_edit = s1.text.includes('first') && !s1.text.includes('second') && s2.text.includes('first second');
    out.save_ends_with_newline = s1.text.endsWith('\\n') && s2.text.endsWith('\\n');
    // the visual view scrolls in an element as wide as the window, so its scrollbar is at the window's
    // edge; the text keeps GitHub's 980 px column (1.9.0 put the scrollbar at the column's edge)
    mdApp.load(LONG, null);
    await new Promise(r => setTimeout(r, 300));
    const sc = document.querySelector('.toastui-editor-ww-container .ProseMirror');
    const colW = (el) => { const cs = getComputedStyle(el); return el.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight); };
    const wide = { right: sc.getBoundingClientRect().right, iw: innerWidth, scrolls: sc.scrollHeight > sc.clientHeight, col: colW(sc), bar: sc.offsetWidth - sc.clientWidth };
    document.body.style.width = '700px';
    await new Promise(r => setTimeout(r, 200));
    const narrow = { right: sc.getBoundingClientRect().right, col: colW(sc), pad: parseFloat(getComputedStyle(sc).paddingLeft) };
    document.body.style.width = '';
    out.scrollbar_at_window_edge = innerWidth > 1100 && wide.scrolls && Math.abs(wide.right - wide.iw) < 1 && Math.abs(wide.col + wide.bar - 890) < 2;
    out.narrow_column_padding = Math.abs(narrow.right - 700) < 1 && narrow.pad === 45;
    if (!out.scrollbar_at_window_edge || !out.narrow_column_padding) out.scroll_seen = JSON.stringify({ wide, narrow });
    // [[name]] links: the page is shared with the macOS app, which turns them into links; this app does
    // not resolve them yet (init without wiki), so they stay text. Editing a paragraph that has one keeps
    // it as written (1.10.0 saved it as \\[\\[note\\-a\\]\\]).
    mdApp.load('# W\\n\\nSee [[note-a]] · [[Note-A.md|same]] here.\\n\\nOther line.\\n', null); mdApp.setMode('preview');
    await new Promise(r => setTimeout(r, 300));
    out.wiki_off_stays_text = !document.querySelector('#preview a.wikilink') && document.getElementById('preview').textContent.includes('[[note-a]]');
    mdApp.setMode('visual'); await new Promise(r => setTimeout(r, 200));
    const wv = toastView(); let wpos = null;
    wv.state.doc.descendants((n, pos) => { if (n.isTextblock && n.textContent.startsWith('See ')) wpos = pos; return wpos === null; });
    wv.dispatch(wv.state.tr.insertText('Now ', wpos + 1));
    const wsaved = mdApp.getMarkdownPreserving();
    out.wiki_edit_keeps_brackets = wsaved.includes('Now See [[note-a]] · [[Note-A.md|same]] here') && !wsaved.includes('\\\\[');
    if (!out.wiki_edit_keeps_brackets) out.wiki_saved = wsaved;
    // an edited line keeps the rest of itself as in the file: the editor rewrote the whole line (1.11.0 and
    // earlier saved \\| and \\. in a line with a pipe, *x* for _x_, \\~ for ~, and dropped <kbd> tags)
    const ML = 'Start | pipe _under_ and <kbd>K</kbd> mid ~t~ end.';
    async function mfresh() {
      mdApp.load('# T\\n\\n' + ML + '\\n\\nOther.\\n', null); mdApp.setMode('visual'); await new Promise(r => setTimeout(r, 80));
      const v = toastView(); let pos = null;
      v.state.doc.descendants((n, q) => { if (n.isTextblock && n.textContent.startsWith('Start')) pos = q; return pos === null; });
      return [v, pos + 1, v.state.doc.nodeAt(pos).textContent];
    }
    const mline = () => mdApp.getMarkdownPreserving().split('\\n')[2];
    let [mv, mb, mt] = await mfresh();
    mv.dispatch(mv.state.tr.insertText('NEW ', mb + mt.indexOf('mid')));
    out.merge_insert_keeps_line = mline() === 'Start | pipe _under_ and <kbd>K</kbd> NEW mid ~t~ end.';
    [mv, mb, mt] = await mfresh();
    mv.dispatch(mv.state.tr.delete(mb + mt.indexOf('mid'), mb + mt.indexOf('mid') + 4));
    out.merge_delete_keeps_line = mline() === 'Start | pipe _under_ and <kbd>K</kbd> ~t~ end.';
    [mv, mb, mt] = await mfresh();
    mv.dispatch(mv.state.tr.insertText('X', mb + mt.indexOf('under') + 2));
    out.merge_inside_emphasis = mline() === 'Start | pipe _unXder_ and <kbd>K</kbd> mid ~t~ end.';
    [mv, mb, mt] = await mfresh();
    mv.dispatch(mv.state.tr.setSelection(mv.state.selection.constructor.create(mv.state.doc, mb + mt.indexOf('pipe'), mb + mt.indexOf('pipe') + 4)));
    mdApp.exec('bold');
    out.merge_bold_applied = mline() === 'Start | **pipe** _under_ and <kbd>K</kbd> mid ~t~ end.';
    if (!out.merge_bold_applied) out.merge_bold_line = mline();
    // a reload marks in green what another program changed: one paragraph changed and one added are two
    // marks in each view; a reload with the same text marks nothing
    const R1 = '# Doc\\n\\nFirst para.\\n\\nSecond para.\\n\\nThird para.\\n';
    const R2 = '# Doc\\n\\nFirst para.\\n\\nSecond para, rewritten by the AI.\\n\\nThird para.\\n\\nA new one.\\n';
    const marks = {};
    for (const mode of ['preview', 'visual', 'source']) {
      mdApp.setMode(mode); mdApp.load(R1, null); await new Promise(r => setTimeout(r, 300));
      await mdApp.reload(R2); const n = mdApp.changesMarked(), on = CSS.highlights.has('gm-changed');
      await mdApp.reload(R2); marks[mode] = [n, on, mdApp.changesMarked()];
    }
    out.reload_marks_changes = ['preview', 'visual'].every(m => marks[m][0] === 2 && marks[m][1] && marks[m][2] === 0)
      && marks.source[0] >= 2 && marks.source[1] && marks.source[2] === 0;
    if (!out.reload_marks_changes) out.marks_seen = JSON.stringify(marks);
    // one point added to a list marks that point, not the whole list
    mdApp.setMode('visual'); mdApp.load('# L\\n\\n- one\\n- two\\n', null); await new Promise(r => setTimeout(r, 200));
    await mdApp.reload('# L\\n\\n- one\\n- two\\n- three\\n');
    out.reload_marks_list_item = mdApp.changesMarked() === 1;
    mdApp.setMode('visual');
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
