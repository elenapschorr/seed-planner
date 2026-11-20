// Splitting an area by a line parallel to an edge, and the straight line through the plot (cut line) with the sizes of both parts.
(function (root) {
    "use strict";

    function create(app) {
        var G = app.G,
            T = app.T,
            data = app.data,
            model = app.model,
            view = app.view,
            svg = app.svg;

        // ---- splitting an area by a line parallel to an edge
        app.splitPanel = document.getElementById("split-panel");
        var splitBox = document.getElementById("split-box");
        var splitNeed = document.getElementById("split-need");
        var splitRef = document.getElementById("split-ref");
        var splitPos = document.getElementById("split-pos");

        function edgeVector(value) {
            if (!value) return null;
            var part = value.split(":"), i = parseInt(part[1], 10);
            if (part[0] === "s") {      // the stored line of a split: along it is across the stored normal
                var g = app.activeZone() && app.activeZone().splitLine;
                return g ? [g[1], -g[0]] : null;
            }
            var pts = part[0] === "b" ? model.boundary : app.splitPoly(app.activeZone());
            var a = pts[i], b = pts[(i + 1) % pts.length];
            return a && b ? [b[0] - a[0], b[1] - a[1]] : null;
        }
        // The line: unit vectors along it (dx, dy) and across it (nx, ny), the position t0 and the extent of the area
        function splitLine() {
            var z = app.activeZone(), d = edgeVector(splitRef.value);
            if (!z || app.splitPoly(z).length < 3 || !d) return null;
            var len = Math.hypot(d[0], d[1]);
            if (!(len > 0)) return null;
            var nx = -d[1] / len, ny = d[0] / len;
            var ext = G.extent(app.splitPoly(z), nx, ny);
            return { dx: d[0] / len, dy: d[1] / len, nx: nx, ny: ny, t0: ext.min + parseFloat(splitPos.value) / 100 * (ext.max - ext.min), ext: ext };
        }
        var splitNum = document.getElementById("split-pos-num");
        var splitDist = document.getElementById("split-dist");
        function setSplitPercent(v) {
            v = Math.min(100, Math.max(0, v));
            splitPos.value = Math.round(v * 10) / 10;
            document.getElementById("split-pos-label").textContent = splitPos.value + " %";
        }
        splitPos.addEventListener("input", function () {
            setSplitPercent(parseFloat(splitPos.value));
            app.drawOverlay();
        });
        // the position as a number: in percent of the area, or as the distance in meters from the side of part 1
        splitNum.addEventListener("input", function () {
            var v = parseFloat(splitNum.value);
            if (!(v >= 0)) return;
            setSplitPercent(v);
            app.drawOverlay();
        });
        splitDist.addEventListener("input", function () {
            var line = splitLine(), s = app.scaleNow(), v = parseFloat(splitDist.value);
            if (!line || !s || !(v >= 0) || !(line.ext.max > line.ext.min)) return;
            setSplitPercent(v / s / (line.ext.max - line.ext.min) * 100);
            app.drawOverlay();
        });
        // the number fields follow the slider and the reference edge (not while one of them is typed in)
        function syncSplitFields(line) {
            var s = app.scaleNow();
            if (document.activeElement !== splitNum) splitNum.value = splitPos.value;
            if (document.activeElement !== splitDist)
                splitDist.value = s ? Math.round((line.t0 - line.ext.min) * s * 100) / 100 : "";
            splitDist.disabled = !s;
        }
        splitRef.addEventListener("change", app.drawOverlay);
        // wanted area (m²) of part 1 -> position of the line
        document.getElementById("split-area-set").addEventListener("click", function () {
            var z = app.activeZone(), d = edgeVector(splitRef.value), s = app.scaleNow();
            var wanted = parseFloat(document.getElementById("split-area").value);
            if (!z || !d || !s || !(wanted > 0)) return;
            var len = Math.hypot(d[0], d[1]), nx = -d[1] / len, ny = d[0] / len;
            var t0 = G.splitPositionForArea(app.splitPoly(z), nx, ny, wanted / (s * s));
            if (t0 === null) return;
            var ext = G.extent(app.splitPoly(z), nx, ny);
            setSplitPercent((t0 - ext.min) / (ext.max - ext.min) * 100);
            app.drawOverlay();
        });
        app.splitPanel.addEventListener("toggle", app.drawOverlay);
        document.getElementById("split-do").addEventListener("click", function () {
            var z = app.activeZone(), line = splitLine();
            if (!z || !line) return;
            var parent = app.splitPoly(z);
            var parts = G.splitByLine(parent, line.nx, line.ny, line.t0);
            if (!parts.below || !parts.above) return;      // the line must run through the area
            var kind1 = document.getElementById("split-kind1").value, kind2 = document.getElementById("split-kind2").value;
            var owner1 = parseInt(document.getElementById("split-owner1").value, 10) || 0, owner2 = parseInt(document.getElementById("split-owner2").value, 10) || 0;
            var stored = [Math.round(line.nx * 1e6) / 1e6, Math.round(line.ny * 1e6) / 1e6, Math.round(line.t0 * 10) / 10];
            var g = app.groupOf(z);
            if (g) {      // the split is changed: both parts follow the new line
                g.a.points = parts.below; g.a.kind = kind1; g.a.owner = owner1; g.a.splitLine = stored;
                g.b.points = parts.above; g.b.kind = kind2; g.b.owner = owner2; g.b.splitLine = stored;
            } else {
                var id = 1 + model.zones.reduce(function (m, o) { return Math.max(m, o.splitGroup | 0); }, 0);
                var copy = z.points.map(function (q) { return [q[0], q[1]]; });
                var part = function (name, kind, owner, points, number) {
                    return {
                        name: name, kind: kind, owner: owner, isShape: false, points: points, splitGroup: id, splitPart: number, splitParent: copy, splitLine: stored,
                        use: z.use, sun: z.sun, soil: z.soil, slope: z.slope, waterlogged: z.waterlogged, waterNearby: z.waterNearby, coldFramePossible: z.coldFramePossible
                    };
                };
                model.zones.splice(view.active, 1, part(z.name + " (1)", kind1, owner1, parts.below, 1), part(z.name + " (2)", kind2, owner2, parts.above, 2));
            }
            app.fillTarget();
            app.redraw();
        });

        document.getElementById("split-undo").addEventListener("click", function () {
            var z = app.activeZone(), g = app.groupOf(z);
            if (!g) return;
            var joined = { name: g.a.name.replace(/ \(1\)$/, ""), kind: g.a.kind, owner: g.a.owner, isShape: false, points: g.a.splitParent.map(function (q) { return [q[0], q[1]]; }), splitGroup: 0, splitPart: 0, splitParent: null, splitLine: null,
                use: g.a.use, sun: g.a.sun, soil: g.a.soil, slope: g.a.slope, waterlogged: g.a.waterlogged, waterNearby: g.a.waterNearby, coldFramePossible: g.a.coldFramePossible };
            var first = Math.min(model.zones.indexOf(g.a), model.zones.indexOf(g.b));
            model.zones = model.zones.filter(function (o) { return o !== g.a && o !== g.b; });
            model.zones.splice(first, 0, joined);
            view.active = first;
            app.fillTarget();
            app.redraw();
        });

        var refSignature = "";
        function fillRefs() {
            var z = app.activeZone();
            var grouped = app.groupOf(z);
            var canSplit = !!z && app.splitPoly(z).length >= 3;
            splitBox.hidden = !canSplit;
            splitNeed.hidden = canSplit;
            document.getElementById("split-undo").hidden = !grouped;
            var signature = model.boundary.length + "|" + view.active + "|" + (z ? z.points.length : 0) + "|" + (grouped ? z.splitGroup : 0);
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
                document.getElementById("split-owner2").value = String(model.neighbors.length > 0 && z.owner === 0 ? 1 : z.owner);
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
            if (model.boundary.length >= 3)
                for (var i = 0; i < model.boundary.length; i++) {
                    orientEntries.push(["b:" + i, T.orientEdge.replace("{0}", String(i + 1))]);
                    splitEntries.push(["b:" + i, T.splitBoundaryEdge.replace("{0}", String(i + 1))]);
                }
            if (grouped) splitEntries.unshift(["s", T.splitStored]);
            if (canSplit)
                for (var j = 0; j < app.splitPoly(z).length; j++)
                    splitEntries.push(["z:" + j, T.splitZoneEdge.replace("{0}", String(j + 1))]);
            refill(app.shapeOrient, orientEntries);
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
            if (on) app.stopToolsExcept("cut");
            view.tool = on ? "cut" : app.releaseTool("cut");
            view.cutStart = null;
            if (on) {
                model.cutLine = null;
                cutMsg.hidden = false;
                cutMsg.textContent = T.cutDrawing;
            } else cutMsg.hidden = true;
            svg.style.cursor = on ? "crosshair" : (app.isTool("shape") ? "copy" : "crosshair");
            if (!on) view.lastMouse = null;
            app.update();
            app.drawOverlay();
        }
        document.getElementById("cut-draw").addEventListener("click", function () { setCutDrawing(!app.isTool("cut")); });
        document.getElementById("cut-clear").addEventListener("click", function () {
            model.cutLine = null;
            setCutDrawing(false);
        });
        // the two parts of the plot; null without a line or a plot
        function cutParts() {
            if (!model.cutLine || model.boundary.length < 3) return null;
            var dx = model.cutLine.b[0] - model.cutLine.a[0], dy = model.cutLine.b[1] - model.cutLine.a[1], len = Math.hypot(dx, dy);
            if (!(len > 0)) return null;
            var nx = -dy / len, ny = dx / len;
            var parts = G.splitByLine(model.boundary, nx, ny, model.cutLine.a[0] * nx + model.cutLine.a[1] * ny);
            return { dx: dx / len, dy: dy / len, pieces: [parts.below, parts.above] };
        }
        function pieceAreas(piece, scale) {
            if (!piece) return { gross: 0, free: 0, garden: null };
            app.skipImplicit = true;       // the part itself is judged by the areas that are drawn, not by who owns it
            var f = app.figures(piece), k = scale * scale;
            app.skipImplicit = false;
            return { gross: G.area(piece) * k, free: f.free * k, garden: f.hasGarden ? f.available * k : null };
        }
        function updateCut(scale) {
            var parts = cutParts(), result = document.getElementById("cut-result");
            document.getElementById("cut-clear").hidden = !model.cutLine && !app.isTool("cut");
            result.hidden = !parts || scale === null;
            if (result.hidden) return;
            parts.pieces.forEach(function (piece, i) {
                var a = pieceAreas(piece, scale);
                document.getElementById("cut-gross" + (i + 1)).textContent = app.fmt(a.gross, 1) + " m\u00b2";
                document.getElementById("cut-free" + (i + 1)).textContent = app.fmt(a.free, 1) + " m\u00b2";
                document.getElementById("cut-garden-row" + (i + 1)).hidden = a.garden === null;
                if (a.garden !== null) document.getElementById("cut-garden" + (i + 1)).textContent = app.fmt(a.garden, 1) + " m\u00b2";
                document.getElementById("cut-take" + (i + 1)).disabled = !piece;
            });
        }
        [1, 2].forEach(function (n) {
            document.getElementById("cut-take" + n).addEventListener("click", function () {
                var parts = cutParts();
                if (!parts || !parts.pieces[n - 1]) return;
                model.zones.push({ name: T.cutName.replace("{0}", String(n)), kind: document.getElementById("cut-kind" + n).value, owner: parseInt(document.getElementById("cut-owner" + n).value, 10) || 0, isShape: false, points: parts.pieces[n - 1] });
                view.active = model.zones.length - 1;
                app.fillTarget();
                app.redraw();
            });
        });

        app.splitLine = splitLine;
        app.syncSplitFields = syncSplitFields;
        app.fillRefs = fillRefs;
        app.setCutDrawing = setCutDrawing;
        app.cutParts = cutParts;
        app.updateCut = updateCut;

    }

    var api = { create: create };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.GardenCuts = api;
})(typeof self !== "undefined" ? self : this);
