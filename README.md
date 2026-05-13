# ⚒️ GumpForge

**Visual Gump Editor for Ultima Online**

GumpForge is a desktop application for designing Ultima Online Gump (Graphical User Menu Pop-up) interfaces. It provides a full-featured visual canvas editor with real-time code generation for multiple UO server emulators.

## Features

### 🎨 Visual Canvas Editor
- Drag-and-drop element placement from the built-in Asset Browser
- Resize handles, snap-to-grid, smart alignment guides
- Multi-select, grouping, distribution, z-order controls
- Zoom, pan, and ruler-based guide lines
- Multi-page gump support with page navigation
- Color tagging for visual element organization

### 📂 UO Client Data Support
- **MUL format** — Reads `gumpart.mul` / `gumpidx.mul` (legacy clients)
- **UOP format** — Reads `gumpartLegacyMUL.uop` (modern clients, auto-detected)
- **Hues** — Full hue rendering with ARGB1555 palette lookup (`hues.mul`)
- **Cliloc** — Localized text preview from `cliloc.enu`
- **Fonts** — Font metrics from `fonts.mul` for accurate label sizing

### 💻 Code Generation
Generates complete C# gump classes for **5 server emulators**:
- ServUO (editable — parse & apply back to canvas)
- RunUO
- ModernUO
- Sphere
- ClassicAssist

### 📦 Import & Export
- **PNG Export** (F5) — Screenshot the canvas
- **MUL Export** (F6) — Write gump art directly to `gumpart.mul`
- **Custom Import** — Import PNG images as custom gump art
- **Project Files** — Save/load `.gumpproj` project files

### ⚡ Editor Features
- C# syntax highlighting (TextMate / DarkPlus theme)
- Undo/redo with full command history
- Clipboard (cut/copy/paste/duplicate)
- Drag-scrub numeric properties
- Validation engine with Problems panel
- Inline element renaming in the Layers panel

## Getting Started

### Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later

### Build & Run
```bash
dotnet build GumpForge.sln
dotnet run --project src/GumpForge.App
```

### Publish as Self-Contained EXE
```bash
dotnet publish src/GumpForge.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

### Usage
1. **File → Open Client Folder** — Point to your UO client data directory
2. Browse assets in the left panel, double-click or drag to place on canvas
3. Edit properties in the right panel
4. Copy generated code from the bottom Code panel
5. **F1** for the built-in Quick Start Guide

## Architecture

```
GumpForge.sln
├── src/
│   ├── GumpForge.App         # Avalonia UI application (views, controls, services)
│   ├── GumpForge.Core        # Models, commands, undo/redo, validation
│   ├── GumpForge.Formats     # Binary file readers (MUL, UOP, Hues, Cliloc, Fonts)
│   ├── GumpForge.Generators  # Code generators (ServUO, RunUO, ModernUO, Sphere, ClassicAssist)
│   ├── GumpForge.Parsers     # Code parsers (ServUO → canvas)
│   └── GumpForge.Rendering   # SkiaSharp pixel rendering
└── tests/
    ├── GumpForge.Core.Tests
    ├── GumpForge.Formats.Tests
    └── GumpForge.Generators.Tests
```

## Tech Stack
- **UI Framework:** [Avalonia UI](https://avaloniaui.net/) 11.x
- **MVVM:** CommunityToolkit.Mvvm 8.4.2
- **Rendering:** SkiaSharp 3.119.2
- **Code Editing:** AvaloniaEdit + TextMateSharp (DarkPlus theme)
- **Image Processing:** SixLabors.ImageSharp 3.1.11
- **Code Analysis:** Microsoft.CodeAnalysis.CSharp (Roslyn) 5.3.0

## License

### Creative Commons Attribution-NonCommercial 4.0 International

This work is licensed under the Creative Commons Attribution-NonCommercial 4.0
International License. To view a copy of this license, visit
http://creativecommons.org/licenses/by-nc/4.0/ or send a letter to
Creative Commons, PO Box 1866, Mountain View, CA 94042, USA.

Under this license, you are free to:
- **Share** — Copy and redistribute the material in any medium or format.
- **Adapt** — Remix, transform, and build upon the material.

Under the following terms:
- **Attribution** — You must give appropriate credit, provide a link to the license,
  and indicate if changes were made.
- **NonCommercial** — You may not use the material for commercial purposes.
