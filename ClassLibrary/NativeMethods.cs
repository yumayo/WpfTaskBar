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

	public static readonly uint WM_CLOSE = 0x0010;
	public static readonly uint WM_QUIT = 0x0012;
	public static readonly uint WM_SYSCOMMAND = 0x0112;
	public static readonly int SC_RESTORE = 0xF120;
	public static readonly int SC_MINIMIZE = 0xF020;
	public static readonly int SC_CLOSE = 0xF060;

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
	
	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	public static extern int GetWindowTextLength(IntPtr hWnd);

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

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool IsIconic(IntPtr hWnd);

	[DllImport("user32.dll")]
	public static extern IntPtr GetForegroundWindow();

	// How to get the "Application Name" from hWnd for Windows 10 Store Apps (e.g. Edge)
	// https://stackoverflow.com/questions/32001621/how-to-get-the-application-name-from-hwnd-for-windows-10-store-apps-e-g-edge

	/// <summary>
	/// Delegate for the EnumChildWindows method
	/// </summary>
	/// <param name="hWnd">Window handle</param>
	/// <param name="parameter">Caller-defined variable; we use it for a pointer to our list</param>
	/// <returns>True to continue enumerating, false to bail.</returns>
	public delegate bool EnumWindowProc(IntPtr hWnd, IntPtr parameter);

	[DllImport("user32", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowProc lpEnumFunc, IntPtr lParam);


	public const UInt32 PROCESS_QUERY_INFORMATION = 0x400;
	public const UInt32 PROCESS_VM_READ = 0x010;

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern bool QueryFullProcessImageName([In] IntPtr hProcess, [In] int dwFlags, [Out] StringBuilder lpExeName, ref int lpdwSize);

	[DllImport("kernel32.dll", SetLastError = true)]
	public static extern IntPtr OpenProcess(
		UInt32 dwDesiredAccess,
		[MarshalAs(UnmanagedType.Bool)] Boolean bInheritHandle,
		Int32 dwProcessId
	);
}
