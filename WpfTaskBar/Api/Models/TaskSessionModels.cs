using System.ComponentModel.DataAnnotations;

namespace WpfTaskBar;

public sealed class TaskSessionRequest
{
	public int Handle { get; set; }
	public int ProcessId { get; set; }
	[RegularExpression("^[a-f0-9]{32}$")]
	public string? SessionId { get; set; }
}

public sealed class TaskSessionStatusRequest
{
	[Required]
	[RegularExpression("^(none|running|waiting|interrupted|completed)$")]
	public string Status { get; set; } = string.Empty;
	// ツール完了・Esc通知が遅れて届いても、完了済みや中断済みの状態を戻さない。
	public bool OnlyIfActive { get; set; }
	[StringLength(512)]
	public string? ActivityText { get; set; }
}

public sealed class TaskSessionActivityRequest
{
	[Required(AllowEmptyStrings = true)]
	[StringLength(512)]
	public string ActivityText { get; set; } = null!;
}

public sealed class TaskSessionTitleRequest
{
	[Required(AllowEmptyStrings = true)]
	[StringLength(4096)]
	public string TerminalTitle { get; set; } = null!;
}
