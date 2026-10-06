using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using WpfTaskBar;
using Xunit;

public sealed class TaskSessionTests
{
	private readonly Clock _clock = new();
	private readonly Windows _windows = new();
	private readonly TaskStatusStore _statuses;
	private readonly TaskSessionsController _controller;

	public TaskSessionTests()
	{
		_statuses = new TaskStatusStore(_clock);
		_controller = new TaskSessionsController(_windows, _statuses);
	}

	[Fact]
	public void OtherRunningTerminalKeepsWindowRunningUntilEveryResponseCompletes()
	{
		var first = CreateSession();
		var second = CreateSession();
		var otherWindow = CreateSession(20);
		SetStatus(first, "running");
		SetStatus(second, "running");
		SetStatus(otherWindow, "completed");
		SetStatus(first, "completed");
		Assert.Equal("running", _statuses.GetStatus(10, 100));
		Assert.Equal("completed", _statuses.GetStatus(20, 100));
		SetStatus(second, "completed");
		Assert.Equal("completed", _statuses.GetStatus(10, 100));
		SetStatus(first, "running");
		Assert.Equal("running", _statuses.GetStatus(10, 100));
	}

	[Fact]
	public void ClearingOrClosingOneTerminalDoesNotClearOtherTerminals()
	{
		var first = CreateSession();
		var second = CreateSession();
		SetStatus(first, "running");
		SetStatus(second, "completed");
		SetStatus(first, "none");
		Assert.Equal("completed", _statuses.GetStatus(10, 100));
		Assert.IsType<NoContentResult>(_controller.DeleteSession(first));
		Assert.Equal("completed", _statuses.GetStatus(10, 100));
		Assert.IsType<NoContentResult>(_controller.DeleteSession(second));
		Assert.Equal("none", _statuses.GetStatus(10, 100));
		Assert.IsType<NotFoundObjectResult>(_controller.SetStatus(first, new() { Status = "running" }));
	}

	[Theory]
	[InlineData(0, 100, true)]
	[InlineData(10, 0, true)]
	[InlineData(10, -1, true)]
	[InlineData(10, 999, false)]
	[InlineData(999, 100, false)]
	[InlineData(30, 300, false)]
	public void InvalidOrNonVsCodeTargetsCannotBeRegistered(int handle, int processId, bool badRequest)
	{
		var result = _controller.CreateSession(new() { Handle = handle, ProcessId = processId });
		if (badRequest) Assert.IsType<BadRequestObjectResult>(result);
		else Assert.IsType<NotFoundObjectResult>(result);
	}

	[Theory]
	[InlineData("unknown")]
	[InlineData("")]
	[InlineData(null)]
	public void InvalidStatusesDoNotChangeRunningState(string? status)
	{
		var id = CreateSession();
		SetStatus(id, "running");
		Assert.IsType<BadRequestObjectResult>(_controller.SetStatus(id, new() { Status = status! }));
		Assert.Equal("running", _statuses.GetStatus(10, 100));
	}

	[Fact]
	public void WindowTitleChangesDoNotChangeNotificationTarget()
	{
		var id = CreateSession();
		_windows.Items[0] = _windows.Items[0] with { Title = "different file - Visual Studio Code" };
		SetStatus(id, "running");
		Assert.Equal("running", _statuses.GetStatus(10, 100));
		Assert.Equal("none", _statuses.GetStatus(20, 100));
	}

	[Fact]
	public void ClosedOrReusedWindowRejectsStatusAndHeartbeat()
	{
		var first = CreateSession();
		var second = CreateSession();
		SetStatus(first, "running");
		_windows.Items[0] = _windows.Items[0] with { ProcessId = 999 };
		Assert.IsType<NotFoundObjectResult>(_controller.SetStatus(first, new() { Status = "completed" }));
		Assert.IsType<NotFoundObjectResult>(_controller.Heartbeat(second));
		Assert.Null(_statuses.GetSession(first));
		Assert.Null(_statuses.GetSession(second));
		Assert.Equal("none", _statuses.GetStatus(10, 999));
	}

	[Fact]
	public void WindowEnumerationRemovesSessionsAndProtectsNewerUpdates()
	{
		var removed = CreateSession();
		var retained = CreateSession(20);
		var revision = _statuses.GetRevision();
		SetStatus(retained, "running");
		_statuses.RemoveMissingWindows(new HashSet<int>(), revision);
		Assert.Null(_statuses.GetSession(removed));
		Assert.NotNull(_statuses.GetSession(retained));
		_statuses.RemoveMissingWindows(new HashSet<int>(), _statuses.GetRevision());
		Assert.Null(_statuses.GetSession(retained));
	}

	[Fact]
	public void HandleReusedByOtherProcessClearsOldSessionsOnRead()
	{
		var id = CreateSession();
		SetStatus(id, "running");
		Assert.Equal("none", _statuses.GetStatus(10, 999));
		Assert.Null(_statuses.GetSession(id));
	}

	[Fact]
	public void LostExtensionExpiresEvenWhenContainerContinuesNotifying()
	{
		var id = CreateSession();
		_clock.Advance(TimeSpan.FromSeconds(90));
		SetStatus(id, "running");
		_clock.Advance(TimeSpan.FromSeconds(30));
		Assert.Equal("none", _statuses.GetStatus(10, 100));
		Assert.IsType<NotFoundObjectResult>(_controller.SetStatus(id, new() { Status = "completed" }));
		Assert.IsType<NotFoundObjectResult>(_controller.Heartbeat(id));
	}

	[Fact]
	public void HeartbeatRenewsLeaseWithoutChangingStatus()
	{
		var id = CreateSession();
		SetStatus(id, "completed");
		_clock.Advance(TimeSpan.FromSeconds(90));
		Assert.IsType<NoContentResult>(_controller.Heartbeat(id));
		_clock.Advance(TimeSpan.FromSeconds(90));
		Assert.Equal("completed", _statuses.GetStatus(10, 100));
		_clock.Advance(TimeSpan.FromSeconds(30));
		Assert.Equal("none", _statuses.GetStatus(10, 100));
	}

	[Fact]
	public void ExistingManualStatusRemainsIndependentOfTerminalSessions()
	{
		_statuses.SetStatus(10, 100, "completed");
		var id = CreateSession();
		SetStatus(id, "running");
		Assert.Equal("running", _statuses.GetStatus(10, 100));
		_controller.DeleteSession(id);
		Assert.Equal("completed", _statuses.GetStatus(10, 100));
	}

	private string CreateSession(int handle = 10)
	{
		var response = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(
			_controller.CreateSession(new() { Handle = handle, ProcessId = 100 })).Value);
		return response.GetProperty("sessionId").GetString()!;
	}

	private void SetStatus(string id, string status) =>
		Assert.IsType<OkObjectResult>(_controller.SetStatus(id, new() { Status = status }));

	private sealed class Clock : TimeProvider
	{
		private DateTimeOffset _now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
		public override DateTimeOffset GetUtcNow() => _now;
		public void Advance(TimeSpan duration) => _now += duration;
	}

	private sealed class Windows : ITaskWindowProvider
	{
		// Electron の同一プロセスに複数ウィンドウがある場合も HWND で分離する。
		public List<TaskWindow> Items { get; } = new()
		{
			new(10, 100, "project - Visual Studio Code", @"C:\VSCode\Code.exe"),
			new(20, 100, "project - Visual Studio Code", @"C:\VSCode\CODE - INSIDERS.EXE"),
			new(30, 300, "terminal", "WindowsTerminal.exe")
		};
		public IReadOnlyList<TaskWindow> GetWindows() => Items;
	}
}
