using System.IO;
using System.Text.Json;

namespace WpfTaskBar
{
	public class TimeRecordModel
	{
		private static readonly string DataFilePath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"WpfTaskBar",
			"timerecord.json"
		);

		public static DateTime ClockInDate;
		public static DateTime ClockOutDate;

		public static void Save()
		{
			try
			{
				var directory = Path.GetDirectoryName(DataFilePath);
				if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
				{
					Directory.CreateDirectory(directory);
				}

				var data = new TimeRecordData
				{
					ClockInDate = ClockInDate,
					ClockOutDate = ClockOutDate
				};

				var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
				{
					WriteIndented = true
				});

				File.WriteAllText(DataFilePath, json);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to save time record data: {ex.Message}");
			}
		}

		public static void Load()
		{
			try
			{
				if (!File.Exists(DataFilePath))
				{
					ClockInDate = default;
					ClockOutDate = default;
					return;
				}

				var json = File.ReadAllText(DataFilePath);
				var data = JsonSerializer.Deserialize<TimeRecordData>(json);

				if (data != null)
				{
					ClockInDate = data.ClockInDate;
					ClockOutDate = data.ClockOutDate;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to load time record data: {ex.Message}");
				ClockInDate = default;
				ClockOutDate = default;
			}
		}

		private class TimeRecordData
		{
			public DateTime ClockInDate { get; set; }
			public DateTime ClockOutDate { get; set; }
		}
	}
}