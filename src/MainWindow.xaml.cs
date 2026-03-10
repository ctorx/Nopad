using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Nopad;

public class AppSettings
{
    public bool WordWrap { get; set; }
    public bool LineNumbers { get; set; }
    public bool StatusBar { get; set; } = true;

    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nopad");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { }
        var settings = new AppSettings();
        settings.Save();
        return settings;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

public partial class MainWindow : Window
{
    private string? _currentFilePath;
    private bool _isModified;
    private readonly double _baseFontSize = 14.0;
    private double _zoomLevel = 100.0;
    private Encoding _fileEncoding = new UTF8Encoding(false);
    private string _lineEnding = "\r\n";
    private AppSettings _settings = null!;
    private bool _showLineNumbers;
    private ScrollViewer? _editorScrollViewer;

    // Close prompt lock
    private bool _closePromptEnabled;

    // Block selection state
    private bool _blockSelectionActive;
    private int _blockAnchorLine, _blockAnchorCol;
    private int _blockCaretLine, _blockCaretCol;
    private BlockSelectionAdorner? _blockAdorner;

    public MainWindow()
    {
        InitializeComponent();
        FindReplaceBar.Attach(Editor);
        _settings = AppSettings.Load();

        // Apply saved settings (suppress event handlers during init)
        WordWrapMenuItem.IsChecked = _settings.WordWrap;
        LineNumbersMenuItem.IsChecked = _settings.LineNumbers;

        // Apply word wrap
        Editor.TextWrapping = _settings.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        Editor.HorizontalScrollBarVisibility = _settings.WordWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;

        // Apply line numbers
        _showLineNumbers = _settings.LineNumbers;
        LineNumberColumn.Width = _showLineNumbers ? GridLength.Auto : new GridLength(0);

        // Apply status bar
        StatusBarMenuItem.IsChecked = _settings.StatusBar;
        AppStatusBar.Visibility = _settings.StatusBar ? Visibility.Visible : Visibility.Collapsed;

        StateChanged += (_, _) => AdjustMaximizedPadding();

        Editor.Loaded += (_, _) =>
        {
            Editor.FocusVisualStyle = null; // Removes the dotted focus line

            // Force vertical padding by setting margin on the internal ScrollContentPresenter
            var scrollContentPresenter = FindVisualChild<ScrollContentPresenter>(Editor);
            if (scrollContentPresenter != null)
                scrollContentPresenter.Margin = new Thickness(0, 20, 0, 20);

            // Match the gutter top padding to the editor's internal padding
            LineNumberText.Margin = new Thickness(0, 20, 0, 20);

            // Fluent theme ignores SelectionTextBrush — use semi-transparent selection instead
            Editor.SelectionBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7));
            Editor.SelectionOpacity = 0.4;

            _editorScrollViewer = FindScrollViewer(Editor);
            if (_editorScrollViewer != null)
                _editorScrollViewer.ScrollChanged += EditorScrollViewer_ScrollChanged;
            UpdateLineNumbers();
            ApplyThemeColors();
        };

        Editor.Focus();
    }

    private static T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is T match) return match;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject obj) =>
        FindVisualChild<ScrollViewer>(obj);

    private void UpdateTitle()
    {
        string fileName = _currentFilePath != null ? Path.GetFileName(_currentFilePath) : "Untitled";
        string modified = _isModified ? "*" : "";
        Title = $"{modified}{fileName} - Nopad";
    }

    private void UpdateStatusBar()
    {
        int caretIndex = Editor.CaretIndex;
        int line = Editor.GetLineIndexFromCharacterIndex(caretIndex);
        int lineStart = Editor.GetCharacterIndexFromLineIndex(line);
        int col = caretIndex - lineStart;

        StatusLineCol.Text = $"Ln {line + 1}, Col {col + 1}";
        StatusCharCount.Text = $"{Editor.Text.Length:N0} Characters";
        StatusZoom.Text = $"{(int)_zoomLevel}%";
        StatusLineEnding.Text = _lineEnding == "\r\n" ? "Windows (CRLF)" : "Unix (LF)";
        StatusEncoding.Text = _fileEncoding.EncodingName.Contains("UTF-8") ? "UTF-8" : _fileEncoding.EncodingName;
    }

    // --- File Operations ---

    private void NewFile()
    {
        Editor.Clear();
        _currentFilePath = null;
        _isModified = false;
        _fileEncoding = new UTF8Encoding(false);
        _lineEnding = "\r\n";
        UpdateTitle();
        UpdateStatusBar();
    }

    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            DefaultExt = ".txt"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(dialog.FileName);
                _fileEncoding = DetectEncoding(bytes);
                string content = _fileEncoding.GetString(bytes);

                // Strip BOM if present
                if (content.Length > 0 && content[0] == '\uFEFF')
                    content = content[1..];

                // Detect line ending
                _lineEnding = content.Contains("\r\n") ? "\r\n" : "\n";

                Editor.Text = content;
                _currentFilePath = dialog.FileName;
                _isModified = false;
                UpdateTitle();
                UpdateStatusBar();
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Show($"Error opening file: {ex.Message}", "Nopad", this);
            }
        }
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(true);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;
        return new UTF8Encoding(false);
    }

    private void SaveFile()
    {
        if (_currentFilePath == null)
        {
            SaveFileAs();
            return;
        }

        try
        {
            string content = Editor.Text;
            // Normalize line endings to match detected format
            if (_lineEnding == "\r\n")
            {
                content = content.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
            }

            File.WriteAllText(_currentFilePath, content, _fileEncoding);
            _isModified = false;
            UpdateTitle();
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show($"Error saving file: {ex.Message}", "Nopad", this);
        }
    }

    private void SaveFileAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            DefaultExt = ".txt",
            FileName = _currentFilePath != null ? Path.GetFileName(_currentFilePath) : "Untitled.txt"
        };

        if (dialog.ShowDialog() == true)
        {
            _currentFilePath = dialog.FileName;
            SaveFile();
        }
    }

    // --- Event Handlers ---

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        _isModified = true;
        UpdateTitle();
        UpdateStatusBar();
        UpdateLineNumbers();
    }

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateStatusBar();
    }

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

        // File shortcuts
        if (ctrl && !shift && !alt)
        {
            switch (e.Key)
            {
                case Key.N: NewFile(); e.Handled = true; return;
                case Key.O: OpenFile(); e.Handled = true; return;
                case Key.S: SaveFile(); e.Handled = true; return;
                case Key.F: FindReplaceBar.ShowFind(); e.Handled = true; return;
                case Key.H: FindReplaceBar.ShowReplace(); e.Handled = true; return;
                case Key.OemPlus:
                case Key.Add:
                    ZoomIn(); e.Handled = true; return;
                case Key.OemMinus:
                case Key.Subtract:
                    ZoomOut(); e.Handled = true; return;
                case Key.D0:
                case Key.NumPad0:
                    ZoomReset(); e.Handled = true; return;
            }
        }

        if (ctrl && shift && !alt && e.Key == Key.S)
        {
            SaveFileAs();
            e.Handled = true;
            return;
        }

        // Escape to hide find/replace
        if (e.Key == Key.Escape && FindReplaceBar.Visibility == Visibility.Visible)
        {
            FindReplaceBar.Hide();
            e.Handled = true;
            return;
        }

        // Block selection: Shift+Alt+Arrow
        if (shift && alt && !ctrl && IsArrowKey(e.Key))
        {
            HandleBlockSelection(e.Key);
            e.Handled = true;
            return;
        }

        // Clear block selection on other keys (except modifiers)
        if (_blockSelectionActive && !IsModifierKey(e.Key) && !(shift && alt && IsArrowKey(e.Key)))
        {
            // If typing in block mode, handle it
            if (!ctrl && !alt && e.Key != Key.Escape)
            {
                if (e.Key == Key.Back)
                {
                    BlockBackspace();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Delete)
                {
                    BlockDelete();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Enter)
                {
                    ClearBlockSelection();
                    return;
                }
            }

            if (e.Key == Key.Escape)
            {
                ClearBlockSelection();
                e.Handled = true;
                return;
            }
        }

        // Tab indent/unindent for multi-line selections
        if (e.Key == Key.Tab && !_blockSelectionActive)
        {
            if (HandleTabIndent(shift))
            {
                e.Handled = true;
                return;
            }
        }
    }

    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (e.Delta > 0)
                ZoomIn();
            else
                ZoomOut();
            e.Handled = true;
        }
    }

    // Override TextInput for block selection typing
    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        if (_blockSelectionActive && !string.IsNullOrEmpty(e.Text) && e.Text[0] >= ' ')
        {
            BlockInsertChar(e.Text);
            e.Handled = true;
            return;
        }
        base.OnTextInput(e);
    }

    // --- Tab Indent/Unindent ---

    private bool HandleTabIndent(bool shift)
    {
        string text = Editor.Text;
        int selStart = Editor.SelectionStart;
        int selLength = Editor.SelectionLength;

        if (selLength == 0 && !shift)
            return false; // Let default tab behavior happen for no selection

        int startLine = Editor.GetLineIndexFromCharacterIndex(selStart);
        int endLine = selLength > 0
            ? Editor.GetLineIndexFromCharacterIndex(selStart + selLength - 1)
            : startLine;

        if (startLine == endLine && !shift)
            return false; // Single line forward tab with no multi-line selection

        string[] lines = text.Split('\n');
        // Fix for \r\n: lines may have trailing \r
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].EndsWith('\r'))
                lines[i] = lines[i][..^1];
        }

        int lineStartIndex = 0;
        for (int i = 0; i < startLine; i++)
            lineStartIndex += lines[i].Length + _lineEnding.Length;

        for (int i = startLine; i <= endLine && i < lines.Length; i++)
        {
            if (shift)
            {
                // Unindent: remove one leading tab or up to 4 spaces
                if (lines[i].StartsWith('\t'))
                    lines[i] = lines[i][1..];
                else if (lines[i].StartsWith("    "))
                    lines[i] = lines[i][4..];
                else
                {
                    int spaces = 0;
                    while (spaces < lines[i].Length && spaces < 4 && lines[i][spaces] == ' ')
                        spaces++;
                    if (spaces > 0)
                        lines[i] = lines[i][spaces..];
                }
            }
            else
            {
                // Indent: add tab
                lines[i] = "\t" + lines[i];
            }
        }

        string newText = string.Join(_lineEnding, lines);
        Editor.Text = newText;

        // Restore selection to cover the modified lines
        int newSelStart = 0;
        for (int i = 0; i < startLine; i++)
            newSelStart += lines[i].Length + _lineEnding.Length;

        int newSelEnd = newSelStart;
        for (int i = startLine; i <= endLine && i < lines.Length; i++)
            newSelEnd += lines[i].Length + (i < endLine ? _lineEnding.Length : 0);

        Editor.Select(newSelStart, newSelEnd - newSelStart);
        return true;
    }

    // --- Zoom ---

    private void ZoomIn()
    {
        if (Editor.FontSize < 72)
        {
            Editor.FontSize = Math.Min(72, Editor.FontSize + 2);
            LineNumberText.FontSize = Editor.FontSize;
            _zoomLevel = (Editor.FontSize / _baseFontSize) * 100;
            UpdateStatusBar();
        }
    }

    private void ZoomOut()
    {
        if (Editor.FontSize > 6)
        {
            Editor.FontSize = Math.Max(6, Editor.FontSize - 2);
            LineNumberText.FontSize = Editor.FontSize;
            _zoomLevel = (Editor.FontSize / _baseFontSize) * 100;
            UpdateStatusBar();
        }
    }

    private void ZoomReset()
    {
        Editor.FontSize = _baseFontSize;
        LineNumberText.FontSize = _baseFontSize;
        _zoomLevel = 100;
        UpdateStatusBar();
    }

    // --- Block Selection (Shift+Alt+Arrow) ---

    private static bool IsArrowKey(Key key) =>
        key is Key.Up or Key.Down or Key.Left or Key.Right;

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LeftCtrl or Key.RightCtrl or Key.System;

    private void HandleBlockSelection(Key key)
    {
        if (!_blockSelectionActive)
        {
            // Initialize block selection at current caret position
            int caretIndex = Editor.CaretIndex;
            _blockAnchorLine = Editor.GetLineIndexFromCharacterIndex(caretIndex);
            int lineStart = Editor.GetCharacterIndexFromLineIndex(_blockAnchorLine);
            _blockAnchorCol = caretIndex - lineStart;
            _blockCaretLine = _blockAnchorLine;
            _blockCaretCol = _blockAnchorCol;
            _blockSelectionActive = true;
        }

        // Move block caret
        switch (key)
        {
            case Key.Up:
                if (_blockCaretLine > 0) _blockCaretLine--;
                break;
            case Key.Down:
                if (_blockCaretLine < Editor.LineCount - 1) _blockCaretLine++;
                break;
            case Key.Left:
                if (_blockCaretCol > 0) _blockCaretCol--;
                break;
            case Key.Right:
                _blockCaretCol++;
                break;
        }

        // Update visual adorner
        UpdateBlockAdorner();
    }

    private void UpdateBlockAdorner()
    {
        var adornerLayer = AdornerLayer.GetAdornerLayer(Editor);
        if (adornerLayer == null) return;

        if (_blockAdorner != null)
            adornerLayer.Remove(_blockAdorner);

        _blockAdorner = new BlockSelectionAdorner(Editor,
            _blockAnchorLine, _blockAnchorCol,
            _blockCaretLine, _blockCaretCol);
        adornerLayer.Add(_blockAdorner);

        // Clear native selection
        Editor.Select(Editor.CaretIndex, 0);
    }

    private void ClearBlockSelection()
    {
        _blockSelectionActive = false;
        if (_blockAdorner != null)
        {
            var adornerLayer = AdornerLayer.GetAdornerLayer(Editor);
            adornerLayer?.Remove(_blockAdorner);
            _blockAdorner = null;
        }
    }

    private (int startLine, int endLine, int startCol, int endCol) GetBlockRange()
    {
        int startLine = Math.Min(_blockAnchorLine, _blockCaretLine);
        int endLine = Math.Max(_blockAnchorLine, _blockCaretLine);
        int startCol = Math.Min(_blockAnchorCol, _blockCaretCol);
        int endCol = Math.Max(_blockAnchorCol, _blockCaretCol);
        return (startLine, endLine, startCol, endCol);
    }

    private void BlockInsertChar(string ch)
    {
        var (startLine, endLine, startCol, endCol) = GetBlockRange();
        string[] lines = GetLines();

        for (int i = startLine; i <= endLine && i < lines.Length; i++)
        {
            // Pad line if shorter than column
            while (lines[i].Length < startCol)
                lines[i] += " ";

            if (startCol != endCol && lines[i].Length >= startCol)
            {
                int removeLen = Math.Min(endCol - startCol, lines[i].Length - startCol);
                lines[i] = lines[i].Remove(startCol, removeLen);
            }

            lines[i] = lines[i].Insert(startCol, ch);
        }

        SetLines(lines);

        // Move block caret right by inserted length
        _blockAnchorCol = startCol + ch.Length;
        _blockCaretCol = _blockAnchorCol;
        UpdateBlockAdorner();
    }

    private void BlockBackspace()
    {
        var (startLine, endLine, startCol, endCol) = GetBlockRange();
        if (startCol == 0 && startCol == endCol) return;

        string[] lines = GetLines();

        for (int i = startLine; i <= endLine && i < lines.Length; i++)
        {
            if (startCol != endCol)
            {
                // Delete selected block
                int removeStart = Math.Min(startCol, lines[i].Length);
                int removeLen = Math.Min(endCol - startCol, lines[i].Length - removeStart);
                if (removeLen > 0)
                    lines[i] = lines[i].Remove(removeStart, removeLen);
            }
            else if (startCol > 0 && startCol <= lines[i].Length)
            {
                lines[i] = lines[i].Remove(startCol - 1, 1);
            }
        }

        SetLines(lines);

        int newCol = startCol != endCol ? startCol : Math.Max(0, startCol - 1);
        _blockAnchorCol = newCol;
        _blockCaretCol = newCol;
        UpdateBlockAdorner();
    }

    private void BlockDelete()
    {
        var (startLine, endLine, startCol, endCol) = GetBlockRange();
        string[] lines = GetLines();

        for (int i = startLine; i <= endLine && i < lines.Length; i++)
        {
            if (startCol != endCol)
            {
                int removeStart = Math.Min(startCol, lines[i].Length);
                int removeLen = Math.Min(endCol - startCol, lines[i].Length - removeStart);
                if (removeLen > 0)
                    lines[i] = lines[i].Remove(removeStart, removeLen);
            }
            else if (startCol < lines[i].Length)
            {
                lines[i] = lines[i].Remove(startCol, 1);
            }
        }

        SetLines(lines);

        _blockAnchorCol = startCol;
        _blockCaretCol = startCol;
        UpdateBlockAdorner();
    }

    private string[] GetLines()
    {
        return Editor.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
    }

    private void SetLines(string[] lines)
    {
        int caretPos = Editor.CaretIndex;
        Editor.Text = string.Join(_lineEnding, lines);
        Editor.CaretIndex = Math.Min(caretPos, Editor.Text.Length);
    }

    // --- Windows Integration ---

    private static readonly string System32Exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Nopad.exe");
    private const string RegistryKeyPath = @"*\shell\Edit in Nopad";

    private bool IsInstalled()
    {
        bool exeExists = File.Exists(System32Exe);
        bool regExists = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(RegistryKeyPath) != null;
        return exeExists && regExists;
    }

    private void FileMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        bool installed = IsInstalled();
        IntegrateMenuItem.Header = installed ? "_Uninstall" : "_Install";
    }

    private void Integrate_Click(object sender, RoutedEventArgs e)
    {
        if (IsInstalled())
            Uninstall();
        else
            Install();
    }

    private void Install()
    {
        try
        {
            string currentExe = Process.GetCurrentProcess().MainModule!.FileName;

            var commands = new List<string>();

            if (!File.Exists(System32Exe))
                commands.Add($"copy /Y \"{currentExe}\" \"{System32Exe}\"");

            if (Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(RegistryKeyPath) == null)
            {
                commands.Add($"reg add \"HKEY_CLASSES_ROOT\\{RegistryKeyPath}\" /ve /d \"Edit in Nopad\" /f");
                commands.Add($"reg add \"HKEY_CLASSES_ROOT\\{RegistryKeyPath}\\command\" /ve /d \"\\\"{System32Exe}\\\" \\\"%1\\\"\" /f");
                commands.Add($"reg add \"HKEY_CLASSES_ROOT\\{RegistryKeyPath}\" /v \"Icon\" /d \"\\\"{System32Exe}\\\",0\" /f");
            }

            if (commands.Count == 0) return;

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + string.Join(" && ", commands),
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var proc = Process.Start(psi);
            proc?.WaitForExit();

            if (proc?.ExitCode == 0)
                ThemedMessageBox.Show("Nopad has been installed.", "Nopad", this);
            else
                ThemedMessageBox.Show("Install failed. Make sure to approve the administrator prompt.", "Nopad", this);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled UAC prompt
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show($"Install failed: {ex.Message}", "Nopad", this);
        }
    }

    private void Uninstall()
    {
        try
        {
            string currentExe = Process.GetCurrentProcess().MainModule!.FileName;
            bool runningFromSystem32 = currentExe.Equals(System32Exe, StringComparison.OrdinalIgnoreCase);

            // Remove registry key and delete exe via elevated background script
            // If running from System32, the exe is locked — use a script that waits for exit then deletes
            var script = new StringBuilder();
            script.Append("reg delete \"HKEY_CLASSES_ROOT\\");
            script.Append(RegistryKeyPath);
            script.Append("\" /f");

            if (runningFromSystem32)
            {
                // Ping localhost to wait ~2 seconds for the process to exit, then delete
                script.Append(" && ping 127.0.0.1 -n 3 > nul");
            }

            script.Append($" && del /f /q \"{System32Exe}\"");

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + script.ToString(),
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var proc = Process.Start(psi);

            if (runningFromSystem32)
            {
                // Close immediately so the background script can delete the exe
                Application.Current.Shutdown();
            }
            else
            {
                proc?.WaitForExit();
                if (proc?.ExitCode == 0)
                    ThemedMessageBox.Show("Nopad has been uninstalled.", "Nopad", this);
                else
                    ThemedMessageBox.Show("Uninstall failed. Make sure to approve the administrator prompt.", "Nopad", this);
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled UAC prompt
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show($"Uninstall failed: {ex.Message}", "Nopad", this);
        }
    }

    // --- Menu Click Handlers ---

    private void New_Click(object sender, RoutedEventArgs e) => NewFile();
    private void Open_Click(object sender, RoutedEventArgs e) => OpenFile();
    private void Save_Click(object sender, RoutedEventArgs e) => SaveFile();
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveFileAs();
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Undo_Click(object sender, RoutedEventArgs e) => Editor.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Editor.Redo();
    private void Cut_Click(object sender, RoutedEventArgs e) => Editor.Cut();
    private void Copy_Click(object sender, RoutedEventArgs e) => Editor.Copy();
    private void Paste_Click(object sender, RoutedEventArgs e) => Editor.Paste();
    private void Delete_Click(object sender, RoutedEventArgs e) => Editor.SelectedText = "";
    private void SelectAll_Click(object sender, RoutedEventArgs e) => Editor.SelectAll();

    private void Find_Click(object sender, RoutedEventArgs e) => FindReplaceBar.ShowFind();
    private void Replace_Click(object sender, RoutedEventArgs e) => FindReplaceBar.ShowReplace();

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomIn();
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomOut();
    private void ZoomReset_Click(object sender, RoutedEventArgs e) => ZoomReset();

    private void WordWrap_Changed(object sender, RoutedEventArgs e)
    {
        Editor.TextWrapping = WordWrapMenuItem.IsChecked ? TextWrapping.Wrap : TextWrapping.NoWrap;
        Editor.HorizontalScrollBarVisibility = WordWrapMenuItem.IsChecked
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;
        _settings.WordWrap = WordWrapMenuItem.IsChecked;
        _settings.Save();
    }

    private void LineNumbers_Changed(object sender, RoutedEventArgs e)
    {
        _showLineNumbers = LineNumbersMenuItem.IsChecked;
        LineNumberColumn.Width = _showLineNumbers ? GridLength.Auto : new GridLength(0);
        _settings.LineNumbers = _showLineNumbers;
        _settings.Save();
        UpdateLineNumbers();
    }

    private void StatusBar_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        AppStatusBar.Visibility = StatusBarMenuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
        _settings.StatusBar = StatusBarMenuItem.IsChecked;
        _settings.Save();
    }

    private void EditorScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        SyncLineNumberScroll();
    }

    private void SyncLineNumberScroll()
    {
        if (_editorScrollViewer != null && _showLineNumbers)
            LineNumberScroller.ScrollToVerticalOffset(_editorScrollViewer.VerticalOffset);
    }

    private void UpdateLineNumbers()
    {
        if (!_showLineNumbers)
        {
            LineNumberText.Text = "";
            return;
        }

        int lineCount = Editor.LineCount;
        var sb = new StringBuilder();
        for (int i = 1; i <= lineCount; i++)
        {
            if (i > 1) sb.AppendLine();
            sb.Append(i);
        }
        LineNumberText.Text = sb.ToString();

        SyncLineNumberScroll();
    }

    // --- Theme ---

    private void ApplyThemeColors()
    {
        var editorFg = Editor.Foreground as SolidColorBrush;
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
            var chromeBg = B("#2C2C2C");
            var editorBg = B("#272727");
            var fg       = B("#CFCFCF");
            var sepColor = B("#3D3D3D");
            var gutterBg = B("#252525");
            var gutterFg = B("#6E6E6E");
            var borderClr = B("#3D3D3D");

            Editor.Background = editorBg;
            Editor.BorderBrush = Brushes.Transparent; // Ensure no border color
            Editor.CaretBrush = fg;                   // Make sure the cursor is visible!
            Editor.Foreground = fg;

            // Menu bar
            MenuBarGrid.Background = chromeBg;
            AppMenu.Background = chromeBg;
            AppMenu.Foreground = fg;

            // Status bar
            AppStatusBar.Background = chromeBg;
            AppStatusBar.BorderBrush = borderClr;
            foreach (var tb in new[] { StatusLineCol, StatusCharCount, StatusZoom, StatusLineEnding, StatusEncoding })
                tb.Foreground = fg;
            foreach (var sep in new[] { StatusSep1, StatusSep2, StatusSep3 })
                sep.Foreground = sepColor;

            // Gutter
            LineNumberBorder.Background = gutterBg;
            LineNumberBorder.BorderBrush = borderClr;
            LineNumberText.Foreground = gutterFg;
        }
        else
        {
            var chromeBg = B("#F3F3F3");
            var editorBg = B("#FFFFFF");
            var fg       = B("#1A1A1A");
            var sepColor = B("#D0D0D0");
            var gutterBg = B("#F0F0F0");
            var gutterFg = B("#787878");
            var borderClr = B("#E0E0E0");

            Editor.Background = editorBg;
            Editor.BorderBrush = Brushes.Transparent;
            Editor.CaretBrush = fg;
            Editor.Foreground = fg;

            MenuBarGrid.Background = chromeBg;
            AppMenu.Background = chromeBg;
            AppMenu.Foreground = fg;


            AppStatusBar.Background = chromeBg;
            AppStatusBar.BorderBrush = borderClr;
            foreach (var tb in new[] { StatusLineCol, StatusCharCount, StatusZoom, StatusLineEnding, StatusEncoding })
                tb.Foreground = fg;
            foreach (var sep in new[] { StatusSep1, StatusSep2, StatusSep3 })
                sep.Foreground = sepColor;

            LineNumberBorder.Background = gutterBg;
            LineNumberBorder.BorderBrush = borderClr;
            LineNumberText.Foreground = gutterFg;
        }

        FindReplaceBar.ApplyThemeColors();
    }

    // --- Maximized padding fix ---

    private void AdjustMaximizedPadding()
    {
        if (WindowState == WindowState.Maximized)
            RootPanel.Margin = new Thickness(6, 6, 6, 6);
        else
            RootPanel.Margin = new Thickness(0);
    }

    // --- Lock Button ---

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        _closePromptEnabled = LockButton.IsChecked == true;
        UpdateLockVisual();
    }

    private void UpdateLockVisual()
    {
        if (_closePromptEnabled)
        {
            LockGlyph.Text = "\uE72E"; // Locked icon
            LockGlyph.Opacity = 0.8;
            LockButton.ToolTip = "Save prompt on close is enabled";
        }
        else
        {
            LockGlyph.Text = "\uE785"; // Unlocked icon
            LockGlyph.Opacity = 0.4;
            LockButton.ToolTip = "Click to enable save prompt on close";
        }
    }

    // --- Window Close ---

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_closePromptEnabled && _isModified)
        {
            var result = ThemedMessageBox.Show(
                "You have unsaved changes. Do you want to save before closing?",
                "Nopad", this, showCancel: true);

            if (result == "Yes")
            {
                SaveFile();
                // If still modified (user cancelled save dialog), abort close
                if (_isModified)
                    e.Cancel = true;
            }
            else if (result == "Cancel")
            {
                e.Cancel = true;
            }
        }

        base.OnClosing(e);
    }
}

// --- Block Selection Adorner ---

public class BlockSelectionAdorner : Adorner
{
    private readonly TextBox _editor;
    private readonly int _anchorLine, _anchorCol, _caretLine, _caretCol;

    public BlockSelectionAdorner(TextBox editor, int anchorLine, int anchorCol, int caretLine, int caretCol)
        : base(editor)
    {
        _editor = editor;
        _anchorLine = anchorLine;
        _anchorCol = anchorCol;
        _caretLine = caretLine;
        _caretCol = caretCol;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        int startLine = Math.Min(_anchorLine, _caretLine);
        int endLine = Math.Max(_anchorLine, _caretLine);
        int startCol = Math.Min(_anchorCol, _caretCol);
        int endCol = Math.Max(_anchorCol, _caretCol);

        if (startCol == endCol && startLine == endLine)
            return;

        var brush = new SolidColorBrush(Color.FromArgb(80, 51, 153, 255));
        brush.Freeze();

        for (int line = startLine; line <= endLine && line < _editor.LineCount; line++)
        {
            int lineStartIdx = _editor.GetCharacterIndexFromLineIndex(line);
            int lineLength = _editor.GetLineLength(line);
            if (lineLength < 0) continue;

            // Clamp columns to actual line length (excluding line ending)
            string lineText = _editor.GetLineText(line);
            int actualLen = lineText.TrimEnd('\r', '\n').Length;

            int colStart = Math.Min(startCol, actualLen);
            int colEnd = Math.Min(endCol, actualLen);

            if (colStart == colEnd && startCol != endCol)
            {
                // Line is shorter than selection - show cursor line
                colEnd = colStart;
            }

            if (colStart >= colEnd && startCol == endCol)
                continue;

            try
            {
                var rectStart = _editor.GetRectFromCharacterIndex(lineStartIdx + colStart);
                Rect rectEnd;

                if (colEnd > colStart)
                {
                    rectEnd = _editor.GetRectFromCharacterIndex(lineStartIdx + colEnd);
                }
                else
                {
                    rectEnd = rectStart;
                    rectEnd.X += 2; // Cursor width
                }

                var selectionRect = new Rect(rectStart.TopLeft, rectEnd.BottomRight);
                drawingContext.DrawRectangle(brush, null, selectionRect);
            }
            catch
            {
                // GetRectFromCharacterIndex can throw for invalid indices
            }
        }
    }
}
