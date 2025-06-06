using System.IO;
using System.Text;

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
		_logger?.InternalLog("DEBUG", message);
	}

	public static void Info(object message)
	{
		_logger?.InternalLog(" INFO", message);
	}

	public static void Warning(object message)
	{
		_logger?.InternalLog(" WARN", message);
	}

	public static void Error(Exception exception, object message)
	{
		_logger?.InternalLog("ERROR", message);
	}

	private void InternalLog(string type, object message, Exception? exception = null)
	{
		var now = DateTime.Now;
		var dateTimeString = now.ToString("yyyy-MM-dd HH:mm:ss");
		var sb = new StringBuilder();
		sb.Append(dateTimeString).Append(" ").Append(type).Append(" ").Append(message);
		if (exception != null)
		{
			sb.Append(" Exception: ").Append(exception.Message);
			if (!string.IsNullOrEmpty(exception.StackTrace))
			{
				sb.Append(" StackTrace: ").Append(exception.StackTrace);
			}
		}
		_textWriterSynchronized.Write(sb.ToString());
		_textWriterSynchronized.WriteLine();
		_textWriterSynchronized.Flush();
	}

	public void Dispose()
	{
		_textWriterSynchronized.Dispose();
	}
}
