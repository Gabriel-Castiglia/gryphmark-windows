/* Gryphmark — editor page. Talks to the WPF host through chrome.webview.
   Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
   Date: November 10, 2026
   SPDX-License-Identifier: GPL-2.0-only */
(function () {
  'use strict';
  const host = window.chrome && window.chrome.webview;
  const post = (msg) => host && host.postMessage(msg);

  let editor = null;
  let state = { lang: 'en', theme: 'light', mode: 'visual', spell: true, locked: false };
  let suppressDirty = false;
  // While suppressDirty is on (the editor is being filled by the program), only the change that
  // filling makes is ignored: quietText is the text it leaves (null while it is being written).
  // Anything the user types in that moment differs from it and still marks the document
  // modified, so a reload never hides an edit.
  let quietText = null;
  let original = '';        // text exactly as read from disk
  let baseline = '';        // the same text after the editor's normalisation (captured at load)

  // ------------------------------------------------------------------ GitHub renderer
  const md = new Marked({ gfm: true, breaks: false });
  md.use(markedAlert());
  md.use(markedFootnote());
  md.use(markedKatex({ throwOnError: false, nonStandard: true }));
  md.use({
    extensions: [{
      name: 'emoji', level: 'inline',
      start(src) { const i = src.indexOf(':'); return i < 0 ? undefined : i; },
      tokenizer(src) {
        const m = /^:([a-z0-9_+\-]+):/.exec(src);
        if (m && nameToEmoji[m[1]]) return { type: 'emoji', raw: m[0], ch: nameToEmoji[m[1]] };
      },
      renderer(t) { return t.ch; },
    }],
  });
  const slugCount = {};
  md.use({
    renderer: {
      code({ text, lang }) {
        const l = (lang || '').split(/\s+/)[0];
        if (l === 'mermaid') return '<pre class="mermaid">' + escapeHtml(text) + '</pre>';
        let html;
        try { html = l && hljs.getLanguage(l) ? hljs.highlight(text, { language: l }).value : hljs.highlightAuto(text).value; }
        catch (e) { html = escapeHtml(text); }
        return '<pre><code class="hljs' + (l ? ' language-' + escapeHtml(l) : '') + '">' + html + '</code></pre>';
      },
      heading({ tokens, depth }) {
        const inner = this.parser.parseInline(tokens);
        const plain = inner.replace(/<[^>]+>/g, '').toLowerCase().trim().replace(/[^\p{L}\p{N}\s-]/gu, '').replace(/\s/g, '-');
        let slug = plain; const n = slugCount[plain] || 0; if (n) slug += '-' + n; slugCount[plain] = n + 1;
        return '<h' + depth + ' id="' + slug + '">' + inner + '</h' + depth + '>';
      },
    },
  });

  function escapeHtml(s) { return String(s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c])); }

  // marked passes raw HTML through, and this page can talk to the host: everything a document
  // renders is sanitised first, as GitHub does (no scripts, event handlers or javascript: links).
  const PURIFY = { USE_PROFILES: { html: true, svg: true, mathMl: true }, FORBID_TAGS: ['style', 'form'], FORBID_ATTR: ['srcset'] };

  function renderGithub(text) {
    for (const k in slugCount) delete slugCount[k];
    let html = DOMPurify.sanitize(md.parse(text || ''), PURIFY);
    html = html.replace(/<input (checked="" )?disabled="" type="checkbox">/g, (m, c) => '<input type="checkbox" disabled ' + (c ? 'checked ' : '') + 'class="task-list-item-checkbox">');
    // GitHub's classes on task items and their lists: no bullet under the box (styled in app.css)
    const tmp = document.createElement('div');
    tmp.innerHTML = html;
    tmp.querySelectorAll('li > input.task-list-item-checkbox:first-child, li > p:first-child > input.task-list-item-checkbox:first-child').forEach((box) => {
      const li = box.closest('li');
      li.classList.add('task-list-item');
      if (box.checked) li.classList.add('checked');
      li.parentElement.classList.add('contains-task-list');
    });
    return tmp.innerHTML;
  }

  let mermaidLoading = null;
  function loadMermaid() {
    if (window.mermaid) return Promise.resolve();
    if (!mermaidLoading) mermaidLoading = new Promise((ok, fail) => {
      const s = document.createElement('script'); s.src = 'https://app.local/lib/mermaid.min.js';
      s.onload = ok; s.onerror = fail; document.head.appendChild(s);
    });
    return mermaidLoading;
  }
  async function runMermaid(root) {
    const nodes = root.querySelectorAll('pre.mermaid');
    if (!nodes.length) return;
    await loadMermaid();
    mermaid.initialize({ startOnLoad: false, theme: state.theme === 'dark' ? 'dark' : 'default', securityLevel: 'strict' });
    try { await mermaid.run({ nodes }); } catch (e) { /* invalid diagram: mermaid leaves its error text */ }
  }

  async function showPreview() {
    const el = document.getElementById('preview');
    el.innerHTML = renderGithub(preserveFormatting(editor.getMarkdown()));
    await runMermaid(el);
  }

  // ------------------------------------------------------------------ editor
  function markGithubClass() {
    document.querySelectorAll('.toastui-editor-contents').forEach((e) => e.classList.add('markdown-body'));
    applySpell();
  }

  function build(markdown) {
    const el = document.getElementById('editorHost');
    if (editor) { editor.destroy(); editor = null; }
    el.innerHTML = '';
    suppressDirty = true;
    quietText = null;
    editor = new ToastEditor({
      el,
      height: '100%',
      initialEditType: state.mode === 'source' ? 'markdown' : 'wysiwyg',
      previewStyle: 'vertical',
      initialValue: markdown || '',
      language: state.lang === 'es' ? 'es-ES' : 'en-US',
      theme: state.theme === 'dark' ? 'dark' : 'light',
      usageStatistics: false,
      hideModeSwitch: true,
      autofocus: false,
      plugins: [codeSyntaxHighlight],
      toolbarItems: [],   // the toolbar is native (WPF)
      events: {
        change: () => {
          if (!suppressDirty || (quietText !== null && editor.getMarkdown() !== quietText)) post({ type: 'dirty', words: countWords() });
          scheduleOutline(); if (find.query) scheduleFind();
        },
      },
    });
    markGithubClass();
    applyLock();
    quietText = editor.getMarkdown();
    setTimeout(() => { suppressDirty = false; }, 50);
  }

  // ------------------------------------------------------------------ read-only ("Read only" switch)
  // Both ProseMirror views (visual and Markdown) stop accepting edits; selecting and copying still work.
  function applyLock() {
    document.body.classList.toggle('locked', state.locked);
    if (!editor) return;
    [editor.wwEditor, editor.mdEditor].forEach((m) => {
      if (m && m.view) m.view.setProps({ editable: () => !state.locked });
    });
  }
  // Task check boxes toggle on mousedown outside ProseMirror's editable check: stop them while locked.
  ['mousedown', 'click'].forEach((t) => document.addEventListener(t, (e) => {
    if (!state.locked) return;
    const li = e.target.closest && e.target.closest('#editorHost li.task-list-item');
    if (li && e.target === li) { e.preventDefault(); e.stopPropagation(); }
  }, true));
  let hintAt = 0;
  document.addEventListener('keydown', (e) => {
    if (!state.locked || state.mode === 'preview' || e.ctrlKey || e.altKey || e.metaKey) return;
    if (!e.target.closest || !e.target.closest('#editorHost')) return;
    if (e.key.length === 1 || e.key === 'Enter' || e.key === 'Backspace' || e.key === 'Delete' || e.key === 'Tab') {
      e.preventDefault();
      if (Date.now() - hintAt > 1500) { hintAt = Date.now(); post({ type: 'lockedHint' }); }
    }
  }, true);
  document.addEventListener('paste', (e) => {
    if (state.locked && e.target.closest && e.target.closest('#editorHost')) { e.preventDefault(); post({ type: 'lockedHint' }); }
  }, true);

  function countWords() {
    const t = editor ? editor.getMarkdown().replace(/[#>*_`~\-\[\]()|]/g, ' ') : '';
    const m = t.match(/[\p{L}\p{N}]+/gu); return m ? m.length : 0;
  }

  function applyTheme() {
    document.body.classList.toggle('dark', state.theme === 'dark');
    document.body.classList.toggle('light', state.theme !== 'dark');
    document.getElementById('ghcss').href = 'https://app.local/lib/github-markdown-' + (state.theme === 'dark' ? 'dark' : 'light') + '.css';
    document.getElementById('hlcss').href = 'https://app.local/lib/hljs-' + (state.theme === 'dark' ? 'dark' : 'light') + '.css';
  }

  function applySpell() {
    document.documentElement.lang = state.lang;
    document.querySelectorAll('.ProseMirror').forEach((e) => e.setAttribute('spellcheck', state.spell ? 'true' : 'false'));
  }

  async function applyMode() {
    document.body.classList.toggle('mode-preview', state.mode === 'preview');
    if (state.mode === 'preview') {
      await showPreview();
      document.getElementById('previewHost').scrollTop = 0;
    } else {
      editor.changeMode(state.mode === 'source' ? 'markdown' : 'wysiwyg', true);
      markGithubClass(); applyLock();
    }
    if (find.query) runFind(0);
  }

  // ------------------------------------------------------------------ keep the original formatting
  // The visual editor rewrites Markdown in its own style. On save, every block the user did not
  // touch is written back exactly as it was on disk; only edited blocks take the new text.
  function preserveFormatting(current) {
    if (current === baseline) return original;
    const O = original.split('\n'), N = baseline.split('\n'), C = current.split('\n');
    // 1) align original <-> baseline into segments
    const seg = [];            // { o: [...], n: [...] }
    const segOfN = new Array(N.length);
    const zeroBefore = {};     // n-index -> segments with no baseline lines, placed before it
    let ni = 0;
    const d1 = diffArrays(O, N);
    for (let i = 0; i < d1.length; i++) {
      const p = d1[i];
      if (!p.added && !p.removed) {
        for (const line of p.value) { seg.push({ o: [line], n: [ni] }); segOfN[ni++] = seg.length - 1; }
      } else {
        const removed = p.removed ? p.value : [];
        let added = [];
        if (p.removed && d1[i + 1] && d1[i + 1].added) { added = d1[i + 1].value; i++; }
        else if (p.added) added = p.value;
        const s = { o: removed, n: [] };
        for (let k = 0; k < added.length; k++) { s.n.push(ni); segOfN[ni++] = seg.length; }
        seg.push(s);
        if (!s.n.length) (zeroBefore[ni] = zeroBefore[ni] || []).push(s);
      }
    }
    // 2) which baseline lines survive unchanged in current
    const kept = new Array(N.length).fill(false);
    const out = [];
    const d2 = diffArrays(N, C);
    let n = 0;
    const plan = [];           // sequence of { kind: 'n', idx } | { kind: 'c', line }
    for (const p of d2) {
      if (p.added) { for (const line of p.value) plan.push({ kind: 'c', line }); }
      else if (p.removed) { n += p.value.length; }
      else { for (let k = 0; k < p.value.length; k++) { kept[n] = true; plan.push({ kind: 'n', idx: n++ }); } }
    }
    const segIntact = (s) => s.n.every((i) => kept[i]);
    const emitted = new Set();
    let prevKept = true;
    for (const step of plan) {
      if (step.kind === 'c') { out.push(step.line); prevKept = false; continue; }
      const i = step.idx;
      if (prevKept && zeroBefore[i]) for (const z of zeroBefore[i]) out.push(...z.o);
      const s = seg[segOfN[i]];
      if (segIntact(s)) { if (!emitted.has(s)) { out.push(...s.o); emitted.add(s); } }
      else out.push(N[i]);
      prevKept = true;
    }
    if (prevKept && zeroBefore[N.length]) for (const z of zeroBefore[N.length]) out.push(...z.o);
    return out.join('\n');
  }

  // ------------------------------------------------------------------ find & replace
  const find = { query: '', cs: false, matches: [], cur: -1 };
  let findTimer = null;
  function scheduleFind() { clearTimeout(findTimer); findTimer = setTimeout(() => runFind(0, true), 150); }

  function pmView() { try { const m = editor.getCurrentModeEditor(); return m && m.view; } catch (e) { return null; } }

  function collectPm(view, q, cs) {
    const res = []; const needle = cs ? q : q.toLowerCase();
    view.state.doc.descendants((node, pos) => {
      if (!node.isTextblock) return true;
      let text = '', map = [];
      node.forEach((child, off) => {
        if (child.isText) { for (let k = 0; k < child.text.length; k++) map.push(pos + 1 + off + k); text += child.text; }
        else { text += '￼'; map.push(pos + 1 + off); }
      });
      const hay = cs ? text : text.toLowerCase();
      let i = hay.indexOf(needle);
      while (i >= 0 && needle) { res.push({ from: map[i], to: map[i + needle.length - 1] + 1 }); i = hay.indexOf(needle, i + needle.length); }
      return false;
    });
    return res;
  }

  function collectDom(root, q, cs) {
    const res = []; const needle = cs ? q : q.toLowerCase();
    const tw = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    let node;
    while ((node = tw.nextNode())) {
      const hay = cs ? node.data : node.data.toLowerCase();
      let i = hay.indexOf(needle);
      while (i >= 0 && needle) { res.push({ node, start: i, end: i + needle.length }); i = hay.indexOf(needle, i + needle.length); }
    }
    return res;
  }

  function toRange(m) {
    const r = document.createRange();
    if (m.node) { r.setStart(m.node, m.start); r.setEnd(m.node, m.end); return r; }
    const view = pmView();
    const a = view.domAtPos(m.from), b = view.domAtPos(m.to);
    r.setStart(a.node, a.offset); r.setEnd(b.node, b.offset); return r;
  }

  function paint() {
    if (!window.CSS || !CSS.highlights) return;
    CSS.highlights.clear();
    const all = [], cur = [];
    find.matches.forEach((m, i) => { try { (i === find.cur ? cur : all).push(toRange(m)); } catch (e) { } });
    CSS.highlights.set('gm-find', new Highlight(...all));
    CSS.highlights.set('gm-cur', new Highlight(...cur));
    if (find.cur >= 0 && cur[0]) {
      const el = cur[0].startContainer.nodeType === 1 ? cur[0].startContainer : cur[0].startContainer.parentElement;
      if (el) el.scrollIntoView({ block: 'center', behavior: 'smooth' });
    }
  }

  function runFind(step, keepPosition) {
    if (!find.query) { find.matches = []; find.cur = -1; paint(); return report(); }
    if (state.mode === 'preview') find.matches = collectDom(document.getElementById('preview'), find.query, find.cs);
    else { const v = pmView(); find.matches = v ? collectPm(v, find.query, find.cs) : []; }
    const n = find.matches.length;
    if (!n) find.cur = -1;
    else if (keepPosition && find.cur >= 0) find.cur = Math.min(find.cur, n - 1);
    else find.cur = find.cur < 0 ? 0 : ((find.cur + step) % n + n) % n;
    paint(); report();
  }

  function report() { post({ type: 'find', index: find.cur, total: find.matches.length }); }

  function replaceCurrent(repl) {
    if (state.mode === 'preview' || state.locked || find.cur < 0) return;
    const v = pmView(); const m = find.matches[find.cur];
    v.dispatch(v.state.tr.insertText(repl, m.from, m.to));
    runFind(0, true);
  }

  function replaceAll(repl) {
    if (state.mode === 'preview' || state.locked) return 0;
    const v = pmView(); const ms = collectPm(v, find.query, find.cs);
    let tr = v.state.tr;
    for (let i = ms.length - 1; i >= 0; i--) tr = tr.insertText(repl, ms[i].from, ms[i].to);
    if (ms.length) v.dispatch(tr);
    runFind(0);
    return ms.length;
  }

  // ------------------------------------------------------------------ outline
  let outlineTimer = null;
  function scheduleOutline() { clearTimeout(outlineTimer); outlineTimer = setTimeout(sendOutline, 250); }
  function headings() {
    const out = []; let fence = false;
    (editor ? editor.getMarkdown() : '').split('\n').forEach((line, i) => {
      if (/^\s*(```|~~~)/.test(line)) { fence = !fence; return; }
      if (fence) return;
      const m = /^(#{1,6})\s+(.+?)\s*#*\s*$/.exec(line);
      if (m) out.push({ level: m[1].length, text: m[2].replace(/[*_`~]/g, ''), line: i });
    });
    return out;
  }
  function sendOutline() { post({ type: 'outline', items: headings() }); }

  function scrollToHeading(i) {
    let el = null;
    if (state.mode === 'preview') el = document.querySelectorAll('#preview h1,#preview h2,#preview h3,#preview h4,#preview h5,#preview h6')[i];
    else if (state.mode === 'visual') { const v = pmView(); el = v && v.dom.querySelectorAll('h1,h2,h3,h4,h5,h6')[i]; }
    else {
      const v = pmView(); const h = headings()[i];
      if (v && h && h.line < v.state.doc.childCount) {
        let pos = 0; for (let k = 0; k < h.line; k++) pos += v.state.doc.child(k).nodeSize;
        el = v.nodeDOM(pos);
        try { v.dispatch(v.state.tr.setSelection(PMTextSelection.create(v.state.doc, pos + 1))); } catch (e) { }
      }
    }
    if (el && el.scrollIntoView) el.scrollIntoView({ block: 'start', behavior: 'smooth' });
  }

  // ------------------------------------------------------------------ API used by the host
  window.mdApp = {
    load(markdown, docBase) {
      document.getElementById('docbase').href = docBase || 'https://app.local/';
      original = (markdown || '').replace(/\r\n/g, '\n');
      build(original);
      baseline = editor.getMarkdown();
      applyMode(); sendOutline();
      return true;
    },
    // The same file changed on disk (another program, often an AI assistant, is editing it):
    // swap the text in place and keep the reader where they were in every view.
    async reload(markdown) {
      if (!editor) { this.load(markdown); return true; }
      const panes = ['#previewHost', '#editorHost .toastui-editor-md-preview', '#editorHost .ProseMirror'];
      const kept = panes.map((sel) => Array.from(document.querySelectorAll(sel)).map((el) => el.scrollTop));
      original = (markdown || '').replace(/\r\n/g, '\n');
      suppressDirty = true;
      quietText = null;
      editor.setMarkdown(original, false);
      baseline = editor.getMarkdown();
      quietText = baseline;
      setTimeout(() => { suppressDirty = false; }, 50);
      markGithubClass(); applyLock();
      if (state.mode === 'preview') await showPreview();
      const restore = () => panes.forEach((sel, i) => document.querySelectorAll(sel).forEach((el, j) => {
        if (kept[i][j] !== undefined) el.scrollTop = kept[i][j];
      }));
      restore(); requestAnimationFrame(() => { restore(); setTimeout(restore, 120); });
      sendOutline(); if (find.query) scheduleFind();
      return true;
    },
    getMarkdown() { return editor ? editor.getMarkdown() : ''; },
    getMarkdownPreserving() { return editor ? preserveFormatting(editor.getMarkdown()) : ''; },
    markSaved(text) { original = text.replace(/\r\n/g, '\n'); const cur = editor.getMarkdown(); baseline = cur; },
    setLang(lang) { const t = editor.getMarkdown(); state.lang = lang; build(t); applyMode(); },
    setTheme(theme) { const t = editor.getMarkdown(); state.theme = theme; applyTheme(); build(t); applyMode(); },
    setMode(mode) { state.mode = mode; applyMode(); },
    setSpell(on) { state.spell = !!on; applySpell(); },
    init(lang, theme, mode, spell, locked) { state = { lang, theme, mode, spell: spell !== false, locked: !!locked }; applyTheme(); build(''); applyMode(); },
    setLocked(on) { state.locked = !!on; applyLock(); },
    exec(cmd, payload) { if (state.mode === 'preview' || state.locked) return; try { editor.exec(cmd, payload); } catch (e) { } editor.focus(); },
    insertText(t) { if (!state.locked) editor.insertText(t); },
    words() { return countWords(); },
    selectedText() { try { return editor.getSelectedText() || ''; } catch (e) { return ''; } },
    focus() { if (editor && state.mode !== 'preview') editor.focus(); },
    find(q, cs, step) { const changed = q !== find.query || !!cs !== find.cs; find.query = q; find.cs = !!cs; if (changed) find.cur = -1; runFind(changed ? 0 : (step || 1)); },
    findClose() { find.query = ''; find.matches = []; find.cur = -1; paint(); },
    replace(q, cs, r) { find.query = q; find.cs = !!cs; replaceCurrent(r); },
    replaceAll(q, cs, r) { find.query = q; find.cs = !!cs; return replaceAll(r); },
    scrollToHeading(i) { scrollToHeading(i); },
    _view() { return pmView(); },   // for tools/jstest.py
    async exportHtml(title) {
      const tmp = document.createElement('div'); tmp.className = 'markdown-body'; tmp.style.cssText = 'position:absolute;left:-99999px;width:980px';
      document.body.appendChild(tmp);
      tmp.innerHTML = renderGithub(preserveFormatting(editor.getMarkdown()));
      await runMermaid(tmp);
      const body = tmp.innerHTML; tmp.remove();
      post({ type: 'export', title, body, theme: state.theme, math: /class="katex/.test(body) });
    },
    async preparePrint() { await showPreview(); post({ type: 'printReady' }); },
  };

  // ------------------------------------------------------------------ keyboard shortcuts while the page has focus
  document.addEventListener('keydown', (e) => {
    if (e.key === 'F1') { e.preventDefault(); post({ type: 'cmd', cmd: 'manual' }); return; }
    if (e.key === 'F3') { e.preventDefault(); post({ type: 'cmd', cmd: e.shiftKey ? 'findprev' : 'findnext' }); return; }
    if (e.key === 'Escape') { post({ type: 'cmd', cmd: 'findclose' }); return; }
    if (!e.ctrlKey || e.altKey) return;
    const k = e.key.toLowerCase();
    const map = { n: 'new', o: 'open', s: e.shiftKey ? 'saveas' : 'save', p: 'print', w: 'close', f: 'find', h: 'replace' };
    if (k === 'o' && e.shiftKey) { e.preventDefault(); post({ type: 'cmd', cmd: 'outline' }); return; }
    if (k === 'e' && e.shiftKey) { e.preventDefault(); post({ type: 'cmd', cmd: 'files' }); return; }
    if (k === 'l' && e.shiftKey) { e.preventDefault(); post({ type: 'cmd', cmd: 'readonly' }); return; }
    if (map[k]) { e.preventDefault(); post({ type: 'cmd', cmd: map[k] }); }
    else if (k === '1' || k === '2' || k === '3') { e.preventDefault(); post({ type: 'cmd', cmd: 'mode' + k }); }
    else if (k === '=' || k === '+' || k === '-' || k === '0') { e.preventDefault(); post({ type: 'cmd', cmd: k === '0' ? 'zoom0' : (k === '-' ? 'zoom-' : 'zoom+') }); }
  }, true);

  // ------------------------------------------------------------------ drag & drop a file onto the window
  window.addEventListener('dragover', (e) => { e.preventDefault(); }, true);
  window.addEventListener('drop', (e) => {
    if (state.locked && !(e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length)) { e.preventDefault(); e.stopPropagation(); return; }
    if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length) {
      e.preventDefault(); e.stopPropagation();
      if (host.postMessageWithAdditionalObjects) host.postMessageWithAdditionalObjects('drop', e.dataTransfer.files);
    }
  }, true);

  // ------------------------------------------------------------------ links in the GitHub view
  document.getElementById('preview').addEventListener('click', (e) => {
    const a = e.target.closest('a'); if (!a) return;
    const href = a.getAttribute('href') || '';
    e.preventDefault();
    if (href.startsWith('#')) { const t = document.getElementById(decodeURIComponent(href.slice(1))); if (t) t.scrollIntoView(); return; }
    post({ type: 'link', href: a.href });
  });

  post({ type: 'ready' });
})();
