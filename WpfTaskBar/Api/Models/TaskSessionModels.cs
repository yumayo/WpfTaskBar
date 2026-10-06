using System.ComponentModel.DataAnnotations;

namespace WpfTaskBar;

public sealed class TaskSessionRequest
{
	public int Handle { get; set; }
	public int ProcessId { get; set; }
}

public sealed class TaskSessionStatusRequest
{
	[Required]
	[RegularExpression("^(none|running|completed)$")]
	public string Status { get; set; } = string.Empty;
}
