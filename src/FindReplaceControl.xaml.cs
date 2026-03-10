using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nopad;

public partial class FindReplaceControl : UserControl
{
    private TextBox? _editor;
    private int _lastFoundIndex = -1;
    private int _lastFoundLength;

    public FindReplaceControl()
    {
        InitializeComponent();
        FindTextBox.TextChanged += FindTextBox_TextChanged;
        ReplaceTextBox.TextChanged += ReplaceTextBox_TextChanged;
        Loaded += OnLoaded;
    }

    public void Attach(TextBox editor)
    {
        _editor = editor;

        // Subscribe to scroll after visual tree is ready so inactive highlight repaints
        editor.Loaded += (_, _) =>
        {
            var sv = FindScrollViewer(editor);
            if (sv != null)
                sv.ScrollChanged += (_, _) =>
                {
                    if (_lastFoundIndex >= 0 && _lastFoundLength > 0 && !_editor.IsFocused)
                    {
                        _editor.Select(_lastFoundIndex, _lastFoundLength);
                    }
                };
        };
    }

    private static System.Windows.Controls.ScrollViewer? FindScrollViewer(DependencyObject obj)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is System.Windows.Controls.ScrollViewer sv) return sv;
            var result = FindScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyThemeColors();
    }

    public void ApplyThemeColors()
    {
        if (_editor == null) return;

        // Detect dark mode from the editor's resolved foreground — if the text is light, the theme is dark
        var editorFg = _editor.Foreground as SolidColorBrush;
        bool isDark = editorFg != null && editorFg.Color.R > 128;

        static SolidColorBrush B(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        if (isDark)
        {
            // --- Dark mode: explicit Win11 dark hex values ---
            var cardBg    = B("#2D2D2D");
            var cardBdr   = B("#404040");
            var fg        = B("#FFFFFF");
            var tbBg      = B("#1E1E1E");
            var tbBdr     = B("#404040");
            var btnBg     = B("#333333");
            var btnBdr    = B("#404040");

            CardBorder.Background  = cardBg;
            CardBorder.BorderBrush = cardBdr;
            PopupBorder.Background  = cardBg;
            PopupBorder.BorderBrush = cardBdr;

            foreach (var tb in new[] { FindTextBox, ReplaceTextBox })
            {
                tb.Background  = tbBg;
                tb.Foreground  = fg;
                tb.CaretBrush  = fg;
                tb.BorderBrush = tbBdr;
            }

            foreach (var btn in new[] { ReplaceButton, ReplaceAllButton })
            {
                btn.Background  = btnBg;
                btn.Foreground  = fg;
                btn.BorderBrush = btnBdr;
            }

            // All icon buttons / toggles
            ChevronToggle.Foreground = fg;
            foreach (UIElement child in IconButtonPanel.Children)
                if (child is ButtonBase b) b.Foreground = fg;

            // Embedded icons inside text fields
            ClearFindButton.Foreground = fg;
            ClearReplaceButton.Foreground = fg;
            SearchEmbeddedButton.Foreground = fg;

            // Checkboxes
            MatchCaseCheckBox.Foreground = fg;
            WholeWordCheckBox.Foreground = fg;
        }
        else
        {
            // --- Light mode ---
            var cardBg    = B("#F3F3F3");
            var cardBdr   = B("#E0E0E0");
            var fg        = B("#1A1A1A");
            var tbBg      = B("#FFFFFF");
            var tbBdr     = B("#C0C0C0");
            var btnBg     = B("#E5E5E5");
            var btnBdr    = B("#C0C0C0");

            CardBorder.Background  = cardBg;
            CardBorder.BorderBrush = cardBdr;
            PopupBorder.Background  = cardBg;
            PopupBorder.BorderBrush = cardBdr;

            foreach (var tb in new[] { FindTextBox, ReplaceTextBox })
            {
                tb.Background  = tbBg;
                tb.Foreground  = fg;
                tb.CaretBrush  = fg;
                tb.BorderBrush = tbBdr;
            }

            foreach (var btn in new[] { ReplaceButton, ReplaceAllButton })
            {
                btn.Background  = btnBg;
                btn.Foreground  = fg;
                btn.BorderBrush = btnBdr;
            }

            ChevronToggle.Foreground = fg;
            foreach (UIElement child in IconButtonPanel.Children)
                if (child is ButtonBase b) b.Foreground = fg;

            ClearFindButton.Foreground = fg;
            ClearReplaceButton.Foreground = fg;
            SearchEmbeddedButton.Foreground = fg;

            MatchCaseCheckBox.Foreground = fg;
            WholeWordCheckBox.Foreground = fg;
        }
    }

    public void ShowFind()
    {
        Visibility = Visibility.Visible;
        ChevronToggle.IsChecked = false;
        ReplaceRow.Height = new GridLength(0);
        FocusFindTextBox();
    }

    public void ShowReplace()
    {
        Visibility = Visibility.Visible;
        ChevronToggle.IsChecked = true;
        ReplaceRow.Height = GridLength.Auto;
        FocusFindTextBox();
    }

    private void FocusFindTextBox()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            FindTextBox.Focus();
            FindTextBox.SelectAll();
        });
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        RestoreEditorPadding();
        _editor?.Focus();
    }

    // --- Chevron toggle ---

    private void ChevronToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (ChevronToggle.IsChecked == true)
        {
            ReplaceRow.Height = GridLength.Auto;
            ChevronToggle.Content = "\uE70E"; // Up chevron
        }
        else
        {
            ReplaceRow.Height = new GridLength(0);
            ChevronToggle.Content = "\uE70D"; // Down chevron
        }
    }

    // --- Options popup ---

    private void OptionsToggle_Changed(object sender, RoutedEventArgs e)
    {
        OptionsPopup.IsOpen = OptionsToggle.IsChecked == true;
    }

    private void OptionsPopup_Closed(object sender, EventArgs e)
    {
        OptionsToggle.IsChecked = false;
    }

    // --- Clear buttons ---

    private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ClearFindButton.Visibility = string.IsNullOrEmpty(FindTextBox.Text)
            ? Visibility.Collapsed : Visibility.Visible;
        _lastFoundIndex = -1;
        _lastFoundLength = 0;
    }

    private void ReplaceTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ClearReplaceButton.Visibility = string.IsNullOrEmpty(ReplaceTextBox.Text)
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClearFindButton_Click(object sender, RoutedEventArgs e)
    {
        FindTextBox.Clear();
        FindTextBox.Focus();
    }

    private void ClearReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        ReplaceTextBox.Clear();
        ReplaceTextBox.Focus();
    }

    // --- Search logic ---

    private StringComparison GetComparison()
    {
        return MatchCaseCheckBox.IsChecked == true
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
    }

    private bool IsWholeWord(string text, int index, int length)
    {
        if (WholeWordCheckBox.IsChecked != true)
            return true;

        bool leftBoundary = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
        bool rightBoundary = index + length >= text.Length || !char.IsLetterOrDigit(text[index + length]);
        return leftBoundary && rightBoundary;
    }

    private void SelectInEditor(int index, int length)
    {
        if (_editor == null) return;
        _editor.Focus();
        _editor.Select(index, length);
        int line = _editor.GetLineIndexFromCharacterIndex(index);
        _editor.ScrollToLine(line);
        _lastFoundIndex = index;
        _lastFoundLength = length;

        // If the match is in the first 50px of the editor (unscrolled position),
        // add temporary top padding to push content below the find/replace card
        if (Visibility == Visibility.Visible)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                var rect = _editor.GetRectFromCharacterIndex(index);
                var sv = FindScrollViewer(_editor);
                if (sv == null) return;

                // Calculate the match position without scroll offset (its natural position)
                double naturalY = rect.Top + sv.VerticalOffset;

                if (naturalY < 50)
                {
                    if (_editor.Padding.Top < 50)
                    {
                        var p = _editor.Padding;
                        _editor.Padding = new Thickness(p.Left, 50, p.Right, p.Bottom);
                        _editor.ScrollToLine(line);
                    }
                }
                else
                {
                    RestoreEditorPadding();
                }
            });
        }
        else
        {
            RestoreEditorPadding();
        }
    }

    private void RestoreEditorPadding()
    {
        if (_editor == null) return;
        var p = _editor.Padding;
        if (p.Top != 0)
            _editor.Padding = new Thickness(p.Left, 0, p.Right, p.Bottom);
    }

    public void FindNext()
    {
        if (_editor == null || string.IsNullOrEmpty(FindTextBox.Text))
            return;

        string searchText = FindTextBox.Text;
        string editorText = _editor.Text;
        var comparison = GetComparison();

        int startIndex = _lastFoundIndex >= 0
            ? _lastFoundIndex + _lastFoundLength
            : 0;
        if (startIndex > editorText.Length) startIndex = 0;

        int index = editorText.IndexOf(searchText, startIndex, comparison);

        // Wrap around
        if (index < 0)
            index = editorText.IndexOf(searchText, 0, comparison);

        while (index >= 0 && !IsWholeWord(editorText, index, searchText.Length))
        {
            index = editorText.IndexOf(searchText, index + 1, comparison);
        }

        if (index >= 0)
            SelectInEditor(index, searchText.Length);
        else
            ThemedMessageBox.Show($"Cannot find \"{searchText}\"", "Nopad", Window.GetWindow(this));
    }

    public void FindPrevious()
    {
        if (_editor == null || string.IsNullOrEmpty(FindTextBox.Text))
            return;

        string searchText = FindTextBox.Text;
        string editorText = _editor.Text;
        var comparison = GetComparison();

        int startIndex = _lastFoundIndex > 0
            ? _lastFoundIndex - 1
            : editorText.Length - 1;

        int index = editorText.LastIndexOf(searchText, startIndex, comparison);

        // Wrap around
        if (index < 0)
            index = editorText.LastIndexOf(searchText, editorText.Length - 1, comparison);

        while (index >= 0 && !IsWholeWord(editorText, index, searchText.Length))
        {
            if (index == 0) { index = -1; break; }
            index = editorText.LastIndexOf(searchText, index - 1, comparison);
        }

        if (index >= 0)
            SelectInEditor(index, searchText.Length);
        else
            ThemedMessageBox.Show($"Cannot find \"{searchText}\"", "Nopad", Window.GetWindow(this));
    }

    public void ReplaceCurrent()
    {
        if (_editor == null || string.IsNullOrEmpty(FindTextBox.Text))
            return;

        // If current selection isn't already a match, find the next one first
        if (_editor.SelectionLength == 0 ||
            !string.Equals(_editor.SelectedText, FindTextBox.Text, GetComparison()))
        {
            FindNext();
            return;
        }

        // Replace the current match
        _editor.Focus();
        _editor.SelectedText = ReplaceTextBox.Text;

        // Find and highlight the next occurrence
        FindNext();
    }

    public void ReplaceAll()
    {
        if (_editor == null || string.IsNullOrEmpty(FindTextBox.Text))
            return;

        string searchText = FindTextBox.Text;
        string replaceText = ReplaceTextBox.Text;
        string editorText = _editor.Text;
        var comparison = GetComparison();

        int count = 0;
        int index = 0;

        while ((index = editorText.IndexOf(searchText, index, comparison)) >= 0)
        {
            if (IsWholeWord(editorText, index, searchText.Length))
            {
                editorText = editorText.Remove(index, searchText.Length).Insert(index, replaceText);
                index += replaceText.Length;
                count++;
            }
            else
            {
                index += searchText.Length;
            }
        }

        if (count > 0)
            _editor.Text = editorText;
    }

    // --- Key handlers ---

    private void FindTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            FindNext();
            FindTextBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void ReplaceTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ReplaceCurrent();
            ReplaceTextBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    // --- Click handlers ---

    private void NextButton_Click(object sender, RoutedEventArgs e) => FindNext();
    private void PreviousButton_Click(object sender, RoutedEventArgs e) => FindPrevious();
    private void ReplaceButton_Click(object sender, RoutedEventArgs e) => ReplaceCurrent();
    private void ReplaceAllButton_Click(object sender, RoutedEventArgs e) => ReplaceAll();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();
}
