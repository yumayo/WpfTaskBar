using System.IO;

namespace WpfTaskBar;

public static class AppDataPath
{
	public static string DirectoryPath => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
		"yumayo",
		"App.WpfTaskBar"
	);

	public static string GetFilePath(string filename)
	{
		return Path.Combine(DirectoryPath, filename);
	}

	public static void EnsureDirectory()
	{
		Directory.CreateDirectory(DirectoryPath);
	}
}
