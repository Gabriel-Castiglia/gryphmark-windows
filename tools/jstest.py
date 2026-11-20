"""Headless check of app.js logic: formatting preservation, find, GitHub extras. Prints the JSON result.
Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
Date: November 10, 2026
SPDX-License-Identifier: GPL-2.0-only
"""
import json, pathlib, subprocess, sys, tempfile
root = pathlib.Path(__file__).resolve().parents[1]
www = root / "src" / "Vigia" / "wwwroot"
import os
edge = next((p for p in (os.path.expandvars(x) for x in (
    r"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe", r"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe",
    r"%ProgramFiles%\Google\Chrome\Application\chrome.exe", r"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe",
    r"%ProgramFiles%\BraveSoftware\Brave-Browser\Application\brave.exe", r"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\Application\brave.exe",
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", "/Applications/Brave Browser.app/Contents/MacOS/Brave Browser"))
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

# a memory file with properties at the top: backslashes, a very long quoted value, nested keys, a list
fm_sample = (root / "tools" / "frontmatter_sample.md").read_text(encoding="utf-8")

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
    // the Search pane goes straight to the n-th match
    mdApp.findAt('item', false, 1);
    out.find_at = !!window.__lastFind && window.__lastFind.index === 1 && window.__lastFind.total === 2;
    mdApp.findClose();
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
    // a reader at the end follows what the AI appends; a reader in the middle stays where they were
    const FE = Array.from({ length: 200 }, (_, i) => 'Line ' + i).join('\\n\\n');
    mdApp.setMode('preview'); mdApp.load(FE, null); await new Promise(r => setTimeout(r, 300));
    const ph = document.getElementById('previewHost');
    ph.scrollTop = ph.scrollHeight;
    await mdApp.reload(FE + '\\n\\nAppended 1\\n\\nAppended 2\\n\\nAppended 3\\n'); await new Promise(r => setTimeout(r, 300));
    const endGap = ph.scrollHeight - ph.scrollTop - ph.clientHeight;
    ph.scrollTop = 1500;
    await mdApp.reload(FE + '\\n\\nAppended 1\\n\\nAppended 2\\n\\nAppended 3\\n\\nAppended 4\\n'); await new Promise(r => setTimeout(r, 300));
    out.follow_end = endGap < 2 && Math.abs(ph.scrollTop - 1500) < 5;
    if (!out.follow_end) out.follow_seen = JSON.stringify({ endGap, mid: ph.scrollTop });
    // a paste inside a code block goes in as plain text: a clipboard with HTML alone (block markup, no
    // text/plain) split the block at the cursor and made the rest one paragraph (1.13.0)
    const CB = '# P\\n\\n```text\\n[ ] WEB\\n    \\n    [ ] - next\\n\\n[ ] Other\\n```\\n';
    const pasted = {};
    for (const [name, data] of Object.entries({
      html_only: { 'text/html': '<div>[ ] - </div><div><br></div>' },
      html_and_text: { 'text/plain': '[ ] - ', 'text/html': '<div>[ ] - </div><div><br></div>' },
      text_only: { 'text/plain': '[ ] - ' } })) {
      mdApp.setMode('visual'); mdApp.load(CB, null); await new Promise(r => setTimeout(r, 150));
      const v = mdApp._view(); let at = null;
      v.state.doc.descendants((n, p) => { if (at === null && n.type.name === 'codeBlock') at = p + 1 + n.textContent.indexOf('    \\n') + 4; return at === null; });
      v.dispatch(v.state.tr.setSelection(v.state.selection.constructor.create(v.state.doc, at))); v.focus();
      const dt = new DataTransfer(); for (const [k, val] of Object.entries(data)) dt.setData(k, val);
      v.dom.dispatchEvent(new ClipboardEvent('paste', { clipboardData: dt, bubbles: true, cancelable: true }));
      await new Promise(r => setTimeout(r, 100));
      const top = []; v.state.doc.forEach(n => top.push(n.type.name));
      pasted[name] = top.join(',') === 'heading,codeBlock'
        && mdApp.getMarkdownPreserving() === CB.replace('    \\n    [ ]', '    [ ] - \\n    [ ]');
    }
    out.paste_in_code_block_plain = Object.values(pasted).every(Boolean);
    if (!out.paste_in_code_block_plain) out.paste_seen = JSON.stringify(pasted);
    // properties at the top (front matter): one folded block in the visual view, a table in the GitHub view,
    // out of the outline, and the file saved as it was (1.14.0 showed them as one huge bold heading)
    const FM = %(fm)s;
    const fmSeen = {};
    mdApp.setMode('visual'); mdApp.load(FM, null); await new Promise(r => setTimeout(r, 200));
    let fv = mdApp._view();
    fmSeen.first_block = fv.state.doc.firstChild.type.name === 'frontMatter';
    fmSeen.no_yaml_heading = !Array.from(fv.dom.querySelectorAll('h1,h2,h3,h4,h5,h6')).some(h => /index_line|originSessionId/.test(h.textContent));
    fmSeen.unchanged = mdApp.getMarkdownPreserving() === FM;
    const box = fv.dom.querySelector('[data-front-matter]');
    fmSeen.folded = !!box && getComputedStyle(box).maxHeight !== 'none' && box.clientHeight < 100;
    fv.dispatch(fv.state.tr.setSelection(fv.state.selection.constructor.create(fv.state.doc, 5)));
    await new Promise(r => setTimeout(r, 50));
    fmSeen.opens = document.body.classList.contains('gm-fm-open') && getComputedStyle(box).maxHeight === 'none';
    let bpos = null;
    fv.state.doc.descendants((n, p) => { if (bpos === null && n.isTextblock && n.textContent.startsWith('Notes on what')) bpos = p; return bpos === null; });
    fv.dispatch(fv.state.tr.setSelection(fv.state.selection.constructor.create(fv.state.doc, bpos + 1)));
    fv.dispatch(fv.state.tr.insertText('X '));
    await new Promise(r => setTimeout(r, 50));
    fmSeen.closes = !document.body.classList.contains('gm-fm-open');
    const fsaved = mdApp.getMarkdownPreserving();
    fmSeen.edit_keeps_rest = fsaved === FM.replace('Notes on what', 'X Notes on what');
    let outline = null;
    const oh = window.chrome.webview.postMessage;
    window.chrome.webview.postMessage = (m) => { if (m && m.type === 'outline') outline = m.items; oh(m); };
    mdApp.load(FM, null); await new Promise(r => setTimeout(r, 400));
    window.chrome.webview.postMessage = oh;
    fmSeen.outline = !!outline && outline[0].text === 'Spring garden' && outline.some(h => h.text.includes('IMG_0412'));
    fmSeen.outline_escaped = mdApp._headingText('foto IMG\\\\_1915 and **bold** _em_ `c` a\\\\*b') === 'foto IMG_1915 and bold em c a*b';
    mdApp.setMode('source'); await new Promise(r => setTimeout(r, 100));
    fmSeen.source_unchanged = mdApp.getMarkdownPreserving() === FM;
    mdApp.setMode('preview'); await new Promise(r => setTimeout(r, 300));
    const pv = document.getElementById('preview');
    const det = pv.querySelector('details.gm-frontmatter');
    fmSeen.preview_table = !!det && !det.open && /^Properties name, description, metadata/.test(det.querySelector('summary').textContent)
      && !!det.querySelector('th') && det.querySelector('th').textContent === 'name';
    fmSeen.preview_no_yaml_heading = !Array.from(pv.querySelectorAll('h1,h2,h3')).some(h => /index_line/.test(h.textContent))
      && pv.querySelector('h1').textContent === 'Spring garden';
    const tds = Array.from(det ? det.querySelectorAll('td') : []).map(t => t.textContent);
    fmSeen.preview_values = tds.some(t => t === 'active') && tds.some(t => t.includes('C:\\\\Garden\\\\Seeds\\\\2026') && t.includes('`\\\\\\\\nas\\\\share') && !t.includes('Garden\\\\\\\\'))
      && !!det.querySelector('td ul') && det.querySelector('td ul').textContent.includes('Spring/Summer');
    if (!fmSeen.preview_values) out.fm_tds = JSON.stringify(tds);
    // a --- further down is still a rule, and a --- at the top that never closes is text
    mdApp.load('# A\\n\\ntext\\n\\n---\\n\\nmore\\n', null); await new Promise(r => setTimeout(r, 200));
    fmSeen.later_rule = !!pv.querySelector('hr') && !pv.querySelector('details.gm-frontmatter');
    const OPEN = '---\\nname: x\\n\\nno end\\n';
    mdApp.load(OPEN, null); await new Promise(r => setTimeout(r, 200));
    fmSeen.unclosed = !pv.querySelector('details.gm-frontmatter') && mdApp.getMarkdownPreserving() === OPEN && !mdApp.editorBroken();
    // the two views agree on what is a properties block: it closes, and a --- block holds a key. An unclosed ---
    // line, a "..." close or a file that starts with a rule keep their text in sight (before 2.0.0 they hid the whole
    // document in the Visual view); a +++ block (TOML) is properties in both.
    for (const [k, t, want] of [['unclosed', '---\\nname: x\\n\\n# Title\\n\\nBody.\\n', false],
        ['dots', '---\\nname: x\\n...\\n\\n# T\\n', false], ['rule_first', '---\\n\\nIntro.\\n\\n---\\n\\n# T\\n', false],
        ['trailing_space', '--- \\nname: x\\n---\\n\\n# T\\n', false], ['toml', '+++\\ntitle = "x"\\n+++\\n\\n# T\\n', true],
        ['yaml', '---\\nname: x\\n---\\n\\n# T\\n', true]]) {
      mdApp.setMode('visual'); mdApp.load(t, null); await new Promise(r => setTimeout(r, 150));
      const isFm = mdApp._view().state.doc.firstChild.type.name === 'frontMatter';
      mdApp.setMode('preview'); await new Promise(r => setTimeout(r, 150));
      const gh = !!pv.querySelector('details.gm-frontmatter');
      fmSeen['agree_' + k] = isFm === want && gh === want && mdApp.getMarkdownPreserving() === t;
    }
    out.front_matter = Object.values(fmSeen).every(v => v === true);
    // View -> Text width: the column of the Visual and GitHub views follows it; 0 = the whole window
    const widths = {};
    mdApp.load(LONG, null);
    for (const px of [1280, 0, 980]) {
      mdApp.setWidth(px);
      mdApp.setMode('visual'); await new Promise(r => setTimeout(r, 150));
      const wsc = document.querySelector('.toastui-editor-ww-container .ProseMirror');
      const vis = colW(wsc) + wsc.offsetWidth - wsc.clientWidth;
      mdApp.setMode('preview'); await new Promise(r => setTimeout(r, 150));
      widths[px] = [Math.round(vis), Math.round(document.getElementById('preview').getBoundingClientRect().width)];
    }
    out.text_width = Math.abs(widths[1280][0] - 1190) < 2 && widths[1280][1] === 1280
      && Math.abs(widths[0][0] - (innerWidth - 90)) < 2 && widths[0][1] > 1500
      && Math.abs(widths[980][0] - 890) < 2 && widths[980][1] === 980;
    if (!out.text_width) out.text_width_seen = JSON.stringify(widths);
    if (!out.front_matter) out.front_matter_seen = JSON.stringify(fmSeen);
    // pictures: a local absolute path goes through the host, a path with spaces and ![[name|300]] are pictures,
    // alt|300 gives the width, a ![[name]] that does not load is asked to the host and the answer is used
    window.__gmMsgs = [];
    const imgPost = window.chrome.webview.postMessage;
    window.chrome.webview.postMessage = (m) => { window.__gmMsgs.push(m); imgPost(m); };
    const IMG = '![w](C:\\\\Pics\\\\a.png)\\n\\n![f](' + 'file:' + '///C:/x/b%%20c.png)\\n\\n![s](my pic.png)\\n\\n![z|300](d.png)\\n\\n![[e f.png|200x100]]\\n\\n`![[g.png]]`\\n';
    mdApp.setMode('preview'); await new Promise(r => setTimeout(r, 50));
    mdApp.load(IMG, 'https://doc.local/C%%3A/Docs/');   // read at once: the addresses fail to load here, and a failed one is boxed
    const im = Array.from(pv.querySelectorAll('img')).map(i => [i.getAttribute('alt'), i.getAttribute('src'), i.getAttribute('width') || '']);
    const want = JSON.stringify([['w', 'https://doc.local/C%%3A/Pics/a.png', ''], ['f', 'https://doc.local/C%%3A/x/b%%20c.png', ''], ['s', 'my%%20pic.png', ''], ['z', 'd.png', '300'], ['e f.png', 'e%%20f.png', '200']]);
    const embed = pv.querySelector('img[data-embed]');
    embed.dispatchEvent(new Event('error'));
    const asked = window.__gmMsgs.some(m => m && m.type === 'findfile' && m.name === 'e f.png');
    mdApp.fileFound('e f.png', 'https://doc.local/C%%3A/Docs/att/e%%20f.png');
    const found = embed.getAttribute('src') === 'https://doc.local/C%%3A/Docs/att/e%%20f.png';
    embed.dispatchEvent(new Event('error'));
    const boxed = !!pv.querySelector('.gm-img-missing') && !embed.isConnected && pv.querySelector('code').textContent === '![[g.png]]';
    window.chrome.webview.postMessage = imgPost;
    out.pictures = JSON.stringify(im) === want && asked && found && boxed;
    if (!out.pictures) out.pictures_seen = JSON.stringify({ im, asked, found, boxed });

    // the search in the folders counts matches in the file's text; a link address is not in the views: the line
    // of the file picks the match (here the 2nd of the file is the 1st that Visual and GitHub show)
    const HIDDEN = 'A [link](notes/item.md) here.\\n\\nFirst the item word.\\n\\nLast line about item and more.\\n';
    const hid = {};
    for (const m of ['visual', 'preview']) {
      mdApp.setMode(m); await new Promise(r => setTimeout(r, 50));
      mdApp.load(HIDDEN, null); await new Promise(r => setTimeout(r, 150));
      mdApp.findAt('item', false, 1, 'First the item word.', 3);
      hid[m] = window.__lastFind && [window.__lastFind.index, window.__lastFind.total];
      mdApp.findClose();
    }
    mdApp.setMode('source'); await new Promise(r => setTimeout(r, 50));
    mdApp.findAt('item', false, 1, 'First the item word.', 3);
    hid.markdown = window.__lastFind && [window.__lastFind.index, window.__lastFind.total];
    mdApp.findClose();
    out.find_at_hidden_markdown = JSON.stringify(hid) === JSON.stringify({ visual: [0, 2], preview: [0, 2], markdown: [1, 3] });
    if (!out.find_at_hidden_markdown) out.find_at_seen = JSON.stringify(hid);

    mdApp.setMode('visual');
  } catch (e) { out.err = String(e && e.stack || e); }
  document.getElementById('out').textContent = JSON.stringify(out);
})();
function toastView() {
  // the editor instance is private to app.js; reach the ProseMirror view through the DOM
  return mdApp._view();

}
</script>
""" % {"orig": json.dumps(original), "fm": json.dumps(fm_sample)}

# capture 'find' messages: chrome.webview is absent, so hook post via a fake host
fake = "<script>window.chrome={webview:{postMessage:(m)=>{if(m&&m.type==='find')window.__lastFind=m;}}};</script>"
print(page(fake.replace("</script>", "</script>") + js) if False else "")
html_inject = fake + js
res = page(js)
print(res)
