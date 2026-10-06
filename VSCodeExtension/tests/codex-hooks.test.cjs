'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { execFile } = require('node:child_process');
const { promisify } = require('node:util');
const test = require('node:test');

const run = promisify(execFile);
const installer = path.join(__dirname, '../scripts/install-codex-hooks.py');

test('Codex共通フックは既存設定を保持して登録され、別ディレクトリから状態を通知できる', { skip: process.platform === 'win32' }, async t => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'wpftaskbar-codex-'));
  t.after(() => fs.rm(dir, { recursive: true, force: true }));
  const target = path.join(dir, "config with 'quotes' and spaces");
  await fs.mkdir(target);
  const configFile = path.join(target, 'hooks.json');
  const existingHandler = { type: 'command', command: 'echo existing', timeout: 30 };
  const original = JSON.stringify({ description: 'Existing hooks', hooks: {
    Stop: [{ hooks: [existingHandler] }],
    PreToolUse: [{ matcher: 'Bash', hooks: [existingHandler] }],
  } });
  await fs.writeFile(configFile, original);
  await run('python3', [installer, '--codex-dir', target]);
  const installed = await fs.readFile(configFile, 'utf8');
  const config = JSON.parse(installed);
  assert.equal(config.description, 'Existing hooks');
  assert.deepEqual(config.hooks.Stop[0].hooks, [existingHandler]);
  assert.deepEqual(config.hooks.PreToolUse, [{ matcher: 'Bash', hooks: [existingHandler] }]);
  const backups = (await fs.readdir(target)).filter(file => file.endsWith('.bak'));
  assert.equal(backups.length, 1);
  assert.equal(await fs.readFile(path.join(target, backups[0]), 'utf8'), original);

  await run('python3', [installer, '--codex-dir', target]);
  assert.equal(await fs.readFile(configFile, 'utf8'), installed);
  assert.equal((await fs.readdir(target)).filter(file => file.endsWith('.bak')).length, 1);

  const capture = path.join(dir, 'curl-args');
  await fs.writeFile(path.join(dir, 'curl'), '#!/bin/sh\nprintf "%s\\n" "$@" > "$TASKBAR_TEST_CAPTURE"\n', { mode: 0o755 });
  const env = {
    ...process.env,
    PATH: `${dir}:${process.env.PATH}`,
    TASKBAR_TEST_CAPTURE: capture,
    WPF_TASKBAR_URL: 'http://windows-host:5000',
    WPF_TASKBAR_SESSION_ID: '0123456789abcdef0123456789abcdef',
  };
  for (const [event, status, timeout] of [
    ['UserPromptSubmit', 'running', '5'], ['Stop', 'completed', '5'],
    ['Interrupt', 'none', '2'], ['SessionEnd', 'none', '2'],
  ]) {
    const handler = config.hooks[event].at(-1).hooks[0];
    const execution = run('sh', ['-c', handler.command], { env, cwd: dir });
    execution.child.stdin.end(JSON.stringify({ hook_event_name: event, prompt: 'not sent to taskbar' }));
    const result = await execution;
    assert.equal(result.stdout, ''); // 通知内容をCodexの追加指示に混ぜない。
    const args = (await fs.readFile(capture, 'utf8')).trim().split('\n');
    assert.equal(args[args.indexOf('--data') + 1], JSON.stringify({ status }));
    assert.equal(args[args.indexOf('--max-time') + 1], timeout);
    assert.ok(handler.timeout > Number(timeout));
  }
  assert.equal(config.hooks.SubagentStop, undefined);
});

test('Codexフックが未設定なら新規作成し、壊れた既存設定は上書きしない', { skip: process.platform === 'win32' }, async t => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'wpftaskbar-codex-invalid-'));
  t.after(() => fs.rm(dir, { recursive: true, force: true }));
  const fresh = path.join(dir, 'new-config');
  await run('python3', [installer, '--codex-dir', fresh]);
  const config = JSON.parse(await fs.readFile(path.join(fresh, 'hooks.json'), 'utf8'));
  assert.deepEqual(Object.keys(config.hooks), ['UserPromptSubmit', 'Stop', 'Interrupt', 'SessionEnd']);

  for (const [index, invalid] of ['not JSON', '[]', '{"hooks":[]}', '{"hooks":{"Stop":"invalid"}}'].entries()) {
    const target = path.join(dir, `invalid-${index}`);
    await fs.mkdir(target);
    const file = path.join(target, 'hooks.json');
    await fs.writeFile(file, invalid);
    await assert.rejects(run('python3', [installer, '--codex-dir', target]), error => error.code === 1);
    assert.equal(await fs.readFile(file, 'utf8'), invalid);
    assert.deepEqual(await fs.readdir(target), ['hooks.json']);
  }
});
