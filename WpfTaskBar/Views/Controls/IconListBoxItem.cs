using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace WpfTaskBar;

public class IconListBoxItem : INotifyPropertyChanged
{
	private string _text = "";

	public required string Text
	{
		get => _text;
		set => SetField(ref _text, value);
	}

	private bool _isForeground;

	public required bool IsForeground
	{
		get => _isForeground;
		set => SetField(ref _isForeground, value);
	}

	private BitmapSource? _icon;

	public required BitmapSource? Icon
	{
		get => _icon;
		set => SetField(ref _icon, value);
	}

	public required IntPtr Handle { get; set; }

	public required string? ModuleFileName { get; set; }

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