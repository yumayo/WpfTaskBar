using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace WpfTaskBar;

public static class NativeMethodUtility
{
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
	
	public static double GetPixelsPerDpi()
	{
		var dpiScale = VisualTreeHelper.GetDpi(System.Windows.Application.Current.MainWindow);
		return dpiScale.PixelsPerDip;
	}

	public static double GetTaskbarHeight()
	{
		IntPtr taskbarHandle = NativeMethods.FindWindow("Shell_TrayWnd", "");
		if (taskbarHandle == IntPtr.Zero)
		{
			return 0;
		}

		if (NativeMethods.GetWindowRect(taskbarHandle, out var rect))
		{
			return rect.Bottom - rect.Top;
		}

		return 0;
	}
}