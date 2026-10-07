using Microsoft.AspNetCore.Mvc;

namespace WpfTaskBar;

[ApiController]
public sealed class TaskStatusController(ITaskWindowProvider windows, TaskStatusStore statuses) : ControllerBase
{
	[HttpGet("tasks")]
	public IActionResult GetTasks()
	{
		return Ok(new { tasks = windows.GetWindows().Select(ToResponse).ToArray() });
	}

	[HttpPost("tasks/status")]
	public IActionResult SetStatus([FromBody] TaskStatusRequest request)
	{
		if (!TaskStatusStore.IsValidStatus(request.Status))
		{
			return BadRequest(new { message = "status は running、waiting、interrupted、completed、none のいずれかを指定してください。" });
		}

		if (request.Handle.HasValue == (request.Title != null) ||
		    request.Handle == 0 ||
		    (request.Title != null && string.IsNullOrWhiteSpace(request.Title)))
		{
			return BadRequest(new { message = "handle または空でない title のどちらか一方を指定してください。" });
		}

		var matches = windows.GetWindows().Where(window => request.Handle.HasValue
			? window.Handle == request.Handle.Value
			: window.Title.Contains(request.Title!, StringComparison.OrdinalIgnoreCase)).ToArray();

		if (matches.Length == 0)
		{
			return NotFound(new { message = "対象のタスクが見つかりません。GET /tasks で現在のタスクを確認してください。" });
		}
		if (matches.Length > 1)
		{
			return Conflict(new
			{
				message = "複数のタスクが一致しました。handle で対象を指定してください。",
				candidates = matches.Select(ToResponse).ToArray()
			});
		}

		var target = matches[0];
		statuses.SetStatus(target.Handle, target.ProcessId, request.Status);
		return Ok(ToResponse(target));
	}

	private object ToResponse(TaskWindow window)
	{
		var state = statuses.GetWindowState(window.Handle, window.ProcessId);
		return new
		{
			handle = window.Handle,
			processId = window.ProcessId,
			title = window.Title,
			moduleFileName = window.ModuleFileName,
			status = state.Status,
			hasAiTask = state.HasAiTask,
			terminalTitle = state.TerminalTitle
		};
	}
}
