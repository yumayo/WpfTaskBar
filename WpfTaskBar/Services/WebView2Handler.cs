using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

	namespace WpfTaskBar
	{
		public class WebView2Handler
		{
			private const string WebViewDevServerUrl = "http://localhost:13001";
			private static readonly TimeSpan SlowMessageThreshold = TimeSpan.FromMilliseconds(80);
			private static readonly TimeSpan QueuedMessageThreshold = TimeSpan.FromMilliseconds(80);
			private static long _nextWebMessageId;
			private static int _activeBackgroundMessages;
			private readonly WindowIconService _windowIconService;
			private Dispatcher? _dispatcher;
			private WebView2? _webView2;

			public WebView2Handler(WindowIconService windowIconService)
			{
				_windowIconService = windowIconService;
			}

		public async Task InitializeAsync(Dispatcher dispatcher, WebView2 webView2)
		{
			_dispatcher = dispatcher;
			_webView2 = webView2;

			try
			{
				// WebView2環境を初期化
				await _webView2.EnsureCoreWebView2Async(null);

				// JavaScriptからのメッセージを受信するイベントハンドラを設定
				_webView2.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

#if DEBUG
				var devUrl = $"{WebViewDevServerUrl}/?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
				_webView2.CoreWebView2.Navigate(devUrl);
#else
				// HTMLファイルのパスを取得
				var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebView", "index.html");
				var htmlUri = new Uri($"file:///{htmlPath.Replace('\\', '/')}");
				Logger.Info($"Loading HTML from: {htmlUri}");

				// HTMLファイルを読み込み
				_webView2.CoreWebView2.Navigate(htmlUri.ToString());
#endif

				Logger.Info("WebView2初期化完了 - NavigationCompletedイベントを待機中");
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "WebView2初期化時にエラーが発生しました。");
			}
		}

		public void SendMessageToWebView(object data)
		{
			var dispatcher = _dispatcher;
			if (dispatcher == null)
			{
				return;
			}

			void Send()
			{
				if (_webView2?.CoreWebView2 != null)
				{
					var options = new JsonSerializerOptions
					{
						PropertyNamingPolicy = null, // CamelCaseを削除
						WriteIndented = false
					};

					var json = JsonSerializer.Serialize(data, options);

					_webView2.CoreWebView2.PostWebMessageAsString(json);
				}
				else
				{
					Logger.Error(null, "WebView2が初期化されていません。メッセージ送信をスキップします。");
				}
			}

			if (dispatcher.CheckAccess())
			{
				Send();
			}
			else
			{
				dispatcher.BeginInvoke((Action)Send, DispatcherPriority.Send);
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

				if (!string.IsNullOrEmpty(messageJson))
				{
					using var document = JsonDocument.Parse(messageJson);
					var root = document.RootElement;

					if (root.TryGetProperty("type", out var typeElement))
					{
						var messageType = typeElement.GetString();
						DispatchWebMessage(messageType, root);
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "WebView2メッセージ処理時にエラーが発生しました。");
			}
		}

		private void DispatchWebMessage(string? messageType, JsonElement root)
		{
			if (string.IsNullOrEmpty(messageType))
			{
				return;
			}

			var queuedDuration = GetClientQueuedDuration(root);
			if (ShouldRunInBackground(messageType))
			{
				var messageId = Interlocked.Increment(ref _nextWebMessageId);
				var rootClone = root.Clone();
				var activeAtStart = Interlocked.Increment(ref _activeBackgroundMessages);

				_ = Task.Run(() =>
				{
					var stopwatch = Stopwatch.StartNew();
					try
					{
						ProcessWebMessage(messageType, rootClone);
					}
					catch (Exception ex)
					{
						Logger.Error(ex, $"WebView2バックグラウンドメッセージ処理時にエラーが発生しました。 type={messageType} id={messageId}");
					}
					finally
					{
						stopwatch.Stop();
						var activeAfterFinish = Interlocked.Decrement(ref _activeBackgroundMessages);
						LogWebMessageTiming(messageType, messageId, stopwatch.Elapsed, queuedDuration, activeAtStart, activeAfterFinish);
					}
				});

				return;
			}

			var inlineId = Interlocked.Increment(ref _nextWebMessageId);
			var inlineStopwatch = Stopwatch.StartNew();
			ProcessWebMessage(messageType, root);
			inlineStopwatch.Stop();
			LogWebMessageTiming(messageType, inlineId, inlineStopwatch.Elapsed, queuedDuration, 0, _activeBackgroundMessages);
		}

		private static bool ShouldRunInBackground(string messageType)
		{
			return messageType is
				"request_window_handles" or
				"request_window_snapshot" or
				"request_taskbar_items" or
				"request_is_taskbar_window" or
				"request_window_info" or
				"request_is_window_on_current_virtual_desktop" or
				"file_write_request" or
				"file_read_request";
		}

		private void ProcessWebMessage(string messageType, JsonElement root)
		{
			switch (messageType)
			{
				case "request_window_handles":
					HandleRequestWindowHandles();
					break;

				case "request_foreground_window":
					HandleRequestForegroundWindow();
					break;

				case "request_window_snapshot":
					HandleRequestWindowSnapshot();
					break;

				case "request_taskbar_items":
					HandleRequestTaskBarItems(root);
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

				case "task_middle_click":
					HandleTaskMiddleClick(root);
					break;

				case "task_click":
					HandleTaskClick(root);
					break;

				case "request_is_window_minimized":
					HandleRequestIsWindowMinimized(root);
					break;

				case "request_next_window_to_activate":
					HandleRequestNextWindowToActivate(root);
					break;

				case "restore_window":
					HandleRestoreWindow(root);
					break;

				case "minimize_window":
					HandleMinimizeWindow(root);
					break;

				case "activate_window":
					HandleActivateWindow(root);
					break;

				case "file_write_request":
					HandleFileWriteRequest(root);
					break;

				case "file_read_request":
					HandleFileReadRequest(root);
					break;

				case "exit_application":
					Application.Current.Shutdown();
					break;

				case "open_task_manager":
					HandleOpenTaskManager();
					break;

				case "open_app_data_folder":
					HandleOpenAppDataFolder();
					break;

				case "open_dev_tools":
					HandleOpenDevTools();
					break;

				case "request_time_record_status":
					HandleRequestTimeRecordStatus();
					break;

				default:
					Logger.Info($"未知のメッセージタイプ: {messageType}");
					break;
			}
		}

		private static TimeSpan? GetClientQueuedDuration(JsonElement root)
		{
			if (!root.TryGetProperty("timestamp", out var timestampElement) ||
			    timestampElement.ValueKind != JsonValueKind.String)
			{
				return null;
			}

			var timestamp = timestampElement.GetString();
			if (string.IsNullOrEmpty(timestamp) ||
			    !DateTimeOffset.TryParse(timestamp, out var sentAt))
			{
				return null;
			}

			var queuedDuration = DateTimeOffset.UtcNow - sentAt.ToUniversalTime();
			return queuedDuration < TimeSpan.Zero ? TimeSpan.Zero : queuedDuration;
		}

		private static void LogWebMessageTiming(
			string messageType,
			long messageId,
			TimeSpan processingDuration,
			TimeSpan? queuedDuration,
			int activeAtStart,
			int activeAfterFinish)
		{
			var isSlowProcessing = processingDuration >= SlowMessageThreshold;
			var isQueued = queuedDuration >= QueuedMessageThreshold;
			if (!isSlowProcessing && !isQueued)
			{
				return;
			}

			var queuedText = queuedDuration.HasValue ? queuedDuration.Value.TotalMilliseconds.ToString("F1") : "n/a";
			Logger.Warning(
				$"WebView2 message timing type={messageType} id={messageId} queuedMs={queuedText} processingMs={processingDuration.TotalMilliseconds:F1} activeAtStart={activeAtStart} activeAfterFinish={activeAfterFinish}");
		}

		private void HandleTaskMiddleClick(JsonElement root)
		{
			try
			{
				if (root.TryGetProperty("data", out var dataElement) &&
				    dataElement.TryGetProperty("handle", out var handleElement))
				{
					var handle = IntPtr.Parse(handleElement.GetInt32().ToString());

					// handleのプロセスIDを取得
					NativeMethods.GetWindowThreadProcessId(handle, out var targetProcessId);

					// 全ウィンドウのhandleを一旦配列に格納
					var allHandles = new List<IntPtr>();
					NativeMethods.EnumWindows(
						(hwnd, lParam) =>
						{
							allHandles.Add(hwnd);
							return true;
						},
						0
					);

					// タスクバーに表示されているウィンドウのみ抽出
					var taskbarHandles = allHandles.Where(NativeMethodUtility.IsTaskBarWindow).ToList();

					var sameProcessCount = taskbarHandles.Count(h =>
					{
						NativeMethods.GetWindowThreadProcessId(h, out var processId);
						return processId == targetProcessId;
					});

					if (sameProcessCount > 1)
					{
						NativeMethods.PostMessage(handle, NativeMethods.WM_SYSCOMMAND, new IntPtr(NativeMethods.SC_CLOSE), IntPtr.Zero);
					}
					else
					{
						// プロセスを終了する
						NativeMethods.PostMessage(handle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
						Logger.Info($"プロセス終了メッセージを送信: {handle}");
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "タスク中クリック処理時にエラーが発生しました。");
			}
		}

		private void HandleTaskClick(JsonElement root)
		{
			try
			{
				if (root.TryGetProperty("data", out var dataElement) &&
				    dataElement.TryGetProperty("handle", out var handleElement))
				{
					var handle = IntPtr.Parse(handleElement.GetInt32().ToString());
					var foregroundHandle = handle;
					var action = "activate";

					if (NativeMethods.IsIconic(handle))
					{
						NativeMethods.SendMessage(handle, NativeMethods.WM_SYSCOMMAND, (IntPtr)NativeMethods.SC_RESTORE, IntPtr.Zero);
						NativeMethods.SetForegroundWindow(handle);
						action = "restore";
						Logger.Info($"ウィンドウを復元しました: {handle}");
					}
					else
					{
						var currentForegroundWindow = NativeMethods.GetForegroundWindow();
						if (handle == currentForegroundWindow)
						{
							foregroundHandle = GetNextWindowToActivate(handle);
							NativeMethods.SendMessage(handle, NativeMethods.WM_SYSCOMMAND, (IntPtr)NativeMethods.SC_MINIMIZE, IntPtr.Zero);
							action = "minimize";
							Logger.Info($"ウィンドウを最小化しました: {handle}");
						}
						else
						{
							NativeMethods.SetForegroundWindow(handle);
							Logger.Info($"ウィンドウをアクティブにしました: {handle}");
						}
					}

					SendMessageToWebView(new
					{
						type = "task_click_response",
						handle = handle.ToInt32(),
						action,
						foregroundHandle = foregroundHandle.ToInt32()
					});
				}
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "タスククリック処理時にエラーが発生しました。");
			}
		}

		private void HandleRequestWindowHandles()
		{
			try
			{
				var windowHandles = new List<int>();
				NativeMethods.EnumWindows((hwnd, lParam) =>
					{
						windowHandles.Add(hwnd.ToInt32());
						return true;
					},
					0);

				var response = new
				{
					type = "window_handles_response",
					windowHandles
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
					foregroundWindow = foregroundWindow.ToInt32()
				};

				SendMessageToWebView(response);
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "フォアグラウンドウィンドウ取得時にエラーが発生しました。");
			}
		}

		private void HandleRequestWindowSnapshot()
		{
			try
			{
				var foregroundWindow = NativeMethods.GetForegroundWindow();
				var items = new List<object>();

				NativeMethods.EnumWindows((hwnd, lParam) =>
					{
						items.Add(new
						{
							handle = hwnd.ToInt32(),
							isTaskBarWindow = NativeMethodUtility.IsTaskBarWindow(hwnd),
							isOnCurrentVirtualDesktop = VirtualDesktopUtility.IsWindowOnCurrentVirtualDesktop(hwnd),
						});

						return true;
					},
					0);

				SendMessageToWebView(new
				{
					type = "window_snapshot_response",
					items
				});
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "ウィンドウ状態一覧取得時にエラーが発生しました。");
			}
		}

		private void HandleRequestTaskBarItems(JsonElement root)
		{
			try
			{
				if (root.TryGetProperty("data", out var dataElement) &&
				    dataElement.TryGetProperty("windowHandles", out var handlesElement) &&
				    handlesElement.ValueKind == JsonValueKind.Array)
				{
					var foregroundWindow = NativeMethods.GetForegroundWindow();
					var items = new List<object>();

					foreach (var handleElement in handlesElement.EnumerateArray())
					{
						var handle = IntPtr.Parse(handleElement.GetInt32().ToString());
						items.Add(CreateTaskBarItemResponse(handle, foregroundWindow));
					}

					SendMessageToWebView(new
					{
						type = "taskbar_items_response",
						items
					});
				}
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "タスクバーウィンドウ詳細取得時にエラーが発生しました。");
			}
		}

		private object CreateTaskBarItemResponse(IntPtr handle, IntPtr foregroundWindow)
		{
			var rawProcessName = UwpUtility.GetRawProcessName(handle) ?? "";
			var processName = UwpUtility.GetProcessName(handle) ?? rawProcessName;
			var resolvedProcessId = UwpUtility.GetProcessId(handle);
			var sortKey = GetStableSortKey(handle, rawProcessName, processName);
			var sb = new StringBuilder(255);
			NativeMethods.GetWindowText(handle, sb, sb.Capacity);
			var title = sb.ToString();
			var iconResult = _windowIconService.ResolveWindowIcon(handle, processName, title);
			var rawFileName = Path.GetFileName(rawProcessName);
			if (string.Equals(rawFileName, "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase) ||
			    !string.Equals(rawProcessName, processName, StringComparison.OrdinalIgnoreCase))
			{
				Logger.Trace(
					$"WindowInfoTrace handle={handle.ToInt64()} rawProcess={rawProcessName} resolvedProcess={processName} resolvedProcessId={resolvedProcessId.ToInt64()} sortKey={sortKey} title={title}");
			}

			return new
			{
				handle = handle.ToInt32(),
				sortKey,
				moduleFileName = processName,
				title,
				isForeground = handle == foregroundWindow,
				iconData = iconResult.Base64 != null ? "data:image/png;base64," + iconResult.Base64 : null,
			};
		}

		private void HandleRequestIsTaskBarWindow(JsonElement root)
		{
			try
			{
				if (root.TryGetProperty("data", out var dataElement) &&
				    dataElement.TryGetProperty("windowHandle", out var handleElement))
				{
					var handle = IntPtr.Parse(handleElement.GetInt32().ToString());

					var isTaskBarWindow = NativeMethodUtility.IsTaskBarWindow(handle);

					// 現在の仮想デスクトップにあるウィンドウのみを対象とする
					if (isTaskBarWindow && !VirtualDesktopUtility.IsWindowOnCurrentVirtualDesktop(handle))
					{
						isTaskBarWindow = false;
					}

					SendMessageToWebView(new
					{
						type = "is_taskbar_window_response",
						windowHandle = handle.ToInt32(),
						isTaskBarWindow
					});
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
					var handle = IntPtr.Parse(handleElement.GetInt32().ToString());
					var rawProcessName = UwpUtility.GetRawProcessName(handle) ?? "";
					var processName = UwpUtility.GetProcessName(handle) ?? rawProcessName;
					var resolvedProcessId = UwpUtility.GetProcessId(handle);
					var sortKey = GetStableSortKey(handle, rawProcessName, processName);
					var sb = new StringBuilder(255);
					NativeMethods.GetWindowText(handle, sb, sb.Capacity);
					var title = sb.ToString();
					var iconResult = _windowIconService.ResolveWindowIcon(handle, processName, title);
					var rawFileName = Path.GetFileName(rawProcessName);
					if (string.Equals(rawFileName, "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase) ||
					    !string.Equals(rawProcessName, processName, StringComparison.OrdinalIgnoreCase))
					{
						Logger.Trace(
							$"WindowInfoTrace handle={handle.ToInt64()} rawProcess={rawProcessName} resolvedProcess={processName} resolvedProcessId={resolvedProcessId.ToInt64()} sortKey={sortKey} title={title}");
					}

					var response = new
					{
						type = "window_info_response",
						windowHandle = handle.ToInt32(),
						sortKey,
						moduleFileName = processName,
						title,
						iconData = iconResult.Base64 != null ? "data:image/png;base64," + iconResult.Base64 : null,
					};
					SendMessageToWebView(response);
				}
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "ウィンドウ情報取得時にエラーが発生しました。");
			}
		}

		private void HandleRequestIsWindowOnCurrentVirtualDesktop(JsonElement root)
		{
			try
			{
				if (root.TryGetProperty("data", out var dataElement) &&
				    dataElement.TryGetProperty("windowHandle", out var handleElement))
				{
					var handle = IntPtr.Parse(handleElement.GetInt32().ToString());
					var isOnCurrentVirtualDesktop = VirtualDesktopUtility.IsWindowOnCurrentVirtualDesktop(handle);
					SendMessageToWebView(new
					{
						type = "is_window_on_current_virtual_desktop_response",
						windowHandle = handle.ToInt32(),
						isOnCurrentVirtualDesktop
					});
				}
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "仮想デスクトップ判定時にエラーが発生しました。");
			}
		}

		private void HandleRequestIsWindowMinimized(JsonElement root)
		{
			try
			{
				if (root.TryGetProperty("data", out var dataElement) &&
				    dataElement.TryGetProperty("handle", out var handleElement))
				{
					var handle = IntPtr.Parse(handleElement.GetInt32().ToString());
					var isMinimized = NativeMethods.IsIconic(handle);
					SendMessageToWebView(new
					{
						type = "is_window_minimized_response",
						handle = handle.ToInt32(),
						isMinimized
					});
				}
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "ウィンドウ最小化判定時にエラーが発生しました。");
			}
		}

		private void HandleRequestNextWindowToActivate(JsonElement root)
		{
			try
			{
				if (root.TryGetProperty("data", out var dataElement) &&
				    dataElement.TryGetProperty("handle", out var handleElement))
				{
					var currentHandle = IntPtr.Parse(handleElement.GetInt32().ToString());
					var nextHandle = GetNextWindowToActivate(currentHandle);
					SendMessageToWebView(new
					{
						type = "next_window_to_activate_response",
						currentHandle = currentHandle.ToInt32(),
						nextHandle = nextHandle.ToInt32()
					});
				}
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "次のウィンドウ取得時にエラーが発生しました。");
			}
		}

		private IntPtr GetNextWindowToActivate(IntPtr currentHWnd)
		{
			IntPtr hWnd = NativeMethods.GetWindow(currentHWnd, NativeMethods.GW_HWNDNEXT);

			while (hWnd != IntPtr.Zero)
			{
				if (NativeMethods.IsWindowVisible(hWnd))
				{
					ulong exStyle = NativeMethods.GetWindowLongA(hWnd, NativeMethods.GWL_EXSTYLE);
					if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) == 0 &&
					    (exStyle & NativeMethods.WS_EX_NOACTIVATE) == 0)
					{
						// タスクバーに表示されるべきウィンドウであることを確認
						if (NativeMethodUtility.IsTaskBarWindow(hWnd))
						{
							return hWnd;
						}
					}
				}

				hWnd = NativeMethods.GetWindow(hWnd, NativeMethods.GW_HWNDNEXT);
			}

			return IntPtr.Zero;
		}

		private void HandleRestoreWindow(JsonElement root)
		{
			try
			{
				if (root.TryGetProperty("data", out var dataElement) &&
				    dataElement.TryGetProperty("handle", out var handleElement))
				{
					var handle = IntPtr.Parse(handleElement.GetInt32().ToString());
					NativeMethods.SendMessage(handle, NativeMethods.WM_SYSCOMMAND, (IntPtr)NativeMethods.SC_RESTORE, IntPtr.Zero);
					NativeMethods.SetForegroundWindow(handle);
					Logger.Info($"ウィンドウを復元しました: {handle}");
				}
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "ウィンドウ復元時にエラーが発生しました。");
			}
		}

		private void HandleMinimizeWindow(JsonElement root)
		{
			try
			{
				if (root.TryGetProperty("data", out var dataElement) &&
				    dataElement.TryGetProperty("handle", out var handleElement))
				{
					var handle = IntPtr.Parse(handleElement.GetInt32().ToString());
					NativeMethods.SendMessage(handle, NativeMethods.WM_SYSCOMMAND, (IntPtr)NativeMethods.SC_MINIMIZE, IntPtr.Zero);
					Logger.Info($"ウィンドウを最小化しました: {handle}");
				}
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "ウィンドウ最小化時にエラーが発生しました。");
			}
		}

		private void HandleActivateWindow(JsonElement root)
		{
			try
			{
				if (root.TryGetProperty("data", out var dataElement) &&
				    dataElement.TryGetProperty("handle", out var handleElement))
				{
					var handle = IntPtr.Parse(handleElement.GetInt32().ToString());
					NativeMethods.SetForegroundWindow(handle);
					Logger.Info($"ウィンドウをアクティブにしました: {handle}");
				}
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "ウィンドウアクティブ化時にエラーが発生しました。");
			}
		}

		private void HandleOpenDevTools()
		{
			try
			{
				if (_webView2?.CoreWebView2 != null)
				{
					_webView2.CoreWebView2.OpenDevToolsWindow();
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

		private bool IsValidFilename(string? filename)
		{
			if (string.IsNullOrWhiteSpace(filename))
			{
				return false;
			}

			// パストラバーサル攻撃の防止
			if (filename.Contains(".."))
			{
				return false;
			}

			// 無効な文字をチェック
			var invalidChars = Path.GetInvalidFileNameChars();
			if (filename.Any(c => invalidChars.Contains(c)))
			{
				return false;
			}

			return true;
		}

		private void HandleFileWriteRequest(JsonElement root)
		{
			try
			{
				if (root.TryGetProperty("filename", out var filenameElement) &&
				    root.TryGetProperty("data", out var dataElement))
				{
					var filename = filenameElement.GetString();
					var data = dataElement.GetString();

					// ファイル名の安全性をチェック
					if (string.IsNullOrEmpty(filename) || !IsValidFilename(filename))
					{
						SendMessageToWebView(new
						{
							type = "file_write_response",
							filename,
							success = false,
							error = "Invalid filename"
						});
						Logger.Warning($"ファイル書き込み拒否: 無効なファイル名 {filename}");
						return;
					}

					var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
					var appFolder = Path.Combine(appDataPath, "WpfTaskBar");
					Directory.CreateDirectory(appFolder);
					var filePath = Path.Combine(appFolder, filename);

					File.WriteAllText(filePath, data);

					SendMessageToWebView(new
					{
						type = "file_write_response",
						filename,
						success = true
					});
				}
			}
			catch (Exception ex)
			{
				var filename = "";
				if (root.TryGetProperty("filename", out var filenameElement))
				{
					filename = filenameElement.GetString() ?? "";
				}

				var response = new
				{
					type = "file_write_response",
					filename,
					success = false,
					error = ex.Message
				};

				SendMessageToWebView(response);
				Logger.Error(ex, $"ファイル書き込み時にエラーが発生しました: {filename}");
			}
		}

		private void HandleFileReadRequest(JsonElement root)
		{
			try
			{
				if (root.TryGetProperty("filename", out var filenameElement))
				{
					var filename = filenameElement.GetString();

					if (string.IsNullOrEmpty(filename) || !IsValidFilename(filename))
					{
						SendMessageToWebView(new
						{
							type = "file_read_response",
							filename,
							success = false,
							error = "Invalid filename",
							data = ""
						});
						Logger.Warning($"ファイル読み込み拒否: 無効なファイル名 {filename}");
						return;
					}

					var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
					var appFolder = Path.Combine(appDataPath, "WpfTaskBar");
					var filePath = Path.Combine(appFolder, filename);

					string data = "";
					if (File.Exists(filePath))
					{
						data = File.ReadAllText(filePath);
					}

					SendMessageToWebView(new
					{
						type = "file_read_response",
						filename,
						success = true,
						data
					});
				}
			}
			catch (Exception ex)
			{
				var filename = "";
				if (root.TryGetProperty("filename", out var filenameElement))
				{
					filename = filenameElement.GetString() ?? "";
				}

				SendMessageToWebView(new
				{
					type = "file_read_response",
					filename,
					success = false,
					error = ex.Message,
					data = ""
				});
					Logger.Error(ex, $"ファイル読み込み時にエラーが発生しました: {filename}");
				}
			}

		private void HandleOpenTaskManager()
		{
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "taskmgr.exe",
					UseShellExecute = true
				});
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "タスクマネージャーの起動に失敗しました。");
			}
		}

		private void HandleOpenAppDataFolder()
		{
			try
			{
				var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
				var appFolder = Path.Combine(appDataPath, "WpfTaskBar");
				Directory.CreateDirectory(appFolder);

				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = appFolder,
					UseShellExecute = true
				});
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "保存データフォルダを開く処理に失敗しました。");
			}
		}

		private void HandleRequestTimeRecordStatus()
		{
			try
			{
				var response = new
				{
					type = "time_record_status_response",
					clock_in_date = TimeRecordModel.ClockInDate,
					clock_out_date = TimeRecordModel.ClockOutDate
				};

				SendMessageToWebView(response);
				Logger.Info("時刻記録の状態を送信しました");
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "時刻記録の状態取得時にエラーが発生しました。");
			}
		}

		private static string GetStableSortKey(IntPtr handle, string rawProcessName, string resolvedProcessName)
		{
			var aumid = GetWindowApplicationUserModelId(handle);
			if (!string.IsNullOrWhiteSpace(aumid))
			{
				return $"aumid:{aumid}";
			}

			var package = AppxPackage.FromWindow(handle);
			if (package != null)
			{
				if (!string.IsNullOrWhiteSpace(package.ApplicationUserModelId))
				{
					return $"aumid:{package.ApplicationUserModelId}";
				}

				if (!string.IsNullOrWhiteSpace(package.FamilyName))
				{
					return $"package:{package.FamilyName}";
				}
			}

			if (!string.IsNullOrWhiteSpace(resolvedProcessName))
			{
				return $"process:{resolvedProcessName}";
			}

			if (!string.IsNullOrWhiteSpace(rawProcessName))
			{
				return $"process:{rawProcessName}";
			}

			return $"handle:{handle.ToInt64()}";
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

	}
}
