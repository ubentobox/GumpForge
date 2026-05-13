using System.Collections.ObjectModel;
using GumpForge.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GumpForge.Core.Services;

/// <summary>
/// Manages the current selection of gump elements on the canvas.
/// Provides observable collections and computed properties for the Properties panel.
/// </summary>
public partial class SelectionManager : ObservableObject
{
    /// <summary>Currently selected elements.</summary>
    public ObservableCollection<GumpElement> SelectedElements { get; } = [];

    /// <summary>True if exactly one element is selected.</summary>
    [ObservableProperty] private bool _hasSingleSelection;

    /// <summary>True if one or more elements are selected.</summary>
    [ObservableProperty] private bool _hasSelection;

    /// <summary>The single selected element, or null.</summary>
    [ObservableProperty] private GumpElement? _primarySelection;

    public SelectionManager()
    {
        SelectedElements.CollectionChanged += (_, _) => UpdateState();
    }

    /// <summary>Select a single element, replacing existing selection.</summary>
    public void Select(GumpElement element)
    {
        SelectedElements.Clear();
        SelectedElements.Add(element);
    }

    /// <summary>Toggle an element in/out of the selection (Shift+click).</summary>
    public void ToggleSelect(GumpElement element)
    {
        if (SelectedElements.Contains(element))
            SelectedElements.Remove(element);
        else
            SelectedElements.Add(element);
    }

    /// <summary>Alias for ToggleSelect.</summary>
    public void ToggleSelection(GumpElement element) => ToggleSelect(element);

    /// <summary>Select multiple elements (marquee).</summary>
    public void SelectMany(IEnumerable<GumpElement> elements)
    {
        SelectedElements.Clear();
        foreach (var e in elements)
            SelectedElements.Add(e);
    }

    /// <summary>Clear selection entirely.</summary>
    public void ClearSelection()
    {
        SelectedElements.Clear();
    }

    private void UpdateState()
    {
        HasSelection = SelectedElements.Count > 0;
        HasSingleSelection = SelectedElements.Count == 1;
        PrimarySelection = SelectedElements.Count > 0 ? SelectedElements[0] : null;
    }
}
