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
  const script = path.join(__dirname, '../scripts/taskbar-status.sh');
  for (const status of ['running', 'completed', 'none']) {
    await promisify(execFile)('sh', [script, status], { env });
    const args = (await fs.readFile(capture, 'utf8')).trim().split('\n');
    assert.equal(args.at(-1), 'http://windows-host:5000/tasks/sessions/0123456789abcdef0123456789abcdef/status');
    assert.equal(args[args.indexOf('--data') + 1], JSON.stringify({ status }));
    assert.equal(args[args.indexOf('--max-time') + 1], '5');
  }
  await promisify(execFile)('sh', [script, 'none', '2'], { env });
  const shortTimeoutArgs = (await fs.readFile(capture, 'utf8')).trim().split('\n');
  assert.equal(shortTimeoutArgs[shortTimeoutArgs.indexOf('--max-time') + 1], '2');
  await assert.rejects(promisify(execFile)('sh', [script, 'none', '0'], { env }), error => error.code === 2);
  const result = await promisify(execFile)('sh', [script, 'completed'], { env: { ...env, TASKBAR_TEST_EXIT: '22' } });
  assert.match(result.stderr, /通知できません/);
  await fs.unlink(capture);
  await promisify(execFile)('sh', [script, 'running'], { env: { ...env, WPF_TASKBAR_SESSION_ID: '' } });
  await assert.rejects(fs.access(capture));
  await promisify(execFile)('sh', [script, 'running'], { env: { ...env, WPF_TASKBAR_SESSION_ID: '../../tasks' } });
  await assert.rejects(fs.access(capture));
});
