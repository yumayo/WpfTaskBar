namespace WpfTaskBar
{
	public class Payload
	{
		public string Action { get; set; } = "";
		public object Data { get; set; } = new {};
	}

	public class TabInfo
	{
		public int TabId { get; set; }
		public int WindowId { get; set; }
		public string Url { get; set; } = "";
		public string Title { get; set; } = "";
		public string LastActivity { get; set; } = DateTime.UtcNow.ToString("O");
		public string FaviconUrl { get; set; } = "";
		public bool IsActive { get; set; } = false;
		public int Index { get; set; } = 0;
	}

	public class NotificationData
	{
		public string Title { get; set; } = "";
		public string Message { get; set; } = "";
		public int TabId { get; set; }
		public int WindowId { get; set; }
		public string Url { get; set; } = "";
		public string TabTitle { get; set; } = "";
		public string Timestamp { get; set; } = "";
		public IntPtr WindowHandle { get; set; } = IntPtr.Zero;
	}

	public class TabsUpdateData
	{
		public List<TabInfo> Tabs { get; set; } = new();
	}
}