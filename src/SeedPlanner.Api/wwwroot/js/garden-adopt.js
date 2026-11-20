// Editing the marked areas as a list, and taking one area (or the own part) as the new plot while the rest is removed, with the button that takes this back.
(function (root) {
    "use strict";

    function create(app) {
        var G = app.G,
            T = app.T,
            data = app.data,
            model = app.model,
            view = app.view,
            svg = app.svg;

        // ---- editing the marked areas as a list, and taking one area as the new plot (the rest is removed)
        var adoptBackup = null;
        function showAdoptMsg(text) {
            var m = document.getElementById("adopt-msg");
            m.textContent = text || "";
            m.hidden = !text;
            document.getElementById("adopt-undo").hidden = !adoptBackup;
        }
        function adoptAsPlot(i) {
            var z = model.zones[i];
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
            var start = given || [model.boundary.map(function (p) { return [p[0], p[1]]; })];
            var cp = !given && model.cutLine ? app.cutParts() : null;
            if (cp) {
                var owners = [model.cutLine.owner1, model.cutLine.owner2], mine = cp.pieces.filter(function (p, k) { return p && owners[k] === 0; });
                if (mine.length < 2) start = mine;      // both sides are the user's: the whole plot stays
            }
            var neighborZones = model.zones.filter(function (z) { return z.owner > 0 && z.points.length >= 3; });
            // corners that stop up to about 1/150 of the picture short of the edge of the plot count as reaching it
            var r = G.subtractAll(start, neighborZones.map(function (z) { return z.points; }), Math.max(app.W, app.H) / 150), best = null;
            r.polys.forEach(function (p) { if (!best || G.area(p) > G.area(best)) best = p; });
            var names = neighborZones.filter(function (z) { return r.holes.indexOf(z.points) >= 0; }).map(function (z) { return z.name; });
            return { poly: best, others: r.polys.length - 1, holes: names.length, holeNames: names.join(", ") };
        }
        document.getElementById("restrict-mine").addEventListener("click", restrictToMine);
        function restrictToMine() {
            if (model.boundary.length < 3) return;
            var m = minePolygon();
            if (!m.poly) { showAdoptMsg(T.restrictNone); return; }
            adoptPolygon(m.poly, "", -1, m);
        }
        function adoptPolygon(poly, name, skip, extra) {
            var s = app.scaleNow();
            if (s === null) { showAdoptMsg(T.noScale); return; }
            var areaM2 = G.area(poly) * s * s;
            var inside = function (o) {
                var cx = o.points.reduce(function (t, p) { return t + p[0]; }, 0) / o.points.length;
                var cy = o.points.reduce(function (t, p) { return t + p[1]; }, 0) / o.points.length;
                return G.pointInPolygon([cx, cy], poly) || o.points.some(function (p) { return G.pointInPolygon(p, poly); });
            };
            var kept = model.zones.filter(function (o, k) { return k !== skip && o.owner === 0 && o.points.length >= 3 && inside(o); });
            var dropped = model.zones.length - (skip >= 0 ? 1 : 0) - kept.length;
            var question = T.adoptConfirm.replace("{0}", name || T.mineName).replace("{1}", app.fmt(areaM2, 1)).replace("{2}", String(dropped));
            if (extra && (extra.others > 0 || extra.holes > 0)) question += "\n\n" + T.restrictWarn.replace("{0}", String(extra.others)).replace("{1}", extra.holeNames || "\u2013");
            if (!window.confirm(question)) return;
            adoptBackup = { plot: model.snapshot(), mode: app.scaleMode.value, area: app.plotArea.value };
            model.boundary = poly;
            model.lengths = poly.map(function (p, k) { return Math.round(G.edgeLen(poly, k) * s * 100) / 100; });      // the edges of the new plot, calculated from the scale
            model.zones = kept;
            model.zones.forEach(app.detach);
            model.neighbors = [];
            model.cutLine = null;
            view.cutStart = null;
            app.setCutDrawing(false);
            app.scaleMode.value = "area";       // the scale stays as it was: the area of the new plot in square meters at the same meters per pixel
            app.plotArea.value = areaM2.toFixed(3);
            view.active = -1;
            app.buildNeighbors();
            app.showScaleMode();
            app.fillTarget();
            var box = document.getElementById("mine-only");
            box.checked = view.mineOnly = true;
            model.notify();
            app.fitMine();
            showAdoptMsg(T.adoptDone.replace("{0}", app.fmt(areaM2, 1)).replace("{1}", String(kept.length)));
        }
        // Shows a plot that was put back by model.restore(): the fields next to the picture follow the data
        function showRestoredPlot() {
            if (view.active >= model.zones.length) view.active = -1;
            app.buildNeighbors();
            if (model.cutLine) {
                document.getElementById("cut-owner1").value = String(model.cutLine.owner1);
                document.getElementById("cut-owner2").value = String(model.cutLine.owner2);
            }
            app.showScaleMode();
            app.fillTarget();
            model.notify();
        }
        document.getElementById("adopt-undo").addEventListener("click", function () {
            if (!adoptBackup) return;
            var b = adoptBackup;
            adoptBackup = null;
            model.restore(b.plot);
            app.scaleMode.value = b.mode; app.plotArea.value = b.area;
            view.active = -1;
            showRestoredPlot();
            showAdoptMsg("");
        });
        function editZoneRow(i) {
            view.active = i;
            app.setDrawing(false);
            app.fillTarget();
            app.redraw();
            app.showTools(true);
            var stage = document.getElementById("stage");
            if (stage.scrollIntoView) stage.scrollIntoView({ block: "nearest" });
        }
        function deleteZoneRow(i) {
            if (!window.confirm(T.rowDeleteConfirm.replace("{0}", model.zones[i].name))) return;
            model.zones.splice(i, 1);
            if (view.active === i) view.active = -1; else if (view.active > i) view.active--;
            app.fillTarget();
            app.redraw();
        }

        app.adoptAsPlot = adoptAsPlot;
        app.restrictToMine = restrictToMine;
        app.showRestoredPlot = showRestoredPlot;
        app.editZoneRow = editZoneRow;
        app.deleteZoneRow = deleteZoneRow;

    }

    var api = { create: create };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.GardenAdopt = api;
})(typeof self !== "undefined" ? self : this);
