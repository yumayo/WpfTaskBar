using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

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

	public void Update()
	{
		var now = DateTime.Now;
		Date = now.ToString("yyyy/MM/dd");
		Time = now.ToString("H:mm:ss");
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
