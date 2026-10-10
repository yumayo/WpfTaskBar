using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using WpfTaskBar;
using Xunit;

public sealed class ForegroundTaskTests
{
	private readonly Windows _windows = new();
	private readonly TaskStatusStore _statuses = new();

	[Fact]
	public void ForegroundHandleIdentifiesSecondWindowEvenWithSameTitleAndProcess()
	{
		var controller = new TaskStatusController(_windows, _statuses);
		var response = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(controller.GetForegroundTask()).Value);
		Assert.Equal(20, response.GetProperty("task").GetProperty("handle").GetInt32());
		Assert.Equal(100, response.GetProperty("task").GetProperty("processId").GetInt32());
		Assert.Empty(_statuses.GetSessions());
	}

	[Theory]
	[InlineData(0)]
	[InlineData(30)]
	[InlineData(999)]
	public void MissingOrNonVsCodeForegroundDoesNotFallBackToFirstWindow(int handle)
	{
		_windows.Foreground = handle;
		var controller = new TaskStatusController(_windows, _statuses);
		var response = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(controller.GetForegroundTask()).Value);
		Assert.Equal(JsonValueKind.Null, response.GetProperty("task").ValueKind);
	}

	[Fact]
	public void ForegroundChangeDuringEnumerationDiscardsTheCandidate()
	{
		_windows.OnEnumerate = () => _windows.Foreground = 10;
		var controller = new TaskStatusController(_windows, _statuses);
		var response = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(controller.GetForegroundTask()).Value);
		Assert.Equal(JsonValueKind.Null, response.GetProperty("task").ValueKind);
	}

	[Fact]
	public void AutoRegistrationRequiresTheSameForegroundAtRegistrationTime()
	{
		var controller = new TaskSessionsController(_windows, _statuses);
		Assert.IsType<ConflictObjectResult>(controller.CreateSession(new()
		{
			Handle = 10, ProcessId = 100, RequireForeground = true
		}));
		Assert.Empty(_statuses.GetSessions());
		Assert.IsType<OkObjectResult>(controller.CreateSession(new()
		{
			Handle = 20, ProcessId = 100, RequireForeground = true
		}));
		Assert.Equal(20, Assert.Single(_statuses.GetSessions()).Handle);
	}

	[Fact]
	public void ForegroundChangeDuringRegistrationValidationDoesNotCreateASession()
	{
		_windows.OnEnumerate = () => _windows.Foreground = 10;
		var controller = new TaskSessionsController(_windows, _statuses);
		Assert.IsType<ConflictObjectResult>(controller.CreateSession(new()
		{
			Handle = 20, ProcessId = 100, RequireForeground = true
		}));
		Assert.Empty(_statuses.GetSessions());
	}

	[Fact]
	public void ManualSelectionAndRememberedTargetCanRegisterInTheBackground()
	{
		var controller = new TaskSessionsController(_windows, _statuses);
		Assert.IsType<OkObjectResult>(controller.CreateSession(new() { Handle = 10, ProcessId = 100 }));
		Assert.Equal(10, Assert.Single(_statuses.GetSessions()).Handle);
	}

	private sealed class Windows : ITaskWindowProvider
	{
		public int Foreground { get; set; } = 20;
		public Action? OnEnumerate { get; set; }
		public int GetForegroundHandle() => Foreground;
		public IReadOnlyList<TaskWindow> GetWindows()
		{
			OnEnumerate?.Invoke();
			return new[]
			{
				new TaskWindow(10, 100, "same project", @"C:\VSCode\Code.exe"),
				new TaskWindow(20, 100, "same project", @"C:\VSCode\Code.exe"),
				new TaskWindow(30, 300, "terminal", "WindowsTerminal.exe")
			};
		}
	}
}
