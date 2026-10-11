'use strict';

const fs = require('node:fs');
const path = require('node:path');

for (const name of ['kya.exe', 'KeepingYouAwake.exe']) {
  const executable = path.join(__dirname, '..', 'native', name);
  if (!fs.existsSync(executable) || !fs.statSync(executable).isFile()) {
    console.error(`Missing ${name}. Build the package with scripts/Build-Npm.ps1 before packing or publishing.`);
    process.exit(1);
  }
}
