// How to get the "Application Name" from hWnd for Windows 10 Store Apps (e.g. Edge)
// https://stackoverflow.com/questions/32001621/how-to-get-the-application-name-from-hwnd-for-windows-10-store-apps-e-g-edge

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WpfTaskBar;

public struct WINDOWINFO
{
	public int ownerpid;
	public int childpid;
}

public class UwpUtility
{
	public static string? GetRawProcessName(IntPtr hWnd)
	{
		return GetWindowProcessImagePath(hWnd);
	}

	public static IntPtr GetProcessId(IntPtr hWnd)
	{
		int pID;
		NativeMethods.GetWindowThreadProcessId(hWnd, out pID);
		var processName = GetProcessImagePathFromProcessId(pID);
		if (processName == null)
		{
			return IntPtr.Zero;
		}

		// UWP apps are wrapped in another app called, if this has focus then try and find the child UWP process
		if (Path.GetFileName(processName).Equals("ApplicationFrameHost.exe"))
		{
			var childProcId = UwpProcessId(hWnd, pID);
			return childProcId;
		}

		return (IntPtr)pID;
	}

	/// <summary>
	/// Find child process for uwp apps, edge, mail, etc.
	/// </summary>
	/// <param name="hWnd">hWnd</param>
	/// <param name="pID">pID</param>
	/// <returns>The application name of the UWP.</returns>
	private static IntPtr UwpProcessId(IntPtr hWnd, int pID)
	{
		WINDOWINFO windowinfo = new WINDOWINFO();
		windowinfo.ownerpid = pID;
		windowinfo.childpid = windowinfo.ownerpid;

		IntPtr pWindowinfo = Marshal.AllocHGlobal(Marshal.SizeOf(windowinfo));
		try
		{
			Marshal.StructureToPtr(windowinfo, pWindowinfo, false);

			NativeMethods.EnumWindowProc lpEnumFunc = new NativeMethods.EnumWindowProc(EnumChildWindowsCallback);
			NativeMethods.EnumChildWindows(hWnd, lpEnumFunc, pWindowinfo);

			windowinfo = (WINDOWINFO)Marshal.PtrToStructure(pWindowinfo, typeof(WINDOWINFO))!;
			if (windowinfo.childpid != windowinfo.ownerpid)
			{
				return (IntPtr)windowinfo.childpid;
			}

			return FindDescendantUwpProcessId(windowinfo.ownerpid);
		}
		finally
		{
			Marshal.FreeHGlobal(pWindowinfo);
		}
	}
	
	public static string? GetProcessName(IntPtr hWnd)
	{
		int pID;
		NativeMethods.GetWindowThreadProcessId(hWnd, out pID);
		var processName = GetProcessImagePathFromProcessId(pID);
		if (processName == null)
		{
			return null;
		}

		// UWP apps are wrapped in another app called, if this has focus then try and find the child UWP process
		if (Path.GetFileName(processName).Equals("ApplicationFrameHost.exe"))
		{
			processName = UWP_AppName(hWnd, pID);
		}

		return processName;
	}

	/// <summary>
	/// Find child process for uwp apps, edge, mail, etc.
	/// </summary>
	/// <param name="hWnd">hWnd</param>
	/// <param name="pID">pID</param>
	/// <returns>The application name of the UWP.</returns>
	private static string? UWP_AppName(IntPtr hWnd, int pID)
	{
		WINDOWINFO windowinfo = new WINDOWINFO();
		windowinfo.ownerpid = pID;
		windowinfo.childpid = windowinfo.ownerpid;

		IntPtr pWindowinfo = Marshal.AllocHGlobal(Marshal.SizeOf(windowinfo));
		try
		{
			Marshal.StructureToPtr(windowinfo, pWindowinfo, false);

			NativeMethods.EnumWindowProc lpEnumFunc = new NativeMethods.EnumWindowProc(EnumChildWindowsCallback);
			NativeMethods.EnumChildWindows(hWnd, lpEnumFunc, pWindowinfo);

			windowinfo = (WINDOWINFO)Marshal.PtrToStructure(pWindowinfo, typeof(WINDOWINFO))!;
			if (windowinfo.childpid == windowinfo.ownerpid)
			{
				windowinfo.childpid = (int)FindDescendantUwpProcessId(windowinfo.ownerpid);
			}

			IntPtr proc;
			if ((proc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_VM_READ, false, (int)windowinfo.childpid)) == IntPtr.Zero)
				return null;

			try
			{
				int capacity = 2000;
				StringBuilder sb = new StringBuilder(capacity);
				NativeMethods.QueryFullProcessImageName(proc, 0, sb, ref capacity);
				
				return sb.ToString(0, capacity);
			}
			finally
			{
				NativeMethods.CloseHandle(proc);
			}
		}
		finally
		{
			Marshal.FreeHGlobal(pWindowinfo);
		}
	}

	/// <summary>
	/// Callback for enumerating the child windows.
	/// </summary>
	/// <param name="hWnd">hWnd</param>
	/// <param name="lParam">lParam</param>
	/// <returns>always <c>true</c>.</returns>
	private static bool EnumChildWindowsCallback(IntPtr hWnd, IntPtr lParam)
	{
		WINDOWINFO info = (WINDOWINFO)Marshal.PtrToStructure(lParam, typeof(WINDOWINFO))!;

		int pID;
		NativeMethods.GetWindowThreadProcessId(hWnd, out pID);

		if (pID != info.ownerpid)
			info.childpid = pID;

		Marshal.StructureToPtr(info, lParam, true);

		return true;
	}

	private static IntPtr FindDescendantUwpProcessId(int rootProcessId)
	{
		var descendantProcessIds = ProcessUtility.GetDescendantProcessIds(rootProcessId);
		IntPtr fallbackProcessId = IntPtr.Zero;
		foreach (var descendantProcessId in descendantProcessIds)
		{
			try
			{
				var process = Process.GetProcessById(descendantProcessId);
				if (string.Equals(process.ProcessName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				if (fallbackProcessId == IntPtr.Zero)
				{
					fallbackProcessId = (IntPtr)descendantProcessId;
				}

				if (AppxPackage.FromUwpProcess(descendantProcessId) != null)
				{
					return (IntPtr)descendantProcessId;
				}
			}
			catch
			{
				// ignore stale/inaccessible child processes and continue searching
			}
		}

		if (fallbackProcessId != IntPtr.Zero)
		{
			return fallbackProcessId;
		}

		return (IntPtr)rootProcessId;
	}

	private static string? GetWindowProcessImagePath(IntPtr hWnd)
	{
		int pID;
		NativeMethods.GetWindowThreadProcessId(hWnd, out pID);
		return GetProcessImagePathFromProcessId(pID);
	}

	private static string? GetProcessImagePathFromProcessId(int processId)
	{
		IntPtr proc;
		if ((proc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_VM_READ, false, processId)) == IntPtr.Zero)
		{
			return null;
		}

		try
		{
			int capacity = 2000;
			StringBuilder sb = new StringBuilder(capacity);
			NativeMethods.QueryFullProcessImageName(proc, 0, sb, ref capacity);
			return sb.ToString(0, capacity);
		}
		finally
		{
			NativeMethods.CloseHandle(proc);
		}
	}
}
