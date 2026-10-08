'use strict';

const assert = require('node:assert/strict');
const { once } = require('node:events');
const http = require('node:http');
const { execFile } = require('node:child_process');
const path = require('node:path');
const { promisify } = require('node:util');
const test = require('node:test');
const { ApiError, TaskbarClient, isConnectionError, normalizeUrl, codeWindows, terminalEnvironment } = require('../dist/client');
const skipNetwork = process.env.WPF_TASKBAR_SKIP_NETWORK_TESTS === '1';

async function serverFor(t, handler) {
  const server = http.createServer(handler);
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  t.after(() => server.close());
  return `http://127.0.0.1:${server.address().port}`;
}

test('Code と Insiders だけを候補にし、同名ウィンドウも区別して残す', () => {
  const tasks = ['C:\\VSCode\\Code.exe', '/Code - Insiders.exe', 'WindowsTerminal.exe', 'NotCode.exe']
    .map((moduleFileName, handle) => ({ moduleFileName, handle, title: 'same title', processId: 100 }));
  assert.deepEqual(codeWindows(tasks).map(task => task.handle), [0, 1]);
});

test('既存の WSLENV を保ち、通知先の変数を Windows から WSL に引き継ぐ', () => {
  assert.deepEqual(terminalEnvironment('http://192.0.2.1:5000/', 'abc', 'KEEP/p:WPF_TASKBAR_URL/p::OTHER/u'), {
    WPF_TASKBAR_URL: 'http://192.0.2.1:5000',
    WPF_TASKBAR_SESSION_ID: 'abc',
    WSLENV: 'KEEP/p:OTHER/u:WPF_TASKBAR_URL/u:WPF_TASKBAR_SESSION_ID/u',
  });
  assert.equal(terminalEnvironment('http://localhost:5000', 'abc').WSLENV, 'WPF_TASKBAR_URL/u:WPF_TASKBAR_SESSION_ID/u');
});

test('接続先の不正な URL を通知前に拒否する', () => {
  for (const url of ['file:///etc/passwd', 'ftp://example.com', 'http://user:pass@example.com', 'http://localhost/?x=1', 'http://localhost/#fragment']) {
    assert.throws(() => normalizeUrl(url));
  }
});

test('HTTP 経由で一覧取得・登録・生存通知・削除を実行する', { skip: skipNetwork }, async t => {
  const requests = [];
  const id = '0123456789abcdef0123456789abcdef';
  const url = await serverFor(t, async (request, response) => {
    let body = '';
    for await (const chunk of request) body += chunk;
    requests.push([request.method, request.url, body ? JSON.parse(body) : null]);
    response.setHeader('Content-Type', 'application/json');
    if (request.url === '/tasks') response.end(JSON.stringify({ tasks: [{ handle: 10, processId: 100, title: 'project', moduleFileName: 'Code.exe' }] }));
    else if (request.method === 'POST') response.end(JSON.stringify({ sessionId: id, handle: 10, processId: 100 }));
    else { response.statusCode = 204; response.end(); }
  });
  const client = new TaskbarClient(url);
  const [window] = await client.windows();
  const session = await client.createSession(window);
  await client.setTitle(session.sessionId, '日本語の質問 "続行？" <title>');
  await client.interrupt(session.sessionId);
  await client.renew(session.sessionId);
  await client.remove(session.sessionId);
  assert.deepEqual(requests, [
    ['GET', '/tasks', null],
    ['POST', '/tasks/sessions', { handle: 10, processId: 100 }],
    ['PUT', `/tasks/sessions/${id}/title`, { terminalTitle: '日本語の質問 "続行？" <title>' }],
    ['POST', `/tasks/sessions/${id}/status`, { status: 'interrupted', onlyIfActive: true }],
    ['PUT', `/tasks/sessions/${id}/heartbeat`, null],
    ['DELETE', `/tasks/sessions/${id}`, null],
  ]);
});

test('登録の応答が別ウィンドウや不正な通知 ID を指す場合は拒否する', async () => {
  const client = new TaskbarClient('http://localhost:5000');
  const target = { handle: 10, processId: 100 };
  for (const response of [undefined, {}, { sessionId: 'bad', ...target },
    { sessionId: '0123456789abcdef0123456789abcdef', handle: 20, processId: 100 }]) {
    client.request = async () => response;
    await assert.rejects(client.createSession(target), /有効な通知先/);
  }
});

test('接続エラーだけを未起動として扱い、APIや設定のエラーと区別する', () => {
  for (const code of ['ECONNREFUSED', 'ECONNRESET', 'ECONNABORTED', 'ETIMEDOUT', 'EHOSTUNREACH', 'ENETUNREACH', 'ENOTFOUND', 'EAI_AGAIN']) {
    assert.equal(isConnectionError(Object.assign(new Error('offline'), { code })), true);
  }
  for (const error of [undefined, new Error('invalid JSON'), new ApiError(500, 'server error'), { code: 'EACCES' }]) {
    assert.equal(isConnectionError(error), false);
  }
});

test('登録要求で既存の通知IDを送り、旧本体が異なるIDを返したら登録を片付ける', async () => {
  const client = new TaskbarClient('http://localhost:5000');
  const target = { handle: 10, processId: 100 };
  const id = '0123456789abcdef0123456789abcdef';
  const otherId = 'abcdef0123456789abcdef0123456789';
  const requests = [];
  let responseId = id;
  client.request = async (method, path, body) => {
    requests.push({ method, path, body });
    return { ...target, sessionId: responseId };
  };
  assert.equal((await client.createSession(target, id)).sessionId, id);
  assert.deepEqual(requests[0].body, { ...target, sessionId: id });
  responseId = otherId;
  await assert.rejects(client.createSession(target, id), /本体も更新/);
  assert.equal(requests.at(-1).method, 'DELETE');
  assert.equal(requests.at(-1).path, `/tasks/sessions/${otherId}`);
});

test('HTTP エラーと壊れた応答を成功として扱わない', { skip: skipNetwork }, async t => {
  let status = 404;
  const url = await serverFor(t, (_request, response) => { response.statusCode = status; response.end('not JSON'); });
  const client = new TaskbarClient(url);
  await assert.rejects(client.renew('missing'), error => error instanceof ApiError && error.statusCode === 404);
  status = 200;
  await assert.rejects(client.windows(), /JSON/);
});

test('コンテナの通知スクリプトがセッションへ状態を送り、API 停止で AI を止めない', { skip: skipNetwork || process.platform === 'win32' }, async t => {
  const requests = [];
  const url = await serverFor(t, async (request, response) => {
    let body = '';
    for await (const chunk of request) body += chunk;
    requests.push([request.url, JSON.parse(body)]);
    response.statusCode = requests.length > 1 ? 404 : 200;
    response.end();
  });
  const script = path.join(__dirname, '../scripts/wpftaskbar.py');
  const sessionId = '0123456789abcdef0123456789abcdef';
  const env = { ...process.env, WPF_TASKBAR_URL: url, WPF_TASKBAR_SESSION_ID: sessionId };
  await promisify(execFile)('python3', [script, 'running'], { env });
  const result = await promisify(execFile)('python3', [script, 'completed'], { env });
  assert.match(result.stderr, /通知できません/);
  assert.deepEqual(requests, [
    [`/tasks/sessions/${sessionId}/status`, { status: 'running' }],
    [`/tasks/sessions/${sessionId}/status`, { status: 'completed' }],
  ]);
  await promisify(execFile)('python3', [script, 'running'], { env: { ...env, WPF_TASKBAR_SESSION_ID: '' } });
  assert.equal(requests.length, 2);
  await assert.rejects(promisify(execFile)('python3', [script, 'invalid'], { env }), error => error.code === 2);
});
