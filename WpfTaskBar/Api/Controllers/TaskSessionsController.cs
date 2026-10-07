using Microsoft.AspNetCore.Mvc;

namespace WpfTaskBar;

// VSCode 拡張がウィンドウを選び、コンテナは発行されたセッション ID だけで通知する。
[ApiController]
public sealed class TaskSessionsController(ITaskWindowProvider windows, TaskStatusStore statuses) : ControllerBase
{
	[HttpPost("tasks/sessions")]
	public IActionResult CreateSession([FromBody] TaskSessionRequest request)
	{
		if (request.Handle == 0 || request.ProcessId <= 0)
			return BadRequest(new { message = "handle と processId を指定してください。" });

		var target = windows.GetWindows().FirstOrDefault(window =>
			window.Handle == request.Handle && window.ProcessId == request.ProcessId);
		if (target == null || !IsVsCode(target))
			return NotFound(new { message = "対象の VSCode ウィンドウが見つかりません。" });

		var session = statuses.CreateSession(target.Handle, target.ProcessId);
		return Ok(new { sessionId = session.Id, handle = target.Handle, processId = target.ProcessId });
	}

	[HttpPost("tasks/sessions/{id}/status")]
	public IActionResult SetStatus(string id, [FromBody] TaskSessionStatusRequest request)
	{
		if (!TaskStatusStore.IsValidStatus(request.Status))
			return BadRequest(new { message = "status は running、waiting、interrupted、completed、none のいずれかを指定してください。" });

		var session = statuses.GetSession(id);
		if (session == null) return SessionNotFound();
		var target = windows.GetWindows().FirstOrDefault(window =>
			window.Handle == session.Handle && window.ProcessId == session.ProcessId && IsVsCode(window));
		if (target == null)
		{
			statuses.RemoveSession(id);
			return SessionNotFound();
		}
		if (!statuses.SetSessionStatus(id, request.Status, request.OnlyIfActive)) return SessionNotFound();
		return Ok(new { handle = target.Handle, status = statuses.GetStatus(target.Handle, target.ProcessId) });
	}

	[HttpDelete("tasks/sessions/{id}")]
	public IActionResult DeleteSession(string id)
	{
		statuses.RemoveSession(id);
		return NoContent();
	}

	[HttpPut("tasks/sessions/{id}/title")]
	public IActionResult SetTitle(string id, [FromBody] TaskSessionTitleRequest request)
	{
		if (request.TerminalTitle == null || request.TerminalTitle.Length > 4096)
			return BadRequest(new { message = "terminalTitle は4096文字以内の文字列を指定してください。" });
		var session = statuses.GetSession(id);
		if (session == null) return SessionNotFound();
		if (!windows.GetWindows().Any(window =>
			window.Handle == session.Handle && window.ProcessId == session.ProcessId && IsVsCode(window)))
		{
			statuses.RemoveSession(id);
			return SessionNotFound();
		}
		return statuses.SetSessionTitle(id, request.TerminalTitle) ? NoContent() : SessionNotFound();
	}

	[HttpPut("tasks/sessions/{id}/heartbeat")]
	public IActionResult Heartbeat(string id)
	{
		var session = statuses.GetSession(id);
		if (session == null) return SessionNotFound();
		if (!windows.GetWindows().Any(window =>
			window.Handle == session.Handle && window.ProcessId == session.ProcessId && IsVsCode(window)))
		{
			statuses.RemoveSession(id);
			return SessionNotFound();
		}
		return statuses.RenewSession(id) ? NoContent() : SessionNotFound();
	}

	private NotFoundObjectResult SessionNotFound() =>
		NotFound(new { message = "通知先のセッションがありません。VSCode 拡張からターミナルを開き直してください。" });

	private static bool IsVsCode(TaskWindow window)
	{
		var executable = window.ModuleFileName.Replace('\\', '/').Split('/').Last();
		return executable.Equals("Code.exe", StringComparison.OrdinalIgnoreCase) ||
		       executable.Equals("Code - Insiders.exe", StringComparison.OrdinalIgnoreCase);
	}
}
