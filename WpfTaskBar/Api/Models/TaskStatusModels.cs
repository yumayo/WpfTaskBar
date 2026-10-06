using System.ComponentModel.DataAnnotations;

namespace WpfTaskBar;

public sealed class TaskStatusRequest
{
	public int? Handle { get; set; }
	public string? Title { get; set; }

	[Required]
	[RegularExpression("^(none|running|completed)$")]
	public string Status { get; set; } = string.Empty;
}

public sealed record TaskWindow(int Handle, int ProcessId, string Title, string ModuleFileName);

public interface ITaskWindowProvider
{
	IReadOnlyList<TaskWindow> GetWindows();
}
