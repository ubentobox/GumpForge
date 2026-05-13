using CommunityToolkit.Mvvm.ComponentModel;

namespace GumpForge.Core.Commands;

/// <summary>
/// Manages the undo/redo stack. All document edits flow through here.
/// </summary>
public partial class UndoStack : ObservableObject
{
    private readonly Stack<IEditCommand> _undoStack = new();
    private readonly Stack<IEditCommand> _redoStack = new();
    private const int MaxDepth = 200;

    [ObservableProperty] private bool _canUndo;
    [ObservableProperty] private bool _canRedo;
    [ObservableProperty] private string _undoDescription = string.Empty;
    [ObservableProperty] private string _redoDescription = string.Empty;

    /// <summary>
    /// Execute a command and push it onto the undo stack.
    /// Clears the redo stack (new edits invalidate redo history).
    /// </summary>
    public void Execute(IEditCommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();

        // Enforce max depth
        if (_undoStack.Count > MaxDepth)
        {
            var temp = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = 0; i < MaxDepth; i++)
                _undoStack.Push(temp[i]);
        }

        UpdateState();
    }

    /// <summary>Undo the most recent command.</summary>
    public void Undo()
    {
        if (_undoStack.Count == 0) return;

        var command = _undoStack.Pop();
        command.Undo();
        _redoStack.Push(command);
        UpdateState();
    }

    /// <summary>Redo the most recently undone command.</summary>
    public void Redo()
    {
        if (_redoStack.Count == 0) return;

        var command = _redoStack.Pop();
        command.Execute();
        _undoStack.Push(command);
        UpdateState();
    }

    /// <summary>Clear all undo/redo history.</summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        UpdateState();
    }

    private void UpdateState()
    {
        CanUndo = _undoStack.Count > 0;
        CanRedo = _redoStack.Count > 0;
        UndoDescription = CanUndo ? _undoStack.Peek().Description : string.Empty;
        RedoDescription = CanRedo ? _redoStack.Peek().Description : string.Empty;
    }
}
