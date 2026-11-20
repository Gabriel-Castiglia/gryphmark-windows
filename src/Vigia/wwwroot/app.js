/* Vigia — editor page. Talks to the WPF host through chrome.webview.
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
  let lastMarked = 0;        // blocks marked by the last reload (markChanges), for the tests
  let editSeq = 0;           // counts the user's edits; a save says which edit it holds (takeForSave)
  let original = '';        // text exactly as read from disk
  let baseline = '';        // the same text after the editor's normalisation (captured at load)
  // The editor library (TOAST UI) can throw on a text it cannot parse (1.8.1 and 1.1.1 showed a blank page
  // on a table with an empty <tbody>). Then the page keeps working from the text read from disk: the GitHub
  // view shows it, the editor views say why they are empty, and saving writes the file back as it was.
  let editorBroken = false;
  function editorText() { return editor ? editor.getMarkdown() : original; }
  function currentText() { return editor ? preserveFormatting(editor.getMarkdown()) : original; }
  function editorFailed(e) {
    try { console.error('Vigia: the editor failed', e); } catch (x) { }
    try { if (editor) editor.destroy(); } catch (x) { }
    editor = null;
    editorBroken = true;
    document.getElementById('editorHost').innerHTML = '';
    let el = document.getElementById('editorError');
    if (!el) { el = document.createElement('div'); el.id = 'editorError'; document.body.insertBefore(el, document.getElementById('editorHost')); }
    el.textContent = state.lang === 'es'
      ? 'El editor no pudo abrir este archivo (falla de la biblioteca TOAST UI). La vista GitHub lo muestra completo. Esta vista no permite editarlo, y guardar deja el archivo como está en disco.'
      : 'The editor could not open this file (a failure of the TOAST UI library). The GitHub view shows all of it. This view cannot edit it, and saving leaves the file as it is on disk.';
    document.body.classList.add('editor-broken');
  }

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
    }, {
      // Pictures many editors write and CommonMark does not take: ![alt](a path with spaces.png), which wants
      // <...> around the path, and ![[picture.png]] or ![[picture.png|300]] (Obsidian), the picture of that name.
      name: 'gmImage', level: 'inline',
      start(src) { const i = src.indexOf('!['); return i < 0 ? undefined : i; },
      tokenizer(src) {
        let m = /^!\[([^\]\n]*)\]\(([^()<>\n"]*\s[^()<>\n"]*?\.(?:png|jpe?g|gif|webp|svg|bmp|avif|ico))\s*(?:"([^"\n]*)")?\)/i.exec(src);
        if (m && m[2].trim()) return { type: 'gmImage', raw: m[0], href: m[2].trim(), alt: m[1], title: m[3] || '' };
        m = /^!\[\[([^\[\]\n|#]+?\.(?:png|jpe?g|gif|webp|svg|bmp|avif|ico))(?:\|(\d+)(?:x(\d+))?)?\]\]/i.exec(src);
        if (m) return { type: 'gmImage', raw: m[0], href: m[1].trim(), alt: m[1].trim().split('/').pop(), title: '', embed: m[1].trim(), w: m[2], h: m[3] };
      },
      renderer(t) {
        const src = /^([A-Za-z]:[\\/]|\\\\|file:)/i.test(t.href) ? t.href : t.href.split('/').map(encodeURIComponent).join('/');
        return '<img src="' + escapeHtml(src) + '" alt="' + escapeHtml(t.alt) + '"' + (t.title ? ' title="' + escapeHtml(t.title) + '"' : '') +
          (t.w ? ' width="' + t.w + '"' : '') + (t.h ? ' height="' + t.h + '"' : '') + (t.embed ? ' data-embed="' + escapeHtml(t.embed) + '"' : '') + '>';
      },
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
    const fm = splitFrontMatter(text || '');
    let html = DOMPurify.sanitize((fm.yaml !== null ? frontMatterBlock(fm.yaml) : '') + md.parse(fm.body), PURIFY);
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
    tmp.querySelectorAll('img').forEach(sizeImage);
    return tmp.innerHTML;
  }

  // ------------------------------------------------------------------ pictures
  // An absolute path of this computer (C:\…, \\server\share\…, a file: address) is loaded through the host, as the
  // document's own files are: <origin of the page base>/<path>, the path with forward slashes and a UNC path as
  // UNC/server/share/…. A path that starts with / is left to the page base (on the Mac it is the disk's root).
  function localFileUrl(src) {
    let p = String(src || '').trim();
    if (/^[A-Za-z]:%5C/i.test(p) || /^%5C%5C/i.test(p)) { try { p = decodeURI(p).replace(/%5C/gi, '\\'); } catch (e) { return null; } }   // marked writes \ as %5C
    if (/^file:/i.test(p)) { try { p = decodeURIComponent(p.replace(/^file:(?:\/\/localhost)?\/*/i, '/')); } catch (e) { return null; } }
    else if (!/^([A-Za-z]:[\\/]|\\\\)/.test(p)) return null;
    p = p.replace(/\\/g, '/');
    if (/^\/[A-Za-z]:\//.test(p)) p = p.slice(1);
    if (p.startsWith('//')) p = 'UNC/' + p.slice(2);
    const base = (/^([a-z][\w+.-]*:\/\/[^/]+)\//i.exec(document.getElementById('docbase').href) || [])[1];
    if (!base || /\/\/app\.local$/i.test(base)) return null;
    return base + '/' + p.replace(/^\/+/, '').split('/').map(encodeURIComponent).join('/');
  }
  // The sanitiser drops a src such as C:\… or file:… (and the Mac's gm-doc: scheme): a picture's local path is
  // turned into the host's address first, and that address, made here, is kept.
  DOMPurify.addHook('uponSanitizeAttribute', (node, data) => {
    if (node.nodeName !== 'IMG' || data.attrName !== 'src') return;
    const local = localFileUrl(data.attrValue);
    if (local) { node.setAttribute('src', local); data.attrValue = local; data.forceKeepAttr = true; }   // forceKeepAttr keeps the node's own value
  });
  // "alt|300" or "alt|300x200" (as Obsidian writes it) gives the size.
  function sizeImage(img) {
    const m = /^(.*?)\|(\d+)(?:x(\d+))?$/.exec(img.getAttribute('alt') || '');
    if (m) { img.setAttribute('alt', m[1].trim()); img.setAttribute('width', m[2]); if (m[3]) img.setAttribute('height', m[3]); }
  }
  // A picture that does not load: a ![[name]] is looked for by the host in the open folders (fileFound); any
  // other, or one the host does not find, becomes a dashed box with its name instead of a broken icon.
  function watchImages(root) {
    root.querySelectorAll('img').forEach((img) => {
      const miss = () => {
        if (img.dataset.embed && !img.dataset.asked) { img.dataset.asked = 'wait'; post({ type: 'findfile', name: img.dataset.embed }); return; }
        if (img.dataset.asked !== 'wait') markMissing(img);   // while the host looks for it, its answer decides
      };
      img.addEventListener('error', miss);
      if (img.complete && img.getAttribute('src') && img.naturalWidth === 0) miss();
    });
  }
  function markMissing(img) {
    if (!img.isConnected) return;
    const s = document.createElement('span');
    let name = img.getAttribute('alt') || '';
    try { name = name || decodeURIComponent((img.getAttribute('src') || '').split('/').pop()); } catch (e) { }
    s.className = 'gm-img-missing'; s.textContent = name || '?';
    s.title = (state.lang === 'es' ? 'No se encuentra la imagen: ' : 'Picture not found: ') + (img.dataset.embed || img.getAttribute('src') || '');
    img.replaceWith(s);
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
    el.innerHTML = renderGithub(currentText());
    watchImages(el);
    await runMermaid(el);
    if (state.wiki) linkWiki(el);
  }

  // ------------------------------------------------------------------ [[name]] links (GitHub view only)
  // [[name]], [[name|text]] and [[name#heading]] become links to the Markdown file of that name in the
  // open folders; the host finds it, and says which names match no file (shown as missing). Only the
  // view is changed, never the text, and an exported page keeps them as they are written.
  const WIKI = /\[\[([^\[\]\n|#]+)(#[^\[\]\n|]*)?(?:\|([^\[\]\n]+))?\]\]/g;
  function linkWiki(root) {
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
      acceptNode: (n) => n.parentElement.closest('code, pre, a, kbd, .katex, .mermaid') ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT,
    });
    const hits = [];
    for (let n = walker.nextNode(); n; n = walker.nextNode()) if (n.nodeValue.includes('[[')) hits.push(n);
    hits.forEach((n) => {
      const text = n.nodeValue; const frag = document.createDocumentFragment(); let last = 0; let m;
      WIKI.lastIndex = 0;
      while ((m = WIKI.exec(text))) {
        if (m.index > last) frag.appendChild(document.createTextNode(text.slice(last, m.index)));
        const a = document.createElement('a');
        a.className = 'wikilink'; a.href = '#'; a.dataset.wiki = m[1].trim();
        a.textContent = (m[3] || (m[1] + (m[2] || ''))).trim();
        frag.appendChild(a); last = WIKI.lastIndex;
      }
      if (last === 0) return;
      if (last < text.length) frag.appendChild(document.createTextNode(text.slice(last)));
      n.parentNode.replaceChild(frag, n);
    });
    askWiki();
  }
  function askWiki() {
    const names = Array.from(new Set(Array.from(document.querySelectorAll('#preview a.wikilink')).map((a) => a.dataset.wiki)));
    if (names.length) post({ type: 'wikinames', names });
  }

  // ------------------------------------------------------------------ editor
  function markGithubClass() {
    document.querySelectorAll('.toastui-editor-contents').forEach((e) => e.classList.add('markdown-body'));
    applySpell();
  }

  function build(markdown) {
    const el = document.getElementById('editorHost');
    if (editor) { try { editor.destroy(); } catch (e) { } editor = null; }
    el.innerHTML = '';
    editorBroken = false;
    document.body.classList.remove('editor-broken');
    suppressDirty = true;
    quietText = null;
    frontMatterFor(markdown);
    try {
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
        frontMatter: true,  // a block of properties at the top (--- ... ---) stays one block, kept as written
        events: {
          change: () => {
            if (!suppressDirty || (quietText !== null && editor.getMarkdown() !== quietText)) post({ type: 'dirty', words: countWords(), seq: ++editSeq });
            scheduleOutline(); if (find.query) scheduleFind();
            foldFrontMatter();
          },
          caretChange: () => foldFrontMatter(),
        },
      });
      markGithubClass();
      applyLock();
      quietText = editor.getMarkdown();
    } catch (e) { editorFailed(e); }
    setTimeout(() => { suppressDirty = false; }, 50);
  }

  // ------------------------------------------------------------------ properties at the top (front matter)
  // In the visual view the block is shown folded (its first lines, in grey) and opens while the cursor is in
  // it. The class goes on the body, outside ProseMirror: a class on the block itself would be read back by the
  // editor as a change of the document.
  function foldFrontMatter() {
    let open = false;
    const v = state.mode === 'visual' ? pmView() : null;
    if (v) { const $f = v.state.selection.$from; open = $f.depth > 0 && $f.node(1).type.name === 'frontMatter'; }
    document.body.classList.toggle('gm-fm-open', open);
  }

  // Leading properties block of a text, by the editor's own rule: the first line is exactly ---, +++ or ;;;
  // and the block ends at the next line that is exactly one of them. It counts only when it closes and, for
  // ---, holds a key (name: value). The editor alone (before 2.0.0) also took an opening line that never closes, or
  // closes with "...", and then the whole document went into the folded box; and a file that starts with a
  // rule (---, a paragraph, ---) had that paragraph read as properties. Both views now use this test, and the
  // editor's parser follows it for each text it reads whole (frontMatterFor).
  const FRONT = /^(---|\+\+\+|;;;)\n(?:([\s\S]*?)\n)?(?:---|\+\+\+|;;;)(?:\n|$)/;
  function frontMatterMatch(text) {
    const m = FRONT.exec(text);
    if (!m) return null;
    const body = m[2] || '';
    if (m[1] === '---' ? !/^[^\s#:-][^:\n]*:(?:[ \t]|$)/m.test(body) : !body.trim()) return null;
    return m;
  }
  function splitFrontMatter(text) {
    const m = frontMatterMatch(text);
    return m ? { yaml: m[2] || '', body: text.slice(m[0].length) } : { yaml: null, body: text };
  }
  // The editor's parser reads a properties block only when the text has one by the rule above. Its option
  // frontMatter is read once, when the first editor is made, and adds the rule for good; the bundle is patched
  // so that the rule also asks window.gmFrontMatter (web.sh / build.ps1). Set before each whole read of a text
  // (load, reload); while typing it stays as it was, so a --- typed on the first line hides nothing.
  function frontMatterFor(text) { window.gmFrontMatter = !!frontMatterMatch(text || ''); }
  function yamlScalar(s) {
    s = s.trim();
    if (s.length > 1 && s[0] === '"' && s.endsWith('"'))
      return s.slice(1, -1).replace(/\\(u[0-9a-fA-F]{4}|.)/g, (m, c) => c.length > 1 ? String.fromCharCode(parseInt(c.slice(1), 16)) : ({ n: '\n', t: '\t', '0': '\0' }[c] || c));
    if (s.length > 1 && s[0] === "'" && s.endsWith("'")) return s.slice(1, -1).replace(/''/g, "'");
    return s;
  }
  // The YAML a properties block usually holds (keys, nested keys, lists, quoted values) as a tree of
  // { key, value, kids }; key is null for a list item. Lines it does not understand continue the value above.
  function parseFrontMatter(yaml) {
    const root = { kids: [], indent: -1 }; const stack = [root]; let last = null;
    for (const raw of yaml.split('\n')) {
      if (!raw.trim() || /^\s*#/.test(raw)) continue;
      const indent = raw.length - raw.trimStart().length; const line = raw.trim();
      let m = /^-(?:\s+(.*))?$/.exec(line), node = null;
      if (m) {
        const kv = m[1] && /^("[^"]*"|'[^']*'|[^:"'][^:]*?):(?:\s+(.*))?$/.exec(m[1]);
        node = kv ? { key: null, value: '', kids: [{ key: yamlScalar(kv[1]), value: yamlScalar(kv[2] || ''), kids: [] }] } : { key: null, value: yamlScalar(m[1] || ''), kids: [] };
      } else if ((m = /^("[^"]*"|'[^']*'|[^:"'\s][^:]*?):(?:\s+(.*))?$/.exec(line))) {
        node = { key: yamlScalar(m[1]), value: yamlScalar(m[2] || ''), kids: [] };
      } else if (last) { last.value += (last.value ? ' ' : '') + yamlScalar(line); continue; }
      else continue;
      while (stack.length > 1 && indent <= stack[stack.length - 1].indent) stack.pop();
      node.indent = indent;
      stack[stack.length - 1].kids.push(node);
      stack.push(node); last = node;
    }
    return root.kids;
  }
  function frontMatterHtml(nodes) {
    const list = nodes.length && nodes.every((n) => n.key === null);
    if (list) return '<ul>' + nodes.map((n) => '<li>' + escapeHtml(n.value) + (n.kids.length ? frontMatterHtml(n.kids) : '') + '</li>').join('') + '</ul>';
    return '<table><tbody>' + nodes.map((n) => '<tr><th>' + escapeHtml(n.key === null ? '-' : n.key) + '</th><td>' +
      escapeHtml(n.value) + (n.kids.length ? frontMatterHtml(n.kids) : '') + '</td></tr>').join('') + '</tbody></table>';
  }
  // GitHub view: the properties as a table, folded under a "Properties" line that names their keys.
  function frontMatterBlock(yaml) {
    const nodes = parseFrontMatter(yaml);
    const title = state.lang === 'es' ? 'Propiedades' : 'Properties';
    const keys = nodes.filter((n) => n.key !== null).map((n) => n.key).join(', ');
    const inner = nodes.length ? frontMatterHtml(nodes) : '<pre>' + escapeHtml(yaml) + '</pre>';
    return '<details class="gm-frontmatter"><summary>' + title + (keys ? ' <span>' + escapeHtml(keys) + '</span>' : '') + '</summary>' + inner + '</details>';
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

  // ------------------------------------------------------------------ paste inside a code block
  // In the visual view, whatever is pasted inside a code block goes in as plain text. ProseMirror only takes
  // the clipboard's plain text there when it has some: a clipboard that carries HTML alone (e.g. "[ ] - " as
  // <div>[ ] - </div><div><br></div>) was read as paragraphs, which closed the block at the cursor and turned
  // the rest of it into one paragraph, line breaks lost (1.13.0 and earlier). Files (images) are left to the editor.
  document.addEventListener('paste', (e) => {
    if (e.defaultPrevented || state.locked || state.mode !== 'visual' || !editor) return;
    const view = pmView(), cd = e.clipboardData;
    if (!view || !cd || !view.dom.contains(e.target)) return;
    const { $from, $to } = view.state.selection;
    if (!$from.parent.type.spec.code || !$from.sameParent($to)) return;
    if (Array.from(cd.items || []).some((i) => i.kind === 'file')) return;
    let text = cd.getData('text/plain');
    if (!text) { const html = cd.getData('text/html'); if (!html) return; text = htmlToText(html); }
    e.preventDefault(); e.stopPropagation();
    view.dispatch(view.state.tr.insertText(text.replace(/\r\n?/g, '\n')).scrollIntoView()
      .setMeta('paste', true).setMeta('uiEvent', 'paste'));
  }, true);

  /** The text of pasted HTML, one line per block or <br>, as it reads (DOMParser runs no script). */
  function htmlToText(html) {
    const body = new DOMParser().parseFromString(html, 'text/html').body;
    body.querySelectorAll('script,style,template').forEach((n) => n.remove());
    body.querySelectorAll('br').forEach((n) => n.replaceWith('\n'));
    body.querySelectorAll('p,div,li,tr,pre,blockquote,h1,h2,h3,h4,h5,h6').forEach((n) => n.after('\n'));
    return body.textContent.replace(/ /g, ' ').replace(/\n+$/, '');
  }

  function countWords() {
    const t = editorText().replace(/[#>*_`~\-\[\]()|]/g, ' ');
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
    } else if (editor) {
      try { editor.changeMode(state.mode === 'source' ? 'markdown' : 'wysiwyg', true); markGithubClass(); applyLock(); }
      catch (e) { editorFailed(e); }
    }
    if (find.query) runFind(0);
  }

  // ------------------------------------------------------------------ keep the original formatting
  // The visual editor rewrites Markdown in its own style. On save, every block the user did not
  // touch is written back exactly as it was on disk; only edited blocks take the new text.
  // The visual editor escapes the brackets (and - . |) of a [[name]] in a block it rewrites:
  // \[\[note\-a\]\] goes back to [[note-a]], so editing a paragraph never breaks its wiki links.
  function unescapeWiki(line) {
    return line.replace(/\\\[\\\[((?:\\.|[^\\\]\n])*?)\\\]\\\]/g, (m, inner) => '[[' + inner.replace(/\\(.)/g, '$1') + ']]');
  }
  // An edited line takes only the user's change; the rest of it stays as written in the file. The editor
  // rewrites a whole line in its own style when one character of it changes: \| and \. in a line with a
  // pipe, *x* for _x_, \~ for ~, and an inline tag it does not know (<kbd>, <br>) dropped. O is the line in
  // the file, B the editor's version of it when the file was read, C its version now. The change is what
  // differs between B and C past their common start and end; aligning B with O letter by letter gives that
  // stretch its place in O, where the new text goes.
  function mergeLine(O, B, C) {
    if (O === B) return unescapeWiki(C);
    let p = 0; const max = Math.min(B.length, C.length);
    while (p < max && B[p] === C[p]) p++;
    let s = 0;
    while (s < max - p && B[B.length - 1 - s] === C[C.length - 1 - s]) s++;
    const bEnd = B.length - s, ins = unescapeWiki(C.slice(p, C.length - s));
    const at = new Array(B.length).fill(-1);       // at[i]: where B[i] is in O, or -1 (only in B: an escape)
    let bi = 0, oi = 0;
    for (const part of diffArrays(B.split(''), O.split(''))) {
      if (part.added) oi += part.count;              // only in O: kept wherever it is
      else if (part.removed) bi += part.count;
      else for (let k = 0; k < part.count; k++) at[bi++] = oi++;
    }
    let start = -1, end = -1;
    for (let i = p; i < bEnd; i++) if (at[i] >= 0) { if (start < 0) start = at[i]; end = at[i] + 1; }
    if (start < 0) {                                 // nothing of O in the stretch: an insertion after its last kept letter
      let k = p - 1; while (k >= 0 && at[k] < 0) k--;
      start = end = k >= 0 ? at[k] + 1 : 0;
    }
    return O.slice(0, start) + ins + O.slice(end);
  }
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
    for (let i = 0; i < d2.length; i++) {
      const p = d2[i];
      if (p.removed && d2[i + 1] && d2[i + 1].added && d2[i + 1].value.length === p.value.length) {
        // lines changed one for one: each can keep the rest of its line as in the file (mergeLine)
        d2[i + 1].value.forEach((line, k) => plan.push({ kind: 'm', idx: n + k, line }));
        n += p.value.length; i++;
      }
      else if (p.added) { for (const line of p.value) plan.push({ kind: 'c', line }); }
      else if (p.removed) { n += p.value.length; }
      else { for (let k = 0; k < p.value.length; k++) { kept[n] = true; plan.push({ kind: 'n', idx: n++ }); } }
    }
    const segIntact = (s) => s.n.every((i) => kept[i]);
    const emitted = new Set();
    let prevKept = true;
    for (const step of plan) {
      if (step.kind === 'c') { out.push(unescapeWiki(step.line)); prevKept = false; continue; }
      if (step.kind === 'm') {
        const s = seg[segOfN[step.idx]];
        out.push(s.o.length === 1 && s.n.length === 1 ? mergeLine(s.o[0], N[step.idx], step.line) : unescapeWiki(step.line));
        prevKept = false; continue;
      }
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

  function wordsOf(s) { return (String(s).toLowerCase().match(/[\p{L}\p{N}]{3,}/gu) || []); }
  // the text of the paragraph (or item, cell, heading) a match is in
  function blockTextOf(m) {
    if (m.node) {
      let el = m.node.parentElement;
      while (el && !/^(P|LI|H[1-6]|TD|TH|PRE|BLOCKQUOTE|DT|DD|SUMMARY)$/.test(el.tagName)) el = el.parentElement;
      return (el || m.node.parentElement || { textContent: '' }).textContent || '';
    }
    const v = pmView();
    try { return v ? v.state.doc.resolve(m.from).parent.textContent : ''; } catch (e) { return ''; }
  }
  function closestByLine(line, k) {
    const want = new Set(wordsOf(line));
    if (!want.size) return k;
    let best = -1, bestScore = -1;
    find.matches.forEach((m, j) => {
      const have = new Set(wordsOf(blockTextOf(m)));
      let s = 0; want.forEach(w => { if (have.has(w)) s++; });
      if (s > bestScore || (s === bestScore && Math.abs(j - k) < Math.abs(best - k))) { best = j; bestScore = s; }
    });
    return best < 0 ? k : best;
  }

  function toRange(m) {
    const r = document.createRange();
    if (m.node) { r.setStart(m.node, m.start); r.setEnd(m.node, m.end); return r; }
    const view = pmView();
    const a = view.domAtPos(m.from), b = view.domAtPos(m.to);
    r.setStart(a.node, a.offset); r.setEnd(b.node, b.offset); return r;
  }

  // ------------------------------------------------------------------ what another program changed
  // After a reload, the blocks that are new or different are marked in green for a few seconds, in the
  // view on screen, so the reader sees what the AI wrote. A highlight paints over the text and leaves the
  // editor's own DOM alone. The blocks of each view are compared as text before and after the reload.
  const changeRoots = () => ({
    preview: ['#preview'],
    visual: ['.toastui-editor-ww-container .ProseMirror'],
    source: ['.toastui-editor-md-container .ProseMirror', '.toastui-editor-md-preview .toastui-editor-contents'],
  }[state.mode] || []).map((sel) => document.querySelector(sel)).filter(Boolean);
  // lists, tables and quotes count by item, so one point added to a list marks that point, not the list
  const units = (root) => Array.from(root.children).flatMap((el) =>
    /^(UL|OL|BLOCKQUOTE)$/.test(el.tagName) ? Array.from(el.children) : el.tagName === 'TABLE' ? Array.from(el.querySelectorAll('tr')) : [el]);
  const blockTexts = (root) => units(root).map((el) => el.textContent);
  let changeTimers = [];
  function markChanges(before) {
    if (!window.CSS || !CSS.highlights) return 0;
    changeTimers.forEach(clearTimeout); changeTimers = [];
    CSS.highlights.delete('gm-changed'); CSS.highlights.delete('gm-changed-fade');
    const ranges = [];
    changeRoots().forEach((root, i) => {
      const old = before[i]; if (!old) return;
      const kids = units(root);
      let k = 0;
      for (const part of diffArrays(old, kids.map((el) => el.textContent))) {
        if (part.removed) continue;
        if (part.added) for (let j = 0; j < part.count; j++) {
          const el = kids[k + j];
          if (el && el.textContent.trim()) { const r = new Range(); r.selectNodeContents(el); ranges.push(r); }
        }
        k += part.count;
      }
    });
    if (!ranges.length) return 0;
    CSS.highlights.set('gm-changed', new Highlight(...ranges));
    changeTimers.push(setTimeout(() => { CSS.highlights.delete('gm-changed'); CSS.highlights.set('gm-changed-fade', new Highlight(...ranges)); }, 2500));
    changeTimers.push(setTimeout(() => CSS.highlights.delete('gm-changed-fade'), 4000));
    return ranges.length;
  }

  function paint() {
    if (!window.CSS || !CSS.highlights) return;
    CSS.highlights.delete('gm-find'); CSS.highlights.delete('gm-cur');   // not the marks of a reload
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
    const text = editorText();
    const fm = frontMatterMatch(text);
    const skip = fm ? fm[0].replace(/\n$/, '').split('\n').length : 0;   // lines of the properties block
    text.split('\n').forEach((line, i) => {
      if (i < skip) return;
      if (/^\s*(```|~~~)/.test(line)) { fence = !fence; return; }
      if (fence) return;
      const m = /^(#{1,6})\s+(.+?)\s*#*\s*$/.exec(line);
      if (m) out.push({ level: m[1].length, text: headingText(m[2]), line: i });
    });
    return out;
  }
  // A heading as it reads: without emphasis and code marks, with escaped characters as themselves.
  // An underscore inside a word (IMG_1915) is text, not a mark; the editor writes it as \_ (1.14.0 showed
  // "IMG\1915": the mark was dropped and the backslash kept).
  function headingText(s) {
    const kept = [];
    s = s.replace(/\\([\\`*_{}\[\]()#+\-.!~|<>])/g, (m, c) => { kept.push(c); return '\u0000' + (kept.length - 1) + '\u0000'; });
    s = s.replace(/[*`~]/g, '').replace(/(^|[^\p{L}\p{N}])_+|_+(?=[^\p{L}\p{N}]|$)/gu, '$1');
    return s.replace(/\u0000(\d+)\u0000/g, (m, i) => kept[+i]);
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

  // ------------------------------------------------------------------ plain text ("Convert to TXT")
  // The document as it reads in the GitHub view, without the Markdown marks: headings underlined, lists with
  // bullets or numbers, tables in padded columns, quotes with "> ", code as it is and links with their address.
  const TXT_BLOCKS = /^(P|H[1-6]|UL|OL|PRE|TABLE|BLOCKQUOTE|HR|DIV|SECTION|DETAILS|SUMMARY|DL|DT|DD|LI|FIGURE)$/;
  function txtInline(node) {
    let s = '';
    node.childNodes.forEach((n) => { s += txtInlineOne(n); });
    return s;
  }
  function txtInlineOne(n) {
    if (n.nodeType === 3) return n.nodeValue;
    if (n.nodeType !== 1) return '';
    const t = n.tagName;
    if (t === 'BR') return '\n';
    if (t === 'IMG') return n.getAttribute('alt') || '';
    if (t === 'INPUT') return n.type === 'checkbox' ? (n.checked ? '[x] ' : '[ ] ') : '';
    if (n.classList.contains('sr-only') || n.hasAttribute('data-footnote-backref')) return '';
    if (n.classList.contains('katex')) {
      // DOMPurify drops <annotation> but keeps its TeX as loose text inside <math>; the drawn copy is the fallback
      const m = n.querySelector('math');
      const tex = m ? Array.from(m.childNodes).filter((x) => x.nodeType === 3).map((x) => x.nodeValue).join('').trim() : '';
      return tex || (n.querySelector('.katex-html') || n).textContent;
    }
    if (t === 'A') {
      const text = txtInline(n); const href = n.getAttribute('href') || '';
      const shown = href.replace(/^mailto:/i, '');
      return /^(https?:|mailto:)/i.test(href) && shown !== text.trim() ? text + ' (' + shown + ')' : text;
    }
    if (t === 'SUP' && n.querySelector('a')) return '[' + n.textContent.trim() + ']';
    if (TXT_BLOCKS.test(t)) return '\n' + txtInline(n) + '\n';
    return txtInline(n);
  }
  const txtClean = (s) => s.replace(/[ \t ]+/g, ' ').replace(/ *\n */g, '\n').replace(/\n{2,}/g, '\n').trim();
  function txtBlocks(el, out) {           // the blocks inside el, each a string, in order
    let para = '';
    const flush = () => { const t = txtClean(para); if (t) out.push(t); para = ''; };
    el.childNodes.forEach((n) => {
      if (n.nodeType === 1 && n.classList.contains('sr-only')) return;     // GitHub hides it ("Footnotes")
      if (n.nodeType === 1 && TXT_BLOCKS.test(n.tagName)) { flush(); txtBlock(n, out); }
      else para += txtInlineOne(n);
    });
    flush();
    return out;
  }
  const txtIndent = (s, pre) => s.split('\n').map((l) => (l ? pre + l : l)).join('\n');
  function txtBlock(n, out) {
    const t = n.tagName;
    if (/^H[1-6]$/.test(t)) {
      const h = txtClean(txtInline(n)).replace(/\n/g, ' ');
      if (h) out.push(t === 'H1' ? h + '\n' + '='.repeat(h.length) : t === 'H2' ? h + '\n' + '-'.repeat(h.length) : h);
    } else if (t === 'P' || t === 'DT' || t === 'SUMMARY') {
      const p = txtClean(txtInline(n)); if (p) out.push(p);
    } else if (t === 'PRE') {
      out.push(n.textContent.replace(/\n+$/, ''));
    } else if (t === 'HR') {
      out.push('----------');
    } else if (t === 'UL' || t === 'OL') {
      const start = parseInt(n.getAttribute('start') || '1', 10) || 1;
      const lines = Array.from(n.children).filter((li) => li.tagName === 'LI').map((li, i) => {
        const mark = t === 'OL' ? (start + i) + '. ' : '• ';
        const parts = txtBlocks(li, []);
        return parts.map((b, j) => (j === 0 ? mark + txtIndent(b, ' '.repeat(mark.length)).slice(mark.length) : txtIndent(b, ' '.repeat(mark.length)))).join('\n');
      });
      if (lines.length) out.push(lines.join('\n'));
    } else if (t === 'TABLE') {
      const rows = Array.from(n.querySelectorAll('tr')).map((tr) => Array.from(tr.children).map((c) => txtClean(txtInline(c)).replace(/\n/g, ' ')));
      if (!rows.length) return;
      const cols = Math.max(...rows.map((r) => r.length));
      const w = Array.from({ length: cols }, (_, i) => Math.max(...rows.map((r) => (r[i] || '').length)));
      const line = (r) => r.map((c, i) => (i === r.length - 1 ? c : c.padEnd(w[i]))).join('  ').replace(/\s+$/, '');
      const head = n.querySelector('thead tr') ? 1 : 0;
      const text = rows.map(line);
      if (head) text.splice(1, 0, w.map((x) => '-'.repeat(Math.max(x, 1))).join('  '));
      out.push(text.join('\n'));
    } else if (n.classList.contains('footnotes')) {
      out.push('----------'); txtBlocks(n, out);
    } else if (t === 'BLOCKQUOTE' || n.classList.contains('markdown-alert')) {
      const inner = txtBlocks(n, []).join('\n\n');
      if (inner) out.push(inner.split('\n').map((l) => (l ? '> ' + l : '>')).join('\n'));
    } else if (t === 'DD') {
      const inner = txtBlocks(n, []).join('\n\n'); if (inner) out.push(txtIndent(inner, '    '));
    } else {
      txtBlocks(n, out);                  // DIV, SECTION, DETAILS, DL, FIGURE, LI out of a list
    }
  }
  function plainText(markdown) {
    const root = document.createElement('div');
    root.innerHTML = renderGithub(String(markdown || '').replace(/^﻿/, '').replace(/\r\n?/g, '\n'));
    const text = txtBlocks(root, []).join('\n\n');
    return text ? text + '\n' : '';
  }

  // ------------------------------------------------------------------ API used by the host
  window.mdApp = {
    load(markdown, docBase) {
      document.getElementById('docbase').href = docBase || 'https://app.local/';
      original = (markdown || '').replace(/\r\n/g, '\n');
      build(original);
      baseline = editorText();
      applyMode(); sendOutline();
      return true;
    },
    // The same file changed on disk (another program, often an AI assistant, is editing it):
    // swap the text in place and keep the reader where they were in every view.
    async reload(markdown) {
      if (!editor) { this.load(markdown); return true; }
      const before = changeRoots().map(blockTexts);
      const panes = ['#previewHost', '#editorHost .toastui-editor-md-preview', '#editorHost .ProseMirror'];
      const kept = panes.map((sel) => Array.from(document.querySelectorAll(sel)).map((el) => el.scrollTop));
      // a reader at the end of a view follows the end: what the AI appends comes into sight (as in a terminal)
      const atEnd = panes.map((sel) => Array.from(document.querySelectorAll(sel)).map((el) =>
        el.scrollHeight > el.clientHeight + 2 && el.scrollHeight - el.scrollTop - el.clientHeight < 40));
      original = (markdown || '').replace(/\r\n/g, '\n');
      suppressDirty = true;
      quietText = null;
      frontMatterFor(original);
      try { editor.setMarkdown(original, false); } catch (e) { editorFailed(e); }
      baseline = editorText();
      quietText = baseline;
      setTimeout(() => { suppressDirty = false; }, 50);
      markGithubClass(); applyLock();
      if (state.mode === 'preview') await showPreview();
      const restore = () => panes.forEach((sel, i) => document.querySelectorAll(sel).forEach((el, j) => {
        if (atEnd[i][j]) el.scrollTop = el.scrollHeight;
        else if (kept[i][j] !== undefined) el.scrollTop = kept[i][j];
      }));
      restore(); requestAnimationFrame(() => { restore(); setTimeout(restore, 120); });
      sendOutline(); if (find.query) scheduleFind();
      // the side preview of the Markdown view renders a moment later
      await new Promise((r) => setTimeout(r, state.mode === 'source' ? 200 : 0));
      lastMarked = markChanges(before);
      return true;
    },
    changesMarked() { return lastMarked; },
    getMarkdown() { return editorText(); },
    getMarkdownPreserving() { return currentText(); },
    plainText(markdown) { return plainText(markdown); },
    markSaved(text) { original = text.replace(/\r\n/g, '\n'); baseline = editorText(); },
    // What a save writes, taken and marked as saved in one step. Taking the text and marking it in two
    // calls let an edit typed in between enter the new baseline without being in the saved text, and
    // the next save then dropped it. seq is the last edit the text holds: an edit after it keeps the
    // document modified.
    takeForSave() {
      let t = currentText();
      if (!t.endsWith('\n')) t += '\n';
      original = t; baseline = editorText();
      return { text: t, seq: editSeq };
    },
    setLang(lang) { const t = editorText(); state.lang = lang; build(t); applyMode(); },
    setTheme(theme) { const t = editorText(); state.theme = theme; applyTheme(); build(t); applyMode(); },
    setMode(mode) { state.mode = mode; applyMode(); },
    setSpell(on) { state.spell = !!on; applySpell(); },
    // Width of the text column in the Visual and GitHub views, in px; 0 = the whole window.
    setWidth(px) {
      px = Number(px) || 0;
      document.body.style.setProperty('--gm-col', (px > 0 ? Math.max(600, px) : 100000) + 'px');
    },
    // wiki: the host resolves [[name]] links (the macOS app does; until the Windows app does, they stay as text)
    init(lang, theme, mode, spell, locked, wiki) { state = { lang, theme, mode, spell: spell !== false, locked: !!locked, wiki: !!wiki }; applyTheme(); build(''); applyMode(); },
    setLocked(on) { state.locked = !!on; applyLock(); },
    exec(cmd, payload) { if (!editor || state.mode === 'preview' || state.locked) return; try { editor.exec(cmd, payload); } catch (e) { } editor.focus(); },
    insertText(t) { if (!state.locked && editor) editor.insertText(t); },
    words() { return countWords(); },
    selectedText() { try { return editor.getSelectedText() || ''; } catch (e) { return ''; } },
    focus() { if (editor && state.mode !== 'preview') editor.focus(); },
    // step 0 with the same words keeps the place (the find bar asks again when its text is set from outside)
    find(q, cs, step) { const changed = q !== find.query || !!cs !== find.cs; find.query = q; find.cs = !!cs; if (changed) find.cur = -1; runFind(changed ? 0 : step, !changed && !step); },
    findClose() { find.query = ''; find.matches = []; find.cur = -1; paint(); },
    // the search in the folders: straight to the i-th match of q, counted in the file's text. A view that hides
    // the Markdown (link addresses, tags) can hold another number of matches: then the one taken is the one whose
    // paragraph shares most words with that line of the file, the nearest to i among equals.
    findAt(q, cs, i, line, total) {
      find.query = q; find.cs = !!cs; find.cur = -1; runFind(0);
      const n = find.matches.length; if (!n) return;
      let k = Math.max(0, i | 0);
      if (typeof line === 'string' && total !== n) k = closestByLine(line, k);
      find.cur = Math.min(k, n - 1); paint(); report();
    },
    replace(q, cs, r) { find.query = q; find.cs = !!cs; replaceCurrent(r); },
    replaceAll(q, cs, r) { find.query = q; find.cs = !!cs; return replaceAll(r); },
    scrollToHeading(i) { scrollToHeading(i); },
    _view() { return pmView(); },   // for tools/jstest.py
    _headingText(s) { return headingText(s); },
    editorBroken() { return editorBroken; },
    async exportHtml(title) {
      const tmp = document.createElement('div'); tmp.className = 'markdown-body'; tmp.style.cssText = 'position:absolute;left:-99999px;width:980px';
      document.body.appendChild(tmp);
      tmp.innerHTML = renderGithub(currentText());
      await runMermaid(tmp);
      const body = tmp.innerHTML; tmp.remove();
      post({ type: 'export', title, body, theme: state.theme, math: /class="katex/.test(body) });
    },
    async preparePrint() { await showPreview(); post({ type: 'printReady' }); },
    setBase(docBase) { document.getElementById('docbase').href = docBase || 'https://app.local/'; },
    wikiState(found) {
      document.querySelectorAll('#preview a.wikilink').forEach((a) => {
        const missing = found[a.dataset.wiki] === false;
        a.classList.toggle('missing', missing);
        if (missing) a.title = a.dataset.wiki + '.md ?'; else a.removeAttribute('title');
      });
    },
    refreshWikiLinks() { askWiki(); },
    // The host's answer to a ![[name]] that did not load: its address, or null when no open folder has it.
    fileFound(name, url) {
      document.querySelectorAll('#preview img[data-embed]').forEach((img) => {
        if (img.dataset.embed !== name || img.dataset.asked !== 'wait') return;
        img.dataset.asked = 'done';
        if (url) img.setAttribute('src', url); else markMissing(img);
      });
    },
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
    if (k === 'f' && e.shiftKey) { e.preventDefault(); post({ type: 'cmd', cmd: 'search' }); return; }
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
    // a picture that is not a link opens in its viewer
    const img = e.target.closest('img');
    if (img && !img.closest('a') && img.currentSrc) { e.preventDefault(); post({ type: 'link', href: img.currentSrc }); return; }
    const a = e.target.closest('a'); if (!a) return;
    const href = a.getAttribute('href') || '';
    e.preventDefault();
    if (a.classList.contains('wikilink')) { post({ type: 'wikilink', name: a.dataset.wiki }); return; }
    if (href.startsWith('#')) { const t = document.getElementById(decodeURIComponent(href.slice(1))); if (t) t.scrollIntoView(); return; }
    post({ type: 'link', href: a.href });
  });

  post({ type: 'ready' });
})();
