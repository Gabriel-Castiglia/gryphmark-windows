// Gryphmark -- a question with named buttons ("Reload" / "Keep my changes"), as the macOS version asks it.
// Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
// Date: November 13, 2026
// SPDX-License-Identifier: GPL-2.0-only

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Gryphmark;

/// <summary>Rounded dialog in the style of InputDialog. The first line of the text is the heading, the rest
/// the explanation. Buttons are laid out right to left from the last one, which is the default (Enter);
/// Escape gives <c>cancel</c>. Ask returns the index of the button chosen.</summary>
public sealed class ChoiceDialog : Window
{
    private int _choice;

    private ChoiceDialog(Window owner, string text, string[] buttons, int cancel)
    {
        Owner = owner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _choice = cancel;

        var lines = text.Split('\n', 2);
        var stack = new StackPanel { Width = 400 };
        var head = new TextBlock
        {
            Text = lines[0], FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, lines.Length > 1 ? 8 : 14), FontFamily = (FontFamily)FindResource("Gm.Font"),
        };
        head.SetResourceReference(TextBlock.ForegroundProperty, "Gm.Text");
        stack.Children.Add(head);
        if (lines.Length > 1)
        {
            var body = new TextBlock { Text = lines[1], FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16), FontFamily = (FontFamily)FindResource("Gm.Font") };
            body.SetResourceReference(TextBlock.ForegroundProperty, "Gm.TextMuted");
            stack.Children.Add(body);
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        for (int i = 0; i < buttons.Length; i++)
        {
            int n = i;
            var b = new Button { Content = buttons[i], Style = (Style)FindResource("Gm.DialogButton"), IsDefault = i == buttons.Length - 1, IsCancel = i == cancel };
            b.Click += (_, _) => { _choice = n; DialogResult = true; };
            row.Children.Add(b);
        }
        stack.Children.Add(row);

        var card = new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(22, 20, 22, 18), Margin = new Thickness(24),
            BorderThickness = new Thickness(1), Child = stack,
            Effect = new DropShadowEffect { BlurRadius = 36, ShadowDepth = 10, Direction = 270, Opacity = 0.18 },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Gm.Popup");
        card.SetResourceReference(Border.BorderBrushProperty, "Gm.PopupBorder");
        card.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Content = card;
    }

    /// <summary>Shows the question and returns the index of the button chosen (cancel on Escape or close).</summary>
    public static int Ask(Window owner, string text, string[] buttons, int cancel)
    {
        var d = new ChoiceDialog(owner, text, buttons, cancel);
        d.ShowDialog();
        return d._choice;
    }
}
