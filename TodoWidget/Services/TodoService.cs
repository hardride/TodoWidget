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
            Save();
        }
    }

    public void Remove(string id)
    {
        _todos.RemoveAll(t => t.Id == id);
        Save();
    }

    private void Load()
    {
        if (File.Exists(_filePath))
        {
            var json = File.ReadAllText(_filePath);
            _todos = JsonSerializer.Deserialize<List<TodoItem>>(json) ?? new();
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_todos, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}