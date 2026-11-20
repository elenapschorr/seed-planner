// Changing the shape of an area: handles on its box for resizing, and the ready-made shapes (rectangle, ellipse, triangle, trapezoid) that are put into the picture.
(function (root) {
    "use strict";

    function create(app) {
        var G = app.G,
            T = app.T,
            data = app.data,
            model = app.model,
            view = app.view,
            svg = app.svg;

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
            app.shapes.appendChild(app.make("polygon", { points: box.map(function (p) { return p[0] + "," + p[1]; }).join(" ") }, {
                fill: "none", stroke: "var(--sp-accent)", strokeWidth: r / 4, strokeDasharray: r / 2 + "," + r / 2, pointerEvents: "none"
            }));
            [-1, 0, 1].forEach(function (su) {
                [-1, 0, 1].forEach(function (sv) {
                    if (su === 0 && sv === 0) return;
                    var p = at(su < 0 ? f.umin : su > 0 ? f.umax : (f.umin + f.umax) / 2, sv < 0 ? f.vmin : sv > 0 ? f.vmax : (f.vmin + f.vmax) / 2);
                    var side = r * 1.1;
                    var h = app.make("rect", { x: p[0] - side / 2, y: p[1] - side / 2, width: side, height: side }, {
                        fill: "var(--sp-paper)", stroke: "var(--sp-accent)", strokeWidth: r / 3, cursor: "nwse-resize", touchAction: "none"
                    });
                    h.addEventListener("pointerdown", function (e) {
                        e.preventDefault();
                        e.stopPropagation();
                        app.resizeDrag = { su: su, sv: sv, f: f, orig: zone.points.map(function (q) { return [q[0], q[1]]; }) };
                        view.moved = false;
                        svg.setPointerCapture(e.pointerId);
                    });
                    h.addEventListener("click", function (e) { e.stopPropagation(); view.moved = false; });
                    app.shapes.appendChild(h);
                });
            });
        }
        function resizeTo(p) {
            var d = app.resizeDrag, f = d.f, minSize = app.unit() * 0.5;
            var pu = p[0] * f.u[0] + p[1] * f.u[1], pv = p[0] * f.v[0] + p[1] * f.v[1];
            var umin = d.su < 0 ? Math.min(pu, f.umax - minSize) : f.umin, umax = d.su > 0 ? Math.max(pu, f.umin + minSize) : f.umax;
            var vmin = d.sv < 0 ? Math.min(pv, f.vmax - minSize) : f.vmin, vmax = d.sv > 0 ? Math.max(pv, f.vmin + minSize) : f.vmax;
            app.detach(model.zones[view.active]);
            model.zones[view.active].points = d.orig.map(function (q) {
                var qu = q[0] * f.u[0] + q[1] * f.u[1], qv = q[0] * f.v[0] + q[1] * f.v[1];
                var nu = f.umax > f.umin ? umin + (qu - f.umin) / (f.umax - f.umin) * (umax - umin) : qu;
                var nv = f.vmax > f.vmin ? vmin + (qv - f.vmin) / (f.vmax - f.vmin) * (vmax - vmin) : qv;
                return [Math.round((nu * f.u[0] + nv * f.v[0]) * 10) / 10, Math.round((nu * f.u[1] + nv * f.v[1]) * 10) / 10];
            });
        }

        // ---- ready-made shapes
        var shapeType = document.getElementById("shape-type");
        var shapeA = document.getElementById("shape-a");
        var shapeB = document.getElementById("shape-b");
        var shapeC = document.getElementById("shape-c");
        app.shapeOrient = document.getElementById("shape-orient");
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
            var v = app.shapeOrient.value;
            if (!v || v === "h") return 0;
            var i = parseInt(v.split(":")[1], 10), a = model.boundary[i], b = model.boundary[(i + 1) % model.boundary.length];
            return a && b ? Math.atan2(b[1] - a[1], b[0] - a[0]) : 0;
        }
        // The corners of the shape with the entered size (meters) around the point, or null
        function makeShape(c) {
            var s = app.scaleNow();
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
            if (on) app.stopToolsExcept("shape");
            view.tool = on ? "shape" : app.releaseTool("shape");
            placeButton.classList.toggle("active", on);
            svg.style.cursor = on ? "copy" : "crosshair";
            shapeMsg.hidden = !on;
            shapeMsg.textContent = T.placing;
            if (!on) view.lastMouse = null;
            app.drawOverlay();
        }
        placeButton.addEventListener("click", function () {
            if (app.isTool("shape")) { setPlacing(false); return; }
            app.setCutDrawing(false);
            if (!app.scaleNow()) {
                shapeMsg.hidden = false;
                shapeMsg.textContent = T.noScale;
                return;
            }
            setPlacing(true);
        });
        document.addEventListener("keydown", function (e) {
            if (e.key !== "Escape") return;
            app.stopToolsExcept(null);
            app.setDrawing(false);
            if (view.inserting) app.setInserting(false);
        });

        app.drawResizeHandles = drawResizeHandles;
        app.resizeTo = resizeTo;
        app.showShapeType = showShapeType;
        app.makeShape = makeShape;
        app.setPlacing = setPlacing;

    }

    var api = { create: create };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.GardenShapes = api;
})(typeof self !== "undefined" ? self : this);
