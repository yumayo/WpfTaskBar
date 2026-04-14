using System.Runtime.InteropServices;
using System.Text;

namespace WpfTaskBar;

public class NativeMethods
{
	private const ushort VT_EMPTY = 0;
	private const ushort VT_LPWSTR = 31;

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
	public static readonly uint WM_GETICON = 0x007F;
	public static readonly int SC_RESTORE = 0xF120;
	public static readonly int SC_MINIMIZE = 0xF020;
	public static readonly int SC_CLOSE = 0xF060;
	public static readonly int ICON_SMALL = 0;
	public static readonly int ICON_BIG = 1;
	public static readonly int ICON_SMALL2 = 2;
	public static readonly int GCL_HICON = -14;
	public static readonly int GCL_HICONSM = -34;

	// GetWindow用の定数
	public static readonly uint GW_HWNDFIRST = 0;
	public static readonly uint GW_HWNDLAST = 1;
	public static readonly uint GW_HWNDNEXT = 2;
	public static readonly uint GW_HWNDPREV = 3;
	public static readonly uint GW_OWNER = 4;
	public static readonly uint GW_CHILD = 5;

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

	[DllImport("user32.dll", EntryPoint = "GetClassLongPtr", SetLastError = true)]
	public static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "GetClassLong", SetLastError = true)]
	public static extern uint GetClassLongPtr32(IntPtr hWnd, int nIndex);

	public static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex)
	{
		return IntPtr.Size == 8
			? GetClassLongPtr64(hWnd, nIndex)
			: new IntPtr(unchecked((int)GetClassLongPtr32(hWnd, nIndex)));
	}

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool IsIconic(IntPtr hWnd);

	[DllImport("user32.dll")]
	public static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll", SetLastError = true)]
	public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern IntPtr GetTopWindow(IntPtr hWnd);

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

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool CloseHandle(IntPtr hObject);

	[DllImport("gdi32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool DeleteObject(IntPtr hObject);

	[DllImport("shell32.dll")]
	public static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, [In] ref Guid riid, [Out, MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

	[DllImport("ole32.dll")]
	public static extern int PropVariantClear(ref PROPVARIANT pvar);

	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct PROPERTYKEY
	{
		public Guid fmtid;
		public uint pid;

		public PROPERTYKEY(Guid fmtid, uint pid)
		{
			this.fmtid = fmtid;
			this.pid = pid;
		}
	}

	[StructLayout(LayoutKind.Explicit)]
	public struct PROPVARIANT
	{
		[FieldOffset(0)]
		public ushort vt;
		[FieldOffset(8)]
		public IntPtr pointerValue;

		public string? GetValue()
		{
			return vt == VT_LPWSTR && pointerValue != IntPtr.Zero
				? Marshal.PtrToStringUni(pointerValue)
				: null;
		}

		public bool IsEmpty => vt == VT_EMPTY;
	}

	[ComImport]
	[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface IPropertyStore
	{
		uint GetCount([Out] out uint cProps);
		uint GetAt([In] uint iProp, out PROPERTYKEY pkey);
		uint GetValue([In] ref PROPERTYKEY key, out PROPVARIANT pv);
		uint SetValue([In] ref PROPERTYKEY key, [In] ref PROPVARIANT pv);
		uint Commit();
	}

	public static readonly PROPERTYKEY PKEY_AppUserModel_ID = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
}
