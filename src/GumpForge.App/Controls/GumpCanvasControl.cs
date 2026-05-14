using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using GumpForge.App.ViewModels;
using GumpForge.App.Helpers;
using GumpForge.App.Services;
using GumpForge.Core.Commands;
using GumpForge.Core.Models;
using System.Globalization;

namespace GumpForge.App.Controls;

/// <summary>
/// Custom canvas control for rendering gump elements with full interaction:
/// - Grid, rulers, center crosshair
/// - Element rendering with selection handles
/// - Click/shift-click/marquee selection
/// - Drag to move, resize handles
/// - Pan (middle mouse) and zoom (Ctrl+wheel)
/// - Arrow key nudge
/// </summary>
public class GumpCanvasControl : Control
{
    private CanvasViewModel? _vm;
    private Point _dragStart;
    private Point _dragElementStart;
    private bool _isDragging;
    private bool _isMarquee;
    private Rect _marqueeRect;
    private GumpElement? _dragTarget;
    private int _resizeHandle = -1; // -1 = none, 0=TL, 1=T, 2=TR, 3=R, 4=BR, 5=B, 6=BL, 7=L
    private int _resizeStartW;
    private int _resizeStartH;

    // Canvas offset (centering the workspace in the control)
    private double _offsetX;
    private double _offsetY;

    // Smart guides — alignment lines shown during drag
    private const int GuideSnapThreshold = 5;
    private readonly List<double> _guideHorizontal = []; // Y positions in canvas coords
    private readonly List<double> _guideVertical = [];   // X positions in canvas coords

    // Debounce flag — prevents thousands of queued InvalidateVisual calls
    private volatile bool _invalidatePending;

    public GumpCanvasControl()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /// <summary>
    /// Schedules a single repaint. Multiple calls before the next render
    /// are coalesced into one, preventing UI freeze from cascade storms.
    /// </summary>
    private void RequestRepaint()
    {
        if (_invalidatePending) return;
        _invalidatePending = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _invalidatePending = false;
            InvalidateVisual();
        }, Avalonia.Threading.DispatcherPriority.Render);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _vm = DataContext as CanvasViewModel;
        if (_vm is null) return;

        // Repaint when ViewModel properties change (zoom, pan, grid, document swap)
        _vm.PropertyChanged += (_, args) =>
        {
            // If the Document was swapped (e.g., by ApplyCode), re-subscribe
            if (args.PropertyName == nameof(CanvasViewModel.Document))
                SubscribeToDocument(_vm.Document);
            RequestRepaint();
        };

        // Repaint when selection changes
        _vm.Selection.PropertyChanged += (_, _) => RequestRepaint();

        // Repaint when undo/redo stack changes (element moves, adds, deletes)
        _vm.UndoStack.PropertyChanged += (_, _) => RequestRepaint();

        // Watch document pages for element add/remove
        SubscribeToDocument(_vm.Document);
    }

    private void SubscribeToDocument(GumpDocument doc)
    {
        doc.Pages.CollectionChanged += (_, _) =>
        {
            foreach (var page in doc.Pages)
                SubscribeToPage(page);
            RequestRepaint();
        };

        foreach (var page in doc.Pages)
            SubscribeToPage(page);
    }

    private void SubscribeToPage(GumpPage page)
    {
        page.Elements.CollectionChanged += (_, args) =>
        {
            // Subscribe to newly added elements
            if (args.NewItems is not null)
                foreach (GumpElement el in args.NewItems)
                    el.PropertyChanged += OnElementPropertyChanged;
            RequestRepaint();
        };

        // Subscribe to all existing elements
        foreach (var el in page.Elements)
            el.PropertyChanged += OnElementPropertyChanged;
    }

    private void OnElementPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RequestRepaint();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        _vm = DataContext as CanvasViewModel;
        if (_vm is null) return;

        var doc = _vm.Document;
        double zoom = _vm.Zoom;
        double canvasW = doc.CanvasWidth * zoom;
        double canvasH = doc.CanvasHeight * zoom;

        _offsetX = Math.Max(40, (bounds.Width - canvasW) / 2 + _vm.PanX);
        _offsetY = Math.Max(25, (bounds.Height - canvasH) / 2 + _vm.PanY);

        // Background
        context.FillRectangle(new SolidColorBrush(Color.Parse("#0a0a1a")), bounds);

        var canvasRect = new Rect(_offsetX, _offsetY, canvasW, canvasH);

        // Canvas workspace background
        context.FillRectangle(new SolidColorBrush(Color.Parse("#12122a")), canvasRect);

        // Grid
        if (_vm.ShowGrid)
            DrawGrid(context, canvasW, canvasH, zoom);

        // Center crosshair
        var centerPen = new Pen(new SolidColorBrush(Color.Parse("#252555")), 1);
        context.DrawLine(centerPen, new Point(_offsetX + canvasW / 2, _offsetY),
                                    new Point(_offsetX + canvasW / 2, _offsetY + canvasH));
        context.DrawLine(centerPen, new Point(_offsetX, _offsetY + canvasH / 2),
                                    new Point(_offsetX + canvasW, _offsetY + canvasH / 2));

        // Render elements from page 0 + active page
        RenderElements(context, doc, zoom);

        // Canvas border
        var borderPen = new Pen(new SolidColorBrush(Color.Parse("#e94560")), 1);
        context.DrawRectangle(borderPen, canvasRect);

        // Selection handles
        if (_vm.Selection.HasSelection)
            DrawSelectionHandles(context, zoom);

        // Marquee selection rectangle
        if (_isMarquee)
        {
            var marqueePen = new Pen(new SolidColorBrush(Color.Parse("#e94560")), 1,
                new DashStyle([4, 4], 0));
            var marqueeFill = new SolidColorBrush(Color.FromArgb(30, 233, 69, 96));
            context.FillRectangle(marqueeFill, _marqueeRect);
            context.DrawRectangle(marqueePen, _marqueeRect);
        }

        // Smart alignment guides
        if (_isDragging && (_guideVertical.Count > 0 || _guideHorizontal.Count > 0))
        {
            var guidePen = new Pen(new SolidColorBrush(Color.Parse("#00d4ff")), 1,
                new DashStyle([3, 3], 0));

            foreach (var gx in _guideVertical)
            {
                double screenX = _offsetX + gx * zoom;
                context.DrawLine(guidePen, new Point(screenX, 0), new Point(screenX, Bounds.Height));
            }
            foreach (var gy in _guideHorizontal)
            {
                double screenY = _offsetY + gy * zoom;
                context.DrawLine(guidePen, new Point(0, screenY), new Point(Bounds.Width, screenY));
            }
        }
        // User-defined guide lines (persistent, magenta)
        if (_vm.UserGuidesX.Count > 0 || _vm.UserGuidesY.Count > 0)
        {
            var userGuidePen = new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 100, 200)), 1,
                new DashStyle([6, 4], 0));

            foreach (var gx in _vm.UserGuidesX)
            {
                double screenX = _offsetX + gx * zoom;
                context.DrawLine(userGuidePen, new Point(screenX, _offsetY), new Point(screenX, _offsetY + canvasH));
            }
            foreach (var gy in _vm.UserGuidesY)
            {
                double screenY = _offsetY + gy * zoom;
                context.DrawLine(userGuidePen, new Point(_offsetX, screenY), new Point(_offsetX + canvasW, screenY));
            }
        }

        // Rulers
        if (_vm.ShowRulers)
            DrawRulers(context, canvasW, canvasH, zoom);

        // Origin + size labels
        DrawLabels(context, canvasW, canvasH, doc);
    }

    private void RenderElements(DrawingContext context, GumpDocument doc, double zoom)
    {
        // Page 0 is always visible (base layer in UO gumps)
        var page0 = doc.Pages.FirstOrDefault(p => p.PageNumber == 0);
        if (page0 is not null)
            RenderPageElements(context, page0, zoom);

        // Also render the active page if different from page 0
        int activePageNum = _vm!.ActivePage;
        if (activePageNum != 0)
        {
            var activePage = doc.Pages.FirstOrDefault(p => p.PageNumber == activePageNum);
            if (activePage is not null)
                RenderPageElements(context, activePage, zoom);
        }
    }

    private void RenderPageElements(DrawingContext context, GumpPage page, double zoom)
    {
        var mgr = AssetManager.Instance;

        foreach (var element in page.Elements)
        {
            if (!element.IsVisible) continue;

            double ex = _offsetX + element.X * zoom;
            double ey = _offsetY + element.Y * zoom;
            double ew = element.Width * zoom;
            double eh = element.Height * zoom;
            var elementRect = new Rect(ex, ey, Math.Max(ew, 20 * zoom), Math.Max(eh, 20 * zoom));

            var isSelected = _vm!.Selection.SelectedElements.Contains(element);

            switch (element)
            {
                case GumpBackground bg:
                    DrawGumpArt(context, bg.GumpId, elementRect, "#1a3a5c", $"BG 0x{bg.GumpId:X4}", isSelected, mgr, zoom);
                    break;
                case GumpImage img:
                    DrawGumpArt(context, img.GumpId, elementRect, "#2a4a3c", $"IMG 0x{img.GumpId:X4}", isSelected, mgr, zoom, img.Hue);
                    break;
                case GumpImageTiled tiled:
                    DrawTiledGumpArt(context, tiled.GumpId, elementRect, isSelected, mgr, zoom);
                    break;
                case GumpButton btn:
                    DrawGumpArt(context, btn.NormalId, elementRect, "#5c1a3a", $"BTN {btn.ButtonId}", isSelected, mgr, zoom);
                    break;
                case GumpCheck chk:
                    DrawGumpArt(context, chk.InactiveId, elementRect, "#3a3a1a", "☐ Check", isSelected, mgr, zoom);
                    break;
                case GumpRadio radio:
                    DrawGumpArt(context, radio.InactiveId, elementRect, "#3a3a1a", "◉ Radio", isSelected, mgr, zoom);
                    break;
                case GumpLabel label:
                    DrawLabelElement(context, ex, ey, zoom, label, isSelected);
                    break;
                case GumpLabelCropped cropped:
                    DrawElementBox(context, elementRect, "#3a3a1a", $"\"{cropped.Text}\"", isSelected);
                    break;
                case GumpHtml html:
                    DrawHtmlElement(context, elementRect, html, isSelected, zoom);
                    break;
                case GumpHtmlLocalized htmlLoc:
                    var clilocText = mgr.GetClilocText(htmlLoc.ClilocId);
                    var locLabel = clilocText is not null
                        ? $"#{htmlLoc.ClilocId}: {(clilocText.Length > 40 ? clilocText[..40] + "…" : clilocText)}"
                        : $"Cliloc #{htmlLoc.ClilocId}";
                    DrawElementBox(context, elementRect, "#1a3a3a", locLabel, isSelected);
                    break;
                case GumpAlphaRegion:
                    var alphaFill = new SolidColorBrush(Color.FromArgb(60, 100, 100, 180));
                    context.FillRectangle(alphaFill, elementRect);
                    var alphaBorder = new Pen(new SolidColorBrush(Color.FromArgb(80, 150, 150, 255)), 1,
                        new DashStyle([3, 3], 0));
                    context.DrawRectangle(alphaBorder, elementRect);
                    if (isSelected) DrawSelectedBorder(context, elementRect);
                    break;
                case GumpTextEntry entry:
                    DrawTextEntryElement(context, elementRect, entry, isSelected, zoom);
                    break;
                case GumpItem item:
                    DrawGumpArt(context, item.ItemId, elementRect, "#3a2a1a", $"Item 0x{item.ItemId:X4}", isSelected, mgr, zoom, item.Hue);
                    break;
                case GumpGroup group:
                    // Draw group bounding box with dashed border
                    var groupFill = new SolidColorBrush(Color.FromArgb(20, 233, 69, 96));
                    context.FillRectangle(groupFill, elementRect);
                    var groupBorder = new Pen(new SolidColorBrush(Color.Parse(isSelected ? "#e94560" : "#666")),
                        isSelected ? 2 : 1, new DashStyle([6, 3], 0));
                    context.DrawRectangle(groupBorder, elementRect);

                    // Draw group label
                    var grpTypeface = new Typeface("Inter, Segoe UI");
                    var grpText = new FormattedText($"⬚ {group.Name} ({group.Children.Count})",
                        CultureInfo.InvariantCulture, FlowDirection.LeftToRight, grpTypeface, 9,
                        new SolidColorBrush(Color.Parse("#e94560")));
                    context.DrawText(grpText, new Point(elementRect.X + 4, elementRect.Y + 2));

                    // Render children within the group
                    foreach (var child in group.Children)
                    {
                        if (!child.IsVisible) continue;
                        double cx = _offsetX + child.X * zoom;
                        double cy = _offsetY + child.Y * zoom;
                        double cw = child.Width * zoom;
                        double ch = child.Height * zoom;
                        var childRect = new Rect(cx, cy, Math.Max(cw, 20 * zoom), Math.Max(ch, 20 * zoom));
                        var childSelected = _vm!.Selection.SelectedElements.Contains(child);

                        switch (child)
                        {
                            case GumpImage cImg:
                                DrawGumpArt(context, cImg.GumpId, childRect, "#2a4a3c", $"IMG 0x{cImg.GumpId:X4}", childSelected, mgr, zoom);
                                break;
                            case GumpBackground cBg:
                                DrawGumpArt(context, cBg.GumpId, childRect, "#1a3a5c", $"BG 0x{cBg.GumpId:X4}", childSelected, mgr, zoom);
                                break;
                            case GumpButton cBtn:
                                DrawGumpArt(context, cBtn.NormalId, childRect, "#5c1a3a", $"BTN {cBtn.ButtonId}", childSelected, mgr, zoom);
                                break;
                            case GumpLabel cLbl:
                                DrawLabelElement(context, cx, cy, zoom, cLbl, childSelected);
                                break;
                            default:
                                DrawElementBox(context, childRect, "#2a2a2a", child.ElementType, childSelected);
                                break;
                        }
                    }

                    if (isSelected) DrawSelectedBorder(context, elementRect);
                    break;
                default:
                    DrawElementBox(context, elementRect, "#2a2a2a", element.ElementType, isSelected);
                    break;
            }

            // Lock indicator overlay
            if (element.IsLocked && isSelected)
            {
                var lockBg = new SolidColorBrush(Color.FromArgb(200, 30, 30, 40));
                var lockRect = new Rect(elementRect.Right - 16 * zoom, elementRect.Top, 16 * zoom, 16 * zoom);
                context.FillRectangle(lockBg, lockRect);
                var lockPen = new Pen(new SolidColorBrush(Color.Parse("#e94560")), 1);
                context.DrawRectangle(lockPen, lockRect);

                // Draw a small "L" for locked
                var lockText = new FormattedText("🔒", System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 9 * zoom, new SolidColorBrush(Color.Parse("#e94560")));
                context.DrawText(lockText, new Point(lockRect.X + 1, lockRect.Y));
            }
        }
    }

    /// <summary>
    /// Draws a gump art bitmap if available, otherwise falls back to a colored box.
    /// </summary>
    private void DrawGumpArt(DrawingContext context, int gumpId, Rect rect, string fallbackColor,
        string label, bool selected, AssetManager mgr, double zoom, int hue = 0)
    {
        var bitmap = mgr.IsLoaded
            ? (hue > 0 ? mgr.GetHuedBitmap(gumpId, hue) : mgr.GetBitmap(gumpId))
            : null;

        if (bitmap is not null)
        {
            // Draw the actual gump art, scaled to fit the element rect
            context.DrawImage(bitmap, rect);
        }
        else
        {
            // Fallback: colored box with label
            context.FillRectangle(new SolidColorBrush(Color.Parse(fallbackColor)), rect);
        }

        // Always draw border and label overlay
        var borderPen = new Pen(new SolidColorBrush(Color.Parse(selected ? "#e94560" : "#555")), selected ? 2 : 1);
        context.DrawRectangle(borderPen, rect);

        // Label in corner
        if (bitmap is null || selected)
        {
            var typeface = new Typeface("Inter, Segoe UI");
            var fontSize = Math.Min(11, rect.Height / 2);
            if (fontSize > 5)
            {
                var text = new FormattedText(label, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, fontSize,
                    new SolidColorBrush(Color.Parse(selected ? "#fff" : "#bbb")));
                double tx = rect.X + (rect.Width - text.Width) / 2;
                double ty = rect.Y + (rect.Height - text.Height) / 2;
                context.DrawText(text, new Point(tx, ty));
            }
        }
    }

    /// <summary>
    /// Draws a gump art bitmap tiled (repeated) to fill the target rect.
    /// This matches UO's AddImageTiled behavior.
    /// </summary>
    private void DrawTiledGumpArt(DrawingContext context, int gumpId, Rect rect, bool selected,
        AssetManager mgr, double zoom)
    {
        var bitmap = mgr.IsLoaded ? mgr.GetBitmap(gumpId) : null;

        if (bitmap is not null)
        {
            double srcW = bitmap.Size.Width;
            double srcH = bitmap.Size.Height;

            if (srcW > 0 && srcH > 0)
            {
                // Clip to element bounds
                using (context.PushClip(rect))
                {
                    // Tile the image across the rect
                    for (double ty = rect.Top; ty < rect.Bottom; ty += srcH * zoom)
                    {
                        for (double tx = rect.Left; tx < rect.Right; tx += srcW * zoom)
                        {
                            double drawW = Math.Min(srcW * zoom, rect.Right - tx);
                            double drawH = Math.Min(srcH * zoom, rect.Bottom - ty);
                            var tileRect = new Rect(tx, ty, drawW, drawH);

                            // For partial tiles at the edges, use a source rect
                            if (drawW < srcW * zoom || drawH < srcH * zoom)
                            {
                                var srcRect = new Rect(0, 0, drawW / zoom, drawH / zoom);
                                context.DrawImage(bitmap, srcRect, tileRect);
                            }
                            else
                            {
                                context.DrawImage(bitmap, tileRect);
                            }
                        }
                    }
                }
            }
        }
        else
        {
            // Fallback: colored box with tiled pattern indication
            context.FillRectangle(new SolidColorBrush(Color.Parse("#2a4a3c")), rect);

            // Draw a subtle grid pattern to indicate tiling
            var tilePen = new Pen(new SolidColorBrush(Color.FromArgb(60, 200, 200, 200)), 1,
                new DashStyle([2, 2], 0));
            double patternStep = 30 * zoom;
            for (double x = rect.Left + patternStep; x < rect.Right; x += patternStep)
                context.DrawLine(tilePen, new Point(x, rect.Top), new Point(x, rect.Bottom));
            for (double y = rect.Top + patternStep; y < rect.Bottom; y += patternStep)
                context.DrawLine(tilePen, new Point(rect.Left, y), new Point(rect.Right, y));
        }

        // Border and label
        var borderPen = new Pen(new SolidColorBrush(Color.Parse(selected ? "#e94560" : "#555")), selected ? 2 : 1);
        context.DrawRectangle(borderPen, rect);

        if (bitmap is null || selected)
        {
            var typeface = new Typeface("Inter, Segoe UI");
            var fontSize = Math.Min(11, rect.Height / 2);
            if (fontSize > 5)
            {
                var label = $"Tiled 0x{gumpId:X4}";
                var text = new FormattedText(label, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, fontSize,
                    new SolidColorBrush(Color.Parse(selected ? "#fff" : "#bbb")));
                double tx = rect.X + (rect.Width - text.Width) / 2;
                double txy = rect.Y + (rect.Height - text.Height) / 2;
                context.DrawText(text, new Point(tx, txy));
            }
        }
    }

    private void DrawHtmlElement(DrawingContext context, Rect rect, GumpHtml html, bool selected, double zoom)
    {
        // HTML region background
        if (html.HasBackground)
            context.FillRectangle(new SolidColorBrush(Color.Parse("#1a2a3a")), rect);
        else
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(40, 30, 50, 70)), rect);

        // Text preview
        var typeface = new Typeface("Inter, Segoe UI");
        var fontSize = Math.Min(11, rect.Height / 4);
        if (fontSize > 5 && !string.IsNullOrEmpty(html.Text))
        {
            var preview = html.Text.Length > 60 ? html.Text[..60] + "…" : html.Text;
            var text = new FormattedText(preview, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, fontSize,
                new SolidColorBrush(Color.Parse("#aac")));
            context.DrawText(text, new Point(rect.X + 4, rect.Y + 4));
        }

        // Scrollbar indicator
        if (html.HasScrollbar)
        {
            var scrollRect = new Rect(rect.Right - 10, rect.Top + 2, 8, rect.Height - 4);
            context.FillRectangle(new SolidColorBrush(Color.Parse("#2a3a4a")), scrollRect);
        }

        var borderPen = new Pen(new SolidColorBrush(Color.Parse(selected ? "#e94560" : "#3a5a6a")), selected ? 2 : 1);
        context.DrawRectangle(borderPen, rect);
    }

    private void DrawTextEntryElement(DrawingContext context, Rect rect, GumpTextEntry entry, bool selected, double zoom)
    {
        // Input field look
        context.FillRectangle(new SolidColorBrush(Color.Parse("#0a0a1a")), rect);
        var borderPen = new Pen(new SolidColorBrush(Color.Parse(selected ? "#e94560" : "#3a3a5a")), selected ? 2 : 1);
        context.DrawRectangle(borderPen, rect);

        // Initial text or placeholder
        var typeface = new Typeface("Inter, Segoe UI");
        var fontSize = Math.Min(12, rect.Height - 4);
        if (fontSize > 5)
        {
            var displayText = string.IsNullOrEmpty(entry.InitialText) ? $"[Entry {entry.EntryId}]" : entry.InitialText;
            var text = new FormattedText(displayText, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, fontSize,
                new SolidColorBrush(Color.Parse(string.IsNullOrEmpty(entry.InitialText) ? "#555" : "#ccc")));
            context.DrawText(text, new Point(rect.X + 4, rect.Y + (rect.Height - text.Height) / 2));
        }
    }

    private void DrawElementBox(DrawingContext context, Rect rect, string bgColor, string label, bool selected)
    {
        context.FillRectangle(new SolidColorBrush(Color.Parse(bgColor)), rect);
        var borderPen = new Pen(new SolidColorBrush(Color.Parse(selected ? "#e94560" : "#555")), selected ? 2 : 1);
        context.DrawRectangle(borderPen, rect);

        // Label
        var typeface = new Typeface("Inter, Segoe UI");
        var fontSize = Math.Min(11, rect.Height / 2);
        if (fontSize > 5)
        {
            var text = new FormattedText(label, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, fontSize,
                new SolidColorBrush(Color.Parse(selected ? "#fff" : "#bbb")));
            double tx = rect.X + (rect.Width - text.Width) / 2;
            double ty = rect.Y + (rect.Height - text.Height) / 2;
            context.DrawText(text, new Point(tx, ty));
        }
    }

    private void DrawLabelElement(DrawingContext context, double x, double y, double zoom, GumpLabel label, bool selected)
    {
        var mgr = AssetManager.Instance;
        var typeface = new Typeface("Inter, Segoe UI");
        var displayText = string.IsNullOrEmpty(label.Text) ? "(empty)" : label.Text;

        // Use UO font metrics for sizing when available
        double fontSize = 13 * zoom;
        if (mgr.HasFonts)
        {
            int fontId = label.Hue > 0 ? 0 : 0; // UO uses font 0 by default for labels
            int measuredW = mgr.MeasureTextWidth(fontId, displayText);
            int measuredH = mgr.GetFontHeight(fontId);

            // Update element dimensions if they're still default
            if (label.Width == 0 && measuredW > 0)
                label.Width = measuredW;
            if (label.Height == 0 && measuredH > 0)
                label.Height = measuredH;
        }

        var text = new FormattedText(displayText,
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, fontSize,
            new SolidColorBrush(Color.Parse(selected ? "#e94560" : "#ddd")));
        context.DrawText(text, new Point(x, y));

        if (selected)
        {
            var rect = new Rect(x - 2, y - 2, text.Width + 4, text.Height + 4);
            DrawSelectedBorder(context, rect);
        }
    }

    private void DrawSelectedBorder(DrawingContext context, Rect rect)
    {
        var pen = new Pen(new SolidColorBrush(Color.Parse("#e94560")), 2);
        context.DrawRectangle(pen, rect);
    }

    private void DrawSelectionHandles(DrawingContext context, double zoom)
    {
        foreach (var element in _vm!.Selection.SelectedElements)
        {
            double ex = _offsetX + element.X * zoom;
            double ey = _offsetY + element.Y * zoom;
            double ew = Math.Max(element.Width * zoom, 20 * zoom);
            double eh = Math.Max(element.Height * zoom, 20 * zoom);

            var handleBrush = new SolidColorBrush(Color.Parse("#e94560"));
            var handleBg = new SolidColorBrush(Color.Parse("#1a1a2e"));
            double hs = 6; // handle size

            // 8 handles: corners + edge midpoints
            Point[] handles =
            [
                new(ex - hs/2, ey - hs/2),                    // TL
                new(ex + ew/2 - hs/2, ey - hs/2),             // T
                new(ex + ew - hs/2, ey - hs/2),               // TR
                new(ex + ew - hs/2, ey + eh/2 - hs/2),        // R
                new(ex + ew - hs/2, ey + eh - hs/2),          // BR
                new(ex + ew/2 - hs/2, ey + eh - hs/2),        // B
                new(ex - hs/2, ey + eh - hs/2),               // BL
                new(ex - hs/2, ey + eh/2 - hs/2),             // L
            ];

            foreach (var h in handles)
            {
                var handleRect = new Rect(h.X, h.Y, hs, hs);
                context.FillRectangle(handleBg, handleRect);
                context.FillRectangle(handleBrush, handleRect.Inflate(-1));
            }
        }
    }

    private void DrawGrid(DrawingContext context, double canvasW, double canvasH, double zoom)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#1a1a3a")), 0.5);
        double gridStep = _vm!.GridSize * zoom;
        if (gridStep < 4) return; // Don't draw if too dense

        for (double x = 0; x <= canvasW; x += gridStep)
            context.DrawLine(gridPen, new Point(_offsetX + x, _offsetY),
                                      new Point(_offsetX + x, _offsetY + canvasH));
        for (double y = 0; y <= canvasH; y += gridStep)
            context.DrawLine(gridPen, new Point(_offsetX, _offsetY + y),
                                      new Point(_offsetX + canvasW, _offsetY + y));
    }

    private void DrawRulers(DrawingContext context, double canvasW, double canvasH, double zoom)
    {
        var rulerBg = new SolidColorBrush(Color.Parse("#16213e"));
        var rulerPen = new Pen(new SolidColorBrush(Color.Parse("#444")), 0.5);
        var typeface = new Typeface("Inter, Segoe UI");
        int step = zoom >= 0.5 ? 50 : 100;

        // Top ruler
        context.FillRectangle(rulerBg, new Rect(_offsetX, _offsetY - 18, canvasW, 18));
        for (int x = 0; x <= (int)(canvasW / zoom); x += step)
        {
            double px = _offsetX + x * zoom;
            context.DrawLine(rulerPen, new Point(px, _offsetY - 6), new Point(px, _offsetY));
            var label = new FormattedText(x.ToString(), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 8, new SolidColorBrush(Color.Parse("#888")));
            context.DrawText(label, new Point(px + 2, _offsetY - 16));
        }

        // Left ruler
        context.FillRectangle(rulerBg, new Rect(_offsetX - 30, _offsetY, 30, canvasH));
        for (int y = 0; y <= (int)(canvasH / zoom); y += step)
        {
            double py = _offsetY + y * zoom;
            context.DrawLine(rulerPen, new Point(_offsetX - 6, py), new Point(_offsetX, py));
            var label = new FormattedText(y.ToString(), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 8, new SolidColorBrush(Color.Parse("#888")));
            context.DrawText(label, new Point(_offsetX - 28, py + 1));
        }
    }

    private void DrawLabels(DrawingContext context, double canvasW, double canvasH, GumpDocument doc)
    {
        var typeface = new Typeface("Inter, Segoe UI");
        var originText = new FormattedText("(0, 0)", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, 10, new SolidColorBrush(Color.Parse("#666")));
        context.DrawText(originText, new Point(_offsetX + 4, _offsetY + 2));

        var sizeText = new FormattedText($"{doc.CanvasWidth} × {doc.CanvasHeight}", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, 10, new SolidColorBrush(Color.Parse("#e94560")));
        context.DrawText(sizeText, new Point(_offsetX + canvasW - sizeText.Width - 4, _offsetY + canvasH - sizeText.Height - 2));

        // Selection info
        if (_vm!.Selection.HasSingleSelection)
        {
            var sel = _vm.Selection.PrimarySelection!;
            var selText = new FormattedText(
                $"{sel.ElementType}: ({sel.X}, {sel.Y}) {sel.Width}×{sel.Height}",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, 10, new SolidColorBrush(Color.Parse("#e94560")));
            context.DrawText(selText, new Point(_offsetX + 4, _offsetY + canvasH + 4));
        }
    }

    // ═══════════ MOUSE INTERACTION ═══════════

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm is null) return;

        Focus();
        var pos = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        // Middle button = Pan
        if (props.IsMiddleButtonPressed)
        {
            _isDragging = true;
            _dragStart = pos;
            _dragTarget = null;
            _resizeHandle = -1;
            e.Handled = true;
            return;
        }

        if (!props.IsLeftButtonPressed && !props.IsRightButtonPressed) return;

        double zoom = _vm.Zoom;
        double canvasX = (pos.X - _offsetX) / zoom;
        double canvasY = (pos.Y - _offsetY) / zoom;

        // Right-click on rulers removes nearest guide
        if (props.IsRightButtonPressed && _vm.ShowRulers)
        {
            bool inTopRuler = pos.Y >= _offsetY - 18 && pos.Y < _offsetY && pos.X >= _offsetX;
            bool inLeftRuler = pos.X >= _offsetX - 30 && pos.X < _offsetX && pos.Y >= _offsetY;

            if (inTopRuler || inLeftRuler)
            {
                _vm.RemoveGuideNear((int)canvasX, (int)canvasY);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }

        if (!props.IsLeftButtonPressed) return;

        // Double-click on rulers adds guide
        if (e.ClickCount == 2 && _vm.ShowRulers)
        {
            bool inTopRuler = pos.Y >= _offsetY - 18 && pos.Y < _offsetY && pos.X >= _offsetX;
            bool inLeftRuler = pos.X >= _offsetX - 30 && pos.X < _offsetX && pos.Y >= _offsetY;

            if (inTopRuler)
            {
                _vm.AddVerticalGuide((int)canvasX);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            if (inLeftRuler)
            {
                _vm.AddHorizontalGuide((int)canvasY);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }

        // Check if clicking a resize handle of the currently selected element
        if (_vm.Selection.HasSingleSelection)
        {
            int handle = HitTestHandle(pos, zoom);
            if (handle >= 0)
            {
                _resizeHandle = handle;
                _isDragging = true;
                _dragStart = pos;
                _dragTarget = _vm.Selection.PrimarySelection;
                _dragElementStart = new Point(_dragTarget!.X, _dragTarget.Y);
                _resizeStartW = _dragTarget.Width;
                _resizeStartH = _dragTarget.Height;
                e.Handled = true;
                return;
            }
        }

        // Hit test elements
        var hit = HitTest(canvasX, canvasY);

        if (hit is not null)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                _vm.Selection.ToggleSelect(hit);
            else if (!_vm.Selection.SelectedElements.Contains(hit))
                _vm.Selection.Select(hit);

            // Only allow drag/resize if element is not locked
            if (!hit.IsLocked)
            {
                _isDragging = true;
                _dragStart = pos;
                _dragTarget = hit;
                _dragElementStart = new Point(hit.X, hit.Y);
                _resizeHandle = -1;
            }
        }
        else
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                _vm.Selection.ClearSelection();
            _isMarquee = true;
            _dragStart = pos;
            _marqueeRect = new Rect(pos, new Size(0, 0));
            _resizeHandle = -1;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_vm is null) return;

        var pos = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        // Pan with middle button
        if (_isDragging && _dragTarget is null && _resizeHandle < 0 && props.IsMiddleButtonPressed)
        {
            _vm.PanX += pos.X - _dragStart.X;
            _vm.PanY += pos.Y - _dragStart.Y;
            _dragStart = pos;
            InvalidateVisual();
            return;
        }

        // Resize via handle
        if (_isDragging && _resizeHandle >= 0 && _dragTarget is not null && props.IsLeftButtonPressed)
        {
            double zoom = _vm.Zoom;
            double dx = (pos.X - _dragStart.X) / zoom;
            double dy = (pos.Y - _dragStart.Y) / zoom;
            ApplyResize(dx, dy);
            InvalidateVisual();
            return;
        }

        // Drag element
        if (_isDragging && _dragTarget is not null && props.IsLeftButtonPressed)
        {
            double zoom = _vm.Zoom;
            double dx = (pos.X - _dragStart.X) / zoom;
            double dy = (pos.Y - _dragStart.Y) / zoom;

            int newX = (int)(_dragElementStart.X + dx);
            int newY = (int)(_dragElementStart.Y + dy);

            // Alt key temporarily disables all snapping
            bool altHeld = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

            if (_vm.SnapToGrid && !altHeld)
            {
                newX = SnapToGrid(newX);
                newY = SnapToGrid(newY);
            }

            // Smart guide snapping (disabled when Alt is held)
            _guideHorizontal.Clear();
            _guideVertical.Clear();

            if (!altHeld)
            {
                var page = _vm.Document.Pages.FirstOrDefault(p => p.PageNumber == 0);
                if (page is not null)
                {
                    int w = _dragTarget.Width;
                    int h = _dragTarget.Height;
                    int cx = newX + w / 2;
                    int cy = newY + h / 2;
                    int right = newX + w;
                    int bottom = newY + h;

                    foreach (var other in page.Elements)
                    {
                        if (other == _dragTarget || !other.IsVisible) continue;
                        int ox = other.X, oy = other.Y;
                        int ow = other.Width, oh = other.Height;
                        int ocx = ox + ow / 2, ocy = oy + oh / 2;
                        int oRight = ox + ow, oBottom = oy + oh;

                        // Vertical guides (X alignment)
                        if (Math.Abs(newX - ox) <= GuideSnapThreshold) { newX = ox; _guideVertical.Add(ox); }
                        else if (Math.Abs(right - oRight) <= GuideSnapThreshold) { newX = oRight - w; _guideVertical.Add(oRight); }
                        else if (Math.Abs(cx - ocx) <= GuideSnapThreshold) { newX = ocx - w / 2; _guideVertical.Add(ocx); }
                        else if (Math.Abs(newX - oRight) <= GuideSnapThreshold) { newX = oRight; _guideVertical.Add(oRight); }
                        else if (Math.Abs(right - ox) <= GuideSnapThreshold) { newX = ox - w; _guideVertical.Add(ox); }

                        // Horizontal guides (Y alignment)
                        if (Math.Abs(newY - oy) <= GuideSnapThreshold) { newY = oy; _guideHorizontal.Add(oy); }
                        else if (Math.Abs(bottom - oBottom) <= GuideSnapThreshold) { newY = oBottom - h; _guideHorizontal.Add(oBottom); }
                        else if (Math.Abs(cy - ocy) <= GuideSnapThreshold) { newY = ocy - h / 2; _guideHorizontal.Add(ocy); }
                        else if (Math.Abs(newY - oBottom) <= GuideSnapThreshold) { newY = oBottom; _guideHorizontal.Add(oBottom); }
                        else if (Math.Abs(bottom - oy) <= GuideSnapThreshold) { newY = oy - h; _guideHorizontal.Add(oy); }
                    }
                }
            }

            // If dragging a group, move all children by the same delta
            if (_dragTarget is GumpGroup dragGroup)
            {
                int dx2 = newX - _dragTarget.X;
                int dy2 = newY - _dragTarget.Y;
                foreach (var child in dragGroup.Children)
                {
                    child.X += dx2;
                    child.Y += dy2;
                }
            }

            _dragTarget.X = newX;
            _dragTarget.Y = newY;
            InvalidateVisual();
            return;
        }

        // Marquee selection
        if (_isMarquee)
        {
            double x1 = Math.Min(_dragStart.X, pos.X);
            double y1 = Math.Min(_dragStart.Y, pos.Y);
            double x2 = Math.Max(_dragStart.X, pos.X);
            double y2 = Math.Max(_dragStart.Y, pos.Y);
            _marqueeRect = new Rect(x1, y1, x2 - x1, y2 - y1);
            InvalidateVisual();
        }

        // Cursor change for resize handles
        if (!_isDragging && _vm.Selection.HasSingleSelection)
        {
            int handle = HitTestHandle(pos, _vm.Zoom);
            Cursor = handle switch
            {
                0 or 4 => new Cursor(StandardCursorType.TopLeftCorner),
                2 or 6 => new Cursor(StandardCursorType.TopRightCorner),
                1 or 5 => new Cursor(StandardCursorType.SizeNorthSouth),
                3 or 7 => new Cursor(StandardCursorType.SizeWestEast),
                _ => Cursor.Default
            };
        }

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_vm is null) return;

        // Finish resize — commit as undo command
        if (_isDragging && _resizeHandle >= 0 && _dragTarget is not null)
        {
            int finalX = _dragTarget.X;
            int finalY = _dragTarget.Y;
            int finalW = _dragTarget.Width;
            int finalH = _dragTarget.Height;

            // Reset to original, then execute through command
            _dragTarget.X = (int)_dragElementStart.X;
            _dragTarget.Y = (int)_dragElementStart.Y;
            _dragTarget.Width = _resizeStartW;
            _dragTarget.Height = _resizeStartH;

            if (finalX != (int)_dragElementStart.X || finalY != (int)_dragElementStart.Y ||
                finalW != _resizeStartW || finalH != _resizeStartH)
            {
                _vm.UndoStack.Execute(new ResizeElementCommand(_dragTarget, finalX, finalY, finalW, finalH));
            }
        }
        // Finish drag — commit as undo command
        else if (_isDragging && _dragTarget is not null)
        {
            int finalX = _dragTarget.X;
            int finalY = _dragTarget.Y;

            if (finalX != (int)_dragElementStart.X || finalY != (int)_dragElementStart.Y)
            {
                _dragTarget.X = (int)_dragElementStart.X;
                _dragTarget.Y = (int)_dragElementStart.Y;
                _vm.UndoStack.Execute(new MoveElementCommand(_dragTarget, finalX, finalY));
            }
        }

        // Finish marquee — select elements within
        if (_isMarquee && _marqueeRect.Width > 3 && _marqueeRect.Height > 3)
        {
            double zoom = _vm.Zoom;
            var elements = new List<GumpElement>();
            var page = _vm.Document.Pages.FirstOrDefault(p => p.PageNumber == 0);
            if (page is not null)
            {
                foreach (var el in page.Elements)
                {
                    double ex = _offsetX + el.X * zoom;
                    double ey = _offsetY + el.Y * zoom;
                    double ew = Math.Max(el.Width * zoom, 20 * zoom);
                    double eh = Math.Max(el.Height * zoom, 20 * zoom);
                    var elRect = new Rect(ex, ey, ew, eh);
                    if (_marqueeRect.Intersects(elRect))
                        elements.Add(el);
                }
            }
            if (elements.Count > 0)
                _vm.Selection.SelectMany(elements);
        }

        _isDragging = false;
        _isMarquee = false;
        _dragTarget = null;
        _resizeHandle = -1;
        _guideHorizontal.Clear();
        _guideVertical.Clear();
        Cursor = Cursor.Default;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_vm is null) return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Zoom
            double delta = e.Delta.Y > 0 ? 1.1 : 0.9;
            _vm.Zoom = Math.Clamp(_vm.Zoom * delta, 0.1, 5.0);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm is null) return;

        int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;

        switch (e.Key)
        {
            case Key.Left:
                NudgeSelection(-step, 0);
                e.Handled = true;
                break;
            case Key.Right:
                NudgeSelection(step, 0);
                e.Handled = true;
                break;
            case Key.Up:
                NudgeSelection(0, -step);
                e.Handled = true;
                break;
            case Key.Down:
                NudgeSelection(0, step);
                e.Handled = true;
                break;
            case Key.Delete:
            case Key.Back:
                // Delete handled by ViewModel command
                break;
            case Key.Z when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _vm.UndoStack.Undo();
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.Y when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _vm.UndoStack.Redo();
                InvalidateVisual();
                e.Handled = true;
                break;
        }
    }

    // ═══════════ HELPERS ═══════════

    private GumpElement? HitTest(double canvasX, double canvasY)
    {
        // Check active page first (drawn on top), then page 0
        var pagesToCheck = new List<GumpPage>();

        int activePageNum = _vm?.ActivePage ?? 0;
        if (activePageNum != 0)
        {
            var activePage = _vm?.Document.Pages.FirstOrDefault(p => p.PageNumber == activePageNum);
            if (activePage is not null) pagesToCheck.Add(activePage);
        }

        var page0 = _vm?.Document.Pages.FirstOrDefault(p => p.PageNumber == 0);
        if (page0 is not null) pagesToCheck.Add(page0);

        foreach (var page in pagesToCheck)
        {
            // Iterate in reverse for z-order (last drawn = on top)
            for (int i = page.Elements.Count - 1; i >= 0; i--)
            {
                var el = page.Elements[i];
                if (!el.IsVisible) continue;

                // If it's a group, check children first (they're drawn on top)
                if (el is GumpGroup group)
                {
                    for (int j = group.Children.Count - 1; j >= 0; j--)
                    {
                        var child = group.Children[j];
                        if (!child.IsVisible) continue;
                        double cw = Math.Max(child.Width, 20);
                        double ch = Math.Max(child.Height, 20);
                        if (canvasX >= child.X && canvasX <= child.X + cw &&
                            canvasY >= child.Y && canvasY <= child.Y + ch)
                        {
                            return group; // Return the GROUP, not the child — group moves as a unit
                        }
                    }
                }

                // Check the element itself
                double ew = Math.Max(el.Width, 20);
                double eh = Math.Max(el.Height, 20);
                if (canvasX >= el.X && canvasX <= el.X + ew &&
                    canvasY >= el.Y && canvasY <= el.Y + eh)
                {
                    return el;
                }
            }
        }
        return null;
    }

    private void NudgeSelection(int dx, int dy)
    {
        if (_vm?.Selection.HasSelection != true) return;

        var commands = new List<IEditCommand>();
        foreach (var el in _vm.Selection.SelectedElements.Where(el => !el.IsLocked))
        {
            commands.Add(new MoveElementCommand(el, el.X + dx, el.Y + dy));
            // Also nudge group children
            if (el is GumpGroup group)
            {
                foreach (var child in group.Children)
                    commands.Add(new MoveElementCommand(child, child.X + dx, child.Y + dy));
            }
        }
        if (commands.Count == 0) return;
        _vm.UndoStack.Execute(new BatchCommand(commands, "Nudge"));
        InvalidateVisual();
    }

    private int SnapToGrid(int value)
    {
        int grid = _vm?.GridSize ?? 10;
        return (int)Math.Round((double)value / grid) * grid;
    }

    /// <summary>
    /// Tests if the screen position is over one of the 8 resize handles of the selected element.
    /// Returns handle index (0-7) or -1 if no hit.
    /// </summary>
    private int HitTestHandle(Point screenPos, double zoom)
    {
        var el = _vm?.Selection.PrimarySelection;
        if (el is null) return -1;

        double ex = _offsetX + el.X * zoom;
        double ey = _offsetY + el.Y * zoom;
        double ew = Math.Max(el.Width * zoom, 20 * zoom);
        double eh = Math.Max(el.Height * zoom, 20 * zoom);
        double hs = 8; // handle hit size (slightly larger than visual)

        Point[] handles =
        [
            new(ex, ey),                            // 0 TL
            new(ex + ew / 2, ey),                   // 1 T
            new(ex + ew, ey),                       // 2 TR
            new(ex + ew, ey + eh / 2),              // 3 R
            new(ex + ew, ey + eh),                  // 4 BR
            new(ex + ew / 2, ey + eh),              // 5 B
            new(ex, ey + eh),                       // 6 BL
            new(ex, ey + eh / 2),                   // 7 L
        ];

        for (int i = 0; i < handles.Length; i++)
        {
            var h = handles[i];
            if (Math.Abs(screenPos.X - h.X) <= hs && Math.Abs(screenPos.Y - h.Y) <= hs)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Applies a resize delta to the drag target based on which handle is being dragged.
    /// </summary>
    private void ApplyResize(double dx, double dy)
    {
        if (_dragTarget is null) return;

        int origX = (int)_dragElementStart.X;
        int origY = (int)_dragElementStart.Y;
        int origW = _resizeStartW;
        int origH = _resizeStartH;

        int newX = origX, newY = origY, newW = origW, newH = origH;

        switch (_resizeHandle)
        {
            case 0: // TL — move origin, shrink
                newX = origX + (int)dx;
                newY = origY + (int)dy;
                newW = origW - (int)dx;
                newH = origH - (int)dy;
                break;
            case 1: // T — move top edge
                newY = origY + (int)dy;
                newH = origH - (int)dy;
                break;
            case 2: // TR — move top, extend right
                newY = origY + (int)dy;
                newW = origW + (int)dx;
                newH = origH - (int)dy;
                break;
            case 3: // R — extend right
                newW = origW + (int)dx;
                break;
            case 4: // BR — extend both
                newW = origW + (int)dx;
                newH = origH + (int)dy;
                break;
            case 5: // B — extend bottom
                newH = origH + (int)dy;
                break;
            case 6: // BL — move left edge, extend bottom
                newX = origX + (int)dx;
                newW = origW - (int)dx;
                newH = origH + (int)dy;
                break;
            case 7: // L — move left edge
                newX = origX + (int)dx;
                newW = origW - (int)dx;
                break;
        }

        // Enforce minimum size
        if (newW < 10) { newW = 10; newX = origX + origW - 10; }
        if (newH < 10) { newH = 10; newY = origY + origH - 10; }

        if (_vm?.SnapToGrid == true)
        {
            newX = SnapToGrid(newX);
            newY = SnapToGrid(newY);
            newW = SnapToGrid(newW);
            newH = SnapToGrid(newH);
        }

        _dragTarget.X = newX;
        _dragTarget.Y = newY;
        _dragTarget.Width = Math.Max(newW, 10);
        _dragTarget.Height = Math.Max(newH, 10);
    }
}
