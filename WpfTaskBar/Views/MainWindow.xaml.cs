using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Collections.Specialized;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Text.Json;
using System.IO;
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
	private ChromeTabManager? _tabManager;

	private Point _startPoint;
	private readonly DateTimeItem? _dateTimeItem;
	private bool _dragMode;

	public MainWindow()
	{
		InitializeComponent();

		Logger.Info("MainWindow initialized with WebView2");

		try
		{
			_dateTimeItem = new DateTimeItem();
			_dateTimeItem.Update();

			// 通知リストのイベントハンドラーを設定
			NotificationModel.Notifications.CollectionChanged += OnNotificationsChanged;

			// WebView2の初期化
			InitializeWebView();
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "MainWindow初期化時にエラーが発生しました。");
		}
	}

	private async void InitializeWebView()
	{
		try
		{
			// WebView2環境を初期化
			await webView.EnsureCoreWebView2Async(null);

			// JavaScriptからのメッセージを受信するイベントハンドラを設定
			webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

			// HTMLファイルのパスを取得
			var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web", "index.html");
			var htmlUri = new Uri($"file:///{htmlPath.Replace('\\', '/')}");

			Logger.Info($"Loading HTML from: {htmlUri}");

			// HTMLファイルを読み込み
			webView.CoreWebView2.Navigate(htmlUri.ToString());

			Logger.Info("WebView2初期化完了 - NavigationCompletedイベントを待機中");
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "WebView2初期化時にエラーが発生しました。");
		}
	}

	private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
	{
		try
		{
			string? messageJson = null;
			try
			{
				// WebView2のメッセージを文字列として取得
				messageJson = e.WebMessageAsJson;

				// JSON文字列をパースして実際の文字列メッセージを取得
				if (!string.IsNullOrEmpty(messageJson))
				{
					// JSON形式の場合はパースして文字列部分を取得
					if (messageJson.StartsWith("\"") && messageJson.EndsWith("\""))
					{
						messageJson = JsonSerializer.Deserialize<string>(messageJson);
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "メッセージの取得に失敗しました");
				return;
			}

			Logger.Info($"WebView2からメッセージ受信: {messageJson}");

			if (!string.IsNullOrEmpty(messageJson))
			{
				using var document = JsonDocument.Parse(messageJson);
				var root = document.RootElement;

				if (root.TryGetProperty("type", out var typeElement))
				{
					var messageType = typeElement.GetString();

					switch (messageType)
					{
						case "webview_loaded":
							Logger.Info("WebView2が正常に読み込まれました");
							SendInitialData();
							break;

						case "request_datetime_update":
							SendDateTimeUpdate();
							break;

						// WindowManager関連のNativeCallsを追加
						case "request_window_handles":
							HandleRequestWindowHandles();
							break;

						case "request_foreground_window":
							HandleRequestForegroundWindow();
							break;

						case "request_is_taskbar_window":
							HandleRequestIsTaskBarWindow(root);
							break;

						case "request_window_info":
							HandleRequestWindowInfo(root);
							break;

						case "request_is_window_on_current_virtual_desktop":
							HandleRequestIsWindowOnCurrentVirtualDesktop(root);
							break;

						case "request_process_id":
							HandleRequestProcessId(root);
							break;

						case "request_sort_by_order":
							HandleRequestSortByOrder(root);
							break;

						case "update_application_order":
							HandleUpdateApplicationOrder(root);
							break;

						case "update_window_order":
							HandleUpdateWindowOrder(root);
							break;

						case "task_click":
							HandleTaskClick(root);
							break;

						case "task_middle_click":
							HandleTaskMiddleClick(root);
							break;

						case "task_context_menu":
							HandleTaskContextMenu(root);
							break;

						case "notification_click":
							HandleNotificationClick(root);
							break;

						case "task_reorder":
							HandleTaskReorder(root);
							break;

						case "exit_application":
							Application.Current.Shutdown();
							break;

						case "open_dev_tools":
							HandleOpenDevTools();
							break;

						default:
							Logger.Info($"未知のメッセージタイプ: {messageType}");
							break;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "WebView2メッセージ処理時にエラーが発生しました。");
		}
	}

	private void SendInitialData()
	{
		try
		{
			// 初期化データを送信
			var initData = new
			{
				type = "init",
				message = "タスクバー初期化完了",
				version = "2.0.0",
				timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
			};
			SendMessageToWebView(initData);

			// 現在の時刻情報を送信
			SendDateTimeUpdate();

			// 通知情報を送信
			SendNotificationUpdate();

			Logger.Info("初期データをWebView2に送信しました");
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "初期データ送信時にエラーが発生しました。");
		}
	}

	private void SendDateTimeUpdate()
	{
		try
		{
			if (_dateTimeItem == null) return;

			_dateTimeItem.Update();

			var dateTimeData = new
			{
				type = "datetime_update",
				dateTime = new
				{
					startTime = _dateTimeItem.StartTime,
					endTime = _dateTimeItem.EndTime,
					currentTime = _dateTimeItem.Time,
					currentDate = _dateTimeItem.Date,
					isStartTimeMissing = _dateTimeItem.IsStartTimeMissing,
					isEndTimeMissingAfter19 = _dateTimeItem.IsEndTimeMissingAfter19
				}
			};

			SendMessageToWebView(dateTimeData);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "日時更新送信時にエラーが発生しました。");
		}
	}

	private void SendNotificationUpdate()
	{
		try
		{
			var notificationData = new
			{
				type = "notification_update",
				notifications = NotificationModel.Notifications.Select(n => new
				{
					id = n.Id.ToString(),
					title = n.Title,
					message = n.Message,
					timestamp = n.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss"),
					windowHandle = IntPtr.Zero // 必要に応じて設定
				}).ToArray()
			};

			SendMessageToWebView(notificationData);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "通知更新送信時にエラーが発生しました。");
		}
	}

	private void HandleTaskClick(JsonElement root)
	{
		try
		{
			if (root.TryGetProperty("data", out var dataElement) &&
			    dataElement.TryGetProperty("handle", out var handleElement))
			{
				var handleString = handleElement.GetString();
				if (IntPtr.TryParse(handleString, out var handle))
				{
					// ウィンドウが最小化されているかチェック
					if (NativeMethods.IsIconic(handle))
					{
						// 最小化されたウィンドウを復元してアクティブにする
						NativeMethods.SendMessage(handle, NativeMethods.WM_SYSCOMMAND, (IntPtr)NativeMethods.SC_RESTORE, IntPtr.Zero);
						NativeMethods.SetForegroundWindow(handle);
						Logger.Info($"最小化されたウィンドウを復元しました: {handle}");
					}
					else
					{
						// 現在のフォアグラウンドウィンドウを取得
						var foregroundWindow = NativeMethods.GetForegroundWindow();

						// クリックされたウィンドウが既にアクティブな場合は最小化
						if (handle == foregroundWindow)
						{
							// ウィンドウを最小化
							NativeMethods.SendMessage(handle, NativeMethods.WM_SYSCOMMAND, (IntPtr)NativeMethods.SC_MINIMIZE, IntPtr.Zero);
							Logger.Info($"アクティブなウィンドウを最小化しました: {handle}");
						}
						else
						{
							// ウィンドウをアクティブにする
							NativeMethods.SetForegroundWindow(handle);
							Logger.Info($"ウィンドウをアクティブにしました: {handle}");
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "タスククリック処理時にエラーが発生しました。");
		}
	}

	private void HandleTaskMiddleClick(JsonElement root)
	{
		try
		{
			if (root.TryGetProperty("data", out var dataElement) &&
			    dataElement.TryGetProperty("handle", out var handleElement))
			{
				var handleString = handleElement.GetString();
				if (IntPtr.TryParse(handleString, out var handle))
				{
					// プロセスを終了する
					NativeMethods.PostMessage(handle, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
					Logger.Info($"プロセス終了メッセージを送信: {handle}");
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "タスク中クリック処理時にエラーが発生しました。");
		}
	}

	private void HandleTaskContextMenu(JsonElement root)
	{
		// 今回は基本的なコンテキストメニューのログのみ
		Logger.Info("タスクのコンテキストメニューが要求されました");
	}

	private void HandleNotificationClick(JsonElement root)
	{
		try
		{
			if (root.TryGetProperty("data", out var dataElement) &&
			    dataElement.TryGetProperty("windowHandle", out var handleElement))
			{
				var handleValue = handleElement.GetInt64();
				if (handleValue > 0)
				{
					var handle = new IntPtr(handleValue);
					NativeMethods.SetForegroundWindow(handle);
					Logger.Info($"通知クリックでウィンドウをアクティブにしました: {handle}");
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "通知クリック処理時にエラーが発生しました。");
		}
	}

	private void HandleTaskReorder(JsonElement root)
	{
		try
		{
			if (root.TryGetProperty("data", out var dataElement) &&
			    dataElement.TryGetProperty("newOrder", out var newOrderElement))
			{
				var newOrderHandles = new List<string>();
				foreach (var item in newOrderElement.EnumerateArray())
				{
					var handleString = item.GetString();
					if (!string.IsNullOrEmpty(handleString))
					{
						newOrderHandles.Add(handleString);
					}
				}

				if (newOrderHandles.Count > 0 && _windowManager != null)
				{
					// アプリケーション順序とウィンドウ順序の両方を更新
					var moduleFileNames = new List<string>();
					var processedModules = new HashSet<string>();
					var orderedWindows = new List<(string Handle, string ModuleFileName)>();

					foreach (var handleString in newOrderHandles)
					{
						if (IntPtr.TryParse(handleString, out var handle))
						{
							// WindowManagerのTaskBarItemsから該当するアイテムを検索
							var taskBarItem = _windowManager.TaskBarItems.FirstOrDefault(item => item.Handle == handle);
							if (taskBarItem != null && !string.IsNullOrEmpty(taskBarItem.ModuleFileName))
							{
								// 個別ウィンドウの順序を記録
								orderedWindows.Add((handleString, taskBarItem.ModuleFileName));

								// アプリケーション順序用に、同一ModuleFileNameは一度だけ追加（重複排除）
								if (!processedModules.Contains(taskBarItem.ModuleFileName))
								{
									moduleFileNames.Add(taskBarItem.ModuleFileName);
									processedModules.Add(taskBarItem.ModuleFileName);
								}
							}
						}
					}

					// アプリケーション間の順序を更新
					if (moduleFileNames.Count > 0)
					{
						_windowManager.UpdateApplicationOrder(moduleFileNames);
						Logger.Info($"アプリケーション順序を更新しました: {string.Join(", ", moduleFileNames)}");
					}

					// 個別ウィンドウの順序を更新
					if (orderedWindows.Count > 0)
					{
						_windowManager.UpdateWindowOrder(orderedWindows);
						Logger.Info($"ウィンドウ順序を更新しました: {orderedWindows.Count}件");
					}

					if (moduleFileNames.Count == 0 && orderedWindows.Count == 0)
					{
						Logger.Warning("タスクの順序更新: 更新対象が見つかりませんでした");
					}
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "タスク順序変更処理時にエラーが発生しました。");
		}
	}

	// WindowManager用のNativeCallハンドラー群
	private void HandleRequestWindowHandles()
	{
		try
		{
			var windowHandles = new List<string>();
			NativeMethods.EnumWindows((hwnd, lParam) =>
			{
				windowHandles.Add(hwnd.ToString());
				return true;
			}, 0);

			var response = new
			{
				type = "window_handles_response",
				windowHandles = windowHandles
			};

			SendMessageToWebView(response);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "ウィンドウハンドル取得時にエラーが発生しました。");
		}
	}

	private void HandleRequestForegroundWindow()
	{
		try
		{
			var foregroundWindow = NativeMethods.GetForegroundWindow();
			var response = new
			{
				type = "foreground_window_response",
				foregroundWindow = foregroundWindow.ToString()
			};

			SendMessageToWebView(response);
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "フォアグラウンドウィンドウ取得時にエラーが発生しました。");
		}
	}

	private void HandleRequestIsTaskBarWindow(JsonElement root)
	{
		try
		{
			if (root.TryGetProperty("data", out var dataElement) &&
			    dataElement.TryGetProperty("windowHandle", out var handleElement))
			{
				var handleString = handleElement.GetString();
				if (IntPtr.TryParse(handleString, out var hwnd))
				{
					var isTaskBarWindow = NativeMethodUtility.IsTaskBarWindow(hwnd);

					// 現在の仮想デスクトップにあるウィンドウのみを対象とする
					if (isTaskBarWindow && !VirtualDesktopUtility.IsWindowOnCurrentVirtualDesktop(hwnd))
					{
						isTaskBarWindow = false;
					}

					var response = new
					{
						type = "is_taskbar_window_response",
						windowHandle = handleString,
						isTaskBarWindow = isTaskBarWindow
					};

					SendMessageToWebView(response);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "タスクバーウィンドウ判定時にエラーが発生しました。");
		}
	}

	private void HandleRequestWindowInfo(JsonElement root)
	{
		try
		{
			if (root.TryGetProperty("data", out var dataElement) &&
			    dataElement.TryGetProperty("windowHandle", out var handleElement))
			{
				var handleString = handleElement.GetString();
				if (IntPtr.TryParse(handleString, out var hwnd))
				{
					var processName = UwpUtility.GetProcessName(hwnd) ?? "";
					var title = WindowManager.GetWindowText(hwnd);
					var iconData = GetIconAsBase64(processName);

					Logger.Info($"ウィンドウ情報取得: {title}, プロセス: {processName}, アイコンデータ: {(iconData != null ? iconData.Length.ToString() : "null")}文字");

					var response = new
					{
						type = "window_info_response",
						windowHandle = handleString,
						moduleFileName = processName,
						title = title,
						iconData = iconData
					};

					SendMessageToWebView(response);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "ウィンドウ情報取得時にエラーが発生しました。");
		}
	}

	private string? GetIconAsBase64(string? moduleFileName)
	{
		if (string.IsNullOrEmpty(moduleFileName))
		{
			Logger.Info($"GetIconAsBase64: moduleFileNameが空のため null を返します");
			return null;
		}

		try
		{
			Logger.Info($"GetIconAsBase64: アイコン取得開始 {moduleFileName}");
			var icon = GetIcon(moduleFileName);
			if (icon == null)
			{
				Logger.Info($"GetIconAsBase64: アイコン取得失敗 {moduleFileName}");
				return null;
			}

			// BitmapSourceをBase64に変換
			var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
			encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(icon));

			using var stream = new System.IO.MemoryStream();
			encoder.Save(stream);
			var base64String = Convert.ToBase64String(stream.ToArray());
			Logger.Info($"GetIconAsBase64: Base64変換成功 {moduleFileName} ({base64String.Length}文字)");
			return base64String;
		}
		catch (Exception ex)
		{
			Logger.Error(ex, $"アイコンの変換に失敗しました: {moduleFileName}");
			return null;
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

	private void HandleUpdateApplicationOrder(JsonElement root)
	{
		try
		{
			if (root.TryGetProperty("data", out var dataElement) &&
			    dataElement.TryGetProperty("orderedExecutablePaths", out var pathsElement))
			{
				var orderedPaths = pathsElement.EnumerateArray()
					.Select(item => item.GetString() ?? "")
					.Where(path => !string.IsNullOrEmpty(path))
					.ToList();

				_windowManager?.UpdateApplicationOrder(orderedPaths);
				Logger.Info($"アプリケーション順序を更新しました: {orderedPaths.Count}個");
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "アプリケーション順序更新時にエラーが発生しました。");
		}
	}

	private void HandleRequestIsWindowOnCurrentVirtualDesktop(JsonElement root)
	{
		try
		{
			if (root.TryGetProperty("data", out var dataElement) &&
			    dataElement.TryGetProperty("windowHandle", out var handleElement))
			{
				var handleString = handleElement.GetString();
				if (IntPtr.TryParse(handleString, out var hwnd))
				{
					var isOnCurrentVirtualDesktop = VirtualDesktopUtility.IsWindowOnCurrentVirtualDesktop(hwnd);

					var response = new
					{
						type = "is_window_on_current_virtual_desktop_response",
						windowHandle = handleString,
						isOnCurrentVirtualDesktop = isOnCurrentVirtualDesktop
					};

					SendMessageToWebView(response);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "仮想デスクトップ判定時にエラーが発生しました。");
		}
	}

	private void HandleRequestProcessId(JsonElement root)
	{
		try
		{
			if (root.TryGetProperty("data", out var dataElement) &&
			    dataElement.TryGetProperty("windowHandle", out var handleElement))
			{
				var handleString = handleElement.GetString();
				if (IntPtr.TryParse(handleString, out var hwnd))
				{
					var processId = UwpUtility.GetProcessId(hwnd);

					var response = new
					{
						type = "process_id_response",
						windowHandle = handleString,
						processId = processId.ToString()
					};

					SendMessageToWebView(response);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "プロセスID取得時にエラーが発生しました。");
		}
	}

	private void HandleRequestSortByOrder(JsonElement root)
	{
		try
		{
			if (root.TryGetProperty("data", out var dataElement) &&
			    dataElement.TryGetProperty("items", out var itemsElement))
			{
				var items = new List<TaskBarItem>();
				foreach (var itemElement in itemsElement.EnumerateArray())
				{
					if (itemElement.TryGetProperty("handle", out var handleProp) &&
					    itemElement.TryGetProperty("moduleFileName", out var moduleFileNameProp) &&
					    itemElement.TryGetProperty("title", out var titleProp) &&
					    itemElement.TryGetProperty("isForeground", out var isForegroundProp))
					{
						var handleString = handleProp.GetString() ?? "";
						if (IntPtr.TryParse(handleString, out var handle))
						{
							items.Add(new TaskBarItem
							{
								Handle = handle,
								ModuleFileName = moduleFileNameProp.GetString() ?? "",
								Title = titleProp.GetString() ?? "",
								IsForeground = isForegroundProp.GetBoolean()
							});
						}
					}
				}

				var sortedItems = _windowManager?.SortItemsByOrder(items) ?? items;

				var response = new
				{
					type = "sort_by_order_response",
					sortedItems = sortedItems.Select(item => new
					{
						handle = item.Handle.ToString(),
						moduleFileName = item.ModuleFileName,
						title = item.Title,
						isForeground = item.IsForeground
					}).ToArray()
				};

				SendMessageToWebView(response);
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "ソート処理時にエラーが発生しました。");
		}
	}

	private void HandleUpdateWindowOrder(JsonElement root)
	{
		try
		{
			if (root.TryGetProperty("data", out var dataElement) &&
			    dataElement.TryGetProperty("orderedWindows", out var windowsElement))
			{
				var orderedWindows = new List<(string Handle, string ModuleFileName)>();

				foreach (var windowElement in windowsElement.EnumerateArray())
				{
					if (windowElement.TryGetProperty("handle", out var handleProp) &&
					    windowElement.TryGetProperty("moduleFileName", out var moduleFileNameProp))
					{
						var handle = handleProp.GetString() ?? "";
						var moduleFileName = moduleFileNameProp.GetString() ?? "";
						orderedWindows.Add((handle, moduleFileName));
					}
				}

				_windowManager?.UpdateWindowOrder(orderedWindows);
				Logger.Info($"ウィンドウ順序を更新しました: {orderedWindows.Count}個");
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "ウィンドウ順序更新時にエラーが発生しました。");
		}
	}

	private void SendMessageToWebView(object data)
	{
		try
		{
			if (webView?.CoreWebView2 != null)
			{
				var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
				{
					PropertyNamingPolicy = null, // CamelCaseを削除
					WriteIndented = false
				});

				// Logger.Info($"WebView2にメッセージ送信準備: {json}");
				webView.CoreWebView2.PostWebMessageAsString(json);
				// Logger.Info("WebView2にメッセージ送信完了");
			}
			else
			{
				Logger.Error(null, "WebView2が初期化されていません。メッセージ送信をスキップします。");
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "WebView2へのメッセージ送信時にエラーが発生しました。");
		}
	}

	private void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
	{
		try
		{
			if (e.IsSuccess)
			{
				Logger.Info("WebView2のナビゲーションが完了しました。");

				// 少し待ってからJavaScriptが準備完了するのを待つ
				Task.Delay(500).ContinueWith(_ =>
				{
					Dispatcher.Invoke(() =>
					{
						// 初期データを送信
						var initData = new
						{
							type = "init",
							message = "C# MainWindowからの初期化メッセージ",
							version = "1.0.0",
							timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
						};
						SendMessageToWebView(initData);
						Logger.Info("初期データ送信完了");
					});
				});
			}
			else
			{
				Logger.Error(null, $"WebView2のナビゲーションに失敗しました: {e.WebErrorStatus}");
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "WebView2ナビゲーション完了処理時にエラーが発生しました。");
		}
	}

	public void InitializeServices()
	{
		if (App.ServiceProvider == null)
		{
			Logger.Info("ServiceProvider not yet available, services will be initialized later");
			return;
		}
		
		try
		{
			Logger.Info("Starting MainWindow service initialization");

			// DIコンテナからサービスを取得
			_windowManager = App.ServiceProvider.GetRequiredService<WindowManager>();
			_webSocketHandler = App.ServiceProvider.GetRequiredService<WebSocketHandler>();
			_tabManager = App.ServiceProvider.GetRequiredService<ChromeTabManager>();

			Logger.Info("Services obtained from DI container");

			// WindowManagerの初期化（Web版では自動起動しない）
			// C#のWindowManagerはNativeCallsのみで、WebView2のJavaScript側WindowManagerが主導権を持つ
			Logger.Info("WindowManager initialized for WebView2 native calls only");

			Logger.Info("MainWindow services initialized successfully from DI container");
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Failed to initialize services from DI container");
		}
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

				// WebView2に通知更新を送信
				SendNotificationUpdate();
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
		try
		{
			// WindowManagerからタスクバー情報を取得
			var currentTasks = GetCurrentTaskBarItems(e);

			// WebView2にタスクバー更新を送信
			var taskBarData = new
			{
				type = "taskbar_update",
				tasks = currentTasks
			};

			SendMessageToWebView(taskBarData);
			// Logger.Info($"タスクバー更新をWebView2に送信: {currentTasks.Count}件");

			// 時刻情報も更新
			_dateTimeItem?.Update();
			SendDateTimeUpdate();
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "タスクバーリスト更新時にエラーが発生しました。");
		}
	}

	private List<object> GetCurrentTaskBarItems(TaskBarWindowEventArgs e)
	{
		var tasks = new List<object>();

		if (_windowManager != null)
		{
			// WindowManagerから現在の全てのタスクリストを取得（重複を避けるため）
			var allItems = new List<IconListBoxItem>();

			// WindowManagerのTaskBarItemsから全ての現在のアイテムを取得
			foreach (var taskBarItem in _windowManager.TaskBarItems)
			{
				allItems.Add(new IconListBoxItem
				{
					Handle = taskBarItem.Handle,
					Icon = taskBarItem.ModuleFileName != null ? GetIcon(taskBarItem.ModuleFileName) : null,
					Text = taskBarItem.Title,
					IsForeground = taskBarItem.IsForeground,
					ModuleFileName = taskBarItem.ModuleFileName,
				});
			}

			// ソート
			var sortedItems = _windowManager.SortItemsByOrder(allItems);

			// WebView2用の形式に変換
			foreach (var item in sortedItems)
			{
				var iconData = GetIconAsBase64(item.ModuleFileName);
				tasks.Add(new
				{
					handle = item.Handle.ToString(),
					text = item.Text,
					isForeground = item.IsForeground,
					moduleFileName = item.ModuleFileName,
					iconData = iconData
				});
			}
		}

		return tasks;
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
		// WebView2版では通知管理も変更
		_windowManager?.Stop();
		Logger.Close();
	}

	private void OnNotificationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		Dispatcher.Invoke(() =>
		{
			try
			{
				// WebView2版では通知更新を送信
				SendNotificationUpdate();
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "通知表示の更新中にエラーが発生しました。");
			}
		});
	}

	// WebView2版では従来のListBoxイベントハンドラーは不要
	// JavaScript側でドラッグ&ドロップを実装

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
		// まずサービスを初期化
		Logger.Info("MainWindow loaded, initializing services...");
		InitializeServices();

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

	// WebView2版ではマウスイベントもWebView2経由で処理

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

	private void HandleOpenDevTools()
	{
		try
		{
			if (webView?.CoreWebView2 != null)
			{
				webView.CoreWebView2.OpenDevToolsWindow();
				Logger.Info("開発者ツールを開きました");
			}
			else
			{
				Logger.Warning("WebView2が初期化されていないため、開発者ツールを開けませんでした");
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "開発者ツールを開く際にエラーが発生しました");
		}
	}

	private void SaveCurrentOrder()
	{
		// WebView2版では順序保存もWebView2経由で処理
		Logger.Info("順序保存処理 - WebView2版では未実装");
	}

	// WebView2版では通知クリックもWebView2経由で処理
}