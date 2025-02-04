namespace WpfTaskBar;

public record TaskBarItem(IntPtr Handle, string Title, string IconFilePath);

public record UpdateTaskBarItem(IntPtr Handle, string Title);
