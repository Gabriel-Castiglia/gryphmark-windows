/* Quillforge — editor page. Talks to the WPF host through chrome.webview. */
(function () {
  'use strict';
  const host = window.chrome && window.chrome.webview;
  const post = (msg) => host && host.postMessage(msg);

  let editor = null;
  let state = { lang: 'en', theme: 'light', mode: 'visual' };
  let suppressDirty = false;

  marked.setOptions({ gfm: true, breaks: false });
  const renderer = new marked.Renderer();
  renderer.code = function ({ text, lang }) {
    const l = (lang || '').split(/\s+/)[0];
    let html;
    try {
      html = l && hljs.getLanguage(l) ? hljs.highlight(text, { language: l }).value : hljs.highlightAuto(text).value;
    } catch (e) { html = escapeHtml(text); }
    return '<pre><code class="hljs' + (l ? ' language-' + escapeHtml(l) : '') + '">' + html + '</code></pre>';
  };
  // GitHub-style heading anchors
  const slugCount = {};
  renderer.heading = function ({ tokens, depth }) {
    const inner = this.parser.parseInline(tokens);
    const plain = inner.replace(/<[^>]+>/g, '').toLowerCase().trim()
      .replace(/[^\p{L}\p{N}\s-]/gu, '').replace(/\s/g, '-');
    let slug = plain; const n = slugCount[plain] || 0; if (n) slug += '-' + n; slugCount[plain] = n + 1;
    return '<h' + depth + ' id="' + slug + '">' + inner + '</h' + depth + '>';
  };

  function escapeHtml(s) { return String(s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c])); }

  function renderGithub(md) {
    for (const k in slugCount) delete slugCount[k];
    let html = marked.parse(md || '', { renderer });
    // GitHub task lists render disabled checkboxes
    html = html.replace(/<input (checked="" )?disabled="" type="checkbox">/g, (m, c) => '<input type="checkbox" disabled ' + (c ? 'checked ' : '') + 'class="task-list-item-checkbox">');
    return html;
  }

  function markGithubClass() {
    document.querySelectorAll('.toastui-editor-contents').forEach((e) => e.classList.add('markdown-body'));
  }

  function build(markdown) {
    const el = document.getElementById('editorHost');
    if (editor) { editor.destroy(); editor = null; }
    el.innerHTML = '';
    suppressDirty = true;
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
      toolbarItems: [
        ['heading', 'bold', 'italic', 'strike'],
        ['hr', 'quote'],
        ['ul', 'ol', 'task', 'indent', 'outdent'],
        ['table', 'image', 'link'],
        ['code', 'codeblock'],
      ],
      events: {
        change: () => { if (!suppressDirty) post({ type: 'dirty', words: countWords() }); },
      },
    });
    markGithubClass();
    // Toast normalises the text once after load; that is not a user edit
    setTimeout(() => { suppressDirty = false; }, 50);
  }

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

  function applyMode() {
    document.body.classList.toggle('mode-preview', state.mode === 'preview');
    if (state.mode === 'preview') {
      document.getElementById('preview').innerHTML = renderGithub(editor.getMarkdown());
      document.getElementById('previewHost').scrollTop = 0;
    } else {
      editor.changeMode(state.mode === 'source' ? 'markdown' : 'wysiwyg', true);
      markGithubClass();
    }
  }

  // ---- API used by the host (ExecuteScriptAsync) ----
  window.mdApp = {
    load(markdown, docBase) {
      document.getElementById('docbase').href = docBase || 'https://app.local/';
      build(markdown);
      applyMode();
      return true;
    },
    getMarkdown() { return editor ? editor.getMarkdown() : ''; },
    setLang(lang) { const md = editor.getMarkdown(); state.lang = lang; build(md); applyMode(); },
    setTheme(theme) { const md = editor.getMarkdown(); state.theme = theme; applyTheme(); build(md); applyMode(); },
    setMode(mode) { state.mode = mode; applyMode(); },
    init(lang, theme, mode) { state = { lang, theme, mode }; applyTheme(); build(''); applyMode(); },
    exec(cmd, payload) { if (state.mode === 'preview') return; try { editor.exec(cmd, payload); } catch (e) { /* not available in this mode */ } editor.focus(); },
    insertText(t) { editor.insertText(t); },
    words() { return countWords(); },
    exportHtml(title) {
      const css = document.getElementById('ghcss').href, hl = document.getElementById('hlcss').href;
      return { body: renderGithub(editor.getMarkdown()), title, theme: state.theme, css, hl };
    },
    preparePrint() { document.getElementById('preview').innerHTML = renderGithub(editor.getMarkdown()); },
  };

  // ---- keyboard shortcuts while the page has focus ----
  document.addEventListener('keydown', (e) => {
    if (!e.ctrlKey || e.altKey) return;
    const k = e.key.toLowerCase();
    const map = { n: 'new', o: 'open', s: e.shiftKey ? 'saveas' : 'save', p: 'print', w: 'close' };
    if (map[k]) { e.preventDefault(); post({ type: 'cmd', cmd: map[k] }); }
    else if (k === '1' || k === '2' || k === '3') { e.preventDefault(); post({ type: 'cmd', cmd: 'mode' + k }); }
    else if (k === '=' || k === '+' || k === '-' || k === '0') { e.preventDefault(); post({ type: 'cmd', cmd: k === '0' ? 'zoom0' : (k === '-' ? 'zoom-' : 'zoom+') }); }
  }, true);

  // ---- drag & drop a file onto the window ----
  window.addEventListener('dragover', (e) => { e.preventDefault(); }, true);
  window.addEventListener('drop', (e) => {
    if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length) {
      e.preventDefault(); e.stopPropagation();
      if (host.postMessageWithAdditionalObjects) host.postMessageWithAdditionalObjects('drop', e.dataTransfer.files);
    }
  }, true);

  // ---- links in the GitHub view ----
  document.getElementById('preview').addEventListener('click', (e) => {
    const a = e.target.closest('a'); if (!a) return;
    const href = a.getAttribute('href') || '';
    e.preventDefault();
    if (href.startsWith('#')) { const t = document.getElementById(decodeURIComponent(href.slice(1))); if (t) t.scrollIntoView(); return; }
    post({ type: 'link', href: a.href });
  });

  post({ type: 'ready' });
})();
