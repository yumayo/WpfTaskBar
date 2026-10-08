'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const clientModule = require('../dist/client');
const settle = () => new Promise(resolve => setImmediate(resolve));

function extension() {
  const commands = new Map();
  const environment = new Map();
  const collection = {
    persistent: true,
    clear: () => environment.clear(),
    get: name => environment.get(name),
    replace: (name, value) => environment.set(name, { value }),
  };
  const removed = [];
  const renewed = [];
  const titles = [];
  const interrupted = [];
  const sequences = [];
  const contexts = new Map();
  const errors = [];
  const warnings = [];
  const requests = [];
  const timers = new Map();
  let activeTerminal;
  let onActiveTerminal;
  let onConfiguration;
  let windows = [
    { handle: 10, processId: 100, title: 'project A', moduleFileName: 'Code.exe' },
    { handle: 20, processId: 100, title: 'project B', moduleFileName: 'Code.exe' },
  ];
  let selection = 1;
  let picks = 0;
  let renewError;
  let createError;
  let onCreateSession;
  let onTitle;
  let onInterrupt;
  let onPick;
  const config = { apiUrl: 'http://localhost:5000', containerApiUrl: 'http://192.0.2.1:5000' };
  class FakeClient {
    constructor(url) { this.url = url; }
    async windows() { return windows; }
    async createSession(window) {
      requests.push({ ...window, apiUrl: this.url });
      const sessionId = `session-${requests.length}`;
      await onCreateSession?.();
      if (createError) throw createError;
      return { sessionId, handle: window.handle, processId: window.processId };
    }
    async renew(id) { renewed.push(id); if (renewError) throw renewError; }
    async interrupt(id) { interrupted.push(id); await onInterrupt?.(); }
    async setTitle(id, title) { titles.push({ id, title }); await onTitle?.(); }
    async remove(id) { removed.push(id); }
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
      get activeTerminal() { return activeTerminal; },
      onDidChangeActiveTerminal: listener => { onActiveTerminal = listener; return {}; },
      createOutputChannel: () => ({ appendLine() {}, show() {} }),
      showQuickPick: async items => { picks++; await onPick?.(); return items[selection]; },
      showErrorMessage: message => errors.push(message),
      showWarningMessage: message => warnings.push(message),
    },
  };
  const extensionModule = { exports: {} };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../dist/extension.js'), 'utf8'), {
    require: name => name === 'vscode' ? vscode : { ...clientModule, TaskbarClient: FakeClient },
    module: extensionModule,
    exports: extensionModule.exports,
    process: { platform: 'win32', env: { WSLENV: 'KEEP/p' } },
    setInterval: (callback, delay) => { timers.set(delay, callback); return delay; },
    clearInterval: delay => timers.delete(delay),
  });
  const api = extensionModule.exports;
  const ready = api.activate({ subscriptions: [], environmentVariableCollection: collection });
  return {
    ready, commands, environment, collection, timers, removed, renewed, requests, errors, warnings, config,
    titles, interrupted, sequences, contexts,
    get picks() { return picks; },
    set windows(value) { windows = value; },
    set selection(value) { selection = value; },
    set renewError(value) { renewError = value; },
    set createError(value) { createError = value; },
    set onCreateSession(value) { onCreateSession = value; },
    set onTitle(value) { onTitle = value; },
    set onInterrupt(value) { onInterrupt = value; },
    set onPick(value) { onPick = value; },
    activateTerminal: async terminal => { activeTerminal = terminal; onActiveTerminal(); await settle(); },
    interrupt: () => commands.get('wpftaskbar.interruptTerminal')(),
    select: () => commands.get('wpftaskbar.selectWindow')(),
    heartbeat: async () => { timers.get(30000)(); await settle(); },
    pollTitles: async () => { timers.get(1000)(); await settle(); },
    configure: async () => { onConfiguration({ affectsConfiguration: section => section === 'wpftaskbar' }); await settle(); },
    deactivate: api.deactivate,
  };
}

test('拡張起動時に1セッションを登録し、ターミナル作成前に環境変数を設定する', async () => {
  const ext = extension();
  await ext.ready;
  assert.equal(ext.picks, 0);
  assert.deepEqual(ext.requests.map(window => window.handle), [10]);
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-1');
  assert.equal(ext.environment.get('WPF_TASKBAR_URL').value, 'http://192.0.2.1:5000');
  assert.equal(ext.environment.get('WSLENV').value, 'KEEP/p:WPF_TASKBAR_URL/u:WPF_TASKBAR_SESSION_ID/u');
  assert.equal(ext.collection.persistent, false);
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
  assert.equal(ext.environment.size, 0);
  assert.equal(ext.timers.size, 0);
});

test('起動時の接続失敗は定期処理で再試行し、登録成功後にだけIDを設定する', async () => {
  const ext = extension();
  ext.createError = new Error('API unavailable');
  await ext.ready;
  assert.equal(ext.environment.size, 0);
  assert.equal(ext.errors.length, 1);
  ext.createError = undefined;
  await ext.heartbeat();
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-2');
  await ext.deactivate();
});

test('候補がない場合は通知IDを設定しない', async () => {
  const ext = extension();
  ext.windows = [];
  await ext.ready;
  assert.equal(ext.requests.length, 0);
  assert.equal(ext.environment.size, 0);
  assert.match(ext.errors[0], /VSCode のタスクがありません/);
  await ext.deactivate();
});

test('手動選択でセッションを差し替え、キャンセル・同じ通知先の再選択ではIDを変えない', async () => {
  const ext = extension();
  await ext.ready;
  await ext.activateTerminal({ name: 'bash' });
  await ext.select();
  assert.deepEqual(ext.requests.map(window => window.handle), [10, 20]);
  assert.deepEqual(ext.removed, ['session-1']);
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-2');
  assert.match(ext.warnings[0], /ターミナルを開き直し/);
  ext.selection = -1;
  await ext.select();
  ext.selection = 1;
  await ext.select();
  assert.equal(ext.requests.length, 2);
  assert.equal(ext.picks, 3);
  await ext.deactivate();
});

test('通知先の変更に失敗しても現在のセッションと環境変数を維持する', async () => {
  const ext = extension();
  await ext.ready;
  ext.createError = new Error('API unavailable');
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
  const selected = ext.select();
  await settle();
  await ext.heartbeat();
  assert.deepEqual(ext.renewed, ['session-1']);
  release();
  await selected;
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-2');
  await ext.deactivate();
});

test('404では同じウィンドウへ再登録し、一時的な通信エラーではIDを変えない', async () => {
  const ext = extension();
  await ext.ready;
  ext.renewError = new Error('offline');
  await ext.heartbeat();
  assert.equal(ext.requests.length, 1);
  ext.renewError = new clientModule.ApiError(404, 'expired');
  await ext.heartbeat();
  assert.deepEqual(ext.requests.map(window => window.handle), [10, 10]);
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-2');
  assert.equal(ext.warnings.length, 1);
  ext.renewError = undefined;
  await ext.heartbeat();
  assert.deepEqual(ext.renewed, ['session-1', 'session-1', 'session-2']);
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
  assert.equal(ext.environment.size, 0);
  assert.equal(ext.warnings.length, 1);
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
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-2');
  await ext.deactivate();
});

test('起動中の生存通知・設定変更でもセッションを重複登録しない', async () => {
  const ext = extension();
  let release;
  ext.onCreateSession = () => new Promise(resolve => { release = resolve; });
  await settle();
  await ext.heartbeat();
  await ext.configure();
  assert.equal(ext.requests.length, 1);
  release();
  await ext.ready;
  await settle();
  assert.equal(ext.requests.length, 1);
  assert.deepEqual(ext.renewed, ['session-1']);
  await ext.deactivate();
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
  assert.equal(ext.environment.size, 0);
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

test('タイトルの404で失効を一度だけ案内し、次の生存確認で再登録する', async () => {
  const ext = extension();
  await ext.ready;
  ext.onTitle = () => { throw new clientModule.ApiError(404, 'expired'); };
  await ext.activateTerminal({ name: 'expired' });
  await ext.pollTitles();
  assert.equal(ext.environment.size, 0);
  assert.equal(ext.contexts.get('wpftaskbar.sessionActive'), false);
  assert.equal(ext.warnings.length, 1);
  ext.onTitle = undefined;
  await ext.heartbeat();
  assert.equal(ext.environment.get('WPF_TASKBAR_SESSION_ID').value, 'session-2');
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
