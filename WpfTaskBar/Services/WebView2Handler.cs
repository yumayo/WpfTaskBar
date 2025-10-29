using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace WpfTaskBar
{
	public class WebView2Handler
	{
		private Dispatcher _dispatcher;
		private WebView2 _webView2;
		private ChromeTabManager _chromeTabManager;
		private Http2StreamHandler _http2StreamHandler;
		private readonly Dictionary<string, string?> _faviconCache = new Dictionary<string, string?>();

		public WebView2Handler(ChromeTabManager chromeTabManager, Http2StreamHandler http2StreamHandler)
		{
			_chromeTabManager = chromeTabManager;
			_http2StreamHandler = http2StreamHandler;
		}

		public async Task Initialize(Dispatcher dispatcher, WebView2 webView2)
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
				var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Web", "index.html");
				htmlPath = Path.GetFullPath(htmlPath);
#else
				// HTMLファイルのパスを取得
				var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web", "index.html");
#endif

				var htmlUri = new Uri($"file:///{htmlPath.Replace('\\', '/')}");

				Logger.Info($"Loading HTML from: {htmlUri}");

				// HTMLファイルを読み込み
				_webView2.CoreWebView2.Navigate(htmlUri.ToString());

				Logger.Info("WebView2初期化完了 - NavigationCompletedイベントを待機中");
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "WebView2初期化時にエラーが発生しました。");
			}
		}

		public void SendMessageToWebView(object data)
		{
			_dispatcher.Invoke(() =>
			{
				if (_webView2.CoreWebView2 != null)
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
			});
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

						switch (messageType)
						{
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

							case "task_click":
								HandleTaskClick(root);
								break;

							case "task_middle_click":
								HandleTaskMiddleClick(root);
								break;

							case "notification_click":
								HandleNotificationClick(root);
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

		private void HandleTaskClick(JsonElement root)
		{
			try
			{
				if (root.TryGetProperty("data", out var dataElement))
				{
					// Chromeタブの情報を取得（オプショナル）
					int? tabId = null;
					int? windowId = null;
					if (dataElement.TryGetProperty("tabId", out var tabIdElement) &&
					    dataElement.TryGetProperty("windowId", out var windowIdElement))
					{
						tabId = tabIdElement.GetInt32();
						windowId = windowIdElement.GetInt32();
					}

					if (dataElement.TryGetProperty("handle", out var handleElement))
					{
						var handle = IntPtr.Parse(handleElement.GetInt32().ToString());
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

							// クリックされたウィンドウが既にアクティブな場合
							if (handle == foregroundWindow)
							{
								// Chromeタブの場合は、タブIDも比較する
								bool shouldMinimize = true;
								if (tabId.HasValue)
								{
									// WindowIdでフィルタリング（同じChromeウィンドウのタブのみ）
									// hwndから対応するWindowIdを探す
									var targetWindowId = FindWindowIdByHwnd(handle);
									if (targetWindowId.HasValue)
									{
										var currentTab = _chromeTabManager.GetTabsByWindow(targetWindowId.Value).FirstOrDefault(t => t.IsActive);
										// 現在アクティブなタブと異なるタブの場合は最小化しない
										if (currentTab != null && currentTab.TabId != tabId.Value)
										{
											shouldMinimize = false;
											Logger.Info($"Chromeの別タブをアクティブにします: TabId={tabId.Value} (現在アクティブなタブ: {currentTab.TabId})");
										}
										else
										{
											Logger.Info($"Chromeの同じタブがアクティブなため最小化します: TabId={tabId.Value}");
										}
									}
									else
									{
										Logger.Info($"WindowHandleと関連付けられたWindowIdが見つからないため、通常のウィンドウ拡大縮小動作となります。{handle}");
									}
								}
								else
								{
									Logger.Info($"通常のウィンドウがアクティブなため最小化します: {handle}");
								}

								if (shouldMinimize)
								{
									// ウィンドウを最小化
									NativeMethods.SendMessage(handle, NativeMethods.WM_SYSCOMMAND, (IntPtr)NativeMethods.SC_MINIMIZE, IntPtr.Zero);
									Logger.Info($"アクティブなウィンドウを最小化しました: {handle}");
								}
								else
								{
									Logger.Info($"別のウィンドウをアクティブにするため最小化をスキップします: {handle}");
								}
							}
							else
							{
								// ウィンドウをアクティブにする
								NativeMethods.SetForegroundWindow(handle);
								Logger.Info($"ウィンドウをアクティブにしました: {handle}");
							}
						}
					}
					else
					{
						Logger.Info($"Handleプロパティーが代入されていません。");
					}

					// Chromeタブの場合は、タブもアクティブにする
					if (tabId.HasValue && windowId.HasValue)
					{
						_ = _http2StreamHandler.FocusTab(tabId.Value, windowId.Value);
						Logger.Info($"復元後にChromeタブをアクティブにしました: TabId={tabId.Value}, WindowId={windowId.Value}");
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
					// Chromeタブの場合の処理
					bool isChrome = false;
					int? tabId = null;
					int? windowId = null;

					if (dataElement.TryGetProperty("isChrome", out var isChromeElement))
					{
						isChrome = isChromeElement.GetBoolean();
					}

					if (isChrome &&
					    dataElement.TryGetProperty("tabId", out var tabIdElement) &&
					    dataElement.TryGetProperty("windowId", out var windowIdElement))
					{
						tabId = tabIdElement.GetInt32();
						windowId = windowIdElement.GetInt32();

						// Chromeのタブを閉じる
						_ = _http2StreamHandler.CloseTab(tabId.Value, windowId.Value);
						Logger.Info($"Chromeタブを閉じるメッセージを送信: TabId={tabId.Value}, WindowId={windowId.Value}");
						return;
					}

					var handle = IntPtr.Parse(handleElement.GetInt32().ToString());

					// handleのプロセスIDを取得
					NativeMethods.GetWindowThreadProcessId(handle, out var targetProcessId);

					// 全ウィンドウのhandleを一旦配列に格納
					var allHandles = new List<IntPtr>();
					NativeMethods.EnumWindows((hwnd, lParam) =>
						{
							allHandles.Add(hwnd);
							return true;
						},
						0);

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
					var processName = UwpUtility.GetProcessName(handle) ?? "";
					var sb = new StringBuilder(255);
					NativeMethods.GetWindowText(handle, sb, sb.Capacity);
					var title = sb.ToString();

					if (processName.Contains("chrome.exe", StringComparison.OrdinalIgnoreCase))
					{
						var allTabs = _chromeTabManager.GetAllTabsSorted().ToList();

						// Chromeアプリケーションのアイコンを取得
						var chromeIconData = GetIconAsBase64(processName);

						// WindowIdでフィルタリング（同じChromeウィンドウのタブのみ）
						// hwndから対応するWindowIdを探す
						var targetWindowId = FindWindowIdByHwnd(handle);
						IEnumerable<dynamic> chromeTabs;

						if (targetWindowId.HasValue)
						{
							// WindowIdが特定できた場合は、そのウィンドウのタブのみをフィルタリング
							chromeTabs = allTabs
								.Where(tab => tab.WindowId == targetWindowId.Value)
								.Select(tab => new
								{
									tabId = tab.TabId,
									windowId = tab.WindowId,
									title = tab.Title,
									url = tab.Url,
									index = tab.Index,
									iconData = chromeIconData,
									faviconData = ConvertFaviconUrlToBase64(tab.FaviconUrl),
									isActive = tab.IsActive
								}).ToList();

							var response = new
							{
								type = "window_info_response",
								windowHandle = handle.ToInt32(),
								moduleFileName = processName,
								title,
								iconData = chromeIconData,
								chromeTabs = chromeTabs.ToArray()
							};

							SendMessageToWebView(response);
							return;
						}
						else
						{
							// WindowIdが特定できない場合は、フォールバックとして全タブを返す
							chromeTabs = allTabs
								.Select(tab => new
								{
									tabId = tab.TabId,
									windowId = tab.WindowId,
									title = tab.Title,
									url = tab.Url,
									index = tab.Index,
									iconData = chromeIconData,
									faviconData = ConvertFaviconUrlToBase64(tab.FaviconUrl),
									isActive = tab.IsActive
								}).ToList();

							var response = new
							{
								type = "window_info_response",
								windowHandle = handle.ToInt32(),
								moduleFileName = processName,
								title,
								iconData = chromeIconData,
								chromeTabs = chromeTabs.ToArray()
							};

							SendMessageToWebView(response);
							return;
						}
					}

					// Chrome以外の通常のウィンドウ
					var iconData = GetIconAsBase64(processName);

					var normalResponse = new
					{
						type = "window_info_response",
						windowHandle = handle.ToInt32(),
						moduleFileName = processName,
						title,
						iconData,
						chromeTabs = (object[]?)null
					};

					SendMessageToWebView(normalResponse);
				}
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "ウィンドウ情報取得時にエラーが発生しました。");
			}
		}

		private string? ConvertFaviconUrlToBase64(string faviconUrl)
		{
			try
			{
				// キャッシュに存在する場合はキャッシュから返す
				if (_faviconCache.TryGetValue(faviconUrl, out var cachedBase64))
				{
					return cachedBase64;
				}

				string? base64Result = null;

				// data:image形式のURLの場合、既にBase64エンコードされているため、そのまま返す
				if (faviconUrl.StartsWith("data:image"))
				{
					// data:image/png;base64,... の形式からBase64部分のみを抽出
					var base64Index = faviconUrl.IndexOf("base64,");
					if (base64Index >= 0)
					{
						base64Result = faviconUrl.Substring(base64Index + 7);
					}
				}
				else
				{
					// それ以外の場合はHTTPからダウンロード
					using var httpClient = new System.Net.Http.HttpClient();
					httpClient.Timeout = TimeSpan.FromSeconds(5);
					var imageBytes = httpClient.GetByteArrayAsync(faviconUrl).Result;
					base64Result = Convert.ToBase64String(imageBytes);
				}

				// 結果をキャッシュに保存
				_faviconCache[faviconUrl] = base64Result;

				return base64Result;
			}
			catch (Exception ex)
			{
				Logger.Error(ex, $"Favicon URLの変換に失敗しました: {faviconUrl}");

				// 一時的なネットワークエラーかどうかを判定
				bool isTemporaryError = IsTemporaryNetworkError(ex);

				// 一時的なエラーでない場合のみキャッシュに保存
				if (!isTemporaryError)
				{
					_faviconCache[faviconUrl] = null;
					Logger.Info($"Faviconのエラーをキャッシュしました: {faviconUrl}");
				}
				else
				{
					Logger.Info($"一時的なネットワークエラーのためキャッシュしません: {faviconUrl}");
				}

				return null;
			}
		}

		private bool IsTemporaryNetworkError(Exception ex)
		{
			// 一時的なネットワークエラーと判定する条件
			// 1. TaskCanceledException (タイムアウト)
			// 2. HttpRequestException のうち特定のもの (接続失敗など)
			// 3. AggregateException の内部例外を確認

			if (ex is TaskCanceledException || ex is OperationCanceledException)
			{
				return true;
			}

			if (ex is System.Net.Http.HttpRequestException httpEx)
			{
				// 接続エラーやDNS解決失敗などは一時的なエラーとみなす
				var message = httpEx.Message.ToLower();
				if (message.Contains("timeout") ||
				    message.Contains("connection") ||
				    message.Contains("network") ||
				    message.Contains("dns"))
				{
					return true;
				}
			}

			if (ex is AggregateException aggEx)
			{
				// AggregateExceptionの内部例外をチェック
				foreach (var innerEx in aggEx.InnerExceptions)
				{
					if (IsTemporaryNetworkError(innerEx))
					{
						return true;
					}
				}
			}

			// その他のエラー（404, 403など）は永続的なエラーとみなす
			return false;
		}

		private string? GetIconAsBase64(string? moduleFileName)
		{
			if (string.IsNullOrEmpty(moduleFileName))
			{
				return null;
			}

			try
			{
				var icon = GetIcon(moduleFileName);
				if (icon == null)
				{
					Logger.Info($"GetIconAsBase64: アイコン取得失敗 {moduleFileName}");
					return null;
				}

				// BitmapSourceをBase64に変換
				var encoder = new PngBitmapEncoder();
				encoder.Frames.Add(BitmapFrame.Create(icon));

				using var stream = new MemoryStream();
				encoder.Save(stream);
				var base64String = Convert.ToBase64String(stream.ToArray());
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

		private int? FindWindowIdByHwnd(IntPtr hwnd)
		{
			// _chromeTabManagerの全タブから、対応するwindowIdを探す
			var allTabs = _chromeTabManager.GetAllTabsSorted().ToList();

			var uniqueWindowIds = allTabs.Select(tab => tab.WindowId).Distinct();

			foreach (var windowId in uniqueWindowIds)
			{
				var mappedHwnd = _http2StreamHandler.GetHwndByWindowId(windowId);
				if (mappedHwnd == hwnd)
				{
					return windowId;
				}
			}

			return null;
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

		private void HandleOpenDevTools()
		{
			try
			{
				if (_webView2.CoreWebView2 != null)
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
	}
}