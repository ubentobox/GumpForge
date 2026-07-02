using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GumpForge.Core.Models;

namespace GumpForge.App.Views;

public partial class ImportVariablesWindow : Window
{
    private readonly List<PlayerVariableEntry> _allVariables;
    public List<PlayerVariableEntry> SelectedVariables { get; private set; } = new List<PlayerVariableEntry>();
    public bool IsImported { get; private set; }

    public ImportVariablesWindow()
    {
        InitializeComponent();
        _allVariables = new List<PlayerVariableEntry>();
    }

    public ImportVariablesWindow(List<PlayerVariableEntry> newVariables)
    {
        InitializeComponent();
        _allVariables = newVariables;
        VariablesItemsControl.ItemsSource = _allVariables;
    }

    private void SelectAll_Click(object? sender, RoutedEventArgs e)
    {
        SetAllChecked(true);
    }

    private void DeselectAll_Click(object? sender, RoutedEventArgs e)
    {
        SetAllChecked(false);
    }

    private void SetAllChecked(bool isChecked)
    {
        // Traverse visual children of ItemsControl to set IsChecked state
        var children = VariablesItemsControl.GetVisualDescendants().OfType<CheckBox>();
        foreach (var checkbox in children)
        {
            checkbox.IsChecked = isChecked;
        }
    }

    private void Import_Click(object? sender, RoutedEventArgs e)
    {
        // Collect selected items
        var checkBoxes = VariablesItemsControl.GetVisualDescendants().OfType<CheckBox>().ToList();
        
        SelectedVariables.Clear();
        for (int i = 0; i < _allVariables.Count && i < checkBoxes.Count; i++)
        {
            if (checkBoxes[i].IsChecked == true)
            {
                SelectedVariables.Add(_allVariables[i]);
            }
        }

        IsImported = true;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
