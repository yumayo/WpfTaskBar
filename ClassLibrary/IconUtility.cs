using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace WpfTaskBar
{
	public class IconUtility
	{
		public static Icon ConvertPngToMultiSizeIcon(string pngFilePath)
		{
			using (var originalBitmap = new Bitmap(pngFilePath))
			{
				// 複数のサイズのアイコンを作成するためのメモリストリーム
				using (var iconStream = new MemoryStream())
				{
					// 必要なアイコンサイズを定義（例：16x16, 32x32, 48x48）
					int[] sizes = { 200 };

					// 各サイズのビットマップを作成
					using (var iconWriter = new BinaryWriter(iconStream))
					{
						// アイコンヘッダーを書き込む
						iconWriter.Write((short)0); // 予約済み
						iconWriter.Write((short)1); // Iconタイプ
						iconWriter.Write((short)sizes.Length); // イメージ数

						// 各サイズのアイコンを準備する
						List<MemoryStream> imageStreams = new List<MemoryStream>();
						foreach (int size in sizes)
						{
							using (var resizedBitmap = new Bitmap(originalBitmap, new Size(size, size)))
							{
								MemoryStream imageStream = new MemoryStream();
								resizedBitmap.Save(imageStream, ImageFormat.Png);
								imageStreams.Add(imageStream);

								// アイコンディレクトリエントリを書き込む
								iconWriter.Write((byte)size); // 幅
								iconWriter.Write((byte)size); // 高さ
								iconWriter.Write((byte)0); // カラーパレットサイズ
								iconWriter.Write((byte)0); // 予約済み
								iconWriter.Write((short)0); // カラープレーン
								iconWriter.Write((short)32); // ビット深度
								iconWriter.Write((int)imageStream.Length); // データサイズ
								iconWriter.Write((int)(6 + sizes.Length * 16 +
								                       imageStreams.Take(imageStreams.Count - 1).Sum(s => s.Length))); // データオフセット
							}
						}

						// 各イメージデータを書き込む
						foreach (var imageStream in imageStreams)
						{
							iconWriter.Write(imageStream.ToArray());
							imageStream.Dispose();
						}

						// ストリームをリセットしてIconを作成
						iconStream.Position = 0;
						return new Icon(iconStream);
					}
				}
			}
		}

		public static Icon ConvertPngToIcon(string pngFilePath)
		{
			// PNG画像を読み込む
			using (Bitmap originalImage = new Bitmap(pngFilePath))
			{
				using Bitmap normalizedImage = NormalizeIconBitmap(originalImage);

				using (var iconStream = new MemoryStream())
				{
					// アイコンに変換
					// ICOファイルのヘッダを書き込む
					// ICOヘッダ (6バイト)
					iconStream.WriteByte(0); // 予約済み。常に0
					iconStream.WriteByte(0); // 予約済み。常に0
					iconStream.WriteByte(1); // タイプ。1=アイコン
					iconStream.WriteByte(0); // タイプ。1=アイコン
					iconStream.WriteByte(1); // イメージ数。1つだけ
					iconStream.WriteByte(0); // 予約済み。常に0

					// ディレクトリエントリ (16バイト)
					iconStream.WriteByte((byte)normalizedImage.Width); // 幅
					iconStream.WriteByte((byte)normalizedImage.Height); // 高さ
					iconStream.WriteByte(0); // パレットサイズ。0=パレットなし
					iconStream.WriteByte(0); // 予約済み。常に0
					iconStream.WriteByte(0); // カラープレーン。常に0
					iconStream.WriteByte(0); // カラープレーン。常に0
					iconStream.WriteByte(32); // ビット数 (32ビット)
					iconStream.WriteByte(0); // ビット数 (32ビット)

					// データサイズを計算するための一時ストリームを作成
					using (MemoryStream tempStream = new MemoryStream())
					{
						// 画像データをPNG形式で一時ストリームに書き込む
						normalizedImage.Save(tempStream, ImageFormat.Png);
						byte[] imageData = tempStream.ToArray();
						int dataSize = imageData.Length;

						// データサイズを書き込む (4バイト)
						iconStream.WriteByte((byte)(dataSize & 0xFF));
						iconStream.WriteByte((byte)((dataSize >> 8) & 0xFF));
						iconStream.WriteByte((byte)((dataSize >> 16) & 0xFF));
						iconStream.WriteByte((byte)((dataSize >> 24) & 0xFF));

						// データオフセットを書き込む (4バイト) - ヘッダ(6) + ディレクトリエントリ(16) = 22
						iconStream.WriteByte(22);
						iconStream.WriteByte(0);
						iconStream.WriteByte(0);
						iconStream.WriteByte(0);

						// 画像データを書き込む
						iconStream.Write(imageData, 0, dataSize);
					}
					
					// ストリームをリセットしてIconを作成
					iconStream.Position = 0;
					return new Icon(iconStream);
				}
			}
		}

		private static Bitmap NormalizeIconBitmap(Bitmap originalImage)
		{
			var opaqueBounds = FindOpaqueBounds(originalImage);
			if (opaqueBounds == Rectangle.Empty)
			{
				return new Bitmap(originalImage);
			}

			var sideLength = Math.Max(opaqueBounds.Width, opaqueBounds.Height);
			var canvas = new Bitmap(sideLength, sideLength, PixelFormat.Format32bppArgb);
			using var graphics = Graphics.FromImage(canvas);
			graphics.Clear(Color.Transparent);

			var destination = new Rectangle(
				(sideLength - opaqueBounds.Width) / 2,
				(sideLength - opaqueBounds.Height) / 2,
				opaqueBounds.Width,
				opaqueBounds.Height);

			graphics.DrawImage(originalImage, destination, opaqueBounds, GraphicsUnit.Pixel);
			return canvas;
		}

		private static Rectangle FindOpaqueBounds(Bitmap bitmap)
		{
			int left = bitmap.Width;
			int top = bitmap.Height;
			int right = -1;
			int bottom = -1;

			for (var y = 0; y < bitmap.Height; y++)
			{
				for (var x = 0; x < bitmap.Width; x++)
				{
					if (bitmap.GetPixel(x, y).A == 0)
						continue;

					left = Math.Min(left, x);
					top = Math.Min(top, y);
					right = Math.Max(right, x);
					bottom = Math.Max(bottom, y);
				}
			}

			return right < left || bottom < top
				? Rectangle.Empty
				: Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
		}
	}
}
