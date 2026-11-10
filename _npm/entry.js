/* Gryphmark -- entry point of the web bundle (built with esbuild into wwwroot/lib/bundle.js).
   Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
   Date: November 10, 2026
   SPDX-License-Identifier: GPL-2.0-only */
import Editor from '@toast-ui/editor';
import '@toast-ui/editor/dist/i18n/es-es';
import codeSyntaxHighlight from '@toast-ui/editor-plugin-code-syntax-highlight/dist/toastui-editor-plugin-code-syntax-highlight-all.js';
import { Marked } from 'marked';
import markedAlert from 'marked-alert';
import markedFootnote from 'marked-footnote';
import markedKatex from 'marked-katex-extension';
import hljs from 'highlight.js';
import { nameToEmoji } from 'gemoji';
import { diffArrays } from 'diff';
import { TextSelection } from 'prosemirror-state';
import DOMPurify from 'dompurify';

window.ToastEditor = Editor;
window.codeSyntaxHighlight = codeSyntaxHighlight;
window.Marked = Marked;
window.markedAlert = markedAlert;
window.markedFootnote = markedFootnote;
window.markedKatex = markedKatex;
window.hljs = hljs;
window.nameToEmoji = nameToEmoji;
window.diffArrays = diffArrays;
window.PMTextSelection = TextSelection;
window.DOMPurify = DOMPurify;
