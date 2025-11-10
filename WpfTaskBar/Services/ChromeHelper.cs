using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace WpfTaskBar
{
	public class ChromeHelper
	{
		private readonly object _sync = new object(); 
		private readonly List<TabInfo> _tabInfoList = new List<TabInfo>();

		public TabInfo? GetActiveTabInfoByHwnd(IntPtr hwnd)
		{
			if (hwnd == IntPtr.Zero)
			{
				return null;
			}
			lock (_sync)
			{
				return _tabInfoList.FirstOrDefault(x => x.Active && x.Hwnd == hwnd);
			}
		}

		public List<TabInfo> GetPinnedTabs()
		{
			lock (_sync)
			{
				return _tabInfoList.Where(x => x.Pinned).ToList();
			}
		}

		public void UpdateTab(TabInfo tabInfo, HttpContext context)
		{
			try
			{
				var chromeHwnd = FindChromeHwndByConnection(context);
				tabInfo.Hwnd = (int)chromeHwnd;
				lock (_sync)
				{
					if (tabInfo.Active)
					{
						foreach (var t in _tabInfoList.Where(x => x.WindowId == tabInfo.WindowId))
						{
							t.Active = false;
						}
					}
					
					var oldTabInfo = _tabInfoList.FirstOrDefault(x => x.WindowId == tabInfo.WindowId && x.TabId == tabInfo.TabId);
					if (oldTabInfo != null)
					{
						oldTabInfo.FavIconUrl = tabInfo.FavIconUrl;
						oldTabInfo.Url = tabInfo.Url;
						oldTabInfo.Title = tabInfo.Title;
						oldTabInfo.Active = tabInfo.Active;
						oldTabInfo.Pinned = tabInfo.Pinned;
						oldTabInfo.Hwnd = tabInfo.Hwnd;
					}
					else
					{
						_tabInfoList.Add(tabInfo);
					}
				}
				
				Logger.Info($"Chrome Update With HttpContext: {JsonSerializer.Serialize(tabInfo)}");
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Error handling bindWindowHandle");
			}
		}
		
		public void UpdateTab(TabInfo tabInfo)
		{
			lock (_sync)
			{
				if (tabInfo.Active)
				{
					foreach (var t in _tabInfoList.Where(x => x.WindowId == tabInfo.WindowId))
					{
						t.Active = false;
					}
				}

				var oldTabInfo = _tabInfoList.FirstOrDefault(x => x.WindowId == tabInfo.WindowId && x.TabId == tabInfo.TabId);
				if (oldTabInfo != null)
				{
					oldTabInfo.FavIconUrl = tabInfo.FavIconUrl;
					oldTabInfo.Url = tabInfo.Url;
					oldTabInfo.Title = tabInfo.Title;
					oldTabInfo.Active = tabInfo.Active;
					oldTabInfo.Pinned = tabInfo.Pinned;
					Logger.Info($"Chrome Update: {JsonSerializer.Serialize(oldTabInfo)}");
				}
				else
				{
					_tabInfoList.Add(tabInfo);
					Logger.Info($"Chrome Add: {JsonSerializer.Serialize(tabInfo)}");
				}
			}
		}

		private static IntPtr FindChromeHwndByConnection(HttpContext context)
		{
			try
			{
				// リモートIPアドレスとポートを取得
				var remoteEndPoint = context.Connection.RemoteIpAddress;
				var remotePort = context.Connection.RemotePort;

				Logger.Info($"WebSocket connection from: {remoteEndPoint}:{remotePort}");

				// netstatの情報を使ってプロセスIDを特定
				var processId = FindProcessIdByPort(remotePort);
				if (processId > 0)
				{
					// プロセスIDからChromeのメインウィンドウハンドルを取得
					var chromeHandle = FindChromeHwndByProcessId(processId);
					if (chromeHandle != IntPtr.Zero)
					{
						Logger.Info($"Chrome process found: PID={processId}, Handle={chromeHandle}");
						return chromeHandle;
					}
				}

				Logger.Info("Chrome process not found for this connection");
				return IntPtr.Zero;
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Error finding Chrome process by connection");
				return IntPtr.Zero;
			}
		}

		private static int FindProcessIdByPort(int port)
		{
			try
			{
				// netstatコマンドを実行してプロセスIDを取得
				var startInfo = new ProcessStartInfo
				{
					FileName = "netstat",
					Arguments = "-ano",
					RedirectStandardOutput = true,
					UseShellExecute = false,
					CreateNoWindow = true
				};

				using var process = Process.Start(startInfo);
				if (process == null) return 0;

				var output = process.StandardOutput.ReadToEnd();
				process.WaitForExit();

				var lines = output.Split('\n');
				foreach (var line in lines)
				{
					var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length >= 5 && parts[0] == "TCP")
					{
						var localEndPoint = parts[1];
						if (localEndPoint.EndsWith($":{port}"))
						{
							if (int.TryParse(parts[4], out var processId))
							{
								// プロセスがChromeかチェック
								try
								{
									var proc = Process.GetProcessById(processId);
									if (proc.ProcessName.Contains("chrome", StringComparison.OrdinalIgnoreCase))
									{
										Logger.Info($"Found Chrome process for port {port}: PID={processId}");
										return processId;
									}
								}
								catch
								{
									continue;
								}
							}
						}
					}
				}
				return 0;
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Error getting process ID by port using netstat");
				return 0;
			}
		}

		private static IntPtr FindChromeHwndByProcessId(int processId)
		{
			try
			{
				// 同じプロセス名のすべてのプロセスをチェック
				var processes = Process.GetProcessesByName("chrome");

				foreach (var process in processes)
				{
					try
					{
						if (process.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(process.MainWindowTitle))
						{
							Logger.Info($"Found Chrome window: PID={process.Id}, Handle={process.MainWindowHandle}, Title={process.MainWindowTitle}");
							return process.MainWindowHandle;
						}
					}
					catch
					{
						continue;
					}
				}

				return IntPtr.Zero;
			}
			catch (Exception ex)
			{
				Logger.Error(ex, $"Error getting Chrome main window for PID {processId}");
				return IntPtr.Zero;
			}
		}
	}
}