// Geometry of the garden plot page (corners are [x, y] pixels of the plot picture).
// The calculations of the area are the same as PlotGeometry on the server (tests/js/garden-geometry.test.js checks both
// with the same numbers); creating shapes and splitting an area by a line are only needed in the browser.
(function (root) {
    "use strict";

    // ---- tolerances and sizes
    var TINY_AREA = 1e-6;               // a part of a split that is smaller than this is no part
    var MIN_LEFTOVER_AREA = 0.5;        // pixels squared: smaller leftovers of a cut are dropped
    var PARALLEL_EPSILON = 1e-12;       // two edges whose cross product is smaller than this are parallel
    var NARROW_STRIP = 1e-12;           // a strip between two crossings that is narrower than this is skipped
    var SAME_POINT = 1e-9;              // closer than this: the same point
    var JITTER = 0.0013;                // the clip polygons are moved by this much so that they never touch an edge exactly
    var BISECTION_STEPS = 60;
    var ELLIPSE_STEPS = 48;
    var BED_OVAL_STEPS = 32;
    var MAX_WALK_STEPS = 100000;        // safety stop when the polygons are walked

    // ---- points and edges
    function roundPoint(p) { return [Math.round(p[0] * 10) / 10, Math.round(p[1] * 10) / 10]; }

    // The corner list without a corner that is the same as the one before it
    function withoutRepeatedCorners(points) {
        return points.filter(function (p, i, list) {
            var before = list[(i + list.length - 1) % list.length];
            return list.length < 2 || p[0] !== before[0] || p[1] !== before[1];
        });
    }

    function edgeLen(pts, i) {
        var a = pts[i], b = pts[(i + 1) % pts.length];
        return Math.hypot(b[0] - a[0], b[1] - a[1]);
    }

    // The point of the edge a-b that is nearest to p
    function closestPointOnSegment(p, a, b) {
        var dx = b[0] - a[0], dy = b[1] - a[1], lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared === 0 ? 0 : Math.max(0, Math.min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / lengthSquared));
        return [a[0] + t * dx, a[1] + t * dy];
    }

    function distanceToSegment(p, a, b) {
        var q = closestPointOnSegment(p, a, b);
        return Math.hypot(p[0] - q[0], p[1] - q[1]);
    }

    // Positive if c lies left of the line a-b, negative if right, 0 on it
    function orientation(a, b, c) { return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]); }

    // ---- area, scale, angles
    function signedArea(pts) {
        var s = 0;
        for (var i = 0; i < pts.length; i++) {
            var a = pts[i], b = pts[(i + 1) % pts.length];
            s += a[0] * b[1] - b[0] * a[1];
        }
        return s / 2;
    }

    function area(pts) { return pts.length < 3 ? 0 : Math.abs(signedArea(pts)); }

    // Meters per pixel from the known edge lengths (least squares); null if there is none
    function metersPerPixel(pts, lengths) {
        var num = 0, den = 0;
        for (var i = 0; i < pts.length; i++) {
            var m = lengths[i];
            if (!(m > 0)) continue;
            var px = edgeLen(pts, i);
            num += m * px;
            den += px * px;
        }
        return den > 0 ? num / den : null;
    }

    // Interior angle at corner i in degrees (more than 180 where the line bends inward)
    function angle(pts, i) {
        var n = pts.length, a = pts[(i + n - 1) % n], b = pts[i], c = pts[(i + 1) % n];
        var deg = Math.atan2(Math.abs((a[0] - b[0]) * (c[1] - b[1]) - (a[1] - b[1]) * (c[0] - b[0])),
            (a[0] - b[0]) * (c[0] - b[0]) + (a[1] - b[1]) * (c[1] - b[1])) * 180 / Math.PI;
        var turn = (b[0] - a[0]) * (c[1] - b[1]) - (b[1] - a[1]) * (c[0] - b[0]);
        var direction = Math.sign(signedArea(pts));
        if (turn === 0 || direction === 0) return deg;
        return turn * direction > 0 ? deg : 360 - deg;
    }

    // True if two edges that do not touch each other cross
    function selfIntersects(pts) {
        var n = pts.length;
        if (n < 4) return false;
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++) {
                if (j === i + 1 || (i === 0 && j === n - 1)) continue;
                var a = pts[i], b = pts[(i + 1) % n], c = pts[j], d = pts[(j + 1) % n];
                if (orientation(c, d, a) * orientation(c, d, b) < 0 && orientation(a, b, c) * orientation(a, b, d) < 0) return true;
            }
        return false;
    }

    // ---- area of the boundary with areas cut out: vertical strips between all corners and edge crossings;
    // in a strip the free length of a vertical line changes evenly, so the middle value times the width is exact
    function intervals(pts, x) {
        var ys = [];
        for (var i = 0; i < pts.length; i++) {
            var a = pts[i], b = pts[(i + 1) % pts.length];
            if ((a[0] < x && b[0] > x) || (b[0] < x && a[0] > x))
                ys.push(a[1] + (b[1] - a[1]) * (x - a[0]) / (b[0] - a[0]));
        }
        ys.sort(function (p, q) { return p - q; });
        var result = [];
        for (var k = 0; k + 1 < ys.length; k += 2) result.push([ys[k], ys[k + 1]]);
        return result;
    }

    function mergeIntervals(list) {
        list.sort(function (p, q) { return p[0] - q[0]; });
        var result = [];
        list.forEach(function (iv) {
            var last = result[result.length - 1];
            if (last && iv[0] <= last[1]) last[1] = Math.max(last[1], iv[1]);
            else result.push([iv[0], iv[1]]);
        });
        return result;
    }

    // x of the crossing of two edges (each [from, to]); null if they do not cross
    function crossingX(s, t) {
        var d1 = orientation(t[0], t[1], s[0]), d2 = orientation(t[0], t[1], s[1]);
        var d3 = orientation(s[0], s[1], t[0]), d4 = orientation(s[0], s[1], t[1]);
        if (d1 * d2 >= 0 || d3 * d4 >= 0) return null;
        return t[0][0] + (t[1][0] - t[0][0]) * (d3 / (d3 - d4));
    }

    function edgesOf(polygons) {
        var edges = [];
        polygons.forEach(function (poly) {
            poly.forEach(function (a, i) { edges.push([a, poly[(i + 1) % poly.length]]); });
        });
        return edges;
    }

    // All x at which the strips change: the corners and the crossings of two edges, from left to right
    function stripBorders(edges) {
        var xs = [];
        edges.forEach(function (e) { xs.push(e[0][0], e[1][0]); });
        for (var i = 0; i < edges.length; i++)
            for (var j = i + 1; j < edges.length; j++) {
                var x = crossingX(edges[i], edges[j]);
                if (x !== null) xs.push(x);
            }
        return xs.sort(function (p, q) { return p - q; });
    }

    // The free length of the vertical line at x: inside the boundary and outside all cut out areas
    function freeLength(bound, cut, x) {
        var blocked = mergeIntervals([].concat.apply([], cut.map(function (z) { return intervals(z, x); })));
        var free = 0;
        intervals(bound, x).forEach(function (iv) {
            var len = iv[1] - iv[0];
            blocked.forEach(function (c) { len -= Math.max(0, Math.min(iv[1], c[1]) - Math.max(iv[0], c[0])); });
            free += len;
        });
        return free;
    }

    function areaWithout(bound, zoneList) {
        if (bound.length < 3) return 0;
        var cut = zoneList.filter(function (z) { return z.length >= 3; });
        var xs = stripBorders(edgesOf([bound].concat(cut)));
        var total = 0;
        for (var k = 0; k + 1 < xs.length; k++) {
            var width = xs[k + 1] - xs[k];
            if (width <= NARROW_STRIP) continue;
            total += freeLength(bound, cut, xs[k] + width / 2) * width;
        }
        return total;
    }

    // Total, cut out (not mine), occupied (mine, built on) and free area; the same as PlotGeometry.Areas
    function areas(bound, excluded, occupied) {
        var total = area(bound);
        var mine = areaWithout(bound, excluded);
        var free = areaWithout(bound, excluded.concat(occupied));
        return { total: total, cut: total - mine, occupied: mine - free, free: free };
    }

    // ---- shapes (sizes in pixels, angle in radians; the first side runs along the angle)
    // The corners are given in the own system of the shape (u along the angle, v across) around the centre (cx, cy)
    function placeShape(cx, cy, angleRad, local) {
        var cos = Math.cos(angleRad), sin = Math.sin(angleRad);
        return local.map(function (p) { return roundPoint([cx + cos * p[0] + (-sin) * p[1], cy + sin * p[0] + cos * p[1]]); });
    }

    function rectangle(cx, cy, w, h, angleRad) {
        return placeShape(cx, cy, angleRad, [[-w / 2, -h / 2], [w / 2, -h / 2], [w / 2, h / 2], [-w / 2, h / 2]]);
    }

    // a and b are the half axes
    function ellipse(cx, cy, a, b, angleRad, steps) {
        var n = steps || ELLIPSE_STEPS, local = [];
        for (var k = 0; k < n; k++) {
            var t = 2 * Math.PI * k / n;
            local.push([a * Math.cos(t), b * Math.sin(t)]);
        }
        return placeShape(cx, cy, angleRad, local);
    }

    // The right angle is at (cx, cy); the legs are a (along the angle) and b (across)
    function rightTriangle(cx, cy, a, b, angleRad) {
        return placeShape(cx, cy, angleRad, [[0, 0], [a, 0], [0, b]]);
    }

    // Symmetric trapezoid: the parallel sides are bottom (at -h/2) and top (at +h/2), both centered on (cx, cy)
    function trapezoid(cx, cy, bottom, top, h, angleRad) {
        return placeShape(cx, cy, angleRad, [[-bottom / 2, -h / 2], [bottom / 2, -h / 2], [top / 2, h / 2], [-top / 2, h / 2]]);
    }

    // Trapezoid with two right angles on the left side: bottom and top are the parallel sides, h the leg at right angles to them
    function rightTrapezoid(cx, cy, bottom, top, h, angleRad) {
        var left = -Math.max(bottom, top) / 2;
        return placeShape(cx, cy, angleRad, [[left, -h / 2], [left + bottom, -h / 2], [left + top, h / 2], [left, h / 2]]);
    }

    // The outline of a bed in pixels: centre, length (along x before turning) and width in meters, angle in degrees, scale in meters per pixel;
    // oval = ellipse with the length and width as axes, otherwise a rectangle
    function bedOutline(cx, cy, lengthM, widthM, angleDeg, oval, scale) {
        var halfLength = lengthM / scale / 2, halfWidth = widthM / scale / 2, angleRad = angleDeg * Math.PI / 180;
        return oval
            ? ellipse(cx, cy, halfLength, halfWidth, angleRad, BED_OVAL_STEPS)
            : rectangle(cx, cy, 2 * halfLength, 2 * halfWidth, angleRad);
    }

    // ---- splitting an area by a line: the line is perpendicular to the unit vector (nx, ny) and passes the points p with
    // p . n = t0; "below" is the part with p . n <= t0, "above" the other one (null if there is nothing of it)
    function extent(pts, nx, ny) {
        var min = Infinity, max = -Infinity;
        pts.forEach(function (p) {
            var t = p[0] * nx + p[1] * ny;
            if (t < min) min = t;
            if (t > max) max = t;
        });
        return { min: min, max: max };
    }

    function clipHalfPlane(poly, nx, ny, t0, keepBelow) {
        var sign = keepBelow ? 1 : -1, out = [];
        function dist(p) { return sign * (p[0] * nx + p[1] * ny - t0); }
        function cross(p, c, dp, dc) {
            var t = dp / (dp - dc);
            return [p[0] + (c[0] - p[0]) * t, p[1] + (c[1] - p[1]) * t];
        }
        for (var i = 0; i < poly.length; i++) {
            var cur = poly[i], prev = poly[(i + poly.length - 1) % poly.length];
            var dc = dist(cur), dp = dist(prev);
            if (dc <= 0) {
                if (dp > 0) out.push(cross(prev, cur, dp, dc));
                out.push(cur);
            } else if (dp <= 0) out.push(cross(prev, cur, dp, dc));
        }
        return withoutRepeatedCorners(out.map(roundPoint));
    }

    function splitByLine(poly, nx, ny, t0) {
        function valid(p) { return p.length >= 3 && area(p) > TINY_AREA ? p : null; }
        return { below: valid(clipHalfPlane(poly, nx, ny, t0, true)), above: valid(clipHalfPlane(poly, nx, ny, t0, false)) };
    }

    // The position t0 of the line (see splitByLine) at which the "below" part has the wanted area (same unit as area());
    // null if the wanted area is not between 0 and the whole area. Bisection: the area below grows with t0.
    function splitPositionForArea(poly, nx, ny, wanted) {
        var total = area(poly), ext = extent(poly, nx, ny);
        if (!(wanted > 0) || !(wanted < total)) return null;
        var lo = ext.min, hi = ext.max;
        for (var k = 0; k < BISECTION_STEPS; k++) {
            var mid = (lo + hi) / 2, below = clipHalfPlane(poly, nx, ny, mid, true);
            if (area(below) < wanted) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }

    // ---- point in polygon, overlap
    // Is the point inside the polygon (ray casting; points on the edge may go either way)
    function pointInPolygon(p, poly) {
        var inside = false;
        for (var i = 0, j = poly.length - 1; i < poly.length; j = i++) {
            var a = poly[i], b = poly[j];
            if ((a[1] > p[1]) !== (b[1] > p[1]) && p[0] < (b[0] - a[0]) * (p[1] - a[1]) / (b[1] - a[1]) + a[0]) inside = !inside;
        }
        return inside;
    }

    // Do two convex polygons overlap (separating axis); touching only is no overlap
    function convexOverlap(a, b, eps) {
        eps = eps || 0;
        return [a, b].every(function (poly) {
            return poly.every(function (p, i) {
                var q = poly[(i + 1) % poly.length];
                var nx = p[1] - q[1], ny = q[0] - p[0], len = Math.hypot(nx, ny);
                if (len === 0) return true;
                nx /= len; ny /= len;
                var onA = extent(a, nx, ny), onB = extent(b, nx, ny);
                return !(onA.max <= onB.min + eps || onB.max <= onA.min + eps);
            });
        });
    }

    // ---- polygon minus polygon (Greiner-Hormann): the corners of both polygons and their crossings are rings of linked vertices
    function createVertex(x, y, crossing, alpha, neighbor) {
        return { x: x, y: y, intersect: crossing, alpha: alpha, neighbor: neighbor, fwd: false, processed: false };
    }

    function buildRing(points) {
        var ring = points.map(function (p) { return createVertex(p[0], p[1], false, 0, null); });
        ring.forEach(function (v, i) { v.next = ring[(i + 1) % ring.length]; v.prev = ring[(i + ring.length - 1) % ring.length]; });
        return ring;
    }

    // Puts the crossing v into the ring after the vertex "after", in the order of alpha (the place along the edge)
    function insertCrossing(after, v) {
        var cursor = after;
        while (cursor.next.intersect && cursor.next.alpha < v.alpha) cursor = cursor.next;
        v.next = cursor.next; v.prev = cursor; cursor.next.prev = v; cursor.next = v;
    }

    // Finds all crossings of the edges of the two rings and puts them into both; true if there is any
    function insertCrossings(subjectCorners, clipCorners) {
        var found = false;
        for (var i = 0; i < subjectCorners.length; i++) {
            var a = subjectCorners[i], b = subjectCorners[(i + 1) % subjectCorners.length];
            for (var j = 0; j < clipCorners.length; j++) {
                var c = clipCorners[j], d = clipCorners[(j + 1) % clipCorners.length];
                var den = (b.x - a.x) * (d.y - c.y) - (b.y - a.y) * (d.x - c.x);
                if (Math.abs(den) < PARALLEL_EPSILON) continue;
                var t = ((c.x - a.x) * (d.y - c.y) - (c.y - a.y) * (d.x - c.x)) / den;
                var u = ((c.x - a.x) * (b.y - a.y) - (c.y - a.y) * (b.x - a.x)) / den;
                if (!(t > 0 && t < 1 && u > 0 && u < 1)) continue;
                var x = a.x + t * (b.x - a.x), y = a.y + t * (b.y - a.y);
                var onSubject = createVertex(x, y, true, t, null);
                var onClip = createVertex(x, y, true, u, onSubject);
                onSubject.neighbor = onClip;
                insertCrossing(a, onSubject);
                insertCrossing(c, onClip);
                found = true;
            }
        }
        return found;
    }

    // Tells every crossing of the ring in which direction the walk goes on (forward or backward)
    function markDirections(first, other, forwardWhenInside) {
        var inside = pointInPolygon([first.x, first.y], other), v = first;
        do {
            if (v.intersect) { inside = !inside; v.fwd = forwardWhenInside ? inside : !inside; }
            v = v.next;
        } while (v !== first);
    }

    // The polygons that are walked from the crossings where the subject goes out of the clip polygon
    function walkPolygons(subjectRing) {
        var starts = [], v = subjectRing[0], result = [];
        do { if (v.intersect && v.fwd) starts.push(v); v = v.next; } while (v !== subjectRing[0]);
        starts.forEach(function (start) {
            if (start.processed) return;
            var poly = [[start.x, start.y]], cur = start, steps = 0;
            do {
                cur.processed = true; cur.neighbor.processed = true;
                var forward = cur.fwd;
                do { cur = forward ? cur.next : cur.prev; poly.push([cur.x, cur.y]); } while (!cur.intersect && ++steps < MAX_WALK_STEPS);
                cur = cur.neighbor;
            } while (!cur.processed && steps < MAX_WALK_STEPS);
            result.push(poly);
        });
        return result;
    }

    // Subject minus clip. Returns { polys: [polygons that remain], hole: the clip polygon if it lies completely inside the subject (then the
    // result has a hole and cannot be a simple polygon) or null }. The clip polygon must not touch the subject exactly (see subtractAll, which moves it a tiny bit).
    function difference(subject, clip) {
        var subjectRing = buildRing(subject), clipRing = buildRing(clip);
        var subjectCorners = subjectRing.slice(), clipCorners = clipRing.slice();
        if (!insertCrossings(subjectCorners, clipCorners)) {
            if (pointInPolygon(subject[0], clip)) return { polys: [], hole: null };
            if (pointInPolygon(clip[0], subject)) return { polys: [subject], hole: clip };
            return { polys: [subject], hole: null };
        }
        // the subject is left (forward) where it goes out of the clip polygon; the clip polygon is followed in the direction that stays inside the subject
        markDirections(subjectRing[0], clip, false);
        markDirections(clipRing[0], subject, true);
        return { polys: walkPolygons(subjectRing), hole: null };
    }

    // A corner of a clip polygon that lies inside the subject but no further than tol from its outline is moved out over the edge (by tol): an area that was
    // drawn up to the edge of the plot but stops just short of it then cuts the plot as intended instead of counting as a hole.
    function pushOut(p, polys, tol) {
        var best = null;
        polys.forEach(function (poly) {
            for (var i = 0; i < poly.length; i++) {
                var q = closestPointOnSegment(p, poly[i], poly[(i + 1) % poly.length]), d = Math.hypot(p[0] - q[0], p[1] - q[1]);
                if (d <= tol && (!best || d < best.d)) best = { d: d, q: q };
            }
        });
        if (!best) return p;
        if (!polys.some(function (poly) { return pointInPolygon(p, poly); })) return p;      // already outside: it crosses the edge anyway
        var ux = best.q[0] - p[0], uy = best.q[1] - p[1], len = Math.hypot(ux, uy);
        if (len < SAME_POINT) return p;
        return [best.q[0] + ux / len * tol, best.q[1] + uy / len * tol];
    }

    // The corner moved by a tiny amount (the same every time) so that no corner lies exactly on an edge
    function jitter(p, cornerIndex, clipIndex) {
        return [p[0] + JITTER * Math.sin(cornerIndex * 12.9898 + clipIndex * 78.233 + 1), p[1] + JITTER * Math.cos(cornerIndex * 4.1414 + clipIndex * 37.719 + 1)];
    }

    // Subject polygons minus several polygons; the clip polygons are moved by a tiny amount so that they never touch an edge exactly. Points are rounded to 0.1.
    function subtractAll(subjects, clips, tol) {
        var polys = subjects.map(function (p) { return p.map(function (q) { return [q[0], q[1]]; }); }), holes = [];
        clips.forEach(function (clip, k) {
            var shifted = tol > 0 ? clip.map(function (p) { return pushOut(p, polys, tol); }) : clip;
            var moved = shifted.map(function (p, i) { return jitter(p, i, k); });
            var next = [];
            polys.forEach(function (poly) {
                var r = difference(poly, moved);
                r.polys.forEach(function (q) { next.push(q); });
                if (r.hole) holes.push(clip);
            });
            polys = next;
        });
        polys = polys
            .map(function (poly) { return withoutRepeatedCorners(poly.map(roundPoint)); })
            .filter(function (p) { return p.length >= 3 && area(p) > MIN_LEFTOVER_AREA; });
        return { polys: polys, holes: holes };
    }

    var api = {
        difference: difference, subtractAll: subtractAll, pointInPolygon: pointInPolygon, convexOverlap: convexOverlap, bedOutline: bedOutline,
        edgeLen: edgeLen, signedArea: signedArea, area: area, metersPerPixel: metersPerPixel, angle: angle,
        selfIntersects: selfIntersects, areaWithout: areaWithout, areas: areas,
        rectangle: rectangle, ellipse: ellipse, rightTriangle: rightTriangle, trapezoid: trapezoid, rightTrapezoid: rightTrapezoid, splitPositionForArea: splitPositionForArea,
        extent: extent, splitByLine: splitByLine,
        closestPointOnSegment: closestPointOnSegment, distanceToSegment: distanceToSegment
    };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.GardenGeometry = api;
})(this);
