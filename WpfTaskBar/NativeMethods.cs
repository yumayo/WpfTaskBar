using System.Runtime.InteropServices;
using System.Text;

namespace WpfTaskBar;

public class NativeMethods
{
	public static readonly int GWL_STYLE = -16;
	public static readonly ulong WS_VISIBLE = 0x10000000L;
	public static readonly ulong WS_BORDER = 0x00800000L;
	public static readonly int GWL_EXSTYLE = -20;
	public static readonly ulong WS_EX_APPWINDOW = 0x00040000L;
	public static readonly ulong WS_EX_TOOLWINDOW = 0x00000080L;
	public static readonly ulong WS_EX_NOACTIVATE = 0x08000000L;

	public static readonly int HWND_TOPMOST = -1;
	public static readonly int SWP_SHOWWINDOW = 0x0040;

	public static readonly int ABE_LEFT = 0;
	public static readonly int ABE_TOP = 1;
	public static readonly int ABE_RIGHT = 2;
	public static readonly int ABE_BOTTOM = 3;

	public static readonly int ABM_NEW = 0;
	public static readonly int ABM_REMOVE = 1;
	public static readonly int ABM_QUERYPOS = 2;
	public static readonly int ABM_SETPOS = 3;
	public static readonly int ABM_GETSTATE = 4;
	public static readonly int ABM_GETTASKBARPOS = 5;
	public static readonly int ABM_ACTIVATE = 6;
	public static readonly int ABM_GETAUTOHIDEBAR = 7;
	public static readonly int ABM_SETAUTOHIDEBAR = 8;
	public static readonly int ABM_WINDOWPOSCHANGED = 9;
	public static readonly int ABM_SETSTAT = 10;
	
	public static readonly uint WM_SYSCOMMAND = 0x0112;
	public static readonly int SC_RESTORE = 0xF120;
	public static readonly int SC_MINIMIZE = 0xF020;
	

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

	[DllImport("user32.dll")]
	public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

	[DllImport("user32.dll")]
	public static extern ulong SetWindowLongA(IntPtr hWnd, int index, ulong unValue);

	[StructLayout(LayoutKind.Sequential)]
	public struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
	public static extern uint SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int x, int y, int cx, int cy, int uFlags);

	[StructLayout(LayoutKind.Sequential)]
	public struct APPBARDATA
	{
		public int cbSize;
		public IntPtr hWnd;
		public int uCallbackMessage;
		public int uEdge;
		public RECT rc;
		public IntPtr lParam;
	}
	
	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool SetForegroundWindow(IntPtr hWnd);
	
	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool IsIconic(IntPtr hWnd);
	
	[DllImport("user32.dll")]
	public static extern IntPtr GetForegroundWindow();
}
