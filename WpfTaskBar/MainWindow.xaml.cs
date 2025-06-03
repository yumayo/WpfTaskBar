using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Point = System.Windows.Point;
using Window = System.Windows.Window;

namespace WpfTaskBar;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
	private WindowManager windowManager = new WindowManager();

	private Point _startPoint;
	private IconListBoxItem? _draggedItem;
	private DateTimeItem _dateTimeItem;
	private bool _dragMode;

	public MainWindow()
	{
		// Console.OutputEncoding = Encoding.UTF8;
		InitializeComponent();

		_dateTimeItem = new DateTimeItem();
		_dateTimeItem.Update();

		stackPanelTime.DataContext = _dateTimeItem;

		windowManager.WindowListChanged += WindowManagerOnWindowListChanged;

		windowManager.Start();
	}

	private void WindowManagerOnWindowListChanged(object sender, TaskBarWindowEventArgs e)
	{
		Dispatcher.Invoke(() =>
		{
			if (e.AddedTaskBarItems.Count > 0)
			{
				foreach (var taskBarItem in e.AddedTaskBarItems)
				{
					var newIconListBoxItem = new IconListBoxItem
					{
						Handle = taskBarItem.Handle,
						Icon = taskBarItem.ModuleFileName != null ? GetIcon(taskBarItem.ModuleFileName) : null,
						Text = taskBarItem.Title,
						IsForeground = taskBarItem.IsForeground,
						ModuleFileName = taskBarItem.ModuleFileName,
					};

					int i;
					for (i = listBox.Items.Count - 1; i >= 0; --i)
					{
						if (listBox.Items[i] is IconListBoxItem iconListBoxItem)
						{
							if (iconListBoxItem.ModuleFileName == newIconListBoxItem.ModuleFileName)
							{
								listBox.Items.Insert(i + 1, newIconListBoxItem);
								break;
							}
						}
					}
					if (i == -1)
					{
						listBox.Items.Add(newIconListBoxItem);
					}
				}
			}

			for (int i = listBox.Items.Count - 1; i >= 0; --i)
			{
				var item = listBox.Items[i];
				if (item is IconListBoxItem iconListBoxItem)
				{
					foreach (var handle in e.RemovedWindowHandles)
					{
						if (iconListBoxItem.Handle == handle)
						{
							listBox.Items.RemoveAt(i);
							break;
						}
					}
				}
			}

			foreach (var updateTaskBarItem in e.UpdateTaskBarItems)
			{
				foreach (var item in listBox.Items)
				{
					if (item is IconListBoxItem iconListBoxItem)
					{
						if (iconListBoxItem.Handle == updateTaskBarItem.Handle)
						{
							if (iconListBoxItem.ModuleFileName != updateTaskBarItem.ModuleFileName)
							{
								Console.WriteLine($"Changed {iconListBoxItem.ModuleFileName} to {updateTaskBarItem.ModuleFileName}");
								iconListBoxItem.Icon = updateTaskBarItem.ModuleFileName != null ? GetIcon(updateTaskBarItem.ModuleFileName) : null;
								iconListBoxItem.ModuleFileName = updateTaskBarItem.ModuleFileName;
							}
							iconListBoxItem.Text = updateTaskBarItem.Title;
							iconListBoxItem.IsForeground = updateTaskBarItem.IsForeground;
							break;
						}
					}
				}
			}

			_dateTimeItem.Update();
		});
	}

	private void MainWindow_OnClosed(object? sender, EventArgs e)
	{
		windowManager.Stop();
	}

	public static BitmapSource? GetIcon(string iconFilePath)
	{
		try
		{
			System.Drawing.Icon? icon;
			if (iconFilePath.ToUpper().EndsWith("EXE"))
			{
				icon = System.Drawing.Icon.ExtractAssociatedIcon(iconFilePath);
			}
			else
			{
				icon = IconUtility.ConvertPngToIcon(iconFilePath);
			}
			return GetIcon(icon);
		}
		catch (System.ComponentModel.Win32Exception ex)
		{
			if (ex.Message.Contains("アクセスが拒否されました"))
			{
				return null;
			}
			throw;
		}
		catch (Exception e)
		{
			Console.WriteLine(e);
			return null;
		}
	}

	public static BitmapSource? GetIcon(System.Drawing.Icon icon)
	{
		var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap((icon.ToBitmap()).GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
		bitmapSource.Freeze();
		icon.Dispose();
		return bitmapSource;
	}

	private void ListBox_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		_startPoint = e.GetPosition(null);
		_draggedItem = ((FrameworkElement)e.OriginalSource).DataContext as IconListBoxItem;
		_dragMode = false;
	}

	private void ListBox_OnPreviewMouseMove(object sender, MouseEventArgs e)
	{
		if (_draggedItem == null)
		{
			return;
		}

		if (e.LeftButton == MouseButtonState.Pressed)
		{
			Point position = e.GetPosition(null);
			if (Math.Abs(position.X - _startPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
			    Math.Abs(position.Y - _startPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
			{
				_dragMode = true;
				DataObject data = new DataObject(typeof(IconListBoxItem), _draggedItem);
				DragDrop.DoDragDrop(listBox, data, DragDropEffects.Move);
			}
		}
	}

	private void ListBox_OnDrop(object sender, DragEventArgs e)
	{
		if (e.Data.GetDataPresent(typeof(IconListBoxItem)))
		{
			IconListBoxItem? droppedData = e.Data.GetData(typeof(IconListBoxItem)) as IconListBoxItem;
			IconListBoxItem? target = ((FrameworkElement)e.OriginalSource).DataContext as IconListBoxItem;

			if (target == null)
			{
				Point position = e.GetPosition(listBox);
				if (listBox.Items.Count > 0)
				{
					if (position.Y > _startPoint.Y)
					{
						target = listBox.Items[^1] as IconListBoxItem;
					}
					else
					{
						target = listBox.Items[0] as IconListBoxItem;
					}
				}
			}

			if (target == null)
			{
				return;
			}

			if (droppedData == null)
			{
				return;
			}

			int removedIdx = listBox.Items.IndexOf(droppedData);
			int targetIdx = listBox.Items.IndexOf(target);

			if (removedIdx < targetIdx)
			{
				listBox.Items.Insert(targetIdx + 1, droppedData);
				listBox.Items.RemoveAt(removedIdx);
			}
			else
			{
				int remIdx = removedIdx + 1;
				if (listBox.Items.Count + 1 > remIdx)
				{
					listBox.Items.Insert(targetIdx, droppedData);
					listBox.Items.RemoveAt(remIdx);
				}
			}
		}
	}

	private void Window_Loaded(object sender, RoutedEventArgs e)
	{
		int height = (int)SystemParameters.PrimaryScreenHeight;
		int width = (int)SystemParameters.PrimaryScreenWidth;

		int myTaskBarWidth = 400;

		IntPtr handle = new WindowInteropHelper(this).Handle;

		// 登録領域から外されないように属性を変更する
		ulong style = NativeMethods.GetWindowLongA(handle, NativeMethods.GWL_EXSTYLE);
		style ^= NativeMethods.WS_EX_APPWINDOW;
		style |= NativeMethods.WS_EX_TOOLWINDOW;
		style |= NativeMethods.WS_EX_NOACTIVATE;
		NativeMethods.SetWindowLongA(handle, NativeMethods.GWL_EXSTYLE, style);

		// タスクバーは表示しないほうが分かりやすそうなので高さ0にしておきます。
		var taskBarHeight = NativeMethodUtility.GetTaskbarHeight();
		taskBarHeight = 0;

		NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST, 0, 0, myTaskBarWidth, (int)(height * NativeMethodUtility.GetPixelsPerDpi() - taskBarHeight), NativeMethods.SWP_SHOWWINDOW);

		// AppBarの登録
		NativeMethods.APPBARDATA barData = new NativeMethods.APPBARDATA();
		barData.cbSize = Marshal.SizeOf(barData);
		barData.hWnd = handle;
		NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref barData);

		// 左端に登録する
		barData.uEdge = NativeMethods.ABE_LEFT;
		barData.rc.Top = 0;
		barData.rc.Left = 0;
		barData.rc.Right = myTaskBarWidth;
		barData.rc.Bottom = (int)SystemParameters.PrimaryScreenHeight;

		NativeMethods.GetWindowRect(handle, out barData.rc);
		NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref barData);
		NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref barData);
	}

	private void ListBox_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (!_dragMode)
		{
			var target = ((FrameworkElement)e.OriginalSource).DataContext as IconListBoxItem;
			if (target != null)
			{
				if (NativeMethods.IsIconic(target.Handle))
				{
					NativeMethods.SendMessage(target.Handle, NativeMethods.WM_SYSCOMMAND, new IntPtr(NativeMethods.SC_RESTORE), IntPtr.Zero);
					NativeMethods.SetForegroundWindow(target.Handle);
				}
				else
				{
					if (NativeMethods.GetForegroundWindow() == target.Handle)
					{
						NativeMethods.SendMessage(target.Handle, NativeMethods.WM_SYSCOMMAND, new IntPtr(NativeMethods.SC_MINIMIZE), IntPtr.Zero);
					}
					else
					{
						NativeMethods.SetForegroundWindow(target.Handle);
					}
				}
			}
		}
	}

	private void ListBox_OnMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Middle && e.ButtonState == MouseButtonState.Pressed)
		{
			if (((FrameworkElement)e.OriginalSource).DataContext is IconListBoxItem removeItem)
			{
				NativeMethods.SendMessage(removeItem.Handle, NativeMethods.WM_SYSCOMMAND, new IntPtr(NativeMethods.SC_CLOSE), IntPtr.Zero);
			}
		}
	}

	private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
	{
		Application.Current.Shutdown();
	}
}