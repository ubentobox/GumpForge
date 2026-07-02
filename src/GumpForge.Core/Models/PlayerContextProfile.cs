using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GumpForge.Core.Models;

public partial class PlayerVariableEntry : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _value = string.Empty;

    public PlayerVariableEntry() { }
    public PlayerVariableEntry(string name, string value)
    {
        Name = name;
        Value = value;
    }
}

public partial class PlayerContextProfile : ObservableObject
{
    [ObservableProperty] private string _name = "Default Character";
    [ObservableProperty] private int _serial;
    
    public ObservableCollection<PlayerVariableEntry> Variables { get; set; } = [];

    public PlayerContextProfile() { }

    public PlayerContextProfile Clone()
    {
        var clone = new PlayerContextProfile
        {
            Name = Name + " (Copy)",
            Serial = Serial
        };
        foreach (var entry in Variables)
        {
            clone.Variables.Add(new PlayerVariableEntry(entry.Name, entry.Value));
        }
        return clone;
    }
}
