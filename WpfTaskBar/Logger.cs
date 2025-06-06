using System.IO;

namespace WpfTaskBar;

public class Logger : IDisposable
{
	private static Logger? _logger;

	private static readonly string LogName = "WpfTaskBar";

	private readonly TextWriter _textWriterSynchronized;

	private Logger(TextWriter textWriterSynchronized)
	{
		_textWriterSynchronized = textWriterSynchronized;
	}

	public static Logger CreateLogger()
	{
		var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "log");
		var logName = LogName + ".log";
		var path = Path.Join(logDirectory, logName);

		var directoryName = Path.GetDirectoryName(path);
		if (!Directory.Exists(directoryName))
		{
			Directory.CreateDirectory(directoryName!);
		}

		var streamWriter = File.AppendText(path);
		var textWriterSynchronized = TextWriter.Synchronized(streamWriter);
		_logger = new Logger(textWriterSynchronized);

		return _logger;
	}

	public static void Debug(object message)
	{
		_logger?.InternalDebug(message);
	}

	private void InternalDebug(object message)
	{
		var now = DateTime.Now;
		var dateTimeString = now.ToString("yyyy-MM-dd HH:mm:ss");
		_textWriterSynchronized.WriteLine(dateTimeString + " " + message);
		_textWriterSynchronized.Flush();
	}

	public void Dispose()
	{
		_textWriterSynchronized.Dispose();
	}
}
