#!/usr/bin/env node
'use strict';

const { spawnSync } = require('node:child_process');
const path = require('node:path');

if (process.platform !== 'win32' || process.arch !== 'x64') {
  console.error('KeepingYouAwake requires Windows x64.');
  process.exit(4);
}

const executable = path.join(__dirname, '..', 'native', 'kya.exe');
// Pass arguments directly, preserving quotes and avoiding shell interpretation.
const result = spawnSync(executable, process.argv.slice(2), {
  stdio: 'inherit',
  windowsHide: true,
  shell: false
});

if (result.error) {
  if (process.argv.includes('--json')) {
    console.log(JSON.stringify({ success: false, code: 'unavailable', message: result.error.message }));
  } else {
    console.error(`Unable to start kya: ${result.error.message}`);
  }
  process.exit(4);
}
process.exit(result.status === null ? 4 : result.status);
