using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Collections.Specialized;
using WpfTaskBar.Rest.Models;
using Microsoft.Extensions.DependencyInjection;
using Point = System.Windows.Point;
using Window = System.Windows.Window;

namespace WpfTaskBar;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
	private WindowManager? _windowManager;
	private WebSocketHandler? _webSocketHandler;
	private TabManager? _tabManager;

	private Point _startPoint;
	private IconListBoxItem? _draggedItem;
	private readonly DateTimeItem _dateTimeItem;
	private bool _dragMode;

	public MainWindow()
	{
		InitializeComponent();

		Logger.Setup();

		try
		{
			_dateTimeItem = new DateTimeItem();
			_dateTimeItem.Update();

			stackPanelTime.DataContext = _dateTimeItem;

			// 通知リストをバインド
			notificationListBox.ItemsSource = NotificationModel.Notifications;
			NotificationModel.Notifications.CollectionChanged += OnNotificationsChanged;
			UpdateNotificationVisibility();

			// DI初期化はApp.xaml.csから実行される
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "MainWindow初期化時にエラーが発生しました。");
		}
	}

	public void InitializeServices()
	{
		if (App.ServiceProvider == null)
		{
			Logger.Info("ServiceProvider not yet available, services will be initialized later");
			return;
		}
		
		// DIコンテナからサービスを取得
		_windowManager = App.ServiceProvider.GetRequiredService<WindowManager>();
		_webSocketHandler = App.ServiceProvider.GetRequiredService<WebSocketHandler>();
		_tabManager = App.ServiceProvider.GetRequiredService<TabManager>();
		
		// WindowManagerの初期化
		_windowManager.WindowListChanged += WindowManagerOnWindowListChanged;
		_windowManager.Start();
		
		Logger.Info("MainWindow services initialized from DI container");
	}

	public void ShowNotification(NotificationData notification)
	{
		try
		{
			var notificationItem = new NotificationItem
			{
				Id = Guid.NewGuid(),
				Title = notification.Title,
				Message = notification.Message,
				Timestamp = DateTime.Now
			};

			// 通知をUIに追加
			Application.Current.Dispatcher.Invoke(() =>
			{
				NotificationModel.Notifications.Insert(0, notificationItem);
				
				// 500件を超える場合は古いものを削除
				while (NotificationModel.Notifications.Count > 500)
				{
					NotificationModel.Notifications.RemoveAt(NotificationModel.Notifications.Count - 1);
				}
			});

			// 通知とタブIDの関連付けを保存
			_tabManager?.AssociateNotificationWithTab(notificationItem.Id.ToString(), notification);

			Logger.Info($"Notification added: {notification.Title}");
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "通知表示時にエラーが発生しました。");
		}
	}

	private void WindowManagerOnWindowListChanged(object sender, TaskBarWindowEventArgs e)
	{
		Dispatcher.Invoke(() =>
		{
			try
			{
				UpdateTaskBarList(e);
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "タスクバーの更新中にエラーが発生しました。");
			}
		});
	}

	private void UpdateTaskBarList(TaskBarWindowEventArgs e)
	{
		if (e.AddedTaskBarItems.Count > 0)
		{
			var newItems = new List<IconListBoxItem>();
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

				newItems.Add(newIconListBoxItem);
			}

			var currentItems = listBox.Items.Cast<IconListBoxItem>().ToList();
			var allItems = new List<IconListBoxItem>(currentItems);
			allItems.AddRange(newItems);

			var sortedItems = _windowManager?.SortItemsByOrder(allItems) ?? allItems;

			listBox.Items.Clear();
			foreach (var item in sortedItems)
			{
				listBox.Items.Add(item);
			}
		}

		for (int i = listBox.Items.Count - 1; i >= 0; --i)
		{
			var item = listBox.Items[i];
			if (item is IconListBoxItem iconListBoxItem)
			{
				foreach (var taskBarItem in e.RemovedTaskBarItemHandles)
				{
					if (iconListBoxItem.Handle == taskBarItem.Handle)
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

		RearrangeListBoxItems(e.UpdateTaskBarItems);
		_dateTimeItem.Update();
	}

	private void MainWindow_OnClosed(object? sender, EventArgs e)
	{
		try
		{
			HandleMainWindowClosed(sender, e);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "ウィンドウクローズ時にエラーが発生しました。");
		}
	}

	private void HandleMainWindowClosed(object? sender, EventArgs e)
	{
		NotificationModel.Notifications.CollectionChanged -= OnNotificationsChanged;
		_windowManager?.Stop();
		Logger.Close();
	}

	private void OnNotificationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		Dispatcher.Invoke(() =>
		{
			try
			{
				UpdateNotificationVisibility();
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "通知表示の更新中にエラーが発生しました。");
			}
		});
	}

	private void UpdateNotificationVisibility()
	{
		if (NotificationModel.Notifications.Count > 0)
		{
			notificationListBox.Visibility = Visibility.Visible;
		}
		else
		{
			notificationListBox.Visibility = Visibility.Collapsed;
		}
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
		using (var bitmap = icon.ToBitmap())
		{
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

	private void ListBox_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		try
		{
			HandlePreviewMouseLeftButtonDown(sender, e);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "ListBox_OnPreviewMouseLeftButtonDown時にエラーが発生しました。");
		}
	}

	private void HandlePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		_startPoint = e.GetPosition(null);
		_draggedItem = ((FrameworkElement)e.OriginalSource).DataContext as IconListBoxItem;
		_dragMode = false;
	}

	private void ListBox_OnPreviewMouseMove(object sender, MouseEventArgs e)
	{
		try
		{
			HandlePreviewMouseMove(sender, e);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "ListBox_OnPreviewMouseMove時にエラーが発生しました。");
		}
	}

	private void HandlePreviewMouseMove(object sender, MouseEventArgs e)
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
		try
		{
			HandleDrop(sender, e);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "ドロップ中にエラーが発生しました。");
		}
		finally
		{
			_dragMode = false;
			_draggedItem = null;
		}
	}

	private void HandleDrop(object sender, DragEventArgs e)
	{
		_dragMode = false;
		_draggedItem = null;
		
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

			SaveCurrentOrder();
		}
	}

	private void Window_Loaded(object sender, RoutedEventArgs e)
	{
		try
		{
			HandleWindowLoaded(sender, e);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Window_Loaded時にエラーが発生しました。");
		}
	}

	private void HandleWindowLoaded(object sender, RoutedEventArgs e)
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
		try
		{
			HandleMouseLeftButtonUp(sender, e);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "ListBox_OnMouseLeftButtonUp時にエラーが発生しました。");
		}
	}

	private void HandleMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (!_dragMode)
		{
			var target = ((FrameworkElement)e.OriginalSource).DataContext as IconListBoxItem;
			if (target != null)
			{
				if (NativeMethods.IsIconic(target.Handle))
				{
					NativeMethods.PostMessage(target.Handle, NativeMethods.WM_SYSCOMMAND, new IntPtr(NativeMethods.SC_RESTORE), IntPtr.Zero);
					NativeMethods.SetForegroundWindow(target.Handle);
				}
				else
				{
					if (NativeMethods.GetForegroundWindow() == target.Handle)
					{
						NativeMethods.PostMessage(target.Handle, NativeMethods.WM_SYSCOMMAND, new IntPtr(NativeMethods.SC_MINIMIZE), IntPtr.Zero);
					}
					else
					{
						NativeMethods.SetForegroundWindow(target.Handle);
					}
				}
			}
		}

		_draggedItem = null;
		_dragMode = false;
	}

	private void ListBox_OnMouseDown(object sender, MouseButtonEventArgs e)
	{
		try
		{
			HandleMouseDown(sender, e);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "ListBox_OnMouseDown時にエラーが発生しました。");
		}
	}

	private void HandleMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Middle && e.ButtonState == MouseButtonState.Pressed)
		{
			if (((FrameworkElement)e.OriginalSource).DataContext is IconListBoxItem removeItem)
			{
				if (_windowManager?.CountBySameProcess(removeItem.Handle) > 1)
				{
					NativeMethods.PostMessage(removeItem.Handle, NativeMethods.WM_SYSCOMMAND, new IntPtr(NativeMethods.SC_CLOSE), IntPtr.Zero);
				}
				else
				{
					NativeMethods.PostMessage(removeItem.Handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
				}
			}
		}
	}

	private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			HandleExitMenuItemClick(sender, e);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "ExitMenuItem_Click時にエラーが発生しました。");
		}
	}

	private void HandleExitMenuItemClick(object sender, RoutedEventArgs e)
	{
		Application.Current.Shutdown();
	}

	private void SaveCurrentOrder()
	{
		try
		{
			var orderedPaths = listBox.Items
				.OfType<IconListBoxItem>()
				.Where(item => !string.IsNullOrEmpty(item.ModuleFileName))
				.Select(item => item.ModuleFileName!)
				.Distinct()
				.ToList();

			_windowManager?.UpdateApplicationOrder(orderedPaths);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "並び替え順序の保存中にエラーが発生しました。");
		}
	}

	private void RearrangeListBoxItems(List<TaskBarItem> sortedTaskBarItems)
	{
		try
		{
			var currentItems = listBox.Items.Cast<IconListBoxItem>().ToList();
			var sortedHandles = sortedTaskBarItems.Select(item => item.Handle).ToHashSet();
			
			var orderedItems = new List<IconListBoxItem>();
			var addedItems = new List<IconListBoxItem>();
			
			foreach (var currentItem in currentItems)
			{
				if (sortedHandles.Contains(currentItem.Handle))
				{
					orderedItems.Add(currentItem);
				}
				else
				{
					addedItems.Add(currentItem);
				}
			}
			
			orderedItems.AddRange(addedItems);
			
			bool needsReorder = false;
			if (currentItems.Count != orderedItems.Count)
			{
				needsReorder = true;
			}
			else
			{
				for (int i = 0; i < currentItems.Count; i++)
				{
					if (currentItems[i].Handle != orderedItems[i].Handle)
					{
						needsReorder = true;
						break;
					}
				}
			}
			
			if (needsReorder)
			{
				listBox.Items.Clear();
				foreach (var item in orderedItems)
				{
					listBox.Items.Add(item);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "ListBox項目の並び替え中にエラーが発生しました。");
		}
	}

	private void NotificationListBox_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		try
		{
			HandleNotificationClick(sender, e);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "NotificationListBox_OnMouseLeftButtonUp時にエラーが発生しました。");
		}
	}

	private void HandleNotificationClick(object sender, MouseButtonEventArgs e)
	{
		if (((FrameworkElement)e.OriginalSource).DataContext is NotificationItem clickedNotification)
		{
			var notificationData = _tabManager?.GetNotificationData(clickedNotification.Id.ToString());
			if (notificationData != null && _webSocketHandler != null)
			{
				Task.Run(async () =>
				{
					await _webSocketHandler.FocusTab(notificationData.TabId, notificationData.WindowId);
					Logger.Info($"Focus tab requested: TabId={notificationData.TabId}, WindowId={notificationData.WindowId}");
				});

				// 通知を削除
				Dispatcher.Invoke(() =>
				{
					NotificationModel.Notifications.Remove(clickedNotification);
					_tabManager?.RemoveNotificationAssociation(clickedNotification.Id.ToString());
				});
			}
		}
	}
}