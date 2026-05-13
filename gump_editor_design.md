# Modern Gump Editor — Design Document

**Project codename:** *Aetheric* (working name — a visual + code editor for Ultima Online gumps)
**Status:** Draft v0.1
**Author:** Design draft prepared with Claude

---

## 1. Executive Summary

Aetheric is a desktop application for authoring and editing Ultima Online gumps for custom shards. It combines a Fiddler-style asset browser with a visual WYSIWYG canvas and a live, bidirectional code panel. The editor targets the workflows of shard developers writing dialogs and UI for RunUO, ServUO, ModernUO, Sphere, TazUO, and ClassicAssist, and treats client-side art (`gumpart.mul` / `gumpartlegacy.uop`) and server-side gump scripts as two sides of the same editing surface.

The current ecosystem forces shard devs to bounce between UO Fiddler (asset inspection), a code editor (writing `AddImage` / `AddButton` calls by hand), and an in-game test client (to see what the gump actually looks like). Aetheric collapses that loop: drag the asset, see the layout, get the code, paste it into your shard.

---

## 2. Goals and Non-Goals

### Goals
- Visual canvas-based gump composition with drag, snap, multi-select, layers, and groups.
- Live, bidirectional code panel — visual edits update code; code edits update the canvas.
- First-class support for multiple emulator/client targets via pluggable code generators.
- Native read/write of `.mul`/`.idx` and `.uop` gump art containers.
- Import of arbitrary PNG/BMP/TGA assets, with allocation into a chosen gump ID range.
- Cross-platform desktop (Windows primary, Linux as a stretch goal — see §4).

### Non-Goals (at least for v1)
- Not a full Fiddler replacement (no map/tile/animation editing — gump-focused only).
- Not a runtime — does not connect to a live shard or replace an in-game gump host.
- Not a localization editor (clilocs are referenced, not authored, in v1).
- Not a code IDE — the code panel is a generator and parser, not a general-purpose editor.

---

## 3. Target Users & Use Cases

**Primary:** custom shard developers and content creators who already write gump code by hand.

**Use cases driving the design:**
1. *"I'm building a quest journal gump."* User drags a parchment background, drops buttons and labels, exports as a `ServUO Gump` subclass.
2. *"I need to reskin an existing vendor gump."* User pastes existing C# into the code panel, sees it rendered, swaps gump IDs visually, exports.
3. *"I have custom UI art from an artist."* User imports a folder of PNGs, assigns IDs, and writes them into `gumpart.mul` / `gumpartlegacy.uop`.
4. *"I'm porting a gump from Sphere to ServUO."* User opens a Sphere `DIALOG` block, edits in canvas, exports to the C# target.
5. *"I want a multi-page tabbed gump."* User authors with pages, sees each page in the layers tree, can preview each.

---

## 4. Technology Stack

### Recommendation: **.NET 8 + Avalonia UI (C#)**

**Why:**
- RunUO, ServUO, and ModernUO are all C#. The gump element vocabulary (`Gump`, `GumpImage`, `GumpButton`, etc.) maps directly onto a C# domain model — and in many cases existing emulator source can be referenced as a parsing oracle.
- Avalonia is a mature, cross-platform XAML UI framework that runs on Windows, Linux, and macOS from a single codebase. This satisfies the Linux bonus without dual-coding.
- `SkiaSharp` (which Avalonia uses under the hood) is ideal for the high-performance canvas — gump composition is fundamentally 2D blitting and Skia handles that well.
- Strong ecosystem for binary file I/O (`BinaryReader`, `System.IO.Pipelines`) — important for `.mul`/`.uop` parsing.

**Runners-up considered:**
- **Tauri (Rust + web frontend):** smallest binaries, great Linux story, but you lose the C# domain alignment and have to re-implement gump semantics.
- **Electron:** fastest to prototype UI, but heavy, and the binary file work is awkward in JS.
- **Qt/C++:** maximum performance, maximum effort.

### Key libraries
- **Avalonia 11** — UI toolkit
- **SkiaSharp** — canvas rendering, image manipulation
- **CommunityToolkit.Mvvm** — MVVM plumbing, observable models
- **MessagePack** or **System.Text.Json** — project file serialization
- **Roslyn** (`Microsoft.CodeAnalysis.CSharp`) — for parsing pasted ServUO/RunUO gump source, *not* for compilation. Roslyn's syntax tree gives accurate `AddImage(...)`/`AddButton(...)` extraction without regex fragility.
- **ImageSharp** or **SkiaSharp.Codec** — PNG/BMP/TGA import

---

## 5. File Format Support

### Read/Write
| Format | Purpose | Status |
|---|---|---|
| `gumpart.mul` + `gumpidx.mul` | Legacy gump art container | Full r/w (v1) |
| `gumpartlegacy.uop` | Post-HS gump art container | Full r/w (v1) |
| `hues.mul` | Color tables for hued elements | Read-only (v1) |
| `fonts.mul` / `unifont*.mul` | Font metrics for label preview | Read-only (v1) |
| `cliloc.*` | Localized text lookup for `AddHtmlLocalized` | Read-only (v1) |

### Notes on the binary formats
- **MUL/IDX:** classic format. `gumpidx.mul` is a sequence of 12-byte records `(lookup, length, extra)`; `gumpart.mul` holds the pixel payloads. Pixel format is 16-bit ARGB1555 with a per-row lookup table for run-length decoding.
- **UOP:** Mythic's package format introduced with UO:HS. Zlib-compressed blocks indexed by 64-bit hash (Jenkins hash of a path string like `build/gumpartlegacy/{id:D8}.tga`). Hash collision resolution is by chain.
- Both formats must round-trip cleanly — read, edit one gump, write — without corrupting unrelated entries. This is a non-negotiable correctness requirement.

### Project file (Aetheric's own format)
- `.gumpproj` — JSON or MessagePack, containing:
  - Document metadata (canvas size, target emulator, target gump class name)
  - Tree of elements with full property bags
  - Layer/group structure
  - References to asset files (paths, hashes for change detection)
  - Custom asset imports (PNG sources + assigned gump IDs)

---

## 6. Application Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                          UI Layer (Avalonia)                    │
│  ┌──────────┬──────────────────────┬───────────────────────┐   │
│  │  Asset   │   Canvas / Workspace │  Layers + Properties  │   │
│  │ Browser  │      (SkiaSharp)     │       (panels)        │   │
│  ├──────────┴──────────────────────┴───────────────────────┤   │
│  │                  Code Panel (live, tabbed)              │   │
│  └─────────────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────────────┤
│                         Document Model                          │
│  GumpDocument → Pages → Layers/Groups → GumpElement (abstract)  │
│  Observable. All edits go through commands (undo/redo).         │
├─────────────────────────────────────────────────────────────────┤
│   Code Generators        │   Code Parsers      │   Renderer     │
│   (target-pluggable)     │   (target-pluggable)│   (SkiaSharp)  │
├──────────────────────────┴─────────────────────┴────────────────┤
│                       Asset Subsystem                           │
│   MUL reader/writer  │  UOP reader/writer  │  Asset cache       │
└─────────────────────────────────────────────────────────────────┘
```

### Key principle: target-agnostic core
The document model knows about *gump elements* (Background, Image, Button, Label, TextEntry, Page, etc.), not about C# or Sphere syntax. Generators and parsers are pluggable strategies. Adding a new emulator target is implementing two interfaces:

```csharp
public interface IGumpCodeGenerator
{
    string TargetName { get; }            // e.g. "ServUO"
    string FileExtension { get; }         // e.g. ".cs"
    string Generate(GumpDocument doc, GenerationOptions opts);
}

public interface IGumpCodeParser
{
    string TargetName { get; }
    bool CanParse(string source);
    ParseResult Parse(string source);     // → GumpDocument or errors
}
```

### Command pattern for edits
Every mutation — move, resize, add, delete, regroup, property change — is a `IEditCommand` pushed onto an undo stack. This is what makes the code panel's bidirectional editing safe: a code-side change is just another command (or batch of commands) being applied to the model.

---

## 7. UI/UX Design

### Window layout (default)

```
┌─ Menu / Toolbar ───────────────────────────────────────────────┐
├──────────┬──────────────────────────────────┬──────────────────┤
│          │                                  │  Layers          │
│  Asset   │                                  │  ─────────       │
│ Browser  │       Canvas / Workspace         │  Properties      │
│          │       (with rulers, grid)        │                  │
│          │                                  │                  │
│          │                                  │                  │
├──────────┴──────────────────────────────────┴──────────────────┤
│  Code Panel  [ServUO] [RunUO] [Sphere] [ClassicAssist] [Tazuo] │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

All side panels are dockable/detachable (Avalonia supports this via `Dock.Avalonia` or similar).

### Asset Browser (left)
- Tree at top: open art files (`gumpart.mul`, an imported custom set, etc.).
- Grid view of thumbnails below, with:
  - ID filter / range filter / search by ID
  - Tag filter (Background, Button, Decorative, etc. — user-assigned)
  - "Show only custom assets" toggle
- Click to preview at full size in a flyout; **drag** onto canvas to place.
- Right-click → "Replace with image…", "Export PNG…", "Delete from file" (with confirmation).
- Bottom strip: "Import images…" button → opens the import wizard (§9.3).

### Canvas / Workspace (center)
- Rulers on top and left, pixel-accurate.
- Snap targets:
  - Pixel grid (default 1px, configurable to 2/5/10)
  - Other elements' edges and centers (smart guides)
  - User-defined guide lines (drag from rulers)
- Element interactions:
  - Click to select; shift-click to add to selection; marquee select
  - Drag to move; arrow keys nudge (1px, shift+arrow for 10px)
  - Resize handles (for elements that support it — backgrounds, tiled images, text entries, HTML)
  - Right-click context menu: Bring Forward / Send Back / Group / Ungroup / Duplicate / Delete
- Page selector along the top of the canvas — gumps with multiple `AddPage` regions render one page at a time, with the other pages dimmed or hidden.
- Zoom (Ctrl+wheel), pan (middle-drag), Fit, 1:1.

### Layers Panel (top right)
- Tree of pages → groups → elements, in z-order (top = front).
- Drag to reorder (changes z-order).
- Drag into a group node to nest.
- Visibility toggle, lock toggle per row.
- Inline rename.
- Color tag (organizational only).

### Properties Panel (bottom right, under Layers)
- Context-sensitive to selection.
- Common fields: `X`, `Y`, `Width`, `Height`, layer name, locked state.
- Type-specific fields:
  - **Image**: gump ID (with picker that opens Asset Browser scoped), hue
  - **Background**: gump ID, width, height (9-slice preview)
  - **Button**: normal ID, pressed ID, button type (Reply/Page), param, return code
  - **Label**: text, hue, font, cropped width
  - **TextEntry**: entry ID, initial text, width, height, max length
  - **Check / Radio**: inactive ID, active ID, group ID, switch ID, initial state
  - **HTML**: text, scrollbar, background, width, height
- Numeric fields support drag-scrubbing (click-and-drag on label).
- Editing `X` or `Y` here immediately moves the element on the canvas — and the code panel updates in the same tick.

### Code Panel (bottom)
- Tabbed by target. The active tab is the "source of truth" for paste-in/out, but **all tabs stay synchronized** because they're all generated from the same model.
- Read mode: shows generated code, syntax-highlighted. Cannot be edited inline by default (because the model is the source of truth).
- Edit mode toggle: switches into a real editor for that target. On commit (or on a debounced delay), the code is parsed and the model is updated. Elements whose code changed are highlighted on the canvas briefly.
- "Copy to clipboard" button per tab.
- Parse errors surface as inline diagnostics with line numbers.

---

## 8. Element Model

The internal vocabulary is the union of what the supported targets need. Approximate v1 set:

| Element | Notes |
|---|---|
| `Page` | Logical container; gumps use 0 = always visible, plus AddPage(n) regions |
| `Background` | 9-slice from gump ID |
| `Image` | Static gump pic, optional hue |
| `ImageTiled` | Repeating image in a rect |
| `AlphaRegion` | Translucent dim rectangle |
| `Button` | Normal + pressed art, type, param, return |
| `Check` | Checkbox |
| `Radio` | Radio button (grouped by switch ID) |
| `Label` | Single-line text, hue, font |
| `LabelCropped` | Clipped to width |
| `Html` | Wrapped text region |
| `HtmlLocalized` | Cliloc-driven text |
| `TextEntry` | Editable text field |
| `Item` | Item art (not gump art — uses `art.mul`) |
| `Tooltip` | Cliloc tooltip on previous element |
| `Group` | Editor-only construct; flattens to a sequence of child Add* calls |

`Group` is important to call out: in the code, groups don't exist — the generator emits the group's children in order with their absolute coordinates. But for the editor, grouping is essential for "drag this whole window together" workflows.

---

## 9. Key Workflows

### 9.1 Drag-drop authoring (the common case)
1. User opens `gumpart.mul` from their client folder; Asset Browser populates.
2. User filters to backgrounds (IDs in the 9200–9400 / 5054 / 3500 ranges, etc.).
3. Drags a paper background onto the canvas — it appears, scaled to a default size, with handles.
4. Drags a button art onto the canvas — Properties panel asks "Normal art?" and offers to also set Pressed art with the next-numbered gump (UO convention: pressed = normal + 1).
5. Adds a label; types text in the Properties panel; picks a hue from the hue picker (which uses `hues.mul`).
6. Code panel already shows a complete ServUO `Gump` subclass. User copies it to their shard project.

### 9.2 Reverse-engineering an existing gump
1. User pastes a `Gump` subclass into the Code Panel (ServUO tab).
2. Parser walks the Roslyn syntax tree, identifies `AddImage(...)`, `AddButton(...)`, etc. calls in `BuildGump()` / constructor.
3. Model is populated; canvas renders the gump.
4. User can now rearrange, restyle, and regenerate.

**Parsing notes:** the parser handles literal arguments cleanly and tolerates simple constant references (e.g. `AddImage(x, y, GumpIds.MyBackground)`). Dynamic / computed arguments (`AddImage(x, y, GetBgForUser(user))`) become *opaque expressions* — the element is created with a placeholder, marked as "dynamic", and the original expression is preserved verbatim on regeneration. The model never silently rewrites code it doesn't understand.

### 9.3 Importing custom art
1. User clicks "Import images" → wizard opens.
2. Selects one or more PNGs (with alpha).
3. Wizard prompts: target file (`gumpart.mul` or `gumpartlegacy.uop`), starting gump ID, per-image overrides for ID.
4. Conflict detection: warns if a chosen ID is already populated and offers Replace / Skip / Pick new ID.
5. On confirm: images are encoded to the format's expected pixel layout (ARGB1555 with RLE for MUL; TGA-in-UOP for UOP) and written. The file is backed up to `*.bak` first.
6. New entries appear in the Asset Browser, tagged "Custom".

### 9.4 Multi-target export
- Right pane shows live ServUO code by default.
- User clicks the Sphere tab → same gump regenerated as a `DIALOG`/`DIALOGITEM` block.
- "Export…" menu writes the active tab's contents to a file with the target's extension.

---

## 10. Code Generators — Sketches

### ServUO / RunUO / ModernUO (C#)
```csharp
public class QuestJournalGump : Gump
{
    public QuestJournalGump() : base(100, 100)
    {
        Closable = true; Disposable = true; Draggable = true; Resizable = false;

        AddPage(0);
        AddBackground(0, 0, 400, 300, 9270);
        AddImage(20, 20, 0x589);
        AddLabel(60, 22, 1153, "Quest Journal");
        AddButton(360, 20, 4017, 4018, 1, GumpButtonType.Reply, 0);
    }

    public override void OnResponse(NetState sender, RelayInfo info)
    {
        // generated stub
    }
}
```

### Sphere
```
[DIALOG questjournal]
100,100
[DIALOG questjournal TEXT]
[BUTTON]
[PAGE 0]
DIALOGITEM dresizepic 0 0 9270 400 300
DIALOGITEM dgumppic 20 20 1417
DIALOGITEM dtext 60 22 1153 0
DIALOGITEM dbutton 360 20 4017 4018 1 0 0
```

### ClassicAssist (Python)
```python
def quest_journal_gump():
    g = Gump(100, 100)
    g.AddBackground(0, 0, 400, 300, 9270)
    g.AddImage(20, 20, 0x589)
    g.AddLabel(60, 22, 1153, "Quest Journal")
    g.AddButton(360, 20, 4017, 4018, 1, GumpButtonType.Reply, 0)
    return g
```

### TazUO
TazUO consumes server-sent gump packets like any classic client, so its "target" is really the same C# the server emits. The TazUO tab in v1 will primarily exist to (a) preview TazUO-specific behaviors (grid containers, modern scrollbars), and (b) emit TazUO-flavored client-side macros where relevant. Full TazUO support — including its newer gump features — is a v2 item; the design treats it as a renderer profile plus a generator variant rather than a totally separate target.

---

## 11. Rendering & Performance

- All canvas drawing goes through SkiaSharp on a `SKCanvas` backed by an Avalonia control.
- Gump art is decoded once on file load into RGBA8888 `SKBitmap`s and cached. Cache is LRU-bounded (default 256 MB).
- 9-slice backgrounds: pre-slice into 9 sub-bitmaps on first use, then redraw cheaply at any size.
- Hue rendering: hues are applied via a Skia color filter generated from `hues.mul` (each hue is a 32-entry palette that remaps grayscale to colored).
- Target: 60 fps interaction with up to ~500 elements on screen. Above that, draw is throttled during drag.

---

## 12. Snapping, Alignment, and Selection Details

- **Pixel grid:** active by default, 1 px granularity. Configurable.
- **Smart guides:** when dragging, compute alignment candidates against (a) other elements' edges and centers within current page, (b) user guides, (c) canvas edges and center. Show pink guide lines and snap within a 3 px threshold.
- **Hold Alt** to temporarily disable snapping.
- **Alignment toolbar** (with multi-selection): Align Left / Right / Top / Bottom / Horizontal Center / Vertical Center; Distribute Horizontally / Vertically.
- **Group bounding box** is the union of children; dragging the group moves children together; resizing a group (v2) scales the layout proportionally.

---

## 13. Validation & Diagnostics

A "Problems" panel (toggleable, in the bottom dock with the code panel) surfaces:
- Duplicate button IDs (causes silent overwrites in `OnResponse`)
- Switch IDs reused across unrelated checks/radios
- Elements with gump ID 0 or unknown gump ID (asset missing)
- Elements partially or fully outside the canvas bounds
- Pages referenced by buttons but never defined with `AddPage(n)`
- Custom assets referenced but no longer present in the source file
- Tooltip applied to no preceding element

Each problem links back to the offending element (selects it on the canvas).

---

## 14. Cross-Platform Considerations

- **Windows:** primary target. UO client typically lives here, so file paths and font availability are straightforward.
- **Linux:** Avalonia runs natively; SkiaSharp ships native binaries for `linux-x64` and `linux-arm64`. The two areas to watch are:
  1. **File path conventions** — store all paths in the project file as relative-to-project or as an explicit base-dir reference; never assume `C:\...`.
  2. **Font rendering** — UO's bitmap fonts are loaded from `fonts.mul` / `unifont*.mul`, so we render text ourselves from those tables rather than relying on system fonts. This actually *helps* Linux parity: the text looks identical to in-game regardless of host OS.
- **macOS:** not a stated requirement, but Avalonia + SkiaSharp gives it to us nearly free; defer to v2 unless a tester asks.

---

## 15. MVP Scope (v1.0)

**In:**
- Open/save `gumpart.mul`+`gumpidx.mul` and `gumpartlegacy.uop` (round-trip safe)
- Asset Browser with filter/search/drag-to-canvas
- Canvas with pan/zoom/snap/marquee/multi-select
- Element types: Background, Image, ImageTiled, AlphaRegion, Button, Check, Radio, Label, LabelCropped, Html, HtmlLocalized, TextEntry, Page, Group (editor-only)
- Layers + Properties panels with live bidirectional binding
- Code panel for **ServUO** (full r/w) and **RunUO** (same generator, minor flag differences)
- Custom PNG import → write into MUL or UOP at chosen ID
- Undo/redo, project save/load (`.gumpproj`)
- Problems panel with the validations in §13

**Out (deferred):**
- Sphere, ClassicAssist, TazUO generators (v1.1 — generators only; parser is harder)
- macOS build
- Localization editor for clilocs
- Animation/cursor art
- Direct shard connection / live preview

### Rough v1 effort estimate
For a single experienced .NET dev: ~4–6 months full-time. The MUL/UOP I/O and the bidirectional Roslyn-based code sync are the two biggest items; everything else is well-understood UI work.

---

## 16. Risks & Open Questions

1. **UOP round-trip correctness.** Mythic's hash-and-chain layout has edge cases (hash collisions, unaligned chunks). Mitigation: build a regression corpus of real `gumpartlegacy.uop` files and verify byte-identical round-trips before adding any write features that touch the rest of the file.
2. **Parser fragility on hand-written code.** Real-world ServUO gumps use helper methods, conditionals, loops. The parser must degrade gracefully — *opaque expressions* (§9.2) are the escape valve, but UX for them needs care.
3. **Sphere DIALOG syntax variance.** Different Sphere versions (56b, X, etc.) have slightly different DIALOGITEM verbs and argument orders. Decide which to target as the canonical Sphere generator; document the rest as variants.
4. **TazUO scope.** TazUO has features (grid containers, modern UI primitives) that don't exist in classic gumps. Punt to v2 with a clear "TazUO-extended elements" namespace in the model.
5. **Asset licensing.** Custom assets imported by users are theirs; we don't ship UO art. The app should refuse to start without the user pointing it at their own client folder.

---

## 17. Out-of-Scope but Worth Mentioning

- **Live shard preview** via a small ServUO/ModernUO plugin that sends the gump to a connected client on save — appealing v2 feature, but a different architecture problem.
- **Git-friendly project files** — `.gumpproj` should be deterministic JSON (sorted keys, stable element IDs) so designers can diff and merge.
- **Theme/skin templates** — let users save and reapply a hue+gumpID palette across a project for fast reskinning.

---

## Appendix A — Pixel Format Reference

`gumpart.mul` entries are width × height of 16-bit ARGB1555 pixels, preceded by a per-row lookup table giving offsets into a run-length-encoded stream:

```
[ lookup table: height × uint32 ]
[ RLE rows: for each row, sequence of (run_header: uint16, pixels: uint16[]) ]
  where run_header = (transparent_run_length << 12) | colored_run_length (mask with 0xFFF)
  row terminator: 0x0000
```

A reference decoder/encoder pair is the single most error-prone piece of MUL I/O — recommend lifting a known-good implementation from UO Fiddler or ServUO's `Ultima.dll` (both MIT-friendly) and writing a fuzz test that round-trips every gump in a stock `gumpart.mul`.

## Appendix B — Suggested Repo Layout

```
Aetheric/
├── src/
│   ├── Aetheric.Core/            # Document model, commands, undo stack
│   ├── Aetheric.Formats/         # MUL/UOP/IDX readers and writers
│   ├── Aetheric.Generators/      # IGumpCodeGenerator implementations
│   ├── Aetheric.Parsers/         # IGumpCodeParser implementations
│   ├── Aetheric.Rendering/       # SkiaSharp drawing, hue/font helpers
│   └── Aetheric.App/             # Avalonia UI
├── tests/
│   ├── Aetheric.Formats.Tests/   # round-trip corpus tests
│   └── Aetheric.Generators.Tests/
├── samples/
│   └── gumps/                    # example .gumpproj files
└── docs/
    └── this design doc
```
