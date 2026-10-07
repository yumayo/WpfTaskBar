'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { execFile } = require('node:child_process');
const { promisify } = require('node:util');
const test = require('node:test');

test('AI フックは開始・完了を通知し、通知失敗でも正常終了する', { skip: process.platform === 'win32' }, async t => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'wpftaskbar-hook-'));
  t.after(() => fs.rm(dir, { recursive: true, force: true }));
  const capture = path.join(dir, 'args');
  await fs.writeFile(path.join(dir, 'curl'), '#!/bin/sh\nprintf "%s\\n" "$@" > "$TASKBAR_TEST_CAPTURE"\nexit "${TASKBAR_TEST_EXIT:-0}"\n', { mode: 0o755 });
  const env = {
    ...process.env,
    PATH: `${dir}:${process.env.PATH}`,
    TASKBAR_TEST_CAPTURE: capture,
    WPF_TASKBAR_URL: 'http://windows-host:5000/',
    WPF_TASKBAR_SESSION_ID: '0123456789abcdef0123456789abcdef',
  };
  const script = path.join(__dirname, '../scripts/wpftaskbar.py');
  for (const status of ['running', 'waiting', 'interrupted', 'completed', 'none']) {
    const result = await promisify(execFile)('python3', [script, status], { env, timeout: 3000 });
    assert.equal(result.stdout, '');
    const args = (await fs.readFile(capture, 'utf8')).trim().split('\n');
    assert.equal(args.at(-1), 'http://windows-host:5000/tasks/sessions/0123456789abcdef0123456789abcdef/status');
    assert.equal(args[args.indexOf('--data') + 1], JSON.stringify({ status }));
    assert.equal(args[args.indexOf('--max-time') + 1], '5');
  }
  await promisify(execFile)('python3', [script, 'none', '2'], { env });
  const shortTimeoutArgs = (await fs.readFile(capture, 'utf8')).trim().split('\n');
  assert.equal(shortTimeoutArgs[shortTimeoutArgs.indexOf('--max-time') + 1], '2');
  for (const args of [[], ['invalid'], ['none', '0'], ['none', '6'], ['none', 'invalid']]) {
    await assert.rejects(promisify(execFile)('python3', [script, ...args], { env }), error => error.code === 2);
  }
  const result = await promisify(execFile)('python3', [script, 'completed'], { env: { ...env, TASKBAR_TEST_EXIT: '22' } });
  assert.match(result.stderr, /通知できません/);
  await fs.unlink(capture);
  await promisify(execFile)('python3', [script, 'running'], { env: { ...env, WPF_TASKBAR_SESSION_ID: '' } });
  await assert.rejects(fs.access(capture));
  await promisify(execFile)('python3', [script, 'running'], { env: { ...env, WPF_TASKBAR_URL: '' } });
  await assert.rejects(fs.access(capture));
  await promisify(execFile)('python3', [script, 'running'], { env: { ...env, WPF_TASKBAR_SESSION_ID: '../../tasks' } });
  await assert.rejects(fs.access(capture));
  // curlが見つからない環境でもAIのフックを失敗させない。
  const python = (await promisify(execFile)('python3', ['-c', 'import sys; print(sys.executable)'])).stdout.trim();
  await fs.unlink(path.join(dir, 'curl'));
  const missingCurl = await promisify(execFile)(python, [script, 'running'], { env: { ...env, PATH: dir } });
  assert.equal(missingCurl.stdout, '');
  assert.match(missingCurl.stderr, /通知できません/);
});


test('ツール結果は条件付きで再開し、Claudeの中断フラグを厳密に判定する', { skip: process.platform === 'win32' }, async t => {
  const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'wpftaskbar-tool-result-'));
  t.after(() => fs.rm(dir, { recursive: true, force: true }));
  const capture = path.join(dir, 'args');
  await fs.writeFile(path.join(dir, 'curl'), '#!/bin/sh\nprintf "%s\\n" "$@" > "$TASKBAR_TEST_CAPTURE"\n', { mode: 0o755 });
  const env = { ...process.env, PATH: `${dir}:${process.env.PATH}`, TASKBAR_TEST_CAPTURE: capture,
    WPF_TASKBAR_URL: 'http://windows-host:5000', WPF_TASKBAR_SESSION_ID: '0123456789abcdef0123456789abcdef' };
  const script = path.join(__dirname, '../scripts/wpftaskbar.py');
  for (const [action, input, status] of [
    ['resume', {}, 'running'],
    ['tool-failed', { is_interrupt: true }, 'interrupted'],
    ['tool-failed', { is_interrupt: false }, 'running'],
    ['tool-failed', { is_interrupt: 'true', error: '"is_interrupt":true' }, 'running'],
  ]) {
    const execution = promisify(execFile)('python3', [script, action], { env });
    execution.child.stdin.end(JSON.stringify(input));
    const result = await execution;
    assert.equal(result.stdout, '');
    const args = (await fs.readFile(capture, 'utf8')).trim().split('\n');
    assert.deepEqual(JSON.parse(args[args.indexOf('--data') + 1]), { status, onlyIfActive: true });
  }
  await fs.unlink(capture);
  const invalid = promisify(execFile)('python3', [script, 'tool-failed'], { env });
  invalid.child.stdin.end('not json');
  const result = await invalid;
  assert.match(result.stderr, /入力を読み取れません/);
  await assert.rejects(fs.access(capture));
});
