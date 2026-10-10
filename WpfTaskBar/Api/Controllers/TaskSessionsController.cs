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
		if (request.SessionId != null && (request.SessionId.Length != 32 ||
			request.SessionId.Any(character => !(character is >= 'a' and <= 'f' or >= '0' and <= '9'))))
			return BadRequest(new { message = "sessionId は32文字の小文字16進数を指定してください。" });

		var target = windows.GetWindows().FirstOrDefault(window =>
			window.Handle == request.Handle && window.ProcessId == request.ProcessId);
		if (target == null || !target.IsVsCode())
			return NotFound(new { message = "対象の VSCode ウィンドウが見つかりません。" });

		var session = request.SessionId == null
			? statuses.CreateSession(target.Handle, target.ProcessId)
			: statuses.RegisterSession(target.Handle, target.ProcessId, request.SessionId);
		if (session == null) return Conflict(new { message = "sessionId は別の通知先に登録されています。" });
		return Ok(new { sessionId = session.Id, handle = target.Handle, processId = target.ProcessId });
	}

	[HttpGet("tasks/sessions/{id}")]
	public IActionResult GetSession(string id)
	{
		var session = statuses.GetSession(id);
		if (session == null) return SessionNotFound();
		if (!windows.GetWindows().Any(window =>
			window.Handle == session.Handle && window.ProcessId == session.ProcessId && window.IsVsCode()))
		{
			statuses.RemoveSession(id, session);
			return SessionNotFound();
		}
		return SessionResponse(session);
	}

	[HttpPut("tasks/sessions/{id}/target")]
	public IActionResult ChangeTarget(string id, [FromBody] TaskSessionTargetRequest request)
	{
		if (request.Handle == 0 || request.ProcessId <= 0)
			return BadRequest(new { message = "handle と processId を指定してください。" });
		if (!windows.GetWindows().Any(window =>
			window.Handle == request.Handle && window.ProcessId == request.ProcessId && window.IsVsCode()))
			return NotFound(new { message = "対象の VSCode ウィンドウが見つかりません。" });
		var session = statuses.ReassignSession(id, request.Handle, request.ProcessId);
		return session == null ? SessionNotFound() : SessionResponse(session);
	}

	[HttpPost("tasks/sessions/{id}/status")]
	public IActionResult SetStatus(string id, [FromBody] TaskSessionStatusRequest request)
	{
		if (!TaskStatusStore.IsValidStatus(request.Status))
			return BadRequest(new { message = "status は running、waiting、interrupted、completed、none のいずれかを指定してください。" });
		if (request.ActivityText?.Length > 512)
			return BadRequest(new { message = "activityText は512文字以内の文字列を指定してください。" });

		var session = statuses.GetSession(id);
		if (session == null) return SessionNotFound();
		var target = windows.GetWindows().FirstOrDefault(window =>
			window.Handle == session.Handle && window.ProcessId == session.ProcessId && window.IsVsCode());
		if (target == null)
		{
			statuses.RemoveSession(id, session);
			return SessionNotFound();
		}
		if (!statuses.SetSessionStatus(id, request.Status, request.OnlyIfActive, request.ActivityText)) return SessionNotFound();
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
			window.Handle == session.Handle && window.ProcessId == session.ProcessId && window.IsVsCode()))
		{
			statuses.RemoveSession(id, session);
			return SessionNotFound();
		}
		return statuses.SetSessionTitle(id, request.TerminalTitle) ? NoContent() : SessionNotFound();
	}

	[HttpPut("tasks/sessions/{id}/activity")]
	public IActionResult SetActivity(string id, [FromBody] TaskSessionActivityRequest request)
	{
		if (request.ActivityText == null || request.ActivityText.Length > 512)
			return BadRequest(new { message = "activityText は512文字以内の文字列を指定してください。" });
		var session = statuses.GetSession(id);
		if (session == null) return SessionNotFound();
		if (!windows.GetWindows().Any(window =>
			window.Handle == session.Handle && window.ProcessId == session.ProcessId && window.IsVsCode()))
		{
			statuses.RemoveSession(id, session);
			return SessionNotFound();
		}
		return statuses.SetSessionActivity(id, request.ActivityText) ? NoContent() : SessionNotFound();
	}

	[HttpPut("tasks/sessions/{id}/heartbeat")]
	public IActionResult Heartbeat(string id)
	{
		var session = statuses.GetSession(id);
		if (session == null) return SessionNotFound();
		if (!windows.GetWindows().Any(window =>
			window.Handle == session.Handle && window.ProcessId == session.ProcessId && window.IsVsCode()))
		{
			statuses.RemoveSession(id, session);
			return SessionNotFound();
		}
		if (!statuses.RenewSession(id)) return SessionNotFound();
		// タスクバー側の変更を拡張にも返し、再接続時に古い HWND へ戻らないようにする。
		var current = statuses.GetSession(id);
		return current == null ? SessionNotFound() : SessionResponse(current);
	}

	private OkObjectResult SessionResponse(TaskStatusSession session) =>
		Ok(new { sessionId = session.Id, handle = session.Handle, processId = session.ProcessId });

	private NotFoundObjectResult SessionNotFound() =>
		NotFound(new { message = "通知先のセッションがありません。VSCode 拡張で通知先を選び直してください。" });

}
