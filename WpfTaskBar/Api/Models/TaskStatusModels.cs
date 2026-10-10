using System.ComponentModel.DataAnnotations;

namespace WpfTaskBar;

public sealed class TaskStatusRequest
{
	public int? Handle { get; set; }
	public string? Title { get; set; }

	[Required]
	[RegularExpression("^(none|running|waiting|interrupted|completed)$")]
	public string Status { get; set; } = string.Empty;
}

public sealed record TaskWindow(int Handle, int ProcessId, string Title, string ModuleFileName)
{
	public bool IsVsCode()
	{
		var executable = ModuleFileName.Replace('\\', '/').Split('/').Last();
		return executable.Equals("Code.exe", StringComparison.OrdinalIgnoreCase) ||
		       executable.Equals("Code - Insiders.exe", StringComparison.OrdinalIgnoreCase);
	}
}

public interface ITaskWindowProvider
{
	IReadOnlyList<TaskWindow> GetWindows();
	int GetForegroundHandle();
}
