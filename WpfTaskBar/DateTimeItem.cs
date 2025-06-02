using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using WpfTaskBar.Rest.Models;

namespace WpfTaskBar;

public class DateTimeItem : INotifyPropertyChanged
{
	private string _date;
	public string Date
	{
		get => _date;
		set => SetField(ref _date, value);
	}
	
	private string _time;
	public string Time
	{
		get => _time;
		set => SetField(ref _time, value);
	}

	private string _startTime;
	public string StartTime
	{
		get => _startTime;
		set => SetField(ref _startTime, value);
	}

	private string _endTime;
	public string EndTime
	{
		get => _endTime;
		set => SetField(ref _endTime, value);
	}

	private DateTime _updateDate;

	public void Update()
	{
		var now = DateTime.Now;
		
		if (now.Hour < 4 && _updateDate.Hour >= 4)
		{
			TimeRecordModel.ClockInDate = default;
			TimeRecordModel.ClockOutDate = default;
		}

		StartTime = TimeRecordModel.ClockInDate != default ? TimeRecordModel.ClockInDate.ToString("HH:mm:ss") : "";
		EndTime = TimeRecordModel.ClockOutDate != default ? TimeRecordModel.ClockOutDate.ToString("HH:mm:ss") : "";
		
		Date = now.ToString("yyyy/MM/dd");
		Time = now.ToString("HH:mm:ss");

		_updateDate = now;
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value)) return false;
		field = value;
		OnPropertyChanged(propertyName);
		return true;
	}
}
