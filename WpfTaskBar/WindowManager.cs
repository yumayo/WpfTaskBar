using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WpfTaskBar;

public class WindowManager : IDisposable
{
	public List<IntPtr> WindowHandles = new List<IntPtr>();
	public List<TaskBarItem> TaskBarItems = new List<TaskBarItem>();

	public event WindowListChangedEventHandler WindowListChanged;

	public delegate void WindowListChangedEventHandler(object sender, TaskBarWindowEventArgs e);

	public Task? BackgroundTask;
	public CancellationTokenSource? CancellationTokenSource;
	
	private readonly ApplicationOrderService _orderService = new();

	public void Start()
	{
		Logger.Info("WindowManager.Start() called");
		CancellationTokenSource = new CancellationTokenSource();
		BackgroundTask = UpdateTaskWindows();
		Logger.Info("WindowManager background task started");
	}

	private async Task? UpdateTaskWindows()
	{
		Logger.Info("WindowManager.UpdateTaskWindows() loop started");
		while (!CancellationTokenSource.IsCancellationRequested)
		{
			try
			{
				WindowHandles.Clear();
				NativeMethods.EnumWindows(EnumerationWindows, 0);
				UpdateTaskBarWindows();
				await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationTokenSource.Token);
			}
			catch (TaskCanceledException)
			{
				Logger.Info("WindowManager task cancelled");
				break;
			}
			catch (Exception e)
			{
				Logger.Error(e, "Error in WindowManager.UpdateTaskWindows()");
				break;
			}
		}
		Logger.Info("WindowManager.UpdateTaskWindows() loop ended");
	}

	public void Stop()
	{
		CancellationTokenSource?.Cancel();
		// TODO: Windowを閉じようとした際にWaitで止まる。
		// BackgroundTask?.Wait();
		// BackgroundTask?.Dispose();
		BackgroundTask = null;
		CancellationTokenSource = null;
	}

	[return: MarshalAs(UnmanagedType.Bool)]
	private bool EnumerationWindows(IntPtr hwnd, int lParam)
	{
		// EnumWindowsで列挙されたWindowを全て記録する。
		WindowHandles.Add(hwnd);
		return true;
	}

	private void UpdateTaskBarWindows()
	{
		List<TaskBarItem> updateTaskBarItems = new List<TaskBarItem>();
		List<TaskBarItem> addedTaskBarItems = new List<TaskBarItem>();
		List<TaskBarItem> removedTaskBarItems = new List<TaskBarItem>();

		var foregroundHwnd = NativeMethods.GetForegroundWindow();

		foreach (var windowHandle in WindowHandles)
		{
			// タスクバーとして管理すべきなWindowハンドルか？
			var isTaskBarWindow = NativeMethodUtility.IsTaskBarWindow(windowHandle);
			
			// 現在の仮想デスクトップにあるウィンドウのみを対象とする
			if (isTaskBarWindow && !VirtualDesktopUtility.IsWindowOnCurrentVirtualDesktop(windowHandle))
			{
				isTaskBarWindow = false;
			}

			// タスクバー管理されている場合
			if (TaskBarItems.Select(x => x.Handle).Contains(windowHandle))
			{
				// すでにタスクバー管理化になっているが、今回のフレームではタスクバー管理外となったため削除
				// または異なる仮想デスクトップに移動されたため削除
				if (!isTaskBarWindow)
				{
					var taskBarItem = TaskBarItems.First(x => x.Handle == windowHandle);
					TaskBarItems.Remove(taskBarItem);
					removedTaskBarItems.Add(taskBarItem);
				}
			}
			else
			{
				if (isTaskBarWindow)
				{
					var processName = UwpUtility.GetProcessName(windowHandle);
					
					// TODO: UWPのアプリケーションに対応する
					// var processId = (int)UwpUtility.GetProcessId(windowHandle);
					// var appxPackage = AppxPackage.FromUwpProcess(processId);
					// if (appxPackage != null)
					// {
					// 	processName = appxPackage.FindHighestScaleQualifiedImagePath(appxPackage.Logo);
					// }

					var taskBarItem = new TaskBarItem
					{
						Handle = windowHandle,
						ModuleFileName = processName,
						Title = GetWindowText(windowHandle),
						IsForeground = windowHandle == foregroundHwnd
					};
					TaskBarItems.Add(taskBarItem);
					addedTaskBarItems.Add(taskBarItem);
				}
			}
		}

		foreach (var taskBarWindow in TaskBarItems.ToList())
		{
			if (!WindowHandles.Contains(taskBarWindow.Handle))
			{
				TaskBarItems.Remove(taskBarWindow);
				removedTaskBarItems.Add(taskBarWindow);
			}
		}

		foreach (var taskBarItem in addedTaskBarItems)
		{
			Console.WriteLine($"追加({taskBarItem.Handle.ToString(),10}) {taskBarItem.Title}");
		}

		foreach (var taskBarItem in removedTaskBarItems)
		{
			Console.WriteLine($"削除({taskBarItem.Handle.ToString(),10}) {taskBarItem.Title}");
		}

		foreach (var taskBarWindow in TaskBarItems.ToList())
		{
			var processName = UwpUtility.GetProcessName(taskBarWindow.Handle);
			
			// TODO: UWPのアプリケーションに対応する
			// var processId = (int)UwpUtility.GetProcessId(taskBarWindow.Handle);
			// var appxPackage = AppxPackage.FromProcess(processId);
			// if (appxPackage != null)
			// {
			// 	processName = appxPackage.FindHighestScaleQualifiedImagePath(appxPackage.Logo);
			// }

			updateTaskBarItems.Add(new TaskBarItem
			{
				Handle = taskBarWindow.Handle,
				ModuleFileName = processName,
				IsForeground = taskBarWindow.Handle == foregroundHwnd,
				Title = GetWindowText(taskBarWindow.Handle),
			});
		}

		var sortedUpdateTaskBarItems = _orderService.SortByRelations(updateTaskBarItems, item => item.ModuleFileName ?? string.Empty);
		var sortedAddedTaskBarItems = _orderService.SortByRelations(addedTaskBarItems, item => item.ModuleFileName ?? string.Empty);

		WindowListChanged?.Invoke(this, new TaskBarWindowEventArgs(sortedUpdateTaskBarItems, sortedAddedTaskBarItems, removedTaskBarItems));
	}

	public static string GetWindowText(IntPtr hwnd)
	{
		var sb = new StringBuilder(255);
		NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
		return sb.ToString();
	}

	public void Dispose()
	{
		Stop();
	}

	public int CountBySameProcess(IntPtr hwnd)
	{
		var targetProcessId = (int)UwpUtility.GetProcessId(hwnd);
		return TaskBarItems.Count(item => (int)UwpUtility.GetProcessId(item.Handle) == targetProcessId);
	}

	public void UpdateApplicationOrder(IEnumerable<string> orderedExecutablePaths)
	{
		_orderService.UpdateOrderFromList(orderedExecutablePaths);
	}

	public List<T> SortItemsByOrder<T>(IEnumerable<T> items) where T : class
	{
		return _orderService.SortByRelations(items, item =>
		{
			var property = item.GetType().GetProperty("ModuleFileName");
			return property?.GetValue(item) as string ?? string.Empty;
		});
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