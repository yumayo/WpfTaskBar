using System.IO;
using System.Text.Json;

namespace WpfTaskBar;

public class ApplicationOrderService
{
    private readonly string _orderFilePath;
    private List<string> _applicationOrder = new();

    public ApplicationOrderService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataPath, "WpfTaskBar");
        Directory.CreateDirectory(appFolder);
        _orderFilePath = Path.Combine(appFolder, "application_order.json");
        Logger.Debug("タスクバーの順序を保管するファイルを作成します。" + _orderFilePath);
        LoadOrder();
    }

    public void SaveOrder(IEnumerable<string> executablePaths)
    {
        try
        {
            _applicationOrder = executablePaths.ToList();
            var json = JsonSerializer.Serialize(_applicationOrder, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_orderFilePath, json);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "アプリケーション順序の保存");
        }
    }

    public List<string> GetOrder()
    {
        return new List<string>(_applicationOrder);
    }

    public void UpdateOrder(string executablePath)
    {
        if (!_applicationOrder.Contains(executablePath))
        {
            _applicationOrder.Add(executablePath);
            SaveOrder(_applicationOrder);
        }
    }

    public void RemoveFromOrder(string executablePath)
    {
        if (_applicationOrder.Remove(executablePath))
        {
            SaveOrder(_applicationOrder);
        }
    }

    public List<T> SortByOrder<T>(IEnumerable<T> items, Func<T, string> getExecutablePath)
    {
        var itemList = items.ToList();
        var result = new List<T>();
        
        foreach (var orderPath in _applicationOrder)
        {
            var matchingItems = itemList.Where(item => 
                string.Equals(getExecutablePath(item), orderPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            result.AddRange(matchingItems);
            foreach (var item in matchingItems)
            {
                itemList.Remove(item);
            }
        }
        
        result.AddRange(itemList);
        return result;
    }

    private void LoadOrder()
    {
        try
        {
            if (File.Exists(_orderFilePath))
            {
                var json = File.ReadAllText(_orderFilePath);
                _applicationOrder = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "アプリケーション順序の読み込み");
            _applicationOrder = new List<string>();
        }
    }
}