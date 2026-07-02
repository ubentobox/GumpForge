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

### 🌐 Game Server Link
- **Secure Link Connection** — Connect directly to your game server (ServUO/RunUO) using staff credentials.
- **Dynamic Gump Capture** — Run the `[gf` command in-game and target any item to automatically load its live gump layout (like spellbooks, quest menus, or containers) onto the GumpForge canvas.
- **In-Game Player Inspection** — Target players to sync their stats, skills, and custom variables into GumpForge for contextual testing.
- **Server Script Hook (`GumpForgeLink.cs`)** — A lightweight, backward-compatible, and security-conscious C# script that handles server-side communication and property reflection.
- **Security Redaction** — Automatically strips sensitive database attributes (passwords, hashes, IP addresses) before sending data to GumpForge.

### 👤 Player Context Profiles
- **Profile Management** — Add, edit, rename, and delete player context profiles inside GumpForge.
- **Local/Reflected Variables** — Populate profiles with core stats, skills, or custom properties (e.g. `JediLevel`, `IsMonk`) to evaluate conditional scripts.
- **Variable Auto-Import** — When targeting items/players, GumpForge detects new properties and prompts to import them into your active profile.

### 🎮 Interactive Simulation Mode
- **Design vs. Play Mode** — Toggle between editing gump designs and testing interactive behavior.
- **Dynamic Controls** — Live checkbox toggling, radio button group scoping, and keyboard text entry typing.
- **Action Simulation** — Click page navigation buttons to flip gump pages, double-click item tiles to simulate looting, and adjust numeric counters (e.g. stat or crafting menus).
- **Replacements & Context Resolution** — Resolves C# member lookups (e.g. `from.Name` or `owner.AccessLevel`) and logical `if/else` statements dynamically using the active player context.


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

### 🏷️ Asset Tagging & Collections
- **Auto-Tagger** — Automatically classifies assets by ID range (e.g. "cursor", "button", "container"), dimensions, and data sources like `containers.txt`
- **Editable Tag Rules** — Customize, add, disable, or remove auto-tag rules through the Tag & Collection Manager
- **Manual Tags** — Add, rename, and remove user-defined tags on any asset
- **Tag Suppression** — Remove auto-tags permanently; they won't return when the auto-tagger runs again
- **Collections** — Organize assets into named groups with full CRUD management
- **Multi-Select Bulk Ops** — Select multiple assets and tag or assign them to collections in one action
- **Click-to-Filter** — Click any tag badge to instantly filter the Asset Browser by that tag

### 📁 Shard Profiles
- Per-shard `.gfprofile` stores client path, editor preferences, tags, collections, and auto-tag rules
- Profiles load automatically on startup
- All metadata persists across sessions

### ⚡ Editor Features
- C# syntax highlighting (TextMate / DarkPlus theme)
- Undo/redo with full command history
- Clipboard (cut/copy/paste/duplicate)
- Drag-scrub numeric properties
- Validation engine with Problems panel
- Inline element renaming in the Layers panel
- Scrollable, proportionally-scaled panel layout

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
5. **Tags → Tag & Collection Manager** — Manage asset organization
6. **F1** for the built-in Quick Start Guide

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
