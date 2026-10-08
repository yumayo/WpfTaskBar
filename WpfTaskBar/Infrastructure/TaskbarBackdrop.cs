using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Window = System.Windows.Window;

namespace WpfTaskBar;

/// <summary>
/// 非アクティブ時にも背景のぼかしを使えるよう、Accent の BlurBehind と一定の色を重ねる。
/// AllowsTransparency は false にする（レイヤードウィンドウでは使用しない）。
/// </summary>
internal sealed class TaskbarBackdrop : IDisposable
{
	private const int DwmwaSystemBackdropType = 38;
	private const int DwmsbtNone = 1;
	private const int WcaAccentPolicy = 19;
	private const int AccentDisabled = 0;
	private const int AccentEnableBlurBehind = 3;
	private const int WmShowWindow = 0x0018;
	private const int WmSettingChange = 0x001A;
	private const int WmThemeChanged = 0x031A;
	private const int WmDwmCompositionChanged = 0x031E;

	private readonly Window _window;
	private readonly HwndSource _source;
	private readonly System.Windows.Media.Brush _fallbackBackground;
	private readonly SolidColorBrush _tintBackground = new(
		System.Windows.Media.Color.FromArgb(0xB3, 0x21, 0x25, 0x2B));
	private DispatcherOperation? _pendingApply;
	private bool _accentApiAvailable = true;
	private bool _disposed;
	private string? _lastMode;

	public TaskbarBackdrop(Window window, HwndSource source)
	{
		_window = window;
		_source = source;
		_fallbackBackground = window.Background;
		_tintBackground.Freeze();
		_source.AddHook(WndProc);
		Apply();
	}

	private void Apply()
	{
		// 対象 OS は従来どおり Windows 11 22H2 以降。未対応 OS は単色のまま。
		if (_disposed || _source.IsDisposed || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
		{
			return;
		}

		if (SystemParameters.HighContrast || !IsTransparencyEnabled())
		{
			UseSolidBackground();
			LogMode("solid (Windows settings)");
			return;
		}

		// Desktop Acrylic は非アクティブ時などに不透明な素材へ切り替わる。
		// 常駐タスクバーでは無効にし、単純な BlurBehind を使用する。
		int backdrop = DwmsbtNone;
		int result = DwmSetWindowAttribute(_source.Handle, DwmwaSystemBackdropType,
			ref backdrop, sizeof(int));
		if (result >= 0)
		{
			var margins = new Margins(-1);
			result = DwmExtendFrameIntoClientArea(_source.Handle, ref margins);
		}

		if (result < 0)
		{
			UseSolidBackground();
			LogMode($"solid (DWM setup failed, HRESULT=0x{result:X8})");
			return;
		}

		if (!TrySetAccent(AccentEnableBlurBehind))
		{
			UseSolidBackground();
			LogMode("solid (SetWindowCompositionAttribute failed)");
			return;
		}

		// 色は WPF で重ね、フォーカスによって濃さが変わらないようにする。
		_source.CompositionTarget.BackgroundColor = Colors.Transparent;
		_window.Background = _tintBackground;
		LogMode("blur");
	}

	private static bool IsTransparencyEnabled()
	{
		try
		{
			using var key = Registry.CurrentUser.OpenSubKey(
				@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
			return key?.GetValue("EnableTransparency") is not int enabled || enabled != 0;
		}
		catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
		{
			Logger.Warning($"透明効果の設定を取得できないため単色を使用します。{ex.Message}");
			return false;
		}
	}

	private bool TrySetAccent(int state)
	{
		if (!_accentApiAvailable) return false;

		var policy = new AccentPolicy { State = state };
		int size = Marshal.SizeOf<AccentPolicy>();
		IntPtr policyPointer = Marshal.AllocHGlobal(size);
		try
		{
			Marshal.StructureToPtr(policy, policyPointer, false);
			var data = new WindowCompositionAttributeData
			{
				Attribute = WcaAccentPolicy,
				Data = policyPointer,
				SizeOfData = (nuint)size
			};
			return SetWindowCompositionAttribute(_source.Handle, ref data);
		}
		catch (EntryPointNotFoundException)
		{
			_accentApiAvailable = false;
			Logger.Warning("SetWindowCompositionAttribute が利用できないため単色を使用します。");
			return false;
		}
		finally
		{
			Marshal.FreeHGlobal(policyPointer);
		}
	}

	private void UseSolidBackground()
	{
		_window.Background = _fallbackBackground;
		TrySetAccent(AccentDisabled);
		int backdrop = DwmsbtNone;
		DwmSetWindowAttribute(_source.Handle, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
		var margins = new Margins(0);
		DwmExtendFrameIntoClientArea(_source.Handle, ref margins);
	}

	private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (message is WmDwmCompositionChanged or WmThemeChanged or WmSettingChange
			|| (message == WmShowWindow && wParam != IntPtr.Zero))
		{
			// Windows / WPF の設定更新後に再適用する。連続する通知はまとめる。
			// マウス移動やアクティブ状態の通知で再設定し続けない。
			if (!_disposed && _pendingApply == null)
			{
				_pendingApply = _window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
				{
					_pendingApply = null;
					Apply();
				}));
			}
		}

		return IntPtr.Zero;
	}

	private void LogMode(string mode)
	{
		if (_lastMode == mode) return;
		_lastMode = mode;
		Logger.Info($"Taskbar background: {mode}");
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_pendingApply?.Abort();
		if (!_source.IsDisposed) _source.RemoveHook(WndProc);
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct AccentPolicy
	{
		public int State;
		public int Flags;
		public uint GradientColor;
		public int AnimationId;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct WindowCompositionAttributeData
	{
		public int Attribute;
		public IntPtr Data;
		public nuint SizeOfData;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Margins
	{
		public int Left;
		public int Right;
		public int Top;
		public int Bottom;

		public Margins(int value)
		{
			Left = Right = Top = Bottom = value;
		}
	}

	[DllImport("dwmapi.dll", ExactSpelling = true)]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

	[DllImport("dwmapi.dll", ExactSpelling = true)]
	private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

	// 非公開の AccentPolicy を使うため、失敗時は不透明な背景へ戻す。
	[DllImport("user32.dll", ExactSpelling = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
}
