namespace WpfTaskBar
{
	public class Payload
	{
		public string Action { get; set; } = "";
		public object Data { get; set; } = new object();
	}

	public class TabInfo
	{
		public int TabId { get; set; }
		public int WindowId { get; set; }
		public string? Url { get; set; }
		public string? Title { get; set; }
		public string? FavIconUrl { get; set; }
		public bool? Active { get; set; }
		public bool? Pinned { get; set; }
		public int Hwnd { get; set; }
	}

	public class FocusTabData
	{
		public int TabId { get; set; }
	}
}