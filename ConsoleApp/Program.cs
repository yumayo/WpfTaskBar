using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using WpfTaskBar;

namespace ConsoleApp;

class Program
{
	private static List<IntPtr> windowHandles = new List<IntPtr>();

	static Task Main(string[] args)
	{
		ShowWindowTitles();
		var windowHandles = FindWindowHandles("設定");
		foreach (var windowHandle in windowHandles)
		{
			var package = AppxPackage.FromWindow(windowHandle);
			if (package == null)
			{
				continue;
			}
			AppxPackageUtility.Show(0, AppxPackage.FromWindow(windowHandle));
			PrintAppPackage(windowHandle);
		}
		return Task.CompletedTask;
	}

	private static void ShowWindowTitles()
	{
		NativeMethods.EnumWindows(EnumerationWindows, 0);

		StringBuilder titleBuffer = new StringBuilder(256);
		foreach (var windowHandle in windowHandles)
		{
			if (!NativeMethodUtility.IsTaskBarWindow(windowHandle))
			{
				continue;
			}

			titleBuffer.Clear();
			NativeMethods.GetWindowText(windowHandle, titleBuffer, titleBuffer.Capacity);
			string windowTitle = titleBuffer.ToString();
			Console.WriteLine($"{windowHandle} {windowTitle}");
		}
	}

	private static List<IntPtr> FindWindowHandles(string title)
	{
		NativeMethods.EnumWindows(EnumerationWindows, 0);

		List<IntPtr> newWindowHandle = new List<IntPtr>();

		StringBuilder titleBuffer = new StringBuilder(256);
		foreach (var windowHandle in windowHandles)
		{
			titleBuffer.Clear();
			NativeMethods.GetWindowText(windowHandle, titleBuffer, titleBuffer.Capacity);
			string windowTitle = titleBuffer.ToString();
			if (windowTitle == title)
			{
				newWindowHandle.Add(windowHandle);
			}
		}

		return newWindowHandle;
	}

	private static void PrintAppPackage(IntPtr hWnd)
	{
		// タイトルを取得
		StringBuilder titleBuffer = new StringBuilder(256);
		NativeMethods.GetWindowText(hWnd, titleBuffer, titleBuffer.Capacity);
		string windowTitle = titleBuffer.ToString();

		NativeMethods.GetWindowThreadProcessId(hWnd, out var rootProcessId);
		// var processIds = ProcessUtility.GetDescendantProcessIds(rootProcessId);

		var processName = UwpUtility.GetProcessName(hWnd);
		var processId = (int)UwpUtility.GetProcessId(hWnd);
		var appxPackage = AppxPackage.FromUwpProcess(processId);

		// 情報を表示
		Console.WriteLine("===== Window Information =====");
		Console.WriteLine($"Window Handle: {hWnd}");
		Console.WriteLine($"Window Title: {windowTitle}");
		Console.WriteLine($"Process Name: {processName}");
		Console.WriteLine($"Process ID: {processId}");

		// AppxPackage情報を表示（nullでない場合）
		if (appxPackage != null)
		{
			AppxPackageUtility.Show(4, appxPackage);
		}
		else
		{
			Console.WriteLine("Appx Package: Not Available (Win32 application)");
		}

		Console.WriteLine();
	}

	private static Task ShowSettingsApp()
	{
		foreach (var windowHandle in windowHandles)
		{
			if (!NativeMethodUtility.IsTaskBarWindow(windowHandle))
			{
				continue;
			}

			// タイトルを取得
			StringBuilder titleBuffer = new StringBuilder(256);
			NativeMethods.GetWindowText(windowHandle, titleBuffer, titleBuffer.Capacity);
			string windowTitle = titleBuffer.ToString();

			if (windowTitle != "設定")
			{
				continue;
			}

			NativeMethods.GetWindowThreadProcessId(windowHandle, out var rootProcessId);
			var processIds = ProcessUtility.GetDescendantProcessIds(rootProcessId);

			var processName = UwpUtility.GetProcessName(windowHandle);
			var processId = (int)UwpUtility.GetProcessId(windowHandle);
			var appxPackage = AppxPackage.FromUwpProcess(processId);

			// 情報を表示
			Console.WriteLine("===== Window Information =====");
			Console.WriteLine($"Window Handle: {windowHandle}");
			Console.WriteLine($"Window Title: {windowTitle}");
			Console.WriteLine($"Process Name: {processName}");
			Console.WriteLine($"Process ID: {processId}");

			// AppxPackage情報を表示（nullでない場合）
			if (appxPackage != null)
			{
				AppxPackageUtility.Show(4, appxPackage);
			}
			else
			{
				Console.WriteLine("Appx Package: Not Available (Win32 application)");
			}

			Console.WriteLine();
		}

		return Task.CompletedTask;
	}

	[return: MarshalAs(UnmanagedType.Bool)]
	private static bool EnumerationWindows(IntPtr hwnd, int lParam)
	{
		// EnumWindowsで列挙されたWindowを全て記録する。
		windowHandles.Add(hwnd);
		return true;
	}
}