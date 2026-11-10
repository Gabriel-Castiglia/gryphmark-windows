// Gryphmark -- View -> Theme and colour.
// Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
// Date: November 10, 2026
// SPDX-License-Identifier: GPL-2.0-only

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Gryphmark;

/// <summary>View → Theme…: light / dark / automatic, and the colour catalogue (icon + highlight). Changes apply at once.</summary>
public sealed class ThemeDialog : Window
{
    public FrameworkElement Card { get; }
    private readonly List<(string Key, Border Tile)> _tiles = new();

    public ThemeDialog(Window owner, string mode, string accent, Action<string> setMode, Action<string> setAccent)
    {
        Owner = owner;
        Title = Strings.Plain("ThemeMenu");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var font = (FontFamily)FindResource("Gm.Font");
        TextBlock Text(string s, double size, string brush, FontWeight? w = null, double bottom = 0)
        {
            var t = new TextBlock { Text = s, FontSize = size, FontFamily = font, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, bottom) };
            if (w.HasValue) t.FontWeight = w.Value;
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return t;
        }

        var stack = new StackPanel { Width = 520 };
        stack.Children.Add(Text(Strings.Plain("ThemeMenu").TrimEnd('…', '.'), 20, "Gm.Text", FontWeights.SemiBold, 14));

        // appearance
        stack.Children.Add(Text(Strings.Get("ThemeLook"), 12.5, "Gm.TextMuted", FontWeights.SemiBold, 6));
        var segHost = new Border { Style = (Style)FindResource("Gm.SegmentHost"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 18) };
        var segs = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (key, label) in new[] { ("light", "ThemeLight"), ("dark", "ThemeDark"), ("auto", "ThemeAuto") })
        {
            var rb = new RadioButton { Content = Strings.Get(label), GroupName = "thememode", Style = (Style)FindResource("Gm.Segment"), IsChecked = mode == key };
            if (key == "auto") rb.ToolTip = Strings.Get("ThemeAutoTip");
            rb.Checked += (_, _) => setMode(key);
            segs.Children.Add(rb);
        }
        segHost.Child = segs;
        stack.Children.Add(segHost);

        // colour catalogue
        stack.Children.Add(Text(Strings.Get("ThemeColor"), 12.5, "Gm.TextMuted", FontWeights.SemiBold, 8));
        var wrap = new WrapPanel { Margin = new Thickness(-4, 0, -4, 6) };
        foreach (var a in Accents.All)
        {
            var img = new Image { Width = 52, Height = 52, Source = Accents.Icon(a.Key, 64), Margin = new Thickness(0, 2, 0, 4) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var name = Text(a.Name, 11.5, "Gm.Text");
            name.TextAlignment = TextAlignment.Center;
            var tile = new Border
            {
                Width = 92, Padding = new Thickness(4, 6, 4, 6), Margin = new Thickness(4), CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(2), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = a.Name,
                Child = new StackPanel { Children = { img, name } },
            };
            var key = a.Key;
            tile.MouseLeftButtonUp += (_, _) => { setAccent(key); Mark(key); };
            tile.MouseEnter += (_, _) => { if (!IsMarked(tile)) tile.SetResourceReference(Border.BackgroundProperty, "Gm.Hover"); };
            tile.MouseLeave += (_, _) => { if (!IsMarked(tile)) tile.Background = Brushes.Transparent; };
            _tiles.Add((key, tile));
            wrap.Children.Add(tile);
        }
        stack.Children.Add(wrap);
        stack.Children.Add(Text(Strings.Get("ThemeNote"), 11.5, "Gm.TextMuted", null, 4));
        Mark(Accents.Get(accent).Key);

        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, Style = (Style)FindResource("Gm.DialogButton") };
        ok.Click += (_, _) => Close();
        stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Children = { ok } });

        var card = new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(26, 20, 26, 18), Margin = new Thickness(24),
            BorderThickness = new Thickness(1), Child = stack,
            Effect = new DropShadowEffect { BlurRadius = 36, ShadowDepth = 10, Direction = 270, Opacity = 0.18 },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Gm.Popup");
        card.SetResourceReference(Border.BorderBrushProperty, "Gm.PopupBorder");
        card.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.Source == card) DragMove(); };
        Card = card;
        Content = card;
    }

    private static bool IsMarked(Border b) => b.Tag is true;

    private void Mark(string key)
    {
        foreach (var (k, t) in _tiles)
        {
            bool on = k == key;
            t.Tag = on;
            if (on) { t.SetResourceReference(Border.BorderBrushProperty, "Gm.Accent"); t.SetResourceReference(Border.BackgroundProperty, "Gm.Selected"); }
            else { t.BorderBrush = Brushes.Transparent; t.Background = Brushes.Transparent; }
        }
    }
}
