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

	public void Start()
	{
		CancellationTokenSource = new CancellationTokenSource();
		BackgroundTask = UpdateTaskWindows();
	}

	private async Task? UpdateTaskWindows()
	{
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
				break;
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				break;
			}
		}
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
		List<IntPtr> removedWindowHandles = new List<IntPtr>();

		var foregroundHwnd = NativeMethods.GetForegroundWindow();

		foreach (var windowHandle in WindowHandles)
		{
			// タスクバーとして管理すべきなWindowハンドルか？
			var isTaskBarWindow = NativeMethodUtility.IsTaskBarWindow(windowHandle);

			// タスクバー管理されている場合
			if (TaskBarItems.Select(x => x.Handle).Contains(windowHandle))
			{
				// すでにタスクバー管理化になっているが、今回のフレームではタスクバー管理外となったため削除
				if (!isTaskBarWindow)
				{
					var taskBarItem = TaskBarItems.First(x => x.Handle == windowHandle);
					TaskBarItems.Remove(taskBarItem);
					if (!removedWindowHandles.Contains(windowHandle))
					{
						removedWindowHandles.Add(windowHandle);
					}
				}
			}
			else
			{
				if (isTaskBarWindow)
				{
					var processName = UwpUtility.GetProcessName(windowHandle);
					var processId = (int)UwpUtility.GetProcessId(windowHandle);
					var appxPackage = AppxPackage.FromUwpProcess(processId);
					if (appxPackage != null)
					{
						processName = appxPackage.FindHighestScaleQualifiedImagePath(appxPackage.Logo);
					}
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
				if (!removedWindowHandles.Contains(taskBarWindow.Handle))
				{
					removedWindowHandles.Add(taskBarWindow.Handle);
				}
			}
		}

		foreach (var taskBarItem in addedTaskBarItems)
		{
			Console.WriteLine($"追加({taskBarItem.Handle.ToString(),10}) {taskBarItem.Title}");
		}

		foreach (var hwnd in removedWindowHandles)
		{
			Console.WriteLine($"削除({hwnd.ToString(),10})");
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

		WindowListChanged?.Invoke(this, new TaskBarWindowEventArgs(updateTaskBarItems, addedTaskBarItems, removedWindowHandles));
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
}

public class TaskBarWindowEventArgs : EventArgs
{
	public List<TaskBarItem> UpdateTaskBarItems { get; }

	public List<TaskBarItem> AddedTaskBarItems { get; }

	public List<IntPtr> RemovedWindowHandles { get; }

	public TaskBarWindowEventArgs(List<TaskBarItem> updateTaskBarItems, List<TaskBarItem> addedTaskBarItems, List<IntPtr> removedWindows)
	{
		UpdateTaskBarItems = updateTaskBarItems;
		AddedTaskBarItems = addedTaskBarItems;
		RemovedWindowHandles = removedWindows;
	}
}