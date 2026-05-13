using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace GumpForge.App.Controls;

/// <summary>
/// Attached behavior that turns a TextBlock into a drag-scrubbable numeric label.
/// Click+drag horizontally to increment/decrement the bound integer property.
/// The cursor changes to a resize arrow to indicate scrubbability.
/// 
/// Usage in XAML:
///   <TextBlock Text="X:" controls:DragScrubBehavior.Target="{Binding Selection.PrimarySelection}"
///              controls:DragScrubBehavior.Property="X"/>
/// </summary>
public static class DragScrubBehavior
{
    public static readonly AttachedProperty<object?> TargetProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, object?>("Target", typeof(DragScrubBehavior));

    public static readonly AttachedProperty<string?> PropertyProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("Property", typeof(DragScrubBehavior));

    private static bool _isDragging;
    private static Point _dragStart;
    private static int _startValue;
    private static TextBlock? _activeLabel;

    static DragScrubBehavior()
    {
        TargetProperty.Changed.AddClassHandler<TextBlock>(OnTargetChanged);
    }

    public static object? GetTarget(TextBlock element) => element.GetValue(TargetProperty);
    public static void SetTarget(TextBlock element, object? value) => element.SetValue(TargetProperty, value);

    public static string? GetProperty(TextBlock element) => element.GetValue(PropertyProperty);
    public static void SetProperty(TextBlock element, string? value) => element.SetValue(PropertyProperty, value);

    private static void OnTargetChanged(TextBlock sender, AvaloniaPropertyChangedEventArgs e)
    {
        sender.Cursor = new Cursor(StandardCursorType.SizeWestEast);
        sender.PointerPressed -= OnPointerPressed;
        sender.PointerMoved -= OnPointerMoved;
        sender.PointerReleased -= OnPointerReleased;

        if (e.NewValue is not null)
        {
            sender.PointerPressed += OnPointerPressed;
            sender.PointerMoved += OnPointerMoved;
            sender.PointerReleased += OnPointerReleased;
        }
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TextBlock label) return;
        var props = e.GetCurrentPoint(label).Properties;
        if (!props.IsLeftButtonPressed) return;

        var target = GetTarget(label);
        var propName = GetProperty(label);
        if (target is null || propName is null) return;

        var propInfo = target.GetType().GetProperty(propName);
        if (propInfo is null || propInfo.PropertyType != typeof(int)) return;

        _isDragging = true;
        _dragStart = e.GetPosition(label);
        _startValue = (int)(propInfo.GetValue(target) ?? 0);
        _activeLabel = label;
        e.Pointer.Capture(label);
        e.Handled = true;
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || sender is not TextBlock label || _activeLabel != label) return;

        var target = GetTarget(label);
        var propName = GetProperty(label);
        if (target is null || propName is null) return;

        var propInfo = target.GetType().GetProperty(propName);
        if (propInfo is null) return;

        var pos = e.GetPosition(label);
        double dx = pos.X - _dragStart.X;
        int newValue = _startValue + (int)(dx / 2.0); // 2px per unit
        propInfo.SetValue(target, newValue);
        e.Handled = true;
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        _activeLabel = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }
}
