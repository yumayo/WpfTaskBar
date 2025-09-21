using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace WpfTaskBar;

public class IconListBoxItem
{
	public required string Text { get; set; }

	public required bool IsForeground { get; set; }

	public required BitmapSource? Icon { get ; set; }

	public required IntPtr Handle { get; set; }

	public required string? ModuleFileName { get; set; }

	public override string ToString()
	{
		return Text;
	}
}