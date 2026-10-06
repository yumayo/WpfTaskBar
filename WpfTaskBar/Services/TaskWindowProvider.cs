using System.Text;

namespace WpfTaskBar;

public sealed class TaskWindowProvider : ITaskWindowProvider
{
	public IReadOnlyList<TaskWindow> GetWindows()
	{
		var windows = new List<TaskWindow>();
		NativeMethods.EnumWindows((handle, _) =>
		{
			if (!NativeMethodUtility.IsTaskBarWindow(handle)) return true;

			var title = new StringBuilder(NativeMethods.GetWindowTextLength(handle) + 1);
			NativeMethods.GetWindowText(handle, title, title.Capacity);
			NativeMethods.GetWindowThreadProcessId(handle, out var processId);
			if (processId != 0 && !string.IsNullOrWhiteSpace(title.ToString()))
			{
				windows.Add(new TaskWindow(handle.ToInt32(), processId, title.ToString(),
					UwpUtility.GetProcessName(handle) ?? UwpUtility.GetRawProcessName(handle) ?? string.Empty));
			}

			return true;
		}, 0);
		return windows;
	}
}
