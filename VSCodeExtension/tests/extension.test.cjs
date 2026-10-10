'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const clientModule = require('../dist/client');
const settle = () => new Promise(resolve => setImmediate(resolve));

function extension({ cachedEnvironment = [], nextId = 1 } = {}) {
  const commands = new Map();
  const environment = new Map(cachedEnvironment);
  const environmentChanges = [];
  const collection = {
    persistent: true,
    clear: () => environment.clear(),
    get: name => environment.get(name),
    replace: (name, value) => { environmentChanges.push({ name, value }); environment.set(name, { value }); },
  };
  const removed = [];
  const renewed = [];
  const titles = [];
  const interrupted = [];
  const sequences = [];
  const contexts = new Map();
  const errors = [];
  const warnings = [];
  const logs = [];
  const requests = [];
  const timers = new Map();
  let timerId = 0;
  const addTimer = (callback, delay, repeat) => {
    const id = ++timerId;
    timers.set(id, { callback, delay, repeat });
    return id;
  };
  const fireTimer = async repeat => {
    const timer = [...timers].find(([, value]) => value.repeat === repeat);
    if (!timer) return false;
    const [id, { callback, delay }] = timer;
    assert.equal(delay, 1000);
    if (!repeat) timers.delete(id);
    callback();
    await settle();
    return true;
  };
  let activeTerminal;
  let onActiveTerminal;
  let onConfiguration;
  let focused = true;
  let onWindowState;
  let foregroundHandle = 10;
  let foregroundRequests = 0;
  let foregroundError;
  let onForeground;
  let windows = [
    { handle: 10, processId: 100, title: 'project A', moduleFileName: 'Code.exe' },
    { handle: 20, processId: 100, title: 'project B', moduleFileName: 'Code.exe' },
  ];
  let selection = 0;
  const sessions = new Map();
  const changed = [];
  let picks = 0;
  let renewError;
  let createError;
  let windowsError;
  let removeError;
  let ids = nextId - 1;
  let onCreateSession;
  let onRenew;
  let onTitle;
  let onInterrupt;
  let onPick;
  const config = { apiUrl: 'http://localhost:5000', containerApiUrl: 'http://192.0.2.1:5000' };
  class FakeClient {
    constructor(url) { this.url = url; }
    async windows() { if (windowsError) throw windowsError; return windows; }
    async foregroundWindow() {
      foregroundRequests++;
      await onForeground?.();
      if (foregroundError) throw foregroundError;
      if (windowsError) throw windowsError;
      return windows.find(window => window.handle === foregroundHandle);
    }
    async createSession(window, sessionId, requireForeground = false) {
      requests.push({ ...window, apiUrl: this.url, sessionId, requireForeground });
      await onCreateSession?.();
      if (createError) throw createError;
      if (requireForeground && foregroundHandle !== window.handle) throw new clientModule.ApiError(409, 'foreground changed');
      const result = { sessionId, handle: window.handle, processId: window.processId };
      sessions.set(this.url, result);
      return result;
    }
    async getSession() { return sessions.get(this.url); }
    async changeTarget(id, window) {
      changed.push({ id, window });
      if (createError) throw createError;
      const result = { sessionId: id, handle: window.handle, processId: window.processId };
      sessions.set(this.url, result);
      return result;
    }
    async renew(id) {
      renewed.push(id);
      await onRenew?.();
      if (renewError) {
        if (renewError.statusCode === 404) sessions.delete(this.url);
        throw renewError;
      }
      return sessions.get(this.url);
    }
    async interrupt(id) { interrupted.push(id); await onInterrupt?.(); }
    async setTitle(id, title) {
      titles.push({ id, title });
      try { await onTitle?.(); }
      catch (error) { if (error.statusCode === 404) sessions.delete(this.url); throw error; }
    }
    async remove(id) { removed.push(id); if (removeError) throw removeError; sessions.delete(this.url); }
  }
  const vscode = {
    workspace: {
      getConfiguration: () => ({ get: (name, fallback) => config[name] ?? fallback }),
      onDidChangeConfiguration: listener => { onConfiguration = listener; return {}; },
    },
    commands: {
      registerCommand: (name, command) => { commands.set(name, command); return {}; },
      executeCommand: async (name, ...args) => {
        if (name === 'setContext') contexts.set(args[0], args[1]);
        if (name === 'workbench.action.terminal.sendSequence') sequences.push({ terminal: activeTerminal, text: args[0].text });
      },
    },
    window: {
      get state() { return { focused }; },
      onDidChangeWindowState: listener => { onWindowState = listener; return {}; },
      get activeTerminal() { return activeTerminal; },
      onDidChangeActiveTerminal: listener => { onActiveTerminal = listener; return {}; },
      createOutputChannel: () => ({ appendLine: line => logs.push(line), show() {} }),
      showQuickPick: async items => { picks++; await onPick?.(); return items[selection]; },
      showErrorMessage: message => errors.push(message),
      showWarningMessage: message => warnings.push(message),
    },
  };
  const extensionModule = { exports: {} };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../dist/extension.js'), 'utf8'), {
    require: name => name === 'vscode' ? vscode : name === 'node:crypto'
      ? { randomBytes: () => ({ toString: () => `session-${++ids}` }) }
      : { ...clientModule, TaskbarClient: FakeClient },
    module: extensionModule,
    exports: extensionModule.exports,
    process: { platform: 'win32', env: { WSLENV: 'KEEP/p' } },
    setInterval: (callback, delay) => addTimer(callback, delay, true),
    clearInterval: id => timers.delete(id),
    setTimeout: (callback, delay) => addTimer(callback, delay, false),
    clearTimeout: id => timers.delete(id),
  });
  const api = extensionModule.exports;
  const ready = api.activate({ subscriptions: [], environmentVariableCollection: collection });
  return {
    ready, commands, environment, collection, timers, removed, renewed, requests, errors, warnings, config,
    titles, interrupted, sequences, contexts, logs, environmentChanges, changed, sessions,
    get picks() { return picks; },
    get foregroundRequests() { return foregroundRequests; },
    set focused(value) { focused = value; onWindowState?.({ focused }); },
    set foregroundHandle(value) { foregroundHandle = value; },
    set foregroundError(value) { foregroundError = value; },
    set onForeground(value) { onForeground = value; },
    set windows(value) { windows = value; },
    set selection(value) { selection = value; },
    set renewError(value) { renewError = value; },
    set createError(value) { createError = value; },
    set windowsError(value) { windowsError = value; },
    set removeError(value) { removeError = value; },
    set onCreateSession(value) { onCreateSession = value; },
    set onRenew(value) { onRenew = value; },
    set onTitle(value) { onTitle = value; },
    set onInterrupt(value) { onInterrupt = value; },
    set onPick(value) { onPick = value; },
    activateTerminal: async terminal => { activeTerminal = terminal; onActiveTerminal(); await settle(); },
    interrupt: () => commands.get('wpftaskbar.interruptTerminal')(),
    select: () => commands.get('wpftaskbar.selectWindow')(),
    heartbeat: () => fireTimer(false),
    pollTitles: () => fireTimer(true),
    configure: async () => { onConfiguration({ affectsConfiguration: section => section === 'wpftaskbar' }); await settle(); },
    deactivate: api.deactivate,
  };
}

test('フォーカス中の起動では前面ウィンドウへ自動登録し、先に環境変数を設定する', async () => {
  const ext = extension();
  await ext.ready;
  assert.equal(ext.picks, 0);
  assert.deepEqual(ext.requests.map(window => window.handle), [10]);
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-1');
  assert.equal(ext.environment.get('WPF_TASKBAR_URL').value, 'http://192.0.2.1:5000');
  assert.equal(ext.environment.get('WSLENV').value, 'KEEP/p:WPF_TASKBAR_URL/u:WPF_TASKBAR_SESSION_ID/u');
  assert.equal(ext.collection.persistent, true);
  await ext.deactivate();
});

test('通常のターミナルを切り替え、すべて閉じても同じウィンドウセッションを維持する', async () => {
  const ext = extension();
  await ext.ready;
  await ext.activateTerminal({ name: 'bash' });
  await ext.activateTerminal({ name: 'PowerShell' });
  await ext.activateTerminal(undefined);
  await ext.heartbeat();
  assert.equal(ext.requests.length, 1);
  assert.deepEqual(ext.renewed, ['session-1']);
  assert.deepEqual(ext.removed, []);
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-1');
  assert.deepEqual(ext.titles.at(-1), { id: 'session-1', title: '' });
  await ext.deactivate();
  assert.deepEqual(ext.removed, ['session-1']);
  assert.equal(ext.environment.size, 3);
  assert.equal(ext.timers.size, 0);
});

test('VSCode再起動・再読み込み後も復元ターミナルのIDを使い、環境変数を書き換えない', async () => {
  let ext = extension();
  await ext.ready;
  const terminalEnvironment = [...ext.environment];
  for (const nextId of [2, 3]) {
    await ext.deactivate();
    assert.deepEqual(ext.removed, ['session-1']);
    const cachedEnvironment = ext.collection.persistent ? [...ext.environment] : [];
    ext = extension({ cachedEnvironment, nextId });
    ext.foregroundHandle = 20;
    await ext.ready;
    assert.deepEqual(ext.requests.map(request => [request.sessionId, request.handle]), [['session-1', 20]]);
    assert.deepEqual([...ext.environment], terminalEnvironment);
    assert.deepEqual(ext.environmentChanges, []);
    await ext.heartbeat();
    assert.deepEqual(ext.renewed, ['session-1']);
  }
  await ext.deactivate();
  assert.deepEqual([...ext.environment], terminalEnvironment);
});

test('VSCode再起動後に本体が停止中でも保存済みIDを維持し、復帰後に同じIDで登録する', async () => {
  const previous = extension();
  await previous.ready;
  await previous.deactivate();
  const ext = extension({ cachedEnvironment: previous.environment, nextId: 2 });
  ext.windowsError = Object.assign(new Error('offline'), { code: 'ECONNREFUSED' });
  await ext.ready;
  await ext.heartbeat();
  assert.equal(ext.requests.length, 0);
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-1');
  assert.deepEqual(ext.environmentChanges, []);
  ext.windowsError = undefined;
  await ext.heartbeat();
  assert.equal(ext.requests[0].sessionId, 'session-1');
  assert.deepEqual(ext.environmentChanges, []);
  await ext.deactivate();
});

test('別ワークスペースのウィンドウには別のIDを用意する', async () => {
  const first = extension();
  const second = extension({ nextId: 2 });
  await Promise.all([first.ready, second.ready]);
  assert.equal(first.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-1');
  assert.equal(second.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-2');
  await Promise.all([first.deactivate(), second.deactivate()]);
});

test('復元後に通知先URLを変更した場合はIDを維持し、必要な環境変数だけを更新する', async () => {
  const previous = extension();
  await previous.ready;
  await previous.deactivate();
  const ext = extension({ cachedEnvironment: previous.environment, nextId: 2 });
  await ext.ready;
  ext.config.containerApiUrl = 'http://192.0.2.2:5000';
  await ext.configure();
  assert.deepEqual(ext.environmentChanges, [{ name: 'WPF_TASKBAR_URL', value: 'http://192.0.2.2:5000' }]);
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-1');
  await ext.deactivate();
});

test('一覧の先頭や同名・同じPIDではなく、前面のHWNDを登録する', async () => {
  const ext = extension();
  ext.windows = [10, 20].map(handle => ({ handle, processId: 100, title: 'same project', moduleFileName: 'Code.exe' }));
  ext.foregroundHandle = 20;
  await ext.ready;
  assert.equal(ext.picks, 0);
  assert.deepEqual(ext.requests.map(request => [request.handle, request.requireForeground]), [[20, true]]);
  assert.ok(ext.logs.some(line => line.includes('自動登録')));
  await ext.deactivate();
});

test('背景で起動した拡張は登録や選択画面を出さず、フォーカス後に自動登録する', async () => {
  const ext = extension();
  ext.focused = false;
  await ext.ready;
  await ext.heartbeat();
  assert.equal(ext.foregroundRequests, 0);
  assert.equal(ext.requests.length, 0);
  assert.equal(ext.picks, 0);
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-1');
  ext.focused = true;
  ext.foregroundHandle = 20;
  await ext.heartbeat();
  assert.deepEqual(ext.requests.map(request => request.handle), [20]);
  assert.equal(ext.environmentChanges.length, 3);
  await ext.deactivate();
});

for (const returnsFocus of [false, true]) {
  test(`前面取得中にフォーカスが変わったら候補を捨てる（復帰=${returnsFocus}）`, async () => {
    const ext = extension();
    ext.onForeground = () => {
      ext.focused = false;
      if (returnsFocus) ext.focused = true;
    };
    await ext.ready;
    assert.equal(ext.requests.length, 0);
    assert.equal(ext.picks, 0);
    ext.onForeground = undefined;
    ext.focused = true;
    ext.foregroundHandle = 20;
    await ext.heartbeat();
    assert.deepEqual(ext.requests.map(request => request.handle), [20]);
    await ext.deactivate();
  });
}

test('取得後に前面が変わり本体が登録を拒否した場合、古い候補を記憶しない', async () => {
  const ext = extension();
  ext.onCreateSession = () => { ext.foregroundHandle = 20; ext.focused = false; };
  await ext.ready;
  assert.equal(ext.sessions.size, 0);
  assert.equal(ext.errors.length, 0);
  assert.equal(ext.picks, 0);
  ext.onCreateSession = undefined;
  await ext.heartbeat();
  assert.equal(ext.requests.length, 1);
  ext.focused = true;
  await ext.heartbeat();
  assert.deepEqual(ext.requests.map(request => [request.handle, request.requireForeground]), [[10, true], [20, true]]);
  assert.equal(ext.sessions.get(ext.config.apiUrl).handle, 20);
  await ext.deactivate();
});

test('自動登録後のフォーカス移動や本体再起動で通知先を別ウィンドウへ変えない', async () => {
  const ext = extension();
  await ext.ready;
  ext.focused = false;
  ext.foregroundHandle = 20;
  await ext.heartbeat();
  ext.renewError = new clientModule.ApiError(404, 'restarted');
  await ext.heartbeat();
  assert.deepEqual(ext.requests.map(request => [request.handle, request.requireForeground]), [[10, true], [10, false]]);
  assert.equal(ext.foregroundRequests, 1);
  assert.equal(ext.picks, 0);
  await ext.deactivate();
});

test('手動で変更した通知先は自分のウィンドウが前面でも維持する', async () => {
  const ext = extension();
  await ext.ready;
  ext.selection = 1;
  await ext.select();
  ext.focused = false;
  ext.focused = true;
  await ext.heartbeat();
  await ext.configure();
  assert.equal(ext.sessions.get(ext.config.apiUrl).handle, 20);
  assert.equal(ext.changed.length, 1);
  assert.equal(ext.foregroundRequests, 1);
  await ext.deactivate();
});

test('前面取得APIがない本体では手動選択へ戻る', async () => {
  const ext = extension();
  ext.foregroundError = new clientModule.ApiError(404, 'unsupported');
  ext.selection = 1;
  await ext.ready;
  assert.equal(ext.picks, 1);
  assert.deepEqual(ext.requests.map(request => [request.handle, request.requireForeground]), [[20, false]]);
  await ext.deactivate();
});

test('前面取得中のAPI接続先変更では古い接続先へ登録しない', async () => {
  const ext = extension();
  ext.onForeground = () => { ext.config.apiUrl = 'http://localhost:6000'; };
  await ext.ready;
  assert.equal(ext.requests.length, 0);
  ext.onForeground = undefined;
  await ext.heartbeat();
  assert.equal(ext.requests[0].apiUrl, ext.config.apiUrl);
  await ext.deactivate();
});

test('未登録で手動選択中は定期確認が自動登録を割り込ませない', async () => {
  const ext = extension();
  ext.focused = false;
  await ext.ready;
  let release;
  ext.onPick = () => new Promise(resolve => { release = resolve; });
  ext.focused = true;
  ext.selection = 1;
  const selected = ext.select();
  await settle();
  await ext.heartbeat();
  assert.equal(ext.requests.length, 0);
  assert.equal(ext.foregroundRequests, 0);
  release();
  await selected;
  assert.deepEqual(ext.requests.map(request => [request.handle, request.requireForeground]), [[20, false]]);
  await ext.deactivate();
});

test('前面取得中に手動選択をキャンセルした場合も遅れた応答で自動登録しない', async () => {
  const ext = extension();
  let release;
  ext.onForeground = () => new Promise(resolve => { release = resolve; });
  await settle();
  ext.selection = -1;
  await ext.select();
  release();
  await ext.ready;
  await ext.heartbeat();
  assert.equal(ext.picks, 1);
  assert.equal(ext.requests.length, 0);
  assert.equal(ext.foregroundRequests, 1);
  await ext.deactivate();
});

test('自動判定できず手動選択をキャンセルすると未登録で待機する', async () => {
  const ext = extension();
  ext.foregroundHandle = 0;
  ext.selection = -1;
  await ext.ready;
  ext.foregroundHandle = 10;
  ext.focused = false;
  ext.focused = true;
  await ext.heartbeat();
  await ext.heartbeat();
  assert.equal(ext.picks, 1);
  assert.equal(ext.requests.length, 0);
  assert.equal(ext.contexts.get('wpftaskbar.sessionActive'), false);
  ext.selection = 1;
  await ext.select();
  assert.deepEqual(ext.requests.map(window => window.handle), [20]);
  assert.equal(ext.environmentChanges.length, 3);
  await ext.deactivate();
});

test('登録済みの通知先の選び直しをキャンセルしても背景で同じ通知先へ再接続する', async () => {
  const ext = extension();
  await ext.ready;
  ext.selection = -1;
  await ext.select();
  ext.focused = false;
  ext.foregroundHandle = 20;
  ext.renewError = new clientModule.ApiError(404, 'restarted');
  await ext.heartbeat();
  assert.deepEqual(ext.requests.map(request => request.handle), [10, 10]);
  assert.equal(ext.foregroundRequests, 1);
  assert.equal(ext.environmentChanges.length, 3);
  await ext.deactivate();
});

test('前面の確認ができない場合は候補が1つでも手動選択する', async () => {
  const ext = extension();
  ext.windows = [{ handle: 20, processId: 100, title: 'only window', moduleFileName: 'Code.exe' }];
  ext.selection = -1;
  await ext.ready;
  assert.equal(ext.picks, 1);
  assert.equal(ext.requests.length, 0);
  await ext.deactivate();
});

test('タスクバー側の変更を生存通知で取り込み、本体再起動後も同じIDと新しい通知先を使う', async () => {
  const ext = extension();
  await ext.ready;
  ext.sessions.set(ext.config.apiUrl, { sessionId: 'session-1', handle: 20, processId: 100 });
  await ext.heartbeat();
  ext.renewError = new clientModule.ApiError(404, 'restarted');
  await ext.heartbeat();
  assert.deepEqual(ext.requests.map(window => window.handle), [10, 20]);
  assert.deepEqual(ext.requests.map(window => window.sessionId), ['session-1', 'session-1']);
  assert.equal(ext.picks, 0);
  assert.equal(ext.changed.length, 0);
  assert.equal(ext.environmentChanges.length, 3);
  await ext.deactivate();
});

test('再接続時にも本体側の通知先を読み、古い紐付けで上書きしない', async () => {
  const ext = extension();
  await ext.ready;
  ext.onTitle = () => { throw new clientModule.ApiError(404, 'expired'); };
  await ext.activateTerminal({ name: 'Codex' });
  ext.onTitle = undefined;
  ext.sessions.set(ext.config.apiUrl, { sessionId: 'session-1', handle: 20, processId: 100 });
  await ext.heartbeat();
  assert.equal(ext.requests.length, 1);
  assert.equal(ext.changed.length, 0);
  assert.equal(ext.picks, 0);
  assert.equal(ext.sessions.get(ext.config.apiUrl).handle, 20);
  await ext.deactivate();
});

test('起動時の接続失敗から1秒後に再試行し、接続前にターミナルへ渡したIDで登録する', async () => {
  const ext = extension();
  ext.createError = new Error('API unavailable');
  await ext.ready;
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-1');
  assert.equal(ext.errors.length, 0);
  ext.createError = undefined;
  await ext.heartbeat();
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-1');
  assert.deepEqual(ext.requests.map(request => request.sessionId), ['session-1', 'session-1']);
  assert.equal(ext.environmentChanges.length, 3);
  await ext.deactivate();
});

test('候補がまだない場合はIDだけ先に渡し、ウィンドウが現れたら登録する', async () => {
  const ext = extension();
  ext.windows = [];
  await ext.ready;
  assert.equal(ext.requests.length, 0);
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-1');
  assert.equal(ext.errors.length, 0);
  ext.windows = [{ handle: 10, processId: 100, title: 'project A', moduleFileName: 'Code.exe' }];
  await ext.heartbeat();
  assert.equal(ext.requests[0].sessionId, 'session-1');
  await ext.deactivate();
});

test('未起動時の一覧取得・手動選択・設定変更・定期再試行で接続エラーを表示しない', async () => {
  const ext = extension();
  ext.windowsError = Object.assign(new Error('connect ECONNREFUSED 127.0.0.1:5000'), { code: 'ECONNREFUSED' });
  await ext.ready;
  await ext.select();
  await ext.configure();
  await ext.heartbeat();
  await ext.heartbeat();
  assert.equal(ext.requests.length, 0);
  assert.equal(ext.errors.length, 0);
  assert.equal(ext.warnings.length, 0);
  assert.equal(ext.logs.length, 1);
  assert.ok(!ext.logs[0].includes('ECONNREFUSED'));
  ext.windowsError = undefined;
  await ext.heartbeat();
  assert.equal(ext.requests[0].sessionId, 'session-1');
  assert.equal(ext.environmentChanges.length, 3);
  await ext.deactivate();
});

test('停止中のタイトル再送を生存確認まで待ち、復帰後に最新タイトルを送る', async () => {
  const ext = extension();
  await ext.ready;
  ext.onTitle = () => { throw Object.assign(new Error('connection reset'), { code: 'ECONNRESET' }); };
  await ext.activateTerminal({ name: 'offline' });
  const count = ext.titles.length;
  await ext.pollTitles();
  await ext.activateTerminal({ name: 'latest' });
  assert.equal(ext.titles.length, count);
  ext.onTitle = undefined;
  await ext.heartbeat();
  assert.deepEqual(ext.titles.at(-1), { id: 'session-1', title: 'latest' });
  assert.equal(ext.requests.length, 1);
  assert.equal(ext.errors.length, 0);
  await ext.deactivate();
});

test('停止中のEscと終了時の登録削除も接続エラーを表示しない', async () => {
  const ext = extension();
  await ext.ready;
  await ext.activateTerminal({ name: 'bash' });
  const offline = Object.assign(new Error('connect ECONNREFUSED 127.0.0.1:5000'), { code: 'ECONNREFUSED' });
  ext.onInterrupt = () => { throw offline; };
  await ext.interrupt();
  assert.equal(ext.sequences.length, 1);
  assert.equal(ext.errors.length, 0);
  ext.removeError = offline;
  await ext.deactivate();
  assert.ok(ext.logs.every(line => !line.includes('ECONNREFUSED')));
});

test('手動選択で同じIDの通知先を変更し、キャンセル・同じ通知先の再選択では変更しない', async () => {
  const ext = extension();
  await ext.ready;
  await ext.activateTerminal({ name: 'bash' });
  ext.selection = 1;
  await ext.select();
  assert.deepEqual(ext.changed.map(change => change.window.handle), [20]);
  assert.deepEqual(ext.removed, []);
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-1');
  assert.equal(ext.warnings.length, 0);
  ext.selection = -1;
  await ext.select();
  ext.selection = 1;
  await ext.select();
  assert.equal(ext.requests.length, 1);
  assert.equal(ext.changed.length, 1);
  assert.equal(ext.environmentChanges.length, 3);
  assert.equal(ext.picks, 3);
  await ext.deactivate();
});

test('通知先の変更に失敗しても現在のセッションと環境変数を維持する', async () => {
  const ext = extension();
  await ext.ready;
  ext.createError = new Error('API unavailable');
  ext.selection = 1;
  await ext.select();
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-1');
  assert.deepEqual(ext.removed, []);
  assert.equal(ext.errors.length, 1);
  await ext.deactivate();
});

test('通知先の選択画面を開いている間も生存通知を続ける', async () => {
  const ext = extension();
  await ext.ready;
  let release;
  ext.onPick = () => new Promise(resolve => { release = resolve; });
  ext.selection = 1;
  const selected = ext.select();
  await settle();
  await ext.heartbeat();
  assert.deepEqual(ext.renewed, ['session-1']);
  release();
  await selected;
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-1');
  await ext.deactivate();
});

test('接続中も1秒間隔で本体の停止・再起動を検知し、同じウィンドウとIDで再登録する', async () => {
  const ext = extension();
  await ext.ready;
  ext.renewError = Object.assign(new Error('connect ECONNREFUSED 127.0.0.1:5000'), { code: 'ECONNREFUSED' });
  await ext.heartbeat();
  assert.equal(ext.requests.length, 1);
  ext.renewError = new clientModule.ApiError(404, 'expired');
  await ext.heartbeat();
  assert.deepEqual(ext.requests.map(window => window.handle), [10, 10]);
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-1');
  assert.equal(ext.environmentChanges.length, 3);
  assert.equal(ext.warnings.length, 0);
  assert.equal(ext.errors.length, 0);
  ext.renewError = undefined;
  await ext.heartbeat();
  assert.deepEqual(ext.renewed, ['session-1', 'session-1', 'session-1']);
  assert.deepEqual(ext.requests.map(request => request.sessionId), ['session-1', 'session-1']);
  await ext.deactivate();
});

test('元のウィンドウが消えた場合は先頭の別ウィンドウへ通知を転送しない', async () => {
  const ext = extension();
  await ext.ready;
  ext.windows = [{ handle: 10, processId: 999, title: 'reused handle', moduleFileName: 'Code.exe' }];
  ext.renewError = new clientModule.ApiError(404, 'expired');
  await ext.heartbeat();
  await ext.heartbeat();
  assert.equal(ext.requests.length, 1);
  assert.equal(ext.environmentChanges.length, 3);
  assert.equal(ext.warnings.length, 0);
  await ext.deactivate();
});

test('コンテナURL変更ではIDを維持し、API変更では旧登録を解除する', async () => {
  const ext = extension();
  await ext.ready;
  ext.config.containerApiUrl = 'http://192.0.2.2:5000';
  await ext.configure();
  assert.equal(ext.requests.length, 1);
  assert.equal(ext.environment.get('WPF_TASKBAR_URL').value, ext.config.containerApiUrl);
  ext.config.apiUrl = 'http://localhost:6000';
  await ext.configure();
  assert.equal(ext.requests[1].apiUrl, ext.config.apiUrl);
  assert.deepEqual(ext.removed, ['session-1']);
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-1');
  await ext.deactivate();
});

test('起動中の生存通知・設定変更でもセッションを重複登録しない', async () => {
  const ext = extension();
  let release;
  ext.onCreateSession = () => new Promise(resolve => { release = resolve; });
  await settle();
  assert.equal(await ext.heartbeat(), false);
  await ext.configure();
  assert.equal(ext.requests.length, 1);
  release();
  await ext.ready;
  await settle();
  assert.equal(ext.requests.length, 1);
  assert.deepEqual(ext.renewed, []);
  assert.equal(await ext.heartbeat(), true);
  assert.deepEqual(ext.renewed, ['session-1']);
  await ext.deactivate();
});

test('生存確認の応答待ち中は次の確認を予約せず、完了の1秒後に再開する', async () => {
  const ext = extension();
  await ext.ready;
  let release;
  ext.onRenew = () => new Promise(resolve => { release = resolve; });
  assert.equal(await ext.heartbeat(), true);
  for (let i = 0; i < 10; i++) assert.equal(await ext.heartbeat(), false);
  assert.deepEqual(ext.renewed, ['session-1']);
  ext.onRenew = undefined;
  release();
  await settle();
  assert.deepEqual(ext.renewed, ['session-1']);
  assert.equal(await ext.heartbeat(), true);
  assert.deepEqual(ext.renewed, ['session-1', 'session-1']);
  assert.equal(ext.requests.length, 1);
  await ext.deactivate();
});

test('生存確認がタイムアウトしても1秒後に静かに再試行する', async () => {
  const ext = extension();
  await ext.ready;
  ext.renewError = Object.assign(new Error('timeout'), { code: 'ETIMEDOUT' });
  assert.equal(await ext.heartbeat(), true);
  assert.equal(await ext.heartbeat(), true);
  assert.equal(ext.errors.length, 0);
  assert.equal(ext.logs.filter(line => line.includes('起動・接続を待っています')).length, 1);
  ext.renewError = undefined;
  assert.equal(await ext.heartbeat(), true);
  assert.equal(ext.renewed.length, 3);
  assert.equal(ext.requests.length, 1);
  await ext.deactivate();
});

test('生存確認中に拡張を停止した場合は処理完了後もタイマーを再開しない', async () => {
  const ext = extension();
  await ext.ready;
  let release;
  ext.onRenew = () => new Promise(resolve => { release = resolve; });
  await ext.heartbeat();
  const stopped = ext.deactivate();
  assert.equal(ext.timers.size, 0);
  release();
  await stopped;
  await settle();
  assert.equal(ext.timers.size, 0);
  assert.equal(await ext.heartbeat(), false);
  assert.equal(ext.environment.size, 3);
});

test('登録処理中に拡張が停止しても遅れて返されたセッションを残さない', async () => {
  const ext = extension();
  let release;
  ext.onCreateSession = () => new Promise(resolve => { release = resolve; });
  await settle();
  const stopped = ext.deactivate();
  release();
  await Promise.all([stopped, ext.ready]);
  assert.deepEqual(ext.removed, ['session-1']);
  assert.equal(ext.environment.size, 3);
  assert.equal(ext.timers.size, 0);
});

test('アクティブなターミナルのタイトルを共有セッションへ差分通知する', async () => {
  const ext = extension();
  await ext.ready;
  const terminal = { name: '質問しますか？' };
  await ext.activateTerminal(terminal);
  await ext.pollTitles();
  assert.deepEqual(ext.titles.map(value => value.title), ['', terminal.name]);
  terminal.name = 'a'.repeat(5000);
  await ext.pollTitles();
  assert.equal(ext.titles.at(-1).title.length, 4096);
  await ext.activateTerminal({ name: 'second' });
  assert.deepEqual(ext.titles.at(-1), { id: 'session-1', title: 'second' });
  await ext.deactivate();
});

test('タイトル通知中の変更を順に送り、通信失敗は次の監視で再送する', async () => {
  const ext = extension();
  await ext.ready;
  let release;
  ext.onTitle = () => new Promise(resolve => { release = resolve; });
  await ext.activateTerminal({ name: 'first' });
  await ext.activateTerminal({ name: 'latest' });
  ext.onTitle = undefined;
  release();
  await settle();
  assert.deepEqual(ext.titles.slice(-2).map(value => value.title), ['first', 'latest']);
  ext.onTitle = () => { throw new Error('offline'); };
  await ext.activateTerminal({ name: 'retry' });
  ext.onTitle = undefined;
  await ext.pollTitles();
  assert.deepEqual(ext.titles.slice(-2).map(value => value.title), ['retry', 'retry']);
  await ext.deactivate();
});

test('タイトルの404で通知を出さず、次の生存確認で同じIDを再登録する', async () => {
  const ext = extension();
  await ext.ready;
  ext.onTitle = () => { throw new clientModule.ApiError(404, 'expired'); };
  await ext.activateTerminal({ name: 'expired' });
  await ext.pollTitles();
  assert.equal(ext.environmentChanges.length, 3);
  assert.equal(ext.contexts.get('wpftaskbar.sessionActive'), false);
  assert.equal(ext.warnings.length, 0);
  ext.onTitle = undefined;
  await ext.heartbeat();
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-1');
  assert.equal(ext.contexts.get('wpftaskbar.sessionActive'), true);
  await ext.deactivate();
});

test('通常のターミナルへEscを先に1回送り、ウィンドウ共通のセッションを中断する', async () => {
  const ext = extension();
  await ext.ready;
  const terminal = { name: 'bash' };
  await ext.activateTerminal(terminal);
  assert.equal(ext.contexts.get('wpftaskbar.sessionActive'), true);
  ext.onInterrupt = () => {
    assert.equal(ext.sequences.at(-1).terminal, terminal);
    assert.equal(ext.sequences.at(-1).text, '\u001b');
    throw new Error('API offline');
  };
  await ext.interrupt();
  assert.deepEqual(ext.interrupted, ['session-1']);
  assert.equal(ext.sequences.length, 1);
  assert.equal(ext.errors.length, 1);
  await ext.activateTerminal(undefined);
  assert.equal(ext.contexts.get('wpftaskbar.sessionActive'), false);
  await ext.interrupt();
  assert.deepEqual(ext.interrupted, ['session-1']);
  await ext.deactivate();
});

test('起動時の有効化を宣言し、専用ターミナル・プロファイル・シェル設定を公開しない', async () => {
  const manifest = require('../package.json');
  assert.ok(manifest.activationEvents.includes('*'));
  assert.equal(manifest.contributes.terminal, undefined);
  assert.ok(!manifest.contributes.commands.some(value => value.command === 'wpftaskbar.openTerminal'));
  for (const key of ['wpftaskbar.shellPath', 'wpftaskbar.shellArgs']) {
    assert.equal(manifest.contributes.configuration.properties[key], undefined);
  }
  const ext = extension();
  await ext.ready;
  assert.equal(ext.commands.has('wpftaskbar.openTerminal'), false);
  const binding = manifest.contributes.keybindings.find(value => value.command === 'wpftaskbar.interruptTerminal');
  assert.equal(binding.key, 'escape');
  for (const condition of ['terminalFocus', 'wpftaskbar.sessionActive', '!terminalFindVisible', '!terminalSuggestWidgetVisible', '!terminalAccessibleBufferFocus']) {
    assert.ok(binding.when.split(' && ').includes(condition));
  }
  await ext.deactivate();
});
