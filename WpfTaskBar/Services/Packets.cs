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
		public int? Index { get; set; }
		public string? Url { get; set; }
		public string? Title { get; set; }
		public string? FavIconUrl { get; set; }
		public bool? Active { get; set; }
		public bool? Pinned { get; set; }

		public string? Status { get; set; }
		public TabInfo? Snapshot { get; set; }

		public int Hwnd { get; set; }
		public bool HasNotification { get; set; }

		public TabInfo Clone()
		{
			return new TabInfo
			{
				TabId = this.TabId,
				WindowId = this.WindowId,
				Index = this.Index,
				Url = this.Url,
				Title = this.Title,
				FavIconUrl = this.FavIconUrl,
				Active = this.Active,
				Pinned = this.Pinned,
				Status = this.Status,
				Snapshot = null,
				Hwnd = this.Hwnd,
				HasNotification = this.HasNotification
			};
		}
	}

	public class FocusTabData
	{
		public int TabId { get; set; }
	}
}