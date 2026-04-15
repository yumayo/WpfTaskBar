using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace WpfTaskBar
{
	public sealed class AppxPackage
	{
		public sealed record LogoSelection(string Label, string? Resource, string Path, int PreferredSize, int TargetSizeScore, int Scale, int Unplated);

		private const int DefaultPreferredLogoSize = 64;
		private static readonly ConcurrentDictionary<string, AppxPackage[]> QueryPackageInfoCache = new(StringComparer.OrdinalIgnoreCase);
		private static readonly ConcurrentDictionary<string, string[]> PackageFullNamesByFamilyCache = new(StringComparer.OrdinalIgnoreCase);
		private List<AppxApp> _apps = new List<AppxApp>();
		private AppxNativeMethods.IAppxManifestProperties _properties = null!;

		private AppxPackage()
		{
		}

		private AppxPackage(AppxPackage source)
		{
			FullName = source.FullName;
			Path = source.Path;
			Publisher = source.Publisher;
			PublisherId = source.PublisherId;
			ResourceId = source.ResourceId;
			FamilyName = source.FamilyName;
			ApplicationUserModelId = source.ApplicationUserModelId;
			Logo = source.Logo;
			PublisherDisplayName = source.PublisherDisplayName;
			Description = source.Description;
			DisplayName = source.DisplayName;
			IsFramework = source.IsFramework;
			Version = source.Version;
			ProcessorArchitecture = source.ProcessorArchitecture;
			_properties = source._properties;
			_apps = source._apps;
		}

		public string FullName { get; private set; } = "";

		public string Path { get; private set; } = "";

		public string Publisher { get; private set; } = "";

		public string PublisherId { get; private set; } = "";

		public string ResourceId { get; private set; } = "";

		public string FamilyName { get; private set; } = "";

		public string ApplicationUserModelId { get; private set; } = "";

		public string Logo { get; private set; } = "";

		public string PublisherDisplayName { get; private set; } = "";

		public string Description { get; private set; } = "";

		public string DisplayName { get; private set; } = "";

		public bool IsFramework { get; private set; }

		public Version Version { get; private set; } = new Version();

		public AppxPackageArchitecture ProcessorArchitecture { get; private set; }

		public IReadOnlyList<AppxApp> Apps
		{
			get { return _apps; }
		}

		public IEnumerable<AppxPackage> DependencyGraph
		{
			get { return QueryPackageInfo(FullName, AppxNativeMethods.PackageConstants.PACKAGE_FILTER_ALL_LOADED).Where(p => p.FullName != FullName); }
		}

		public string? FindHighestScaleQualifiedImagePath(string resourceName)
		{
			if (resourceName == null)
				throw new ArgumentNullException("resourceName");

			const string scaleToken = ".scale-";
			var sizes = new List<int>();
			string name = System.IO.Path.GetFileNameWithoutExtension(resourceName);
			string ext = System.IO.Path.GetExtension(resourceName);
			var resourceDir = System.IO.Path.GetDirectoryName(resourceName);
			if (resourceDir == null)
				return null;

			foreach (var file in System.IO.Directory.EnumerateFiles(System.IO.Path.Combine(Path, resourceDir), name + scaleToken + "*" + ext))
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
			return System.IO.Path.Combine(Path, resourceDir, name + scaleToken + sizes.Last() + ext);
		}

		public AppxApp? FindAppByApplicationUserModelId()
		{
			if (_apps.Count == 0)
				return null;

			if (string.IsNullOrWhiteSpace(ApplicationUserModelId))
				return _apps.Count == 1 ? _apps[0] : null;

			var parts = ApplicationUserModelId.Split('!', 2, StringSplitOptions.TrimEntries);
			if (parts.Length == 2)
			{
				var app = _apps.FirstOrDefault(x => string.Equals(x.Id, parts[1], StringComparison.OrdinalIgnoreCase));
				if (app != null)
					return app;
			}

			return _apps.Count == 1 ? _apps[0] : null;
		}

		public string? GetBestLogoPath()
		{
			return GetBestLogoSelection(FindAppByApplicationUserModelId())?.Path;
		}

		public string? GetBestLogoPath(AppxApp? app)
		{
			return GetBestLogoSelection(app)?.Path;
		}

		public LogoSelection? GetBestLogoSelection()
		{
			return GetBestLogoSelection(FindAppByApplicationUserModelId());
		}

		public LogoSelection? GetBestLogoSelection(AppxApp? app)
		{
			foreach (var candidate in EnumerateLogoCandidates(app))
			{
				var resolved = ResolveBestImageSelection(candidate.Label, candidate.Resource);
				if (resolved != null)
					return resolved;
			}

			return null;
		}

		public override string ToString()
		{
			return FullName;
		}

		private AppxPackage Clone()
		{
			return new AppxPackage(this);
		}

		public static AppxPackage? FromWindow(IntPtr handle)
		{
			int processId;
			AppxNativeMethods.GetWindowThreadProcessId(handle, out processId);
			if (processId == 0)
				return null;

			return FromProcess(handle, processId);
		}

		public static AppxPackage? FromProcess(Process? process)
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

		public static AppxPackage? FromProcess(IntPtr hwnd, int processId)
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
				var uwpProcessId = (int)UwpUtility.GetProcessId(hwnd);
				if (uwpProcessId == 0 || uwpProcessId == processId)
				{
					return null;
				}

				return FromUwpProcess(uwpProcessId);
			}
			finally
			{
				if (hProcess != IntPtr.Zero)
				{
					AppxNativeMethods.CloseHandle(hProcess);
				}
			}
		}

		public static AppxPackage? FromUwpProcess(int processId)
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

		public static AppxPackage? FromApplicationUserModelId(string? applicationUserModelId)
		{
			if (string.IsNullOrWhiteSpace(applicationUserModelId))
				return null;

			var familyName = GetPackageFamilyNameFromApplicationUserModelId(applicationUserModelId);
			if (string.IsNullOrWhiteSpace(familyName))
				return null;

			foreach (var fullName in FindPackageFullNamesByFamily(familyName))
			{
				var package = QueryPackageInfo(fullName, AppxNativeMethods.PackageConstants.PACKAGE_FILTER_HEAD).FirstOrDefault();
				if (package == null)
					continue;

				package.ApplicationUserModelId = applicationUserModelId;
				if (package.FindAppByApplicationUserModelId() != null || package.Apps.Count == 1)
					return package;
			}

			return null;
		}

		public static AppxPackage? FromProcess(IntPtr hProcess)
		{
			if (hProcess == IntPtr.Zero)
				return null;

			// hprocess must have been opened with QueryLimitedInformation
			int len = 0;
			AppxNativeMethods.GetPackageFullName(hProcess, ref len, null!);
			if (len == 0)
				return null;

			var sb = new StringBuilder(len);
			string? fullName = AppxNativeMethods.GetPackageFullName(hProcess, ref len, sb) == 0 ? sb.ToString() : null;
			if (string.IsNullOrEmpty(fullName)) // not an AppX
				return null;

			var package = QueryPackageInfo(fullName, AppxNativeMethods.PackageConstants.PACKAGE_FILTER_HEAD).First();

			len = 0;
			AppxNativeMethods.GetApplicationUserModelId(hProcess, ref len, null!);
			sb = new StringBuilder(len);
			package.ApplicationUserModelId = AppxNativeMethods.GetApplicationUserModelId(hProcess, ref len, sb) == 0 ? sb.ToString() : "";
			return package;
		}

		public string? GetPropertyStringValue(string name)
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

		public string? LoadResourceString(string resource)
		{
			return LoadResourceString(FullName, resource);
		}

		private static IEnumerable<AppxPackage> QueryPackageInfo(string fullName, AppxNativeMethods.PackageConstants flags)
		{
			var cacheKey = $"{flags}:{fullName}";
			foreach (var package in QueryPackageInfoCache.GetOrAdd(cacheKey, _ => LoadPackageInfo(fullName, flags)))
			{
				yield return package.Clone();
			}
		}

		private static AppxPackage[] LoadPackageInfo(string fullName, AppxNativeMethods.PackageConstants flags)
		{
			var packages = new List<AppxPackage>();
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
							var info = (AppxNativeMethods.PACKAGE_INFO)Marshal.PtrToStructure(infoBuffer + i * Marshal.SizeOf(typeof(AppxNativeMethods.PACKAGE_INFO)), typeof(AppxNativeMethods.PACKAGE_INFO))!;
							var package = new AppxPackage();
							package.FamilyName = Marshal.PtrToStringUni(info.packageFamilyName) ?? "";
							package.FullName = Marshal.PtrToStringUni(info.packageFullName) ?? "";
							package.Path = Marshal.PtrToStringUni(info.path) ?? "";
							package.Publisher = Marshal.PtrToStringUni(info.packageId.publisher) ?? "";
							package.PublisherId = Marshal.PtrToStringUni(info.packageId.publisherId) ?? "";
							package.ResourceId = Marshal.PtrToStringUni(info.packageId.resourceId) ?? "";
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
								package.Description = package.GetPropertyStringValue("Description") ?? "";
								package.DisplayName = package.GetPropertyStringValue("DisplayName") ?? "";
								package.Logo = package.GetPropertyStringValue("Logo") ?? "";
								package.PublisherDisplayName = package.GetPropertyStringValue("PublisherDisplayName") ?? "";
								package.IsFramework = package.GetPropertyBoolValue("Framework");

								var apps = reader.GetApplications();
								while (apps.GetHasCurrent())
								{
									var app = apps.GetCurrent();
									var appx = new AppxApp(app);
									appx.Description = GetStringValue(app, "Description") ?? "";
									appx.DisplayName = GetStringValue(app, "DisplayName") ?? "";
									appx.EntryPoint = GetStringValue(app, "EntryPoint") ?? "";
									appx.Executable = GetStringValue(app, "Executable") ?? "";
									appx.Id = GetStringValue(app, "Id") ?? "";
									appx.Logo = GetStringValue(app, "Logo") ?? "";
									appx.SmallLogo = GetStringValue(app, "SmallLogo") ?? "";
									appx.StartPage = GetStringValue(app, "StartPage") ?? "";
									appx.Square150x150Logo = GetStringValue(app, "Square150x150Logo") ?? "";
									appx.Square30x30Logo = GetStringValue(app, "Square30x30Logo") ?? "";
									appx.Square44x44Logo = GetStringValue(app, "Square44x44Logo") ?? "";
									appx.BackgroundColor = GetStringValue(app, "BackgroundColor") ?? "";
									appx.ForegroundText = GetStringValue(app, "ForegroundText") ?? "";
									appx.WideLogo = GetStringValue(app, "WideLogo") ?? "";
									appx.Wide310x310Logo = GetStringValue(app, "Wide310x310Logo") ?? "";
									appx.ShortName = GetStringValue(app, "ShortName") ?? "";
									appx.Square310x310Logo = GetStringValue(app, "Square310x310Logo") ?? "";
									appx.Square70x70Logo = GetStringValue(app, "Square70x70Logo") ?? "";
									appx.MinWidth = GetStringValue(app, "MinWidth") ?? "";
									package._apps.Add(appx);
									apps.MoveNext();
								}
								Marshal.ReleaseComObject(strm);
							}
							packages.Add(package);
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

			return packages.ToArray();
		}

		public static string? LoadResourceString(string packageFullName, string resource)
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

		private string? ResolveBestImagePath(string? resourceName)
		{
			return ResolveBestImageSelection("", resourceName)?.Path;
		}

		private LogoSelection? ResolveBestImageSelection(string label, string? resourceName)
		{
			if (string.IsNullOrWhiteSpace(resourceName) || resourceName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
				return null;

			var normalizedResourceName = resourceName.Replace('/', System.IO.Path.DirectorySeparatorChar);
			var preferredSize = GetPreferredLogoSize(normalizedResourceName);
			if (System.IO.Path.IsPathRooted(normalizedResourceName))
			{
				if (!System.IO.File.Exists(normalizedResourceName))
					return null;

				return new LogoSelection(label, resourceName, normalizedResourceName, preferredSize, 3000, 100, 0);
			}

			var exactPath = System.IO.Path.Combine(Path, normalizedResourceName);
			var directory = System.IO.Path.GetDirectoryName(exactPath);
			if (string.IsNullOrWhiteSpace(directory) || !System.IO.Directory.Exists(directory))
			{
				if (!System.IO.File.Exists(exactPath))
					return null;

				return new LogoSelection(label, resourceName, exactPath, preferredSize, 3000, 100, 0);
			}

			var baseName = System.IO.Path.GetFileNameWithoutExtension(normalizedResourceName);
			var extension = System.IO.Path.GetExtension(normalizedResourceName);
			if (string.IsNullOrWhiteSpace(baseName) || string.IsNullOrWhiteSpace(extension))
			{
				if (!System.IO.File.Exists(exactPath))
					return null;

				return new LogoSelection(label, resourceName, exactPath, preferredSize, 3000, 100, 0);
			}

			return System.IO.Directory
				.EnumerateFiles(directory, $"{baseName}*{extension}")
				.Where(file => IsQualifiedResourceMatch(file, baseName, extension))
				.Select(file => new
				{
					Path = file,
					Score = ScoreQualifiedResource(file, baseName, preferredSize)
				})
				.OrderByDescending(x => x.Score.TargetSizeScore)
				.ThenByDescending(x => x.Score.Scale)
				.ThenByDescending(x => x.Score.Unplated)
				.ThenBy(x => x.Path.Length)
				.Select(x => new LogoSelection(label, resourceName, x.Path, preferredSize, x.Score.TargetSizeScore, x.Score.Scale, x.Score.Unplated))
				.FirstOrDefault();
		}

		private static bool IsQualifiedResourceMatch(string filePath, string expectedBaseName, string expectedExtension)
		{
			if (!string.Equals(System.IO.Path.GetExtension(filePath), expectedExtension, StringComparison.OrdinalIgnoreCase))
				return false;

			var candidateName = System.IO.Path.GetFileNameWithoutExtension(filePath);
			return candidateName.Equals(expectedBaseName, StringComparison.OrdinalIgnoreCase)
				|| candidateName.StartsWith(expectedBaseName + ".", StringComparison.OrdinalIgnoreCase);
		}

		private static (int TargetSizeScore, int Scale, int Unplated) ScoreQualifiedResource(string filePath, string baseName, int preferredSize)
		{
			var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
			if (fileName.Equals(baseName, StringComparison.OrdinalIgnoreCase))
				return (3000, 100, 0);

			var suffix = fileName.Substring(baseName.Length).TrimStart('.');
			var targetSize = preferredSize;
			var hasTargetSize = false;
			var scale = 100;
			var unplated = 0;

			foreach (var rawToken in suffix.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				if (rawToken.Contains("altform-unplated", StringComparison.OrdinalIgnoreCase))
				{
					unplated = 1;
				}

				foreach (var token in rawToken.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				{
					if (token.StartsWith("targetsize-", StringComparison.OrdinalIgnoreCase)
						&& int.TryParse(token.Substring("targetsize-".Length), out var parsedTargetSize))
					{
						targetSize = parsedTargetSize;
						hasTargetSize = true;
						continue;
					}

					if (token.StartsWith("scale-", StringComparison.OrdinalIgnoreCase)
						&& int.TryParse(token.Substring("scale-".Length), out var parsedScale))
					{
						scale = parsedScale;
						continue;
					}
				}
			}

			var targetSizeScore = hasTargetSize
				? 6000 - Math.Abs(targetSize - preferredSize)
				: 3500;
			return (targetSizeScore, scale, unplated);
		}

		private static int GetPreferredLogoSize(string resourceName)
		{
			var fileName = System.IO.Path.GetFileNameWithoutExtension(resourceName);
			var match = System.Text.RegularExpressions.Regex.Match(fileName, @"Square(?<size>\d+)x\d+Logo", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
			if (match.Success && int.TryParse(match.Groups["size"].Value, out var size))
				return Math.Max(size, DefaultPreferredLogoSize);

			return DefaultPreferredLogoSize;
		}

		private IEnumerable<(string Label, string? Resource)> EnumerateLogoCandidates(AppxApp? app)
		{
			yield return ("App.Square44x44Logo.Raw", app?.GetStringValue("Square44x44Logo"));
			yield return ("App.Square44x44Logo", app?.Square44x44Logo);
			yield return ("App.Logo", app?.Logo);
			yield return ("App.SmallLogo", app?.SmallLogo);
			yield return ("App.Square30x30Logo", app?.Square30x30Logo);
			yield return ("App.Square70x70Logo", app?.Square70x70Logo);
			yield return ("App.Square150x150Logo", app?.Square150x150Logo);
			yield return ("Package.Logo", Logo);
		}

		private static string? GetStringValue(AppxNativeMethods.IAppxManifestProperties? props, string name)
		{
			if (props == null)
				return null;

			string? value;
			props.GetStringValue(name, out value);
			return value;
		}

		private static bool GetBoolValue(AppxNativeMethods.IAppxManifestProperties? props, string name)
		{
			if (props == null)
				return false;

			bool value;
			props.GetBoolValue(name, out value);
			return value;
		}

		internal static string? GetStringValue(AppxNativeMethods.IAppxManifestApplication app, string name)
		{
			string? value;
			app.GetStringValue(name, out value);
			return value;
		}

		private static string? GetPackageFamilyNameFromApplicationUserModelId(string applicationUserModelId)
		{
			var separatorIndex = applicationUserModelId.IndexOf('!');
			if (separatorIndex <= 0)
				return null;

			return applicationUserModelId.Substring(0, separatorIndex);
		}

		private static IEnumerable<string> FindPackageFullNamesByFamily(string familyName)
		{
			foreach (var fullName in PackageFullNamesByFamilyCache.GetOrAdd(familyName, LoadPackageFullNamesByFamily))
			{
				yield return fullName;
			}
		}

		private static string[] LoadPackageFullNamesByFamily(string familyName)
		{
			var fullNames = new List<string>();
			uint count = 0;
			uint bufferLength = 0;
			var result = AppxNativeMethods.FindPackagesByPackageFamily(
				familyName,
				(uint)AppxNativeMethods.PackageConstants.PACKAGE_FILTER_HEAD,
				ref count,
				IntPtr.Zero,
				ref bufferLength,
				IntPtr.Zero,
				IntPtr.Zero);
			if (count == 0 || bufferLength == 0)
				return fullNames.ToArray();

			var fullNamePointers = Marshal.AllocHGlobal(IntPtr.Size * (int)count);
			var buffer = Marshal.AllocHGlobal(sizeof(char) * (int)bufferLength);
			try
			{
				result = AppxNativeMethods.FindPackagesByPackageFamily(
					familyName,
					(uint)AppxNativeMethods.PackageConstants.PACKAGE_FILTER_HEAD,
					ref count,
					fullNamePointers,
					ref bufferLength,
					buffer,
					IntPtr.Zero);
				if (result != 0 || count == 0)
					return fullNames.ToArray();

				for (var i = 0; i < count; i++)
				{
					var fullNamePtr = Marshal.ReadIntPtr(fullNamePointers, i * IntPtr.Size);
					var fullName = Marshal.PtrToStringUni(fullNamePtr);
					if (!string.IsNullOrWhiteSpace(fullName))
						fullNames.Add(fullName);
				}
			}
			finally
			{
				Marshal.FreeHGlobal(buffer);
				Marshal.FreeHGlobal(fullNamePointers);
			}

			return fullNames.ToArray();
		}


	}
}
