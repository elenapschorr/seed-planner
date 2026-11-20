// The table of the areas, the summary of the sizes and the remarks.
(function (root) {
    "use strict";

    function create(app) {
        var G = app.G,
            T = app.T,
            data = app.data,
            model = app.model,
            view = app.view,
            svg = app.svg;

        // ---- table, summary and remarks
        var table = document.getElementById("edge-table");
        var body = table.querySelector("tbody");
        function buildTable() {
            var n = model.boundary.length >= 3 ? model.boundary.length : 0;
            if (n === app.tableEdges) return;
            app.tableEdges = n;
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
                input.value = model.lengths[i] > 0 ? model.lengths[i] : "";
                input.disabled = app.areaMode();
                cells[1].appendChild(input);
                input.addEventListener("input", (function (index) {
                    return function (e) {
                        var v = parseFloat(e.target.value);
                        model.lengths[index] = v > 0 ? v : null;
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
            box.hidden = scale === null || model.boundary.length < 3;
            if (box.hidden) return;
            var total = G.area(model.boundary), nb = app.neighborAreas(), mine = total, cp = model.cutLine ? app.cutParts() : null;
            nb.forEach(function (a) { mine -= a; });
            function sq(a) { return app.fmt(a * scale * scale, 1) + " m\u00b2"; }
            function centroid(pts) { return [pts.reduce(function (t, p) { return t + p[0]; }, 0) / pts.length, pts.reduce(function (t, p) { return t + p[1]; }, 0) / pts.length]; }
            // one row: level 0 owner, 1 part or area of the owner itself, 2 area in a part
            function addRow(o) {
                var tr = document.createElement("tr");
                if (o.level === 0) tr.className = "fw-semibold"; else if (o.level === 2) tr.className = "text-muted";
                [o.label, o.kind || "", o.area === null ? "" : sq(o.area), o.area === null || total <= 0 ? "" : app.fmt(o.area / total * 100, 1) + " %", o.props || ""].forEach(function (t, ti) {
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
                var zi = model.zones.indexOf(z), b = [[T.rowEdit, "btn-outline-secondary", function () { app.editZoneRow(zi); }]];
                if (withAdopt) b.push([T.adopt, "btn-outline-secondary", function () { app.adoptAsPlot(zi); }]);
                b.push([T.rowDelete, "btn-outline-danger", function () { app.deleteZoneRow(zi); }]);
                return b;
            }
            var owners = [mine].concat(nb);
            owners.forEach(function (ownerArea, o) {
                // the parts of this owner: marked parts and the sides of the straight line
                var parts = model.zones.filter(function (z) { return z.kind === "Mine" && z.owner === o && z.points.length >= 3; }).map(function (z) {
                    return { zone: z, name: z.name, area: app.zoneArea(z), points: z.points };
                });
                if (cp) [model.cutLine.owner1, model.cutLine.owner2].forEach(function (own, i) {
                    if (own === o && cp.pieces[i]) parts.push({ zone: null, name: T.cutName.replace("{0}", String(i + 1)) + " (" + T.cutLineShort + ")", area: G.area(cp.pieces[i]), points: cp.pieces[i] });
                });
                var areas = model.zones.filter(function (z) { return z.kind !== "Mine" && z.owner === o && z.points.length >= 3 && app.zoneArea(z) > 0; });
                var inPart = parts.map(function () { return []; }), loose = [];
                areas.forEach(function (z) {
                    var c = centroid(z.points), best = -1;
                    parts.forEach(function (p, pi) { if (G.pointInPolygon(c, p.points) && (best < 0 || G.area(p.points) < G.area(parts[best].points))) best = pi; });
                    if (best >= 0) inPart[best].push(z); else loose.push(z);
                });
                addRow({ level: 0, label: app.ownerLabel(o), area: ownerArea, key: parts.length + areas.length ? "o" + o : "" });
                if (ownerCollapsed["o" + o]) return;
                function areaRow(z, level) {
                    addRow({ level: level, label: z.name, kind: T.kinds[z.kind] || z.kind, area: app.zoneArea(z), props: app.propSummary(z), buttons: zoneButtons(z, false) });
                }
                parts.forEach(function (p, pi) {
                    var buttons = p.zone ? zoneButtons(p.zone, o === 0) : (o === 0 ? [[T.adopt, "btn-outline-secondary", function () { app.restrictToMine(); }]] : []);
                    addRow({ level: 1, label: p.name, kind: T.partName, area: p.area, buttons: buttons, key: inPart[pi].length ? "p" + o + "-" + pi : "" });
                    if (!ownerCollapsed["p" + o + "-" + pi]) inPart[pi].forEach(function (z) { areaRow(z, 2); });
                });
                loose.forEach(function (z) { areaRow(z, 1); });
            });
        }

        function fillZoneList(scale) {
            var box = document.getElementById("zone-list-box"), list = document.getElementById("zone-list");
            list.replaceChildren();
            box.hidden = model.zones.length === 0;
            model.zones.forEach(function (z, zi) {
                var tr = document.createElement("tr");
                if (zi === view.active) tr.className = "table-active";
                [z.name, T.kinds[z.kind] || z.kind, app.ownerLabel(z.owner), scale === null ? "\u2013" : app.fmt(G.area(z.points) * scale * scale, 1) + " m\u00b2", scale === null ? "\u2013" : app.fmt(app.zoneArea(z) * scale * scale, 1) + " m\u00b2", app.propSummary(z)].forEach(function (t) {
                    var td = document.createElement("td");
                    td.textContent = t;
                    tr.appendChild(td);
                });
                var actions = document.createElement("td");
                actions.className = "text-nowrap";
                [[T.rowEdit, "btn-outline-secondary", function () { app.editZoneRow(zi); }],
                 [T.adopt, "btn-outline-secondary", function () { app.adoptAsPlot(zi); }],
                 [T.rowDelete, "btn-outline-danger", function () { app.deleteZoneRow(zi); }]].forEach(function (b, bi) {
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
            var z = app.activeZone(), box = document.getElementById("zone-edges-box");
            var bodyZ = document.getElementById("zone-edges");
            bodyZ.replaceChildren();
            if (!z || z.points.length < 3 || scale === null) { box.hidden = true; return; }
            box.hidden = false;
            document.getElementById("zone-edges-title").textContent = T.zoneEdges.replace("{0}", z.name);
            var n = z.points.length;
            for (var i = 0; i < n; i++) {
                var tr = document.createElement("tr");
                [(i + 1) + " \u2192 " + ((i + 1) % n + 1), app.fmt(G.edgeLen(z.points, i) * scale, 2) + " m", app.fmt(G.angle(z.points, i), 1) + "\u00b0"].forEach(function (t) {
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
                        app.detach(z);
                        z.isShape = false;
                        z.points[at][axis] = Math.round(v / scale * 10) / 10;
                        app.fillTarget();
                        app.redraw();
                    });
                    td.appendChild(input);
                    tr.appendChild(td);
                });
                var cell = document.createElement("td"), del = document.createElement("button");
                del.type = "button"; del.className = "btn btn-outline-danger btn-sm"; del.textContent = "\u00d7";
                del.title = T.deleteCorner; del.setAttribute("aria-label", T.deleteCorner);
                del.disabled = n <= 3;
                del.addEventListener("click", function () { app.detach(z); z.isShape = false; app.removeCorner(at); app.fillTarget(); });
                cell.appendChild(del);
                tr.appendChild(cell);
                bodyZ.appendChild(tr);
            }
            document.getElementById("zone-area").textContent = app.fmt(G.area(z.points) * scale * scale, 2) + " m\u00b2";
        }

        function update() {
            buildTable();
            var warnings = [];
            var n = model.boundary.length;
            var scale = null;
            if (n < 3) warnings.push(T.warnFew);
            else {
                scale = app.scaleNow();
                if (G.selfIntersects(model.boundary)) warnings.push(T.warnCross);
                if (scale === null) warnings.push(T.noScale);
            }

            if (!app.areaMode()) app.plotArea.value = scale !== null ? (G.area(model.boundary) * scale * scale).toFixed(2) : "";      // the area follows the edges and cannot be changed here
            var rows = body.querySelectorAll("tr");
            for (var i = 0; i < rows.length; i++) {
                var lenInput = rows[i].querySelector(".len");       // the lengths follow the area and cannot be changed then
                if (lenInput) {
                    lenInput.disabled = app.areaMode();
                    if (app.areaMode()) {      // the edges are calculated from the area and kept, so they are there when the user switches to single edges
                        lenInput.value = scale === null ? "" : (G.edgeLen(model.boundary, i) * scale).toFixed(2);
                        model.lengths[i] = scale === null ? null : Math.round(G.edgeLen(model.boundary, i) * scale * 100) / 100;
                    }
                }
                var measured = scale === null ? null : G.edgeLen(model.boundary, i) * scale;
                var dev = (!app.areaMode() && measured !== null && model.lengths[i] > 0) ? (measured - model.lengths[i]) / model.lengths[i] : null;
                rows[i].querySelector(".m").textContent = measured === null ? "–" : app.fmt(measured, 2);
                rows[i].querySelector(".d").textContent = dev === null ? "–" : (dev > 0 ? "+" : "") + app.fmt(dev * 100, 1) + " %";
                rows[i].querySelector(".a").textContent = app.fmt(G.angle(model.boundary, i), 1) + "°";
                if (dev !== null && Math.abs(dev) > 0.05)
                    warnings.push(T.warnDeviation.replace("{0}", String(i + 1)).replace("{1}", app.fmt(Math.abs(dev) * 100, 0)));
            }

            var total = null, cut = null, occupied = null, available = null;
            if (n >= 3 && scale !== null) {
                var a = app.figures(model.boundary), k2 = scale * scale;
                total = a.total * k2;
                cut = Math.max(0, a.cut * k2);
                occupied = Math.max(0, a.built * k2);
                available = a.available * k2;
            }
            fillZoneList(n >= 3 && scale !== null ? scale : null);
            fillOwnerList(total === null ? null : scale);
    if (app.editMode) document.getElementById("edit-need").hidden = app.plotDefined();
            fillZoneEdges(scale);
            app.updateCut(scale);
            function show(id, v) { document.getElementById(id).textContent = v === null ? "–" : app.fmt(v, 1) + " m²"; }
            show("sum-total", total);
            show("sum-cut", cut);
            show("sum-blocked", occupied);
            show("sum-available", available);
            document.getElementById("chip-available").textContent = available === null ? "\u2013" : app.fmt(available, 1) + " m\u00b2";

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
            document.getElementById("f-boundary").value = JSON.stringify(model.boundary);
            document.getElementById("f-lengths").value = JSON.stringify(model.lengths);
            document.getElementById("f-zones").value = JSON.stringify(model.zones);
            document.getElementById("f-neighbors").value = JSON.stringify(model.neighbors);
            document.getElementById("f-cutline").value = model.cutLine ? JSON.stringify(model.cutLine) : "";
            document.getElementById("f-placed").value = JSON.stringify(model.placedBeds);
            document.getElementById("f-area").value = app.areaMode() && parseFloat(app.plotArea.value) > 0 ? app.plotArea.value : "";
        });

        app.fillZoneList = fillZoneList;
        app.update = update;

    }

    var api = { create: create };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.GardenTable = api;
})(typeof self !== "undefined" ? self : this);
