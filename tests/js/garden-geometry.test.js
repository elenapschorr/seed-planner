// Run with: node --test tests/js
// The same numbers as tests/SeedPlanner.Tests/PlotGeometryTests.cs, so the browser and the server calculate alike.
const test = require("node:test");
const assert = require("node:assert/strict");
const G = require("../../src/SeedPlanner.Api/wwwroot/js/garden-geometry.js");

const near = (actual, expected, eps = 1e-6) => assert.ok(Math.abs(actual - expected) <= eps, `${actual} != ${expected}`);
const square = [[0, 0], [100, 0], [100, 100], [0, 100]];
const rect = (x0, y0, x1, y1) => [[x0, y0], [x1, y0], [x1, y1], [x0, y1]];
const free = (bound, ...zones) => G.areaWithout(bound, zones);

test("area is the same for either direction", () => {
    near(G.area(square), 10000);
    near(G.area(square.slice().reverse()), 10000);
    assert.equal(G.area([[0, 0], [10, 0]]), 0);
});

test("scale and deviation", () => {
    near(G.metersPerPixel(square, [10, null, null, null]), 0.1);
    assert.equal(G.metersPerPixel(square, [null, null, null, null]), null);
    near(G.metersPerPixel(square, [10, 10, 10, 12]), 0.105);
});

test("angles", () => {
    for (const shape of [square, square.slice().reverse()]) for (let i = 0; i < 4; i++) near(G.angle(shape, i), 90);
    const l = [[0, 0], [2, 0], [2, 1], [1, 1], [1, 2], [0, 2]];
    near(G.angle(l, 3), 270);
    near(G.angle(l, 2), 90);
});

test("self intersection", () => {
    assert.equal(G.selfIntersects([[0, 0], [10, 10], [10, 0], [0, 10]]), true);
    assert.equal(G.selfIntersects(square), false);
});

test("cut out areas", () => {
    near(free(square), 10000);
    near(free(square, rect(10, 10, 30, 40)), 10000 - 600);
    near(free(square, rect(80, 10, 120, 30)), 10000 - 400);
    near(free(square, rect(10, 10, 50, 50), rect(30, 30, 70, 70)), 10000 - 2800);
    near(free(square, rect(200, 0, 300, 100)), 10000);
    near(free(square, rect(-10, -10, 200, 200)), 0);
    near(free(square, [[50, 50], [150, 50], [50, 100]]), 10000 - (2500 - 625));
    const l = [[0, 0], [2, 0], [2, 1], [1, 1], [1, 2], [0, 2]];
    near(free(l), 3);
    near(free(l, rect(1, 0, 3, 3)), 2);
});

test("total, cut, occupied and free area", () => {
    const a = G.areas(square, [rect(70, 10, 80, 30)], [rect(60, 10, 70, 30)]);
    near(a.total, 10000); near(a.cut, 200); near(a.occupied, 200); near(a.free, 9600);
    const b = G.areas(square, [rect(0, 0, 50, 100)], [rect(25, 0, 75, 100)]);
    near(b.cut, 5000); near(b.occupied, 2500); near(b.free, 2500);
});

test("shapes have the size that was asked for", () => {
    // sizes of a real picture (hundreds of pixels); corners are rounded to a tenth of a pixel
    near(G.area(G.rectangle(500, 500, 200, 100, 0.3)), 20000, 20);
    near(G.area(G.ellipse(500, 500, 100, 100, 0, 96)), Math.PI * 10000, 20 + 0.0011 * Math.PI * 10000);
    near(G.area(G.ellipse(500, 500, 200, 100, 1.1, 96)), Math.PI * 20000, 20 + 0.0011 * Math.PI * 20000);
    near(G.area(G.rightTriangle(100, 100, 300, 200, 0.7)), 30000, 20);
    // the first side runs along the angle: a rectangle turned by 90 degrees is higher than wide
    const r = G.rectangle(500, 500, 200, 100, Math.PI / 2);
    const xs = r.map(p => p[0]), ys = r.map(p => p[1]);
    near(Math.max(...xs) - Math.min(...xs), 100, 0.2);
    near(Math.max(...ys) - Math.min(...ys), 200, 0.2);
});

test("splitting an area by a parallel line", () => {
    const garage = rect(10, 10, 50, 30); // 40 x 20
    // line parallel to the x axis: normal (0, 1); halfway between y = 10 and y = 30
    const e = G.extent(garage, 0, 1);
    near(e.min, 10); near(e.max, 30);
    const half = G.splitByLine(garage, 0, 1, 20);
    near(G.area(half.below), 400);
    near(G.area(half.above), 400);

    const third = G.splitByLine(garage, 1, 0, 10 + 40 / 4); // normal (1, 0), a quarter of the width
    near(G.area(third.below), 200);
    near(G.area(third.above), 600);

    const outside = G.splitByLine(garage, 0, 1, 5);
    assert.equal(outside.below, null);
    near(G.area(outside.above), 800);

    // a bent area: the two parts together are as large as the whole
    const l = [[0, 0], [20, 0], [20, 10], [10, 10], [10, 20], [0, 20]];
    const parts = G.splitByLine(l, 1, 0, 15);
    near(G.area(parts.below) + G.area(parts.above), G.area(l));
});

test("trapezoid area and split position for a wanted area", () => {
  const G = require("../../src/SeedPlanner.Api/wwwroot/js/garden-geometry.js");
  const t = G.trapezoid(500, 500, 400, 200, 100, 0);
  assert.ok(Math.abs(G.area(t) - 30000) < 1);
  const t0 = G.splitPositionForArea(t, 0, 1, 10000);
  const parts = G.splitByLine(t, 0, 1, t0);
  assert.ok(Math.abs(G.area(parts.below) - 10000) < 50);
  assert.equal(G.splitPositionForArea(t, 0, 1, 40000), null);
});

test("right trapezoid has two right angles and the right area", () => {
  const G = require("../../src/SeedPlanner.Api/wwwroot/js/garden-geometry.js");
  const t = G.rightTrapezoid(500, 500, 400, 200, 100, 0);
  assert.ok(Math.abs(G.area(t) - 30000) < 1);
  assert.ok(Math.abs(G.angle(t, 0) - 90) < 0.5 && Math.abs(G.angle(t, 3) - 90) < 0.5);
});

test("point in polygon, overlap of convex shapes and bed outline", () => {
  assert.ok(G.pointInPolygon([50, 50], square));
  assert.ok(!G.pointInPolygon([150, 50], square));
  assert.ok(G.convexOverlap(rect(0, 0, 10, 10), rect(5, 5, 15, 15)));
  assert.ok(!G.convexOverlap(rect(0, 0, 10, 10), rect(10, 0, 20, 10)));   // touching only
  assert.ok(!G.convexOverlap(rect(0, 0, 10, 10), rect(20, 0, 30, 10)));
  // a bed of 2 m x 1 m at 0.1 m per pixel is 20 x 10 pixels; turned by 90 degrees it is 10 x 20
  const bed = G.bedOutline(100, 100, 2, 1, 0, false, 0.1);
  near(G.area(bed), 200, 0.5);
  const turned = G.bedOutline(100, 100, 2, 1, 90, false, 0.1);
  near(Math.max(...turned.map(p => p[1])) - Math.min(...turned.map(p => p[1])), 20, 0.5);
  const oval = G.bedOutline(100, 100, 2, 1, 30, true, 0.1);
  near(G.area(oval), Math.PI * 10 * 5, 2);
  assert.ok(G.pointInPolygon([100, 100], oval));
});

test("subtracting polygons: notch, corner, hole, no overlap, concave subject", () => {
  const sq = [[0, 0], [100, 0], [100, 100], [0, 100]];
  const total = (r) => r.polys.reduce((s, p) => s + G.area(p), 0);
  // a rectangle that crosses one edge: a notch
  let r = G.subtractAll([sq], [rect(30, -20, 60, 30)]);
  assert.equal(r.polys.length, 1); near(total(r), 9100, 2);
  // a rectangle over a corner
  r = G.subtractAll([sq], [rect(50, 50, 150, 150)]);
  near(total(r), 7500, 2);
  // completely inside: a hole, the subject is kept
  r = G.subtractAll([sq], [rect(40, 40, 60, 60)]);
  assert.equal(r.holes.length, 1); near(total(r), 10000, 1);
  // no overlap, and the whole subject covered
  near(total(G.subtractAll([sq], [rect(200, 0, 300, 100)])), 10000, 1);
  assert.equal(G.subtractAll([sq], [rect(-10, -10, 110, 110)]).polys.length, 0);
  // exactly on the edge of the subject (the clip polygon is moved a tiny bit)
  near(total(G.subtractAll([sq], [rect(0, 0, 50, 100)])), 5000, 2);
  near(total(G.subtractAll([sq], [rect(50, -10, 100, 110)])), 5000, 2);
  // a rectangle that cuts the square into two pieces
  r = G.subtractAll([sq], [rect(40, -10, 60, 110)]);
  assert.equal(r.polys.length, 2); near(total(r), 8000, 2);
  // concave subject (L shape) minus a rectangle in the corner of the L
  const L = [[0, 0], [100, 0], [100, 40], [40, 40], [40, 100], [0, 100]];
  near(G.area(L), 6400);
  near(total(G.subtractAll([L], [rect(30, 30, 50, 50)])), 6100, 3);   // 20 x 10 in the bar and 10 x 10 in the upright
  // an area that stops a little short of the edge: with a tolerance it cuts the plot like one that reaches the edge
  r = G.subtractAll([sq], [rect(40, 2, 60, 30)]);
  assert.equal(r.holes.length, 1);
  r = G.subtractAll([sq], [rect(40, 2, 60, 30)], 4);
  assert.equal(r.holes.length, 0); near(total(r), 10000 - 20 * 30, 3);
  // far from the edge it stays a hole, also with a tolerance
  assert.equal(G.subtractAll([sq], [rect(40, 40, 60, 60)], 4).holes.length, 1);
  // two clips one after the other
  near(total(G.subtractAll([sq], [rect(-10, -10, 30, 30), rect(70, 70, 110, 110)])), 10000 - 900 - 900, 3);
});
