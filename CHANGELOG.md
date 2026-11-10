# Changelog

## 1.7.0 — 2026-11-10

- **Dedicated instances:** a file or folder can open in its own Gryphmark, always separate from the main window,
  with its own settings, open folders, recent files and window (`--profile "Name"`, kept under
  `%APPDATA%\Gryphmark\profiles\Name`; a new one starts with the main language, theme and colour). Opening it
  again brings that window to the front instead of starting another. Right-click a file or a folder title in the
  Files panel → *Open in a dedicated instance* or *Desktop shortcut (dedicated instance)*. The title bar shows the
  instance name.
- **Several windows done right:** an extra window (*Open in a new window*) no longer writes over the main
  window's session (open folders, window size); it only saves preference changes. Crash-recovery drafts of a
  window that is still running are never offered to another one.

## 1.6.0 — 2026-11-10

- **Several folders at once** in the Files panel, one tree per folder, added in the order they are opened. Each
  folder is a block with its own pastel colour and a stripe on the left: by default tones of the theme colour,
  or *Multicolour*, or *No colour*, and any folder can get its own colour from its right-click menu.
- **Order** from the panel's ⋯ button: order opened (default), name A → Z, name Z → A, recently modified first;
  *Move up / Move down* in the right-click menu.
- **Pin to top**: pinned folders always stay first.
- **Folders at start** (*File → Folders at start*, also in the ⋯ button): reopen the last session's folders
  (default), start clean, or start clean with only the pinned folders.
- **Sturdier when things disappear**: a deleted open folder or a lost network drive is shown as *not available*
  and comes back by itself; a file deleted in Explorer leaves the tree at once, and clicking a stale entry just
  refreshes the panel. Watcher errors and buffer overflows trigger a refresh instead of being lost.
- **Narrow windows**: the formatting buttons that do not fit go into a ⋯ menu instead of sliding under the view
  and read-only switches.
- **Zoom bar** in the status bar: −, slider, +, and the percentage (click it for 100 %); Ctrl + mouse wheel works
  over the whole window.
- The default theme colour is now **dark gold**. A colour saved by 1.5 without being chosen gives way to it.

## 1.5.1 — 2026-11-10

- The theme colour now reaches the desktop and Start-menu shortcuts at once: Windows caches icons by file
  path, so each colour has its own icon file and the shortcuts and file-type entries are pointed at it.

## 1.5.0 — 2026-11-10

- **Themes** (*View → Theme…*): **Light**, **Dark** or **Automatic** (follows the Windows app mode and switches
  live), plus a catalogue of eleven colours. The colour is the background of the icon — window, taskbar,
  desktop and Start-menu shortcuts, `.md` files and the folder menu — and the highlight of the app (folders in
  the Files panel, check marks, the selected item). Shortcuts with an icon of their own are left alone.
- The default colour became Advent purple (1.6.0 changes it to dark gold); the original orange is in the catalogue. New installs start in
  Automatic; existing ones keep their light or dark choice.

## 1.4.0 — 2026-11-10

- **Read only:** a new *Edit | Read only* switch next to the view buttons (also *View → Read only*,
  `Ctrl+Shift+L`) locks the text in the Visual and Markdown views, so a stray key, paste, drop or click on a
  task box cannot change the document. Reading, selecting, copying, finding and switching views still work;
  typing shows a hint in the status bar. The choice is remembered.
- **Hiding the sample is now visible:** *File → Open recent* shows a **Hide sample** item right below it
  (before, it was only a right-click inside the submenu), and *Help → Show sample in Open recent* turns it
  off and on again.

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
