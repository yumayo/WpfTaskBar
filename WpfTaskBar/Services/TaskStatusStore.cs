namespace WpfTaskBar;

// 状態はウィンドウの生存中だけ保持し、ディスクには保存しない。
public sealed class TaskStatusStore
{
	private readonly object _syncRoot = new();
	private readonly Dictionary<int, Entry> _entries = new();
	private long _revision;

	public static bool IsValidStatus(string? status) => status is "none" or "running" or "completed";

	public long GetRevision()
	{
		lock (_syncRoot) return _revision;
	}

	public string GetStatus(int handle, int processId)
	{
		lock (_syncRoot)
		{
			if (!_entries.TryGetValue(handle, out var entry)) return "none";
			if (entry.ProcessId == processId) return entry.Status;

			// HWND が別プロセスに再利用された場合、以前の状態を引き継がない。
			_entries.Remove(handle);
			return "none";
		}
	}

	public void SetStatus(int handle, int processId, string status)
	{
		if (!IsValidStatus(status)) throw new ArgumentException("Unknown task status.", nameof(status));

		lock (_syncRoot)
		{
			if (status == "none")
			{
				_entries.Remove(handle);
			}
			else
			{
				_entries[handle] = new Entry(processId, status, ++_revision);
			}
		}
	}

	public void RemoveMissingWindows(IReadOnlySet<int> windowHandles, long snapshotRevision)
	{
		lock (_syncRoot)
		{
			// 列挙中に API で追加・更新された状態は、古いスナップショットでは削除しない。
			var missingHandles = _entries
				.Where(pair => pair.Value.Revision <= snapshotRevision && !windowHandles.Contains(pair.Key))
				.Select(pair => pair.Key).ToArray();
			foreach (var handle in missingHandles)
			{
				_entries.Remove(handle);
			}
		}
	}

	private sealed record Entry(int ProcessId, string Status, long Revision);
}
