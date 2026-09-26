using System.IO;
using System.Text.Json;
using TodoWidget.Models;

namespace TodoWidget.Services;

public class TodoService
{
    private readonly string _filePath;
    private List<TodoItem> _todos = new();

    public TodoService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "TodoWidget");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "todos.json");
        Load();
    }

    public List<TodoItem> GetAll() => _todos.ToList();

    public void Add(string title)
    {
        _todos.Add(new TodoItem { Title = title });
        Save();
    }

    public void Toggle(string id)
    {
        var todo = _todos.FirstOrDefault(t => t.Id == id);
        if (todo != null)
        {
            todo.IsCompleted = !todo.IsCompleted;
            if (todo.IsCompleted && todo.IsUrgent)
            {
                todo.IsUrgent = false;
                todo.UrgentStartedAt = null;
            }
            Save();
        }
    }

    public void SetUrgent(string id)
    {
        // Only one task can be urgent at a time
        foreach (var todo in _todos)
        {
            todo.IsUrgent = false;
            todo.UrgentStartedAt = null;
        }

        var target = _todos.FirstOrDefault(t => t.Id == id);
        if (target != null && !target.IsCompleted)
        {
            target.IsUrgent = true;
            target.UrgentStartedAt = DateTime.UtcNow;
        }

        Save();
    }

    public void ClearUrgent()
    {
        foreach (var todo in _todos)
        {
            todo.IsUrgent = false;
            todo.UrgentStartedAt = null;
        }
        Save();
    }

    public void Remove(string id)
    {
        _todos.RemoveAll(t => t.Id == id);
        Save();
    }

    public void MoveToTop(string id)
    {
        var todo = _todos.FirstOrDefault(t => t.Id == id);
        if (todo != null)
        {
            _todos.Remove(todo);
            _todos.Insert(0, todo);
            Save();
        }
    }

    public void MoveBefore(string draggedId, string targetId)
    {
        var dragged = _todos.FirstOrDefault(t => t.Id == draggedId);
        var target = _todos.FirstOrDefault(t => t.Id == targetId);
        if (dragged == null || target == null || draggedId == targetId) return;

        _todos.Remove(dragged);
        var targetIndex = _todos.IndexOf(target);
        _todos.Insert(targetIndex, dragged);
        Save();
    }

    public void MoveAfter(string draggedId, string targetId)
    {
        var dragged = _todos.FirstOrDefault(t => t.Id == draggedId);
        var target = _todos.FirstOrDefault(t => t.Id == targetId);
        if (dragged == null || target == null || draggedId == targetId) return;

        _todos.Remove(dragged);
        var targetIndex = _todos.IndexOf(target);
        _todos.Insert(targetIndex + 1, dragged);
        Save();
    }

    private void Load()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                _todos = JsonSerializer.Deserialize<List<TodoItem>>(json) ?? new();
            }
            catch
            {
                _todos = new();
            }
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_todos, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch { }
    }
}