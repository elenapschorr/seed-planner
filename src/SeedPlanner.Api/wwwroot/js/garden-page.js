(function () {
    var dataEl = document.getElementById("garden-data");
    var svg = document.getElementById("plot");
    if (!dataEl || !svg || !window.GardenGeometry) return;
    var G = window.GardenGeometry;
var editMode = document.getElementById("stage").getAttribute("data-mode") === "edit";      // second page: only the own part, beds and areas; the plot itself is not changed here

    var NS = "http://www.w3.org/2000/svg";
    var data = JSON.parse(dataEl.textContent);
    var T = JSON.parse(document.getElementById("garden-text").textContent);
    var boundary = data.boundary || [];
    var lengths = data.lengths || [];
    var zones = data.zones || [];
    var cutLine = data.cutLine && data.cutLine.a ? { a: data.cutLine.a, b: data.cutLine.b, owner1: data.cutLine.owner1 | 0, owner2: data.cutLine.owner2 | 0 } : null;   // the straight line through the plot (saved) and who owns the two parts
    var neighbors = (data.neighbors || []).slice();   // names of the neighbours (an area owned by neighbour k has owner k)
    zones.forEach(function (z) {
        if (!T.kinds[z.kind]) z.kind = "Occupied";
        z.isShape = !!z.isShape;
        z.owner = z.owner > 0 ? z.owner | 0 : 0;
        z.splitGroup = z.splitGroup | 0;
        z.splitPart = z.splitPart | 0;
        z.splitParent = z.splitParent || null;
        z.splitLine = z.splitLine || null;
        z.use = z.use || "";
        z.sun = z.sun || "";
        z.soil = z.soil || "";
        z.slope = z.slope || "";
        z.waterlogged = !!z.waterlogged;
        z.waterNearby = !!z.waterNearby;
        z.coldFramePossible = !!z.coldFramePossible;
        if (z.kind === "Excluded") {      // the first kind of "not mine": now a neighbour owns it
            z.kind = "Mine";
            if (z.owner === 0) z.owner = 1;
        }
        while (neighbors.length < z.owner) neighbors.push("");
    });
    function ownerLabel(o) { return o === 0 ? T.ownerMe : (neighbors[o - 1] || T.neighborName.replace("{0}", String(o))); }
    var active = -1;            // -1 = the boundary, otherwise the index of an area
    var W = 1000, H = 1000;     // size of the picture in pixels
    var drag = null;            // a corner that is moved
    var selected = -1;          // the selected corner of the shape that is edited
    var lastPress = null;       // the last press on a corner (for deleting by a double press)
    var suppressClick = false;  // the click that ends a press on a corner does not set a new corner
    var cutDrawing = false;     // the next two clicks set the line
    var cutStart = null;
    var cutDrag = null;         // an end of the straight line that is moved
    var resizeDrag = null;      // a handle of a shape that is dragged
    var moveDrag = null;        // an area that is moved as a whole
    var moved = false;
    var placing = false;        // the next click sets a ready-made shape
    var lastMouse = null;
    var tableEdges = 0;
    var zoom = 1;
    var wrap = document.getElementById("plot-wrap");
    var lang = document.documentElement.lang || undefined;

    var picture = document.createElementNS(NS, "image");
    picture.setAttribute("href", svg.dataset.src);
    var shapes = document.createElementNS(NS, "g");
    var overlay = document.createElementNS(NS, "g");
    overlay.style.pointerEvents = "none";
    var defs = document.createElementNS(NS, "defs");
    var mineClip = document.createElementNS(NS, "clipPath");
    mineClip.setAttribute("id", "mine-clip");
    var mineMask = document.createElementNS(NS, "mask");
    mineMask.setAttribute("id", "mine-mask");
    mineMask.setAttribute("maskUnits", "userSpaceOnUse");
    mineMask.setAttribute("x", "0");
    mineMask.setAttribute("y", "0");
    defs.appendChild(mineClip);
    defs.appendChild(mineMask);
    var bedLayer = document.createElementNS(NS, "g");
    svg.appendChild(defs);
    svg.appendChild(picture);
    svg.appendChild(shapes);
    svg.appendChild(bedLayer);
    svg.appendChild(overlay);

    var probe = new Image();
    probe.onload = function () {
        W = probe.naturalWidth;
        H = probe.naturalHeight;
        svg.setAttribute("viewBox", "0 0 " + W + " " + H);
        picture.setAttribute("width", W);
        picture.setAttribute("height", H);
        redraw();
        if (mineOnly) fitMine(); else fitZoom();
    };
    probe.src = svg.dataset.src;

    function fmt(v, digits) {
        return v.toLocaleString(lang, { minimumFractionDigits: digits, maximumFractionDigits: digits });
    }
    // The scale (meters per pixel): from the edge lengths the user entered, or from the area of the whole plot
    var scaleMode = document.getElementById("scale-mode");
    var plotArea = document.getElementById("plot-area");
    function areaMode() { return scaleMode.value === "area"; }
    // the scale from the edge lengths that were entered, or from the area of the plot; the other one is calculated from it and cannot be changed
    function edgesScale() { return boundary.length < 3 ? null : G.metersPerPixel(boundary, lengths); }
    function areaScale() {
        var wanted = parseFloat(plotArea.value), pixels = G.area(boundary);
        return boundary.length >= 3 && wanted > 0 && pixels > 0 ? Math.sqrt(wanted / pixels) : null;
    }
    function scaleNow() { return areaMode() ? areaScale() : edgesScale(); }
    function showScaleMode() {
        plotArea.readOnly = !areaMode();      // area given: it is entered here and the edges are calculated; edges given: the area is calculated
        document.getElementById("scale-note").textContent = areaMode() ? T.scaleAreaNote : T.scaleEdgesNote;
        tableEdges = -1;      // the length fields are built (or hidden) again
        update();
    }
    // fills in the length of every edge from the area (the proportions are those of the drawing) and goes back to single lengths
    // takes over the calculated values and changes to the other kind of input: from the edges the area is calculated, from the area the edges
    function changeMode(to) {
        if (to === "area") {
            var s = edgesScale();
            if (s !== null) plotArea.value = (G.area(boundary) * s * s).toFixed(2);
        } else {
            var a = areaScale();
            if (a !== null) for (var i = 0; i < boundary.length; i++) lengths[i] = Math.round(G.edgeLen(boundary, i) * a * 100) / 100;
        }
        scaleMode.value = to;
        showScaleMode();
        drawOverlay();
    }
    document.getElementById("calc-edges").addEventListener("click", function () { changeMode(areaMode() ? "edges" : "area"); });
    if (data.knownArea > 0) {
        scaleMode.value = "area";
        plotArea.value = data.knownArea;
    }
    scaleMode.addEventListener("change", function () { changeMode(scaleMode.value); });
    plotArea.addEventListener("input", function () { update(); drawOverlay(); });

    // the tools lie over the picture and fold away
    var toolsBody = document.getElementById("tools-body"), toolsToggle = document.getElementById("tools-toggle");
    function showTools(on) {
        toolsBody.hidden = !on;
        toolsToggle.setAttribute("aria-expanded", on ? "true" : "false");
    }
    toolsToggle.addEventListener("click", function () { showTools(toolsBody.hidden); });
    showTools(true);
    function activeZone() { return active >= 0 ? zones[active] : null; }
    function unit() { return Math.max(W, H) / 120 / zoom; }   // size of a corner marker; constant on the screen when zoomed

    // ---- drawing
    function make(name, attrs, style) {
        var e = document.createElementNS(NS, name);
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
    var BUILT = ["House", "Shed", "Terrace", "Garage", "Parking", "Pond", "Occupied"];
    // The parts of the plot that the straight line makes count as areas of their owner (a part of a neighbour is cut out and credited to him)
    var skipImplicit = false;
    function implicitZones() {
        if (skipImplicit || !cutLine) return [];
        var cp = cutParts(), result = [];
        if (!cp) return result;
        [cutLine.owner1, cutLine.owner2].forEach(function (owner, i) {
            if (owner > 0 && cp.pieces[i]) result.push({ name: "", kind: "Mine", owner: owner, points: cp.pieces[i] });
        });
        return result;
    }
    function zonePoints(test) {
        return zones.concat(implicitZones()).filter(function (z) { return z.points.length >= 3 && test(z); }).map(function (z) { return z.points; });
    }
    function kindPoints(list) { return zonePoints(function (z) { return z.owner === 0 && list.indexOf(z.kind) >= 0; }); }
    function neighborPoints() { return zonePoints(function (z) { return z.owner > 0; }); }
    // in pixels squared: the whole, cut out, built on and available (the garden areas if there are any, otherwise what is left)
    function figures(bound) {
        var ex = neighborPoints(), bu = kindPoints(BUILT), ga = kindPoints(["Garden"]);
        var a = G.areas(bound, ex, bu), available = a.free;
        if (ga.length) available = a.free - G.areaWithout(bound, ex.concat(bu, ga));
        return { total: a.total, cut: a.cut, built: a.occupied, free: Math.max(0, a.free), available: Math.max(0, available), hasGarden: ga.length > 0 };
    }
    // the part of one area that counts: inside the plot and not cut out
    function zoneArea(z) {
        if (z.points.length < 3) return 0;
        var ex = neighborPoints();
        if (z.owner > 0) return G.area(boundary) - G.areaWithout(boundary, [z.points]);
        return G.areaWithout(boundary, ex) - G.areaWithout(boundary, ex.concat([z.points]));
    }
    // The area of every neighbour inside the plot (pixels squared); where areas of two neighbours overlap it goes to the first one
    function neighborAreas() {
        var result = [];
        for (var k = 1; k <= neighbors.length; k++) {
            var before = zonePoints(function (z) { return z.owner > 0 && z.owner < k; });
            var own = zonePoints(function (z) { return z.owner === k; });
            result.push(own.length === 0 ? 0 : G.areaWithout(boundary, before) - G.areaWithout(boundary, before.concat(own)));
        }
        return result;
    }
    function uniqueName(base) {
        var taken = function (n) { return zones.some(function (z) { return z.name === n; }); };
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
                    if (placing || cutDrawing) return;
                    moveDrag = { start: toSvgRaw(e), orig: zone.points.map(function (p) { return [p[0], p[1]]; }), started: false };
                    svg.setPointerCapture(e.pointerId);
                });
            }
            shapes.appendChild(poly);
        }
        if (!isZone && points.length >= 3) {
            for (var i = 0; i < points.length; i++) {      // number of every edge at its middle
                var a = points[i], b = points[(i + 1) % points.length];
                shapes.appendChild(label(String(i + 1), (a[0] + b[0]) / 2, (a[1] + b[1]) / 2, r * 1.8));
            }
        }
        if (isZone && points.length >= 3) {
            var cx = points.reduce(function (s, p) { return s + p[0]; }, 0) / points.length;
            var cy = points.reduce(function (s, p) { return s + p[1]; }, 0) / points.length;
            shapes.appendChild(label(zone.name + (zone.owner > 0 ? " \u00b7 " + ownerLabel(zone.owner) : ""), cx, cy, r * 1.6));
        }
        if (isZone && isActive && zone.isShape && points.length >= 3) drawResizeHandles(zone, r);
        if (editMode && !isZone) return;      // the corners of the plot are set on the other page
                if (!isActive || (isZone && zone.isShape)) return;   // ready-made shapes have no single corners to move
        points.forEach(function (p, i) {
            var chosen = i === selected;
            var c = make("circle", { cx: p[0], cy: p[1], r: (i === 0 ? r * 1.3 : r) * (chosen ? 1.4 : 1) }, {
                fill: chosen ? "var(--sp-ink)" : (i === 0 ? "var(--sp-paper)" : "var(--sp-accent)"),
                stroke: chosen ? "var(--sp-ink)" : "var(--sp-accent)", strokeWidth: r / 3, cursor: "move", touchAction: "none"
            });
            // a larger invisible circle takes the presses: the visible corner is small
            c.style.pointerEvents = "none";
            var hit = make("circle", { cx: p[0], cy: p[1], r: r * 5 }, { fill: "transparent", cursor: "move", touchAction: "none" });
            hit.addEventListener("pointerdown", function (e) {
                if (cutDrawing || placing || tracing || placingBed) return;      // the click belongs to the tool: it goes on to the picture
                e.preventDefault();
                e.stopPropagation();
                // a second press on the same corner within a short time deletes it (the click events go to the picture while a corner is held)
                var now = Date.now();
                if (lastPress && lastPress.index === i && now - lastPress.time < 450) {
                    lastPress = null;
                    removeCorner(i);
                    return;
                }
                lastPress = { index: i, time: now };
                if (drawing && points.length >= 3) setDrawing(false);
                // the press does not move the focus (preventDefault): a field that had it keeps it and would swallow the Delete key
                var focused = document.activeElement;
                if (focused && focused !== document.body && focused.blur) focused.blur();
                suppressClick = true;
                if (selected !== i) { selected = i; showSelection(); }
                drag = { index: i };
                moved = false;
                svg.setPointerCapture(e.pointerId);
            });
            hit.addEventListener("contextmenu", function (e) { e.preventDefault(); e.stopPropagation(); removeCorner(i); });
            shapes.appendChild(c);
            shapes.appendChild(hit);
        });
    }

    // ---- resizing a ready-made shape: handles on its box (turned like the shape); the points are scaled along the box axes
    function frameOf(pts) {
        var n = pts.length, ang;
        if (n >= 24) {      // ellipse / circle: the first point lies on the first axis
            var cx = pts.reduce(function (s, p) { return s + p[0]; }, 0) / n, cy = pts.reduce(function (s, p) { return s + p[1]; }, 0) / n;
            ang = Math.atan2(pts[0][1] - cy, pts[0][0] - cx);
        } else ang = Math.atan2(pts[1][1] - pts[0][1], pts[1][0] - pts[0][0]);
        var u = [Math.cos(ang), Math.sin(ang)], v = [-Math.sin(ang), Math.cos(ang)];
        var us = pts.map(function (p) { return p[0] * u[0] + p[1] * u[1]; }), vs = pts.map(function (p) { return p[0] * v[0] + p[1] * v[1]; });
        return { u: u, v: v, umin: Math.min.apply(null, us), umax: Math.max.apply(null, us), vmin: Math.min.apply(null, vs), vmax: Math.max.apply(null, vs) };
    }
    function drawResizeHandles(zone, r) {
        var f = frameOf(zone.points);
        function at(a, b) { return [a * f.u[0] + b * f.v[0], a * f.u[1] + b * f.v[1]]; }
        var box = [at(f.umin, f.vmin), at(f.umax, f.vmin), at(f.umax, f.vmax), at(f.umin, f.vmax)];
        shapes.appendChild(make("polygon", { points: box.map(function (p) { return p[0] + "," + p[1]; }).join(" ") }, {
            fill: "none", stroke: "var(--sp-accent)", strokeWidth: r / 4, strokeDasharray: r / 2 + "," + r / 2, pointerEvents: "none"
        }));
        [-1, 0, 1].forEach(function (su) {
            [-1, 0, 1].forEach(function (sv) {
                if (su === 0 && sv === 0) return;
                var p = at(su < 0 ? f.umin : su > 0 ? f.umax : (f.umin + f.umax) / 2, sv < 0 ? f.vmin : sv > 0 ? f.vmax : (f.vmin + f.vmax) / 2);
                var side = r * 1.1;
                var h = make("rect", { x: p[0] - side / 2, y: p[1] - side / 2, width: side, height: side }, {
                    fill: "var(--sp-paper)", stroke: "var(--sp-accent)", strokeWidth: r / 3, cursor: "nwse-resize", touchAction: "none"
                });
                h.addEventListener("pointerdown", function (e) {
                    e.preventDefault();
                    e.stopPropagation();
                    resizeDrag = { su: su, sv: sv, f: f, orig: zone.points.map(function (q) { return [q[0], q[1]]; }) };
                    moved = false;
                    svg.setPointerCapture(e.pointerId);
                });
                h.addEventListener("click", function (e) { e.stopPropagation(); moved = false; });
                shapes.appendChild(h);
            });
        });
    }
    function resizeTo(p) {
        var d = resizeDrag, f = d.f, minSize = unit() * 0.5;
        var pu = p[0] * f.u[0] + p[1] * f.u[1], pv = p[0] * f.v[0] + p[1] * f.v[1];
        var umin = d.su < 0 ? Math.min(pu, f.umax - minSize) : f.umin, umax = d.su > 0 ? Math.max(pu, f.umin + minSize) : f.umax;
        var vmin = d.sv < 0 ? Math.min(pv, f.vmax - minSize) : f.vmin, vmax = d.sv > 0 ? Math.max(pv, f.vmin + minSize) : f.vmax;
        detach(zones[active]);
        zones[active].points = d.orig.map(function (q) {
            var qu = q[0] * f.u[0] + q[1] * f.u[1], qv = q[0] * f.v[0] + q[1] * f.v[1];
            var nu = f.umax > f.umin ? umin + (qu - f.umin) / (f.umax - f.umin) * (umax - umin) : qu;
            var nv = f.vmax > f.vmin ? vmin + (qv - f.vmin) / (f.vmax - f.vmin) * (vmax - vmin) : qv;
            return [Math.round((nu * f.u[0] + nv * f.v[0]) * 10) / 10, Math.round((nu * f.u[1] + nv * f.v[1]) * 10) / 10];
        });
    }

    // the two ends of the straight line through the plot can be moved
    function drawCutHandles(r) {
        if (!cutLine || cutDrawing) return;
        ["a", "b"].forEach(function (end) {
            var h = make("circle", { cx: cutLine[end][0], cy: cutLine[end][1], r: r * 1.2 }, {
                fill: "var(--sp-paper)", stroke: "var(--sp-accent)", strokeWidth: r / 3, cursor: "move", touchAction: "none"
            });
            h.addEventListener("pointerdown", function (e) {
                e.preventDefault();
                e.stopPropagation();
                cutDrag = end;
                moved = false;
                suppressClick = true;
                svg.setPointerCapture(e.pointerId);
            });
            shapes.appendChild(h);
        });
    }

    function redraw() {
        shapes.replaceChildren();
        var r = unit() * 0.5;       // corners, numbers and lines of the areas: smaller than the unit that is used for the hit areas
        zones.forEach(function (z, zi) { if (!(mineOnly && z.owner > 0)) drawShape(z.points, true, zi === active, r, z); });
        drawShape(boundary, false, active === -1, r, null);
        drawCutHandles(r);
        fillRefs();
        update();
        drawOverlay();
        applyMine();
        drawBeds();
        fillBeds();
    }

    function toSvgRaw(e) {
        var p = svg.createSVGPoint();
        p.x = e.clientX;
        p.y = e.clientY;
        var q = p.matrixTransform(svg.getScreenCTM().inverse());
        return [Math.round(q.x * 10) / 10, Math.round(q.y * 10) / 10];
    }
    function toSvg(e) {
        var p = toSvgRaw(e);
        return [Math.min(W, Math.max(0, p[0])), Math.min(H, Math.max(0, p[1]))];
    }

    // ---- ready-made shapes
    var shapeType = document.getElementById("shape-type");
    var shapeA = document.getElementById("shape-a");
    var shapeB = document.getElementById("shape-b");
    var shapeC = document.getElementById("shape-c");
    var shapeOrient = document.getElementById("shape-orient");
    var shapeMsg = document.getElementById("shape-msg");
    var placeButton = document.getElementById("place-shape");
    var defaults = { rect: [6, 3], circle: [3, 3], ellipse: [4, 2], triangle: [4, 3], trapezoid: [6, 4, 2.5], righttrapezoid: [6, 4, 2.5] };

    function showShapeType() {
        var cfg = T.shapes[shapeType.value];
        document.getElementById("shape-a-label").textContent = cfg[1];
        document.getElementById("shape-b-label").textContent = cfg[2];
        document.getElementById("shape-b-box").hidden = cfg[2] === "";
        document.getElementById("shape-c-box").hidden = !cfg[3];
        document.getElementById("shape-c-label").textContent = cfg[3] || "";
        shapeA.value = defaults[shapeType.value][0];
        shapeB.value = defaults[shapeType.value][1];
        shapeC.value = defaults[shapeType.value][2] || 2;
    }
    shapeType.addEventListener("change", showShapeType);

    function orientAngle() {
        var v = shapeOrient.value;
        if (!v || v === "h") return 0;
        var i = parseInt(v.split(":")[1], 10), a = boundary[i], b = boundary[(i + 1) % boundary.length];
        return a && b ? Math.atan2(b[1] - a[1], b[0] - a[0]) : 0;
    }
    // The corners of the shape with the entered size (meters) around the point, or null
    function makeShape(c) {
        var s = scaleNow();
        var a = parseFloat(shapeA.value), b = parseFloat(shapeB.value), type = shapeType.value;
        var h = parseFloat(shapeC.value);
        if (!s || !(a > 0) || (type !== "circle" && !(b > 0)) || ((type === "trapezoid" || type === "righttrapezoid") && !(h > 0))) return null;
        var angle = orientAngle();
        if (type === "rect") return G.rectangle(c[0], c[1], a / s, b / s, angle);
        if (type === "circle") return G.ellipse(c[0], c[1], a / 2 / s, a / 2 / s, angle);
        if (type === "righttrapezoid") return G.rightTrapezoid(c[0], c[1], a / s, b / s, h / s, angle);
        if (type === "trapezoid") return G.trapezoid(c[0], c[1], a / s, b / s, h / s, angle);
        if (type === "ellipse") return G.ellipse(c[0], c[1], a / 2 / s, b / 2 / s, angle);
        return G.rightTriangle(c[0], c[1], a / s, b / s, angle);
    }
    function setPlacing(on) {
        if (on && tracing) setTracing(null);
        placing = on;
        placeButton.classList.toggle("active", on);
        svg.style.cursor = on ? "copy" : "crosshair";
        shapeMsg.hidden = !on;
        shapeMsg.textContent = T.placing;
        if (!on) lastMouse = null;
        drawOverlay();
    }
    placeButton.addEventListener("click", function () {
        if (placing) { setPlacing(false); return; }
        setCutDrawing(false);
        if (!scaleNow()) {
            shapeMsg.hidden = false;
            shapeMsg.textContent = T.noScale;
            return;
        }
        setPlacing(true);
    });
    document.addEventListener("keydown", function (e) {
        if (e.key !== "Escape") return;
        if (placing) setPlacing(false);
        if (cutDrawing) setCutDrawing(false);
        if (tracing) setTracing(null);
        setDrawing(false);
        if (inserting) setInserting(false);
    });

    // ---- taking an area from the lines that are already in the picture (cadastral map): one click into the outline
    var tracing = null;         // "zone" or "boundary": the next click takes the area around it
    var pixelData = null;       // the pixels of the picture (read once)
    function getPixels() {
        if (pixelData) return pixelData;
        try {
            var canvas = document.createElement("canvas");
            canvas.width = W; canvas.height = H;
            var ctx = canvas.getContext("2d", { willReadFrequently: true });
            ctx.drawImage(probe, 0, 0, W, H);
            pixelData = ctx.getImageData(0, 0, W, H).data;
        } catch (x) { pixelData = null; }
        return pixelData;
    }
    function traceMessage(kind, text) {
        var box = document.getElementById("trace-msg-" + kind);
        if (!box) return;
        box.hidden = !text;
        box.textContent = text || "";
    }
    function setTracing(kind) {
        if (kind && tracing !== kind) { setPlacing(false); setCutDrawing(false); setPlacingBed(0); }
        var before = tracing;
        tracing = kind;
        ["zone", "boundary"].forEach(function (k) {
            var b = document.getElementById("trace-" + k);
            if (b) b.classList.toggle("active", tracing === k);
            traceMessage(k, tracing === k ? T.traceHint : "");
        });
        svg.style.cursor = tracing ? "cell" : "crosshair";
        if (!kind && before) lastMouse = null;
        drawOverlay();
    }
    function traceAt(p) {
        var data = getPixels(), kind = tracing;
        if (!data || !window.GardenTrace) { traceMessage(kind, T.traceNoImage); return; }
        var r = window.GardenTrace.polygonAt(data, W, H, p[0], p[1]);
        if (r.status !== "ok") {
            traceMessage(kind, r.status === "line" ? T.traceLine : r.status === "tiny" ? T.traceTiny : T.traceOpen);
            return;
        }
        if (kind === "boundary") {
            if (boundary.length >= 3 && !window.confirm(T.traceReplace)) return;
            boundary = r.points;
            lengths = r.points.map(function () { return null; });
            selected = -1;
            showSelection();
            setTracing(null);
            showScaleMode();
            redraw();
            return;
        }
        var input = document.getElementById("zone-name"), k = document.getElementById("zone-kind").value;
        zones.push({ name: input.value.trim() || uniqueName(T.kinds[k]), kind: k, owner: parseInt(document.getElementById("zone-owner").value, 10) || 0, isShape: false, points: r.points });
        input.value = "";
        active = zones.length - 1;
        setTracing(null);
        fillTarget();
        redraw();
    }
    ["zone", "boundary"].forEach(function (k) {
        var b = document.getElementById("trace-" + k);
        if (b) b.addEventListener("click", function () { setTracing(tracing === k ? null : k); });
    });


    // Areas that came from one split know the area before and the line; a hand made change of one of them cuts that link
    function detach(z) {
        if (!z) return;
        z.splitGroup = 0;
        z.splitPart = 0;
        z.splitParent = null;
        z.splitLine = null;
    }
    // the two parts of the split the area belongs to, or null
    function groupOf(z) {
        if (!z || !z.splitGroup || !z.splitParent || !z.splitLine) return null;
        var a = null, b = null;
        zones.forEach(function (o) {
            if (o.splitGroup !== z.splitGroup || !o.splitParent) return;
            if (o.splitPart === 1) a = o;
            if (o.splitPart === 2) b = o;
        });
        return a && b ? { a: a, b: b } : null;
    }
    function splitPoly(z) {
        if (!z) return [];
        return groupOf(z) ? z.splitParent : z.points;
    }

    // ---- splitting an area by a line parallel to an edge
    var splitPanel = document.getElementById("split-panel");
    var splitBox = document.getElementById("split-box");
    var splitNeed = document.getElementById("split-need");
    var splitRef = document.getElementById("split-ref");
    var splitPos = document.getElementById("split-pos");

    function edgeVector(value) {
        if (!value) return null;
        var part = value.split(":"), i = parseInt(part[1], 10);
        if (part[0] === "s") {      // the stored line of a split: along it is across the stored normal
            var g = activeZone() && activeZone().splitLine;
            return g ? [g[1], -g[0]] : null;
        }
        var pts = part[0] === "b" ? boundary : splitPoly(activeZone());
        var a = pts[i], b = pts[(i + 1) % pts.length];
        return a && b ? [b[0] - a[0], b[1] - a[1]] : null;
    }
    // The line: unit vectors along it (dx, dy) and across it (nx, ny), the position t0 and the extent of the area
    function splitLine() {
        var z = activeZone(), d = edgeVector(splitRef.value);
        if (!z || splitPoly(z).length < 3 || !d) return null;
        var len = Math.hypot(d[0], d[1]);
        if (!(len > 0)) return null;
        var nx = -d[1] / len, ny = d[0] / len;
        var ext = G.extent(splitPoly(z), nx, ny);
        return { dx: d[0] / len, dy: d[1] / len, nx: nx, ny: ny, t0: ext.min + parseFloat(splitPos.value) / 100 * (ext.max - ext.min), ext: ext };
    }
    var splitNum = document.getElementById("split-pos-num"), splitDist = document.getElementById("split-dist");
    function setSplitPercent(v) {
        v = Math.min(100, Math.max(0, v));
        splitPos.value = Math.round(v * 10) / 10;
        document.getElementById("split-pos-label").textContent = splitPos.value + " %";
    }
    splitPos.addEventListener("input", function () {
        setSplitPercent(parseFloat(splitPos.value));
        drawOverlay();
    });
    // the position as a number: in percent of the area, or as the distance in meters from the side of part 1
    splitNum.addEventListener("input", function () {
        var v = parseFloat(splitNum.value);
        if (!(v >= 0)) return;
        setSplitPercent(v);
        drawOverlay();
    });
    splitDist.addEventListener("input", function () {
        var line = splitLine(), s = scaleNow(), v = parseFloat(splitDist.value);
        if (!line || !s || !(v >= 0) || !(line.ext.max > line.ext.min)) return;
        setSplitPercent(v / s / (line.ext.max - line.ext.min) * 100);
        drawOverlay();
    });
    // the number fields follow the slider and the reference edge (not while one of them is typed in)
    function syncSplitFields(line) {
        var s = scaleNow();
        if (document.activeElement !== splitNum) splitNum.value = splitPos.value;
        if (document.activeElement !== splitDist)
            splitDist.value = s ? Math.round((line.t0 - line.ext.min) * s * 100) / 100 : "";
        splitDist.disabled = !s;
    }
    splitRef.addEventListener("change", drawOverlay);
    // wanted area (m²) of part 1 -> position of the line
    document.getElementById("split-area-set").addEventListener("click", function () {
        var z = activeZone(), d = edgeVector(splitRef.value), s = scaleNow();
        var wanted = parseFloat(document.getElementById("split-area").value);
        if (!z || !d || !s || !(wanted > 0)) return;
        var len = Math.hypot(d[0], d[1]), nx = -d[1] / len, ny = d[0] / len;
        var t0 = G.splitPositionForArea(splitPoly(z), nx, ny, wanted / (s * s));
        if (t0 === null) return;
        var ext = G.extent(splitPoly(z), nx, ny);
        setSplitPercent((t0 - ext.min) / (ext.max - ext.min) * 100);
        drawOverlay();
    });
    splitPanel.addEventListener("toggle", drawOverlay);
    document.getElementById("split-do").addEventListener("click", function () {
        var z = activeZone(), line = splitLine();
        if (!z || !line) return;
        var parent = splitPoly(z);
        var parts = G.splitByLine(parent, line.nx, line.ny, line.t0);
        if (!parts.below || !parts.above) return;      // the line must run through the area
        var kind1 = document.getElementById("split-kind1").value, kind2 = document.getElementById("split-kind2").value;
        var owner1 = parseInt(document.getElementById("split-owner1").value, 10) || 0, owner2 = parseInt(document.getElementById("split-owner2").value, 10) || 0;
        var stored = [Math.round(line.nx * 1e6) / 1e6, Math.round(line.ny * 1e6) / 1e6, Math.round(line.t0 * 10) / 10];
        var g = groupOf(z);
        if (g) {      // the split is changed: both parts follow the new line
            g.a.points = parts.below; g.a.kind = kind1; g.a.owner = owner1; g.a.splitLine = stored;
            g.b.points = parts.above; g.b.kind = kind2; g.b.owner = owner2; g.b.splitLine = stored;
        } else {
            var id = 1 + zones.reduce(function (m, o) { return Math.max(m, o.splitGroup | 0); }, 0);
            var copy = z.points.map(function (q) { return [q[0], q[1]]; });
            var part = function (name, kind, owner, points, number) {
                return {
                    name: name, kind: kind, owner: owner, isShape: false, points: points, splitGroup: id, splitPart: number, splitParent: copy, splitLine: stored,
                    use: z.use, sun: z.sun, soil: z.soil, slope: z.slope, waterlogged: z.waterlogged, waterNearby: z.waterNearby, coldFramePossible: z.coldFramePossible
                };
            };
            zones.splice(active, 1, part(z.name + " (1)", kind1, owner1, parts.below, 1), part(z.name + " (2)", kind2, owner2, parts.above, 2));
        }
        fillTarget();
        redraw();
    });

    document.getElementById("split-undo").addEventListener("click", function () {
        var z = activeZone(), g = groupOf(z);
        if (!g) return;
        var joined = { name: g.a.name.replace(/ \(1\)$/, ""), kind: g.a.kind, owner: g.a.owner, isShape: false, points: g.a.splitParent.map(function (q) { return [q[0], q[1]]; }), splitGroup: 0, splitPart: 0, splitParent: null, splitLine: null,
            use: g.a.use, sun: g.a.sun, soil: g.a.soil, slope: g.a.slope, waterlogged: g.a.waterlogged, waterNearby: g.a.waterNearby, coldFramePossible: g.a.coldFramePossible };
        var first = Math.min(zones.indexOf(g.a), zones.indexOf(g.b));
        zones = zones.filter(function (o) { return o !== g.a && o !== g.b; });
        zones.splice(first, 0, joined);
        active = first;
        fillTarget();
        redraw();
    });

    var refSignature = "";
    function fillRefs() {
        var z = activeZone();
        var grouped = groupOf(z);
        var canSplit = !!z && splitPoly(z).length >= 3;
        splitBox.hidden = !canSplit;
        splitNeed.hidden = canSplit;
        document.getElementById("split-undo").hidden = !grouped;
        var signature = boundary.length + "|" + active + "|" + (z ? z.points.length : 0) + "|" + (grouped ? z.splitGroup : 0);
        if (signature === refSignature) return;
        refSignature = signature;
        if (grouped) {      // a split that exists: the controls show it, the line can be moved
            document.getElementById("split-kind1").value = grouped.a.kind;
            document.getElementById("split-kind2").value = grouped.b.kind;
            document.getElementById("split-owner1").value = String(grouped.a.owner);
            document.getElementById("split-owner2").value = String(grouped.b.owner);
        } else if (z) {      // both parts start like the area that is split; part 2 goes to the first neighbour if there is one
            document.getElementById("split-kind1").value = z.kind;
            document.getElementById("split-kind2").value = z.kind;
            document.getElementById("split-owner1").value = String(z.owner);
            document.getElementById("split-owner2").value = String(neighbors.length > 0 && z.owner === 0 ? 1 : z.owner);
        }

        function refill(select, entries) {
            var keep = select.value;
            select.replaceChildren();
            entries.forEach(function (e) {
                var o = document.createElement("option");
                o.value = e[0];
                o.textContent = e[1];
                select.appendChild(o);
            });
            var found = entries.some(function (e) { return e[0] === keep; });
            if (found) select.value = keep;
        }
        var orientEntries = [["h", T.orientH]];
        var splitEntries = [];
        if (boundary.length >= 3)
            for (var i = 0; i < boundary.length; i++) {
                orientEntries.push(["b:" + i, T.orientEdge.replace("{0}", String(i + 1))]);
                splitEntries.push(["b:" + i, T.splitBoundaryEdge.replace("{0}", String(i + 1))]);
            }
        if (grouped) splitEntries.unshift(["s", T.splitStored]);
        if (canSplit)
            for (var j = 0; j < splitPoly(z).length; j++)
                splitEntries.push(["z:" + j, T.splitZoneEdge.replace("{0}", String(j + 1))]);
        refill(shapeOrient, orientEntries);
        refill(splitRef, splitEntries);
        if (grouped) {      // start with the stored line: its direction and where it lies
            splitRef.value = "s";
            var line = splitLine();
            if (line) setSplitPercent((z.splitLine[2] - line.ext.min) / (line.ext.max - line.ext.min) * 100);
        }
    }

    // ---- a straight line through the plot: the sizes of both parts
    var cutMsg = document.getElementById("cut-msg");
    function setCutDrawing(on) {
        if (on && tracing) setTracing(null);
        cutDrawing = on;
        cutStart = null;
        if (on) {
            setPlacing(false);
            cutLine = null;
            cutMsg.hidden = false;
            cutMsg.textContent = T.cutDrawing;
        } else cutMsg.hidden = true;
        svg.style.cursor = on ? "crosshair" : (placing ? "copy" : "crosshair");
        if (!on) lastMouse = null;
        update();
        drawOverlay();
    }
    document.getElementById("cut-draw").addEventListener("click", function () { setCutDrawing(!cutDrawing); });
    document.getElementById("cut-clear").addEventListener("click", function () {
        cutLine = null;
        setCutDrawing(false);
    });
    // the two parts of the plot; null without a line or a plot
    function cutParts() {
        if (!cutLine || boundary.length < 3) return null;
        var dx = cutLine.b[0] - cutLine.a[0], dy = cutLine.b[1] - cutLine.a[1], len = Math.hypot(dx, dy);
        if (!(len > 0)) return null;
        var nx = -dy / len, ny = dx / len;
        var parts = G.splitByLine(boundary, nx, ny, cutLine.a[0] * nx + cutLine.a[1] * ny);
        return { dx: dx / len, dy: dy / len, pieces: [parts.below, parts.above] };
    }
    function pieceAreas(piece, scale) {
        if (!piece) return { gross: 0, free: 0, garden: null };
        skipImplicit = true;       // the part itself is judged by the areas that are drawn, not by who owns it
        var f = figures(piece), k = scale * scale;
        skipImplicit = false;
        return { gross: G.area(piece) * k, free: f.free * k, garden: f.hasGarden ? f.available * k : null };
    }
    function updateCut(scale) {
        var parts = cutParts(), result = document.getElementById("cut-result");
        document.getElementById("cut-clear").hidden = !cutLine && !cutDrawing;
        result.hidden = !parts || scale === null;
        if (result.hidden) return;
        parts.pieces.forEach(function (piece, i) {
            var a = pieceAreas(piece, scale);
            document.getElementById("cut-gross" + (i + 1)).textContent = fmt(a.gross, 1) + " m\u00b2";
            document.getElementById("cut-free" + (i + 1)).textContent = fmt(a.free, 1) + " m\u00b2";
            document.getElementById("cut-garden-row" + (i + 1)).hidden = a.garden === null;
            if (a.garden !== null) document.getElementById("cut-garden" + (i + 1)).textContent = fmt(a.garden, 1) + " m\u00b2";
            document.getElementById("cut-take" + (i + 1)).disabled = !piece;
        });
    }
    [1, 2].forEach(function (n) {
        document.getElementById("cut-take" + n).addEventListener("click", function () {
            var parts = cutParts();
            if (!parts || !parts.pieces[n - 1]) return;
            zones.push({ name: T.cutName.replace("{0}", String(n)), kind: document.getElementById("cut-kind" + n).value, owner: parseInt(document.getElementById("cut-owner" + n).value, 10) || 0, isShape: false, points: parts.pieces[n - 1] });
            active = zones.length - 1;
            fillTarget();
            redraw();
        });
    });

    // The shape that follows the mouse while it is placed, and the line that would split the area
    function drawOverlay() {
        overlay.replaceChildren();
        var r = unit();
        if (placing && lastMouse) {
            var pts = makeShape(lastMouse);
            if (pts) overlay.appendChild(make("polygon", { points: pts.map(function (p) { return p[0] + "," + p[1]; }).join(" ") }, {
                fill: "rgba(143,63,41,0.15)", stroke: "var(--sp-accent)", strokeWidth: r / 2.5, strokeDasharray: r + "," + r / 2
            }));
        }
        if (placingBed && lastMouse) {
            var ghost = bedPoly({ bedId: placingBed, x: lastMouse[0], y: lastMouse[1], angle: 0 }, scaleNow());
            if (ghost) overlay.appendChild(make("polygon", { points: ptsAttr(ghost) }, {
                fill: "rgba(143,63,41,0.15)", stroke: "var(--sp-accent)", strokeWidth: r / 2.5, strokeDasharray: r + "," + r / 2
            }));
        }
        var far = Math.max(W, H);
        if (cutDrawing && cutStart && lastMouse)
            overlay.appendChild(make("line", { x1: cutStart[0], y1: cutStart[1], x2: lastMouse[0], y2: lastMouse[1] }, {
                stroke: "var(--sp-accent)", strokeWidth: r / 2, strokeDasharray: r * 2 + "," + r
            }));
        var cp = cutParts(), cs = scaleNow();
        if (cp) {
            overlay.appendChild(make("line", { x1: cutLine.a[0] - cp.dx * far, y1: cutLine.a[1] - cp.dy * far, x2: cutLine.a[0] + cp.dx * far, y2: cutLine.a[1] + cp.dy * far }, {
                stroke: "var(--sp-accent)", strokeWidth: r / 2
            }));
            cp.pieces.forEach(function (piece, i) {
                if (!piece) return;
                var mx = piece.reduce(function (s, p) { return s + p[0]; }, 0) / piece.length;
                var my = piece.reduce(function (s, p) { return s + p[1]; }, 0) / piece.length;
                var owner = i === 0 ? cutLine.owner1 : cutLine.owner2;
                var text = String(i + 1) + (cs ? ": " + fmt(G.area(piece) * cs * cs, 1) + " m\u00b2" : "") + " \u00b7 " + ownerLabel(owner);
                overlay.appendChild(label(text, mx, my, r * 2));
            });
        }
        if (cutStart) overlay.appendChild(make("circle", { cx: cutStart[0], cy: cutStart[1], r: r }, { fill: "var(--sp-accent)" }));
        var line = splitPanel.open ? splitLine() : null;
        if (line) {
            syncSplitFields(line);
            var z = activeZone();
            var mx = z.points.reduce(function (s, p) { return s + p[0]; }, 0) / z.points.length;
            var my = z.points.reduce(function (s, p) { return s + p[1]; }, 0) / z.points.length;
            var shift = line.t0 - (mx * line.nx + my * line.ny);
            var cx = mx + line.nx * shift, cy = my + line.ny * shift, far = Math.max(W, H);
            overlay.appendChild(make("line", { x1: cx - line.dx * far, y1: cy - line.dy * far, x2: cx + line.dx * far, y2: cy + line.dy * far }, {
                stroke: "var(--sp-accent)", strokeWidth: r / 2, strokeDasharray: r * 2 + "," + r
            }));
            overlay.appendChild(label("1", cx - line.nx * r * 3, cy - line.ny * r * 3, r * 2.2));
            overlay.appendChild(label("2", cx + line.nx * r * 3, cy + line.ny * r * 3, r * 2.2));
        }
    }

    // ---- changing the shapes (the edge lengths follow the corners of the boundary)
    // A shape that is drawn corner by corner (new, or emptied) takes the clicks as its next corners; a closed shape does not:
    // a click beside a corner must not make a new one. A corner is inserted with the button "Insert corner" (on the nearest edge).
    var drawing = false;        // the clicks add corners at the end
    function setDrawing(on) {
        drawing = on;
        if (!inserting) cornerMessage(on ? T.drawingHint : "");
    }
    var inserting = false;      // the clicks insert a corner on the nearest edge
    function cornerMessage(text) {
        var box = document.getElementById("corner-msg");
        box.hidden = !text;
        box.textContent = text || "";
    }
    function setInserting(on) {
        inserting = on;
        document.getElementById("insert-corner").classList.toggle("active", on);
        cornerMessage(on ? T.insertHint : "");
    }
    function edgeDistance(p, a, b) {
        var dx = b[0] - a[0], dy = b[1] - a[1], len2 = dx * dx + dy * dy;
        var t = len2 === 0 ? 0 : Math.max(0, Math.min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / len2));
        return Math.hypot(p[0] - (a[0] + t * dx), p[1] - (a[1] + t * dy));
    }
    function addCorner(p) {
        var pts = active < 0 ? boundary : zones[active].points;
        if (active >= 0 && zones[active].isShape) return;
        if (pts.length < 3 || drawing) {
            if (active < 0) {
                if (boundary.length >= 3) lengths[boundary.length - 1] = null; // the closing edge is replaced
                boundary.push(p);
                lengths.push(null);
            } else {
                detach(zones[active]);
                zones[active].points.push(p);
            }
        } else if (inserting) {
            var at = 0, best = Infinity;
            for (var i = 0; i < pts.length; i++) {
                var d = edgeDistance(p, pts[i], pts[(i + 1) % pts.length]);
                if (d < best) { best = d; at = i; }
            }
            if (active < 0) { lengths[at] = null; lengths.splice(at + 1, 0, null); } else detach(zones[active]);
            pts.splice(at + 1, 0, p);
        } else {
            cornerMessage(T.cornerClosed);
            return;
        }
        redraw();
    }
    document.getElementById("insert-corner").addEventListener("click", function () { setInserting(!inserting); });
    function showSelection() {
        var button = document.getElementById("delete-point");
        button.disabled = selected < 0;
    }
    function removeCorner(i) {
        selected = -1;
        showSelection();
        var pts = active < 0 ? boundary : zones[active].points;
        if (active >= 0) detach(zones[active]);
        pts.splice(i, 1);
        if (active < 0) {
            lengths.splice(i, 1);
            if (boundary.length >= 3) lengths[(i - 1 + boundary.length) % boundary.length] = null;
        }
        redraw();
    }

    svg.addEventListener("pointerdown", function () { suppressClick = false; });
    svg.addEventListener("click", function (e) {
        if (suppressClick) { suppressClick = false; moved = false; return; }
        if (moved) { moved = false; return; }
        if (placingBed) { placeBedAt(toSvgRaw(e)); return; }
        if (tracing) { traceAt(toSvgRaw(e)); return; }
        if (mineOnly && active < 0 && !cutDrawing && !placing) return;    // in the view of the own part a click on empty ground does not change the boundary (the corners of an area still can be set)
        if (cutDrawing) {
            var q = toSvg(e);
            if (!cutStart) cutStart = q;
            else if (Math.hypot(q[0] - cutStart[0], q[1] - cutStart[1]) > unit() * 0.5) {
                cutLine = { a: cutStart, b: q, owner1: parseInt(document.getElementById("cut-owner1").value, 10) || 0, owner2: parseInt(document.getElementById("cut-owner2").value, 10) || 0 };
                setCutDrawing(false);
                return;
            }
            drawOverlay();
            return;
        }
        if (placing) {
            var points = makeShape(toSvgRaw(e));
            if (points) {
                var input = document.getElementById("zone-name");
                zones.push({
                    name: input.value.trim() || uniqueName(T.kinds[document.getElementById("zone-kind").value]),
                    kind: document.getElementById("zone-kind").value, owner: parseInt(document.getElementById("zone-owner").value, 10) || 0, isShape: true, points: points
                });
                input.value = "";
                active = zones.length - 1;
                setPlacing(false);
                fillTarget();
                redraw();
            }
            return;
        }
        addCorner(toSvg(e));
    });
    svg.addEventListener("pointermove", function (e) {
        if (drag) {
            if (active >= 0) detach(zones[active]);
            (active < 0 ? boundary : zones[active].points)[drag.index] = toSvg(e);
            moved = true;
            redraw();
        } else if (bedDrag) {
            var bp = toSvgRaw(e), bed = placedBed(bedDrag.id);
            if (bed && (bedDrag.started || Math.hypot(bp[0] - bedDrag.start[0], bp[1] - bedDrag.start[1]) > unit() * 0.6)) {
                bedDrag.started = true;
                moved = true;
                bed.x = Math.min(W, Math.max(0, Math.round((bedDrag.orig[0] + bp[0] - bedDrag.start[0]) * 10) / 10));
                bed.y = Math.min(H, Math.max(0, Math.round((bedDrag.orig[1] + bp[1] - bedDrag.start[1]) * 10) / 10));
                drawBeds();
            }
        } else if (cutDrag) {
            cutLine[cutDrag] = toSvg(e);
            moved = true;
            redraw();
        } else if (resizeDrag) {
            resizeTo(toSvgRaw(e));
            moved = true;
            redraw();
        } else if (moveDrag) {
            var p = toSvgRaw(e), dx = p[0] - moveDrag.start[0], dy = p[1] - moveDrag.start[1];
            if (!moveDrag.started && Math.hypot(dx, dy) > unit() * 0.6) moveDrag.started = true;
            if (moveDrag.started) {
                moved = true;
                detach(zones[active]);
                zones[active].points = moveDrag.orig.map(function (q) { return [Math.round((q[0] + dx) * 10) / 10, Math.round((q[1] + dy) * 10) / 10]; });
                redraw();
            }
        } else if (placing || cutDrawing || placingBed) {
            lastMouse = toSvgRaw(e);
            drawOverlay();
        }
    });
    function endDrag() {
        var movedBed = bedDrag && bedDrag.started;
        drag = null; moveDrag = null; resizeDrag = null; cutDrag = null; bedDrag = null;
        if (movedBed) fillBeds();      // the remarks of the bed list follow the new place
    }
    svg.addEventListener("pointerup", endDrag);
    svg.addEventListener("pointercancel", endDrag);

    // ---- controls
    var target = document.getElementById("target");
    var deleteZone = document.getElementById("delete-zone");
    // ---- the neighbours: how many there are and what they are called; every area is owned by the user or by one of them
    function fillOwnerSelects() {
        document.querySelectorAll(".owner-select").forEach(function (select) {
            var keep = select.value;
            select.replaceChildren();
            for (var o = 0; o <= neighbors.length; o++) {
                var option = document.createElement("option");
                option.value = String(o);
                option.textContent = ownerLabel(o);
                select.appendChild(option);
            }
            var add = document.createElement("option");      // a new neighbour can be added right here
            add.value = "+";
            add.textContent = T.neighborAdd;
            select.appendChild(add);
            select.value = parseInt(keep, 10) <= neighbors.length ? keep : "0";
        });
    }
    // choosing "+ neighbour" in any owner list adds a neighbour and selects it (before the other handlers of the list see the value)
    document.addEventListener("change", function (e) {
        var select = e.target;
        if (!select || !select.classList || !select.classList.contains("owner-select") || select.value !== "+") return;
        if (neighbors.length >= 10) { select.value = "0"; return; }
        neighbors.push("");
        buildNeighbors();
        select.value = String(neighbors.length);
    }, true);
    function buildNeighbors() {
        var holder = document.getElementById("neighbor-names");
        document.getElementById("neighbor-count").value = neighbors.length;
        holder.replaceChildren();
        neighbors.forEach(function (name, k) {
            var input = document.createElement("input");
            input.type = "text";
            input.maxLength = 100;
            input.className = "form-control form-control-sm";
            input.style.maxWidth = "10rem";
            input.value = name;
            input.placeholder = T.neighborName.replace("{0}", String(k + 1));
            input.setAttribute("aria-label", input.placeholder);
            input.addEventListener("input", function () {
                neighbors[k] = input.value;
                fillOwnerSelects();
                fillTarget();
                redraw();
            });
            holder.appendChild(input);
        });
        fillOwnerSelects();
    }
    document.getElementById("neighbor-count").addEventListener("change", function (e) {
        var count = Math.min(10, Math.max(0, parseInt(e.target.value, 10) || 0));
        while (neighbors.length < count) neighbors.push("");
        neighbors.length = count;
        zones.forEach(function (z) { if (z.owner > count) z.owner = 0; });   // an area of a neighbour that is gone goes back to the user
        if (cutLine) { if (cutLine.owner1 > count) cutLine.owner1 = 0; if (cutLine.owner2 > count) cutLine.owner2 = 0; }
        buildNeighbors();
        fillTarget();
        redraw();
    });

    function fillTarget() {
        selected = -1;
        showSelection();
        target.replaceChildren();
        var first = document.createElement("option");
        first.value = "-1";
        first.textContent = T.boundary;
        target.appendChild(first);
        zones.forEach(function (z, i) {
            var o = document.createElement("option");
            o.value = String(i);
            o.textContent = z.name + " – " + (T.kinds[z.kind] || z.kind) + " – " + ownerLabel(z.owner);
            target.appendChild(o);
        });
        target.value = String(active);
        deleteZone.hidden = active < 0;
        document.getElementById("adopt-zone").hidden = active < 0 || !(zones[active].kind === "Mine" && zones[active].owner === 0 && zones[active].points.length >= 3);
        document.getElementById("active-kind-box").hidden = active < 0;
        document.getElementById("active-owner-box").hidden = active < 0;
        document.getElementById("active-name-box").hidden = active < 0;
        document.getElementById("shape-to-corners").hidden = active < 0 || !zones[active].isShape;
        showProps();
        if (active >= 0) {
            document.getElementById("active-name").value = zones[active].name;
            document.getElementById("active-kind").value = zones[active].kind;
            document.getElementById("active-owner").value = String(zones[active].owner);
        }
    }
    document.getElementById("active-name").addEventListener("input", function (e) {
        if (active < 0) return;
        zones[active].name = e.target.value;
        target.options[active + 1].textContent = zones[active].name + " \u2013 " + (T.kinds[zones[active].kind] || zones[active].kind) + " \u2013 " + ownerLabel(zones[active].owner);
        redraw();
    });
    // what the user knows about the active area (to place beds later)
    var propFields = { use: "prop-use", sun: "prop-sun", soil: "prop-soil", slope: "prop-slope" };
    var propFlags = { waterlogged: "prop-waterlogged", waterNearby: "prop-water", coldFramePossible: "prop-coldframe" };
    function showProps() {
        document.getElementById("props-panel").hidden = active < 0;
        if (active < 0) return;
        Object.keys(propFields).forEach(function (k) { document.getElementById(propFields[k]).value = zones[active][k] || ""; });
        Object.keys(propFlags).forEach(function (k) { document.getElementById(propFlags[k]).checked = !!zones[active][k]; });
    }
    Object.keys(propFields).forEach(function (k) {
        document.getElementById(propFields[k]).addEventListener("change", function (e) {
            if (active >= 0) { zones[active][k] = e.target.value; fillZoneList(scaleNow()); }
        });
    });
    Object.keys(propFlags).forEach(function (k) {
        document.getElementById(propFlags[k]).addEventListener("change", function (e) {
            if (active >= 0) { zones[active][k] = e.target.checked; fillZoneList(scaleNow()); }
        });
    });
    // the properties of an area as a line of text
    function propSummary(z) {
        var parts = [];
        [["use", z.use], ["sun", z.sun], ["soil", z.soil], ["slope", z.slope]].forEach(function (e) {
            if (e[1] && T.props[e[0]][e[1]]) parts.push(T.props[e[0]][e[1]]);
        });
        if (z.waterlogged) parts.push(T.props.waterlogged);
        if (z.waterNearby) parts.push(T.props.water);
        if (z.coldFramePossible) parts.push(T.props.coldFrame);
        return parts.join(", ");
    }

    // a ready-made shape becomes a free area whose corners can be moved one by one
    document.getElementById("shape-to-corners").addEventListener("click", function () {
        if (active < 0 || !zones[active].isShape) return;
        zones[active].isShape = false;
        detach(zones[active]);
        fillTarget();
        redraw();
    });
    document.getElementById("active-owner").addEventListener("change", function (e) {
        if (active < 0) return;
        zones[active].owner = parseInt(e.target.value, 10) || 0;
        fillTarget();
        redraw();
    });
    document.getElementById("active-kind").addEventListener("change", function (e) {
        if (active < 0) return;
        zones[active].kind = e.target.value;
        fillTarget();
        redraw();
    });
    target.addEventListener("change", function () {
        active = parseInt(target.value, 10);
        setDrawing(false);
        setInserting(false);
        fillTarget();
        redraw();
    });
    document.getElementById("delete-point").addEventListener("click", function () {
        if (selected >= 0) removeCorner(selected);
    });
    document.addEventListener("keydown", function (e) {
        if ((e.key !== "Delete" && e.key !== "Backspace") || selected < 0) return;
        var tag = document.activeElement ? document.activeElement.tagName : "";
        if (tag === "INPUT" || tag === "SELECT" || tag === "TEXTAREA") return;
        e.preventDefault();
        removeCorner(selected);
    });
    document.getElementById("undo").addEventListener("click", function () {
        var pts = active < 0 ? boundary : zones[active].points;
        if (active >= 0 && zones[active].isShape) return;
        if (pts.length > 0) removeCorner(pts.length - 1);
    });
    document.getElementById("clear").addEventListener("click", function () {
        if (active < 0) {
            boundary.length = 0;
            lengths.length = 0;
        } else {
            detach(zones[active]);
            zones[active].points.length = 0;
        }
        if (active >= 0 && zones[active].isShape) zones[active].isShape = false;   // an emptied shape is drawn by hand again
        setDrawing(true);
        redraw();
    });
    document.getElementById("add-zone").addEventListener("click", function () {
        var input = document.getElementById("zone-name");
        zones.push({ name: input.value.trim() || uniqueName(T.kinds[document.getElementById("zone-kind").value]), kind: document.getElementById("zone-kind").value, owner: parseInt(document.getElementById("zone-owner").value, 10) || 0, isShape: false, points: [] });
        input.value = "";
        active = zones.length - 1;
        fillTarget();
        setDrawing(true);
        redraw();
    });
    deleteZone.addEventListener("click", function () {
        if (active < 0) return;
        zones.splice(active, 1);
        active = -1;
        fillTarget();
        redraw();
    });

    // ---- zoom: the picture gets wider than the box and is scrolled (keeps the point under the cursor in place)
    function setZoom(z, clientX, clientY) {
        z = Math.min(8, Math.max(0.1, z));
        if (z === zoom) return;
        var rect = wrap.getBoundingClientRect();
        var ax = (clientX === undefined ? rect.left + rect.width / 2 : clientX) - rect.left;
        var ay = (clientY === undefined ? rect.top + rect.height / 2 : clientY) - rect.top;
        var fx = (wrap.scrollLeft + ax) / wrap.scrollWidth;
        var fy = (wrap.scrollTop + ay) / wrap.scrollHeight;
        zoom = z;
        svg.style.width = (zoom * 100) + "%";
        wrap.scrollLeft = fx * wrap.scrollWidth - ax;
        wrap.scrollTop = fy * wrap.scrollHeight - ay;
        redraw();
    }
    document.getElementById("zoom-in").addEventListener("click", function () { setZoom(zoom * 1.5); });
    document.getElementById("zoom-out").addEventListener("click", function () { setZoom(zoom / 1.5); });
    document.getElementById("zoom-reset").addEventListener("click", function () { setZoom(1); });
    // the whole picture in the window
    function fitZoom() {
        var f = Math.min(1, (wrap.clientHeight - 2) / (wrap.clientWidth * H / W));
        setZoom(f > 0 ? Math.max(0.1, f) : 1);
        wrap.scrollLeft = 0;
        wrap.scrollTop = 0;
    }
    document.getElementById("zoom-fit").addEventListener("click", fitZoom);
    wrap.addEventListener("wheel", function (e) {
        if (!e.ctrlKey && !e.metaKey) return;   // the wheel alone scrolls the page as usual
        e.preventDefault();
        setZoom(zoom * (e.deltaY < 0 ? 1.25 : 0.8), e.clientX, e.clientY);
    }, { passive: false });

    // ---- table, summary and remarks
    var table = document.getElementById("edge-table");
    var body = table.querySelector("tbody");
    function buildTable() {
        var n = boundary.length >= 3 ? boundary.length : 0;
        if (n === tableEdges) return;
        tableEdges = n;
        body.replaceChildren();
        table.hidden = n === 0;
        document.getElementById("edge-empty").hidden = n !== 0;
        for (var i = 0; i < n; i++) {
            var tr = document.createElement("tr");
            var cells = [];
            for (var c = 0; c < 5; c++) {
                cells.push(document.createElement("td"));
                tr.appendChild(cells[c]);
            }
            cells[0].textContent = (i + 1) + " → " + ((i + 1) % n + 1);
            cells[2].className = "m";
            cells[3].className = "d";
            cells[4].className = "a";
            var input = document.createElement("input");
            input.type = "number";
            input.min = "0";
            input.step = "0.01";
            input.className = "form-control form-control-sm len";
            input.style.maxWidth = "8rem";
            input.value = lengths[i] > 0 ? lengths[i] : "";
            input.disabled = areaMode();
            cells[1].appendChild(input);
            input.addEventListener("input", (function (index) {
                return function (e) {
                    var v = parseFloat(e.target.value);
                    lengths[index] = v > 0 ? v : null;
                    update();
                };
            })(i));
            body.appendChild(tr);
        }
    }

    // The share of every owner of the plot: the user gets what is not cut out by a neighbour
    var ownerCollapsed = {};     // folded entries of the tree (owners and parts)
    // The plot as a tree: owner -> parts (areas of the kind "Mine", or the sides of the straight line) -> areas with a purpose inside the part
    function fillOwnerList(scale) {
        var box = document.getElementById("owner-list-box"), list = document.getElementById("owner-list");
        list.replaceChildren();
        box.hidden = scale === null || boundary.length < 3;
        if (box.hidden) return;
        var total = G.area(boundary), nb = neighborAreas(), mine = total, cp = cutLine ? cutParts() : null;
        nb.forEach(function (a) { mine -= a; });
        function sq(a) { return fmt(a * scale * scale, 1) + " m\u00b2"; }
        function centroid(pts) { return [pts.reduce(function (t, p) { return t + p[0]; }, 0) / pts.length, pts.reduce(function (t, p) { return t + p[1]; }, 0) / pts.length]; }
        // one row: level 0 owner, 1 part or area of the owner itself, 2 area in a part
        function addRow(o) {
            var tr = document.createElement("tr");
            if (o.level === 0) tr.className = "fw-semibold"; else if (o.level === 2) tr.className = "text-muted";
            [o.label, o.kind || "", o.area === null ? "" : sq(o.area), o.area === null || total <= 0 ? "" : fmt(o.area / total * 100, 1) + " %", o.props || ""].forEach(function (t, ti) {
                var td = document.createElement("td");
                td.style.paddingLeft = ti === 0 ? (o.level * 1.5) + "rem" : "";
                if (ti === 0 && o.key) {
                    var toggle = document.createElement("button");
                    toggle.type = "button";
                    toggle.className = "btn btn-sm btn-link p-0 me-1 text-decoration-none";
                    toggle.textContent = ownerCollapsed[o.key] ? "\u25b8" : "\u25be";
                    toggle.setAttribute("aria-expanded", ownerCollapsed[o.key] ? "false" : "true");
                    toggle.addEventListener("click", function () { ownerCollapsed[o.key] = !ownerCollapsed[o.key]; fillOwnerList(scale); });
                    td.appendChild(toggle);
                }
                td.appendChild(document.createTextNode(t));
                tr.appendChild(td);
            });
            var actions = document.createElement("td");
            actions.className = "text-nowrap";
            (o.buttons || []).forEach(function (b) {
                var btn = document.createElement("button");
                btn.type = "button";
                btn.className = "btn btn-sm me-1 " + b[1];
                btn.textContent = b[0];
                btn.addEventListener("click", b[2]);
                actions.appendChild(btn);
            });
            tr.appendChild(actions);
            list.appendChild(tr);
        }
        function zoneButtons(z, withAdopt) {
            var zi = zones.indexOf(z), b = [[T.rowEdit, "btn-outline-secondary", function () { editZoneRow(zi); }]];
            if (withAdopt) b.push([T.adopt, "btn-outline-secondary", function () { adoptAsPlot(zi); }]);
            b.push([T.rowDelete, "btn-outline-danger", function () { deleteZoneRow(zi); }]);
            return b;
        }
        var owners = [mine].concat(nb);
        owners.forEach(function (ownerArea, o) {
            // the parts of this owner: marked parts and the sides of the straight line
            var parts = zones.filter(function (z) { return z.kind === "Mine" && z.owner === o && z.points.length >= 3; }).map(function (z) {
                return { zone: z, name: z.name, area: zoneArea(z), points: z.points };
            });
            if (cp) [cutLine.owner1, cutLine.owner2].forEach(function (own, i) {
                if (own === o && cp.pieces[i]) parts.push({ zone: null, name: T.cutName.replace("{0}", String(i + 1)) + " (" + T.cutLineShort + ")", area: G.area(cp.pieces[i]), points: cp.pieces[i] });
            });
            var areas = zones.filter(function (z) { return z.kind !== "Mine" && z.owner === o && z.points.length >= 3 && zoneArea(z) > 0; });
            var inPart = parts.map(function () { return []; }), loose = [];
            areas.forEach(function (z) {
                var c = centroid(z.points), best = -1;
                parts.forEach(function (p, pi) { if (G.pointInPolygon(c, p.points) && (best < 0 || G.area(p.points) < G.area(parts[best].points))) best = pi; });
                if (best >= 0) inPart[best].push(z); else loose.push(z);
            });
            addRow({ level: 0, label: ownerLabel(o), area: ownerArea, key: parts.length + areas.length ? "o" + o : "" });
            if (ownerCollapsed["o" + o]) return;
            function areaRow(z, level) {
                addRow({ level: level, label: z.name, kind: T.kinds[z.kind] || z.kind, area: zoneArea(z), props: propSummary(z), buttons: zoneButtons(z, false) });
            }
            parts.forEach(function (p, pi) {
                var buttons = p.zone ? zoneButtons(p.zone, o === 0) : (o === 0 ? [[T.adopt, "btn-outline-secondary", function () { restrictToMine(); }]] : []);
                addRow({ level: 1, label: p.name, kind: T.partName, area: p.area, buttons: buttons, key: inPart[pi].length ? "p" + o + "-" + pi : "" });
                if (!ownerCollapsed["p" + o + "-" + pi]) inPart[pi].forEach(function (z) { areaRow(z, 2); });
            });
            loose.forEach(function (z) { areaRow(z, 1); });
        });
    }

    function fillZoneList(scale) {
        var box = document.getElementById("zone-list-box"), list = document.getElementById("zone-list");
        list.replaceChildren();
        box.hidden = zones.length === 0;
        zones.forEach(function (z, zi) {
            var tr = document.createElement("tr");
            if (zi === active) tr.className = "table-active";
            [z.name, T.kinds[z.kind] || z.kind, ownerLabel(z.owner), scale === null ? "\u2013" : fmt(G.area(z.points) * scale * scale, 1) + " m\u00b2", scale === null ? "\u2013" : fmt(zoneArea(z) * scale * scale, 1) + " m\u00b2", propSummary(z)].forEach(function (t) {
                var td = document.createElement("td");
                td.textContent = t;
                tr.appendChild(td);
            });
            var actions = document.createElement("td");
            actions.className = "text-nowrap";
            [[T.rowEdit, "btn-outline-secondary", function () { editZoneRow(zi); }],
             [T.adopt, "btn-outline-secondary", function () { adoptAsPlot(zi); }],
             [T.rowDelete, "btn-outline-danger", function () { deleteZoneRow(zi); }]].forEach(function (b, bi) {
                if (bi === 1 && !(z.kind === "Mine" && z.owner === 0 && z.points.length >= 3)) return;
                var btn = document.createElement("button");
                btn.type = "button";
                btn.className = "btn btn-sm me-1 " + b[1];
                btn.textContent = b[0];
                btn.addEventListener("click", b[2]);
                actions.appendChild(btn);
            });
            tr.appendChild(actions);
            list.appendChild(tr);
        });
    }

    function fillZoneEdges(scale) {
        var z = activeZone(), box = document.getElementById("zone-edges-box");
        var bodyZ = document.getElementById("zone-edges");
        bodyZ.replaceChildren();
        if (!z || z.points.length < 3 || scale === null) { box.hidden = true; return; }
        box.hidden = false;
        document.getElementById("zone-edges-title").textContent = T.zoneEdges.replace("{0}", z.name);
        var n = z.points.length;
        for (var i = 0; i < n; i++) {
            var tr = document.createElement("tr");
            [(i + 1) + " \u2192 " + ((i + 1) % n + 1), fmt(G.edgeLen(z.points, i) * scale, 2) + " m", fmt(G.angle(z.points, i), 1) + "\u00b0"].forEach(function (t) {
                var td = document.createElement("td");
                td.textContent = t;
                tr.appendChild(td);
            });
            let at = i;      // the corner of this row (the buttons are used long after the loop)
            [0, 1].forEach(function (axis) {
                var td = document.createElement("td"), input = document.createElement("input");
                input.type = "number"; input.step = "0.01"; input.className = "form-control form-control-sm"; input.style.maxWidth = "7rem";
                input.value = (z.points[at][axis] * scale).toFixed(2);
                input.setAttribute("aria-label", (axis === 0 ? "x" : "y") + " " + (i + 1));
                input.addEventListener("change", function () {
                    var v = parseFloat(input.value);
                    if (!isFinite(v)) return;
                    detach(z);
                    z.isShape = false;
                    z.points[at][axis] = Math.round(v / scale * 10) / 10;
                    fillTarget();
                    redraw();
                });
                td.appendChild(input);
                tr.appendChild(td);
            });
            var cell = document.createElement("td"), del = document.createElement("button");
            del.type = "button"; del.className = "btn btn-outline-danger btn-sm"; del.textContent = "\u00d7";
            del.title = T.deleteCorner; del.setAttribute("aria-label", T.deleteCorner);
            del.disabled = n <= 3;
            del.addEventListener("click", function () { detach(z); z.isShape = false; removeCorner(at); fillTarget(); });
            cell.appendChild(del);
            tr.appendChild(cell);
            bodyZ.appendChild(tr);
        }
        document.getElementById("zone-area").textContent = fmt(G.area(z.points) * scale * scale, 2) + " m\u00b2";
    }

    function update() {
        buildTable();
        var warnings = [];
        var n = boundary.length;
        var scale = null;
        if (n < 3) warnings.push(T.warnFew);
        else {
            scale = scaleNow();
            if (G.selfIntersects(boundary)) warnings.push(T.warnCross);
            if (scale === null) warnings.push(T.noScale);
        }

        if (!areaMode()) plotArea.value = scale !== null ? (G.area(boundary) * scale * scale).toFixed(2) : "";      // the area follows the edges and cannot be changed here
        var rows = body.querySelectorAll("tr");
        for (var i = 0; i < rows.length; i++) {
            var lenInput = rows[i].querySelector(".len");       // the lengths follow the area and cannot be changed then
            if (lenInput) {
                lenInput.disabled = areaMode();
                if (areaMode()) {      // the edges are calculated from the area and kept, so they are there when the user switches to single edges
                    lenInput.value = scale === null ? "" : (G.edgeLen(boundary, i) * scale).toFixed(2);
                    lengths[i] = scale === null ? null : Math.round(G.edgeLen(boundary, i) * scale * 100) / 100;
                }
            }
            var measured = scale === null ? null : G.edgeLen(boundary, i) * scale;
            var dev = (!areaMode() && measured !== null && lengths[i] > 0) ? (measured - lengths[i]) / lengths[i] : null;
            rows[i].querySelector(".m").textContent = measured === null ? "–" : fmt(measured, 2);
            rows[i].querySelector(".d").textContent = dev === null ? "–" : (dev > 0 ? "+" : "") + fmt(dev * 100, 1) + " %";
            rows[i].querySelector(".a").textContent = fmt(G.angle(boundary, i), 1) + "°";
            if (dev !== null && Math.abs(dev) > 0.05)
                warnings.push(T.warnDeviation.replace("{0}", String(i + 1)).replace("{1}", fmt(Math.abs(dev) * 100, 0)));
        }

        var total = null, cut = null, occupied = null, available = null;
        if (n >= 3 && scale !== null) {
            var a = figures(boundary), k2 = scale * scale;
            total = a.total * k2;
            cut = Math.max(0, a.cut * k2);
            occupied = Math.max(0, a.built * k2);
            available = a.available * k2;
        }
        fillZoneList(n >= 3 && scale !== null ? scale : null);
        fillOwnerList(total === null ? null : scale);
if (editMode) document.getElementById("edit-need").hidden = plotDefined();
        fillZoneEdges(scale);
        updateCut(scale);
        function show(id, v) { document.getElementById(id).textContent = v === null ? "–" : fmt(v, 1) + " m²"; }
        show("sum-total", total);
        show("sum-cut", cut);
        show("sum-blocked", occupied);
        show("sum-available", available);
        document.getElementById("chip-available").textContent = available === null ? "\u2013" : fmt(available, 1) + " m\u00b2";

        var box = document.getElementById("warnings");
        box.replaceChildren();
        warnings.forEach(function (w) {
            var d = document.createElement("div");
            d.className = "small text-danger";
            d.textContent = w;
            box.appendChild(d);
        });
    }

    document.getElementById("garden-form").addEventListener("submit", function () {
        document.getElementById("f-boundary").value = JSON.stringify(boundary);
        document.getElementById("f-lengths").value = JSON.stringify(lengths);
        document.getElementById("f-zones").value = JSON.stringify(zones);
        document.getElementById("f-neighbors").value = JSON.stringify(neighbors);
        document.getElementById("f-cutline").value = cutLine ? JSON.stringify(cutLine) : "";
        document.getElementById("f-placed").value = JSON.stringify(placedBeds);
        document.getElementById("f-area").value = areaMode() && parseFloat(plotArea.value) > 0 ? plotArea.value : "";
    });


    // ---- beds: the user's beds are placed on the plot (centre in pixels of the picture, angle in degrees)
    var beds = data.beds || [];
    var placedBeds = (data.placed || []).filter(function (p) { return bedById(p.bedId); }).map(function (p) { return { bedId: p.bedId, x: +p.x, y: +p.y, angle: +p.angle || 0 }; });
    var mineOnly = false;       // only the user's own part is shown (the rest of the picture is hidden)
    var placingBed = 0;         // the bed that the next click puts down
    var activeBed = 0;          // the selected bed
    var bedDrag = null;
    function bedById(id) { for (var i = 0; i < beds.length; i++) if (beds[i].id === id) return beds[i]; return null; }
    function placedBed(id) { for (var i = 0; i < placedBeds.length; i++) if (placedBeds[i].bedId === id) return placedBeds[i]; return null; }
    function ptsAttr(poly) { return poly.map(function (p) { return p[0] + "," + p[1]; }).join(" "); }
    function bedArea(b) { var a = b.widthCm / 100 * b.lengthCm / 100; return b.shape === "Oval" ? a * Math.PI / 4 : a; }
    function bedPoly(pl, s) {
        var b = bedById(pl.bedId);
        return b && s ? G.bedOutline(pl.x, pl.y, b.lengthCm / 100, b.widthCm / 100, pl.angle, b.shape === "Oval", s) : null;
    }
    // beds can only be placed once the plot is defined: the parts of the neighbours are gone (the own part was taken as the plot)
    function plotDefined() {
        return boundary.length >= 3 && scaleNow() !== null && !zones.some(function (z) { return z.owner > 0; }) && !(cutLine && (cutLine.owner1 > 0 || cutLine.owner2 > 0));
    }
    // what is not the user's (neighbours, built on) and the garden areas (if there are any, only they count)
    function regionParts() { return { bad: neighborPoints().concat(kindPoints(BUILT)), ga: kindPoints(["Garden"]) }; }
    function inMine(p, parts) {
        if (!G.pointInPolygon(p, boundary)) return false;
        for (var i = 0; i < parts.bad.length; i++) if (G.pointInPolygon(p, parts.bad[i])) return false;
        return parts.ga.length === 0 || parts.ga.some(function (q) { return G.pointInPolygon(p, q); });
    }
    // where a bed stands: inside the user's part? on another bed? what the area says about the place
    function bedStatus(pl, parts, polys) {
        var st = { outside: false, overlaps: [], hints: [] }, poly = polys[pl.bedId], b = bedById(pl.bedId);
        if (!poly || !b) return st;
        st.outside = boundary.length < 3 || !poly.concat([[pl.x, pl.y]]).every(function (p) { return inMine(p, parts); });
        placedBeds.forEach(function (o) {
            if (o !== pl && polys[o.bedId] && G.convexOverlap(poly, polys[o.bedId], 0.3)) st.overlaps.push(bedById(o.bedId).name);
        });
        var zone = null, zoneArea = Infinity;
        zones.forEach(function (z) {
            if (z.owner === 0 && z.points.length >= 3 && (z.kind === "Garden" || z.kind === "Mine") && G.pointInPolygon([pl.x, pl.y], z.points)) {
                var a = G.area(z.points);
                if (a < zoneArea) { zoneArea = a; zone = z; }
            }
        });
        if (zone) {
            var sum = propSummary(zone);
            if (sum) st.hints.push(zone.name + ": " + sum);
            if (zone.use && zone.use !== "Beds") st.hints.push(T.bedUseOther.replace("{0}", T.props.use[zone.use] || zone.use));
            if (b.hasCover && !zone.coldFramePossible) st.hints.push(T.bedNoCover);
        }
        return st;
    }
    function bedPolys(s) {
        var polys = {};
        if (s !== null) placedBeds.forEach(function (pl) { polys[pl.bedId] = bedPoly(pl, s); });
        return polys;
    }
    function selectBed(id) {
        activeBed = id;
        document.querySelectorAll("#bed-list details").forEach(function (d) { d.open = parseInt(d.dataset.bed, 10) === id; });
        drawBeds();
    }
    function drawBeds() {
        bedLayer.replaceChildren();
        var s = scaleNow();
        if (s === null) return;
        var parts = regionParts(), polys = bedPolys(s), r = unit();
        placedBeds.forEach(function (pl) {
            var b = bedById(pl.bedId), poly = polys[pl.bedId];
            if (!b || !poly) return;
            var st = bedStatus(pl, parts, polys), bad = st.outside || st.overlaps.length > 0, chosen = pl.bedId === activeBed;
            var shape = make("polygon", { points: ptsAttr(poly) }, {
                fill: bad ? "rgba(179,38,30,0.35)" : "rgba(143,63,41,0.4)", stroke: bad ? "#b3261e" : "var(--sp-accent)",
                strokeWidth: r / (chosen ? 1.2 : 2.5), strokeDasharray: b.hasCover ? r + "," + r / 2 : "", cursor: "move", touchAction: "none"
            });
            shape.addEventListener("pointerdown", function (e) {
                if (placing || cutDrawing || placingBed) return;      // another tool is active: the click goes on to the picture
                e.preventDefault();
                e.stopPropagation();
                suppressClick = true;
                bedDrag = { id: pl.bedId, start: toSvgRaw(e), orig: [pl.x, pl.y], started: false };
                moved = false;
                if (activeBed !== pl.bedId) selectBed(pl.bedId);
                svg.setPointerCapture(e.pointerId);
            });
            bedLayer.appendChild(shape);
            bedLayer.appendChild(label(b.name, pl.x, pl.y, r * 1.8));
        });
    }
    function setPlacingBed(id) {
        if (id && tracing) setTracing(null);
        placingBed = id;
        lastMouse = id ? lastMouse : null;
        svg.style.cursor = id ? "copy" : "";
        drawOverlay();
        fillBeds();
    }
    function placeBedAt(p) {
        var id = placingBed;
        if (!id || !bedById(id) || placedBed(id) || !plotDefined()) { setPlacingBed(0); return; }
        placedBeds.push({ bedId: id, x: Math.min(W, Math.max(0, p[0])), y: Math.min(H, Math.max(0, p[1])), angle: 0 });
        activeBed = id;
        setPlacingBed(0);
        drawBeds();
    }
    function fillBeds() {
        var choose = document.getElementById("bed-choose"), go = document.getElementById("bed-place"), msg = document.getElementById("bed-msg");
        var list = document.getElementById("bed-list"), sum = document.getElementById("bed-sum");
        var s = scaleNow(), keep = choose.value;
        choose.replaceChildren();
        beds.forEach(function (b) {
            if (placedBed(b.id)) return;
            var o = document.createElement("option");
            o.value = String(b.id);
            o.textContent = b.name + " (" + fmt(b.lengthCm / 100, 2) + " × " + fmt(b.widthCm / 100, 2) + " m)";
            choose.appendChild(o);
        });
        if (keep) choose.value = keep;
        if (!choose.value && choose.options.length) choose.selectedIndex = 0;
        var ready = plotDefined(), free = choose.options.length > 0 && ready;
        choose.hidden = go.hidden = !free;
        go.textContent = placingBed ? T.bedCancel : T.bedPlace;
        msg.textContent = !ready ? (s === null ? T.noScale : T.bedNeedPlot) : placingBed ? T.bedPlaceHint : beds.length === 0 ? T.bedNoBeds : !free ? T.bedAllPlaced : "";

        list.replaceChildren();
        var parts = regionParts(), polys = bedPolys(s), total = 0;
        placedBeds.forEach(function (pl) {
            var b = bedById(pl.bedId);
            if (!b) return;
            total += bedArea(b);
            var st = bedStatus(pl, parts, polys), bad = st.outside || st.overlaps.length > 0;
            var d = document.createElement("details");
            d.className = "border-bottom py-1";
            d.dataset.bed = String(pl.bedId);
            d.open = pl.bedId === activeBed;
            var head = document.createElement("summary");
            head.className = "small";
            head.textContent = b.name + " · " + fmt(b.lengthCm / 100, 2) + " × " + fmt(b.widthCm / 100, 2) + " m" + (bad ? " ⚠" : "");
            head.addEventListener("click", function () { activeBed = pl.bedId; drawBeds(); });
            d.appendChild(head);

            var row = document.createElement("div");
            row.className = "d-flex flex-wrap align-items-end gap-2 my-1";
            var angle = document.createElement("input");
            angle.type = "number"; angle.step = "5"; angle.className = "form-control form-control-sm"; angle.style.maxWidth = "6rem";
            angle.value = String(Math.round(pl.angle * 10) / 10);
            angle.setAttribute("aria-label", T.bedAngle);
            angle.addEventListener("input", function () { var v = parseFloat(angle.value); if (isFinite(v)) { pl.angle = ((v % 360) + 360) % 360; drawBeds(); } });
            angle.addEventListener("change", fillBeds);
            var lab = document.createElement("span");
            lab.className = "small"; lab.textContent = T.bedAngle + " (°)";
            row.appendChild(lab); row.appendChild(angle);
            [[-15, "↺ 15°"], [15, "↻ 15°"]].forEach(function (t) {
                var btn = document.createElement("button");
                btn.type = "button"; btn.className = "btn btn-outline-secondary btn-sm"; btn.textContent = t[1];
                btn.addEventListener("click", function () { pl.angle = (((pl.angle + t[0]) % 360) + 360) % 360; activeBed = pl.bedId; drawBeds(); fillBeds(); });
                row.appendChild(btn);
            });
            var del = document.createElement("button");
            del.type = "button"; del.className = "btn btn-outline-danger btn-sm"; del.textContent = T.bedRemove;
            del.addEventListener("click", function () {
                placedBeds = placedBeds.filter(function (o) { return o !== pl; });
                if (activeBed === pl.bedId) activeBed = 0;
                drawBeds(); fillBeds();
            });
            row.appendChild(del);
            d.appendChild(row);

            var lines = [];
            if (st.outside) lines.push([T.bedOutside, "text-danger"]);
            if (st.overlaps.length) lines.push([T.bedOverlap.replace("{0}", st.overlaps.join(", ")), "text-danger"]);
            st.hints.forEach(function (h) { lines.push([h, "text-muted"]); });
            if (!bad && s !== null) lines.unshift([T.bedOk, "text-muted"]);
            lines.forEach(function (l) {
                var div = document.createElement("div");
                div.className = "small " + l[1];
                div.textContent = l[0];
                d.appendChild(div);
            });
            list.appendChild(d);
        });
        if (placedBeds.length && s !== null && boundary.length >= 3) {
            var avail = figures(boundary).available * s * s;
            sum.textContent = T.bedSum.replace("{0}", fmt(total, 1)).replace("{1}", fmt(avail, 1)).replace("{2}", avail > 0 ? fmt(total / avail * 100, 0) : "–");
        } else sum.textContent = "";
    }
    document.getElementById("bed-place").addEventListener("click", function () {
        if (placingBed) { setPlacingBed(0); return; }
        var id = parseInt(document.getElementById("bed-choose").value, 10);
        if (!id || !plotDefined()) { fillBeds(); return; }
        cutDrawing = false; placing = false;
        setPlacingBed(id);
    });
    document.addEventListener("keydown", function (e) { if (e.key === "Escape" && placingBed) setPlacingBed(0); });

    // only the user's own part: the rest of the picture is hidden (mask) and the view goes to that part
    function applyMine() {
        if (!mineOnly || boundary.length < 3) { picture.removeAttribute("mask"); return; }
        mineMask.setAttribute("width", W);
        mineMask.setAttribute("height", H);
        mineMask.replaceChildren();
        mineClip.replaceChildren();
        mineClip.appendChild(make("polygon", { points: ptsAttr(boundary) }));
        mineMask.appendChild(make("rect", { x: 0, y: 0, width: W, height: H }, { fill: "black" }));
        // the user's whole part stays visible (house, terrace and so on too); only what belongs to neighbours is hidden
        mineMask.appendChild(make("polygon", { points: ptsAttr(boundary) }, { fill: "white" }));
        neighborPoints().forEach(function (p) { mineMask.appendChild(make("polygon", { points: ptsAttr(p) }, { fill: "black" })); });
        picture.setAttribute("mask", "url(#mine-mask)");
    }
    function fitMine() {
        if (boundary.length < 3) return;
        // the box around the user's part: sample the bounding box of the plot, keep the points inside the plot and not in a neighbour's area
        var nb = neighborPoints();
        var x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity, bx0 = Infinity, by0 = Infinity, bx1 = -Infinity, by1 = -Infinity;
        boundary.forEach(function (p) { bx0 = Math.min(bx0, p[0]); by0 = Math.min(by0, p[1]); bx1 = Math.max(bx1, p[0]); by1 = Math.max(by1, p[1]); });
        for (var gx = 0; gx <= 60; gx++) {
            for (var gy = 0; gy <= 60; gy++) {
                var sp = [bx0 + (bx1 - bx0) * gx / 60, by0 + (by1 - by0) * gy / 60];
                if (!G.pointInPolygon(sp, boundary) || nb.some(function (q) { return G.pointInPolygon(sp, q); })) continue;
                x0 = Math.min(x0, sp[0]); y0 = Math.min(y0, sp[1]); x1 = Math.max(x1, sp[0]); y1 = Math.max(y1, sp[1]);
            }
        }
        if (!(x1 > x0 && y1 > y0)) { fitZoom(); return; }
        var m = Math.max(x1 - x0, y1 - y0) * 0.04;
        x0 = Math.max(0, x0 - m); y0 = Math.max(0, y0 - m); x1 = Math.min(W, x1 + m); y1 = Math.min(H, y1 + m);
        var bw = x1 - x0, bh = y1 - y0, cw = wrap.clientWidth, ch = wrap.clientHeight - 2;
        setZoom(Math.max(0.1, Math.min(8, W / bw, ch * W / (cw * bh))));
        var k = svg.getBoundingClientRect().width / W;
        wrap.scrollLeft = x0 * k;
        wrap.scrollTop = y0 * k;
    }
    try { mineOnly = localStorage.getItem("garden-mine-only") === "1"; } catch (x) { mineOnly = false; }
if (editMode) mineOnly = true;
    document.getElementById("mine-only").checked = mineOnly;
    document.getElementById("mine-only").addEventListener("change", function (e) {
        mineOnly = e.target.checked;
        if (!editMode) { try { localStorage.setItem("garden-mine-only", mineOnly ? "1" : "0"); } catch (x) { } }
        redraw();
        if (mineOnly) fitMine(); else fitZoom();
    });


    // ---- editing the marked areas as a list, and taking one area as the new plot (the rest is removed)
    var adoptBackup = null;
    function showAdoptMsg(text) {
        var m = document.getElementById("adopt-msg");
        m.textContent = text || "";
        m.hidden = !text;
        document.getElementById("adopt-undo").hidden = !adoptBackup;
    }
    function adoptAsPlot(i) {
        var z = zones[i];
        if (!z || z.points.length < 3) return;
        var copy = z.points.map(function (p) { return [p[0], p[1]]; });
        if (z.owner > 0) { adoptPolygon(copy, z.name, i); return; }
        // the user's part: what lies inside it and belongs to a neighbour (his half of the garage) is taken off
        var m = minePolygon([copy]);
        if (!m.poly) { showAdoptMsg(T.restrictNone); return; }
        adoptPolygon(m.poly, z.name, i, m);
    }
    // the outline of the user's part: the plot (or the side of the straight line that is the user's) minus every area of a neighbour
    function minePolygon(given) {
        var start = given || [boundary.map(function (p) { return [p[0], p[1]]; })];
        var cp = !given && cutLine ? cutParts() : null;
        if (cp) {
            var owners = [cutLine.owner1, cutLine.owner2], mine = cp.pieces.filter(function (p, k) { return p && owners[k] === 0; });
            if (mine.length < 2) start = mine;      // both sides are the user's: the whole plot stays
        }
        var neighborZones = zones.filter(function (z) { return z.owner > 0 && z.points.length >= 3; });
        // corners that stop up to about 1/150 of the picture short of the edge of the plot count as reaching it
        var r = G.subtractAll(start, neighborZones.map(function (z) { return z.points; }), Math.max(W, H) / 150), best = null;
        r.polys.forEach(function (p) { if (!best || G.area(p) > G.area(best)) best = p; });
        var names = neighborZones.filter(function (z) { return r.holes.indexOf(z.points) >= 0; }).map(function (z) { return z.name; });
        return { poly: best, others: r.polys.length - 1, holes: names.length, holeNames: names.join(", ") };
    }
    document.getElementById("restrict-mine").addEventListener("click", restrictToMine);
    function restrictToMine() {
        if (boundary.length < 3) return;
        var m = minePolygon();
        if (!m.poly) { showAdoptMsg(T.restrictNone); return; }
        adoptPolygon(m.poly, "", -1, m);
    }
    function adoptPolygon(poly, name, skip, extra) {
        var s = scaleNow();
        if (s === null) { showAdoptMsg(T.noScale); return; }
        var areaM2 = G.area(poly) * s * s;
        var inside = function (o) {
            var cx = o.points.reduce(function (t, p) { return t + p[0]; }, 0) / o.points.length;
            var cy = o.points.reduce(function (t, p) { return t + p[1]; }, 0) / o.points.length;
            return G.pointInPolygon([cx, cy], poly) || o.points.some(function (p) { return G.pointInPolygon(p, poly); });
        };
        var kept = zones.filter(function (o, k) { return k !== skip && o.owner === 0 && o.points.length >= 3 && inside(o); });
        var dropped = zones.length - (skip >= 0 ? 1 : 0) - kept.length;
        var question = T.adoptConfirm.replace("{0}", name || T.mineName).replace("{1}", fmt(areaM2, 1)).replace("{2}", String(dropped));
        if (extra && (extra.others > 0 || extra.holes > 0)) question += "\n\n" + T.restrictWarn.replace("{0}", String(extra.others)).replace("{1}", extra.holeNames || "\u2013");
        if (!window.confirm(question)) return;
        adoptBackup = JSON.stringify({
            boundary: boundary, lengths: lengths, zones: zones, neighbors: neighbors, cutLine: cutLine, mode: scaleMode.value, area: plotArea.value
        });
        boundary = poly;
        lengths = poly.map(function (p, k) { return Math.round(G.edgeLen(poly, k) * s * 100) / 100; });      // the edges of the new plot, calculated from the scale
        zones = kept;
        zones.forEach(detach);
        neighbors = [];
        cutLine = null;
        cutStart = null;
        setCutDrawing(false);
        scaleMode.value = "area";       // the scale stays as it was: the area of the new plot in square meters at the same meters per pixel
        plotArea.value = areaM2.toFixed(3);
        active = -1;
        buildNeighbors();
        showScaleMode();
        fillTarget();
        var box = document.getElementById("mine-only");
        box.checked = mineOnly = true;
        redraw();
        fitMine();
        showAdoptMsg(T.adoptDone.replace("{0}", fmt(areaM2, 1)).replace("{1}", String(kept.length)));
    }
    document.getElementById("adopt-undo").addEventListener("click", function () {
        if (!adoptBackup) return;
        var b = JSON.parse(adoptBackup);
        adoptBackup = null;
        boundary = b.boundary; lengths = b.lengths; zones = b.zones; neighbors = b.neighbors; cutLine = b.cutLine;
        scaleMode.value = b.mode; plotArea.value = b.area;
        active = -1;
        buildNeighbors();
        if (cutLine) {
            document.getElementById("cut-owner1").value = String(cutLine.owner1);
            document.getElementById("cut-owner2").value = String(cutLine.owner2);
        }
        showScaleMode();
        fillTarget();
        redraw();
        showAdoptMsg("");
    });
    document.getElementById("adopt-zone").addEventListener("click", function () { if (active >= 0) adoptAsPlot(active); });
    document.getElementById("add-corner").addEventListener("click", function () {
        var z = activeZone();
        if (!z || z.points.length < 2) return;
        var a = z.points[z.points.length - 1], b = z.points[0];
        detach(z);
        z.isShape = false;
        z.points.push([Math.round((a[0] + b[0]) / 2 * 10) / 10, Math.round((a[1] + b[1]) / 2 * 10) / 10]);    // on the closing edge: the shape does not change until the corner is moved
        fillTarget();
        redraw();
    });
    function editZoneRow(i) {
        active = i;
        setDrawing(false);
        fillTarget();
        redraw();
        showTools(true);
        var stage = document.getElementById("stage");
        if (stage.scrollIntoView) stage.scrollIntoView({ block: "nearest" });
    }
    function deleteZoneRow(i) {
        if (!window.confirm(T.rowDeleteConfirm.replace("{0}", zones[i].name))) return;
        zones.splice(i, 1);
        if (active === i) active = -1; else if (active > i) active--;
        fillTarget();
        redraw();
    }
    showShapeType();
    showScaleMode();
    buildNeighbors();
    if (cutLine) {
        document.getElementById("cut-owner1").value = String(cutLine.owner1);
        document.getElementById("cut-owner2").value = String(cutLine.owner2);
    }
    // who owns the parts of the straight line counts at once (no need to create areas from them)
    [1, 2].forEach(function (n) {
        document.getElementById("cut-owner" + n).addEventListener("change", function (e) {
            if (!cutLine) return;
            cutLine["owner" + n] = parseInt(e.target.value, 10) || 0;
            update();
            drawOverlay();
        });
    });
    fillTarget();
    redraw();
})();
