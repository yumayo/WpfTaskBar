using System.Windows;
using System.Windows.Media;

namespace WpfTaskBar;

public class NativeMethodUtility
{
	public static double GetPixelsPerDpi()
	{
		var dpiScale = VisualTreeHelper.GetDpi(Application.Current.MainWindow);
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
