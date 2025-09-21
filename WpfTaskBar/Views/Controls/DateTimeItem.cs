using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfTaskBar;

public class DateTimeItem
{
	public string Date { get; set; }
	
	public string Time { get; set; }

	public string StartTime { get; set; }

	public string EndTime { get; set; }

	private DateTime _updateDate;

	public void Update()
	{
		var now = DateTime.Now;

		if (now.Hour >= 4 && _updateDate.Hour < 4)
		{
			TimeRecordModel.ClockInDate = default;
			TimeRecordModel.ClockOutDate = default;
			Logger.Debug("日付が更新されました。出勤・退勤時刻をリセットします。");
		}

		StartTime = TimeRecordModel.ClockInDate != default ? TimeRecordModel.ClockInDate.ToString("HH:mm:ss") : "";
		EndTime = TimeRecordModel.ClockOutDate != default ? TimeRecordModel.ClockOutDate.ToString("HH:mm:ss") : "";
		
		Date = now.ToString("yyyy/MM/dd");
		Time = now.ToString("HH:mm:ss");

		_updateDate = now;
	}

	public bool IsStartTimeMissing => string.IsNullOrEmpty(StartTime);

	public bool IsEndTimeMissingAfter19 => string.IsNullOrEmpty(EndTime) && DateTime.Now.Hour >= 19;
}
