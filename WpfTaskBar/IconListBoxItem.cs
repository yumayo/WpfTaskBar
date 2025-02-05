using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace WpfTaskBar;

public class IconListBoxItem : INotifyPropertyChanged
{
	public IconListBoxItem(string text, string? moduleFileName, BitmapSource? icon, IntPtr handle)
	{
		Text = text;
		ModuleFileName = moduleFileName;
		Icon = icon;
		Handle = handle;
	}

	private string _text;
	public string Text
	{
		get => _text;
		set => SetField(ref _text, value);
	}

	public BitmapSource? Icon { get; set; }
	public IntPtr Handle { get; set; }
	public string? ModuleFileName { get; set; }

	public override string ToString()
	{
		return Text;
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
