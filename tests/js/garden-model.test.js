// Run with: node --test tests/js/garden-model.test.js
const test = require("node:test");
const assert = require("node:assert/strict");
const Model = require("../../src/SeedPlanner.Api/wwwroot/js/garden-model.js");

const kinds = { Garden: "Garden", Mine: "Mine", Occupied: "Occupied", Excluded: "Excluded" };
const data = () => ({
  boundary: [[0, 0], [100, 0], [100, 100]], lengths: [10, null, null],
  zones: [{ name: "A", kind: "Nonsense", owner: 2.9, points: [[1, 1], [2, 2], [3, 1]] }, { name: "B", kind: "Excluded", owner: 0, points: [] }],
  cutLine: { a: [0, 0], b: [10, 10], owner1: "1", owner2: undefined }, neighbors: ["N1"]
});

test("an area from the server gets usable values", () => {
  const model = Model.create(data(), kinds);
  const [a, b] = model.zones;
  assert.equal(a.kind, "Occupied");                    // unknown kind
  assert.equal(a.owner, 2);                            // whole number
  assert.equal(a.use, "");                             // missing text fields
  assert.equal(a.waterlogged, false);
  assert.equal(b.kind, "Mine");                        // the first kind of "not mine" is now owned by a neighbour
  assert.equal(b.owner, 1);
  assert.deepEqual(model.neighbors, ["N1", ""]);       // a neighbour exists for every owner
  assert.deepEqual(model.cutLine, { a: [0, 0], b: [10, 10], owner1: 1, owner2: 0 });
});

test("no data: empty plot without a line", () => {
  const model = Model.create({}, kinds);
  assert.deepEqual([model.boundary, model.lengths, model.zones, model.neighbors, model.placedBeds], [[], [], [], [], []]);
  assert.equal(model.cutLine, null);
});

test("subscribers are told with notify and can leave", () => {
  const model = Model.create({}, kinds);
  let calls = 0;
  const leave = model.subscribe(m => { assert.equal(m, model); calls++; });
  model.notify();
  leave();
  model.notify();
  assert.equal(calls, 1);
});

test("snapshot and restore bring the plot back, the snapshot is a copy", () => {
  const model = Model.create(data(), kinds);
  const snapshot = model.snapshot();
  model.boundary.push([5, 5]);
  model.zones[0].name = "changed";
  model.cutLine = null;
  assert.equal(snapshot.boundary.length, 3);           // not changed by the later changes
  let told = 0;
  model.subscribe(() => told++);
  model.restore(snapshot);
  assert.equal(model.boundary.length, 3);
  assert.equal(model.zones[0].name, "A");
  assert.deepEqual(model.cutLine.b, [10, 10]);
  assert.equal(told, 0);                               // the caller tells the views itself
});
