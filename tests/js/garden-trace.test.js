const test = require("node:test");
const assert = require("node:assert");
const T = require("../../src/SeedPlanner.Api/wwwroot/js/garden-trace.js");

// A small picture: green ground, yellow lines of 1 pixel
function picture(w, h, draw) {
    const data = new Uint8ClampedArray(w * h * 4);
    for (let i = 0; i < w * h; i++) { data[i * 4] = 60; data[i * 4 + 1] = 120; data[i * 4 + 2] = 50; data[i * 4 + 3] = 255; }
    const set = (x, y) => { const p = (y * w + x) * 4; data[p] = 230; data[p + 1] = 220; data[p + 2] = 40; };
    draw({
        rect(x0, y0, x1, y1, gaps) {
            for (let x = x0; x <= x1; x++) { set(x, y0); set(x, y1); }
            for (let y = y0; y <= y1; y++) { set(x0, y); set(x1, y); }
            (gaps || []).forEach(([gx, gy, n]) => { for (let k = 0; k < n; k++) { const p = (gy * w + gx + k) * 4; data[p] = 60; data[p + 1] = 120; data[p + 2] = 50; } });
        }
    });
    return data;
}
const area = p => Math.abs(p.reduce((s, a, i) => { const b = p[(i + 1) % p.length]; return s + a[0] * b[1] - b[0] * a[1]; }, 0)) / 2;

test("a closed outline gives its four corners", () => {
    const data = picture(200, 150, g => g.rect(30, 20, 150, 110));
    const r = T.polygonAt(data, 200, 150, 80, 60);
    assert.strictEqual(r.status, "ok");
    assert.strictEqual(r.points.length, 4);
    assert.ok(Math.abs(area(r.points) - 120 * 90) < 120 * 90 * 0.06, "area " + area(r.points));
});

test("a gap in the line is closed", () => {
    const data = picture(200, 150, g => g.rect(30, 20, 150, 110, [[80, 20, 5]]));
    const r = T.polygonAt(data, 200, 150, 80, 60);
    assert.strictEqual(r.status, "ok");
    assert.ok(Math.abs(area(r.points) - 120 * 90) < 120 * 90 * 0.1, "area " + area(r.points));
});

test("a building inside the plot is part of the plot, and can be taken on its own", () => {
    const data = picture(200, 150, g => { g.rect(30, 20, 150, 110); g.rect(60, 40, 100, 80); });
    const plot = T.polygonAt(data, 200, 150, 40, 100);
    assert.strictEqual(plot.status, "ok");
    assert.ok(Math.abs(area(plot.points) - 120 * 90) < 120 * 90 * 0.06, "area " + area(plot.points));
    const house = T.polygonAt(data, 200, 150, 80, 60);
    assert.strictEqual(house.status, "ok");
    assert.ok(Math.abs(area(house.points) - 40 * 40) < 40 * 40 * 0.15, "area " + area(house.points));
});

test("a building that touches the boundary with its lines is still part of the plot", () => {
    const data = picture(200, 150, g => { g.rect(30, 20, 150, 110); g.rect(60, 20, 100, 60); });
    const plot = T.polygonAt(data, 200, 150, 40, 100);
    assert.strictEqual(plot.status, "ok");
    assert.ok(Math.abs(area(plot.points) - 120 * 90) < 120 * 90 * 0.08, "area " + area(plot.points));
});

test("sheds on the boundary belong to the plot, a building of the neighbour at the boundary does not", () => {
    const data = picture(200, 150, g => { g.rect(30, 20, 150, 110); g.rect(60, 20, 90, 45); g.rect(2, 50, 30, 100); });
    const plot = T.polygonAt(data, 200, 150, 100, 80);
    assert.strictEqual(plot.status, "ok");
    const a = area(plot.points), full = 120 * 90;
    assert.ok(a > full * 0.97 && a < full * 1.07, "area " + a + " of " + full);
});

test("a click on a line, and an outline that is not closed, are reported", () => {
    const data = picture(200, 150, g => g.rect(30, 20, 150, 110));
    assert.strictEqual(T.polygonAt(data, 200, 150, 30, 60).status, "line");
    assert.strictEqual(T.polygonAt(data, 200, 150, 10, 10).status, "open");
    const bigGap = picture(200, 150, g => g.rect(30, 20, 150, 110, [[70, 20, 60]]));
    assert.strictEqual(T.polygonAt(bigGap, 200, 150, 80, 60).status, "open");
});

test("a dot of lettering is too small", () => {
    const data = picture(200, 150, g => g.rect(80, 60, 86, 66));
    assert.strictEqual(T.polygonAt(data, 200, 150, 83, 63).status, "tiny");
});

test("simplify keeps the corners of a staircase edge out", () => {
    const pts = [];
    for (let x = 0; x < 50; x++) pts.push([x + 0.5, 0.5 + (x % 2 === 0 ? 0 : 1)]);
    for (let y = 1; y < 40; y++) pts.push([49.5, y + 0.5]);
    for (let x = 49; x >= 0; x--) pts.push([x + 0.5, 40.5]);
    for (let y = 39; y >= 1; y--) pts.push([0.5, y + 0.5]);
    assert.strictEqual(T.simplify(pts, 2).length, 4);
});
