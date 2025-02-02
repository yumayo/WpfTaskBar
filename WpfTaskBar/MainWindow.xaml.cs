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
using System.Windows.Shapes;
using WinFormsTaskBar;
using Window = System.Windows.Window;

namespace WpfApp1;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
	private WindowManager windowManager = new WindowManager();

	private Point _startPoint;
	private IconListBoxItem _draggedItem;

	public MainWindow()
	{
		Console.OutputEncoding = Encoding.UTF8;
		InitializeComponent();

		windowManager.WindowListChanged += WindowManagerOnWindowListChanged;

		windowManager.Start();
	}

	private void WindowManagerOnWindowListChanged(object sender, TaskBarWindowEventArgs e)
	{
		Dispatcher.Invoke(() =>
		{
			if (e.AddedWindows.Count > 0)
			{
				foreach (var window in e.AddedWindows)
				{
					var iconListBoxItem = new IconListBoxItem(window.Title, window.IconFilePath != null ? GetIcon(window.IconFilePath) : null, window.Handle);
					listBox.Items.Add(iconListBoxItem);
				}
			}

			object[] items = new object[listBox.Items.Count];
			listBox.Items.CopyTo(items, 0);

			foreach (var handle in e.RemovedWindowHandles)
			{
				for (int i = items.Length - 1; i >= 0; --i)
				{
					var item = items[i];
					if (item is IconListBoxItem window)
					{
						if (window.Handle == handle)
						{
							listBox.Items.RemoveAt(i);
						}
					}
				}
			}
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
			var icon = System.Drawing.Icon.ExtractAssociatedIcon(iconFilePath);
			var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap((icon.ToBitmap()).GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
			bitmapSource.Freeze();
			icon.Dispose();
			return bitmapSource;
		}
		catch (System.ComponentModel.Win32Exception ex)
		{
			if (ex.Message.Contains("アクセスが拒否されました"))
			{
				return null;
			}
			throw;
		}
	}

	private void ListBox_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		_startPoint = e.GetPosition(null);
		_draggedItem = ((FrameworkElement)e.OriginalSource).DataContext as IconListBoxItem;
	}

	private void ListBox_OnPreviewMouseMove(object sender, MouseEventArgs e)
	{
		if (e.LeftButton == MouseButtonState.Pressed)
		{
			Point position = e.GetPosition(null);
			if (Math.Abs(position.X - _startPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
			    Math.Abs(position.Y - _startPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
			{
				DataObject data = new DataObject(typeof(IconListBoxItem), _draggedItem);
				DragDrop.DoDragDrop(listBox, data, DragDropEffects.Move);
			}
		}
	}

	private void ListBox_OnDrop(object sender, DragEventArgs e)
	{
		if (e.Data.GetDataPresent(typeof(IconListBoxItem)))
		{
			IconListBoxItem droppedData = e.Data.GetData(typeof(IconListBoxItem)) as IconListBoxItem;
			IconListBoxItem target = ((FrameworkElement)e.OriginalSource).DataContext as IconListBoxItem;

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
}