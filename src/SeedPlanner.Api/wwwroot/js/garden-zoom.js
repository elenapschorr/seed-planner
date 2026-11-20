// Zoom: the picture gets wider than the box and is scrolled; the point under the cursor stays in place.
(function (root) {
    "use strict";

    function create(app) {
        var G = app.G,
            T = app.T,
            data = app.data,
            model = app.model,
            view = app.view,
            svg = app.svg;

        // ---- zoom: the picture gets wider than the box and is scrolled (keeps the point under the cursor in place)
        var zoomLabel = document.getElementById("zoom-reset");      // the button that sets the zoom back to 100 % also shows the current zoom
        function showZoom() { zoomLabel.textContent = Math.round(view.zoom * 100) + " %"; }
        function setZoom(z, clientX, clientY) {
            z = Math.min(8, Math.max(0.1, z));
            if (z === view.zoom) return;
            var rect = app.wrap.getBoundingClientRect();
            var ax = (clientX === undefined ? rect.left + rect.width / 2 : clientX) - rect.left;
            var ay = (clientY === undefined ? rect.top + rect.height / 2 : clientY) - rect.top;
            var fx = (app.wrap.scrollLeft + ax) / app.wrap.scrollWidth;
            var fy = (app.wrap.scrollTop + ay) / app.wrap.scrollHeight;
            view.zoom = z;
            svg.style.width = (view.zoom * 100) + "%";
            showZoom();
            app.wrap.scrollLeft = fx * app.wrap.scrollWidth - ax;
            app.wrap.scrollTop = fy * app.wrap.scrollHeight - ay;
            app.redraw();
        }
        document.getElementById("zoom-in").addEventListener("click", function () { setZoom(view.zoom * 1.5); });
        document.getElementById("zoom-out").addEventListener("click", function () { setZoom(view.zoom / 1.5); });
        document.getElementById("zoom-reset").addEventListener("click", function () { setZoom(1); });
        // the whole picture in the window
        function fitZoom() {
            var f = Math.min(1, (app.wrap.clientHeight - 2) / (app.wrap.clientWidth * app.H / app.W));
            setZoom(f > 0 ? Math.max(0.1, f) : 1);
            app.wrap.scrollLeft = 0;
            app.wrap.scrollTop = 0;
        }
        document.getElementById("zoom-fit").addEventListener("click", fitZoom);
        app.wrap.addEventListener("wheel", function (e) {
            if (!e.ctrlKey && !e.metaKey) return;   // the wheel alone scrolls the page as usual
            e.preventDefault();
            setZoom(view.zoom * (e.deltaY < 0 ? 1.25 : 0.8), e.clientX, e.clientY);
        }, { passive: false });

        app.setZoom = setZoom;
        app.fitZoom = fitZoom;

    }

    var api = { create: create };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else root.GardenZoom = api;
})(typeof self !== "undefined" ? self : this);
