'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { execFile } = require('node:child_process');
const { promisify } = require('node:util');
const test = require('node:test');

const run = promisify(execFile);
const installer = path.join(__dirname, '../scripts/install-claude-hooks.py');

test('Claude共通フックは既存の権限・環境変数・フックを保持し、別ディレクトリから通知できる', { skip: process.platform === 'win32' }, async t => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'wpftaskbar-claude-'));
  t.after(() => fs.rm(dir, { recursive: true, force: true }));
  const target = path.join(dir, "config with 'quotes' and spaces");
  await fs.mkdir(target);
  const configFile = path.join(target, 'settings.json');
  const existingHandler = { type: 'command', command: 'echo existing', timeout: 30 };
  const settings = {
    permissions: { allow: ['Bash(git status)'], deny: ['Read(./.env)'] },
    env: { MY_EXISTING_SETTING: 'keep me' },
    disableAllHooks: true,
    hooks: {
      Stop: [{ hooks: [existingHandler] }],
      PreToolUse: [{ matcher: 'Bash', hooks: [existingHandler] }],
    },
  };
  const original = JSON.stringify(settings);
  await fs.writeFile(configFile, original);
  const installResult = await run('python3', [installer, '--claude-dir', target]);
  assert.match(installResult.stdout, /disableAllHooks: true/);
  const installed = await fs.readFile(configFile, 'utf8');
  const config = JSON.parse(installed);
  for (const key of ['permissions', 'env', 'disableAllHooks']) assert.deepEqual(config[key], settings[key]);
  assert.deepEqual(config.hooks.Stop[0].hooks, [existingHandler]);
  assert.deepEqual(config.hooks.PreToolUse.slice(0, 1), settings.hooks.PreToolUse);
  const backups = (await fs.readdir(target)).filter(file => file.endsWith('.bak'));
  assert.equal(backups.length, 1);
  assert.equal(await fs.readFile(path.join(target, backups[0]), 'utf8'), original);

  await run('python3', [installer, '--claude-dir', target]);
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
  // Claude本体を起動せず、登録されたコマンドの配置先・引数・標準出力を確認する。
  for (const [event, status, timeout] of [
    ['UserPromptSubmit', 'running', '5'], ['Stop', 'completed', '5'],
    ['PreToolUse', 'waiting', '5'], ['PermissionRequest', 'waiting', '5'],
    ['PostToolUse', 'running', '5'], ['PostToolUseFailure', 'interrupted', '2'],
    ['Elicitation', 'waiting', '5'], ['ElicitationResult', 'running', '5'],
    ['StopFailure', 'interrupted', '2'], ['SessionEnd', 'none', '2'],
  ]) {
    const handler = config.hooks[event].at(-1).hooks[0];
    const execution = run('sh', ['-c', handler.command], { env, cwd: dir });
    execution.child.stdin.end(JSON.stringify({ hook_event_name: event, is_interrupt: true, prompt: 'not sent to taskbar' }));
    const result = await execution;
    assert.equal(result.stdout, '');
    const args = (await fs.readFile(capture, 'utf8')).trim().split('\n');
    assert.equal(args.at(-1), 'http://windows-host:5000/tasks/sessions/0123456789abcdef0123456789abcdef/status');
    assert.deepEqual(JSON.parse(args[args.indexOf('--data') + 1]), ['PostToolUse', 'PostToolUseFailure', 'ElicitationResult'].includes(event) ? { status, onlyIfActive: true } : { status });
    assert.equal(args[args.indexOf('--max-time') + 1], timeout);
    assert.ok(handler.timeout > Number(timeout));
  }
  assert.equal(config.hooks.SubagentStop, undefined);
  assert.equal(config.hooks.Interrupt, undefined);
});

test('Claude設定の配置先を環境変数から選び、壊れた既存設定は上書きしない', { skip: process.platform === 'win32' }, async t => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'wpftaskbar-claude-invalid-'));
  t.after(() => fs.rm(dir, { recursive: true, force: true }));
  const fresh = path.join(dir, 'new-config');
  await run('python3', [installer], { env: { ...process.env, CLAUDE_CONFIG_DIR: fresh } });
  const config = JSON.parse(await fs.readFile(path.join(fresh, 'settings.json'), 'utf8'));
  assert.deepEqual(Object.keys(config.hooks), ['UserPromptSubmit', 'PreToolUse', 'PermissionRequest', 'PostToolUse', 'PostToolUseFailure', 'Elicitation', 'ElicitationResult', 'Stop', 'StopFailure', 'SessionEnd']);
  assert.equal(await fs.readFile(path.join(fresh, 'hooks/wpftaskbar/taskbar-status.sh'), 'utf8'),
    await fs.readFile(path.join(__dirname, '../scripts/taskbar-status.sh'), 'utf8'));

  for (const [index, invalid] of [
    'not JSON', '[]', '{"hooks":[]}', '{"hooks":{"Stop":"invalid"}}',
    '{"hooks":{"SessionEnd":[{"hooks":[null]}]}}',
  ].entries()) {
    const target = path.join(dir, `invalid-${index}`);
    await fs.mkdir(target);
    const file = path.join(target, 'settings.json');
    await fs.writeFile(file, invalid);
    await assert.rejects(run('python3', [installer, '--claude-dir', target]), error => error.code === 1);
    assert.equal(await fs.readFile(file, 'utf8'), invalid);
    assert.deepEqual(await fs.readdir(target), ['settings.json']);
  }
});


test('Claudeの旧失敗フックは置き換え、matcherごとの追加を再実行でも重複させない', { skip: process.platform === 'win32' }, async t => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'wpftaskbar-claude-migrate-'));
  t.after(() => fs.rm(dir, { recursive: true, force: true }));
  await run('python3', [installer, '--claude-dir', dir]);
  const file = path.join(dir, 'settings.json');
  const config = JSON.parse(await fs.readFile(file, 'utf8'));
  config.hooks.StopFailure[0].hooks[0].command = config.hooks.StopFailure[0].hooks[0].command.replace('interrupted 2', 'none 2');
  const custom = { type: 'command', command: 'echo keep', timeout: 9 };
  config.hooks.StopFailure[0].hooks.push(custom);
  await fs.writeFile(file, JSON.stringify(config));
  await run('python3', [installer, '--claude-dir', dir]);
  const updated = JSON.parse(await fs.readFile(file, 'utf8'));
  const commands = updated.hooks.StopFailure.flatMap(group => group.hooks);
  assert.deepEqual(commands[0], custom);
  assert.equal(commands.length, 2);
  assert.match(commands[1].command, / interrupted 2$/);
  const matcher = new RegExp(updated.hooks.PreToolUse.find(group => group.matcher)?.matcher);
  for (const tool of ['AskUserQuestion', 'ExitPlanMode']) assert.equal(matcher.test(tool), true);
  assert.equal(matcher.test('Bash'), false);
  const installed = await fs.readFile(file, 'utf8');
  await run('python3', [installer, '--claude-dir', dir]);
  assert.equal(await fs.readFile(file, 'utf8'), installed);
});
