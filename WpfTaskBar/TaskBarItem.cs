namespace WpfTaskBar;

public record TaskBarItem(IntPtr Handle, string Title, string IconFilePath, bool IsForeground);

public record UpdateTaskBarItem(IntPtr Handle, string Title, bool IsForeground);
