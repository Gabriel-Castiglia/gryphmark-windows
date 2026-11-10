import Editor from '@toast-ui/editor';
import '@toast-ui/editor/dist/i18n/es-es';
import codeSyntaxHighlight from '@toast-ui/editor-plugin-code-syntax-highlight/dist/toastui-editor-plugin-code-syntax-highlight-all.js';
import { marked } from 'marked';
import hljs from 'highlight.js';

window.ToastEditor = Editor;
window.codeSyntaxHighlight = codeSyntaxHighlight;
window.marked = marked;
window.hljs = hljs;
