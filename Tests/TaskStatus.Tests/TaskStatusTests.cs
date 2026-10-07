using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using WpfTaskBar;
using Xunit;

public sealed class TaskStatusTests
{
	private readonly TaskStatusStore _statuses = new();
	private readonly TaskStatusController _controller;

	public TaskStatusTests()
	{
		_controller = new TaskStatusController(new Windows(), _statuses);
	}

	[Fact]
	public void StatusCanBeStartedCompletedClearedAndRestarted()
	{
		foreach (var status in new[] { "running", "waiting", "running", "interrupted", "completed", "none", "running" })
		{
			var result = _controller.SetStatus(new TaskStatusRequest { Handle = 10, Status = status });
			var response = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value);
			Assert.Equal(10, response.GetProperty("handle").GetInt32());
			Assert.Equal(status, response.GetProperty("status").GetString());
			Assert.Equal(status, _statuses.GetStatus(10, 100));
			Assert.Equal("none", _statuses.GetStatus(20, 200));
		}
	}

	[Fact]
	public void TitleMatchesIgnoringCaseAndReturnsHandleForLaterUpdates()
	{
		var result = _controller.SetStatus(new TaskStatusRequest { Title = "PROJECT-A", Status = "running" });
		var response = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value);
		Assert.Equal(10, response.GetProperty("handle").GetInt32());
		Assert.Equal("running", _statuses.GetStatus(10, 100));
	}

	[Fact]
	public void AmbiguousTitleReturnsCandidatesWithoutChangingAnyStatus()
	{
		_statuses.SetStatus(10, 100, "completed");
		var result = _controller.SetStatus(new TaskStatusRequest { Title = "project", Status = "running" });
		var response = JsonSerializer.SerializeToElement(Assert.IsType<ConflictObjectResult>(result).Value);
		Assert.Equal(2, response.GetProperty("candidates").GetArrayLength());
		Assert.Equal("completed", _statuses.GetStatus(10, 100));
		Assert.Equal("none", _statuses.GetStatus(20, 200));
	}

	[Theory]
	[InlineData(null, null, "running")]
	[InlineData(null, " ", "running")]
	[InlineData(0, null, "running")]
	[InlineData(10, "project-a", "running")]
	[InlineData(10, "", "running")]
	[InlineData(10, null, "unknown")]
	[InlineData(10, null, "")]
	[InlineData(10, null, null)]
	public void InvalidRequestsDoNotChangeExistingStatus(int? handle, string? title, string? status)
	{
		_statuses.SetStatus(10, 100, "completed");
		Assert.IsType<BadRequestObjectResult>(_controller.SetStatus(new TaskStatusRequest
		{
			Handle = handle, Title = title, Status = status!
		}));
		Assert.Equal("completed", _statuses.GetStatus(10, 100));
	}

	[Theory]
	[InlineData(999, null)]
	[InlineData(null, "missing-project")]
	public void MissingWindowReturnsNotFound(int? handle, string? title)
	{
		Assert.IsType<NotFoundObjectResult>(_controller.SetStatus(new TaskStatusRequest
		{
			Handle = handle, Title = title, Status = "running"
		}));
	}

	[Fact]
	public void TaskListIncludesCurrentStatuses()
	{
		_statuses.SetStatus(20, 200, "running");
		var response = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(_controller.GetTasks()).Value);
		var tasks = response.GetProperty("tasks");
		Assert.Equal(2, tasks.GetArrayLength());
		Assert.Equal("none", tasks[0].GetProperty("status").GetString());
		Assert.Equal("running", tasks[1].GetProperty("status").GetString());
	}

	[Fact]
	public void ClosingWindowClearsItsStatusButRetainsOtherWindows()
	{
		_statuses.SetStatus(10, 100, "running");
		_statuses.SetStatus(20, 200, "completed");
		_statuses.RemoveMissingWindows(new HashSet<int> { 20 }, _statuses.GetRevision());
		Assert.Equal("none", _statuses.GetStatus(10, 100));
		Assert.Equal("completed", _statuses.GetStatus(20, 200));
	}

	[Fact]
	public void SnapshotDoesNotDiscardStatusUpdatedDuringWindowEnumeration()
	{
		_statuses.SetStatus(10, 100, "running");
		var revision = _statuses.GetRevision();
		_statuses.SetStatus(10, 100, "completed");
		_statuses.SetStatus(20, 200, "running");
		_statuses.RemoveMissingWindows(new HashSet<int>(), revision);
		Assert.Equal("completed", _statuses.GetStatus(10, 100));
		Assert.Equal("running", _statuses.GetStatus(20, 200));
	}

	[Fact]
	public void ReusedWindowHandleDoesNotInheritAnotherProcessStatus()
	{
		_statuses.SetStatus(10, 100, "completed");
		Assert.Equal("none", _statuses.GetStatus(10, 200));
		Assert.Equal("none", _statuses.GetStatus(10, 100));
	}

	private sealed class Windows : ITaskWindowProvider
	{
		public IReadOnlyList<TaskWindow> GetWindows() => new[]
		{
			new TaskWindow(10, 100, "project-a - Terminal", "terminal.exe"),
			new TaskWindow(20, 200, "project-b - Terminal", "terminal.exe")
		};
	}
}
