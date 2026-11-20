// Gryphmark -- main window: where the Files and Contents panes go.
// Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
// Date: November 20, 2026
// SPDX-License-Identifier: GPL-2.0-only

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Gryphmark;

/// <summary>The layout of the two panes, Files and Contents. Each one goes to the left or to the right of the text
/// (Settings.FilesSide / OutlineSide); when both are on the same side they sit one above the other or side by side
/// (SideArrange), in either order (OutlineFirst). It is chosen three ways: the layout button at the right of the
/// toolbar (a drawing of every layout, a click applies it), View → Layout, or dragging a pane by its title to an
/// edge of the window, where the place it would take is shown before it is dropped. The dividers between the panes
/// and the text are dragged to change their size, as before.</summary>
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
        if (_settings.SideLayout != "") { _settings.SideLayout = ""; _settings.Save(); }

        SideSplitter.DragCompleted += (_, _) => SaveSideWidth(false);
        RightSplitter.DragCompleted += (_, _) => SaveSideWidth(true);
        PanesSplitter.DragCompleted += (_, _) => SavePaneShare(false);
        RightPanesSplitter.DragCompleted += (_, _) => SavePaneShare(false);
        ColsSplitter.DragCompleted += (_, _) => SavePaneShare(true);
        RightColsSplitter.DragCompleted += (_, _) => SavePaneShare(true);

        LayoutBtn.Click += (_, _) => ShowLayoutPicker();
        InitDocking();
    }

    private bool BothOn(bool right) =>
        _settings.FilesPanel && _settings.Outline && _settings.FilesSide == _settings.OutlineSide && (_settings.FilesSide == "right") == right;

    private void SaveSideWidth(bool right)
    {
        var w = (right ? RightColumn : SideColumn).ActualWidth;
        if (w <= 0) return;
        if (BothOn(right) && _settings.SideArrange == "cols") _settings.SideWidthColumns = Math.Round(w);
        else if (right) _settings.OutlineWidthRight = Math.Round(w);
        else _settings.FilesWidth = Math.Round(w);
        _settings.Save();
    }

    /// <summary>After the divider between the two panes moved: the part that Contents takes.</summary>
    private void SavePaneShare(bool cols)
    {
        double f = cols ? FilesPane.ActualWidth : FilesPane.ActualHeight, o = cols ? OutlinePane.ActualWidth : OutlinePane.ActualHeight;
        if (f <= 0 || o <= 0) return;
        if (cols) _settings.OutlineShareColumns = Math.Round(o / (f + o), 3); else _settings.OutlineShare = Math.Round(o / (f + o), 3);
        _settings.Save();
        ApplySideLayout();
    }

    // ------------------------------------------------------------------ applying it
    /// <summary>Puts each pane in its sidebar and arranges both sidebars. A pane that is off takes no room; a
    /// sidebar with nothing in it disappears with its divider.</summary>
    private void ApplySideLayout()
    {
        bool files = _settings.FilesPanel, outline = _settings.Outline;
        bool fRight = _settings.FilesSide == "right", oRight = _settings.OutlineSide == "right";
        PlacePane(FilesPane, fRight ? RightGrid : SideGrid);
        PlacePane(OutlinePane, oRight ? RightGrid : SideGrid);
        ArrangeSide(false, files && !fRight, outline && !oRight);
        ArrangeSide(true, files && fRight, outline && oRight);
        FilesPane.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
        OutlinePane.Visibility = outline ? Visibility.Visible : Visibility.Collapsed;
        LayoutBtn.Content = LayoutDrawing(_settings.FilesSide, _settings.OutlineSide, _settings.SideArrange, _settings.OutlineFirst, files, outline, 26, 17, mini: true);
        LayoutBtn.ToolTip = Strings.Get("LayoutPickerTip");
    }

    private static void PlacePane(Border pane, Grid target)
    {
        if (pane.Parent == target) return;
        (pane.Parent as Panel)?.Children.Remove(pane);
        target.Children.Add(pane);
    }

    private void ArrangeSide(bool right, bool hasFiles, bool hasOutline)
    {
        var g = right ? RightGrid : SideGrid;
        var col = right ? RightColumn : SideColumn;
        var splitCol = right ? RightSplitterColumn : SideSplitterColumn;
        var split = right ? RightSplitter : SideSplitter;
        var rowSplit = right ? RightPanesSplitter : PanesSplitter;
        var colSplit = right ? RightColsSplitter : ColsSplitter;

        bool any = hasFiles || hasOutline, both = hasFiles && hasOutline;
        bool wide = both && _settings.SideArrange == "cols", rows = both && !wide;
        double w = wide ? Math.Clamp(_settings.SideWidthColumns, 360, 1000)
                        : Math.Clamp(right ? _settings.OutlineWidthRight : _settings.FilesWidth, 180, 700);
        col.MaxWidth = wide ? 1000 : 700;
        col.MinWidth = any ? (wide ? 360 : 180) : 0;
        col.Width = new GridLength(any ? w : 0);
        splitCol.Width = new GridLength(any ? 5 : 0);
        split.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        g.Visibility = split.Visibility;

        // which pane comes first (above, or on the left) and the part of the room the second one takes
        Border? first = both ? (_settings.OutlineFirst ? OutlinePane : FilesPane) : hasFiles ? FilesPane : hasOutline ? OutlinePane : null;
        Border? second = both ? (first == FilesPane ? OutlinePane : FilesPane) : null;
        double oShare = Math.Clamp(_settings.OutlineShare, 0.1, 0.9), oShareCols = Math.Clamp(_settings.OutlineShareColumns, 0.15, 0.85);
        double s2 = second == OutlinePane ? (wide ? oShareCols : oShare) : 1 - (wide ? oShareCols : oShare);

        var r = g.RowDefinitions; var c = g.ColumnDefinitions;
        r[0].Height = new GridLength(rows ? 1 - s2 : 1, GridUnitType.Star);
        r[1].Height = new GridLength(rows ? 5 : 0);
        r[2].Height = new GridLength(rows ? s2 : 0, GridUnitType.Star);
        r[0].MinHeight = r[2].MinHeight = rows ? 90 : 0;
        c[0].Width = new GridLength(wide ? 1 - s2 : 1, GridUnitType.Star);
        c[1].Width = new GridLength(wide ? 5 : 0);
        c[2].Width = new GridLength(wide ? s2 : 0, GridUnitType.Star);
        c[0].MinWidth = c[2].MinWidth = wide ? 150 : 0;
        rowSplit.Visibility = rows ? Visibility.Visible : Visibility.Collapsed;
        colSplit.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;

        foreach (var pane in new[] { FilesPane, OutlinePane })
        {
            if (pane.Parent != g) continue;
            bool isSecond = pane == second;
            Grid.SetRow(pane, rows && isSecond ? 2 : 0);
            Grid.SetRowSpan(pane, rows ? 1 : 3);
            Grid.SetColumn(pane, wide && isSecond ? 2 : 0);
            Grid.SetColumnSpan(pane, 1);
        }
    }

    private void SetLayout(string filesSide, string outlineSide, string? arrange = null, bool? outlineFirst = null)
    {
        _settings.FilesSide = filesSide; _settings.OutlineSide = outlineSide;
        if (arrange != null) _settings.SideArrange = arrange;
        if (outlineFirst != null) _settings.OutlineFirst = outlineFirst.Value;
        _settings.Save();
        ApplySideLayout(); BuildMenu();
    }

    /// <summary>Files and Contents trade places: the order on one side, or the sides themselves.</summary>
    private void SwapPanes()
    {
        if (_settings.FilesSide == _settings.OutlineSide) SetLayout(_settings.FilesSide, _settings.OutlineSide, outlineFirst: !_settings.OutlineFirst);
        else SetLayout(_settings.OutlineSide, _settings.FilesSide);
    }

    private bool IsPreset(LayoutPreset p) =>
        _settings.FilesSide == p.Files && _settings.OutlineSide == p.Outline && (p.Files != p.Outline || _settings.SideArrange == p.Arrange);

    /// <summary>View → Layout: the six layouts and the swap.</summary>
    internal MenuItem LayoutMenu()
    {
        var m = new MenuItem { Header = Strings.Get("Layout") };
        foreach (var p in Presets)
        {
            var preset = p;
            var mi = MenuEntry(Strings.Get(p.Key), () => SetLayout(preset.Files, preset.Outline, preset.Arrange), IsPreset(p));
            m.Items.Add(mi);
        }
        m.Items.Add(new Separator());
        m.Items.Add(MenuEntry(Strings.Get("LayoutSwap"), SwapPanes));
        m.Items.Add(MenuEntry(Strings.Get("LayoutPicker"), ShowLayoutPicker));
        return m;
    }

    // ------------------------------------------------------------------ drawings
    /// <summary>A small drawing of a layout: the window, the text with a few lines, Files in the theme colour and
    /// Contents in a lighter shade of it. The toolbar button shows the current one (mini); the picker, all of them.</summary>
    private FrameworkElement LayoutDrawing(string filesSide, string outlineSide, string arrange, bool outlineFirst,
                                           bool showFiles, bool showOutline, double width, double height, bool mini)
    {
        var accent = (FindResource("Gm.Accent") as SolidColorBrush)?.Color ?? Colors.SteelBlue;
        bool dark = ChromeIsDark();
        Brush filesBrush = new SolidColorBrush(accent);
        Brush outlineBrush = new SolidColorBrush(Color.FromArgb(dark ? (byte)120 : (byte)105, accent.R, accent.G, accent.B));
        Brush paper = new SolidColorBrush(dark ? Color.FromRgb(13, 17, 23) : Colors.White);
        Brush line = new SolidColorBrush(dark ? Color.FromRgb(80, 86, 96) : Color.FromRgb(200, 204, 210));
        double gap = mini ? 1 : 3;

        FrameworkElement Pane(bool files)
        {
            var b = new Border { Background = files ? filesBrush : outlineBrush, CornerRadius = new CornerRadius(mini ? 1 : 3), Margin = new Thickness(gap / 2) };
            if (!mini)
                b.Child = new TextBlock
                {
                    Text = files ? "" : Glyphs["Outline"], FontFamily = (FontFamily)FindResource("Gm.Icons"), FontSize = 11,
                    Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
            return b;
        }

        FrameworkElement? Side(string side)
        {
            bool f = showFiles && filesSide == side, o = showOutline && outlineSide == side;
            if (!f && !o) return null;
            if (!(f && o)) return Pane(f);
            var g = new Grid();
            bool cols = arrange == "cols";
            var a = Pane(!outlineFirst); var b = Pane(outlineFirst);
            if (cols) { g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetColumn(b, 1); }
            else { g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition()); Grid.SetRow(b, 1); }
            g.Children.Add(a); g.Children.Add(b);
            return g;
        }

        double SideShare(string side) =>
            showFiles && showOutline && filesSide == side && outlineSide == side && arrange == "cols" ? 0.42 : 0.26;

        var grid = new Grid { Margin = new Thickness(gap / 2) };
        var left = Side("left"); var right = Side("right");
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(left != null ? SideShare("left") : 0, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - (left != null ? SideShare("left") : 0) - (right != null ? SideShare("right") : 0), GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(right != null ? SideShare("right") : 0, GridUnitType.Star) });
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
    /// <summary>The layout button's panel: every layout drawn (a click applies it and the panel stays open), the two
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
            content.Children.Add(LayoutDrawing(p.Files, p.Outline, p.Arrange, p.Files == p.Outline && _settings.OutlineFirst, true, true, 124, 78, mini: false));
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
            var b = new Button { Content = sp, Style = (Style)FindResource("Gm.DialogButton"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 4, 12, 4) };
            if (on) b.SetResourceReference(Button.BorderBrushProperty, "Gm.Accent");
            b.Click += (_, _) => { run(); RefreshLayoutPicker(); };
            return b;
        }
        row.Children.Add(Chip(Strings.Plain("FilesShort"), _settings.FilesPanel ? "" : "", _settings.FilesPanel, ToggleFiles));
        row.Children.Add(Chip(Strings.Plain("OutlineShort"), _settings.Outline ? "" : "", _settings.Outline, ToggleOutline));
        row.Children.Add(Chip(Strings.Plain("LayoutSwap"), "", false, SwapPanes));
        panel.Children.Add(row);
        var hint = Label(Strings.Get("LayoutDragHint"), 11, muted: true);
        hint.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(hint);
        return new Border { Style = (Style)FindResource("Gm.PopupFrame"), Child = panel };
    }

    // ------------------------------------------------------------------ pictures for --render-chrome
    internal FrameworkElement LayoutPickerForTest() => BuildLayoutPicker();

    /// <summary>The window's body with the drag guide over it, as seen while a pane is dragged to (fx, fy).</summary>
    internal FrameworkElement DockOverlayForTest(bool files, double fx, double fy)
    {
        _settings.FilesPanel = _settings.Outline = true;
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
    // A pane is dragged by its title (FILES / CONTENTS). While it moves, a see-through window over the body of
    // the main one shows both edges and, under the pointer, the place the pane would take; letting go there moves
    // it, anywhere else (or Esc) leaves everything as it was. A see-through window, and not a WPF layer, because
    // the text is a browser window that a layer could not cover.
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
        foreach (var (handle, pane) in new (FrameworkElement, Border)[] { (FilesHeader, FilesPane), (OutlineHeader, OutlinePane) })
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
        FilesTitle.ToolTip = OutlineTitle.ToolTip = Strings.Get("PaneDragTip");
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

    /// <summary>The place under the pointer (BodyGrid coordinates): the strip along each edge; where the other
    /// pane already is, its upper third puts the pane above it, the lower third below, the middle beside it.</summary>
    private DockTarget? DockTargetAt(Point p)
    {
        double W = BodyGrid.ActualWidth, H = BodyGrid.ActualHeight;
        if (W <= 0 || H <= 0 || p.Y < 0 || p.Y > H) return null;
        double edge = Math.Clamp(W * 0.22, 140, 320);
        string? side = p.X < edge ? "left" : p.X > W - edge ? "right" : null;
        if (side == null) return null;
        bool files = _dockPane == FilesPane;
        string name = Strings.Plain(files ? "FilesShort" : "OutlineShort"), other = Strings.Get(files ? "OfOutline" : "OfFiles");
        bool otherOn = files ? _settings.Outline : _settings.FilesPanel;
        string otherSide = files ? _settings.OutlineSide : _settings.FilesSide;
        double x0 = side == "left" ? 0 : W - edge;
        var strip = new Rect(x0, 0, edge, H);
        if (!otherOn || otherSide != side)
            return new DockTarget(side, null, true, strip, string.Format(Strings.Get(side == "left" ? "DockLeft" : "DockRight"), name));
        double fy = p.Y / H;
        if (fy < 1 / 3.0) return new DockTarget(side, "rows", true, new Rect(x0, 0, edge, H / 2), string.Format(Strings.Get("DockAbove"), name, other));
        if (fy > 2 / 3.0) return new DockTarget(side, "rows", false, new Rect(x0, H / 2, edge, H / 2), string.Format(Strings.Get("DockBelow"), name, other));
        bool firstHalf = p.X - x0 < edge / 2;
        return new DockTarget(side, "cols", firstHalf, new Rect(firstHalf ? x0 : x0 + edge / 2, 0, edge / 2, H),
                              string.Format(Strings.Get(firstHalf ? "DockBesideLeft" : "DockBesideRight"), name, other));
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
        if (!apply || !docking || target == null) return;
        bool files = pane == FilesPane;
        string fSide = files ? target.Side : _settings.FilesSide, oSide = files ? _settings.OutlineSide : target.Side;
        bool? outlineFirst = target.Arrange == null ? null : files ? !target.PaneFirst : target.PaneFirst;
        SetLayout(fSide, oSide, target.Arrange, outlineFirst);
    }
}
