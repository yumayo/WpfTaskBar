using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace WpfTaskBar
{
	public sealed class AppxPackage
	{
		private List<AppxApp> _apps = new List<AppxApp>();
		private AppxNativeMethods.IAppxManifestProperties _properties;

		private AppxPackage()
		{
		}

		public string FullName { get; private set; }

		public string Path { get; private set; }

		public string Publisher { get; private set; }

		public string PublisherId { get; private set; }

		public string ResourceId { get; private set; }

		public string FamilyName { get; private set; }

		public string ApplicationUserModelId { get; private set; }

		public string Logo { get; private set; }

		public string PublisherDisplayName { get; private set; }

		public string Description { get; private set; }

		public string DisplayName { get; private set; }

		public bool IsFramework { get; private set; }

		public Version Version { get; private set; }

		public AppxPackageArchitecture ProcessorArchitecture { get; private set; }

		public IReadOnlyList<AppxApp> Apps
		{
			get { return _apps; }
		}

		public IEnumerable<AppxPackage> DependencyGraph
		{
			get { return QueryPackageInfo(FullName, AppxNativeMethods.PackageConstants.PACKAGE_FILTER_ALL_LOADED).Where(p => p.FullName != FullName); }
		}

		public string FindHighestScaleQualifiedImagePath(string resourceName)
		{
			if (resourceName == null)
				throw new ArgumentNullException("resourceName");

			const string scaleToken = ".scale-";
			var sizes = new List<int>();
			string name = System.IO.Path.GetFileNameWithoutExtension(resourceName);
			string ext = System.IO.Path.GetExtension(resourceName);
			foreach (var file in Directory.EnumerateFiles(System.IO.Path.Combine(Path, System.IO.Path.GetDirectoryName(resourceName)), name + scaleToken + "*" + ext))
			{
				string fileName = System.IO.Path.GetFileNameWithoutExtension(file);
				int pos = fileName.IndexOf(scaleToken) + scaleToken.Length;
				string sizeText = fileName.Substring(pos);
				int size;
				if (int.TryParse(sizeText, out size))
				{
					sizes.Add(size);
				}
			}
			if (sizes.Count == 0)
				return null;

			sizes.Sort();
			return System.IO.Path.Combine(Path, System.IO.Path.GetDirectoryName(resourceName), name + scaleToken + sizes.Last() + ext);
		}

		public override string ToString()
		{
			return FullName;
		}

		public static AppxPackage FromWindow(IntPtr handle)
		{
			int processId;
			AppxNativeMethods.GetWindowThreadProcessId(handle, out processId);
			if (processId == 0)
				return null;

			return FromProcess(handle, processId);
		}

		public static AppxPackage FromProcess(Process process)
		{
			if (process == null)
			{
				process = Process.GetCurrentProcess();
			}

			try
			{
				return FromProcess(process.Handle);
			}
			catch
			{
				// probably access denied on .Handle
				return null;
			}
		}

		public static AppxPackage FromProcess(IntPtr hwnd, int processId)
		{
			const int QueryLimitedInformation = 0x1000;
			IntPtr hProcess = AppxNativeMethods.OpenProcess(QueryLimitedInformation, false, processId);
			try
			{
				var package = FromProcess(hProcess);
				if (package != null)
				{
					return package;
				}
				var uwpHandle = UwpUtility.GetProcessId(hwnd);
				AppxNativeMethods.GetWindowThreadProcessId(uwpHandle, out processId);
				return FromUwpProcess(processId);
			}
			finally
			{
				if (hProcess != IntPtr.Zero)
				{
					AppxNativeMethods.CloseHandle(hProcess);
				}
			}
		}

		public static AppxPackage FromUwpProcess(int processId)
		{
			const int QueryLimitedInformation = 0x1000;
			IntPtr hProcess = AppxNativeMethods.OpenProcess(QueryLimitedInformation, false, processId);
			try
			{
				return FromProcess(hProcess);
			}
			finally
			{
				if (hProcess != IntPtr.Zero)
				{
					AppxNativeMethods.CloseHandle(hProcess);
				}
			}
		}

		public static AppxPackage FromProcess(IntPtr hProcess)
		{
			if (hProcess == IntPtr.Zero)
				return null;

			// hprocess must have been opened with QueryLimitedInformation
			int len = 0;
			AppxNativeMethods.GetPackageFullName(hProcess, ref len, null);
			if (len == 0)
				return null;

			var sb = new StringBuilder(len);
			string fullName = AppxNativeMethods.GetPackageFullName(hProcess, ref len, sb) == 0 ? sb.ToString() : null;
			if (string.IsNullOrEmpty(fullName)) // not an AppX
				return null;

			var package = QueryPackageInfo(fullName, AppxNativeMethods.PackageConstants.PACKAGE_FILTER_HEAD).First();

			len = 0;
			AppxNativeMethods.GetApplicationUserModelId(hProcess, ref len, null);
			sb = new StringBuilder(len);
			package.ApplicationUserModelId = AppxNativeMethods.GetApplicationUserModelId(hProcess, ref len, sb) == 0 ? sb.ToString() : null;
			return package;
		}

		public string GetPropertyStringValue(string name)
		{
			if (name == null)
				throw new ArgumentNullException("name");

			return GetStringValue(_properties, name);
		}

		public bool GetPropertyBoolValue(string name)
		{
			if (name == null)
				throw new ArgumentNullException("name");

			return GetBoolValue(_properties, name);
		}

		public string LoadResourceString(string resource)
		{
			return LoadResourceString(FullName, resource);
		}

		private static IEnumerable<AppxPackage> QueryPackageInfo(string fullName, AppxNativeMethods.PackageConstants flags)
		{
			IntPtr infoRef;
			AppxNativeMethods.OpenPackageInfoByFullName(fullName, 0, out infoRef);
			if (infoRef != IntPtr.Zero)
			{
				IntPtr infoBuffer = IntPtr.Zero;
				try
				{
					int len = 0;
					int count;
					AppxNativeMethods.GetPackageInfo(infoRef, flags, ref len, IntPtr.Zero, out count);
					if (len > 0)
					{
						var factory = (AppxNativeMethods.IAppxFactory)new AppxNativeMethods.AppxFactory();
						infoBuffer = Marshal.AllocHGlobal(len);
						int res = AppxNativeMethods.GetPackageInfo(infoRef, flags, ref len, infoBuffer, out count);
						for (int i = 0; i < count; i++)
						{
							var info = (AppxNativeMethods.PACKAGE_INFO)Marshal.PtrToStructure(infoBuffer + i * Marshal.SizeOf(typeof(AppxNativeMethods.PACKAGE_INFO)), typeof(AppxNativeMethods.PACKAGE_INFO));
							var package = new AppxPackage();
							package.FamilyName = Marshal.PtrToStringUni(info.packageFamilyName);
							package.FullName = Marshal.PtrToStringUni(info.packageFullName);
							package.Path = Marshal.PtrToStringUni(info.path);
							package.Publisher = Marshal.PtrToStringUni(info.packageId.publisher);
							package.PublisherId = Marshal.PtrToStringUni(info.packageId.publisherId);
							package.ResourceId = Marshal.PtrToStringUni(info.packageId.resourceId);
							package.ProcessorArchitecture = info.packageId.processorArchitecture;
							package.Version = new Version(info.packageId.VersionMajor, info.packageId.VersionMinor, info.packageId.VersionBuild, info.packageId.VersionRevision);

							// read manifest
							string manifestPath = System.IO.Path.Combine(package.Path, "AppXManifest.xml");
							const int STGM_SHARE_DENY_NONE = 0x40;
							IStream strm;
							AppxNativeMethods.SHCreateStreamOnFileEx(manifestPath, STGM_SHARE_DENY_NONE, 0, false, IntPtr.Zero, out strm);
							if (strm != null)
							{
								var reader = factory.CreateManifestReader(strm);
								package._properties = reader.GetProperties();
								package.Description = package.GetPropertyStringValue("Description");
								package.DisplayName = package.GetPropertyStringValue("DisplayName");
								package.Logo = package.GetPropertyStringValue("Logo");
								package.PublisherDisplayName = package.GetPropertyStringValue("PublisherDisplayName");
								package.IsFramework = package.GetPropertyBoolValue("Framework");

								var apps = reader.GetApplications();
								while (apps.GetHasCurrent())
								{
									var app = apps.GetCurrent();
									var appx = new AppxApp(app);
									appx.Description = GetStringValue(app, "Description");
									appx.DisplayName = GetStringValue(app, "DisplayName");
									appx.EntryPoint = GetStringValue(app, "EntryPoint");
									appx.Executable = GetStringValue(app, "Executable");
									appx.Id = GetStringValue(app, "Id");
									appx.Logo = GetStringValue(app, "Logo");
									appx.SmallLogo = GetStringValue(app, "SmallLogo");
									appx.StartPage = GetStringValue(app, "StartPage");
									appx.Square150x150Logo = GetStringValue(app, "Square150x150Logo");
									appx.Square30x30Logo = GetStringValue(app, "Square30x30Logo");
									appx.BackgroundColor = GetStringValue(app, "BackgroundColor");
									appx.ForegroundText = GetStringValue(app, "ForegroundText");
									appx.WideLogo = GetStringValue(app, "WideLogo");
									appx.Wide310x310Logo = GetStringValue(app, "Wide310x310Logo");
									appx.ShortName = GetStringValue(app, "ShortName");
									appx.Square310x310Logo = GetStringValue(app, "Square310x310Logo");
									appx.Square70x70Logo = GetStringValue(app, "Square70x70Logo");
									appx.MinWidth = GetStringValue(app, "MinWidth");
									package._apps.Add(appx);
									apps.MoveNext();
								}
								Marshal.ReleaseComObject(strm);
							}
							yield return package;
						}
						Marshal.ReleaseComObject(factory);
					}
				}
				finally
				{
					if (infoBuffer != IntPtr.Zero)
					{
						Marshal.FreeHGlobal(infoBuffer);
					}
					AppxNativeMethods.ClosePackageInfo(infoRef);
				}
			}
		}

		public static string LoadResourceString(string packageFullName, string resource)
		{
			if (packageFullName == null)
				throw new ArgumentNullException("packageFullName");

			if (string.IsNullOrWhiteSpace(resource))
				return null;

			const string resourceScheme = "ms-resource:";
			if (!resource.StartsWith(resourceScheme))
				return null;

			string part = resource.Substring(resourceScheme.Length);
			string url;

			if (part.StartsWith("/"))
			{
				url = resourceScheme + "//" + part;
			}
			else
			{
				url = resourceScheme + "///resources/" + part;
			}

			string source = string.Format("@{{{0}? {1}}}", packageFullName, url);
			var sb = new StringBuilder(1024);
			int i = AppxNativeMethods.SHLoadIndirectString(source, sb, sb.Capacity, IntPtr.Zero);
			if (i != 0)
				return null;

			return sb.ToString();
		}

		private static string GetStringValue(AppxNativeMethods.IAppxManifestProperties props, string name)
		{
			if (props == null)
				return null;

			string value;
			props.GetStringValue(name, out value);
			return value;
		}

		private static bool GetBoolValue(AppxNativeMethods.IAppxManifestProperties props, string name)
		{
			bool value;
			props.GetBoolValue(name, out value);
			return value;
		}

		internal static string GetStringValue(AppxNativeMethods.IAppxManifestApplication app, string name)
		{
			string value;
			app.GetStringValue(name, out value);
			return value;
		}


	}
}