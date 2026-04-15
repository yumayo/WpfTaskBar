using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace WpfTaskBar;

public sealed class WindowIconService
{
	public sealed record IconResult(string? Base64, string Trace);

	private sealed record IconPayload(string? Base64, string Trace);
	private sealed record CachedIconPayload(string? Base64, string TraceSuffix);
	private sealed record ExeIconResult(BitmapSource? SelectedIcon, string ComparisonTrace);
	private sealed record ResolvedIconSource(string? PreferredPath, string AppxTrace, string? ProcessPath);

	private const int MinimumRecommendedIconPixels = 36;
	private const int PreferredExtractedIconPixels = 64;

	private readonly ConcurrentDictionary<long, string> _lastIconTraceByHandle = new();
	private readonly ConcurrentDictionary<string, string> _exeComparisonTraceByPath = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<long, string?> _aumidByHandle = new();
	private readonly ConcurrentDictionary<string, ResolvedIconSource> _resolvedIconSourceByKey = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, CachedIconPayload> _iconPayloadByPath = new(StringComparer.OrdinalIgnoreCase);

	public IconResult ResolveWindowIcon(IntPtr handle, string? processPath, string title)
	{
		var (windowIcon, windowIconSource, windowIconSize) = GetWindowIcon(handle);
		var resolvedIconSource = ResolveIconSource(handle, processPath);
		var resolvedIconPath = resolvedIconSource.PreferredPath;
		var hasPreferredAppxIcon = !string.IsNullOrWhiteSpace(resolvedIconPath)
			&& !string.Equals(resolvedIconPath, processPath, StringComparison.OrdinalIgnoreCase)
			&& IsRasterImageFile(resolvedIconPath);

		IconPayload iconPayload;
		if (hasPreferredAppxIcon)
		{
			windowIcon?.Dispose();

			var processComparisonTrace = GetCachedExeComparisonTrace(resolvedIconSource.ProcessPath);
			var tracePrefix = $"appx-preferred {resolvedIconSource.AppxTrace}";
			if (!string.IsNullOrWhiteSpace(processComparisonTrace))
			{
				tracePrefix += $" {processComparisonTrace}";
			}

			iconPayload = GetIconPayload(resolvedIconPath, tracePrefix);
		}
		else if (windowIcon != null)
		{
			var shouldFallbackToFile = windowIconSize.Width > 0
				&& windowIconSize.Height > 0
				&& Math.Min(windowIconSize.Width, windowIconSize.Height) < MinimumRecommendedIconPixels
				&& !string.IsNullOrWhiteSpace(resolvedIconPath);

			if (shouldFallbackToFile)
			{
				windowIcon.Dispose();
				iconPayload = GetIconPayload(resolvedIconPath, $"file-fallback:small-window-icon source={windowIconSource} originalSize={windowIconSize.Width}x{windowIconSize.Height}");
			}
			else
			{
				using (windowIcon)
				{
					iconPayload = GetIconPayload(windowIcon, $"window-icon:{windowIconSource} handleSize={windowIconSize.Width}x{windowIconSize.Height} resolvedPath={resolvedIconPath ?? "(null)"}");
				}
			}
		}
		else
		{
			iconPayload = GetIconPayload(resolvedIconPath, "file");
		}

		TraceResolvedIcon(handle, processPath, title, iconPayload.Trace);
		return new IconResult(iconPayload.Base64, iconPayload.Trace);
	}

	private void TraceResolvedIcon(IntPtr handle, string? processName, string title, string iconTrace)
	{
		var key = handle.ToInt64();
		if (_lastIconTraceByHandle.TryGetValue(key, out var previous) && previous == iconTrace)
		{
			return;
		}

		_lastIconTraceByHandle[key] = iconTrace;
		Logger.Trace($"IconTrace handle={key} process={processName ?? ""} title={title} source={iconTrace}");
	}

	private IconPayload GetIconPayload(System.Drawing.Icon icon, string tracePrefix)
	{
		try
		{
			var bitmapSource = GetIcon(icon);
			if (bitmapSource == null)
			{
				return new IconPayload(null, $"{tracePrefix} output=(null)");
			}

			var encoder = new PngBitmapEncoder();
			encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

			using var stream = new MemoryStream();
			encoder.Save(stream);
			return new IconPayload(
				Convert.ToBase64String(stream.ToArray()),
				$"{tracePrefix} output={bitmapSource.PixelWidth}x{bitmapSource.PixelHeight}");
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "ウィンドウハンドル由来のアイコン変換に失敗しました。");
			return new IconPayload(null, $"{tracePrefix} output=error");
		}
	}

	private IconPayload GetIconPayload(string? moduleFileName, string tracePrefix)
	{
		if (string.IsNullOrEmpty(moduleFileName))
		{
			return new IconPayload(null, $"{tracePrefix} path=(null) output=(null)");
		}

		if (ShouldSkipIconLookup(moduleFileName))
		{
			return new IconPayload(null, $"{tracePrefix} path={moduleFileName} output=skipped");
		}

		var cachedPayload = _iconPayloadByPath.GetOrAdd(moduleFileName, CreateCachedIconPayload);
		return new IconPayload(cachedPayload.Base64, $"{tracePrefix} {cachedPayload.TraceSuffix}");
	}

	private CachedIconPayload CreateCachedIconPayload(string moduleFileName)
	{
		try
		{
			if (IsRasterImageFile(moduleFileName))
			{
				var bytes = File.ReadAllBytes(moduleFileName);
				using var imageStream = new MemoryStream(bytes);
				var bitmap = BitmapFrame.Create(imageStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
				bitmap.Freeze();
				return new CachedIconPayload(
					Convert.ToBase64String(bytes),
					$"path={moduleFileName} kind=raster output={bitmap.PixelWidth}x{bitmap.PixelHeight}");
			}

			var traceSuffix = string.Empty;
			BitmapSource? icon;
			if (moduleFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
			{
				var exeIconResult = GetExeIconResult(moduleFileName);
				icon = exeIconResult.SelectedIcon;
				traceSuffix = $" {exeIconResult.ComparisonTrace}";
			}
			else
			{
				icon = GetIcon(moduleFileName);
			}

			if (icon == null)
			{
				Logger.Info($"GetIconAsBase64: アイコン取得失敗 {moduleFileName}");
				return new CachedIconPayload(null, $"path={moduleFileName} kind=icon output=(null){traceSuffix}");
			}

			var encoder = new PngBitmapEncoder();
			encoder.Frames.Add(BitmapFrame.Create(icon));

			using var stream = new MemoryStream();
			encoder.Save(stream);
			return new CachedIconPayload(
				Convert.ToBase64String(stream.ToArray()),
				$"path={moduleFileName} kind=icon output={icon.PixelWidth}x{icon.PixelHeight}{traceSuffix}");
		}
		catch (Exception ex)
		{
			Logger.Error(ex, $"アイコンの変換に失敗しました: {moduleFileName}");
			return new CachedIconPayload(null, $"path={moduleFileName} output=error");
		}
	}

	private static bool IsRasterImageFile(string filePath)
	{
		var extension = Path.GetExtension(filePath);
		return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
	}

	private static bool ShouldSkipIconLookup(string path)
	{
		if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
		{
			if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
			{
				var fileName = Path.GetFileName(uri.AbsolutePath);
				return string.Equals(fileName, "favicon.ico", StringComparison.OrdinalIgnoreCase)
					|| fileName.StartsWith("favicon", StringComparison.OrdinalIgnoreCase);
			}
		}

		var localFileName = Path.GetFileName(path);
		return string.Equals(localFileName, "favicon.ico", StringComparison.OrdinalIgnoreCase)
			|| localFileName.StartsWith("favicon.", StringComparison.OrdinalIgnoreCase);
	}

	private static (System.Drawing.Icon? Icon, string Source, System.Drawing.Size Size) GetWindowIcon(IntPtr handle)
	{
		var candidates = new (IntPtr Handle, string Source)[]
		{
			(NativeMethods.SendMessage(handle, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_SMALL2, IntPtr.Zero), "WM_GETICON/ICON_SMALL2"),
			(NativeMethods.SendMessage(handle, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_SMALL, IntPtr.Zero), "WM_GETICON/ICON_SMALL"),
			(NativeMethods.SendMessage(handle, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_BIG, IntPtr.Zero), "WM_GETICON/ICON_BIG"),
			(NativeMethods.GetClassLongPtr(handle, NativeMethods.GCL_HICONSM), "GCL_HICONSM"),
			(NativeMethods.GetClassLongPtr(handle, NativeMethods.GCL_HICON), "GCL_HICON")
		};

		System.Drawing.Icon? bestIcon = null;
		var bestSource = "none";
		var bestSize = System.Drawing.Size.Empty;
		var bestScore = -1;

		foreach (var candidate in candidates)
		{
			if (candidate.Handle == IntPtr.Zero)
			{
				continue;
			}

			try
			{
				var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(candidate.Handle).Clone();
				var size = icon.Size;
				var score = size.Width * size.Height;
				if (score > bestScore)
				{
					bestIcon?.Dispose();
					bestIcon = icon;
					bestSource = candidate.Source;
					bestSize = size;
					bestScore = score;
				}
				else
				{
					icon.Dispose();
				}
			}
			catch (Exception ex)
			{
				Logger.Error(ex, $"ウィンドウアイコン候補の取得に失敗しました: handle={handle} source={candidate.Source}");
			}
		}

		return bestIcon == null ? (null, bestSource, bestSize) : (bestIcon, bestSource, bestSize);
	}

	private string? GetCachedExeComparisonTrace(string? processPath)
	{
		if (string.IsNullOrWhiteSpace(processPath) || !processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		return _exeComparisonTraceByPath.GetOrAdd(processPath, path =>
		{
			var comparison = GetExeComparisonTrace(path);
			return $"processCompare={comparison}";
		});
	}

	private ResolvedIconSource ResolveIconSource(IntPtr handle, string? processPath)
	{
		var aumid = _aumidByHandle.GetOrAdd(handle.ToInt64(), _ => GetWindowApplicationUserModelId(handle));
		var cacheKey = !string.IsNullOrWhiteSpace(aumid)
			? $"aumid:{aumid}"
			: $"process:{processPath ?? string.Empty}";

		return _resolvedIconSourceByKey.GetOrAdd(cacheKey, _ =>
		{
			var package = !string.IsNullOrWhiteSpace(aumid)
				? AppxPackage.FromApplicationUserModelId(aumid)
				: AppxPackage.FromWindow(handle);
			var packageLogoSelection = package?.GetBestLogoSelection();
			if (package is { } resolvedPackage && packageLogoSelection != null)
			{
				var appxTrace = $"appx=[aumid:{aumid ?? "(null)"},package:{resolvedPackage.FullName},label:{packageLogoSelection.Label},resource:{packageLogoSelection.Resource ?? "(null)"},preferred:{packageLogoSelection.PreferredSize},score:{packageLogoSelection.TargetSizeScore}/{packageLogoSelection.Scale}/{packageLogoSelection.Unplated}]";
				return new ResolvedIconSource(packageLogoSelection.Path, appxTrace, processPath);
			}

			var fallbackTrace = $"appx=[aumid:{aumid ?? "(null)"},package:{package?.FullName ?? "(null)"},label:(none),resource:(none),preferred:(none),score:(none)]";
			return new ResolvedIconSource(processPath, fallbackTrace, processPath);
		});
	}

	private static string? GetWindowApplicationUserModelId(IntPtr handle)
	{
		NativeMethods.IPropertyStore? propertyStore = null;
		NativeMethods.PROPVARIANT value = default;
		try
		{
			var propertyStoreGuid = typeof(NativeMethods.IPropertyStore).GUID;
			var hr = NativeMethods.SHGetPropertyStoreForWindow(handle, ref propertyStoreGuid, out propertyStore);
			if (hr != 0 || propertyStore == null)
			{
				return null;
			}

			var key = NativeMethods.PKEY_AppUserModel_ID;
			var propertyHr = propertyStore.GetValue(ref key, out value);
			return propertyHr == 0 ? value.GetValue() : null;
		}
		catch (Exception ex)
		{
			Logger.Error(ex, $"AUMID の取得に失敗しました: handle={handle}");
			return null;
		}
		finally
		{
			if (!value.IsEmpty)
			{
				NativeMethods.PropVariantClear(ref value);
			}

			if (propertyStore != null)
			{
				Marshal.ReleaseComObject(propertyStore);
			}
		}
	}

	public static BitmapSource? GetIcon(string iconFilePath)
	{
		try
		{
			System.Drawing.Icon? icon;
			if (iconFilePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
			{
				icon = System.Drawing.Icon.ExtractAssociatedIcon(iconFilePath);
			}
			else
			{
				icon = IconUtility.ConvertPngToIcon(iconFilePath);
			}

			return icon != null ? GetIcon(icon) : null;
		}
		catch (System.ComponentModel.Win32Exception ex)
		{
			if (ex.Message.Contains("アクセスが拒否されました"))
			{
				return null;
			}

			throw;
		}
		catch (Exception ex)
		{
			Logger.Error(ex, $"アイコンの取得に失敗しました: {iconFilePath}");
			return null;
		}
	}

	private static ExeIconResult GetExeIconResult(string iconFilePath)
	{
		var associatedIcon = GetBitmapSourceFromIcon(System.Drawing.Icon.ExtractAssociatedIcon(iconFilePath));
		var privateExtractIcon = GetBitmapSourceFromIcon(ExtractLargeAssociatedIcon(iconFilePath));
		var comparisonTrace = GetExeComparisonTrace(iconFilePath, associatedIcon, privateExtractIcon, "extractAssociated");
		return new ExeIconResult(associatedIcon, comparisonTrace);
	}

	private static string GetExeComparisonTrace(string iconFilePath)
	{
		var associatedIcon = GetBitmapSourceFromIcon(System.Drawing.Icon.ExtractAssociatedIcon(iconFilePath));
		var privateExtractIcon = GetBitmapSourceFromIcon(ExtractLargeAssociatedIcon(iconFilePath));
		return GetExeComparisonTrace(iconFilePath, associatedIcon, privateExtractIcon, "diagnostic");
	}

	private static string GetExeComparisonTrace(string iconFilePath, BitmapSource? associatedIcon, BitmapSource? privateExtractIcon, string selected)
	{
		return $"compare=[path:{iconFilePath},extractAssociated:{FormatBitmapSize(associatedIcon)},privateExtract:{FormatBitmapSize(privateExtractIcon)},selected:{selected}]";
	}

	private static System.Drawing.Icon? ExtractLargeAssociatedIcon(string iconFilePath)
	{
		var iconHandles = new IntPtr[1];
		var iconIds = new uint[1];
		try
		{
			var extracted = NativeMethods.PrivateExtractIcons(
				iconFilePath,
				0,
				PreferredExtractedIconPixels,
				PreferredExtractedIconPixels,
				iconHandles,
				iconIds,
				1,
				0);

			if (extracted == 0 || iconHandles[0] == IntPtr.Zero)
			{
				return null;
			}

			return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(iconHandles[0]).Clone();
		}
		catch (Exception ex)
		{
			Logger.Error(ex, $"大きい関連アイコンの取得に失敗しました: {iconFilePath}");
			return null;
		}
		finally
		{
			if (iconHandles[0] != IntPtr.Zero)
			{
				NativeMethods.DestroyIcon(iconHandles[0]);
			}
		}
	}

	private static BitmapSource? GetBitmapSourceFromIcon(System.Drawing.Icon? icon)
	{
		if (icon == null)
		{
			return null;
		}

		using (icon)
		{
			return GetIcon(icon);
		}
	}

	private static string FormatBitmapSize(BitmapSource? bitmapSource)
	{
		return bitmapSource == null ? "null" : $"{bitmapSource.PixelWidth}x{bitmapSource.PixelHeight}";
	}

	public static BitmapSource? GetIcon(System.Drawing.Icon icon)
	{
		using var bitmap = icon.ToBitmap();
		var hBitmap = bitmap.GetHbitmap();
		try
		{
			var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
			bitmapSource.Freeze();
			return bitmapSource;
		}
		finally
		{
			NativeMethods.DeleteObject(hBitmap);
		}
	}
}
