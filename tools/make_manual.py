"""Builds docs/Gryphmark_Manual_EN.html and _ES.html; build.ps1 prints them to PDF with Edge."""
import pathlib, base64, sys

root = pathlib.Path(__file__).resolve().parents[1]
docs = root / "docs"
version = sys.argv[1] if len(sys.argv) > 1 else "1.0.0"

def img(name):
    return "data:image/png;base64," + base64.b64encode((docs / "img" / name).read_bytes()).decode()

icon = "data:image/png;base64," + base64.b64encode((root / "assets" / "gryphmark.png").read_bytes()).decode()

CSS = """
@page { size: A4; margin: 18mm 17mm 20mm; }
* { box-sizing: border-box; }
body { font-family: "Segoe UI", system-ui, sans-serif; color: #1f2328; font-size: 10.5pt; line-height: 1.55; margin: 0; }
.cover { height: 257mm; display: flex; flex-direction: column; justify-content: center; align-items: center; text-align: center;
  background: radial-gradient(ellipse at 50% 95%, #7a3412 0%, #2a1416 45%, #15172a 100%); color: #fff; border-radius: 10px; page-break-after: always; }
.cover img { width: 190px; height: 190px; filter: drop-shadow(0 12px 30px rgba(255,120,30,.45)); }
.cover h1 { font-size: 46pt; margin: 22px 0 4px; letter-spacing: 1px; font-weight: 700; }
.cover .tag { font-size: 14pt; color: #ffcf8a; margin: 0 0 40px; }
.cover .meta { font-size: 10pt; color: #c9c3d9; }
h2 { font-size: 17pt; color: #8a3a0e; border-bottom: 2px solid #f0a35a; padding-bottom: 4px; margin: 26px 0 10px; page-break-after: avoid; }
h3 { font-size: 12.5pt; margin: 18px 0 6px; color: #2a1416; page-break-after: avoid; }
p { margin: 6px 0 9px; }
table { border-collapse: collapse; width: 100%; margin: 8px 0 14px; font-size: 9.8pt; page-break-inside: avoid; }
th, td { border: 1px solid #d0d7de; padding: 5px 9px; text-align: left; vertical-align: top; }
th { background: #fbeee2; }
kbd { font-family: Consolas, monospace; font-size: 9pt; background: #f6f8fa; border: 1px solid #d0d7de; border-bottom-width: 2px; border-radius: 4px; padding: 0 5px; white-space: nowrap; }
code { font-family: Consolas, monospace; font-size: 9.2pt; background: #f2f2f4; border-radius: 4px; padding: 0 4px; }
figure { margin: 10px 0 16px; page-break-inside: avoid; text-align: center; }
figure img { width: 100%; border: 1px solid #d0d7de; border-radius: 6px; box-shadow: 0 2px 8px rgba(0,0,0,.08); }
figcaption { font-size: 9pt; color: #59636e; margin-top: 4px; }
.note { background: #fff6ec; border-left: 4px solid #f0a35a; padding: 8px 12px; margin: 10px 0; border-radius: 0 6px 6px 0; }
.toc { columns: 2; font-size: 10pt; }
.toc div { margin: 2px 0; }
.page { page-break-before: always; }
"""

T = {
 "en": dict(
  title="User Manual", tag="Write Markdown visually. See it like GitHub.", lang="en",
  toc="Contents",
  sections=[
   ("What is Gryphmark", f"""
<p><b>Gryphmark</b> is a Markdown viewer and editor for Windows. You format text with buttons — headings, bold, lists,
tables, code — and Gryphmark writes the Markdown tags behind the scenes. Every document is shown exactly the way
GitHub displays it, in light or dark theme.</p>
<p>It is designed for <code>README.md</code> files, notes, documentation and any <code>.md</code> file you want to read or edit
without memorising syntax.</p>
<figure><img src="{img('visual_light_en.png')}"><figcaption>Visual editor: format with the toolbar, no tags in sight.</figcaption></figure>
"""),
   ("Installation", """
<p>Run <code>Gryphmark-Setup-{v}.exe</code>. The installer is available in English and Spanish and does <b>not</b> need
administrator rights: it installs for the current user in <code>%LOCALAPPDATA%\\Programs\\Gryphmark</code>.</p>
<table><tr><th>Option</th><th>What it does</th></tr>
<tr><td>Desktop shortcut</td><td>Adds a Gryphmark icon to the desktop.</td></tr>
<tr><td>Open .md files with Gryphmark</td><td>Adds Gryphmark to <i>Open with</i> for .md / .markdown files, and makes it the handler for those files for the current user.</td></tr>
<tr><td>Add "Open with Gryphmark" to folders</td><td>Right-click a folder (or the empty space inside one) in Explorer to open it in the Files panel. In Windows 11 it is under <i>Show more options</i>.</td></tr></table>
<p>Requirements: Windows 10 or 11 (64-bit) and Microsoft Edge WebView2 Runtime, which is already part of Windows 11.
The .NET runtime is included; nothing else has to be installed.</p>
<p>Up to version 1.1.1 the program was called <b>Quillforge</b>. If that version is installed, the installer removes it,
and Gryphmark copies its settings, recent files and recovery drafts on the first start.</p>
<p>To uninstall: <i>Settings → Apps → Installed apps → Gryphmark → Uninstall</i>. Your settings in
<code>%APPDATA%\\Gryphmark</code> are kept.</p>
"""),
   ("The window", """
<table><tr><th>Part</th><th>Use</th></tr>
<tr><td>Menu bar</td><td>File, Edit, Format, View, Language and Help. All menus are available in English and Spanish.</td></tr>
<tr><td>Toolbar</td><td>The formatting buttons (see below). Hidden in GitHub preview, which is read-only.</td></tr>
<tr><td>Document</td><td>The page itself, laid out with the same column width, fonts and spacing as github.com.</td></tr>
<tr><td>Status bar</td><td>File path, word count, current mode, and whether there are unsaved changes.</td></tr></table>
<p>The title bar shows the file name; a <b>●</b> in front of it means there are unsaved changes.</p>
"""),
   ("Three ways to see a document", f"""
<p>Switch with the three buttons at the right end of the toolbar — <b>Visual · Markdown · GitHub</b> —, the <i>View</i> menu or the shortcuts. Hover over a button to see what it does.</p>
<table><tr><th>Button</th><th>Shortcut</th><th>What you see</th></tr>
<tr><td><b>Visual</b><br><small>Visual editor</small></td><td><kbd>Ctrl</kbd>+<kbd>1</kbd></td><td>The formatted document. Type and use the buttons; the Markdown is generated for you.</td></tr>
<tr><td><b>Markdown</b><br><small>Markdown source</small></td><td><kbd>Ctrl</kbd>+<kbd>2</kbd></td><td>The raw Markdown on the left and a live preview on the right. For when you want to see or touch the tags.</td></tr>
<tr><td><b>GitHub</b><br><small>GitHub preview</small></td><td><kbd>Ctrl</kbd>+<kbd>3</kbd></td><td>Read-only page rendered like GitHub, with syntax highlighting, task lists and clickable links.</td></tr></table>
<figure><img src="{img('source_light_en.png')}"><figcaption>Markdown source with live preview.</figcaption></figure>
<figure><img src="{img('preview_light_en.png')}"><figcaption>GitHub preview (light theme).</figcaption></figure>
<h3>Read only</h3>
<p>When you only want to read a document, choose <b>Read only</b> in the <b>Edit | Read only</b> switch, to the left of the view buttons
(also <i>View → Read only</i> or <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>L</kbd>). The text is locked in every view: keys, paste, drag and drop and
the task boxes do nothing, and the formatting buttons are greyed out. You can still read, select, copy, find and switch views.
If you type, the status bar reminds you how to unlock it. Choose <b>Edit</b> to write again; Gryphmark remembers your choice.</p>
"""),
   ("Folders: the Files panel", f"""
<p>Like VS Code or Obsidian, Gryphmark can open a whole folder and show its tree on the left: subfolders and
Markdown files (<code>.md</code>, <code>.markdown</code>). Click a file and it opens beside the tree.</p>
<figure><img src="{img('files_panel_light.png')}" style="width:260px"><figcaption>The Files panel with a folder open; the open file is marked.</figcaption></figure>
<table><tr><th>Action</th><th>How</th></tr>
<tr><td>Open a folder</td><td><i>File → Open folder…</i>, the <b>Open folder…</b> button of the empty panel, dropping a folder on the window, or right-clicking a folder in Explorer → <i>Open with Gryphmark</i> (installer option; in Windows 11 it is under <i>Show more options</i>).</td></tr>
<tr><td>Show / hide the panel</td><td><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>E</kbd>, the folder button at the left of the toolbar, or <i>View → Files panel</i>. Drag its right edge to make it wider.</td></tr>
<tr><td>Browse</td><td>Click a folder to open or close it; click a file to open it. <kbd>Enter</kbd> does the same on the selected row.</td></tr>
<tr><td>Right-click</td><td>On a file: open, open in a new window, show in folder, copy path. On a folder: show in folder, copy path, refresh.</td></tr>
<tr><td>Panel buttons</td><td>Refresh, collapse all, close folder.</td></tr></table>
<p>The panel follows the disk: files and folders created, renamed or deleted by other programs appear at once.
The open file is marked in the tree. Hidden folders and tool folders (<code>.git</code>, <code>node_modules</code>,
<code>bin</code>, <code>obj</code>…) are left out. The folder is reopened the next time Gryphmark starts, and recent
folders are listed in <i>File → Open recent</i>.</p>
"""),
   ("Formatting toolbar", """
<table><tr><th>Button</th><th>Action</th><th>Markdown it writes</th></tr>
<tr><td>H</td><td>Heading 1–6 or normal paragraph</td><td><code># Title</code> … <code>###### Title</code></td></tr>
<tr><td><b>B</b></td><td>Bold</td><td><code>**text**</code></td></tr>
<tr><td><i>I</i></td><td>Italic</td><td><code>*text*</code></td></tr>
<tr><td><s>S</s></td><td>Strikethrough</td><td><code>~~text~~</code></td></tr>
<tr><td>—</td><td>Horizontal rule</td><td><code>***</code></td></tr>
<tr><td>❝</td><td>Quote</td><td><code>&gt; text</code></td></tr>
<tr><td>• list / 1. list</td><td>Bulleted / numbered list</td><td><code>- item</code> / <code>1. item</code></td></tr>
<tr><td>☑</td><td>Task list</td><td><code>- [ ] task</code></td></tr>
<tr><td>→ / ←</td><td>Indent / outdent list item</td><td>nested list</td></tr>
<tr><td>Table</td><td>Insert table (choose rows × columns)</td><td><code>| a | b |</code></td></tr>
<tr><td>Image</td><td>Insert image from URL or file name</td><td><code>![alt](file.png)</code></td></tr>
<tr><td>Link</td><td>Insert link</td><td><code>[text](https://…)</code></td></tr>
<tr><td>&lt;/&gt;</td><td>Inline code</td><td><code>`code`</code></td></tr>
<tr><td>CB</td><td>Code block (choose language for colouring)</td><td><code>```python</code></td></tr></table>
<p>The same actions are in the <b>Format</b> menu. Right-click inside a table to add or delete rows and columns, or to align a column.</p>
"""),
   ("Files", """
<table><tr><th>Action</th><th>How</th></tr>
<tr><td>Open</td><td><i>File → Open</i> (<kbd>Ctrl</kbd>+<kbd>O</kbd>), double-click a .md file, or drag a file onto the window.</td></tr>
<tr><td>Recent files</td><td><i>File → Open recent</i> keeps the last 12 files.</td></tr>
<tr><td>Save / Save as</td><td><kbd>Ctrl</kbd>+<kbd>S</kbd> / <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd>. New files are saved as UTF-8 without BOM, the same as GitHub; existing files keep their encoding and line endings.</td></tr>
<tr><td>New / New window</td><td><kbd>Ctrl</kbd>+<kbd>N</kbd> starts an empty document; <i>New window</i> opens a second Gryphmark.</td></tr>
<tr><td>Show in folder</td><td>Opens Explorer with the file selected.</td></tr></table>
<p>If you close a document with unsaved changes, Gryphmark asks whether to save them.</p>
<h3>Images and links</h3>
<p>Relative paths work as on GitHub: <code>![diagram](img/diagram.png)</code> shows the image stored next to the .md file.
In GitHub preview, links to web pages open in your browser, links to other <code>.md</code> files open in Gryphmark, and
<code>#heading</code> links jump inside the document.</p>
"""),
   ("Export and print", """
<p><b>Export to HTML</b> saves a single, self-contained HTML page with the GitHub style embedded — ready to e-mail or publish.</p>
<p><b>Print / PDF</b> (<kbd>Ctrl</kbd>+<kbd>P</kbd>) prints the GitHub-style page. Choose <i>Save as PDF</i> as the printer to create a PDF.</p>
"""),
   ("Working tools", """
<table><tr><th>Feature</th><th>How it works</th></tr>
<tr><td>Find / Replace</td><td><kbd>Ctrl</kbd>+<kbd>F</kbd> / <kbd>Ctrl</kbd>+<kbd>H</kbd> open a bar above the document. <kbd>F3</kbd> and <kbd>Shift</kbd>+<kbd>F3</kbd> jump between matches; <kbd>Esc</kbd> closes it. <i>Aa</i> matches case.</td></tr>
<tr><td>Outline</td><td><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>O</kbd> or the outline button of the toolbar shows the headings on the left; click one to jump to it.</td></tr>
<tr><td>Your formatting is kept</td><td>Only the lines you change are rewritten when you save: list markers, <code>__bold__</code>, table alignment, line endings (CRLF/LF) and BOM stay as they were.</td></tr>
<tr><td>Changed outside</td><td>If another program modifies the open file, Gryphmark reloads it (or asks first if you have unsaved changes).</td></tr>
<tr><td>Crash recovery</td><td>Unsaved work is backed up every 20 seconds. If Gryphmark or Windows stops unexpectedly, the next start offers to recover it.</td></tr>
<tr><td>Spell check</td><td><i>View → Spell check</i> underlines misspelled words (Windows dictionaries).</td></tr>
<tr><td>One window per file</td><td>Opening a .md while Gryphmark is running brings the existing window forward instead of starting a second copy. <i>File → New window</i> still opens another one.</td></tr>
<tr><td>Updates</td><td>Gryphmark checks once a day for a new version; <i>Help → Check for updates</i> does it on demand.</td></tr>
<tr><td>About</td><td><i>Help → About Gryphmark</i> shows the version, author, license and credits. <i>Copy info</i> copies the version details, handy for reporting a problem.</td></tr>
<tr><td>Sample document</td><td>A sample that shows every feature in the three views. It opens on the first start and from <i>Help → Sample document</i>, and it is pinned at the top of <i>File → Open recent</i>; <i>Hide sample</i>, right below it, removes it from that list, and <i>Help → Show sample in Open recent</i> brings it back. It opens as an untitled copy, so the original is never overwritten, and it changes language together with the menus.</td></tr></table>
<h3>GitHub extras</h3>
<p>Also rendered exactly like GitHub: alerts (<code>&gt; [!NOTE]</code>, <code>[!TIP]</code>, <code>[!IMPORTANT]</code>, <code>[!WARNING]</code>, <code>[!CAUTION]</code>),
<code>```mermaid</code> diagrams, math (<code>$E=mc^2$</code> and <code>$$ … $$</code>), footnotes (<code>[^1]</code>) and emoji shortcodes (<code>:rocket:</code>).</p>
"""),
   ("Language, theme and zoom", f"""
<p><b>Language</b> menu: English or Español. It changes the menus, the toolbar tooltips and dialogs immediately.
The first time, Gryphmark follows the Windows language.</p>
<p><b>View → Dark theme</b> switches between GitHub light and GitHub dark. Zoom with <kbd>Ctrl</kbd>+<kbd>+</kbd>,
<kbd>Ctrl</kbd>+<kbd>-</kbd> and <kbd>Ctrl</kbd>+<kbd>0</kbd>. Language, theme, mode, zoom and window size are remembered.</p>
<figure><img src="{img('preview_dark_es.png')}"><figcaption>GitHub dark theme.</figcaption></figure>
"""),
   ("Keyboard shortcuts", """
<table><tr><th>Keys</th><th>Action</th><th>Keys</th><th>Action</th></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>N</kbd></td><td>New</td><td><kbd>Ctrl</kbd>+<kbd>B</kbd></td><td>Bold</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>O</kbd></td><td>Open</td><td><kbd>Ctrl</kbd>+<kbd>I</kbd></td><td>Italic</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>S</kbd></td><td>Save</td><td><kbd>Ctrl</kbd>+<kbd>Z</kbd> / <kbd>Ctrl</kbd>+<kbd>Y</kbd></td><td>Undo / Redo</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd></td><td>Save as</td><td><kbd>Ctrl</kbd>+<kbd>1</kbd>/<kbd>2</kbd>/<kbd>3</kbd></td><td>Visual / Source / GitHub</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>P</kbd></td><td>Print / PDF</td><td><kbd>Ctrl</kbd>+<kbd>+</kbd> / <kbd>-</kbd> / <kbd>0</kbd></td><td>Zoom</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>W</kbd></td><td>Close window</td><td><kbd>F1</kbd></td><td>This manual</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>F</kbd> / <kbd>H</kbd></td><td>Find / Replace</td><td><kbd>F3</kbd> / <kbd>Shift</kbd>+<kbd>F3</kbd></td><td>Next / previous match</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>O</kbd></td><td>Outline</td><td><kbd>Esc</kbd></td><td>Close find bar</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>E</kbd></td><td>Files panel</td><td><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>L</kbd></td><td>Read only / Edit</td></tr></table>
"""),
   ("Technical information", """
<table><tr><th>Item</th><th>Detail</th></tr>
<tr><td>Version</td><td>{v}</td></tr>
<tr><td>License</td><td>GNU GPL version 2 only (GPL-2.0-only) — free software, no warranty. Full text: <code>LICENSE.txt</code> in the program folder.</td></tr>
<tr><td>Platform</td><td>.NET 8 (WPF, self-contained) + Microsoft Edge WebView2</td></tr>
<tr><td>Editor</td><td>TOAST UI Editor 3.2 (MIT)</td></tr>
<tr><td>GitHub rendering</td><td>marked (GitHub Flavored Markdown), highlight.js, github-markdown-css (MIT)</td></tr>
<tr><td>Icon</td><td>Griffin from Martin Schongauer's engraving (c. 1485), The Metropolitan Museum of Art, CC0; quill added.</td></tr>
<tr><td>Settings</td><td><code>%APPDATA%\\Gryphmark\\settings.json</code></td></tr>
<tr><td>Source code</td><td><code>src/</code> in the project folder; build with <code>build.ps1</code>.</td></tr></table>
<div class="note">The visual editor normalises some details when it writes Markdown (for example, <code>*</code> lists become
<code>-</code>). The text and the way GitHub displays it do not change.</div>
"""),
  ]),
 "es": dict(
  title="Manual de usuario", tag="Escribí Markdown en forma visual. Miralo como en GitHub.", lang="es",
  toc="Contenido",
  sections=[
   ("Qué es Gryphmark", f"""
<p><b>Gryphmark</b> es un visor y editor de Markdown para Windows. Das formato con botones —títulos, negrita, listas,
tablas, código— y Gryphmark escribe por detrás las etiquetas de Markdown. Cada documento se ve exactamente como
lo muestra GitHub, con tema claro u oscuro.</p>
<p>Está pensado para archivos <code>README.md</code>, notas, documentación y cualquier <code>.md</code> que quieras leer o editar
sin aprenderte la sintaxis.</p>
<figure><img src="{img('visual_light_es.png')}"><figcaption>Editor visual: formato con la barra de herramientas, sin etiquetas a la vista.</figcaption></figure>
"""),
   ("Instalación", """
<p>Ejecutá <code>Gryphmark-Setup-{v}.exe</code>. El instalador está en inglés y en español y <b>no</b> pide permisos de
administrador: instala para el usuario actual en <code>%LOCALAPPDATA%\\Programs\\Gryphmark</code>.</p>
<table><tr><th>Opción</th><th>Qué hace</th></tr>
<tr><td>Acceso directo en el escritorio</td><td>Pone el ícono de Gryphmark en el escritorio.</td></tr>
<tr><td>Abrir los .md con Gryphmark</td><td>Agrega Gryphmark a <i>Abrir con</i> para los .md / .markdown y lo deja como programa para esos archivos en el usuario actual.</td></tr>
<tr><td>Agregar «Abrir con Gryphmark» a las carpetas</td><td>Clic derecho sobre una carpeta (o sobre el espacio vacío dentro de ella) en el Explorador para abrirla en el panel de archivos. En Windows 11 está en <i>Mostrar más opciones</i>.</td></tr></table>
<p>Requisitos: Windows 10 u 11 de 64 bits y Microsoft Edge WebView2 Runtime, que ya viene con Windows 11.
El runtime de .NET va incluido; no hay que instalar nada más.</p>
<p>Hasta la versión 1.1.1 el programa se llamaba <b>Quillforge</b>. Si esa versión está instalada, el instalador la quita
y Gryphmark copia su configuración, los recientes y los borradores de recuperación la primera vez que arranca.</p>
<p>Para desinstalar: <i>Configuración → Aplicaciones → Aplicaciones instaladas → Gryphmark → Desinstalar</i>. La
configuración en <code>%APPDATA%\\Gryphmark</code> se conserva.</p>
"""),
   ("La ventana", """
<table><tr><th>Parte</th><th>Para qué sirve</th></tr>
<tr><td>Menú</td><td>Archivo, Edición, Formato, Ver, Idioma y Ayuda. Todos los menús están en inglés y en español.</td></tr>
<tr><td>Barra de herramientas</td><td>Los botones de formato (ver más abajo). No aparece en la vista GitHub, que es de solo lectura.</td></tr>
<tr><td>Documento</td><td>La página, con el mismo ancho de columna, letras y espaciado que github.com.</td></tr>
<tr><td>Barra de estado</td><td>Ruta del archivo, cantidad de palabras, modo actual y si hay cambios sin guardar.</td></tr></table>
<p>La barra de título muestra el nombre del archivo; un <b>●</b> delante indica que hay cambios sin guardar.</p>
"""),
   ("Tres formas de ver un documento", f"""
<p>Se cambia con los tres botones del extremo derecho de la barra — <b>Visual · Markdown · GitHub</b> —, con el menú <i>Ver</i> o con los atajos. Pasá el mouse sobre un botón para ver qué hace.</p>
<table><tr><th>Botón</th><th>Atajo</th><th>Qué ves</th></tr>
<tr><td><b>Visual</b><br><small>Editor visual</small></td><td><kbd>Ctrl</kbd>+<kbd>1</kbd></td><td>El documento con formato. Escribís y usás los botones; el Markdown se genera solo.</td></tr>
<tr><td><b>Markdown</b><br><small>Código Markdown</small></td><td><kbd>Ctrl</kbd>+<kbd>2</kbd></td><td>El Markdown a la izquierda y la vista previa en vivo a la derecha. Para ver o tocar las etiquetas.</td></tr>
<tr><td><b>GitHub</b><br><small>Vista GitHub</small></td><td><kbd>Ctrl</kbd>+<kbd>3</kbd></td><td>Página de solo lectura igual a GitHub, con colores de código, listas de tareas y enlaces que funcionan.</td></tr></table>
<figure><img src="{img('source_light_es.png')}"><figcaption>Código Markdown con vista previa en vivo.</figcaption></figure>
<figure><img src="{img('preview_light_es.png')}"><figcaption>Vista GitHub (tema claro).</figcaption></figure>
<h3>Solo lectura</h3>
<p>Cuando sólo querés leer un documento, elegí <b>Solo lectura</b> en el selector <b>Editar | Solo lectura</b>, a la izquierda de los botones de vista
(también <i>Ver → Solo lectura</i> o <kbd>Ctrl</kbd>+<kbd>Mayús</kbd>+<kbd>L</kbd>). El texto queda bloqueado en todas las vistas: las teclas, pegar, arrastrar y soltar
y las casillas de tareas no hacen nada, y los botones de formato quedan en gris. Igual podés leer, seleccionar, copiar, buscar y cambiar de vista.
Si escribís, la barra de estado te recuerda cómo desbloquearlo. Elegí <b>Editar</b> para volver a escribir; Gryphmark recuerda tu elección.</p>
"""),
   ("Carpetas: el panel de archivos", f"""
<p>Como VS Code u Obsidian, Gryphmark puede abrir una carpeta entera y mostrar su árbol a la izquierda: subcarpetas y
archivos Markdown (<code>.md</code>, <code>.markdown</code>). Hacés clic en un archivo y se abre al costado del árbol.</p>
<figure><img src="{img('files_panel_light.png')}" style="width:260px"><figcaption>El panel de archivos con una carpeta abierta; el archivo abierto queda marcado.</figcaption></figure>
<table><tr><th>Acción</th><th>Cómo</th></tr>
<tr><td>Abrir una carpeta</td><td><i>Archivo → Abrir carpeta…</i>, el botón <b>Abrir carpeta…</b> del panel vacío, arrastrando una carpeta a la ventana, o con clic derecho sobre una carpeta en el Explorador → <i>Abrir con Gryphmark</i> (opción del instalador; en Windows 11 está en <i>Mostrar más opciones</i>).</td></tr>
<tr><td>Mostrar / ocultar el panel</td><td><kbd>Ctrl</kbd>+<kbd>Mayús</kbd>+<kbd>E</kbd>, el botón de carpeta a la izquierda de la barra, o <i>Ver → Panel de archivos</i>. Arrastrando su borde derecho se ensancha.</td></tr>
<tr><td>Recorrer</td><td>Clic en una carpeta la abre o la cierra; clic en un archivo lo abre. <kbd>Enter</kbd> hace lo mismo sobre la fila seleccionada.</td></tr>
<tr><td>Clic derecho</td><td>Sobre un archivo: abrir, abrir en otra ventana, mostrar en la carpeta, copiar ruta. Sobre una carpeta: mostrar en la carpeta, copiar ruta, actualizar.</td></tr>
<tr><td>Botones del panel</td><td>Actualizar, contraer todo, cerrar carpeta.</td></tr></table>
<p>El panel sigue al disco: los archivos y carpetas que otros programas crean, renombran o borran aparecen enseguida.
El archivo abierto queda marcado en el árbol. Las carpetas ocultas y las de herramientas (<code>.git</code>,
<code>node_modules</code>, <code>bin</code>, <code>obj</code>…) no se muestran. La carpeta se vuelve a abrir la próxima vez
que arranca Gryphmark, y las carpetas recientes aparecen en <i>Archivo → Abrir reciente</i>.</p>
"""),
   ("Barra de formato", """
<table><tr><th>Botón</th><th>Acción</th><th>Markdown que escribe</th></tr>
<tr><td>H</td><td>Título 1 a 6 o párrafo normal</td><td><code># Título</code> … <code>###### Título</code></td></tr>
<tr><td><b>B</b></td><td>Negrita</td><td><code>**texto**</code></td></tr>
<tr><td><i>I</i></td><td>Cursiva</td><td><code>*texto*</code></td></tr>
<tr><td><s>S</s></td><td>Tachado</td><td><code>~~texto~~</code></td></tr>
<tr><td>—</td><td>Línea horizontal</td><td><code>***</code></td></tr>
<tr><td>❝</td><td>Cita</td><td><code>&gt; texto</code></td></tr>
<tr><td>• lista / 1. lista</td><td>Lista con viñetas / numerada</td><td><code>- ítem</code> / <code>1. ítem</code></td></tr>
<tr><td>☑</td><td>Lista de tareas</td><td><code>- [ ] tarea</code></td></tr>
<tr><td>→ / ←</td><td>Aumentar / reducir sangría</td><td>lista anidada</td></tr>
<tr><td>Tabla</td><td>Insertar tabla (elegís filas × columnas)</td><td><code>| a | b |</code></td></tr>
<tr><td>Imagen</td><td>Insertar imagen por URL o nombre de archivo</td><td><code>![alt](archivo.png)</code></td></tr>
<tr><td>Enlace</td><td>Insertar enlace</td><td><code>[texto](https://…)</code></td></tr>
<tr><td>&lt;/&gt;</td><td>Código en línea</td><td><code>`código`</code></td></tr>
<tr><td>CB</td><td>Bloque de código (elegís el lenguaje para los colores)</td><td><code>```python</code></td></tr></table>
<p>Las mismas acciones están en el menú <b>Formato</b>. Con clic derecho dentro de una tabla se agregan o borran filas y columnas, o se alinea una columna.</p>
"""),
   ("Archivos", """
<table><tr><th>Acción</th><th>Cómo</th></tr>
<tr><td>Abrir</td><td><i>Archivo → Abrir</i> (<kbd>Ctrl</kbd>+<kbd>O</kbd>), doble clic en un .md, o arrastrar el archivo a la ventana.</td></tr>
<tr><td>Recientes</td><td><i>Archivo → Abrir reciente</i> guarda los últimos 12.</td></tr>
<tr><td>Guardar / Guardar como</td><td><kbd>Ctrl</kbd>+<kbd>S</kbd> / <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd>. Los archivos nuevos se guardan en UTF-8 sin BOM, igual que GitHub; los existentes conservan su codificación y finales de línea.</td></tr>
<tr><td>Nuevo / Nueva ventana</td><td><kbd>Ctrl</kbd>+<kbd>N</kbd> empieza un documento vacío; <i>Nueva ventana</i> abre otro Gryphmark.</td></tr>
<tr><td>Mostrar en la carpeta</td><td>Abre el Explorador con el archivo seleccionado.</td></tr></table>
<p>Si cerrás un documento con cambios sin guardar, Gryphmark pregunta si querés guardarlos.</p>
<h3>Imágenes y enlaces</h3>
<p>Las rutas relativas funcionan como en GitHub: <code>![diagrama](img/diagrama.png)</code> muestra la imagen que está junto al .md.
En la vista GitHub, los enlaces web se abren en el navegador, los enlaces a otros <code>.md</code> se abren en Gryphmark y los
<code>#título</code> saltan dentro del documento.</p>
"""),
   ("Exportar e imprimir", """
<p><b>Exportar a HTML</b> guarda una sola página HTML autónoma con el estilo de GitHub adentro, lista para mandar por mail o publicar.</p>
<p><b>Imprimir / PDF</b> (<kbd>Ctrl</kbd>+<kbd>P</kbd>) imprime la página con estilo GitHub. Elegí <i>Guardar como PDF</i> como impresora para sacar un PDF.</p>
"""),
   ("Herramientas de trabajo", """
<table><tr><th>Función</th><th>Cómo funciona</th></tr>
<tr><td>Buscar / Reemplazar</td><td><kbd>Ctrl</kbd>+<kbd>F</kbd> / <kbd>Ctrl</kbd>+<kbd>H</kbd> abren una barra sobre el documento. <kbd>F3</kbd> y <kbd>Mayús</kbd>+<kbd>F3</kbd> saltan entre coincidencias; <kbd>Esc</kbd> la cierra. <i>Aa</i> distingue mayúsculas.</td></tr>
<tr><td>Esquema</td><td><kbd>Ctrl</kbd>+<kbd>Mayús</kbd>+<kbd>O</kbd> o el botón de esquema de la barra muestra los títulos a la izquierda; un clic salta a ese título.</td></tr>
<tr><td>Se respeta tu formato</td><td>Al guardar sólo se reescriben las líneas que cambiaste: viñetas, <code>__negrita__</code>, alineación de tablas, finales de línea (CRLF/LF) y BOM quedan como estaban.</td></tr>
<tr><td>Cambios externos</td><td>Si otro programa modifica el archivo abierto, Gryphmark lo recarga (o pregunta antes si tenés cambios sin guardar).</td></tr>
<tr><td>Recuperación</td><td>Lo no guardado se respalda cada 20 segundos. Si Gryphmark o Windows se cierran de golpe, al volver a abrir ofrece recuperarlo.</td></tr>
<tr><td>Ortografía</td><td><i>Ver → Revisar ortografía</i> subraya las palabras mal escritas (diccionarios de Windows).</td></tr>
<tr><td>Una sola ventana</td><td>Abrir un .md con Gryphmark ya abierto trae al frente la ventana existente en vez de abrir otra copia. <i>Archivo → Nueva ventana</i> sigue abriendo otra.</td></tr>
<tr><td>Actualizaciones</td><td>Gryphmark busca una versión nueva una vez por día; <i>Ayuda → Buscar actualizaciones</i> lo hace en el momento.</td></tr>
<tr><td>Acerca de</td><td><i>Ayuda → Acerca de Gryphmark</i> muestra la versión, el autor, la licencia y los créditos. <i>Copiar info</i> copia los datos de versión, útil para reportar un problema.</td></tr>
<tr><td>Documento de ejemplo</td><td>Un ejemplo que muestra todas las funciones en las tres vistas. Se abre la primera vez que arrancás el programa y desde <i>Ayuda → Documento de ejemplo</i>, y está fijo arriba de todo en <i>Archivo → Abrir reciente</i>; <i>Ocultar ejemplo</i>, justo debajo, lo saca de esa lista, y <i>Ayuda → Mostrar el ejemplo en Abrir reciente</i> lo vuelve a poner. Se abre como copia sin título, así que el original nunca se pisa, y cambia de idioma junto con los menús.</td></tr></table>
<h3>Extras de GitHub</h3>
<p>También se muestran igual que en GitHub: avisos (<code>&gt; [!NOTE]</code>, <code>[!TIP]</code>, <code>[!IMPORTANT]</code>, <code>[!WARNING]</code>, <code>[!CAUTION]</code>),
diagramas <code>```mermaid</code>, fórmulas (<code>$E=mc^2$</code> y <code>$$ … $$</code>), notas al pie (<code>[^1]</code>) y emojis por código (<code>:rocket:</code>).</p>
"""),
   ("Idioma, tema y zoom", f"""
<p>Menú <b>Idioma</b>: English o Español. Cambia al instante los menús, las ayudas de la barra y los diálogos.
La primera vez, Gryphmark toma el idioma de Windows.</p>
<p><b>Ver → Tema oscuro</b> alterna entre GitHub claro y GitHub oscuro. Zoom con <kbd>Ctrl</kbd>+<kbd>+</kbd>,
<kbd>Ctrl</kbd>+<kbd>-</kbd> y <kbd>Ctrl</kbd>+<kbd>0</kbd>. Idioma, tema, modo, zoom y tamaño de ventana quedan recordados.</p>
<figure><img src="{img('preview_dark_es.png')}"><figcaption>Tema oscuro de GitHub.</figcaption></figure>
"""),
   ("Atajos de teclado", """
<table><tr><th>Teclas</th><th>Acción</th><th>Teclas</th><th>Acción</th></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>N</kbd></td><td>Nuevo</td><td><kbd>Ctrl</kbd>+<kbd>B</kbd></td><td>Negrita</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>O</kbd></td><td>Abrir</td><td><kbd>Ctrl</kbd>+<kbd>I</kbd></td><td>Cursiva</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>S</kbd></td><td>Guardar</td><td><kbd>Ctrl</kbd>+<kbd>Z</kbd> / <kbd>Ctrl</kbd>+<kbd>Y</kbd></td><td>Deshacer / Rehacer</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd></td><td>Guardar como</td><td><kbd>Ctrl</kbd>+<kbd>1</kbd>/<kbd>2</kbd>/<kbd>3</kbd></td><td>Visual / Código / GitHub</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>P</kbd></td><td>Imprimir / PDF</td><td><kbd>Ctrl</kbd>+<kbd>+</kbd> / <kbd>-</kbd> / <kbd>0</kbd></td><td>Zoom</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>W</kbd></td><td>Cerrar ventana</td><td><kbd>F1</kbd></td><td>Este manual</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>F</kbd> / <kbd>H</kbd></td><td>Buscar / Reemplazar</td><td><kbd>F3</kbd> / <kbd>Mayús</kbd>+<kbd>F3</kbd></td><td>Coincidencia siguiente / anterior</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>Mayús</kbd>+<kbd>O</kbd></td><td>Esquema</td><td><kbd>Esc</kbd></td><td>Cerrar búsqueda</td></tr>
<tr><td><kbd>Ctrl</kbd>+<kbd>Mayús</kbd>+<kbd>E</kbd></td><td>Panel de archivos</td><td><kbd>Ctrl</kbd>+<kbd>Mayús</kbd>+<kbd>L</kbd></td><td>Solo lectura / Editar</td></tr></table>
"""),
   ("Información técnica", """
<table><tr><th>Ítem</th><th>Detalle</th></tr>
<tr><td>Versión</td><td>{v}</td></tr>
<tr><td>Licencia</td><td>GNU GPL versión 2 únicamente (GPL-2.0-only) — software libre, sin garantía. Texto completo: <code>LICENSE.txt</code> en la carpeta del programa.</td></tr>
<tr><td>Plataforma</td><td>.NET 8 (WPF, autónomo) + Microsoft Edge WebView2</td></tr>
<tr><td>Editor</td><td>TOAST UI Editor 3.2 (MIT)</td></tr>
<tr><td>Render GitHub</td><td>marked (GitHub Flavored Markdown), highlight.js, github-markdown-css (MIT)</td></tr>
<tr><td>Ícono</td><td>Grifo del grabado de Martin Schongauer (c. 1485), Museo Metropolitano de Arte, CC0; pluma agregada.</td></tr>
<tr><td>Configuración</td><td><code>%APPDATA%\\Gryphmark\\settings.json</code></td></tr>
<tr><td>Código fuente</td><td><code>src/</code> en la carpeta del proyecto; se compila con <code>build.ps1</code>.</td></tr></table>
<div class="note">El editor visual normaliza algunos detalles al escribir el Markdown (por ejemplo, las listas con <code>*</code>
pasan a <code>-</code>). El texto y cómo lo muestra GitHub no cambian.</div>
"""),
  ]),
}

for code, d in T.items():
    toc = "".join(f"<div>{i}. {name}</div>" for i, (name, _) in enumerate(d["sections"], 1))
    body = "".join(f"<h2>{i}. {name}</h2>{html.replace('{v}', version)}" for i, (name, html) in enumerate(d["sections"], 1))
    page = f"""<!doctype html><html lang="{d['lang']}"><head><meta charset="utf-8"><title>Gryphmark — {d['title']}</title><style>{CSS}</style></head>
<body>
<div class="cover"><img src="{icon}"><h1>Gryphmark</h1><div class="tag">{d['tag']}</div>
<div class="meta">{d['title']} · v{version}<br>© 2026 Gabriel Castiglia</div></div>
<h2>{d['toc']}</h2><div class="toc">{toc}</div>
{body}
</body></html>"""
    (docs / f"Gryphmark_Manual_{code.upper()}.html").write_text(page, encoding="utf-8")
print("ok")
