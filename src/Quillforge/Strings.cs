namespace Quillforge;

/// <summary>All user-visible text, English and Spanish.</summary>
public static class Strings
{
    private static readonly Dictionary<string, (string En, string Es)> T = new()
    {
        ["File"] = ("_File", "_Archivo"),
        ["New"] = ("_New", "_Nuevo"),
        ["NewWindow"] = ("New _window", "Nueva _ventana"),
        ["Open"] = ("_Open…", "_Abrir…"),
        ["Recent"] = ("Open _recent", "Abrir _reciente"),
        ["RecentEmpty"] = ("(empty)", "(vacío)"),
        ["RecentClear"] = ("Clear list", "Borrar lista"),
        ["Save"] = ("_Save", "_Guardar"),
        ["SaveAs"] = ("Save _as…", "Guardar _como…"),
        ["ExportHtml"] = ("_Export to HTML…", "_Exportar a HTML…"),
        ["Print"] = ("_Print / PDF…", "_Imprimir / PDF…"),
        ["OpenFolder"] = ("Show in _folder", "Mostrar en la _carpeta"),
        ["Exit"] = ("E_xit", "_Salir"),
        ["Edit"] = ("_Edit", "_Edición"),
        ["Undo"] = ("_Undo", "_Deshacer"),
        ["Redo"] = ("_Redo", "_Rehacer"),
        ["Format"] = ("F_ormat", "_Formato"),
        ["Bold"] = ("_Bold", "_Negrita"),
        ["Italic"] = ("_Italic", "_Cursiva"),
        ["Strike"] = ("_Strikethrough", "_Tachado"),
        ["Code"] = ("Inline _code", "_Código en línea"),
        ["Heading"] = ("_Heading", "_Título"),
        ["Paragraph"] = ("_Paragraph", "_Párrafo"),
        ["Quote"] = ("_Quote", "C_ita"),
        ["Ul"] = ("_Bulleted list", "Lista con _viñetas"),
        ["Ol"] = ("_Numbered list", "Lista _numerada"),
        ["Task"] = ("_Task list", "Lista de _tareas"),
        ["CodeBlock"] = ("Code _block", "_Bloque de código"),
        ["Hr"] = ("Horizontal _rule", "_Línea horizontal"),
        ["View"] = ("_View", "_Ver"),
        ["ModeVisual"] = ("_Visual editor", "Editor _visual"),
        ["ModeSource"] = ("_Markdown source", "Código _Markdown"),
        ["ModePreview"] = ("_GitHub preview", "Vista _GitHub"),
        ["Dark"] = ("_Dark theme", "Tema _oscuro"),
        ["ZoomIn"] = ("Zoom _in", "_Acercar"),
        ["ZoomOut"] = ("Zoom _out", "_Alejar"),
        ["ZoomReset"] = ("_Reset zoom", "_Restablecer zoom"),
        ["Language"] = ("_Language", "_Idioma"),
        ["Help"] = ("_Help", "Ay_uda"),
        ["Guide"] = ("Markdown _guide (GitHub)", "_Guía de Markdown (GitHub)"),
        ["Manual"] = ("User _manual", "_Manual de usuario"),
        ["About"] = ("_About Quillforge", "_Acerca de Quillforge"),
        ["Untitled"] = ("Untitled", "Sin título"),
        ["MdFilter"] = ("Markdown (*.md;*.markdown)|*.md;*.markdown;*.mdown;*.mkd|Text (*.txt)|*.txt|All files (*.*)|*.*",
                        "Markdown (*.md;*.markdown)|*.md;*.markdown;*.mdown;*.mkd|Texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*"),
        ["HtmlFilter"] = ("HTML page (*.html)|*.html", "Página HTML (*.html)|*.html"),
        ["AskSave"] = ("Do you want to save the changes to “{0}”?", "¿Querés guardar los cambios en «{0}»?"),
        ["NotFound"] = ("The file no longer exists:\n{0}", "El archivo ya no existe:\n{0}"),
        ["ErrOpen"] = ("Could not open the file:\n{0}", "No se pudo abrir el archivo:\n{0}"),
        ["ErrSave"] = ("Could not save the file:\n{0}", "No se pudo guardar el archivo:\n{0}"),
        ["NoWebView"] = ("Microsoft Edge WebView2 Runtime is required.\nInstall it from https://go.microsoft.com/fwlink/p/?LinkId=2124703",
                         "Hace falta Microsoft Edge WebView2 Runtime.\nInstalalo desde https://go.microsoft.com/fwlink/p/?LinkId=2124703"),
        ["AboutText"] = ("Quillforge {0}\n\nForge your Markdown visually; it writes the tags for you,\nand shows the result exactly like GitHub.\n\n© 2026 Gabriel Castiglia\nEditor: TOAST UI Editor (MIT) · Rendering: marked, highlight.js, github-markdown-css (MIT)",
                         "Quillforge {0}\n\nForjá tu Markdown en forma visual: las etiquetas las escribe él,\ny ves el resultado tal cual en GitHub.\n\n© 2026 Gabriel Castiglia\nEditor: TOAST UI Editor (MIT) · Render: marked, highlight.js, github-markdown-css (MIT)"),
        ["Modified"] = ("Modified", "Modificado"),
        ["Saved"] = ("Saved", "Guardado"),
        ["Words"] = ("{0} words", "{0} palabras"),
    };

    public static string Lang { get; set; } = "en";

    public static string Get(string key) =>
        T.TryGetValue(key, out var v) ? (Lang == "es" ? v.Es : v.En) : key;

    public static string Plain(string key) => Get(key).Replace("_", "");
}
