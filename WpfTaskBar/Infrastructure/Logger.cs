using System.IO;
using System.Text;

namespace WpfTaskBar;

public class Logger
{
	private enum LogLevel
	{
		Trace = 0,
		Debug = 1,
		Info = 2,
		Warning = 3,
		Error = 4
	}

	private static Logger? _singleton;

	private static readonly string LogName = "WpfTaskBar";
	private static readonly LogLevel MinimumLogLevel = LogLevel.Info;

	private readonly TextWriter _textWriterSynchronized;

	private Logger(TextWriter textWriterSynchronized)
	{
		_textWriterSynchronized = textWriterSynchronized;
	}

	public static void Setup()
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

		_singleton?._textWriterSynchronized.Dispose();
		_singleton = new Logger(textWriterSynchronized);
	}

	public static void Trace(object message)
	{
		_singleton?.InternalLog(LogLevel.Trace, "TRACE", message);
	}

	public static void Debug(object message)
	{
		_singleton?.InternalLog(LogLevel.Debug, "DEBUG", message);
	}

	public static void Info(object message)
	{
		_singleton?.InternalLog(LogLevel.Info, " INFO", message);
	}

	public static void Warning(object message)
	{
		_singleton?.InternalLog(LogLevel.Warning, " WARN", message);
	}

	public static void Error(Exception? exception, object message)
	{
		_singleton?.InternalLog(LogLevel.Error, "ERROR", message, exception);
	}

	private void InternalLog(LogLevel level, string type, object message, Exception? exception = null)
	{
		if (level < MinimumLogLevel)
		{
			return;
		}

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

	public static void Close()
	{
		_singleton?._textWriterSynchronized.Dispose();
	}
}
