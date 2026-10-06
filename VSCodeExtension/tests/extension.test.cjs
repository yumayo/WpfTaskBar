'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');
const clientModule = require('../dist/client');

function extension() {
  const commands = new Map();
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
  let onHeartbeat;
  let failRenew = false;
  let failCreate = false;
  let createError;
  const config = { apiUrl: 'http://localhost:5000', containerApiUrl: 'http://192.0.2.1:5000' };
  class FakeClient {
    constructor(url) { this.url = url; }
    async windows() { return windows; }
    async createSession(window) {
      requests.push(window);
      if (createError) throw createError;
      return { sessionId: `session-${requests.length}`, handle: window.handle, processId: window.processId };
    }
    async renew(id) { renewed.push(id); if (failRenew) throw new clientModule.ApiError(404, 'missing'); }
    async remove(id) { removed.push(id); }
  }
  const vscode = {
    workspace: { getConfiguration: () => ({ get: (name, fallback) => config[name] ?? fallback }) },
    commands: { registerCommand: (name, command) => { commands.set(name, command); return {}; } },
    window: {
      createOutputChannel: () => ({ appendLine() {}, show() {} }),
      showQuickPick: async items => { picks++; return items[selection]; },
      createTerminal: options => {
        if (failCreate) throw new Error('terminal creation failed');
        const terminal = { options, show() {} };
        created.push(terminal);
        return terminal;
      },
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
  };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../dist/extension.js'), 'utf8'), sandbox);
  const api = sandbox.module.exports;
  api.activate({ subscriptions: [] });
  return {
    created, removed, renewed, requests, errors, warnings, config,
    get picks() { return picks; },
    set windows(value) { windows = value; },
    set selection(value) { selection = value; },
    set failCreate(value) { failCreate = value; },
    set failRenew(value) { failRenew = value; },
    set createError(value) { createError = value; },
    open: () => commands.get('wpftaskbar.openTerminal')(),
    select: () => commands.get('wpftaskbar.selectWindow')(),
    close: terminal => onClose(terminal),
    heartbeat: async () => { onHeartbeat(); await new Promise(resolve => setImmediate(resolve)); },
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
