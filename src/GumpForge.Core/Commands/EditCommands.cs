namespace GumpForge.Core.Commands;

/// <summary>
/// Represents an undoable/redoable edit operation on the document model.
/// All mutations to the document go through commands for undo/redo safety.
/// </summary>
public interface IEditCommand
{
    /// <summary>Human-readable description for the undo/redo menu.</summary>
    string Description { get; }

    /// <summary>Execute the command (apply the change).</summary>
    void Execute();

    /// <summary>Undo the command (revert the change).</summary>
    void Undo();
}

/// <summary>
/// Generic property change command. Captures old and new values for any property.
/// </summary>
public class ChangePropertyCommand<T>(
    Action<T> setter,
    T oldValue,
    T newValue,
    string description) : IEditCommand
{
    public string Description => description;

    public void Execute() => setter(newValue);
    public void Undo() => setter(oldValue);
}

/// <summary>
/// Batches multiple commands into a single undo unit.
/// </summary>
public class BatchCommand(IReadOnlyList<IEditCommand> commands, string description) : IEditCommand
{
    public string Description => description;

    public void Execute()
    {
        foreach (var cmd in commands)
            cmd.Execute();
    }

    public void Undo()
    {
        // Undo in reverse order
        for (int i = commands.Count - 1; i >= 0; i--)
            commands[i].Undo();
    }
}

/// <summary>
/// Moves an element to a new position.
/// </summary>
public class MoveElementCommand : IEditCommand
{
    private readonly Models.GumpElement _element;
    private readonly int _oldX, _oldY, _newX, _newY;

    public string Description => $"Move {_element.Name}";

    public MoveElementCommand(Models.GumpElement element, int newX, int newY)
    {
        _element = element;
        _oldX = element.X;
        _oldY = element.Y;
        _newX = newX;
        _newY = newY;
    }

    public void Execute() { _element.X = _newX; _element.Y = _newY; }
    public void Undo() { _element.X = _oldX; _element.Y = _oldY; }
}

/// <summary>
/// Resizes an element.
/// </summary>
public class ResizeElementCommand : IEditCommand
{
    private readonly Models.GumpElement _element;
    private readonly int _oldX, _oldY, _oldW, _oldH;
    private readonly int _newX, _newY, _newW, _newH;

    public string Description => $"Resize {_element.Name}";

    public ResizeElementCommand(Models.GumpElement element, int newX, int newY, int newW, int newH)
    {
        _element = element;
        _oldX = element.X; _oldY = element.Y;
        _oldW = element.Width; _oldH = element.Height;
        _newX = newX; _newY = newY; _newW = newW; _newH = newH;
    }

    public void Execute() { _element.X = _newX; _element.Y = _newY; _element.Width = _newW; _element.Height = _newH; }
    public void Undo() { _element.X = _oldX; _element.Y = _oldY; _element.Width = _oldW; _element.Height = _oldH; }
}

/// <summary>
/// Adds an element to a page.
/// </summary>
public class AddElementCommand : IEditCommand
{
    private readonly Models.GumpPage _page;
    private readonly Models.GumpElement _element;

    public string Description => $"Add {_element.ElementType}";

    public AddElementCommand(Models.GumpPage page, Models.GumpElement element)
    {
        _page = page;
        _element = element;
    }

    public void Execute() => _page.Elements.Add(_element);
    public void Undo() => _page.Elements.Remove(_element);
}

/// <summary>
/// Removes an element from a page.
/// </summary>
public class RemoveElementCommand : IEditCommand
{
    private readonly Models.GumpPage _page;
    private readonly Models.GumpElement _element;
    private int _index;

    public string Description => $"Delete {_element.ElementType}";

    public RemoveElementCommand(Models.GumpPage page, Models.GumpElement element)
    {
        _page = page;
        _element = element;
    }

    public void Execute()
    {
        _index = _page.Elements.IndexOf(_element);
        _page.Elements.Remove(_element);
    }

    public void Undo()
    {
        if (_index >= 0 && _index <= _page.Elements.Count)
            _page.Elements.Insert(_index, _element);
        else
            _page.Elements.Add(_element);
    }
}

/// <summary>
/// Reorders an element within its page (z-order change).
/// </summary>
public class ReorderElementCommand : IEditCommand
{
    private readonly Models.GumpPage _page;
    private readonly Models.GumpElement _element;
    private readonly int _oldIndex;
    private readonly int _newIndex;

    public string Description => "Reorder element";

    public ReorderElementCommand(Models.GumpPage page, Models.GumpElement element, int newIndex)
    {
        _page = page;
        _element = element;
        _oldIndex = page.Elements.IndexOf(element);
        _newIndex = newIndex;
    }

    public void Execute()
    {
        _page.Elements.Remove(_element);
        _page.Elements.Insert(Math.Min(_newIndex, _page.Elements.Count), _element);
    }

    public void Undo()
    {
        _page.Elements.Remove(_element);
        _page.Elements.Insert(Math.Min(_oldIndex, _page.Elements.Count), _element);
    }
}
