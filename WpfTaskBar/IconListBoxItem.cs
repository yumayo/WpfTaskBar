using System.Drawing;

namespace WinFormsTaskBar
{
	public class IconListBoxItem
	{
		public IconListBoxItem(string text, Image icon, IntPtr handle)
		{
			Text = text;
			Icon = icon;
			Handle = handle;
		}

		public string Text { get; set; }
		public Image? Icon { get; set; }
		public IntPtr Handle { get; set; }

		public override string ToString()
		{
			return Text;
		}
	}
}