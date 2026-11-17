// Geometry of the garden plot page (corners are [x, y] pixels of the plot picture).
// The calculations of the area are the same as PlotGeometry on the server (tests/js/garden-geometry.test.js checks both
// with the same numbers); creating shapes and splitting an area by a line are only needed in the browser.
(function (root) {
    "use strict";

    function edgeLen(pts, i) {
        var a = pts[i], b = pts[(i + 1) % pts.length];
        return Math.hypot(b[0] - a[0], b[1] - a[1]);
    }

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
        var orientation = Math.sign(signedArea(pts));
        if (turn === 0 || orientation === 0) return deg;
        return turn * orientation > 0 ? deg : 360 - deg;
    }

    function side(a, b, c) { return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]); }

    // True if two edges that do not touch each other cross
    function selfIntersects(pts) {
        var n = pts.length;
        if (n < 4) return false;
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++) {
                if (j === i + 1 || (i === 0 && j === n - 1)) continue;
                var a = pts[i], b = pts[(i + 1) % n], c = pts[j], d = pts[(j + 1) % n];
                if (side(c, d, a) * side(c, d, b) < 0 && side(a, b, c) * side(a, b, d) < 0) return true;
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

    function merge(list) {
        list.sort(function (p, q) { return p[0] - q[0]; });
        var result = [];
        list.forEach(function (iv) {
            var last = result[result.length - 1];
            if (last && iv[0] <= last[1]) last[1] = Math.max(last[1], iv[1]);
            else result.push([iv[0], iv[1]]);
        });
        return result;
    }

    function crossingX(s, t) {
        var d1 = side(t[0], t[1], s[0]), d2 = side(t[0], t[1], s[1]);
        var d3 = side(s[0], s[1], t[0]), d4 = side(s[0], s[1], t[1]);
        if (d1 * d2 >= 0 || d3 * d4 >= 0) return null;
        return t[0][0] + (t[1][0] - t[0][0]) * (d3 / (d3 - d4));
    }

    function areaWithout(bound, zoneList) {
        if (bound.length < 3) return 0;
        var cut = zoneList.filter(function (z) { return z.length >= 3; });
        var segs = [];
        [bound].concat(cut).forEach(function (poly) {
            poly.forEach(function (a, i) { segs.push([a, poly[(i + 1) % poly.length]]); });
        });
        var xs = [];
        segs.forEach(function (s) { xs.push(s[0][0], s[1][0]); });
        for (var i = 0; i < segs.length; i++)
            for (var j = i + 1; j < segs.length; j++) {
                var x = crossingX(segs[i], segs[j]);
                if (x !== null) xs.push(x);
            }
        xs.sort(function (p, q) { return p - q; });
        var total = 0;
        for (var k = 0; k + 1 < xs.length; k++) {
            var width = xs[k + 1] - xs[k];
            if (width <= 1e-12) continue;
            var middle = xs[k] + width / 2;
            var blocked = merge([].concat.apply([], cut.map(function (z) { return intervals(z, middle); })));
            var free = 0;
            intervals(bound, middle).forEach(function (iv) {
                var len = iv[1] - iv[0];
                blocked.forEach(function (c) { len -= Math.max(0, Math.min(iv[1], c[1]) - Math.max(iv[0], c[0])); });
                free += len;
            });
            total += free * width;
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
    function round(p) { return [Math.round(p[0] * 10) / 10, Math.round(p[1] * 10) / 10]; }

    function axes(angleRad) {
        return { u: [Math.cos(angleRad), Math.sin(angleRad)], v: [-Math.sin(angleRad), Math.cos(angleRad)] };
    }

    function at(cx, cy, ax, su, sv) {
        return round([cx + ax.u[0] * su + ax.v[0] * sv, cy + ax.u[1] * su + ax.v[1] * sv]);
    }

    function rectangle(cx, cy, w, h, angleRad) {
        var ax = axes(angleRad);
        return [at(cx, cy, ax, -w / 2, -h / 2), at(cx, cy, ax, w / 2, -h / 2), at(cx, cy, ax, w / 2, h / 2), at(cx, cy, ax, -w / 2, h / 2)];
    }

    // a and b are the half axes
    function ellipse(cx, cy, a, b, angleRad, steps) {
        var ax = axes(angleRad), n = steps || 48, result = [];
        for (var k = 0; k < n; k++) {
            var t = 2 * Math.PI * k / n;
            result.push(at(cx, cy, ax, a * Math.cos(t), b * Math.sin(t)));
        }
        return result;
    }

    // The right angle is at (cx, cy); the legs are a (along the angle) and b (across)
    function rightTriangle(cx, cy, a, b, angleRad) {
        var ax = axes(angleRad);
        return [at(cx, cy, ax, 0, 0), at(cx, cy, ax, a, 0), at(cx, cy, ax, 0, b)];
    }

    // Symmetric trapezoid: the parallel sides are bottom (at -h/2) and top (at +h/2), both centered on (cx, cy)
    function trapezoid(cx, cy, bottom, top, h, angleRad) {
        var ax = axes(angleRad);
        return [at(cx, cy, ax, -bottom / 2, -h / 2), at(cx, cy, ax, bottom / 2, -h / 2), at(cx, cy, ax, top / 2, h / 2), at(cx, cy, ax, -top / 2, h / 2)];
    }

    // Trapezoid with two right angles on the left side: bottom and top are the parallel sides, h the leg at right angles to them
    function rightTrapezoid(cx, cy, bottom, top, h, angleRad) {
        var ax = axes(angleRad), w = Math.max(bottom, top);
        return [at(cx, cy, ax, -w / 2, -h / 2), at(cx, cy, ax, -w / 2 + bottom, -h / 2), at(cx, cy, ax, -w / 2 + top, h / 2), at(cx, cy, ax, -w / 2, h / 2)];
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
        // no double corners
        return out.map(round).filter(function (p, i, list) {
            var q = list[(i + list.length - 1) % list.length];
            return list.length < 2 || p[0] !== q[0] || p[1] !== q[1];
        });
    }

    function splitByLine(poly, nx, ny, t0) {
        function valid(p) { return p.length >= 3 && area(p) > 1e-6 ? p : null; }
        return { below: valid(clipHalfPlane(poly, nx, ny, t0, true)), above: valid(clipHalfPlane(poly, nx, ny, t0, false)) };
    }

    // The position t0 of the line (see splitByLine) at which the "below" part has the wanted area (same unit as area());
    // null if the wanted area is not between 0 and the whole area. Bisection: the area below grows with t0.
    function splitPositionForArea(poly, nx, ny, wanted) {
        var total = area(poly), ext = extent(poly, nx, ny);
        if (!(wanted > 0) || !(wanted < total)) return null;
        var lo = ext.min, hi = ext.max;
        for (var k = 0; k < 60; k++) {
            var mid = (lo + hi) / 2, below = clipHalfPlane(poly, nx, ny, mid, true);
            if (area(below) < wanted) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }

    // Subject minus clip (Greiner-Hormann). Returns { polys: [polygons that remain], hole: the clip polygon if it lies completely inside the subject (then the
    // result has a hole and cannot be a simple polygon) or null }. The clip polygon must not touch the subject exactly (see subtractAll, which moves it a tiny bit).
    function difference(subject, clip) {
        function build(points) {
            var list = points.map(function (p) { return { x: p[0], y: p[1], intersect: false, alpha: 0, neighbor: null, fwd: false, processed: false }; });
            list.forEach(function (v, i) { v.next = list[(i + 1) % list.length]; v.prev = list[(i + list.length - 1) % list.length]; });
            return list;
        }
        var S = build(subject), C = build(clip), any = false;
        function insert(after, v) {
            var at = after;
            while (at.next.intersect && at.next.alpha < v.alpha) at = at.next;
            v.next = at.next; v.prev = at; at.next.prev = v; at.next = v;
        }
        var S0 = S.slice(), C0 = C.slice();
        for (var i = 0; i < S0.length; i++) {
            var a = S0[i], b = S0[(i + 1) % S0.length];
            for (var j = 0; j < C0.length; j++) {
                var c = C0[j], d = C0[(j + 1) % C0.length];
                var den = (b.x - a.x) * (d.y - c.y) - (b.y - a.y) * (d.x - c.x);
                if (Math.abs(den) < 1e-12) continue;
                var t = ((c.x - a.x) * (d.y - c.y) - (c.y - a.y) * (d.x - c.x)) / den;
                var u = ((c.x - a.x) * (b.y - a.y) - (c.y - a.y) * (b.x - a.x)) / den;
                if (!(t > 0 && t < 1 && u > 0 && u < 1)) continue;
                var x = a.x + t * (b.x - a.x), y = a.y + t * (b.y - a.y);
                var p = { x: x, y: y, intersect: true, alpha: t, neighbor: null, fwd: false, processed: false };
                var q = { x: x, y: y, intersect: true, alpha: u, neighbor: p, fwd: false, processed: false };
                p.neighbor = q;
                insert(a, p); insert(c, q);
                any = true;
            }
        }
        if (!any) {
            if (pointInPolygon(subject[0], clip)) return { polys: [], hole: null };
            if (pointInPolygon(clip[0], subject)) return { polys: [subject], hole: clip };
            return { polys: [subject], hole: null };
        }
        // S is left (forward) where it goes out of the clip polygon; C is followed in the direction that stays inside the subject
        function mark(first, other, forwardWhenInside) {
            var inside = pointInPolygon([first.x, first.y], other), v = first;
            do {
                if (v.intersect) { inside = !inside; v.fwd = forwardWhenInside ? inside : !inside; }
                v = v.next;
            } while (v !== first);
        }
        mark(S[0], clip, false);
        mark(C[0], subject, true);
        var result = [];
        var starts = [];
        var v0 = S[0];
        do { if (v0.intersect && v0.fwd) starts.push(v0); v0 = v0.next; } while (v0 !== S[0]);
        starts.forEach(function (start) {
            if (start.processed) return;
            var poly = [[start.x, start.y]], cur = start, guard = 0;
            do {
                cur.processed = true; cur.neighbor.processed = true;
                var fw = cur.fwd;
                do { cur = fw ? cur.next : cur.prev; poly.push([cur.x, cur.y]); } while (!cur.intersect && ++guard < 100000);
                cur = cur.neighbor;
            } while (!cur.processed && guard < 100000);
            result.push(poly);
        });
        return { polys: result, hole: null };
    }

    // Subject polygons minus several polygons; the clip polygons are moved by a tiny amount so that they never touch an edge exactly. Points are rounded to 0.1.
    // A corner of a clip polygon that lies inside the subject but no further than tol from its outline is moved out over the edge (by tol): an area that was
    // drawn up to the edge of the plot but stops just short of it then cuts the plot as intended instead of counting as a hole.
    function pushOut(p, polys, tol) {
        var best = null;
        polys.forEach(function (poly) {
            for (var i = 0; i < poly.length; i++) {
                var a = poly[i], b = poly[(i + 1) % poly.length], dx = b[0] - a[0], dy = b[1] - a[1], len2 = dx * dx + dy * dy;
                var t = len2 === 0 ? 0 : Math.max(0, Math.min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / len2));
                var q = [a[0] + t * dx, a[1] + t * dy], d = Math.hypot(p[0] - q[0], p[1] - q[1]);
                if (d <= tol && (!best || d < best.d)) best = { d: d, q: q };
            }
        });
        if (!best) return p;
        if (!polys.some(function (poly) { return pointInPolygon(p, poly); })) return p;      // already outside: it crosses the edge anyway
        var ux = best.q[0] - p[0], uy = best.q[1] - p[1], len = Math.hypot(ux, uy);
        if (len < 1e-9) return p;
        return [best.q[0] + ux / len * tol, best.q[1] + uy / len * tol];
    }

    function subtractAll(subjects, clips, tol) {
        var polys = subjects.map(function (p) { return p.map(function (q) { return [q[0], q[1]]; }); }), holes = [];
        clips.forEach(function (clip, k) {
            var shifted = tol > 0 ? clip.map(function (p) { return pushOut(p, polys, tol); }) : clip;
            var moved = shifted.map(function (p, i) { return [p[0] + 0.0013 * Math.sin(i * 12.9898 + k * 78.233 + 1), p[1] + 0.0013 * Math.cos(i * 4.1414 + k * 37.719 + 1)]; });
            var next = [];
            polys.forEach(function (poly) {
                var r = difference(poly, moved);
                r.polys.forEach(function (q) { next.push(q); });
                if (r.hole) holes.push(clip);
            });
            polys = next;
        });
        polys = polys.map(function (poly) {
            return poly.map(round).filter(function (p, i, list) {
                var q = list[(i + list.length - 1) % list.length];
                return p[0] !== q[0] || p[1] !== q[1];
            });
        }).filter(function (p) { return p.length >= 3 && area(p) > 0.5; });
        return { polys: polys, holes: holes };
    }

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
        var polys = [a, b];
        for (var k = 0; k < 2; k++) {
            var poly = polys[k];
            for (var i = 0; i < poly.length; i++) {
                var p = poly[i], q = poly[(i + 1) % poly.length];
                var nx = p[1] - q[1], ny = q[0] - p[0], len = Math.hypot(nx, ny);
                if (len === 0) continue;
                nx /= len; ny /= len;
                var lo1 = Infinity, hi1 = -Infinity, lo2 = Infinity, hi2 = -Infinity;
                a.forEach(function (v) { var t = v[0] * nx + v[1] * ny; lo1 = Math.min(lo1, t); hi1 = Math.max(hi1, t); });
                b.forEach(function (v) { var t = v[0] * nx + v[1] * ny; lo2 = Math.min(lo2, t); hi2 = Math.max(hi2, t); });
                if (hi1 <= lo2 + eps || hi2 <= lo1 + eps) return false;
            }
        }
        return true;
    }

    // The outline of a bed in pixels: centre, length (along x before turning) and width in meters, angle in degrees, scale in meters per pixel;
    // oval = ellipse with the length and width as axes, otherwise a rectangle
    function bedOutline(cx, cy, lengthM, widthM, angleDeg, oval, scale) {
        var hl = lengthM / scale / 2, hw = widthM / scale / 2, t = angleDeg * Math.PI / 180, c = Math.cos(t), s = Math.sin(t), local = [];
        if (oval) for (var i = 0; i < 32; i++) local.push([hl * Math.cos(i * Math.PI / 16), hw * Math.sin(i * Math.PI / 16)]);
        else local = [[-hl, -hw], [hl, -hw], [hl, hw], [-hl, hw]];
        return local.map(function (p) { return round([cx + p[0] * c - p[1] * s, cy + p[0] * s + p[1] * c]); });
    }

    var api = {
        difference: difference, subtractAll: subtractAll, pointInPolygon: pointInPolygon, convexOverlap: convexOverlap, bedOutline: bedOutline,
        edgeLen: edgeLen, signedArea: signedArea, area: area, metersPerPixel: metersPerPixel, angle: angle,
        selfIntersects: selfIntersects, areaWithout: areaWithout, areas: areas,
        rectangle: rectangle, ellipse: ellipse, rightTriangle: rightTriangle, trapezoid: trapezoid, rightTrapezoid: rightTrapezoid, splitPositionForArea: splitPositionForArea,
        extent: extent, splitByLine: splitByLine
    };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.GardenGeometry = api;
})(this);
