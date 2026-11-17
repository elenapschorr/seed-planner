// Takes an area from the lines that are already in the picture (cadastral map: yellow lines for the boundaries and the buildings).
// The user clicks into a closed outline; the area up to the lines is found and returned as a polygon in picture pixels.
// Pure functions on pixel arrays (RGBA as from a canvas), no DOM: runs in the browser and under Node for the tests.
(function (root) {
    "use strict";

    // The pixels that belong to a yellow line: bright, red and green about equal, little blue.
    // (roofs are orange: green clearly below red; grass is green: green above red)
    function lineMask(data, w, h) {
        var mask = new Uint8Array(w * h);
        for (var i = 0, p = 0; i < w * h; i++, p += 4) {
            var r = data[p], g = data[p + 1], b = data[p + 2];
            if (r > 170 && g > 0.8 * r && b < 0.65 * g) mask[i] = 1;
        }
        return mask;
    }

    // Makes every set pixel r pixels wider in all directions (square), so small gaps in a line close
    function dilate(mask, w, h, r) {
        if (r <= 0) return mask;
        var tmp = new Uint8Array(w * h), out = new Uint8Array(w * h);
        var x, y, k, base;
        for (y = 0; y < h; y++) {
            base = y * w;
            for (x = 0; x < w; x++) {
                if (!mask[base + x]) continue;
                for (k = Math.max(0, x - r); k <= Math.min(w - 1, x + r); k++) tmp[base + k] = 1;
            }
        }
        for (x = 0; x < w; x++) {
            for (y = 0; y < h; y++) {
                if (!tmp[y * w + x]) continue;
                for (k = Math.max(0, y - r); k <= Math.min(h - 1, y + r); k++) out[k * w + x] = 1;
            }
        }
        return out;
    }

    // Closing: widens the set and takes it back again; narrow cracks and links (a building that touches the boundary with its lines) close
    function close(mask, w, h, r) {
        var grown = dilate(mask, w, h, r), inv = new Uint8Array(w * h), i;
        for (i = 0; i < w * h; i++) inv[i] = grown[i] ? 0 : 1;
        var shrunk = dilate(inv, w, h, r), out = new Uint8Array(w * h);
        for (i = 0; i < w * h; i++) out[i] = shrunk[i] ? 0 : 1;
        return out;
    }

    // The pixels that can be reached from the start without crossing a wall (4 neighbours); open = reaches the border of the picture
    function fill(wall, w, h, sx, sy) {
        var region = new Uint8Array(w * h), queue = new Int32Array(w * h);
        var head = 0, tail = 0, open = false, count = 0;
        var start = sy * w + sx;
        region[start] = 1;
        queue[tail++] = start;
        while (head < tail) {
            var i = queue[head++], x = i % w, y = (i - x) / w;
            count++;
            if (x === 0 || y === 0 || x === w - 1 || y === h - 1) open = true;
            if (x > 0 && !region[i - 1] && !wall[i - 1]) { region[i - 1] = 1; queue[tail++] = i - 1; }
            if (x < w - 1 && !region[i + 1] && !wall[i + 1]) { region[i + 1] = 1; queue[tail++] = i + 1; }
            if (y > 0 && !region[i - w] && !wall[i - w]) { region[i - w] = 1; queue[tail++] = i - w; }
            if (y < h - 1 && !region[i + w] && !wall[i + w]) { region[i + w] = 1; queue[tail++] = i + w; }
        }
        return { region: region, open: open, count: count };
    }

    // The enclosed spaces that mostly lie within the area: buildings and lettering inside the plot, also a building or a shed whose lines
    // touch the boundary. A space counts when at least `share` of its edge is next to the region (reach = thickness of the lines).
    // Spaces that reach the border of the picture, and the neighbouring plots (only one side next to the region), do not count.
    function addIslands(region, wall, w, h, reach, share, smallShare) {
        var regionArea = 0;
        for (var c = 0; c < w * h; c++) regionArea += region[c];
        var near = dilate(region, w, h, reach), out = new Uint8Array(region), label = new Uint8Array(w * h), queue = new Int32Array(w * h);
        for (var s = 0; s < w * h; s++) {
            if (region[s] || wall[s] || label[s]) continue;
            var head = 0, tail = 0, border = false, edge = 0, edgeNear = 0;
            label[s] = 1; queue[tail++] = s;
            while (head < tail) {
                var i = queue[head++], x = i % w, y = (i - x) / w, onEdge = false;
                if (x === 0 || y === 0 || x === w - 1 || y === h - 1) border = true;
                var nb = [x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, y > 0 ? i - w : -1, y < h - 1 ? i + w : -1];
                for (var k = 0; k < 4; k++) {
                    var j = nb[k];
                    if (j < 0) continue;
                    if (wall[j]) onEdge = true;
                    else if (!region[j] && !label[j]) { label[j] = 1; queue[tail++] = j; }
                }
                if (onEdge) { edge++; if (near[i]) edgeNear++; }
            }
            // a small space that has at least 45 % of its edge at the region (a shed on the boundary) counts as well; a big one is another plot, and a building of the neighbour touches the region on one side only
            var small = tail <= 0.08 * regionArea && edgeNear >= (smallShare || 0.45) * edge;
            if (!border && edge > 0 && (edgeNear >= share * edge || small))
                for (var q = 0; q < tail; q++) out[queue[q]] = 1;
        }
        return out;
    }

    // Opening: takes the set in and out again; spikes and bridges thinner than 2r vanish
    function open(mask, w, h, r) {
        var inv = new Uint8Array(w * h), i;
        for (i = 0; i < w * h; i++) inv[i] = mask[i] ? 0 : 1;
        var eroded = dilate(inv, w, h, r), core = new Uint8Array(w * h);
        for (i = 0; i < w * h; i++) core[i] = eroded[i] ? 0 : 1;
        return dilate(core, w, h, r);
    }

    // The region reaches up to the middle of the lines: adds the line pixels next to it
    function grow(region, wall, w, h, r) {
        var ring = dilate(region, w, h, r), out = new Uint8Array(w * h);
        for (var i = 0; i < w * h; i++) out[i] = region[i] || (ring[i] && wall[i]) ? 1 : 0;
        return out;
    }

    var DX = [1, 1, 0, -1, -1, -1, 0, 1], DY = [0, 1, 1, 1, 0, -1, -1, -1];   // clockwise on the screen, starting east

    // The outline of a region without holes as a list of pixel centres (Moore neighbour tracing)
    function trace(region, w, h) {
        var start = -1, i;
        for (i = 0; i < w * h; i++) if (region[i]) { start = i; break; }
        if (start < 0) return [];
        function inside(x, y) { return x >= 0 && y >= 0 && x < w && y < h && region[y * w + x] === 1; }
        var cx = start % w, cy = (start - cx) / w;
        var sx = cx, sy = cy, back = 4;       // the first pixel in reading order: its west neighbour is empty
        var points = [[cx + 0.5, cy + 0.5]], firstNext = null, guard = 0, limit = 8 * w * h;
        while (guard++ < limit) {
            var moved = false;
            for (var k = 1; k <= 8; k++) {
                var d = (back + k) % 8, nx = cx + DX[d], ny = cy + DY[d];
                if (!inside(nx, ny)) continue;
                var pd = (back + k - 1) % 8, px = cx + DX[pd], py = cy + DY[pd];       // the last empty neighbour looked at
                if (cx === sx && cy === sy && firstNext && nx === firstNext[0] && ny === firstNext[1]) return points;
                if (!firstNext) firstNext = [nx, ny];
                var ddx = px - nx, ddy = py - ny;
                for (var j = 0; j < 8; j++) if (DX[j] === ddx && DY[j] === ddy) { back = j; break; }
                cx = nx; cy = ny;
                points.push([cx + 0.5, cy + 0.5]);
                moved = true;
                break;
            }
            if (!moved) return points;      // a single pixel
        }
        return points;
    }

    function distToSegment(p, a, b) {
        var dx = b[0] - a[0], dy = b[1] - a[1], len2 = dx * dx + dy * dy;
        if (len2 === 0) return Math.hypot(p[0] - a[0], p[1] - a[1]);
        var t = Math.max(0, Math.min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / len2));
        return Math.hypot(p[0] - (a[0] + t * dx), p[1] - (a[1] + t * dy));
    }

    // Douglas-Peucker on a closed outline: keeps only the corners that matter (tolerance in pixels)
    function simplify(points, tolerance) {
        var n = points.length;
        if (n <= 3) return points.slice();
        var far = 0, best = -1, i;
        for (i = 1; i < n; i++) {
            var d = Math.hypot(points[i][0] - points[0][0], points[i][1] - points[0][1]);
            if (d > best) { best = d; far = i; }
        }
        var keep = new Uint8Array(n);
        keep[0] = 1; keep[far] = 1;
        var stack = [[0, far], [far, n]];       // the second part ends at the start again (index n = index 0)
        while (stack.length) {
            var seg = stack.pop(), a = seg[0], b = seg[1];
            var pa = points[a], pb = points[b % n], idx = -1, max = tolerance;
            for (i = a + 1; i < b; i++) {
                var dist = distToSegment(points[i], pa, pb);
                if (dist > max) { max = dist; idx = i; }
            }
            if (idx >= 0) { keep[idx] = 1; stack.push([a, idx], [idx, b]); }
        }
        var out = [];
        for (i = 0; i < n; i++) if (keep[i]) out.push(points[i]);
        return out;
    }

    // Drops corners that are closer than `distance` to the one before (the result of small jags)
    function apart(points, distance) {
        var out = [];
        points.forEach(function (p) {
            var last = out[out.length - 1];
            if (!last || Math.hypot(p[0] - last[0], p[1] - last[1]) >= distance) out.push(p);
        });
        while (out.length > 3 && Math.hypot(out[0][0] - out[out.length - 1][0], out[0][1] - out[out.length - 1][1]) < distance) out.pop();
        return out;
    }

    function polygonArea(p) {
        var s = 0;
        for (var i = 0; i < p.length; i++) { var q = p[(i + 1) % p.length]; s += p[i][0] * q[1] - q[0] * p[i][1]; }
        return Math.abs(s) / 2;
    }

    // The closest pixel without a wall around the start (the click may land on the line itself or next to it)
    function nearestFree(wall, w, h, x, y, radius) {
        for (var r = 0; r <= radius; r++)
            for (var dy = -r; dy <= r; dy++)
                for (var dx = -r; dx <= r; dx++) {
                    if (Math.max(Math.abs(dx), Math.abs(dy)) !== r) continue;
                    var px = x + dx, py = y + dy;
                    if (px >= 0 && py >= 0 && px < w && py < h && !wall[py * w + px]) return [px, py];
                }
        return null;
    }

    // The area around the click up to the lines. Result: { status, points, gap }
    //   status "ok"    points = corners (picture pixels), gap = how much the lines were widened to close the gaps
    //   status "line"  the click is on a line (no area)
    //   status "open"  the outline is not closed even with wide gaps (the area reaches the border of the picture)
    //   status "tiny"  the area is too small to be an area (lettering, a dot)
    // options: gap (first gap size in pixels), tolerance (corner simplification in pixels), minArea (pixels)
    function polygonAt(data, w, h, x, y, options) {
        var o = options || {};
        var gap = o.gap || Math.max(1, Math.round(Math.max(w, h) / 600));
        var base = lineMask(data, w, h);
        var px = Math.max(0, Math.min(w - 1, Math.floor(x))), py = Math.max(0, Math.min(h - 1, Math.floor(y)));
        var status = "open";
        if (base[py * w + px]) return { status: "line", points: [], gap: gap };       // exactly on a line: it is not clear which side is meant
        var steps = [1, 2, 4, 8];       // widen the lines more and more until the outline is closed
        for (var step = 0; step < steps.length; step++) {
            var r = gap * steps[step];
            var wall = dilate(base, w, h, r);
            var seed = nearestFree(wall, w, h, px, py, r + 2);
            if (!seed) return { status: "line", points: [], gap: r };
            var f = fill(wall, w, h, seed[0], seed[1]);
            if (f.open) { status = "open"; continue; }
            var region = f.region, thin = wall;
            if (r > gap) {
                // closed only with wide lines: the wide result limits the area, inside of it the thin lines are used again
                // (otherwise buildings next to the boundary would melt into it)
                var allowed = dilate(f.region, w, h, r + 1), walls = new Uint8Array(w * h);
                thin = dilate(base, w, h, gap);
                for (var i = 0; i < w * h; i++) walls[i] = thin[i] || !allowed[i] ? 1 : 0;
                var seed2 = nearestFree(walls, w, h, px, py, r + 2);
                if (!seed2) return { status: "line", points: [], gap: r };
                region = fill(walls, w, h, seed2[0], seed2[1]).region;
            }
            var reach = o.reach || gap * 2 + 4;       // the lines are several pixels thick: how far the region is from a space behind a line
            region = grow(addIslands(region, thin, w, h, reach, o.share || 0.5, o.smallShare), thin, w, h, reach);
            region = open(region, w, h, o.open || Math.max(1, Math.round(Math.max(w, h) / 400)));
            var tol = o.tolerance || Math.max(1.5, r * 0.6 + 1);
            var corners = apart(simplify(trace(region, w, h), tol), tol);
            if (corners.length < 3 || polygonArea(corners) < (o.minArea || 100)) return { status: "tiny", points: [], gap: r };
            return { status: "ok", points: corners.map(function (p) { return [Math.round(p[0] * 10) / 10, Math.round(p[1] * 10) / 10]; }), gap: r };
        }
        return { status: status, points: [], gap: gap * 8 };
    }

    var api = { lineMask: lineMask, dilate: dilate, fill: fill, addIslands: addIslands, trace: trace, simplify: simplify, polygonAt: polygonAt };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.GardenTrace = api;
})(typeof self !== "undefined" ? self : this);
