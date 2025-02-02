using System.Runtime.InteropServices;
using System.Text;

namespace WinFormsTaskBar
{
	public class NativeMethods
	{
		public static readonly int GWL_STYLE = -16;
		public static readonly ulong WS_VISIBLE = 0x10000000L;
		public static readonly ulong WS_BORDER = 0x00800000L;
		public static readonly int GWL_EXSTYLE = -20;
		public static readonly ulong WS_EX_TOOLWINDOW = 0x00000080L;

		[return: MarshalAs(UnmanagedType.Bool)]
		public delegate bool EnumWindowsCallback(IntPtr hwnd, int lParam);

		[DllImport("user32.dll")]
		public static extern int EnumWindows(EnumWindowsCallback lpEnumFunc, int lParam);

		[DllImport("user32.dll")]
		public static extern ulong GetWindowLongA(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool IsWindowVisible(IntPtr hWnd);

		[DllImport("user32.dll")]
		public static extern IntPtr GetParent(IntPtr hWnd);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern void GetWindowText(IntPtr hWnd, [MarshalAs(UnmanagedType.LPWStr), Out] StringBuilder lpString, int nMaxCount);

		[DllImport("user32.dll")]
		public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);
	}
}