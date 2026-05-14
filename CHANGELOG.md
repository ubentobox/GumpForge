# GumpForge v2.0 — Release Notes

**Visual Gump Editor for Ultima Online**
Release Date: May 14, 2026

---

## 🆕 What's New in v2.0

### 🏷️ Asset Tagging System
- **Auto-Tagger** — Automatically classifies gump assets into categories (cursor, button, scroll, container, paperdoll, spell-icon, etc.) based on well-known UO ID ranges
- **30 default tag rules** covering the full gump art ID space — from cursors (0–4) to custom assets (30000+)
- **Dimension-based tagging** — Assets are also tagged by size: icon, border, large-background, header-bar, wide, tall, square
- **containers.txt integration** — Reads container definitions from your client data to tag and name container gumps automatically
- **Manual tags** — Add your own tags to any asset, with instant inline editing
- **Tag suppression** — Remove an auto-tag from an asset permanently. It won't come back when the auto-tagger runs again

### 📁 Collections
- **Create named collections** to organize assets into groups (e.g. "Login UI", "Vendor Gumps", "Spell Icons")
- **Checkbox assignment** — Toggle collection membership directly from the asset metadata panel
- **Full CRUD** — Create, rename, and delete collections from the Tag & Collection Manager

### 🔧 Tag & Collection Manager (New Window)
A dedicated 3-tab management interface accessible from **Tags → Tag & Collection Manager**:

| Tab | What it does |
|-----|-------------|
| **📐 Auto-Tag Rules** | View, edit, add, delete, enable/disable ID range → tag rules. Reset to defaults. Run the auto-tagger. |
| **🏷️ Tag Manager** | Browse all tags in use. See which assets have each tag. Rename tags globally. Delete tags. Remove a tag from all assets. |
| **📁 Collections** | Create, rename, delete collections. View member assets. Remove assets or clear a collection. |

### 🖱️ Multi-Select & Bulk Operations
- **Multi-select** assets in the Asset Browser (Ctrl+Click / Shift+Click)
- **Bulk Add Tag** — Apply a tag to all selected assets at once ("+" button or "All" button)
- **Bulk Remove Tag** — Remove a tag from all selected assets
- **Bulk Add to Collection** — Assign multiple assets to a collection simultaneously
- Selected count displayed in the metadata panel header

### 🏷️ Interactive Tag Badges
- Tags displayed as colored badges: **green** for user tags, **blue (with ⚙)** for auto-tags
- **Click** tag text → filters the Asset Browser by that tag
- **Click ✕** → removes the tag (auto-tags are permanently suppressed)

### 📋 Tags Menu
New top-level **Tags** menu in the menu bar with quick access to:
- Tag & Collection Manager
- Add/Remove Tag to Selected (with dialog)
- Run Auto-Tagger
- Edit Auto-Tag Rules
- Save Profile

---

## 📁 Shard Profiles
- **Per-shard configuration** saved as `.gfprofile` JSON files
- Stores: client data path, editor preferences (grid, snap, canvas size), all asset metadata, tags, collections, and auto-tag rules
- Profiles load automatically — your tags and collections persist across sessions
- Profile selector on first launch with create/load options

---

## 🔧 UI Improvements
- **Proportional panel layout** — Bottom panels now scale correctly across different screen resolutions
- **Properties panel scrollbar** — All property controls are now accessible on smaller screens
- **Profile window scroll fix** — "Create Profile" button is always reachable on small windows
- **Asset metadata panel** — Now correctly shows actual collection membership state (checked/unchecked)
- **Tag removal** — ✕ buttons now work reliably via proper Button controls (replaced broken PointerPressed approach)

---

## 🐛 Bug Fixes
- Fixed collection checkboxes always showing as checked regardless of actual membership
- Fixed tag removal ✕ buttons not responding to clicks in the asset metadata panel
- Fixed UI overflow issues in the Profile window (contributed via PR #1 by AkaMagician)
- Fixed Properties panel content being cut off on smaller resolutions

---

## 📊 Build Info
- **Platform:** Windows x64, self-contained (.NET 9)
- **Build:** 0 warnings, 0 errors
- **Tests:** All passing (3/3 test suites)

---

## 🚀 Getting Started
1. Download `GumpForge.App.exe` (self-contained, no .NET install required)
2. Run the EXE and create a Shard Profile pointing to your UO client data folder
3. Assets load automatically — start designing gumps!
4. Press **F1** for the built-in Quick Start Guide
