# Changelog

## 1.3.0 — 2026-11-10

- **Files panel** (`Ctrl+Shift+E`, the folder button, or *View → Files panel*): open a whole folder and
  browse its subfolders and Markdown files in a tree on the left, like VS Code or Obsidian. A click opens a
  file beside the tree; the panel follows changes on disk, marks the open file and can be resized.
  Hidden folders and tool folders (`.git`, `node_modules`, `bin`, `obj`…) are left out.
- Open a folder with *File → Open folder…*, by dropping it on the window, from the command line, or with
  the new optional installer task *Open with Gryphmark* in the Explorer context menu of folders.
- The last folder is reopened on the next start; recent folders are listed in *File → Open recent*.
- Right-click menu in the tree: open, open in a new window, show in folder, copy path.

## 1.2.1 — 2026-11-10

- Sample document installed with the program (English and Spanish, hidden file in `samples\`): it opens on
  the first start, from **Help → Sample document**, and pinned at the top of **File → Open recent**, where a
  right-click hides it. It opens as an untitled copy, so the installed file is never overwritten, and it
  switches language together with the interface.
- Closing hides the window at once; the editor is torn down behind it.
- The daily update check is remembered even when it fails, so it no longer runs on every start.
- Diagnostics: `GM_TRACE_CLOSE=<log>` times each shutdown step.

## 1.2.0 — 2026-11-10

- **Renamed from Quillforge to Gryphmark**, after the griffin in the icon. Program, installer, manuals,
  settings folder (`%APPDATA%\Gryphmark`) and file association use the new name.
- The installer removes a previous Quillforge install; settings, recent files and recovery drafts are
  copied over on first start (the old folder is left untouched).

## 1.1.1 — 2026-11-10

- New About window in the app style: icon, version, author, GPL-2.0 license with a link to the full text,
  complete credits (highlight.js and jsdiff are BSD-3-Clause) and a *Copy info* button. English and Spanish.
- The Visual / Markdown / GitHub buttons now explain in their tooltip what each mode is for.
- Manual: the modes are named as on the buttons; About documented; Spanish screenshots.

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
