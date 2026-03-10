# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Nopad is a minimal Notepad replacement built with WPF on .NET 10. Single-project solution targeting `net10.0-windows` with `win-x64` self-contained publishing.

## Build & Run

```bash
# All commands run from src/
cd src

# Build
dotnet build

# Run
dotnet run

# Publish single-file executable
dotnet publish -c Release -r win-x64
```

No tests exist. No linter configured.

## Architecture

Single-window WPF app with two source files beyond `App.xaml.cs`:

- **`MainWindow.xaml/.cs`** — The entire editor. Contains file I/O, keyboard shortcuts, zoom, line numbers, block selection (Shift+Alt+Arrow), tab indent/unindent, and the `BlockSelectionAdorner` class (renders column selection via WPF adorner layer). Settings persisted as JSON to `%APPDATA%/Nopad/settings.json` via the `AppSettings` class (also in this file).
- **`FindReplaceControl.xaml/.cs`** — UserControl for find/replace bar. Attached to the editor TextBox via `Attach()`. Supports match case, whole word, wrap-around search, and replace all.

Key design decisions:
- Uses a plain WPF `TextBox` (not RichTextBox or third-party editor), with `UndoLimit="500"`.
- Line numbers are rendered via an `ItemsControl` on a `Canvas`, synced to the editor's `ScrollViewer` on scroll events.
- Block/column selection is custom — tracked via anchor/caret line+col fields, rendered with a custom `Adorner`, and handles typing/backspace/delete within the block.
- App closes without save prompts.
- Follows WPF Fluent theme (`ThemeMode="System"` in App.xaml).
- Encoding detection supports UTF-8 (with/without BOM), UTF-16 LE/BE. Line ending detection preserves CRLF vs LF.
