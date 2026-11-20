// The neighbours: how many there are and what they are called, who owns which area, the target of the shape tools, and the properties of an area.
(function (root) {
    "use strict";

    function create(app) {
        var G = app.G,
            T = app.T,
            data = app.data,
            model = app.model,
            view = app.view,
            svg = app.svg;

        // ---- the neighbours: how many there are and what they are called; every area is owned by the user or by one of them
        function fillOwnerSelects() {
            document.querySelectorAll(".owner-select").forEach(function (select) {
                var keep = select.value;
                select.replaceChildren();
                for (var o = 0; o <= model.neighbors.length; o++) {
                    var option = document.createElement("option");
                    option.value = String(o);
                    option.textContent = app.ownerLabel(o);
                    select.appendChild(option);
                }
                var add = document.createElement("option");      // a new neighbour can be added right here
                add.value = "+";
                add.textContent = T.neighborAdd;
                select.appendChild(add);
                select.value = parseInt(keep, 10) <= model.neighbors.length ? keep : "0";
            });
        }
        // choosing "+ neighbour" in any owner list adds a neighbour and selects it (before the other handlers of the list see the value)
        document.addEventListener("change", function (e) {
            var select = e.target;
            if (!select || !select.classList || !select.classList.contains("owner-select") || select.value !== "+") return;
            if (model.neighbors.length >= 10) { select.value = "0"; return; }
            model.neighbors.push("");
            buildNeighbors();
            select.value = String(model.neighbors.length);
        }, true);
        function buildNeighbors() {
            var holder = document.getElementById("neighbor-names");
            document.getElementById("neighbor-count").value = model.neighbors.length;
            holder.replaceChildren();
            model.neighbors.forEach(function (name, k) {
                var input = document.createElement("input");
                input.type = "text";
                input.maxLength = 100;
                input.className = "form-control form-control-sm";
                input.style.maxWidth = "10rem";
                input.value = name;
                input.placeholder = T.neighborName.replace("{0}", String(k + 1));
                input.setAttribute("aria-label", input.placeholder);
                input.addEventListener("input", function () {
                    model.neighbors[k] = input.value;
                    fillOwnerSelects();
                    fillTarget();
                    app.redraw();
                });
                holder.appendChild(input);
            });
            fillOwnerSelects();
        }
        document.getElementById("neighbor-count").addEventListener("change", function (e) {
            var count = Math.min(10, Math.max(0, parseInt(e.target.value, 10) || 0));
            while (model.neighbors.length < count) model.neighbors.push("");
            model.neighbors.length = count;
            model.zones.forEach(function (z) { if (z.owner > count) z.owner = 0; });   // an area of a neighbour that is gone goes back to the user
            if (model.cutLine) { if (model.cutLine.owner1 > count) model.cutLine.owner1 = 0; if (model.cutLine.owner2 > count) model.cutLine.owner2 = 0; }
            buildNeighbors();
            fillTarget();
            app.redraw();
        });

        function fillTarget() {
            view.selected = -1;
            app.showSelection();
            app.target.replaceChildren();
            var first = document.createElement("option");
            first.value = "-1";
            first.textContent = T.boundary;
            app.target.appendChild(first);
            model.zones.forEach(function (z, i) {
                var o = document.createElement("option");
                o.value = String(i);
                o.textContent = z.name + " – " + (T.kinds[z.kind] || z.kind) + " – " + app.ownerLabel(z.owner);
                app.target.appendChild(o);
            });
            app.target.value = String(view.active);
            app.deleteZone.hidden = view.active < 0;
            document.getElementById("adopt-zone").hidden = view.active < 0 || !(model.zones[view.active].kind === "Mine" && model.zones[view.active].owner === 0 && model.zones[view.active].points.length >= 3);
            document.getElementById("active-kind-box").hidden = view.active < 0;
            document.getElementById("active-owner-box").hidden = view.active < 0;
            document.getElementById("active-name-box").hidden = view.active < 0;
            document.getElementById("shape-to-corners").hidden = view.active < 0 || !model.zones[view.active].isShape;
            showProps();
            if (view.active >= 0) {
                document.getElementById("active-name").value = model.zones[view.active].name;
                document.getElementById("active-kind").value = model.zones[view.active].kind;
                document.getElementById("active-owner").value = String(model.zones[view.active].owner);
            }
        }
        document.getElementById("active-name").addEventListener("input", function (e) {
            if (view.active < 0) return;
            model.zones[view.active].name = e.target.value;
            app.target.options[view.active + 1].textContent = model.zones[view.active].name + " \u2013 " + (T.kinds[model.zones[view.active].kind] || model.zones[view.active].kind) + " \u2013 " + app.ownerLabel(model.zones[view.active].owner);
            app.redraw();
        });
        // what the user knows about the active area (to place beds later)
        var propFields = { use: "prop-use", sun: "prop-sun", soil: "prop-soil", slope: "prop-slope" };
        var propFlags = { waterlogged: "prop-waterlogged", waterNearby: "prop-water", coldFramePossible: "prop-coldframe" };
        function showProps() {
            document.getElementById("props-panel").hidden = view.active < 0;
            if (view.active < 0) return;
            Object.keys(propFields).forEach(function (k) { document.getElementById(propFields[k]).value = model.zones[view.active][k] || ""; });
            Object.keys(propFlags).forEach(function (k) { document.getElementById(propFlags[k]).checked = !!model.zones[view.active][k]; });
        }
        Object.keys(propFields).forEach(function (k) {
            document.getElementById(propFields[k]).addEventListener("change", function (e) {
                if (view.active >= 0) { model.zones[view.active][k] = e.target.value; app.fillZoneList(app.scaleNow()); }
            });
        });
        Object.keys(propFlags).forEach(function (k) {
            document.getElementById(propFlags[k]).addEventListener("change", function (e) {
                if (view.active >= 0) { model.zones[view.active][k] = e.target.checked; app.fillZoneList(app.scaleNow()); }
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
            if (view.active < 0 || !model.zones[view.active].isShape) return;
            model.zones[view.active].isShape = false;
            app.detach(model.zones[view.active]);
            fillTarget();
            app.redraw();
        });
        document.getElementById("active-owner").addEventListener("change", function (e) {
            if (view.active < 0) return;
            model.zones[view.active].owner = parseInt(e.target.value, 10) || 0;
            fillTarget();
            app.redraw();
        });
        document.getElementById("active-kind").addEventListener("change", function (e) {
            if (view.active < 0) return;
            model.zones[view.active].kind = e.target.value;
            fillTarget();
            app.redraw();
        });
        app.target.addEventListener("change", function () {
            view.active = parseInt(app.target.value, 10);
            app.setDrawing(false);
            app.setInserting(false);
            fillTarget();
            app.redraw();
        });
        document.getElementById("delete-point").addEventListener("click", function () {
            if (view.selected >= 0) app.removeCorner(view.selected);
        });
        document.addEventListener("keydown", function (e) {
            if ((e.key !== "Delete" && e.key !== "Backspace") || view.selected < 0) return;
            var tag = document.activeElement ? document.activeElement.tagName : "";
            if (tag === "INPUT" || tag === "SELECT" || tag === "TEXTAREA") return;
            e.preventDefault();
            app.removeCorner(view.selected);
        });
        document.getElementById("undo").addEventListener("click", function () {
            var pts = view.active < 0 ? model.boundary : model.zones[view.active].points;
            if (view.active >= 0 && model.zones[view.active].isShape) return;
            if (pts.length > 0) app.removeCorner(pts.length - 1);
        });
        document.getElementById("clear").addEventListener("click", function () {
            if (view.active < 0 && model.boundary.length > 0 && !window.confirm(T.clearBoundaryConfirm)) return;
            if (view.active < 0) {
                model.boundary.length = 0;
                model.lengths.length = 0;
            } else {
                app.detach(model.zones[view.active]);
                model.zones[view.active].points.length = 0;
            }
            if (view.active >= 0 && model.zones[view.active].isShape) model.zones[view.active].isShape = false;   // an emptied shape is drawn by hand again
            app.setDrawing(true);
            app.redraw();
        });
        document.getElementById("add-zone").addEventListener("click", function () {
            var input = document.getElementById("zone-name");
            model.zones.push({ name: input.value.trim() || app.uniqueName(T.kinds[document.getElementById("zone-kind").value]), kind: document.getElementById("zone-kind").value, owner: parseInt(document.getElementById("zone-owner").value, 10) || 0, isShape: false, points: [] });
            input.value = "";
            view.active = model.zones.length - 1;
            fillTarget();
            app.setDrawing(true);
            app.redraw();
        });
        app.deleteZone.addEventListener("click", function () {
            if (view.active < 0) return;
            model.zones.splice(view.active, 1);
            view.active = -1;
            fillTarget();
            app.redraw();
        });

        app.buildNeighbors = buildNeighbors;
        app.fillTarget = fillTarget;
        app.propSummary = propSummary;

    }

    var api = { create: create };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.GardenNeighbors = api;
})(typeof self !== "undefined" ? self : this);
