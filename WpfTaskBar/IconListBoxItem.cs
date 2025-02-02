using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace WinFormsTaskBar
{
	public class IconListBoxItem
	{
		public IconListBoxItem(string text, BitmapSource icon, IntPtr handle)
		{
			Text = text;
			Icon = icon;
			Handle = handle;
		}

		public string Text { get; set; }
		public BitmapSource? Icon { get; set; }
		public IntPtr Handle { get; set; }

		public override string ToString()
		{
			return Text;
		}
	}
}