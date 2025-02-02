using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WinFormsTaskBar;

namespace WinFormsTaskBar
{
	public class WindowManager : IDisposable
	{
		public List<IntPtr> WindowHandles = new List<IntPtr>();
		public List<Window> TaskBarWindows = new List<Window>();

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
			List<Window> addedWindows = new List<Window>();
			List<IntPtr> removedWindowHandles = new List<IntPtr>();

			foreach (var windowHandle in WindowHandles)
			{
				// すでに含まれていたら除外
				if (TaskBarWindows.Select(x => x.Handle).Contains(windowHandle))
				{
					continue;
				}

				// タスクバーに表示されるウィンドウでなければ除外
				var isTaskBarWindow = IsTaskBarWindow(windowHandle);
				if (!isTaskBarWindow)
				{
					continue;
				}

				var window = new Window(windowHandle, GetWindowText(windowHandle), GetIconFilePath(windowHandle));
				TaskBarWindows.Add(window);
				addedWindows.Add(window);
			}

			foreach (var taskBarWindow in TaskBarWindows.ToList())
			{
				if (!WindowHandles.Contains(taskBarWindow.Handle))
				{
					TaskBarWindows.Remove(taskBarWindow);
					removedWindowHandles.Add(taskBarWindow.Handle);
				}
			}

			foreach (var window in addedWindows)
			{
				Console.WriteLine($"追加({window.Handle.ToString(),10}) {window.Title}");
			}

			foreach (var hwnd in removedWindowHandles)
			{
				Console.WriteLine($"削除({hwnd.ToString(),10})");
			}

			WindowListChanged?.Invoke(this, new TaskBarWindowEventArgs(addedWindows, removedWindowHandles));
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
			var style = NativeMethods.WS_BORDER | NativeMethods.WS_VISIBLE;
			if ((NativeMethods.GetWindowLongA(hwnd, NativeMethods.GWL_STYLE) & style) != style)
			{
				return false;
			}

			if (!NativeMethods.IsWindowVisible(hwnd))
			{
				return false;
			}

			if (NativeMethods.GetParent(hwnd) != IntPtr.Zero)
			{
				return false;
			}

			// JetBrainsツールボックスを右クリックした際にウィンドウが表示されてしまうため、それを抑制する。
			if ((NativeMethods.GetWindowLongA(hwnd, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOOLWINDOW) != 0)
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
}

public class TaskBarWindowEventArgs : EventArgs
{
	public List<Window> AddedWindows { get; }

	public List<IntPtr> RemovedWindowHandles { get; }

	public TaskBarWindowEventArgs(List<Window> addedWindows, List<IntPtr> removedWindows)
	{
		AddedWindows = addedWindows;
		RemovedWindowHandles = removedWindows;
	}
}