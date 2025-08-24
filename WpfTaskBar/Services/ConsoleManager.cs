using System.IO;
using System.Text;

namespace WpfTaskBar
{
	public static class ConsoleManager
	{
		public static bool Setup()
		{
			try
			{
				// 本当はこれを直接実行したのですが、
				// 「System.IO.IOException: ハンドルが無効です。」
				// と例外が出るため、リダイレクトしています。
				// Console.OutputEncoding = Encoding.UTF8;
				// Console.InputEncoding = Encoding.UTF8;

				// 標準出力をリダイレクト
				var writer = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8)
				{
					AutoFlush = true
				};
				Console.SetOut(writer);

				return true;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Console setup failed: {ex.Message}");
				return false;
			}
		}
	}
}