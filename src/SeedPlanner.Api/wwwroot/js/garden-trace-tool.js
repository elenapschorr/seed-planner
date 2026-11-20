// The tool "take an area from the lines in the picture" (cadastral map): the click into an outline sets the boundary or an area from the lines (garden-trace.js
// does the calculation). The page gives what it needs as `page` and gets the two functions back that the tool switch of the page uses.
(function (root) {
    "use strict";

    function create(page) {
        var T = page.T,
            data = page.data,
            model = page.model,
            view = page.view,
            svg = page.svg,
            probe = page.probe,
            drawOverlay = page.drawOverlay,
            fillTarget = page.fillTarget,
            redraw = page.redraw,
            releaseTool = page.releaseTool,
            showScaleMode = page.showScaleMode,
            showSelection = page.showSelection,
            stopToolsExcept = page.stopToolsExcept,
            uniqueName = page.uniqueName,
            detach = page.detach;
        function width() { return page.width(); }       // the size of the picture is known only when it is loaded
        function height() { return page.height(); }

        // ---- taking an area from the lines that are already in the picture (cadastral map): one click into the outline
        var pixelData = null;       // the pixels of the picture (read once)
        function getPixels() {
            if (pixelData) return pixelData;
            try {
                var canvas = document.createElement("canvas");
                canvas.width = width(); canvas.height = height();
                var ctx = canvas.getContext("2d", { willReadFrequently: true });
                ctx.drawImage(probe, 0, 0, width(), height());
                pixelData = ctx.getImageData(0, 0, width(), height()).data;
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
            if (kind && view.traceKind !== kind) stopToolsExcept("trace");
            var before = view.traceKind;
            view.traceKind = kind;
            view.tool = kind ? "trace" : releaseTool("trace");
            ["zone", "boundary"].forEach(function (k) {
                var b = document.getElementById("trace-" + k);
                if (b) b.classList.toggle("active", view.traceKind === k);
                traceMessage(k, view.traceKind === k ? T.traceHint : "");
            });
            svg.style.cursor = view.traceKind ? "cell" : "crosshair";
            if (!kind && before) view.lastMouse = null;
            drawOverlay();
        }
        function traceAt(p) {
            var data = getPixels(), kind = view.traceKind;
            if (!data || !window.GardenTrace) { traceMessage(kind, T.traceNoImage); return; }
            var r = window.GardenTrace.polygonAt(data, width(), height(), p[0], p[1]);
            if (r.status !== "ok") {
                traceMessage(kind, r.status === "line" ? T.traceLine : r.status === "tiny" ? T.traceTiny : T.traceOpen);
                return;
            }
            if (kind === "boundary") {
                if (model.boundary.length >= 3 && !window.confirm(T.traceReplace)) return;
                model.boundary = r.points;
                model.lengths = r.points.map(function () { return null; });
                view.selected = -1;
                showSelection();
                setTracing(null);
                showScaleMode();
                redraw();
                return;
            }
            var input = document.getElementById("zone-name"), k = document.getElementById("zone-kind").value;
            model.zones.push({ name: input.value.trim() || uniqueName(T.kinds[k]), kind: k, owner: parseInt(document.getElementById("zone-owner").value, 10) || 0, isShape: false, points: r.points });
            input.value = "";
            view.active = model.zones.length - 1;
            setTracing(null);
            fillTarget();
            redraw();
        }
        ["zone", "boundary"].forEach(function (k) {
            var b = document.getElementById("trace-" + k);
            if (b) b.addEventListener("click", function () { setTracing(view.traceKind === k ? null : k); });
        });

        return { setTracing, traceAt };
    }

    var api = { create: create };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.GardenTraceTool = api;
})(typeof self !== "undefined" ? self : this);
