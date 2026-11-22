"use strict";
// Smoke tests of the garden page: garden-page.js and all its modules run against a small DOM mock (mock.js).
// Every *.check.js script prints "ok" or "FAIL" lines; a check fails the test if the script crashes or prints FAIL.
const test = require("node:test");
const assert = require("node:assert");
const fs = require("node:fs");
const path = require("node:path");
const { spawnSync } = require("node:child_process");

fs.readdirSync(__dirname).filter((f) => f.endsWith(".check.js")).forEach((file) => {
    test(file, () => {
        const run = spawnSync(process.execPath, [path.join(__dirname, file)], { encoding: "utf8" });
        const output = run.stdout + run.stderr;
        assert.strictEqual(run.status, 0, output);
        assert.ok(!/FAIL|LOAD ERROR/.test(output), output);
    });
});
