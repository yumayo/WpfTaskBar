using System.Text;

namespace WpfTaskBar;

public class WindowManager : IDisposable
{
	public List<TaskBarItem> TaskBarItems = new List<TaskBarItem>();
	public event WindowListChangedEventHandler? WindowListChanged;
	public delegate void WindowListChangedEventHandler(object sender, TaskBarWindowEventArgs e);

	private readonly ApplicationOrderService _orderService = new();

	public static string GetWindowText(IntPtr hwnd)
	{
		var sb = new StringBuilder(255);
		NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
		return sb.ToString();
	}

	public void UpdateApplicationOrder(IEnumerable<string> orderedExecutablePaths)
	{
		_orderService.UpdateOrderFromList(orderedExecutablePaths);
	}

	public void UpdateWindowOrder(IEnumerable<(string Handle, string ModuleFileName)> orderedWindows)
	{
		_orderService.UpdateWindowOrder(orderedWindows);
	}

	public List<T> SortItemsByOrder<T>(IEnumerable<T> items) where T : class
	{
		return _orderService.SortByRelations(items, item =>
		{
			var property = item.GetType().GetProperty("ModuleFileName");
			return property?.GetValue(item) as string ?? string.Empty;
		}, item =>
		{
			var property = item.GetType().GetProperty("Handle");
			return property?.GetValue(item)?.ToString() ?? string.Empty;
		});
	}

	public void Stop()
	{
		// WebView2版では何も実行する必要なし
	}

	public void Dispose()
	{
		Stop();
	}
}

public class TaskBarWindowEventArgs : EventArgs
{
	public List<TaskBarItem> UpdateTaskBarItems { get; }

	public List<TaskBarItem> AddedTaskBarItems { get; }

	public List<TaskBarItem> RemovedTaskBarItemHandles { get; }

	public TaskBarWindowEventArgs(List<TaskBarItem> updateTaskBarItems, List<TaskBarItem> addedTaskBarItems, List<TaskBarItem> removedTaskBarItems)
	{
		UpdateTaskBarItems = updateTaskBarItems;
		AddedTaskBarItems = addedTaskBarItems;
		RemovedTaskBarItemHandles = removedTaskBarItems;
	}
}