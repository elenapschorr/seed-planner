// The data of the plot that is edited on the garden page: the boundary with its edge lengths, the areas, the neighbours, the line through the plot
// and the placed beds. The page changes the fields and tells the views with notify() (Observer); snapshot() and restore() give the
// "undo" of the changes that replace the whole plot. No DOM: runs in the browser and under Node for the tests.
(function (root) {
    "use strict";

    var FALLBACK_KIND = "Occupied";

    function normalizeCutLine(line) {
        return line && line.a ? { a: line.a, b: line.b, owner1: line.owner1 | 0, owner2: line.owner2 | 0 } : null;
    }

    // Makes the values of an area from the server usable: known kind, whole numbers, no missing fields
    function normalizeZone(zone, kinds, neighbors) {
        if (!kinds[zone.kind]) zone.kind = FALLBACK_KIND;
        zone.isShape = !!zone.isShape;
        zone.owner = zone.owner > 0 ? zone.owner | 0 : 0;
        zone.splitGroup = zone.splitGroup | 0;
        zone.splitPart = zone.splitPart | 0;
        zone.splitParent = zone.splitParent || null;
        zone.splitLine = zone.splitLine || null;
        ["use", "sun", "soil", "slope"].forEach(function (field) { zone[field] = zone[field] || ""; });
        ["waterlogged", "waterNearby", "coldFramePossible"].forEach(function (field) { zone[field] = !!zone[field]; });
        if (zone.kind === "Excluded") {      // the first kind of "not mine": now a neighbour owns it
            zone.kind = "Mine";
            if (zone.owner === 0) zone.owner = 1;
        }
        while (neighbors.length < zone.owner) neighbors.push("");
    }

    // data: as the server sends it (boundary, lengths, zones, cutLine, neighbors); kinds: the known kinds of areas (object with the kind as key)
    function create(data, kinds) {
        var listeners = [];
        var model = {
            boundary: data.boundary || [],
            lengths: data.lengths || [],
            zones: data.zones || [],
            cutLine: normalizeCutLine(data.cutLine),
            neighbors: (data.neighbors || []).slice(),      // names of the neighbours (an area owned by neighbour k has owner k)
            placedBeds: []
        };
        model.zones.forEach(function (zone) { normalizeZone(zone, kinds, model.neighbors); });

        // Observer: the function is called whenever the model tells that it has changed
        model.subscribe = function (listener) {
            listeners.push(listener);
            return function () { listeners = listeners.filter(function (l) { return l !== listener; }); };
        };
        model.notify = function () { listeners.slice().forEach(function (listener) { listener(model); }); };

        // A copy of the plot (without the placed beds) that restore() puts back
        model.snapshot = function () {
            return JSON.parse(JSON.stringify({ boundary: model.boundary, lengths: model.lengths, zones: model.zones, neighbors: model.neighbors, cutLine: model.cutLine }));
        };
        model.restore = function (snapshot) {
            model.boundary = snapshot.boundary;
            model.lengths = snapshot.lengths;
            model.zones = snapshot.zones;
            model.neighbors = snapshot.neighbors;
            model.cutLine = snapshot.cutLine;      // the caller tells the views with notify() when the rest of the page is back as well
        };
        return model;
    }

    var api = { create: create, normalizeZone: normalizeZone };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.GardenModel = api;
})(typeof self !== "undefined" ? self : this);
