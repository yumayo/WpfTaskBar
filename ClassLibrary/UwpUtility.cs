// How to get the "Application Name" from hWnd for Windows 10 Store Apps (e.g. Edge)
// https://stackoverflow.com/questions/32001621/how-to-get-the-application-name-from-hwnd-for-windows-10-store-apps-e-g-edge

using System;
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
	public static IntPtr GetProcessId(IntPtr hWnd)
	{
		string processName = null;

		int pID;
		NativeMethods.GetWindowThreadProcessId(hWnd, out pID);

		IntPtr proc;
		if ((proc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_VM_READ, false, (int)pID)) == IntPtr.Zero)
			return IntPtr.Zero;

		int capacity = 2000;
		StringBuilder sb = new StringBuilder(capacity);
		NativeMethods.QueryFullProcessImageName(proc, 0, sb, ref capacity);

		processName = sb.ToString(0, capacity);

		// UWP apps are wrapped in another app called, if this has focus then try and find the child UWP process
		if (Path.GetFileName(processName).Equals("ApplicationFrameHost.exe"))
		{
			proc = UwpProcessId(hWnd, pID);
		}

		return proc;
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

			windowinfo = (WINDOWINFO)Marshal.PtrToStructure(pWindowinfo, typeof(WINDOWINFO));

			IntPtr proc;
			if ((proc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_VM_READ, false, (int)windowinfo.childpid)) == IntPtr.Zero)
				return IntPtr.Zero;

			return proc;
		}
		finally
		{
			Marshal.FreeHGlobal(pWindowinfo);
		}
	}
	
	public static string? GetProcessName(IntPtr hWnd)
	{
		string processName = null;

		int pID;
		NativeMethods.GetWindowThreadProcessId(hWnd, out pID);

		IntPtr proc;
		if ((proc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_VM_READ, false, (int)pID)) == IntPtr.Zero)
			return null;

		int capacity = 2000;
		StringBuilder sb = new StringBuilder(capacity);
		NativeMethods.QueryFullProcessImageName(proc, 0, sb, ref capacity);

		processName = sb.ToString(0, capacity);

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

			windowinfo = (WINDOWINFO)Marshal.PtrToStructure(pWindowinfo, typeof(WINDOWINFO));

			IntPtr proc;
			if ((proc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_VM_READ, false, (int)windowinfo.childpid)) == IntPtr.Zero)
				return null;

			int capacity = 2000;
			StringBuilder sb = new StringBuilder(capacity);
			NativeMethods.QueryFullProcessImageName(proc, 0, sb, ref capacity);
			
			return sb.ToString(0, capacity);
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
		WINDOWINFO info = (WINDOWINFO)Marshal.PtrToStructure(lParam, typeof(WINDOWINFO));

		int pID;
		NativeMethods.GetWindowThreadProcessId(hWnd, out pID);

		if (pID != info.ownerpid)
			info.childpid = pID;

		Marshal.StructureToPtr(info, lParam, true);

		return true;
	}
}
