using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace WpfTaskBar
{
	public class AppxNativeMethods
	{
		[Guid("5842a140-ff9f-4166-8f5c-62f5b7b0c781"), ComImport]
		public class AppxFactory
		{
		}

		[Guid("BEB94909-E451-438B-B5A7-D79E767B75D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		public interface IAppxFactory
		{
			void _VtblGap0_2(); // skip 2 methods
			IAppxManifestReader CreateManifestReader(IStream inputStream);
		}

		[Guid("4E1BD148-55A0-4480-A3D1-15544710637C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		public interface IAppxManifestReader
		{
			void _VtblGap0_1(); // skip 1 method
			IAppxManifestProperties GetProperties();
			void _VtblGap1_5(); // skip 5 methods
			IAppxManifestApplicationsEnumerator GetApplications();
		}

		[Guid("9EB8A55A-F04B-4D0D-808D-686185D4847A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		public interface IAppxManifestApplicationsEnumerator
		{
			IAppxManifestApplication GetCurrent();
			bool GetHasCurrent();
			bool MoveNext();
		}

		[Guid("5DA89BF4-3773-46BE-B650-7E744863B7E8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		public interface IAppxManifestApplication
		{
			[PreserveSig]
			int GetStringValue([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] out string vaue);
		}

		[Guid("03FAF64D-F26F-4B2C-AAF7-8FE7789B8BCA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		public interface IAppxManifestProperties
		{
			[PreserveSig]
			int GetBoolValue([MarshalAs(UnmanagedType.LPWStr)] string name, out bool value);

			[PreserveSig]
			int GetStringValue([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] out string vaue);
		}

		[DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
		public static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

		[DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
		public static extern int SHCreateStreamOnFileEx(string fileName, int grfMode, int attributes, bool create, IntPtr reserved, out IStream stream);

		[DllImport("user32.dll")]
		public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

		[DllImport("kernel32.dll")]
		public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

		[DllImport("kernel32.dll")]
		public static extern bool CloseHandle(IntPtr hObject);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		public static extern int OpenPackageInfoByFullName(string packageFullName, int reserved, out IntPtr packageInfoReference);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		public static extern int GetPackageInfo(IntPtr packageInfoReference, PackageConstants flags, ref int bufferLength, IntPtr buffer, out int count);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		public static extern int ClosePackageInfo(IntPtr packageInfoReference);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		public static extern int GetPackageFullName(IntPtr hProcess, ref int packageFullNameLength, StringBuilder packageFullName);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		public static extern int GetApplicationUserModelId(IntPtr hProcess, ref int applicationUserModelIdLength, StringBuilder applicationUserModelId);

		[Flags]
		public enum PackageConstants
		{
			PACKAGE_FILTER_ALL_LOADED = 0x00000000,
			PACKAGE_PROPERTY_FRAMEWORK = 0x00000001,
			PACKAGE_PROPERTY_RESOURCE = 0x00000002,
			PACKAGE_PROPERTY_BUNDLE = 0x00000004,
			PACKAGE_FILTER_HEAD = 0x00000010,
			PACKAGE_FILTER_DIRECT = 0x00000020,
			PACKAGE_FILTER_RESOURCE = 0x00000040,
			PACKAGE_FILTER_BUNDLE = 0x00000080,
			PACKAGE_INFORMATION_BASIC = 0x00000000,
			PACKAGE_INFORMATION_FULL = 0x00000100,
			PACKAGE_PROPERTY_DEVELOPMENT_MODE = 0x00010000,
		}

		[StructLayout(LayoutKind.Sequential, Pack = 4)]
		public struct PACKAGE_INFO
		{
			public int reserved;
			public int flags;
			public IntPtr path;
			public IntPtr packageFullName;
			public IntPtr packageFamilyName;
			public PACKAGE_ID packageId;
		}

		[StructLayout(LayoutKind.Sequential, Pack = 4)]
		public struct PACKAGE_ID
		{
			public int reserved;
			public AppxPackageArchitecture processorArchitecture;
			public ushort VersionRevision;
			public ushort VersionBuild;
			public ushort VersionMinor;
			public ushort VersionMajor;
			public IntPtr name;
			public IntPtr publisher;
			public IntPtr resourceId;
			public IntPtr publisherId;
		}
	}
}