'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const clientModule = require('../dist/client');

function cancellation() {
  const listeners = new Set();
  return {
    isCancellationRequested: false,
    onCancellationRequested(listener) {
      listeners.add(listener);
      return { dispose: () => listeners.delete(listener) };
    },
    cancel() {
      this.isCancellationRequested = true;
      for (const listener of listeners) listener();
    },
  };
}

function extension() {
  const commands = new Map();
  const providers = new Map();
  const created = [];
  const removed = [];
  const renewed = [];
  const errors = [];
  const warnings = [];
  const requests = [];
  let windows = [
    { handle: 10, processId: 100, title: 'project A', moduleFileName: 'Code.exe' },
    { handle: 20, processId: 100, title: 'project B', moduleFileName: 'Code.exe' },
  ];
  let selection = 1;
  let picks = 0;
  let onClose;
  let onOpen;
  let onHeartbeat;
  let now = 0;
  let failRenew = false;
  let failCreate = false;
  let createError;
  let onCreateSession;
  let onPick;
  let openEventBeforeReturn = true;
  const config = { apiUrl: 'http://localhost:5000', containerApiUrl: 'http://192.0.2.1:5000' };
  class FakeClient {
    constructor(url) { this.url = url; }
    async windows() { return windows; }
    async createSession(window) {
      requests.push(window);
      const sessionId = `session-${requests.length}`;
      await onCreateSession?.();
      if (createError) throw createError;
      return { sessionId, handle: window.handle, processId: window.processId };
    }
    async renew(id) { renewed.push(id); if (failRenew) throw new clientModule.ApiError(404, 'missing'); }
    async remove(id) { removed.push(id); }
  }
  const vscode = {
    TerminalProfile: class { constructor(options) { this.options = options; } },
    workspace: { getConfiguration: () => ({ get: (name, fallback) => config[name] ?? fallback }) },
    commands: { registerCommand: (name, command) => { commands.set(name, command); return {}; } },
    window: {
      createOutputChannel: () => ({ appendLine() {}, show() {} }),
      showQuickPick: async (items, _options, token) => {
        picks++;
        await onPick?.(token);
        return token?.isCancellationRequested ? undefined : items[selection];
      },
      createTerminal: options => {
        if (failCreate) throw new Error('terminal creation failed');
        const terminal = { options, creationOptions: options, show() {} };
        created.push(terminal);
        if (openEventBeforeReturn) onOpen(terminal);
        return terminal;
      },
      registerTerminalProfileProvider: (id, provider) => { providers.set(id, provider); return {}; },
      onDidOpenTerminal: listener => { onOpen = listener; return {}; },
      onDidCloseTerminal: listener => { onClose = listener; return {}; },
      showErrorMessage: message => errors.push(message),
      showWarningMessage: message => warnings.push(message),
    },
  };
  const extensionModule = { exports: {} };
  const sandbox = {
    require: name => name === 'vscode' ? vscode : { ...clientModule, TaskbarClient: FakeClient },
    module: extensionModule,
    exports: extensionModule.exports,
    process: { platform: 'win32', env: { WSLENV: 'KEEP/p' } },
    setInterval: callback => { onHeartbeat = callback; return 1; },
    clearInterval() {},
    Date: { now: () => now },
  };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../dist/extension.js'), 'utf8'), sandbox);
  const api = sandbox.module.exports;
  api.activate({ subscriptions: [] });
  return {
    created, removed, renewed, requests, errors, warnings, config, providers,
    get picks() { return picks; },
    set windows(value) { windows = value; },
    set selection(value) { selection = value; },
    set failCreate(value) { failCreate = value; },
    set failRenew(value) { failRenew = value; },
    set createError(value) { createError = value; },
    set onCreateSession(value) { onCreateSession = value; },
    set onPick(value) { onPick = value; },
    set openEventBeforeReturn(value) { openEventBeforeReturn = value; },
    open: () => commands.get('wpftaskbar.openTerminal')(),
    profile: (token = cancellation()) => providers.get('wpftaskbar.aiTerminal').provideTerminalProfile(token),
    launchProfile: profile => {
      const terminal = { creationOptions: profile.options };
      onOpen(terminal);
      return terminal;
    },
    opened: terminal => onOpen(terminal),
    select: () => commands.get('wpftaskbar.selectWindow')(),
    close: terminal => onClose(terminal),
    heartbeat: async () => { onHeartbeat(); await new Promise(resolve => setImmediate(resolve)); },
    advanceTime: milliseconds => { now += milliseconds; },
    deactivate: api.deactivate,
  };
}

test('複数ウィンドウでは選択した HWND に対応する独立した通知付きターミナルを作る', async () => {
  const ext = extension();
  await ext.open();
  await ext.open();
  assert.equal(ext.picks, 1);
  assert.deepEqual(ext.requests.map(window => window.handle), [20, 20]);
  assert.deepEqual(ext.created.map(terminal => terminal.options.env.WPF_TASKBAR_SESSION_ID), ['session-1', 'session-2']);
  assert.equal(ext.created[0].options.env.WPF_TASKBAR_URL, 'http://192.0.2.1:5000');
  assert.equal(ext.created[0].options.env.WSLENV, 'KEEP/p:WPF_TASKBAR_URL/u:WPF_TASKBAR_SESSION_ID/u');
  await ext.deactivate();
});

test('選択のキャンセル時にはターミナルもセッションも作成しない', async () => {
  const ext = extension();
  ext.selection = -1;
  await ext.open();
  assert.equal(ext.requests.length, 0);
  assert.equal(ext.created.length, 0);
});

test('通知付きターミナルだけを更新し、閉じたターミナルだけを解除する', async () => {
  const ext = extension();
  await ext.open();
  await ext.open();
  ext.close({ unrelated: true });
  ext.close(ext.created[0]);
  await ext.heartbeat();
  assert.deepEqual(ext.removed, ['session-1']);
  assert.deepEqual(ext.renewed, ['session-2']);
  await ext.deactivate();
  assert.deepEqual(ext.removed, ['session-1', 'session-2']);
});

test('通知先が失効したときは別ウィンドウに転送せず再作成を案内する', async () => {
  const ext = extension();
  await ext.open();
  ext.failRenew = true;
  await ext.heartbeat();
  await ext.heartbeat();
  assert.equal(ext.warnings.length, 1);
  assert.deepEqual(ext.renewed, ['session-1']);
  assert.equal(ext.requests.length, 1);
});

test('登録後にターミナル作成が失敗したらセッションを解除する', async () => {
  const ext = extension();
  ext.failCreate = true;
  await ext.open();
  assert.deepEqual(ext.removed, ['session-1']);
  assert.equal(ext.errors.length, 1);
});

test('API 登録失敗では通知不能なターミナルを作らない', async () => {
  const ext = extension();
  ext.createError = new Error('API unavailable');
  await ext.open();
  assert.equal(ext.created.length, 0);
  assert.equal(ext.errors.length, 1);
});

test('作成済みターミナルがある場合は通知先を変更しない', async () => {
  const ext = extension();
  await ext.open();
  await ext.select();
  assert.equal(ext.errors.length, 1);
  assert.equal(ext.picks, 1);
  await ext.deactivate();
});

test('保存した HWND が別プロセスになった場合は通知先を選び直す', async () => {
  const ext = extension();
  await ext.open();
  ext.windows = [
    { handle: 20, processId: 999, title: 'new window', moduleFileName: 'Code.exe' },
    { handle: 30, processId: 999, title: 'other window', moduleFileName: 'Code.exe' },
  ];
  await ext.open();
  assert.equal(ext.picks, 2);
  assert.deepEqual(ext.requests.map(window => window.handle), [20, 30]);
  await ext.deactivate();
});

test('標準 UI から選択できるプロファイルを宣言し、その ID のプロバイダーを登録する', async () => {
  const manifest = require('../package.json');
  const ext = extension();
  const profile = manifest.contributes.terminal.profiles.find(profile => profile.id === 'wpftaskbar.aiTerminal');
  assert.equal(profile.title, 'AI (WpfTaskBar)');
  assert.ok(ext.providers.has(profile.id));
  await ext.deactivate();
});

test('プロファイルの同時起動でも別々の ID を渡し、開いた順序によらず終了・更新を紐付ける', async () => {
  const ext = extension();
  const [first, second] = await Promise.all([ext.profile(), ext.profile()]);
  assert.equal(ext.picks, 1);
  assert.deepEqual([first, second].map(profile => profile.options.env.WPF_TASKBAR_SESSION_ID), ['session-1', 'session-2']);
  assert.equal(first.options.env.WPF_TASKBAR_URL, 'http://192.0.2.1:5000');
  assert.equal(first.options.env.WSLENV, 'KEEP/p:WPF_TASKBAR_URL/u:WPF_TASKBAR_SESSION_ID/u');
  assert.equal(first.options.shellPath, undefined);
  assert.equal(first.options.shellArgs, undefined);
  assert.equal(ext.created.length, 0); // 実際の作成は VSCode が行う。
  ext.launchProfile(second);
  const terminal = ext.launchProfile(first);
  ext.opened(terminal); // 同じイベントを再処理しても登録を重複させない。
  ext.opened({ creationOptions: {} });
  ext.opened({ creationOptions: { pty: {} } });
  ext.opened({ creationOptions: { env: { WPF_TASKBAR_SESSION_ID: 'unrelated' } } });
  await ext.open(); // 従来のコマンドとも共存する。
  ext.close(terminal);
  await ext.heartbeat();
  assert.deepEqual(ext.requests.map(window => window.handle), [20, 20, 20]);
  assert.deepEqual(ext.removed, ['session-1']);
  assert.deepEqual(ext.renewed, ['session-2', 'session-3']);
  await ext.deactivate();
  assert.deepEqual(ext.removed, ['session-1', 'session-2', 'session-3']);
});

test('指定したシェルと引数をプロファイルと専用コマンドの両方で使用する', async () => {
  const ext = extension();
  ext.config.shellPath = 'wsl.exe';
  ext.config.shellArgs = ['-d', 'Ubuntu'];
  const profile = await ext.profile();
  await ext.open();
  for (const options of [profile.options, ext.created[0].options]) {
    assert.equal(options.shellPath, 'wsl.exe');
    assert.deepEqual(options.shellArgs, ['-d', 'Ubuntu']);
  }
  await ext.deactivate();
});

test('専用コマンドの作成通知が後から届いても生存通知を一度だけ送る', async () => {
  const ext = extension();
  ext.openEventBeforeReturn = false;
  await ext.open();
  await ext.heartbeat();
  ext.opened(ext.created[0]);
  await ext.heartbeat();
  assert.deepEqual(ext.renewed, ['session-1', 'session-1']);
  ext.close(ext.created[0]);
  assert.deepEqual(ext.removed, ['session-1']);
  await ext.deactivate();
});

test('プロファイルで通知先選択をキャンセルしたときや API が失敗したときは起動設定を返さない', async () => {
  const ext = extension();
  ext.selection = -1;
  assert.equal(await ext.profile(), undefined);
  assert.equal(ext.requests.length, 0);
  ext.selection = 0;
  ext.createError = new Error('API unavailable');
  assert.equal(await ext.profile(), undefined);
  assert.equal(ext.errors.length, 1);
  assert.match(ext.errors[0], /API unavailable/);
  ext.createError = undefined;
  assert.ok(await ext.profile()); // 失敗が次の起動を妨げない。
  await ext.deactivate();
});

test('起動前にキャンセルされたプロファイルはセッションを発行しない', async () => {
  const ext = extension();
  const token = cancellation();
  token.cancel();
  assert.equal(await ext.profile(token), undefined);
  assert.equal(ext.requests.length, 0);
  assert.equal(ext.picks, 0);
  await ext.deactivate();
});

test('プロファイルのキャンセルを通知先の選択にも伝えて登録せず終了する', async () => {
  const ext = extension();
  const token = cancellation();
  ext.onPick = received => { assert.equal(received, token); token.cancel(); };
  assert.equal(await ext.profile(token), undefined);
  assert.equal(ext.requests.length, 0);
  assert.equal(ext.errors.length, 0);
  await ext.deactivate();
});

test('セッション発行中にキャンセルされたプロファイルは登録を解除する', async () => {
  const ext = extension();
  const token = cancellation();
  ext.onCreateSession = () => token.cancel();
  assert.equal(await ext.profile(token), undefined);
  assert.deepEqual(ext.removed, ['session-1']);
  assert.equal(ext.errors.length, 0);
  await ext.deactivate();
  assert.deepEqual(ext.removed, ['session-1']);
});

test('プロファイルを返した後のキャンセルでも、開く前のセッションだけを解除する', async () => {
  const ext = extension();
  const first = cancellation();
  await ext.profile(first);
  first.cancel();
  const second = cancellation();
  ext.launchProfile(await ext.profile(second));
  second.cancel();
  await ext.heartbeat();
  assert.deepEqual(ext.removed, ['session-1']);
  assert.deepEqual(ext.renewed, ['session-2']);
  await ext.deactivate();
});

test('ターミナルが開かれなかった登録は延命せず期限後に解除する', async () => {
  const ext = extension();
  await ext.profile();
  await ext.select();
  assert.equal(ext.errors.length, 1);
  assert.equal(ext.picks, 1);
  await ext.heartbeat();
  assert.deepEqual(ext.renewed, []);
  assert.deepEqual(ext.removed, []);
  ext.advanceTime(120000);
  await ext.heartbeat();
  assert.deepEqual(ext.removed, ['session-1']);
  await ext.select();
  assert.equal(ext.picks, 2);
  await ext.deactivate();
});

test('拡張停止時に作成待ちのプロファイルの登録も解除する', async () => {
  const ext = extension();
  await ext.profile();
  await ext.deactivate();
  assert.deepEqual(ext.removed, ['session-1']);
  assert.equal(await ext.profile(), undefined);
  assert.equal(ext.requests.length, 1);
});

test('セッション発行中に拡張が停止しても遅れて返された登録を残さない', async () => {
  const ext = extension();
  ext.onCreateSession = () => ext.deactivate();
  assert.equal(await ext.profile(), undefined);
  assert.deepEqual(ext.removed, ['session-1']);
});
