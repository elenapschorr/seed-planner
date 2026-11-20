// Undo and redo for the plot (Memento pattern): the history keeps copies of the state as text; commit() is called after every change of the user,
// undo() and redo() hand an earlier or later copy to apply(). No DOM: runs in the browser and under Node for the tests.
(function (root) {
    "use strict";

    var DEFAULT_LIMIT = 100;

    // take(): the current state as a plain object; apply(state): puts such a state back into the page
    function create(take, apply, limit) {
        var max = limit || DEFAULT_LIMIT;
        var current = JSON.stringify(take());
        var undoStack = [];
        var redoStack = [];

        function go(from, to) {
            if (from.length === 0) return false;
            to.push(current);
            current = from.pop();
            apply(JSON.parse(current));
            return true;
        }

        return {
            // Remembers the state if it differs from the last one; a new change ends the redo line
            commit: function () {
                var state = JSON.stringify(take());
                if (state === current) return false;
                undoStack.push(current);
                if (undoStack.length > max) undoStack.shift();
                current = state;
                redoStack.length = 0;
                return true;
            },
            undo: function () { return go(undoStack, redoStack); },
            redo: function () { return go(redoStack, undoStack); },
            canUndo: function () { return undoStack.length > 0; },
            canRedo: function () { return redoStack.length > 0; }
        };
    }

    var api = { create: create };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.GardenHistory = api;
})(typeof self !== "undefined" ? self : this);
