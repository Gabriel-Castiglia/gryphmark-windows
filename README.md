<p align="center"><img src="assets/quillforge.png" width="140" alt="Quillforge"></p>

<h1 align="center">Quillforge</h1>
<p align="center"><b>Forge your Markdown. See it like GitHub.</b><br><i>Forjá tu Markdown. Miralo como en GitHub.</i></p>

---

## English

Quillforge is a Markdown viewer and editor for Windows. Format with buttons — headings, bold, lists, tables, code — and
Quillforge writes the Markdown tags for you. Documents are rendered exactly like GitHub, in light or dark theme.

- Visual editor, Markdown source with live preview, and read-only GitHub preview (`Ctrl+1/2/3`)
- English and Spanish interface, switchable at runtime
- Relative images and links, `.md` links open in Quillforge
- GitHub alerts, Mermaid diagrams, math, footnotes and emoji
- Find & replace, outline sidebar, spell check
- Keeps your formatting: only edited lines change on save (CRLF/BOM preserved)
- Reloads files changed outside, crash recovery, single instance, update check
- Export to self-contained HTML, print / save as PDF
- Per-user installer (no admin rights), optional `.md` association
- Manuals: [`docs/Quillforge_Manual_EN.pdf`](docs/Quillforge_Manual_EN.pdf)

**Build:** Windows, .NET 8 SDK, Node.js, Python 3 + Pillow/numpy/scipy/opencv, Inno Setup 6, Microsoft Edge.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -Version 1.1.0
# -> out/Quillforge-Setup-1.1.0.exe
```

## Español

Quillforge es un visor y editor de Markdown para Windows. Das formato con botones —títulos, negrita, listas, tablas,
código— y Quillforge escribe las etiquetas de Markdown por vos. Los documentos se ven exactamente como en GitHub, con
tema claro u oscuro.

- Editor visual, código Markdown con vista previa en vivo y vista GitHub de solo lectura (`Ctrl+1/2/3`)
- Interfaz en inglés y en español, se cambia sin reiniciar
- Imágenes y enlaces relativos; los enlaces a `.md` se abren en Quillforge
- Avisos de GitHub, diagramas Mermaid, fórmulas, notas al pie y emojis
- Buscar y reemplazar, esquema lateral, revisión ortográfica
- Respeta tu formato: al guardar sólo cambian las líneas editadas (conserva CRLF/BOM)
- Recarga archivos cambiados afuera, recuperación ante cierres, instancia única, aviso de actualizaciones
- Exportar a HTML autónomo, imprimir o guardar como PDF
- Instalador por usuario (sin permisos de administrador), asociación opcional de `.md`
- Manuales: [`docs/Quillforge_Manual_ES.pdf`](docs/Quillforge_Manual_ES.pdf)

**Compilar:** Windows, SDK de .NET 8, Node.js, Python 3 + Pillow/numpy/scipy/opencv, Inno Setup 6, Microsoft Edge.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -Version 1.1.0
# -> out/Quillforge-Setup-1.1.0.exe
```

## Project layout / Estructura

| Path | Content |
|---|---|
| `src/Quillforge/` | WPF + WebView2 app (C#) and `wwwroot/` editor page |
| `_npm/` | Web dependencies and esbuild entry (`entry.js`) |
| `installer/` | Inno Setup script and wizard art |
| `tools/` | Icon, wizard art and manual generators |
| `docs/` | Manuals (HTML + PDF, EN/ES) and screenshots |
| `out/` | Build output: publish folder and installer |

## License / Licencia

Copyright (C) 2026 Gabriel Castiglia.

Quillforge is free software: you can redistribute it and/or modify it under the terms of the GNU General
Public License, version 2 only (GPL-2.0-only), as published by the Free Software Foundation. It is
distributed WITHOUT ANY WARRANTY. See [`LICENSE`](LICENSE) for the full text.

Quillforge es software libre: podés redistribuirlo y/o modificarlo bajo los términos de la GNU General
Public License, versión 2 únicamente (GPL-2.0-only). Se distribuye SIN NINGUNA GARANTÍA. El texto completo
está en [`LICENSE`](LICENSE).

## Credits / Créditos

| Component | License |
|---|---|
| TOAST UI Editor, ProseMirror, marked (+ alert, footnote, katex extensions), github-markdown-css, Mermaid, KaTeX, gemoji | MIT |
| highlight.js, jsdiff | BSD-3-Clause |
| .NET 8 / WPF | MIT |
| Microsoft Edge WebView2 SDK | Microsoft WebView2 SDK license (redistributable); the WebView2 Runtime is a Windows system component |
| Icon / ícono: griffin from Martin Schongauer's engraving (c. 1485), The Metropolitan Museum of Art — quill added | CC0 |
