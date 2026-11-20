// The beds on the plot picture: the beds of the user are put on the plot (drawn, moved, turned, removed), the list of the beds with remarks,
// and the view of the own part only (the rest of the picture is hidden). The page gives what it needs as ctx and gets the functions back.
(function (root) {
    "use strict";

    function create(ctx) {
        var G = ctx.G,
            T = ctx.T,
            data = ctx.data,
            model = ctx.model,
            view = ctx.view,
            svg = ctx.svg,
            editMode = ctx.editMode,
            BUILT = ctx.BUILT,
            bedLayer = ctx.bedLayer,
            mineClip = ctx.mineClip,
            mineMask = ctx.mineMask,
            picture = ctx.picture,
            wrap = ctx.wrap,
            anyToolActive = ctx.anyToolActive,
            drawOverlay = ctx.drawOverlay,
            figures = ctx.figures,
            fitZoom = ctx.fitZoom,
            fmt = ctx.fmt,
            kindPoints = ctx.kindPoints,
            label = ctx.label,
            make = ctx.make,
            neighborPoints = ctx.neighborPoints,
            propSummary = ctx.propSummary,
            redraw = ctx.redraw,
            releaseTool = ctx.releaseTool,
            scaleNow = ctx.scaleNow,
            setZoom = ctx.setZoom,
            stopToolsExcept = ctx.stopToolsExcept,
            toSvgRaw = ctx.toSvgRaw,
            unit = ctx.unit,
            zoneArea = ctx.zoneArea;
        function width() { return ctx.width(); }       // the size of the picture is known only when it is loaded
        function height() { return ctx.height(); }

        // ---- beds: the user's beds are placed on the plot (centre in pixels of the picture, angle in degrees)
        var beds = data.beds || [];
        model.placedBeds = (data.placed || []).filter(function (p) { return bedById(p.bedId); }).map(function (p) { return { bedId: p.bedId, x: +p.x, y: +p.y, angle: +p.angle || 0 }; });
        function bedById(id) { for (var i = 0; i < beds.length; i++) if (beds[i].id === id) return beds[i]; return null; }
        function placedBed(id) { for (var i = 0; i < model.placedBeds.length; i++) if (model.placedBeds[i].bedId === id) return model.placedBeds[i]; return null; }
        function ptsAttr(poly) { return poly.map(function (p) { return p[0] + "," + p[1]; }).join(" "); }
        function bedArea(b) { var a = b.widthCm / 100 * b.lengthCm / 100; return b.shape === "Oval" ? a * Math.PI / 4 : a; }
        function bedPoly(pl, s) {
            var b = bedById(pl.bedId);
            return b && s ? G.bedOutline(pl.x, pl.y, b.lengthCm / 100, b.widthCm / 100, pl.angle, b.shape === "Oval", s) : null;
        }
        // beds can only be placed once the plot is defined: the parts of the neighbours are gone (the own part was taken as the plot)
        function plotDefined() {
            return model.boundary.length >= 3 && scaleNow() !== null && !model.zones.some(function (z) { return z.owner > 0; }) && !(model.cutLine && (model.cutLine.owner1 > 0 || model.cutLine.owner2 > 0));
        }
        // what is not the user's (neighbours, built on) and the garden areas (if there are any, only they count)
        function regionParts() { return { bad: neighborPoints().concat(kindPoints(BUILT)), ga: kindPoints(["Garden"]) }; }
        function inMine(p, parts) {
            if (!G.pointInPolygon(p, model.boundary)) return false;
            for (var i = 0; i < parts.bad.length; i++) if (G.pointInPolygon(p, parts.bad[i])) return false;
            return parts.ga.length === 0 || parts.ga.some(function (q) { return G.pointInPolygon(p, q); });
        }
        // where a bed stands: inside the user's part? on another bed? what the area says about the place
        function bedStatus(pl, parts, polys) {
            var st = { outside: false, overlaps: [], hints: [] }, poly = polys[pl.bedId], b = bedById(pl.bedId);
            if (!poly || !b) return st;
            st.outside = model.boundary.length < 3 || !poly.concat([[pl.x, pl.y]]).every(function (p) { return inMine(p, parts); });
            model.placedBeds.forEach(function (o) {
                if (o !== pl && polys[o.bedId] && G.convexOverlap(poly, polys[o.bedId], 0.3)) st.overlaps.push(bedById(o.bedId).name);
            });
            var zone = null, zoneArea = Infinity;
            model.zones.forEach(function (z) {
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
            if (s !== null) model.placedBeds.forEach(function (pl) { polys[pl.bedId] = bedPoly(pl, s); });
            return polys;
        }
        function selectBed(id) {
            view.activeBed = id;
            document.querySelectorAll("#bed-list details").forEach(function (d) { d.open = parseInt(d.dataset.bed, 10) === id; });
            drawBeds();
        }
        function drawBeds() {
            bedLayer.replaceChildren();
            var s = scaleNow();
            if (s === null) return;
            var parts = regionParts(), polys = bedPolys(s), r = unit();
            model.placedBeds.forEach(function (pl) {
                var b = bedById(pl.bedId), poly = polys[pl.bedId];
                if (!b || !poly) return;
                var st = bedStatus(pl, parts, polys), bad = st.outside || st.overlaps.length > 0, chosen = pl.bedId === view.activeBed;
                var shape = make("polygon", { points: ptsAttr(poly) }, {
                    fill: bad ? "rgba(179,38,30,0.35)" : "rgba(143,63,41,0.4)", stroke: bad ? "#b3261e" : "var(--sp-accent)",
                    strokeWidth: r / (chosen ? 1.2 : 2.5), strokeDasharray: b.hasCover ? r + "," + r / 2 : "", cursor: "move", touchAction: "none"
                });
                shape.addEventListener("pointerdown", function (e) {
                    if (anyToolActive()) return;      // another tool is active: the click goes on to the picture
                    e.preventDefault();
                    e.stopPropagation();
                    view.suppressClick = true;
                    view.bedDrag = { id: pl.bedId, start: toSvgRaw(e), orig: [pl.x, pl.y], started: false };
                    view.moved = false;
                    if (view.activeBed !== pl.bedId) selectBed(pl.bedId);
                    svg.setPointerCapture(e.pointerId);
                });
                bedLayer.appendChild(shape);
                bedLayer.appendChild(label(b.name, pl.x, pl.y, r * 1.8));
            });
        }
        function setPlacingBed(id) {
            if (id) stopToolsExcept("bed");
            view.bedId = id;
            view.tool = id ? "bed" : releaseTool("bed");
            view.lastMouse = id ? view.lastMouse : null;
            svg.style.cursor = id ? "copy" : "";
            drawOverlay();
            fillBeds();
        }
        function placeBedAt(p) {
            var id = view.bedId;
            if (!id || !bedById(id) || placedBed(id) || !plotDefined()) { setPlacingBed(0); return; }
            model.placedBeds.push({ bedId: id, x: Math.min(width(), Math.max(0, p[0])), y: Math.min(height(), Math.max(0, p[1])), angle: 0 });
            view.activeBed = id;
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
            go.textContent = view.bedId ? T.bedCancel : T.bedPlace;
            msg.textContent = !ready ? (s === null ? T.noScale : T.bedNeedPlot) : view.bedId ? T.bedPlaceHint : beds.length === 0 ? T.bedNoBeds : !free ? T.bedAllPlaced : "";

            list.replaceChildren();
            var parts = regionParts(), polys = bedPolys(s), total = 0;
            model.placedBeds.forEach(function (pl) {
                var b = bedById(pl.bedId);
                if (!b) return;
                total += bedArea(b);
                var st = bedStatus(pl, parts, polys), bad = st.outside || st.overlaps.length > 0;
                var d = document.createElement("details");
                d.className = "border-bottom py-1";
                d.dataset.bed = String(pl.bedId);
                d.open = pl.bedId === view.activeBed;
                var head = document.createElement("summary");
                head.className = "small";
                head.textContent = b.name + " · " + fmt(b.lengthCm / 100, 2) + " × " + fmt(b.widthCm / 100, 2) + " m" + (bad ? " ⚠" : "");
                head.addEventListener("click", function () { view.activeBed = pl.bedId; drawBeds(); });
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
                    btn.addEventListener("click", function () { pl.angle = (((pl.angle + t[0]) % 360) + 360) % 360; view.activeBed = pl.bedId; drawBeds(); fillBeds(); });
                    row.appendChild(btn);
                });
                var del = document.createElement("button");
                del.type = "button"; del.className = "btn btn-outline-danger btn-sm"; del.textContent = T.bedRemove;
                del.addEventListener("click", function () {
                    model.placedBeds = model.placedBeds.filter(function (o) { return o !== pl; });
                    if (view.activeBed === pl.bedId) view.activeBed = 0;
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
            if (model.placedBeds.length && s !== null && model.boundary.length >= 3) {
                var avail = figures(model.boundary).available * s * s;
                sum.textContent = T.bedSum.replace("{0}", fmt(total, 1)).replace("{1}", fmt(avail, 1)).replace("{2}", avail > 0 ? fmt(total / avail * 100, 0) : "–");
            } else sum.textContent = "";
        }
        document.getElementById("bed-place").addEventListener("click", function () {
            if (view.bedId) { setPlacingBed(0); return; }
            var id = parseInt(document.getElementById("bed-choose").value, 10);
            if (!id || !plotDefined()) { fillBeds(); return; }
            setPlacingBed(id);
        });

        // only the user's own part: the rest of the picture is hidden (mask) and the view goes to that part
        function applyMine() {
            if (!view.mineOnly || model.boundary.length < 3) { picture.removeAttribute("mask"); return; }
            mineMask.setAttribute("width", width());
            mineMask.setAttribute("height", height());
            mineMask.replaceChildren();
            mineClip.replaceChildren();
            mineClip.appendChild(make("polygon", { points: ptsAttr(model.boundary) }));
            mineMask.appendChild(make("rect", { x: 0, y: 0, width: width(), height: height() }, { fill: "black" }));
            // the user's whole part stays visible (house, terrace and so on too); only what belongs to neighbours is hidden
            mineMask.appendChild(make("polygon", { points: ptsAttr(model.boundary) }, { fill: "white" }));
            neighborPoints().forEach(function (p) { mineMask.appendChild(make("polygon", { points: ptsAttr(p) }, { fill: "black" })); });
            picture.setAttribute("mask", "url(#mine-mask)");
        }
        function fitMine() {
            if (model.boundary.length < 3) return;
            // the box around the user's part: sample the bounding box of the plot, keep the points inside the plot and not in a neighbour's area
            var nb = neighborPoints();
            var x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity, bx0 = Infinity, by0 = Infinity, bx1 = -Infinity, by1 = -Infinity;
            model.boundary.forEach(function (p) { bx0 = Math.min(bx0, p[0]); by0 = Math.min(by0, p[1]); bx1 = Math.max(bx1, p[0]); by1 = Math.max(by1, p[1]); });
            for (var gx = 0; gx <= 60; gx++) {
                for (var gy = 0; gy <= 60; gy++) {
                    var sp = [bx0 + (bx1 - bx0) * gx / 60, by0 + (by1 - by0) * gy / 60];
                    if (!G.pointInPolygon(sp, model.boundary) || nb.some(function (q) { return G.pointInPolygon(sp, q); })) continue;
                    x0 = Math.min(x0, sp[0]); y0 = Math.min(y0, sp[1]); x1 = Math.max(x1, sp[0]); y1 = Math.max(y1, sp[1]);
                }
            }
            if (!(x1 > x0 && y1 > y0)) { fitZoom(); return; }
            var m = Math.max(x1 - x0, y1 - y0) * 0.04;
            x0 = Math.max(0, x0 - m); y0 = Math.max(0, y0 - m); x1 = Math.min(width(), x1 + m); y1 = Math.min(height(), y1 + m);
            var bw = x1 - x0, bh = y1 - y0, cw = wrap.clientWidth, ch = wrap.clientHeight - 2;
            setZoom(Math.max(0.1, Math.min(8, width() / bw, ch * width() / (cw * bh))));
            var k = svg.getBoundingClientRect().width / width();
            wrap.scrollLeft = x0 * k;
            wrap.scrollTop = y0 * k;
        }
        try { view.mineOnly = localStorage.getItem("garden-mine-only") === "1"; } catch (x) { view.mineOnly = false; }
    if (editMode) view.mineOnly = true;
        document.getElementById("mine-only").checked = view.mineOnly;
        document.getElementById("mine-only").addEventListener("change", function (e) {
            view.mineOnly = e.target.checked;
            if (!editMode) { try { localStorage.setItem("garden-mine-only", view.mineOnly ? "1" : "0"); } catch (x) { } }
            redraw();
            if (view.mineOnly) fitMine(); else fitZoom();
        });

        return { applyMine, bedPoly, drawBeds, fillBeds, fitMine, placeBedAt, placedBed, plotDefined, ptsAttr, setPlacingBed };
    }

    var api = { create: create };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.GardenBeds = api;
})(typeof self !== "undefined" ? self : this);
