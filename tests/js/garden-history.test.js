"use strict";
const test = require("node:test");
const assert = require("node:assert");
const History = require("../../src/SeedPlanner.Api/wwwroot/js/garden-history.js");

function setup(limit) {
    const box = { value: 1 };
    const history = History.create(() => ({ value: box.value }), (s) => { box.value = s.value; }, limit);
    return { box, history };
}

test("commit without a change records nothing", () => {
    const { history } = setup();
    assert.strictEqual(history.commit(), false);
    assert.strictEqual(history.canUndo(), false);
});

test("undo and redo walk through the states", () => {
    const { box, history } = setup();
    box.value = 2; history.commit();
    box.value = 3; history.commit();
    assert.ok(history.undo()); assert.strictEqual(box.value, 2);
    assert.ok(history.undo()); assert.strictEqual(box.value, 1);
    assert.strictEqual(history.undo(), false);
    assert.ok(history.redo()); assert.strictEqual(box.value, 2);
    assert.ok(history.redo()); assert.strictEqual(box.value, 3);
    assert.strictEqual(history.redo(), false);
});

test("a new change ends the redo line", () => {
    const { box, history } = setup();
    box.value = 2; history.commit();
    history.undo();
    box.value = 5; history.commit();
    assert.strictEqual(history.canRedo(), false);
    history.undo();
    assert.strictEqual(box.value, 1);
});

test("the history keeps at most the given number of steps", () => {
    const { box, history } = setup(2);
    for (let v = 2; v <= 5; v++) { box.value = v; history.commit(); }
    history.undo(); history.undo();
    assert.strictEqual(history.undo(), false);
    assert.strictEqual(box.value, 3);
});
