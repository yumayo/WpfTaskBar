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
		List<UpdateTaskBarItem> updateTaskBarItems = new List<UpdateTaskBarItem>();
		List<TaskBarItem> addedTaskBarItems = new List<TaskBarItem>();
		List<IntPtr> removedWindowHandles = new List<IntPtr>();

		var foregroundHwnd = NativeMethods.GetForegroundWindow();

		foreach (var windowHandle in WindowHandles)
		{
			// タスクバーとして管理すべきなWindowハンドルか？
			var isTaskBarWindow = IsTaskBarWindow(windowHandle);
			
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
					var processId = UwpUtility.GetProcessId(windowHandle);
					var appxPackage = AppxPackageUtility.AppxPackage.FromWindow(windowHandle);
					var taskBarItem = new TaskBarItem(windowHandle, GetWindowText(windowHandle), UwpUtility.GetProcessName(windowHandle), windowHandle == foregroundHwnd);
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
			updateTaskBarItems.Add(new UpdateTaskBarItem(taskBarWindow.Handle, GetWindowText(taskBarWindow.Handle), taskBarWindow.Handle == foregroundHwnd));
		}

		WindowListChanged?.Invoke(this, new TaskBarWindowEventArgs(updateTaskBarItems, addedTaskBarItems, removedWindowHandles));
	}

	public static string? GetIconFilePath(IntPtr hwnd)
	{
		try
		{
			NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
			var process = Process.GetProcessById(processId);
			return process.MainModule?.FileName;
		}
		catch (System.ComponentModel.Win32Exception ex)
		{
			if (ex.Message.Contains("アクセスが拒否されました"))
			{
				return null;
			}
			throw;
		}
	}

	public static string GetWindowText(IntPtr hwnd)
	{
		var sb = new StringBuilder(255);
		NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
		return sb.ToString();
	}

	public static bool IsTaskBarWindow(IntPtr hwnd)
	{
		// ウィンドウのスタイルをチェック
		var style = NativeMethods.WS_BORDER | NativeMethods.WS_VISIBLE;
		if ((NativeMethods.GetWindowLongA(hwnd, NativeMethods.GWL_STYLE) & style) != style)
		{
			return false;
		}

		// ウィンドウが可視かどうかチェック
		if (!NativeMethods.IsWindowVisible(hwnd))
		{
			return false;
		}

		// 親ウィンドウがないことを確認
		if (NativeMethods.GetParent(hwnd) != IntPtr.Zero)
		{
			return false;
		}

		// ツールウィンドウを除外
		if ((NativeMethods.GetWindowLongA(hwnd, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOOLWINDOW) != 0)
		{
			return false;
		}

		// ウィンドウがクリッピング領域を持っているか確認（実際に表示されているウィンドウはクリッピング領域を持つ）
		NativeMethods.RECT rect;
		if (!NativeMethods.GetWindowRect(hwnd, out rect))
		{
			return false;
		}
    
		// ウィンドウが最小化されていないか確認
		if (NativeMethods.IsIconic(hwnd))
		{
			// 最小化されているウィンドウもタスクバーに表示されるので、これはtrueを返す
			return true;
		}
    
		// ウィンドウが実際に表示されているか確認（幅と高さが0より大きい）
		if (rect.Right - rect.Left <= 0 || rect.Bottom - rect.Top <= 0)
		{
			return false;
		}
    
		// ウィンドウタイトルの長さをチェック（タイトルのないウィンドウは通常タスクバーに表示されない）
		int textLen = NativeMethods.GetWindowTextLength(hwnd);
		if (textLen <= 0)
		{
			return false;
		}
    
		// ウィンドウがタスクバーに表示されないように明示的に設定されているか確認
		if ((NativeMethods.GetWindowLongA(hwnd, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_NOACTIVATE) != 0)
		{
			return false;
		}
    
		return true;
	}

	public void Dispose()
	{
		Stop();
	}
}

public class TaskBarWindowEventArgs : EventArgs
{
	public List<UpdateTaskBarItem> UpdateTaskBarItems { get; }

	public List<TaskBarItem> AddedTaskBarItems { get; }

	public List<IntPtr> RemovedWindowHandles { get; }

	public TaskBarWindowEventArgs(List<UpdateTaskBarItem> updateTaskBarItems, List<TaskBarItem> addedTaskBarItems, List<IntPtr> removedWindows)
	{
		UpdateTaskBarItems = updateTaskBarItems;
		AddedTaskBarItems = addedTaskBarItems;
		RemovedWindowHandles = removedWindows;
	}
}