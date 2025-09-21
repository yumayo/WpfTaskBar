using System.IO;
using System.Text.Json;

namespace WpfTaskBar;

public class ApplicationOrderService
{
    private readonly string _relationsFilePath;
    private readonly string _windowOrderFilePath;
    private Dictionary<string, HashSet<string>> _aboveRelations = new();
    private Dictionary<string, List<string>> _windowOrderByApplication = new();

    public ApplicationOrderService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataPath, "WpfTaskBar");
        Directory.CreateDirectory(appFolder);
        _relationsFilePath = Path.Combine(appFolder, "application_relations.json");
        _windowOrderFilePath = Path.Combine(appFolder, "window_order.json");
        LoadRelations();
        LoadWindowOrder();
    }

    public void UpdateOrderFromList(IEnumerable<string> orderedPaths)
    {
        var pathList = orderedPaths.Where(p => !string.IsNullOrEmpty(p)).ToList();
        
        for (int i = 0; i < pathList.Count; i++)
        {
            var currentApp = pathList[i];
            
            if (!_aboveRelations.ContainsKey(currentApp))
            {
                _aboveRelations[currentApp] = new HashSet<string>();
            }
            
            for (int j = i + 1; j < pathList.Count; j++)
            {
                var belowApp = pathList[j];
                _aboveRelations[currentApp].Add(belowApp);
                
                if (_aboveRelations.ContainsKey(belowApp))
                {
                    _aboveRelations[belowApp].Remove(currentApp);
                }
            }
        }
        
        SaveRelations();
    }

    public void UpdateWindowOrder(IEnumerable<(string Handle, string ModuleFileName)> orderedWindows)
    {
        var windowsByApp = orderedWindows
            .Where(w => !string.IsNullOrEmpty(w.Handle) && !string.IsNullOrEmpty(w.ModuleFileName))
            .GroupBy(w => w.ModuleFileName)
            .ToDictionary(g => g.Key, g => g.Select(w => w.Handle).ToList());

        foreach (var kvp in windowsByApp)
        {
            _windowOrderByApplication[kvp.Key] = kvp.Value;
        }

        SaveWindowOrder();
    }

    public List<T> SortByRelations<T>(IEnumerable<T> items, Func<T, string> getExecutablePath)
    {
        var itemList = items.ToList();
        return SortByRelations(itemList, getExecutablePath, item => item.GetType().GetProperty("Handle")?.GetValue(item)?.ToString() ?? "");
    }

    public List<T> SortByRelations<T>(IEnumerable<T> items, Func<T, string> getExecutablePath, Func<T, string> getHandle)
    {
        var itemList = items.ToList();
        var pathToItems = new Dictionary<string, List<T>>();

        foreach (var item in itemList)
        {
            var path = getExecutablePath(item);
            if (!string.IsNullOrEmpty(path))
            {
                if (!pathToItems.ContainsKey(path))
                {
                    pathToItems[path] = new List<T>();
                }
                pathToItems[path].Add(item);
            }
        }

        var paths = pathToItems.Keys.ToList();
        var sortedPaths = TopologicalSort(paths);
        var result = new List<T>();

        foreach (var path in sortedPaths)
        {
            if (pathToItems.ContainsKey(path))
            {
                var itemsForPath = pathToItems[path];

                // 同一アプリケーション内でのウィンドウ順序を適用
                if (_windowOrderByApplication.ContainsKey(path))
                {
                    var savedOrder = _windowOrderByApplication[path];
                    var orderedItems = new List<T>();
                    var remainingItems = new List<T>(itemsForPath);

                    // 保存された順序に従って並び替え
                    foreach (var savedHandle in savedOrder)
                    {
                        var matchingItem = remainingItems.FirstOrDefault(item => getHandle(item) == savedHandle);
                        if (matchingItem != null)
                        {
                            orderedItems.Add(matchingItem);
                            remainingItems.Remove(matchingItem);
                        }
                    }

                    // 保存された順序にない新しいアイテムを追加
                    orderedItems.AddRange(remainingItems);
                    result.AddRange(orderedItems);
                }
                else
                {
                    result.AddRange(itemsForPath);
                }
            }
        }

        return result;
    }

    private List<string> TopologicalSort(List<string> apps)
    {
        var inDegree = new Dictionary<string, int>();
        var graph = new Dictionary<string, List<string>>();
        
        foreach (var app in apps)
        {
            inDegree[app] = 0;
            graph[app] = new List<string>();
        }
        
        foreach (var app in apps)
        {
            if (_aboveRelations.ContainsKey(app))
            {
                foreach (var belowApp in _aboveRelations[app])
                {
                    if (apps.Contains(belowApp))
                    {
                        graph[app].Add(belowApp);
                        inDegree[belowApp]++;
                    }
                }
            }
        }
        
        var queue = new Queue<string>();
        foreach (var app in apps)
        {
            if (inDegree[app] == 0)
            {
                queue.Enqueue(app);
            }
        }
        
        var result = new List<string>();
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);
            
            foreach (var neighbor in graph[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                {
                    queue.Enqueue(neighbor);
                }
            }
        }
        
        foreach (var app in apps)
        {
            if (!result.Contains(app))
            {
                result.Add(app);
            }
        }
        
        return result;
    }

    private void SaveRelations()
    {
        try
        {
            var serializableData = _aboveRelations.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToList()
            );
            
            var json = JsonSerializer.Serialize(serializableData, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_relationsFilePath, json);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "相対的位置関係の保存");
        }
    }

    private void LoadRelations()
    {
        try
        {
            if (File.Exists(_relationsFilePath))
            {
                var json = File.ReadAllText(_relationsFilePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                
                if (data != null)
                {
                    _aboveRelations = data.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new HashSet<string>(kvp.Value)
                    );
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "相対的位置関係の読み込み");
            _aboveRelations = new Dictionary<string, HashSet<string>>();
        }
    }

    private void SaveWindowOrder()
    {
        try
        {
            var json = JsonSerializer.Serialize(_windowOrderByApplication, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_windowOrderFilePath, json);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "ウィンドウ順序の保存");
        }
    }

    private void LoadWindowOrder()
    {
        try
        {
            if (File.Exists(_windowOrderFilePath))
            {
                var json = File.ReadAllText(_windowOrderFilePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);

                if (data != null)
                {
                    _windowOrderByApplication = data;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "ウィンドウ順序の読み込み");
            _windowOrderByApplication = new Dictionary<string, List<string>>();
        }
    }
}