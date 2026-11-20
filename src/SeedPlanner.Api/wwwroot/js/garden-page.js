(function () {
    var dataEl = document.getElementById("garden-data");
    var svg = document.getElementById("plot");
    if (!dataEl || !svg || !window.GardenGeometry || !window.GardenModel || !window.GardenBeds || !window.GardenTraceTool || !window.GardenRender || !window.GardenShapes || !window.GardenCuts || !window.GardenNeighbors || !window.GardenZoom || !window.GardenTable || !window.GardenHistory || !window.GardenAdopt) return;
    var G = window.GardenGeometry;
    var app = {};                  // what the modules share with the page (see below)
    app.editMode = document.getElementById("stage").getAttribute("data-mode") === "edit";      // second page: only the own part, beds and areas; the plot itself is not changed here

    app.NS = "http://www.w3.org/2000/svg";
    var data = JSON.parse(dataEl.textContent);
    var T = JSON.parse(document.getElementById("garden-text").textContent);
    var model = window.GardenModel.create(data, T.kinds);      // boundary, lengths, zones, cutLine, neighbors, placedBeds
    model.subscribe(function () { redraw(); });         // whoever replaces the whole plot tells the views with model.notify()
    // The state of the view (what is selected, how far it is zoomed, what the mouse does) - not the data of the plot, that is in the model
    var view = {
        active: -1,        // -1 = the boundary, otherwise the index of an area
        selected: -1,        // the selected corner of the shape that is edited
        moved: false,
        lastMouse: null,
        suppressClick: false,        // the click that ends a press on a corner does not set a new corner
        zoom: 1,
        cutStart: null,
        drawing: false,        // the clicks add corners at the end
        inserting: false,        // the clicks insert a corner on the nearest edge
        mineOnly: false,        // only the user's own part is shown (the rest of the picture is hidden)
        activeBed: 0,        // the selected bed
        tool: null,        // the picture tool that takes the next click: "shape" (puts a ready-made shape), "cut" (the next two clicks set the line), "trace" (takes the area around the click), "bed" (puts a bed down) or null
        traceKind: null,        // with the tool "trace": "zone" or "boundary"
        bedId: 0,        // with the tool "bed": the bed that the next click puts down
        bedDrag: null        // a bed that is moved: { id, start, orig, started }
    };
    function isTool(name) { return view.tool === name; }
    function releaseTool(name) { return view.tool === name ? null : view.tool; }      // what the tool is when "name" lets go of it
    // What the modules share: the data, the state of the view and the functions and variables of the page that they use
    Object.assign(app, { G: G, T: T, data: data, model: model, view: view, svg: svg, editMode: app.editMode });
    // A function of a module that is created later than the one that needs it: looked up when it is called
    function late(name) { return function () { return app[name].apply(null, arguments); }; }
    // ---- page functions for the modules
    app.showScaleMode = showScaleMode;
    app.showTools = showTools;
    app.areaMode = areaMode;
    app.activeZone = activeZone;
    app.fmt = fmt;
    app.groupOf = groupOf;
    app.redraw = redraw;
    app.detach = detach;
    app.drawOverlay = drawOverlay;
    app.isTool = isTool;
    app.releaseTool = releaseTool;
    app.scaleNow = scaleNow;
    app.setInserting = setInserting;
    app.stopToolsExcept = stopToolsExcept;
    app.unit = unit;
    app.anyToolActive = anyToolActive;
    app.ownerLabel = ownerLabel;
    app.removeCorner = removeCorner;
    app.setDrawing = setDrawing;
    app.showSelection = showSelection;
    app.splitPoly = splitPoly;
    app.toSvgRaw = toSvgRaw;

    function ownerLabel(o) { return o === 0 ? T.ownerMe : (model.neighbors[o - 1] || T.neighborName.replace("{0}", String(o))); }
    app.W = 1000;               // size of the picture in pixels
    app.H = 1000;
    app.drag = null;            // a corner that is moved
    app.lastPress = null;       // the last press on a corner (for deleting by a double press)
    var cutDrag = null;         // an end of the straight line that is moved
    app.resizeDrag = null;      // a handle of a shape that is dragged
    app.moveDrag = null;        // an area that is moved as a whole
    app.tableEdges = 0;
    app.wrap = document.getElementById("plot-wrap");
    var lang = document.documentElement.lang || undefined;

    var picture = document.createElementNS(app.NS, "image");
    picture.setAttribute("href", svg.dataset.src);
    app.shapes = document.createElementNS(app.NS, "g");
    var overlay = document.createElementNS(app.NS, "g");
    overlay.style.pointerEvents = "none";
    var defs = document.createElementNS(app.NS, "defs");
    var mineClip = document.createElementNS(app.NS, "clipPath");
    mineClip.setAttribute("id", "mine-clip");
    var mineMask = document.createElementNS(app.NS, "mask");
    mineMask.setAttribute("id", "mine-mask");
    mineMask.setAttribute("maskUnits", "userSpaceOnUse");
    mineMask.setAttribute("x", "0");
    mineMask.setAttribute("y", "0");
    defs.appendChild(mineClip);
    defs.appendChild(mineMask);
    var bedLayer = document.createElementNS(app.NS, "g");
    svg.appendChild(defs);
    svg.appendChild(picture);
    svg.appendChild(app.shapes);
    svg.appendChild(bedLayer);
    svg.appendChild(overlay);

    var probe = new Image();
    probe.onload = function () {
        app.W = probe.naturalWidth;
        app.H = probe.naturalHeight;
        svg.setAttribute("viewBox", "0 0 " + app.W + " " + app.H);
        picture.setAttribute("width", app.W);
        picture.setAttribute("height", app.H);
        redraw();
        if (view.mineOnly) app.fitMine(); else app.fitZoom();
    };
    probe.src = svg.dataset.src;

    function fmt(v, digits) {
        return v.toLocaleString(lang, { minimumFractionDigits: digits, maximumFractionDigits: digits });
    }
    // The scale (meters per pixel): from the edge lengths the user entered, or from the area of the whole plot
    app.scaleMode = document.getElementById("scale-mode");
    app.plotArea = document.getElementById("plot-area");
    function areaMode() { return app.scaleMode.value === "area"; }
    // the scale from the edge lengths that were entered, or from the area of the plot; the other one is calculated from it and cannot be changed
    function edgesScale() { return model.boundary.length < 3 ? null : G.metersPerPixel(model.boundary, model.lengths); }
    function areaScale() {
        var wanted = parseFloat(app.plotArea.value), pixels = G.area(model.boundary);
        return model.boundary.length >= 3 && wanted > 0 && pixels > 0 ? Math.sqrt(wanted / pixels) : null;
    }
    function scaleNow() { return areaMode() ? areaScale() : edgesScale(); }
    function showScaleMode() {
        app.plotArea.readOnly = !areaMode();      // area given: it is entered here and the edges are calculated; edges given: the area is calculated
        document.getElementById("scale-note").textContent = areaMode() ? T.scaleAreaNote : T.scaleEdgesNote;
        app.tableEdges = -1;      // the length fields are built (or hidden) again
        app.update();
    }
    // fills in the length of every edge from the area (the proportions are those of the drawing) and goes back to single lengths
    // takes over the calculated values and changes to the other kind of input: from the edges the area is calculated, from the area the edges
    function changeMode(to) {
        if (to === "area") {
            var s = edgesScale();
            if (s !== null) app.plotArea.value = (G.area(model.boundary) * s * s).toFixed(2);
        } else {
            var a = areaScale();
            if (a !== null) for (var i = 0; i < model.boundary.length; i++) model.lengths[i] = Math.round(G.edgeLen(model.boundary, i) * a * 100) / 100;
        }
        app.scaleMode.value = to;
        showScaleMode();
        drawOverlay();
    }
    document.getElementById("calc-edges").addEventListener("click", function () { changeMode(areaMode() ? "edges" : "area"); });
    if (data.knownArea > 0) {
        app.scaleMode.value = "area";
        app.plotArea.value = data.knownArea;
    }
    app.scaleMode.addEventListener("change", function () { changeMode(app.scaleMode.value); });
    app.plotArea.addEventListener("input", function () { app.update(); drawOverlay(); });

    // the tools lie over the picture and fold away
    var toolsBody = document.getElementById("tools-body");
    var toolsToggle = document.getElementById("tools-toggle");
    function showTools(on) {
        toolsBody.hidden = !on;
        toolsToggle.setAttribute("aria-expanded", on ? "true" : "false");
    }
    toolsToggle.addEventListener("click", function () { showTools(toolsBody.hidden); });
    showTools(true);
    function activeZone() { return view.active >= 0 ? model.zones[view.active] : null; }
    function unit() { return Math.max(app.W, app.H) / 120 / view.zoom; }   // size of a corner marker; constant on the screen when zoomed

    window.GardenRender.create(app);

    window.GardenShapes.create(app);

    // the two ends of the straight line through the plot can be moved
    function drawCutHandles(r) {
        if (!model.cutLine || isTool("cut")) return;
        ["a", "b"].forEach(function (end) {
            var h = app.make("circle", { cx: model.cutLine[end][0], cy: model.cutLine[end][1], r: r * 1.2 }, {
                fill: "var(--sp-paper)", stroke: "var(--sp-accent)", strokeWidth: r / 3, cursor: "move", touchAction: "none"
            });
            h.addEventListener("pointerdown", function (e) {
                e.preventDefault();
                e.stopPropagation();
                cutDrag = end;
                view.moved = false;
                view.suppressClick = true;
                svg.setPointerCapture(e.pointerId);
            });
            app.shapes.appendChild(h);
        });
    }

    function redraw() {
        app.shapes.replaceChildren();
        var r = unit() * 0.5;       // corners, numbers and lines of the areas: smaller than the unit that is used for the hit areas
        model.zones.forEach(function (z, zi) { if (!(view.mineOnly && z.owner > 0)) app.drawShape(z.points, true, zi === view.active, r, z); });
        app.drawShape(model.boundary, false, view.active === -1, r, null);
        drawCutHandles(r);
        app.fillRefs();
        app.update();
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
        return [Math.min(app.W, Math.max(0, p[0])), Math.min(app.H, Math.max(0, p[1]))];
    }

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
        model.zones.forEach(function (o) {
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

    // ---- the tool "take an area from the lines in the picture" (garden-trace-tool.js)
    var traceTool = window.GardenTraceTool.create({
        T: T, data: data, model: model, view: view, svg: svg, probe: probe, width: function () { return app.W; }, height: function () { return app.H; }, drawOverlay: drawOverlay, fillTarget: late("fillTarget"), redraw: redraw, releaseTool: releaseTool, showScaleMode: showScaleMode, showSelection: showSelection, stopToolsExcept: stopToolsExcept, uniqueName: late("uniqueName"), detach: detach
    });
    var setTracing = traceTool.setTracing;
    var traceAt = traceTool.traceAt;

    window.GardenCuts.create(app);

    // The shape that follows the mouse while it is placed, and the line that would split the area
    function drawOverlay() {
        overlay.replaceChildren();
        var r = unit();
        if (isTool("shape") && view.lastMouse) {
            var pts = app.makeShape(view.lastMouse);
            if (pts) overlay.appendChild(app.make("polygon", { points: pts.map(function (p) { return p[0] + "," + p[1]; }).join(" ") }, {
                fill: "rgba(143,63,41,0.15)", stroke: "var(--sp-accent)", strokeWidth: r / 2.5, strokeDasharray: r + "," + r / 2
            }));
        }
        if (view.bedId && view.lastMouse) {
            var ghost = bedPoly({ bedId: view.bedId, x: view.lastMouse[0], y: view.lastMouse[1], angle: 0 }, scaleNow());
            if (ghost) overlay.appendChild(app.make("polygon", { points: ptsAttr(ghost) }, {
                fill: "rgba(143,63,41,0.15)", stroke: "var(--sp-accent)", strokeWidth: r / 2.5, strokeDasharray: r + "," + r / 2
            }));
        }
        var far = Math.max(app.W, app.H);
        if (isTool("cut") && view.cutStart && view.lastMouse)
            overlay.appendChild(app.make("line", { x1: view.cutStart[0], y1: view.cutStart[1], x2: view.lastMouse[0], y2: view.lastMouse[1] }, {
                stroke: "var(--sp-accent)", strokeWidth: r / 2, strokeDasharray: r * 2 + "," + r
            }));
        var cp = app.cutParts(), cs = scaleNow();
        if (cp) {
            overlay.appendChild(app.make("line", { x1: model.cutLine.a[0] - cp.dx * far, y1: model.cutLine.a[1] - cp.dy * far, x2: model.cutLine.a[0] + cp.dx * far, y2: model.cutLine.a[1] + cp.dy * far }, {
                stroke: "var(--sp-accent)", strokeWidth: r / 2
            }));
            cp.pieces.forEach(function (piece, i) {
                if (!piece) return;
                var mx = piece.reduce(function (s, p) { return s + p[0]; }, 0) / piece.length;
                var my = piece.reduce(function (s, p) { return s + p[1]; }, 0) / piece.length;
                var owner = i === 0 ? model.cutLine.owner1 : model.cutLine.owner2;
                var text = String(i + 1) + (cs ? ": " + fmt(G.area(piece) * cs * cs, 1) + " m\u00b2" : "") + " \u00b7 " + ownerLabel(owner);
                overlay.appendChild(app.label(text, mx, my, r * 2));
            });
        } else if (model.cutLine) {       // no outline yet (fewer than 3 corners): the line is only the stretch between the two points, the parts cannot be calculated
            overlay.appendChild(app.make("line", { x1: model.cutLine.a[0], y1: model.cutLine.a[1], x2: model.cutLine.b[0], y2: model.cutLine.b[1] }, {
                stroke: "var(--sp-accent)", strokeWidth: r / 2
            }));
        }
        if (view.cutStart) overlay.appendChild(app.make("circle", { cx: view.cutStart[0], cy: view.cutStart[1], r: r }, { fill: "var(--sp-accent)" }));
        var line = app.splitPanel.open ? app.splitLine() : null;
        if (line) {
            app.syncSplitFields(line);
            var z = activeZone();
            var mx = z.points.reduce(function (s, p) { return s + p[0]; }, 0) / z.points.length;
            var my = z.points.reduce(function (s, p) { return s + p[1]; }, 0) / z.points.length;
            var shift = line.t0 - (mx * line.nx + my * line.ny);
            var cx = mx + line.nx * shift, cy = my + line.ny * shift, far = Math.max(app.W, app.H);
            overlay.appendChild(app.make("line", { x1: cx - line.dx * far, y1: cy - line.dy * far, x2: cx + line.dx * far, y2: cy + line.dy * far }, {
                stroke: "var(--sp-accent)", strokeWidth: r / 2, strokeDasharray: r * 2 + "," + r
            }));
            overlay.appendChild(app.label("1", cx - line.nx * r * 3, cy - line.ny * r * 3, r * 2.2));
            overlay.appendChild(app.label("2", cx + line.nx * r * 3, cy + line.ny * r * 3, r * 2.2));
        }
    }

    // ---- changing the shapes (the edge lengths follow the corners of the boundary)
    // A shape that is drawn corner by corner (new, or emptied) takes the clicks as its next corners; a closed shape does not:
    // a click beside a corner must not make a new one. A corner is inserted with the button "Insert corner" (on the nearest edge).
    function setDrawing(on) {
        view.drawing = on;
        if (!view.inserting) cornerMessage(on ? T.drawingHint : "");
    }
    function cornerMessage(text) {
        var box = document.getElementById("corner-msg");
        box.hidden = !text;
        box.textContent = text || "";
    }
    function setInserting(on) {
        view.inserting = on;
        document.getElementById("insert-corner").classList.toggle("active", on);
        cornerMessage(on ? T.insertHint : "");
    }
    function addCorner(p) {
        var pts = view.active < 0 ? model.boundary : model.zones[view.active].points;
        if (view.active >= 0 && model.zones[view.active].isShape) return;
        if (pts.length < 3 || view.drawing) {
            if (view.active < 0) {
                if (model.boundary.length >= 3) model.lengths[model.boundary.length - 1] = null; // the closing edge is replaced
                model.boundary.push(p);
                model.lengths.push(null);
            } else {
                detach(model.zones[view.active]);
                model.zones[view.active].points.push(p);
            }
        } else if (view.inserting) {
            var at = 0, best = Infinity;
            for (var i = 0; i < pts.length; i++) {
                var d = G.distanceToSegment(p, pts[i], pts[(i + 1) % pts.length]);
                if (d < best) { best = d; at = i; }
            }
            if (view.active < 0) { model.lengths[at] = null; model.lengths.splice(at + 1, 0, null); } else detach(model.zones[view.active]);
            pts.splice(at + 1, 0, p);
        } else {
            cornerMessage(T.cornerClosed);
            return;
        }
        redraw();
    }
    document.getElementById("insert-corner").addEventListener("click", function () { setInserting(!view.inserting); });
    function showSelection() {
        var button = document.getElementById("delete-point");
        button.disabled = view.selected < 0;
    }
    function removeCorner(i) {
        view.selected = -1;
        showSelection();
        var pts = view.active < 0 ? model.boundary : model.zones[view.active].points;
        if (view.active >= 0) detach(model.zones[view.active]);
        pts.splice(i, 1);
        if (view.active < 0) {
            model.lengths.splice(i, 1);
            if (model.boundary.length >= 3) model.lengths[(i - 1 + model.boundary.length) % model.boundary.length] = null;
        }
        redraw();
    }

    svg.addEventListener("pointerdown", function () { view.suppressClick = false; });
    // ---- tool switch: only one picture tool (shape, cut line, trace, bed) is active at a time
    var toolStoppers = {
        shape: function () { if (isTool("shape")) app.setPlacing(false); },
        cut: function () { if (isTool("cut")) app.setCutDrawing(false); },
        trace: function () { if (view.traceKind) setTracing(null); },
        bed: function () { if (view.bedId) setPlacingBed(0); }
    };
    function stopToolsExcept(keep) {
        Object.keys(toolStoppers).forEach(function (name) { if (name !== keep) toolStoppers[name](); });
    }
    function anyToolActive() {
        return view.tool !== null;
    }

    // ---- click tools: the first active tool takes the click on the picture (State pattern); the corner tool is the default
    var clickTools = [
        {
            isActive: function () { return isTool("bed"); },
            onClick: function (e) { placeBedAt(toSvgRaw(e)); },
            onMove: function (e) { view.lastMouse = toSvgRaw(e); drawOverlay(); }
        },
        {
            isActive: function () { return isTool("trace"); },
            onClick: function (e) { traceAt(toSvgRaw(e)); }
        },
        {
            isActive: function () { return isTool("cut"); },
            onClick: function (e) { clickCutLine(toSvg(e)); },
            onMove: function (e) { view.lastMouse = toSvgRaw(e); drawOverlay(); }
        },
        {
            isActive: function () { return isTool("shape"); },
            onClick: function (e) { clickPlaceShape(toSvgRaw(e)); },
            onMove: function (e) { view.lastMouse = toSvgRaw(e); drawOverlay(); }
        },
        {
            isActive: function () { return true; },
            onClick: function (e) {
                if (view.mineOnly && view.active < 0) return;    // in the view of the own part a click on empty ground does not change the boundary (the corners of an area still can be set)
                addCorner(toSvg(e));
            }
        }
    ];
    function activeClickTool() {
        return clickTools.find(function (tool) { return tool.isActive(); });
    }
    function clickCutLine(q) {
        if (!view.cutStart) view.cutStart = q;
        else if (Math.hypot(q[0] - view.cutStart[0], q[1] - view.cutStart[1]) > unit() * 0.5) {
            model.cutLine = { a: view.cutStart, b: q, owner1: parseInt(document.getElementById("cut-owner1").value, 10) || 0, owner2: parseInt(document.getElementById("cut-owner2").value, 10) || 0 };
            app.setCutDrawing(false);
            return;
        }
        drawOverlay();
    }
    function clickPlaceShape(p) {
        var points = app.makeShape(p);
        if (!points) return;
        var input = document.getElementById("zone-name");
        var kind = document.getElementById("zone-kind").value;
        model.zones.push({
            name: input.value.trim() || app.uniqueName(T.kinds[kind]),
            kind: kind, owner: parseInt(document.getElementById("zone-owner").value, 10) || 0, isShape: true, points: points
        });
        input.value = "";
        view.active = model.zones.length - 1;
        app.setPlacing(false);
        app.fillTarget();
        redraw();
    }
    svg.addEventListener("click", function (e) {
        if (view.suppressClick) { view.suppressClick = false; view.moved = false; return; }
        if (view.moved) { view.moved = false; return; }
        activeClickTool().onClick(e);
    });
    svg.addEventListener("pointermove", function (e) {
        if (app.drag) {
            if (view.active >= 0) detach(model.zones[view.active]);
            (view.active < 0 ? model.boundary : model.zones[view.active].points)[app.drag.index] = toSvg(e);
            view.moved = true;
            redraw();
        } else if (view.bedDrag) {
            var bp = toSvgRaw(e), bed = placedBed(view.bedDrag.id);
            if (bed && (view.bedDrag.started || Math.hypot(bp[0] - view.bedDrag.start[0], bp[1] - view.bedDrag.start[1]) > unit() * 0.6)) {
                view.bedDrag.started = true;
                view.moved = true;
                bed.x = Math.min(app.W, Math.max(0, Math.round((view.bedDrag.orig[0] + bp[0] - view.bedDrag.start[0]) * 10) / 10));
                bed.y = Math.min(app.H, Math.max(0, Math.round((view.bedDrag.orig[1] + bp[1] - view.bedDrag.start[1]) * 10) / 10));
                drawBeds();
            }
        } else if (cutDrag) {
            model.cutLine[cutDrag] = toSvg(e);
            view.moved = true;
            redraw();
        } else if (app.resizeDrag) {
            app.resizeTo(toSvgRaw(e));
            view.moved = true;
            redraw();
        } else if (app.moveDrag) {
            var p = toSvgRaw(e), dx = p[0] - app.moveDrag.start[0], dy = p[1] - app.moveDrag.start[1];
            if (!app.moveDrag.started && Math.hypot(dx, dy) > unit() * 0.6) app.moveDrag.started = true;
            if (app.moveDrag.started) {
                view.moved = true;
                detach(model.zones[view.active]);
                model.zones[view.active].points = app.moveDrag.orig.map(function (q) { return [Math.round((q[0] + dx) * 10) / 10, Math.round((q[1] + dy) * 10) / 10]; });
                redraw();
            }
        } else {
            var tool = activeClickTool();
            if (tool.onMove) tool.onMove(e);
        }
    });
    function endDrag() {
        var movedBed = view.bedDrag && view.bedDrag.started;
        app.drag = null; app.moveDrag = null; app.resizeDrag = null; cutDrag = null; view.bedDrag = null;
        if (movedBed) fillBeds();      // the remarks of the bed list follow the new place
    }
    svg.addEventListener("pointerup", endDrag);
    svg.addEventListener("pointercancel", endDrag);

    // ---- controls
    app.target = document.getElementById("target");
    app.deleteZone = document.getElementById("delete-zone");
    window.GardenNeighbors.create(app);

    window.GardenZoom.create(app);

    window.GardenTable.create(app);

    // ---- beds and the view of the own part (garden-beds.js)
    var bedsView = window.GardenBeds.create({
        G: G, T: T, data: data, model: model, view: view, svg: svg, editMode: app.editMode, BUILT: app.BUILT, bedLayer: bedLayer, mineClip: mineClip, mineMask: mineMask, picture: picture, wrap: app.wrap,
        width: function () { return app.W; }, height: function () { return app.H; },
        anyToolActive: anyToolActive, drawOverlay: drawOverlay, figures: late("figures"), fitZoom: late("fitZoom"), fmt: fmt, kindPoints: late("kindPoints"), label: late("label"), make: late("make"), neighborPoints: late("neighborPoints"), propSummary: late("propSummary"), redraw: redraw, releaseTool: releaseTool, scaleNow: scaleNow, setZoom: late("setZoom"), stopToolsExcept: stopToolsExcept, toSvgRaw: toSvgRaw, unit: unit, zoneArea: late("zoneArea")
    });
    var applyMine = bedsView.applyMine;
    var bedPoly = bedsView.bedPoly;
    var drawBeds = bedsView.drawBeds;
    var fillBeds = bedsView.fillBeds;
    app.fitMine = bedsView.fitMine;
    var placeBedAt = bedsView.placeBedAt;
    var placedBed = bedsView.placedBed;
    app.plotDefined = bedsView.plotDefined;
    var ptsAttr = bedsView.ptsAttr;
    var setPlacingBed = bedsView.setPlacingBed;

    window.GardenAdopt.create(app);

    // Ctrl+Z / Ctrl+Y: every finished action of the user (press, change, key) is one step; text fields keep their own undo
    var history = window.GardenHistory.create(function () { return model.snapshot(); }, function (state) {
        model.restore(state);
        app.showRestoredPlot();
    });
    ["mouseup", "change", "keyup"].forEach(function (type) {
        document.addEventListener(type, function () { setTimeout(function () { history.commit(); }, 0); });
    });
    document.addEventListener("keydown", function (e) {
        if (!(e.ctrlKey || e.metaKey) || e.altKey) return;
        var key = e.key.toLowerCase();
        var inField = /^(input|textarea|select)$/i.test(e.target.tagName || "");
        if (inField || (key !== "z" && key !== "y")) return;
        e.preventDefault();
        if (key === "y" || e.shiftKey) history.redo(); else history.undo();
    });
    document.getElementById("adopt-zone").addEventListener("click", function () { if (view.active >= 0) app.adoptAsPlot(view.active); });
    document.getElementById("add-corner").addEventListener("click", function () {
        var z = activeZone();
        if (!z || z.points.length < 2) return;
        var a = z.points[z.points.length - 1], b = z.points[0];
        detach(z);
        z.isShape = false;
        z.points.push([Math.round((a[0] + b[0]) / 2 * 10) / 10, Math.round((a[1] + b[1]) / 2 * 10) / 10]);    // on the closing edge: the shape does not change until the corner is moved
        app.fillTarget();
        redraw();
    });
    app.showShapeType();
    showScaleMode();
    app.buildNeighbors();
    if (model.cutLine) {
        document.getElementById("cut-owner1").value = String(model.cutLine.owner1);
        document.getElementById("cut-owner2").value = String(model.cutLine.owner2);
    }
    // who owns the parts of the straight line counts at once (no need to create areas from them)
    [1, 2].forEach(function (n) {
        document.getElementById("cut-owner" + n).addEventListener("change", function (e) {
            if (!model.cutLine) return;
            model.cutLine["owner" + n] = parseInt(e.target.value, 10) || 0;
            app.update();
            drawOverlay();
        });
    });
    app.fillTarget();
    redraw();
})();
