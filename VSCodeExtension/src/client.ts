import * as http from 'node:http';
import * as https from 'node:https';

export interface TaskWindow {
  handle: number;
  processId: number;
  title: string;
  moduleFileName: string;
}

export interface Session {
  sessionId: string;
  handle: number;
  processId: number;
}

export class ApiError extends Error {
  constructor(readonly statusCode: number, message: string) {
    super(message);
  }
}

export function isConnectionError(error: unknown): boolean {
  return isRecord(error) && typeof error.code === 'string'
    && ['ECONNREFUSED', 'ECONNRESET', 'ECONNABORTED', 'ETIMEDOUT', 'EHOSTUNREACH', 'ENETUNREACH', 'ENOTFOUND', 'EAI_AGAIN'].includes(error.code);
}

export function normalizeUrl(value: string): string {
  const url = new URL(value);
  if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password || url.search || url.hash) {
    throw new Error('WpfTaskBar の URL には http(s) の URL を指定してください（認証情報・クエリ・フラグメントは使用できません）。');
  }
  return url.href.replace(/\/+$/, '');
}

export function codeWindows(tasks: TaskWindow[]): TaskWindow[] {
  return tasks.filter(task => /(?:^|[\\/])Code(?: - Insiders)?\.exe$/i.test(task.moduleFileName));
}

export function terminalEnvironment(apiUrl: string, sessionId: string, inheritedWslEnv?: string) {
  const names = ['WPF_TASKBAR_URL', 'WPF_TASKBAR_SESSION_ID'];
  const forwarded = (inheritedWslEnv || '').split(':')
    .filter(entry => entry && !names.includes(entry.split('/')[0]));
  return {
    WPF_TASKBAR_URL: normalizeUrl(apiUrl),
    WPF_TASKBAR_SESSION_ID: sessionId,
    // Windows の通常のターミナルから wsl.exe を起動する場合にも引き継ぐ。
    WSLENV: [...forwarded, ...names.map(name => `${name}/u`)].join(':'),
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function isTaskWindow(value: unknown): value is TaskWindow {
  return isRecord(value) && typeof value.handle === 'number' && typeof value.processId === 'number'
    && typeof value.title === 'string' && typeof value.moduleFileName === 'string';
}

export class TaskbarClient {
  readonly url: string;

  constructor(url: string) { this.url = normalizeUrl(url); }

  request(method: string, path: string, body?: object): Promise<unknown> {
    const url = new URL(`${this.url}${path}`);
    const payload = body ? JSON.stringify(body) : undefined;
    return new Promise((resolve, reject) => {
      const request = (url.protocol === 'https:' ? https : http).request(url, {
        method,
        headers: payload ? { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(payload) } : {},
      }, response => {
        let text = '';
        response.setEncoding('utf8');
        response.on('data', chunk => {
          text += chunk;
          if (text.length > 1024 * 1024) request.destroy(new Error('WpfTaskBar の応答が大きすぎます。'));
        });
        response.on('error', reject);
        response.on('end', () => {
          if (!response.statusCode || response.statusCode < 200 || response.statusCode >= 300) {
            reject(new ApiError(response.statusCode || 0, `WpfTaskBar API: HTTP ${response.statusCode}`));
            return;
          }
          try { resolve(text ? JSON.parse(text) : undefined); }
          catch { reject(new Error('WpfTaskBar API が JSON 以外の応答を返しました。')); }
        });
      });
      // 接続前や応答が途切れない場合も含め、全体に期限を設ける。
      const timer = setTimeout(() => request.destroy(Object.assign(
        new Error('WpfTaskBar API に接続できません（5 秒でタイムアウト）。'), { code: 'ETIMEDOUT' })), 5000);
      request.on('close', () => clearTimeout(timer));
      request.on('error', reject);
      request.end(payload);
    });
  }

  async windows(): Promise<TaskWindow[]> {
    const response = await this.request('GET', '/tasks');
    if (!isRecord(response) || !Array.isArray(response.tasks) || !response.tasks.every(isTaskWindow)) {
      throw new Error('WpfTaskBar API の tasks が不正です。');
    }
    return codeWindows(response.tasks);
  }

  async createSession(target: TaskWindow, sessionId?: string): Promise<Session> {
    const session = await this.request('POST', '/tasks/sessions', { handle: target.handle, processId: target.processId, sessionId });
    if (!isRecord(session) || typeof session.sessionId !== 'string' || !/^[a-f0-9]{32}$/.test(session.sessionId)
      || session.handle !== target.handle || session.processId !== target.processId) {
      throw new Error('WpfTaskBar API が有効な通知先を返しませんでした。対応するバージョンの WpfTaskBar を起動してください。');
    }
    if (sessionId && session.sessionId !== sessionId) {
      await this.remove(session.sessionId).catch(() => {});
      throw new Error('同じ通知 ID で再接続するには WpfTaskBar 本体も更新してください。');
    }
    return { sessionId: session.sessionId, handle: session.handle, processId: session.processId };
  }

  async renew(id: string): Promise<void> {
    await this.request('PUT', `/tasks/sessions/${encodeURIComponent(id)}/heartbeat`);
  }

  async setTitle(id: string, terminalTitle: string): Promise<void> {
    await this.request('PUT', `/tasks/sessions/${encodeURIComponent(id)}/title`, { terminalTitle });
  }

  async interrupt(id: string): Promise<void> {
    await this.request('POST', `/tasks/sessions/${encodeURIComponent(id)}/status`, {
      status: 'interrupted', onlyIfActive: true,
    });
  }

  async remove(id: string): Promise<void> {
    await this.request('DELETE', `/tasks/sessions/${encodeURIComponent(id)}`);
  }
}
