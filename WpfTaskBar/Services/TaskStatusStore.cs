namespace WpfTaskBar;

// 状態はウィンドウの生存中だけ保持し、ディスクには保存しない。
public sealed class TaskStatusStore(TimeProvider? timeProvider = null)
{
	private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
	private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(2);
	private readonly object _syncRoot = new();
	private readonly Dictionary<int, Entry> _entries = new();
	private readonly Dictionary<string, TaskStatusSession> _sessions = new();
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
			RemoveExpiredSessions();
			// HWND が別プロセスに再利用された場合、以前の状態を引き継がない。
			if (_entries.TryGetValue(handle, out var entry) && entry.ProcessId != processId)
				_entries.Remove(handle);
			foreach (var id in _sessions.Values
				.Where(session => session.Handle == handle && session.ProcessId != processId)
				.Select(session => session.Id).ToArray())
				_sessions.Remove(id);

			var statuses = _sessions.Values.Where(session => session.Handle == handle)
				.Select(session => session.Status)
				.Append(_entries.TryGetValue(handle, out entry) ? entry.Status : "none").ToArray();
			return statuses.Contains("running") ? "running" : statuses.Contains("completed") ? "completed" : "none";
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

	public TaskStatusSession CreateSession(int handle, int processId)
	{
		lock (_syncRoot)
		{
			RemoveExpiredSessions();
			var session = new TaskStatusSession(Guid.NewGuid().ToString("N"), handle, processId, "none", ++_revision,
				_time.GetUtcNow() + SessionLifetime);
			_sessions.Add(session.Id, session);
			return session;
		}
	}

	public TaskStatusSession? GetSession(string id)
	{
		lock (_syncRoot)
		{
			RemoveExpiredSessions();
			return _sessions.GetValueOrDefault(id);
		}
	}

	public bool SetSessionStatus(string id, string status)
	{
		if (!IsValidStatus(status)) throw new ArgumentException("Unknown task status.", nameof(status));
		lock (_syncRoot)
		{
			RemoveExpiredSessions();
			if (!_sessions.TryGetValue(id, out var session)) return false;
			_sessions[id] = session with { Status = status, Revision = ++_revision };
			return true;
		}
	}

	public bool RenewSession(string id)
	{
		lock (_syncRoot)
		{
			RemoveExpiredSessions();
			if (!_sessions.TryGetValue(id, out var session)) return false;
			_sessions[id] = session with { ExpiresAt = _time.GetUtcNow() + SessionLifetime, Revision = ++_revision };
			return true;
		}
	}

	public void RemoveSession(string id)
	{
		lock (_syncRoot) _sessions.Remove(id);
	}

	public void RemoveMissingWindows(IReadOnlySet<int> windowHandles, long snapshotRevision)
	{
		lock (_syncRoot)
		{
			RemoveExpiredSessions();
			// 列挙中に API で追加・更新された状態は、古いスナップショットでは削除しない。
			var missingHandles = _entries
				.Where(pair => pair.Value.Revision <= snapshotRevision && !windowHandles.Contains(pair.Key))
				.Select(pair => pair.Key).ToArray();
			foreach (var handle in missingHandles)
			{
				_entries.Remove(handle);
			}
			foreach (var id in _sessions.Values
				.Where(session => session.Revision <= snapshotRevision && !windowHandles.Contains(session.Handle))
				.Select(session => session.Id).ToArray())
				_sessions.Remove(id);
		}
	}

	private void RemoveExpiredSessions()
	{
		var now = _time.GetUtcNow();
		foreach (var id in _sessions.Values.Where(session => session.ExpiresAt <= now).Select(session => session.Id).ToArray())
			_sessions.Remove(id);
	}

	private sealed record Entry(int ProcessId, string Status, long Revision);
}

public sealed record TaskStatusSession(string Id, int Handle, int ProcessId, string Status, long Revision, DateTimeOffset ExpiresAt);
