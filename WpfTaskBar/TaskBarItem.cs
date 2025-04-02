namespace WpfTaskBar;

public class TaskBarItem
{
	public required IntPtr Handle { get; init; }

	public required string Title { get; init; }

	public required string ModuleFileName { get; init; }

	public required bool IsForeground { get; init; }
}
