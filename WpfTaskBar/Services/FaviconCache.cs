using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace WpfTaskBar
{
	public class FaviconCache
	{
		private readonly IHttpClientFactory _httpClientFactory;
		private readonly Dictionary<string, string?> _faviconCache = new Dictionary<string, string?>();

		public FaviconCache(IHttpClientFactory httpClientFactory)
		{
			_httpClientFactory = httpClientFactory;
		}

		public string? ConvertFaviconUrlToBase64(string faviconUrl)
		{
			try
			{
				// キャッシュに存在する場合はキャッシュから返す
				if (_faviconCache.TryGetValue(faviconUrl, out var cachedBase64))
				{
					return cachedBase64;
				}

				string dataUrlResult;

				// data:image形式のURLの場合、既にdata URL形式なのでそのまま返す
				if (faviconUrl.StartsWith("data:image"))
				{
					dataUrlResult = faviconUrl;
				}
				else
				{
					// HTTPからダウンロードしてdata URL形式に変換
					var httpClient = _httpClientFactory.CreateClient();
					httpClient.Timeout = TimeSpan.FromSeconds(10);

					// レスポンス全体を取得してContent-Encodingを確認
					var response = httpClient.GetAsync(faviconUrl).Result;
					response.EnsureSuccessStatusCode();

					var imageBytes = response.Content.ReadAsByteArrayAsync().Result;

					// Content-Encodingがgzipの場合は解凍
					if (response.Content.Headers.ContentEncoding.Contains("gzip"))
					{
						using var compressedStream = new MemoryStream(imageBytes);
						using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
						using var decompressedStream = new MemoryStream();
						gzipStream.CopyTo(decompressedStream);
						imageBytes = decompressedStream.ToArray();
					}

					var base64String = Convert.ToBase64String(imageBytes);

					// MIMEタイプを判定（簡易的にPNGとして扱う、必要に応じて拡張可能）
					string mimeType = "image/png";
					if (faviconUrl.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
					{
						mimeType = "image/svg+xml";
					}
					else if (faviconUrl.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
					{
						mimeType = "image/x-icon";
					}

					dataUrlResult = $"data:{mimeType};base64,{base64String}";
				}

				// 結果をキャッシュに保存
				_faviconCache[faviconUrl] = dataUrlResult;

				return dataUrlResult;
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

			if (ex is HttpRequestException httpEx)
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
	}
}