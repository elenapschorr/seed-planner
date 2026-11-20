// Drawing the plot: the helpers that make SVG elements, the areas with their kinds and figures (what is built, what is free), and the drawing of one shape
// with its corners, edge numbers and lengths. The page gives what it needs as `app` and gets the functions back.
(function (root) {
    "use strict";

    function create(app) {
        var G = app.G,
            T = app.T,
            data = app.data,
            model = app.model,
            view = app.view,
            svg = app.svg;

        // ---- drawing
        function make(name, attrs, style) {
            var e = document.createElementNS(app.NS, name);
            for (var k in attrs) e.setAttribute(k, attrs[k]);
            if (style) for (var s in style) e.style[s] = style[s];
            return e;
        }
        function label(text, x, y, size) {
            var t = make("text", { x: x, y: y, "text-anchor": "middle", "dominant-baseline": "central" }, {
                fontSize: size + "px", fill: "var(--sp-ink)", stroke: "var(--sp-paper)", strokeWidth: size * 0.3 + "px", paintOrder: "stroke", pointerEvents: "none"
            });
            t.textContent = text;
            return t;
        }

        // how an area of a kind is drawn
        function kindLook(kind) {
            var looks = {
                Garden: { fill: "rgba(143,63,41,0.22)", stroke: "var(--sp-accent)", dash: 0 },
                Pond: { fill: "rgba(60,110,150,0.45)", stroke: "var(--sp-ink)", dash: 1 },
                Shed: { fill: "rgba(120,90,60,0.5)", stroke: "var(--sp-ink)", dash: 0 },
                Mine: { fill: "rgba(31,42,51,0.06)", stroke: "var(--sp-ink)", dash: 1 },
                Excluded: { fill: "rgba(120,120,120,0.5)", stroke: "var(--sp-muted)", dash: 2 },
                House: { fill: "rgba(31,42,51,0.5)", stroke: "var(--sp-ink)", dash: 0 },
                Terrace: { fill: "rgba(201,191,170,0.7)", stroke: "var(--sp-ink)", dash: 1 },
                Garage: { fill: "rgba(90,90,90,0.5)", stroke: "var(--sp-ink)", dash: 0 },
                Parking: { fill: "rgba(110,125,140,0.45)", stroke: "var(--sp-ink)", dash: 1 },
                Occupied: { fill: "rgba(31,42,51,0.35)", stroke: "var(--sp-ink)", dash: 1 }
            };
            return looks[kind] || looks.Occupied;
        }
        // what the areas mean for the numbers: not mine is cut out, built-on kinds are taken off, garden areas are what is available
        app.BUILT = ["House", "Shed", "Terrace", "Garage", "Parking", "Pond", "Occupied"];
        // The parts of the plot that the straight line makes count as areas of their owner (a part of a neighbour is cut out and credited to him)
        app.skipImplicit = false;
        function implicitZones() {
            if (app.skipImplicit || !model.cutLine) return [];
            var cp = app.cutParts(), result = [];
            if (!cp) return result;
            [model.cutLine.owner1, model.cutLine.owner2].forEach(function (owner, i) {
                if (owner > 0 && cp.pieces[i]) result.push({ name: "", kind: "Mine", owner: owner, points: cp.pieces[i] });
            });
            return result;
        }
        function zonePoints(test) {
            return model.zones.concat(implicitZones()).filter(function (z) { return z.points.length >= 3 && test(z); }).map(function (z) { return z.points; });
        }
        function kindPoints(list) { return zonePoints(function (z) { return z.owner === 0 && list.indexOf(z.kind) >= 0; }); }
        function neighborPoints() { return zonePoints(function (z) { return z.owner > 0; }); }
        // in pixels squared: the whole, cut out, built on and available (the garden areas if there are any, otherwise what is left)
        function figures(bound) {
            var ex = neighborPoints(), bu = kindPoints(app.BUILT), ga = kindPoints(["Garden"]);
            var a = G.areas(bound, ex, bu), available = a.free;
            if (ga.length) available = a.free - G.areaWithout(bound, ex.concat(bu, ga));
            return { total: a.total, cut: a.cut, built: a.occupied, free: Math.max(0, a.free), available: Math.max(0, available), hasGarden: ga.length > 0 };
        }
        // the part of one area that counts: inside the plot and not cut out
        function zoneArea(z) {
            if (z.points.length < 3) return 0;
            var ex = neighborPoints();
            if (z.owner > 0) return G.area(model.boundary) - G.areaWithout(model.boundary, [z.points]);
            return G.areaWithout(model.boundary, ex) - G.areaWithout(model.boundary, ex.concat([z.points]));
        }
        // The area of every neighbour inside the plot (pixels squared); where areas of two neighbours overlap it goes to the first one
        function neighborAreas() {
            var result = [];
            for (var k = 1; k <= model.neighbors.length; k++) {
                var before = zonePoints(function (z) { return z.owner > 0 && z.owner < k; });
                var own = zonePoints(function (z) { return z.owner === k; });
                result.push(own.length === 0 ? 0 : G.areaWithout(model.boundary, before) - G.areaWithout(model.boundary, before.concat(own)));
            }
            return result;
        }
        function uniqueName(base) {
            var taken = function (n) { return model.zones.some(function (z) { return z.name === n; }); };
            if (!taken(base)) return base;
            for (var i = 2; ; i++) if (!taken(base + " " + i)) return base + " " + i;
        }

        function drawShape(points, isZone, isActive, r, zone) {
            if (points.length >= 2) {
                var look = isZone ? kindLook(zone.owner > 0 ? "Excluded" : zone.kind) : null;
                var list = points.map(function (p) { return p[0] + "," + p[1]; }).join(" ");
                var poly = make(points.length >= 3 ? "polygon" : "polyline", { points: list }, {
                    fill: points.length >= 3 ? (isZone ? look.fill : "rgba(143,63,41,0.12)") : "none",
                    stroke: isZone ? look.stroke : "var(--sp-accent)",
                    strokeWidth: r / 2.5,
                    strokeDasharray: isZone ? (look.dash === 0 ? "none" : (r * look.dash + "," + r * look.dash / 2)) : "none"
                });
                // only the area that is edited takes presses (its corners and moving it); the boundary and the other areas lie over it and would catch the clicks
                poly.style.pointerEvents = isZone && isActive ? "auto" : "none";
                if (isZone && isActive) {
                    // dragging the area itself moves it (a click without moving still sets a corner of a free area)
                    poly.style.cursor = "move";
                    poly.addEventListener("pointerdown", function (e) {
                        if (app.anyToolActive()) return;
                        app.moveDrag = { start: app.toSvgRaw(e), orig: zone.points.map(function (p) { return [p[0], p[1]]; }), started: false };
                        svg.setPointerCapture(e.pointerId);
                    });
                }
                app.shapes.appendChild(poly);
            }
            if (!isZone && points.length >= 3) {
                for (var i = 0; i < points.length; i++) {      // number of every edge at its middle
                    var a = points[i], b = points[(i + 1) % points.length];
                    app.shapes.appendChild(label(String(i + 1), (a[0] + b[0]) / 2, (a[1] + b[1]) / 2, r * 1.8));
                }
            }
            if (isZone && points.length >= 3) {
                var cx = points.reduce(function (s, p) { return s + p[0]; }, 0) / points.length;
                var cy = points.reduce(function (s, p) { return s + p[1]; }, 0) / points.length;
                app.shapes.appendChild(label(zone.name + (zone.owner > 0 ? " \u00b7 " + app.ownerLabel(zone.owner) : ""), cx, cy, r * 1.6));
            }
            if (isZone && isActive && zone.isShape && points.length >= 3) app.drawResizeHandles(zone, r);
            if (app.editMode && !isZone) return;      // the corners of the plot are set on the other page
                    if (!isActive || (isZone && zone.isShape)) return;   // ready-made shapes have no single corners to move
            points.forEach(function (p, i) {
                var chosen = i === view.selected;
                var c = make("circle", { cx: p[0], cy: p[1], r: (i === 0 ? r * 1.3 : r) * (chosen ? 1.4 : 1) }, {
                    fill: chosen ? "var(--sp-ink)" : (i === 0 ? "var(--sp-paper)" : "var(--sp-accent)"),
                    stroke: chosen ? "var(--sp-ink)" : "var(--sp-accent)", strokeWidth: r / 3, cursor: "move", touchAction: "none"
                });
                // a larger invisible circle takes the presses: the visible corner is small
                c.style.pointerEvents = "none";
                var hit = make("circle", { cx: p[0], cy: p[1], r: r * 5 }, { fill: "transparent", cursor: "move", touchAction: "none" });
                hit.addEventListener("pointerdown", function (e) {
                    if (app.anyToolActive()) return;      // the click belongs to the tool: it goes on to the picture
                    e.preventDefault();
                    e.stopPropagation();
                    // a second press on the same corner within a short time deletes it (the click events go to the picture while a corner is held)
                    var now = Date.now();
                    if (app.lastPress && app.lastPress.index === i && now - app.lastPress.time < 450) {
                        app.lastPress = null;
                        app.removeCorner(i);
                        return;
                    }
                    app.lastPress = { index: i, time: now };
                    if (view.drawing && points.length >= 3) app.setDrawing(false);
                    // the press does not move the focus (preventDefault): a field that had it keeps it and would swallow the Delete key
                    var focused = document.activeElement;
                    if (focused && focused !== document.body && focused.blur) focused.blur();
                    view.suppressClick = true;
                    if (view.selected !== i) { view.selected = i; app.showSelection(); }
                    app.drag = { index: i };
                    view.moved = false;
                    svg.setPointerCapture(e.pointerId);
                });
                hit.addEventListener("contextmenu", function (e) { e.preventDefault(); e.stopPropagation(); app.removeCorner(i); });
                app.shapes.appendChild(c);
                app.shapes.appendChild(hit);
            });
        }

        app.make = make;
        app.label = label;
        app.kindPoints = kindPoints;
        app.neighborPoints = neighborPoints;
        app.figures = figures;
        app.zoneArea = zoneArea;
        app.neighborAreas = neighborAreas;
        app.uniqueName = uniqueName;
        app.drawShape = drawShape;

    }

    var api = { create: create };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.GardenRender = api;
})(typeof self !== "undefined" ? self : this);
