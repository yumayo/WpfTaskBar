using System.IO;
using System.Text.Json;

namespace WpfTaskBar;

public static class AppSettingsModel
{
	private static readonly object SyncRoot = new();
	private static readonly string DataFilePath = AppDataPath.GetFilePath("settings.json");

	public static event EventHandler? SettingsChanged;

	public static bool IsAttendanceEnabled { get; private set; } = true;
	public static bool IsAiTaskDetailsVisible { get; private set; } = true;

	public static void Load()
	{
		lock (SyncRoot)
		{
			try
			{
				if (!File.Exists(DataFilePath))
				{
					IsAttendanceEnabled = true;
					IsAiTaskDetailsVisible = true;
					return;
				}

				var json = File.ReadAllText(DataFilePath);
				var data = JsonSerializer.Deserialize<AppSettingsData>(json);
				IsAttendanceEnabled = data?.IsAttendanceEnabled ?? true;
				IsAiTaskDetailsVisible = data?.IsAiTaskDetailsVisible ?? true;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to load app settings: {ex.Message}");
				IsAttendanceEnabled = true;
				IsAiTaskDetailsVisible = true;
			}
		}
	}

	public static void SetAttendanceEnabled(bool isEnabled)
	{
		var changed = false;
		lock (SyncRoot)
		{
			if (IsAttendanceEnabled == isEnabled)
			{
				return;
			}

			IsAttendanceEnabled = isEnabled;
			SaveCore();
			changed = true;
		}

		if (changed)
		{
			SettingsChanged?.Invoke(null, EventArgs.Empty);
		}
	}

	public static void SetAiTaskDetailsVisible(bool isVisible)
	{
		lock (SyncRoot)
		{
			if (IsAiTaskDetailsVisible == isVisible) return;
			IsAiTaskDetailsVisible = isVisible;
			SaveCore();
		}
		SettingsChanged?.Invoke(null, EventArgs.Empty);
	}

	public static void Save()
	{
		lock (SyncRoot)
		{
			SaveCore();
		}
	}

	private static void SaveCore()
	{
		try
		{
			AppDataPath.EnsureDirectory();

			var data = new AppSettingsData
			{
				IsAttendanceEnabled = IsAttendanceEnabled,
				IsAiTaskDetailsVisible = IsAiTaskDetailsVisible
			};

			var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
			{
				WriteIndented = true
			});

			File.WriteAllText(DataFilePath, json);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Failed to save app settings: {ex.Message}");
		}
	}

	private sealed class AppSettingsData
	{
		public bool IsAttendanceEnabled { get; set; } = true;
		public bool IsAiTaskDetailsVisible { get; set; } = true;
	}
}
