// Vigia -- main window: where the Files, Contents and Search panes go.
// Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
// Date: November 20, 2026
// SPDX-License-Identifier: GPL-2.0-only

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Vigia;

/// <summary>The layout of the panes: Files, Contents and (from 2.2) Search. Each one goes to the left or to the right
/// of the text (Settings.FilesSide / OutlineSide / SearchSide); the panes on one side sit one above the other or side by
/// side (SideArrange for the left, RightArrange for the right), in the order of Settings.PaneOrder, with a divider
/// between each two. Each layout keeps its own sidebar width (Settings.LayoutWidths) and each pane its share of the
/// room (PaneWeights). It is chosen three ways: the layout button at the right of the toolbar (a drawing of every
/// layout, a click applies it), View → Layout, or dragging a pane by its title to an edge of the window, where the
/// place it would take is shown before it is dropped.</summary>
public partial class MainWindow
{
    private sealed record LayoutPreset(string Key, string Files, string Outline, string Arrange);

    private static readonly LayoutPreset[] Presets =
    {
        new("LayoutLeftRows", "left", "left", "rows"),
        new("LayoutLeftCols", "left", "left", "cols"),
        new("LayoutSplitFiles", "left", "right", "rows"),
        new("LayoutSplitOutline", "right", "left", "rows"),
        new("LayoutRightRows", "right", "right", "rows"),
        new("LayoutRightCols", "right", "right", "cols"),
    };

    private static readonly string[] PaneKeys = { "files", "outline", "search" };
    private Popup? _layoutPopup;

    private void InitLayout()
    {
        // 2.0 kept one of three layouts in SideLayout: turned into the new values once
        switch (_settings.SideLayout)
        {
            case "columns": _settings.FilesSide = "left"; _settings.OutlineSide = "left"; _settings.SideArrange = "cols"; break;
            case "right": _settings.FilesSide = "left"; _settings.OutlineSide = "right"; break;
            case "stacked": _settings.FilesSide = "left"; _settings.OutlineSide = "left"; _settings.SideArrange = "rows"; break;
        }
        _settings.SideLayout = "";
        // 2.1 → 2.2: one arrangement per side, an order of panes, a share per pane
        if (_settings.RightArrange == "") _settings.RightArrange = _settings.SideArrange;
        if (_settings.PaneOrder.Count == 0) _settings.PaneOrder = _settings.OutlineFirst ? new() { "outline", "files", "search" } : new() { "files", "outline", "search" };
        if (_settings.PaneWeights.Count == 0)
        {
            double s = Math.Clamp(_settings.OutlineShare, 0.1, 0.9);
            _settings.PaneWeights = new() { ["files"] = Math.Round(1 - s, 3), ["outline"] = Math.Round(s, 3), ["search"] = 0.5 };
        }
        _settings.Save();

        SideSplitter.DragCompleted += (_, _) => SaveSideWidth(false);
        RightSplitter.DragCompleted += (_, _) => SaveSideWidth(true);
        LayoutBtn.Click += (_, _) => ShowLayoutPicker();
        InitDocking();
    }

    // ------------------------------------------------------------------ the model
    private Border PaneOf(string key) => key switch { "files" => FilesPane, "outline" => OutlinePane, _ => SearchPane };
    private string KeyOf(Border pane) => pane == FilesPane ? "files" : pane == OutlinePane ? "outline" : "search";
    private bool PaneOn(string key) => key switch { "files" => _settings.FilesPanel, "outline" => _settings.Outline, _ => _settings.SearchPanel };
    private string SideOf(string key) => key switch { "files" => _settings.FilesSide, "outline" => _settings.OutlineSide, _ => _settings.SearchSide } == "right" ? "right" : "left";

    private void SetSideOf(string key, string side)
    {
        if (key == "files") _settings.FilesSide = side; else if (key == "outline") _settings.OutlineSide = side; else _settings.SearchSide = side;
    }

    private string ArrangeOf(string side) => (side == "right" ? _settings.RightArrange : _settings.SideArrange) == "cols" ? "cols" : "rows";

    private void SetArrange(string side, string arrange)
    {
        if (side == "right") _settings.RightArrange = arrange; else _settings.SideArrange = arrange;
    }

    /// <summary>Every pane once, in the saved order (anything unknown or missing is mended).</summary>
    private List<string> Order()
    {
        var o = _settings.PaneOrder.Where(PaneKeys.Contains).Distinct().ToList();
        foreach (var k in PaneKeys) if (!o.Contains(k)) o.Add(k);
        _settings.PaneOrder = o;
        return o;
    }

    private List<string> PanesOn(string side) => Order().Where(k => PaneOn(k) && SideOf(k) == side).ToList();

    private double Weight(string key) => _settings.PaneWeights.TryGetValue(key, out var w) && w > 0.01 ? w : key == "files" ? 0.55 : key == "outline" ? 0.45 : 0.5;

    /// <summary>What a sidebar holds, as the key of its saved width: side, panes in order, and how they sit.</summary>
    private string WidthKey(string side, List<string> panes) =>
        side + ":" + string.Join("+", panes) + (panes.Count > 1 ? ":" + ArrangeOf(side) : "");

    private double SideWidth(string side, List<string> panes)
    {
        bool cols = panes.Count > 1 && ArrangeOf(side) == "cols";
        double min = cols ? panes.Count * 150 + (panes.Count - 1) * 5 : 180, max = cols ? 1400 : 700;
        if (!_settings.LayoutWidths.TryGetValue(WidthKey(side, panes), out var w))
            w = cols ? _settings.SideWidthColumns * (panes.Count > 2 ? 1.4 : 1) : side == "right" ? _settings.OutlineWidthRight : _settings.FilesWidth;   // 2.1's widths
        return Math.Clamp(w, min, max);
    }

    private void SaveSideWidth(bool right)
    {
        string side = right ? "right" : "left";
        var w = (right ? RightColumn : SideColumn).ActualWidth;
        var panes = PanesOn(side);
        if (w <= 0 || panes.Count == 0) return;
        _settings.LayoutWidths[WidthKey(side, panes)] = Math.Round(w);
        _settings.Save();
    }

    /// <summary>After a divider between two panes moved: the share of each pane on that side.</summary>
    private void SavePaneWeights(string side)
    {
        var panes = PanesOn(side);
        bool cols = ArrangeOf(side) == "cols";
        var sizes = panes.Select(k => cols ? PaneOf(k).ActualWidth : PaneOf(k).ActualHeight).ToList();
        double total = sizes.Sum();
        if (total <= 0 || sizes.Any(s => s <= 0)) return;
        for (int i = 0; i < panes.Count; i++) _settings.PaneWeights[panes[i]] = Math.Round(sizes[i] / total, 3);
        _settings.Save();
        ApplySideLayout();
    }

    // ------------------------------------------------------------------ applying it
    /// <summary>Puts each pane in its sidebar and lays both sidebars out. A pane that is off takes no room; a
    /// sidebar with nothing in it disappears with its divider.</summary>
    private void ApplySideLayout()
    {
        foreach (var k in PaneKeys)
        {
            var pane = PaneOf(k);
            var target = SideOf(k) == "right" ? RightGrid : SideGrid;
            if (pane.Parent != target) { (pane.Parent as Panel)?.Children.Remove(pane); target.Children.Add(pane); }
            pane.Visibility = PaneOn(k) ? Visibility.Visible : Visibility.Collapsed;
        }
        ArrangeSide("left");
        ArrangeSide("right");
        LayoutBtn.Content = LayoutDrawing(CurrentState(), 26, 17, mini: true);
        LayoutBtn.ToolTip = Strings.Get("LayoutPickerTip");
    }

    private void ArrangeSide(string side)
    {
        bool right = side == "right";
        var g = right ? RightGrid : SideGrid;
        var col = right ? RightColumn : SideColumn;
        var splitCol = right ? RightSplitterColumn : SideSplitterColumn;
        var split = right ? RightSplitter : SideSplitter;
        var panes = PanesOn(side);
        int n = panes.Count;
        bool cols = n > 1 && ArrangeOf(side) == "cols";

        double w = n > 0 ? SideWidth(side, panes) : 0;
        col.MaxWidth = cols ? 1400 : 700;
        col.MinWidth = n == 0 ? 0 : cols ? n * 150 + (n - 1) * 5 : 180;
        col.Width = new GridLength(w);
        splitCol.Width = new GridLength(n > 0 ? 5 : 0);
        split.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        g.Visibility = split.Visibility;

        // the rows or columns of this side are made again: one per pane, a divider between each two
        foreach (var s in g.Children.OfType<GridSplitter>().ToList()) g.Children.Remove(s);
        g.RowDefinitions.Clear(); g.ColumnDefinitions.Clear();
        double total = panes.Sum(Weight);
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
            {
                if (cols) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) }); else g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
                var d = NewSplitter(cols);
                d.DragCompleted += (_, _) => SavePaneWeights(side);
                if (cols) Grid.SetColumn(d, 2 * i - 1); else Grid.SetRow(d, 2 * i - 1);
                g.Children.Add(d);
            }
            var star = new GridLength(Weight(panes[i]) / total, GridUnitType.Star);
            if (cols) g.ColumnDefinitions.Add(new ColumnDefinition { Width = star, MinWidth = 150 }); else g.RowDefinitions.Add(new RowDefinition { Height = star, MinHeight = 90 });
        }
        foreach (var k in PaneKeys)
        {
            var pane = PaneOf(k);
            if (pane.Parent != g) continue;
            int i = panes.IndexOf(k);
            Grid.SetRow(pane, !cols && i > 0 ? 2 * i : 0);
            Grid.SetColumn(pane, cols && i > 0 ? 2 * i : 0);
            Grid.SetRowSpan(pane, 1); Grid.SetColumnSpan(pane, 1);
        }
    }

    /// <summary>A divider between two panes: 5 px to grab, a 1 px line drawn.</summary>
    private static GridSplitter NewSplitter(bool cols)
    {
        var line = new FrameworkElementFactory(typeof(Rectangle));
        if (cols) { line.SetValue(FrameworkElement.WidthProperty, 1.0); line.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); }
        else { line.SetValue(FrameworkElement.HeightProperty, 1.0); line.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); }
        line.SetResourceReference(Shape.FillProperty, "Gm.ChromeBorder");
        var bg = new FrameworkElementFactory(typeof(Border));
        bg.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        bg.AppendChild(line);
        return new GridSplitter
        {
            ResizeBehavior = GridResizeBehavior.PreviousAndNext, ResizeDirection = cols ? GridResizeDirection.Columns : GridResizeDirection.Rows,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Focusable = false,
            Cursor = cols ? Cursors.SizeWE : Cursors.SizeNS, Template = new ControlTemplate(typeof(GridSplitter)) { VisualTree = bg },
        };
    }

    /// <summary>Files and Contents to these sides; when they share one, how they sit there and which comes first.</summary>
    private void SetLayout(string filesSide, string outlineSide, string? arrange = null, bool? outlineFirst = null)
    {
        _settings.FilesSide = filesSide; _settings.OutlineSide = outlineSide;
        if (arrange != null && filesSide == outlineSide) SetArrange(filesSide, arrange);
        if (outlineFirst != null)
        {
            var o = Order();
            o.Remove("outline");
            o.Insert(outlineFirst.Value ? o.IndexOf("files") : o.IndexOf("files") + 1, "outline");
            _settings.PaneOrder = o;
        }
        _settings.Save();
        ApplySideLayout(); BuildMenu();
    }

    /// <summary>Files and Contents trade places: their order on one side, or the sides themselves.</summary>
    private void SwapPanes()
    {
        if (_settings.FilesSide == _settings.OutlineSide)
        {
            var o = Order();
            int f = o.IndexOf("files"), c = o.IndexOf("outline");
            (o[f], o[c]) = (o[c], o[f]);
            _settings.PaneOrder = o; _settings.Save();
            ApplySideLayout(); BuildMenu();
        }
        else SetLayout(_settings.OutlineSide, _settings.FilesSide);
    }

    private bool IsPreset(LayoutPreset p) =>
        _settings.FilesSide == p.Files && _settings.OutlineSide == p.Outline && (p.Files != p.Outline || ArrangeOf(p.Files) == p.Arrange);

    /// <summary>View → Layout: the six layouts, the swap and the picker.</summary>
    internal MenuItem LayoutMenu()
    {
        var m = new MenuItem { Header = Strings.Get("Layout") };
        foreach (var p in Presets)
        {
            var preset = p;
            m.Items.Add(MenuEntry(Strings.Get(p.Key), () => SetLayout(preset.Files, preset.Outline, preset.Arrange), IsPreset(p)));
        }
        m.Items.Add(new Separator());
        m.Items.Add(MenuEntry(Strings.Get("LayoutSwap"), SwapPanes));
        m.Items.Add(MenuEntry(Strings.Get("LayoutPicker"), ShowLayoutPicker));
        return m;
    }

    // ------------------------------------------------------------------ drawings
    /// <summary>A layout as the drawings see it: the side of each pane, the order, how each side sits, what is on.</summary>
    private sealed record LayoutState(Dictionary<string, string> Side, List<string> Order, string LeftArrange, string RightArrange, HashSet<string> On);

    private LayoutState CurrentState() => new(
        PaneKeys.ToDictionary(k => k, SideOf), Order().ToList(), ArrangeOf("left"), ArrangeOf("right"), PaneKeys.Where(PaneOn).ToHashSet());

    /// <summary>The current layout with a preset applied (Files and Contents both shown), for the picker's drawings.</summary>
    private LayoutState PresetState(LayoutPreset p)
    {
        var s = CurrentState();
        s.Side["files"] = p.Files; s.Side["outline"] = p.Outline;
        s.On.Add("files"); s.On.Add("outline");
        return p.Files == p.Outline
            ? s with { LeftArrange = p.Files == "left" ? p.Arrange : s.LeftArrange, RightArrange = p.Files == "right" ? p.Arrange : s.RightArrange }
            : s;
    }

    /// <summary>A small drawing of a layout: the window, the text with a few lines, Files in the theme colour,
    /// Contents in a lighter shade and Search lighter still. The toolbar button shows the current one (mini).</summary>
    private FrameworkElement LayoutDrawing(LayoutState st, double width, double height, bool mini)
    {
        var accent = (FindResource("Gm.Accent") as SolidColorBrush)?.Color ?? Colors.SteelBlue;
        bool dark = ChromeIsDark();
        Brush Shade(byte a) => new SolidColorBrush(Color.FromArgb(a, accent.R, accent.G, accent.B));
        var fills = new Dictionary<string, Brush> { ["files"] = Shade(255), ["outline"] = Shade(dark ? (byte)120 : (byte)105), ["search"] = Shade(dark ? (byte)70 : (byte)55) };
        var glyphs = new Dictionary<string, string> { ["files"] = "", ["outline"] = Glyphs["Outline"], ["search"] = "" };
        Brush paper = new SolidColorBrush(dark ? Color.FromRgb(13, 17, 23) : Colors.White);
        Brush line = new SolidColorBrush(dark ? Color.FromRgb(80, 86, 96) : Color.FromRgb(200, 204, 210));
        double gap = mini ? 1 : 3;

        FrameworkElement Pane(string key)
        {
            var b = new Border { Background = fills[key], CornerRadius = new CornerRadius(mini ? 1 : 3), Margin = new Thickness(gap / 2) };
            if (!mini)
                b.Child = new TextBlock
                {
                    Text = glyphs[key], FontFamily = (FontFamily)FindResource("Gm.Icons"), FontSize = 11,
                    Foreground = key == "search" && !dark ? new SolidColorBrush(accent) : Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
            return b;
        }

        List<string> On(string side) => st.Order.Where(k => st.On.Contains(k) && st.Side[k] == side).ToList();
        bool Cols(string side) => (side == "left" ? st.LeftArrange : st.RightArrange) == "cols";

        FrameworkElement? Side(string side)
        {
            var panes = On(side);
            if (panes.Count == 0) return null;
            if (panes.Count == 1) return Pane(panes[0]);
            var g = new Grid();
            for (int i = 0; i < panes.Count; i++)
            {
                var p = Pane(panes[i]);
                if (Cols(side)) { g.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetColumn(p, i); }
                else { g.RowDefinitions.Add(new RowDefinition()); Grid.SetRow(p, i); }
                g.Children.Add(p);
            }
            return g;
        }

        double Share(string side)
        {
            var n = On(side).Count;
            return n == 0 ? 0 : Cols(side) && n > 1 ? Math.Min(0.5, 0.21 * n) : 0.26;
        }

        var grid = new Grid { Margin = new Thickness(gap / 2) };
        var left = Side("left"); var right = Side("right");
        double sl = Share("left"), sr = Share("right");
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(sl, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.2, 1 - sl - sr), GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(sr, GridUnitType.Star) });
        if (left != null) grid.Children.Add(left);
        if (right != null) { Grid.SetColumn(right, 2); grid.Children.Add(right); }
        var text = new StackPanel { Margin = new Thickness(mini ? 2 : 6, mini ? 2 : 6, mini ? 2 : 6, 0) };
        int lines = mini ? 3 : 5;
        for (int i = 0; i < lines; i++)
            text.Children.Add(new Rectangle { Height = mini ? 1.5 : 2.5, Fill = line, Margin = new Thickness(0, 0, i % 2 == 1 ? (mini ? 3 : 10) : 0, mini ? 2 : 5), RadiusX = 1, RadiusY = 1 });
        var textBox = new Border { Background = paper, Margin = new Thickness(gap / 2), CornerRadius = new CornerRadius(mini ? 1 : 3), Child = text };
        Grid.SetColumn(textBox, 1);
        grid.Children.Add(textBox);
        var frame = new Border
        {
            Width = width, Height = height, CornerRadius = new CornerRadius(mini ? 3 : 6), BorderThickness = new Thickness(1), Child = grid,
            SnapsToDevicePixels = true,
        };
        frame.SetResourceReference(Border.BackgroundProperty, "Gm.Chrome");
        frame.SetResourceReference(Border.BorderBrushProperty, "Gm.ChromeBorder");
        return frame;
    }

    // ------------------------------------------------------------------ the picker
    /// <summary>The layout button's panel: every layout drawn (a click applies it and the panel stays open), the
    /// panes on or off, the swap, and how to drag a pane.</summary>
    private void ShowLayoutPicker()
    {
        _layoutPopup ??= new Popup
        {
            PlacementTarget = LayoutBtn, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
        };
        _layoutPopup.Child = BuildLayoutPicker();
        _layoutPopup.Child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _layoutPopup.HorizontalOffset = LayoutBtn.ActualWidth - _layoutPopup.Child.DesiredSize.Width + 16;   // right edges lined up
        _layoutPopup.IsOpen = true;
    }

    private void RefreshLayoutPicker()
    {
        if (_layoutPopup is { IsOpen: true }) _layoutPopup.Child = BuildLayoutPicker();
    }

    private FrameworkElement BuildLayoutPicker()
    {
        TextBlock Label(string text, double size, bool bold = false, bool muted = false)
        {
            var t = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
            t.SetResourceReference(TextBlock.FontFamilyProperty, "Gm.Font");
            t.SetResourceReference(TextBlock.ForegroundProperty, muted ? "Gm.TextMuted" : "Gm.Text");
            return t;
        }

        var panel = new StackPanel { Margin = new Thickness(14, 10, 14, 12), Width = 448 };   // three tiles of 148
        panel.Children.Add(Label(Strings.Get("LayoutPickerTitle"), 12, bold: true));
        var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(-4, 8, -4, 4) };
        foreach (var p in Presets)
        {
            var preset = p;
            bool current = IsPreset(p);
            var content = new StackPanel();
            content.Children.Add(LayoutDrawing(PresetState(p), 124, 78, mini: false));
            var cap = Label(Strings.Plain(p.Key), 11, muted: !current);
            cap.Margin = new Thickness(2, 6, 2, 0); cap.TextAlignment = TextAlignment.Center; cap.Width = 124;
            content.Children.Add(cap);
            var tile = new Border
            {
                Child = content, Padding = new Thickness(6), Margin = new Thickness(4), CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand, Background = Brushes.Transparent,
            };
            if (current) tile.SetResourceReference(Border.BorderBrushProperty, "Gm.Accent"); else tile.BorderBrush = Brushes.Transparent;
            tile.MouseEnter += (_, _) => { if (!current) tile.SetResourceReference(Border.BackgroundProperty, "Gm.Hover"); };
            tile.MouseLeave += (_, _) => tile.Background = Brushes.Transparent;
            tile.MouseLeftButtonUp += (_, _) =>
            {
                _settings.FilesPanel = _settings.Outline = true;   // a layout shows both panes
                SetLayout(preset.Files, preset.Outline, preset.Arrange);
                RefreshLayoutPicker();
            };
            tiles.Children.Add(tile);
        }
        panel.Children.Add(tiles);

        // the panes on or off, and the swap
        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        Button Chip(string text, string glyph, bool on, Action run)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var g = new TextBlock { Text = glyph, FontSize = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            g.SetResourceReference(TextBlock.FontFamilyProperty, "Gm.Icons");
            var l = Label(text, 12); l.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(g); sp.Children.Add(l);
            var b = new Button { Content = sp, Style = (Style)FindResource("Gm.DialogButton"), Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(10, 4, 12, 4) };
            if (on) b.SetResourceReference(Button.BorderBrushProperty, "Gm.Accent");
            b.Click += (_, _) => { run(); RefreshLayoutPicker(); };
            return b;
        }
        string Check(bool on) => on ? "" : "";
        row.Children.Add(Chip(Strings.Plain("FilesShort"), Check(_settings.FilesPanel), _settings.FilesPanel, ToggleFiles));
        row.Children.Add(Chip(Strings.Plain("OutlineShort"), Check(_settings.Outline), _settings.Outline, ToggleOutline));
        row.Children.Add(Chip(Strings.Plain("SearchShort"), Check(_settings.SearchPanel), _settings.SearchPanel, ToggleSearch));
        row.Children.Add(Chip(Strings.Plain("LayoutSwap"), "", false, SwapPanes));
        panel.Children.Add(row);
        var hint = Label(Strings.Get("LayoutDragHint"), 11, muted: true);
        hint.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(hint);
        return new Border { Style = (Style)FindResource("Gm.PopupFrame"), Child = panel };
    }

    // ------------------------------------------------------------------ pictures for --render-chrome
    internal FrameworkElement LayoutPickerForTest() => BuildLayoutPicker();

    /// <summary>The window's body with the drag guide over it, as seen while a pane is dragged to (fx, fy).</summary>
    internal FrameworkElement DockOverlayForTest(bool files, double fx, double fy)
    {
        _settings.FilesPanel = _settings.Outline = true;
        _settings.SearchPanel = false;
        SetLayout("left", "left", "rows", false);
        UpdateLayout();
        _dockPane = files ? FilesPane : OutlinePane;
        _dockTarget = DockTargetAt(new Point(BodyGrid.ActualWidth * fx, BodyGrid.ActualHeight * fy));
        var canvas = _dockCanvas = new Canvas { Width = BodyGrid.ActualWidth, Height = BodyGrid.ActualHeight };
        DrawDockOverlay();
        _dockCanvas = null; _dockPane = null; _dockTarget = null;
        var body = new Rectangle { Width = canvas.Width, Height = canvas.Height, Fill = new VisualBrush(BodyGrid) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top } };
        return new Grid { Children = { body, canvas } };
    }

    // ------------------------------------------------------------------ dragging a pane
    // A pane is dragged by its title (FILES / CONTENTS / SEARCH). While it moves, a see-through window over the body
    // of the main one shows both edges and, under the pointer, the place the pane would take; letting go there moves
    // it, anywhere else (or Esc) leaves everything as it was. A see-through window, and not a WPF layer, because the
    // text is a browser window that a layer could not cover.
    private sealed record DockTarget(string Side, string? Arrange, bool PaneFirst, Rect Zone, string Label);

    private Border? _dockPane;
    private FrameworkElement? _dockHandle;
    private Point _dockStart;
    private bool _docking;
    private DockTarget? _dockTarget;
    private Window? _dockOverlay;
    private Canvas? _dockCanvas;

    private void InitDocking()
    {
        foreach (var (handle, pane) in new (FrameworkElement, Border)[] { (FilesHeader, FilesPane), (OutlineHeader, OutlinePane), (SearchHeader, SearchPane) })
        {
            handle.Cursor = Cursors.SizeAll;
            handle.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (Ancestor<ButtonBase>(e.OriginalSource as DependencyObject) != null) return;   // the header's buttons work as before
                _dockPane = pane; _dockHandle = handle; _dockStart = e.GetPosition(this); _docking = false;
                handle.CaptureMouse();
                e.Handled = true;
            };
            handle.MouseMove += OnDockMove;
            handle.MouseLeftButtonUp += (_, _) => EndDock(apply: true);
            handle.LostMouseCapture += (_, _) => EndDock(apply: false);
        }
        FilesTitle.ToolTip = OutlineTitle.ToolTip = SearchTitle.ToolTip = Strings.Get("PaneDragTip");
        PreviewKeyDown += (_, e) => { if (_docking && e.Key == Key.Escape) { e.Handled = true; EndDock(apply: false); } };
    }

    private void OnDockMove(object sender, MouseEventArgs e)
    {
        if (_dockPane == null || e.LeftButton != MouseButtonState.Pressed) return;
        if (!_docking)
        {
            var d = e.GetPosition(this) - _dockStart;
            if (Math.Abs(d.X) < 6 && Math.Abs(d.Y) < 6) return;
            _docking = true;
            Mouse.OverrideCursor = Cursors.SizeAll;
            ShowDockOverlay();
        }
        _dockTarget = DockTargetAt(e.GetPosition(BodyGrid));
        DrawDockOverlay();
    }

    private static string PaneName(string key) => Strings.Plain(key == "files" ? "FilesShort" : key == "outline" ? "OutlineShort" : "SearchShort");

    /// <summary>The place under the pointer (BodyGrid coordinates): the strip along each edge; where other panes
    /// already are, the upper third puts the pane above them, the lower third below, the middle beside them.</summary>
    private DockTarget? DockTargetAt(Point p)
    {
        double W = BodyGrid.ActualWidth, H = BodyGrid.ActualHeight;
        if (_dockPane == null || W <= 0 || H <= 0 || p.Y < 0 || p.Y > H) return null;
        double edge = Math.Clamp(W * 0.22, 140, 320);
        string? side = p.X < edge ? "left" : p.X > W - edge ? "right" : null;
        if (side == null) return null;
        string key = KeyOf(_dockPane), name = PaneName(key);
        var others = PanesOn(side).Where(k => k != key).ToList();
        string other = others.Count == 1 ? Strings.Get(others[0] == "files" ? "OfFiles" : others[0] == "outline" ? "OfOutline" : "OfSearch") : Strings.Get("OfOthers");
        double x0 = side == "left" ? 0 : W - edge;
        var strip = new Rect(x0, 0, edge, H);
        if (others.Count == 0)
            return new DockTarget(side, null, true, strip, string.Format(Strings.Get(side == "left" ? "DockLeft" : "DockRight"), name));
        double fy = p.Y / H;
        if (fy < 1 / 3.0) return new DockTarget(side, "rows", true, new Rect(x0, 0, edge, H / 2), string.Format(Strings.Get("DockAbove"), name, other));
        if (fy > 2 / 3.0) return new DockTarget(side, "rows", false, new Rect(x0, H / 2, edge, H / 2), string.Format(Strings.Get("DockBelow"), name, other));
        bool firstHalf = p.X - x0 < edge / 2;
        return new DockTarget(side, "cols", firstHalf, new Rect(firstHalf ? x0 : x0 + edge / 2, 0, edge / 2, H),
                              string.Format(Strings.Get(firstHalf ? "DockBesideLeft" : "DockBesideRight"), name, other));
    }

    /// <summary>The dragged pane goes to the target: its side, how the panes there sit, and first or last among them.</summary>
    private void DockTo(string key, DockTarget target)
    {
        var others = PanesOn(target.Side).Where(k => k != key).ToList();
        SetSideOf(key, target.Side);
        if (target.Arrange != null) SetArrange(target.Side, target.Arrange);
        var o = Order();
        o.Remove(key);
        if (others.Count > 0)
            o.Insert(target.PaneFirst ? o.IndexOf(others[0]) : o.IndexOf(others[^1]) + 1, key);
        else o.Add(key);
        _settings.PaneOrder = o;
        _settings.Save();
        ApplySideLayout(); BuildMenu();
    }

    private void ShowDockOverlay()
    {
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget == null) return;
        var topLeft = src.CompositionTarget.TransformFromDevice.Transform(BodyGrid.PointToScreen(new Point(0, 0)));
        _dockCanvas = new Canvas { Width = BodyGrid.ActualWidth, Height = BodyGrid.ActualHeight };
        _dockOverlay = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false,
            ShowActivated = false, ResizeMode = ResizeMode.NoResize, Owner = this, IsHitTestVisible = false, Focusable = false,
            Left = topLeft.X, Top = topLeft.Y, Width = BodyGrid.ActualWidth, Height = BodyGrid.ActualHeight, Content = _dockCanvas,
        };
        _dockOverlay.Show();
        DrawDockOverlay();
    }

    private void DrawDockOverlay()
    {
        if (_dockCanvas == null) return;
        _dockCanvas.Children.Clear();
        var accent = (FindResource("Gm.Accent") as SolidColorBrush)?.Color ?? Colors.SteelBlue;
        double W = _dockCanvas.Width, H = _dockCanvas.Height, edge = Math.Clamp(W * 0.22, 140, 320);
        void Box(Rect r, double fill, double stroke, bool dashed)
        {
            var b = new Rectangle
            {
                Width = Math.Max(0, r.Width - 8), Height = Math.Max(0, r.Height - 8), RadiusX = 10, RadiusY = 10, StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(fill * 255), accent.R, accent.G, accent.B)),
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(stroke * 255), accent.R, accent.G, accent.B)),
                StrokeDashArray = dashed ? new DoubleCollection { 4, 3 } : null,
            };
            Canvas.SetLeft(b, r.X + 4); Canvas.SetTop(b, r.Y + 4);
            _dockCanvas!.Children.Add(b);
        }
        Box(new Rect(0, 0, edge, H), 0.06, 0.45, true);
        Box(new Rect(W - edge, 0, edge, H), 0.06, 0.45, true);
        if (_dockTarget is not { } t) return;
        Box(t.Zone, 0.28, 0.95, false);
        var label = new Border
        {
            Background = new SolidColorBrush(accent), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 5, 10, 6), MaxWidth = Math.Max(120, t.Zone.Width - 16),
            Child = new TextBlock { Text = t.Label, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, t.Zone.X + (t.Zone.Width - label.DesiredSize.Width) / 2);
        Canvas.SetTop(label, t.Zone.Y + (t.Zone.Height - label.DesiredSize.Height) / 2);
        _dockCanvas.Children.Add(label);
    }

    private void EndDock(bool apply)
    {
        if (_dockPane == null) return;
        var pane = _dockPane; var target = _dockTarget; bool docking = _docking;
        _dockPane = null; _dockTarget = null; _docking = false;
        Mouse.OverrideCursor = null;
        _dockOverlay?.Close(); _dockOverlay = null; _dockCanvas = null;
        if (_dockHandle is { IsMouseCaptured: true } h) h.ReleaseMouseCapture();   // raises LostMouseCapture: already cleared
        _dockHandle = null;
        if (apply && docking && target != null) DockTo(KeyOf(pane), target);
    }
}
