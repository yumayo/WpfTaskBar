using System.Drawing;
using System.Drawing.Imaging;

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
				// 元のサイズが200x200でない場合はリサイズ
				Bitmap resizedImage = originalImage;
				// if (originalImage.Width != 200 || originalImage.Height != 200)
				// {
				// 	resizedImage = new Bitmap(originalImage, new Size(200, 200));
				// }

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
					iconStream.WriteByte((byte)resizedImage.Width); // 幅 (200は255より小さいのでそのまま)
					iconStream.WriteByte((byte)resizedImage.Height); // 高さ
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
						resizedImage.Save(tempStream, ImageFormat.Png);
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
	}
}