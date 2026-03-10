using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nopad;

public static class ThemedMessageBox
{
    public static void Show(string message, string title, Window? owner = null)
    {
        owner ??= Application.Current.MainWindow;

        bool isDark = false;
        if (owner is MainWindow mw)
        {
            var fg = mw.Editor.Foreground as SolidColorBrush;
            isDark = fg != null && fg.Color.R > 128;
        }

        var cardBg   = isDark ? Color.FromRgb(0x2D, 0x2D, 0x2D) : Color.FromRgb(0xF3, 0xF3, 0xF3);
        var textClr  = isDark ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A);
        var btnBg    = isDark ? Color.FromRgb(0x33, 0x33, 0x33) : Color.FromRgb(0xE5, 0xE5, 0xE5);
        var btnBdr   = isDark ? Color.FromRgb(0x40, 0x40, 0x40) : Color.FromRgb(0xC0, 0xC0, 0xC0);

        var bgBrush   = new SolidColorBrush(cardBg);
        var fgBrush   = new SolidColorBrush(textClr);
        var btnBgB    = new SolidColorBrush(btnBg);
        var btnBdrB   = new SolidColorBrush(btnBdr);

        var textBlock = new TextBlock
        {
            Text = message,
            Foreground = fgBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
            FontSize = 13
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Height = 30,
            Background = btnBgB,
            Foreground = fgBrush,
            BorderBrush = btnBdrB,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
            IsCancel = true
        };

        var panel = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        panel.Children.Add(textBlock);
        panel.Children.Add(okButton);

        var dialog = new Window
        {
            Title = title,
            Content = panel,
            Background = bgBrush,
            SizeToContent = SizeToContent.Height,
            Width = 340,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false
        };

        okButton.Click += (_, _) => dialog.Close();
        dialog.ShowDialog();
    }

    public static string Show(string message, string title, Window? owner, bool showCancel)
    {
        owner ??= Application.Current.MainWindow;

        bool isDark = false;
        if (owner is MainWindow mw)
        {
            var fg = mw.Editor.Foreground as SolidColorBrush;
            isDark = fg != null && fg.Color.R > 128;
        }

        var cardBg   = isDark ? Color.FromRgb(0x2D, 0x2D, 0x2D) : Color.FromRgb(0xF3, 0xF3, 0xF3);
        var textClr  = isDark ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A);
        var btnBg    = isDark ? Color.FromRgb(0x33, 0x33, 0x33) : Color.FromRgb(0xE5, 0xE5, 0xE5);
        var btnBdr   = isDark ? Color.FromRgb(0x40, 0x40, 0x40) : Color.FromRgb(0xC0, 0xC0, 0xC0);

        var bgBrush   = new SolidColorBrush(cardBg);
        var fgBrush   = new SolidColorBrush(textClr);
        var btnBgB    = new SolidColorBrush(btnBg);
        var btnBdrB   = new SolidColorBrush(btnBdr);

        string result = "Cancel";

        var textBlock = new TextBlock
        {
            Text = message,
            Foreground = fgBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
            FontSize = 13
        };

        Button MakeButton(string text) => new()
        {
            Content = text,
            Width = 80,
            Height = 30,
            Background = btnBgB,
            Foreground = fgBrush,
            BorderBrush = btnBdrB,
            Margin = new Thickness(4, 0, 0, 0)
        };

        var yesBtn = MakeButton("Yes");
        yesBtn.IsDefault = true;
        var noBtn = MakeButton("No");
        var cancelBtn = MakeButton("Cancel");
        cancelBtn.IsCancel = true;

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttonPanel.Children.Add(yesBtn);
        buttonPanel.Children.Add(noBtn);
        buttonPanel.Children.Add(cancelBtn);

        var panel = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        panel.Children.Add(textBlock);
        panel.Children.Add(buttonPanel);

        var dialog = new Window
        {
            Title = title,
            Content = panel,
            Background = bgBrush,
            SizeToContent = SizeToContent.Height,
            Width = 340,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false
        };

        yesBtn.Click += (_, _) => { result = "Yes"; dialog.Close(); };
        noBtn.Click += (_, _) => { result = "No"; dialog.Close(); };
        cancelBtn.Click += (_, _) => { result = "Cancel"; dialog.Close(); };

        dialog.ShowDialog();
        return result;
    }
}
