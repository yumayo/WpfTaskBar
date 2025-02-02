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
}