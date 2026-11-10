# Changelog

## 1.1.0 — 2026-11-10

- Find & replace bar (`Ctrl+F`, `Ctrl+H`, `F3` / `Shift+F3`, match case).
- Outline sidebar with the document headings (`Ctrl+Shift+O`).
- Formatting is preserved on save: only edited lines change; list markers, emphasis style,
  table alignment, CRLF/LF and BOM are kept.
- GitHub extras: alerts (`[!NOTE]` … `[!CAUTION]`), Mermaid diagrams, KaTeX math, footnotes, emoji shortcodes.
- Reloads the file when another program changes it (asks first if there are unsaved changes).
- Crash recovery: unsaved work is backed up every 20 seconds and offered on the next start.
- Spell check (View menu).
- Single instance: opening a `.md` brings the running window forward (`File → New window` still opens another).
- Daily update check against GitHub releases (Help → Check for updates).
- Native, Apple-style toolbar and menus; refreshed light/dark palette.
- Installer shows and installs the license (GPL-2.0-only). Optional code signing in `build.ps1`.

## 1.0.0 — 2026-11-10

- First release: visual editor, Markdown source with live preview and GitHub preview; English/Spanish UI;
  light/dark theme; export to HTML; print/PDF; recent files; per-user installer with `.md` association; manuals EN/ES.
