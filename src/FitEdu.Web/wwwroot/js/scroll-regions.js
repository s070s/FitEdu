// Swipe rows (marked data-scroll-region) are a keyboard tab stop only while they really scroll.
// On phones such a row scrolls sideways, so a keyboard user must be able to focus it to scroll it
// with the arrow keys; on wider screens the row wraps into a grid and a tab stop there leads nowhere.
// Blazor adds and replaces these rows as pages render, so rows added later are watched too.
(function () {
    var regionSelector = '[data-scroll-region]';
    var watchedRegions = new WeakSet();
    var scanScheduled = false;

    function updateTabStop(region) {
        var scrollsSideways = region.scrollWidth > region.clientWidth;
        if (scrollsSideways) {
            region.setAttribute('tabindex', '0');
        } else {
            region.removeAttribute('tabindex');
        }
    }

    // Fires once when a row is first watched, then whenever its size changes (rotation, resize).
    var resizeObserver = new ResizeObserver(function (entries) {
        entries.forEach(function (entry) {
            updateTabStop(entry.target);
        });
    });

    function watchNewRegions() {
        scanScheduled = false;
        document.querySelectorAll(regionSelector).forEach(function (region) {
            if (watchedRegions.has(region)) {
                return;
            }

            watchedRegions.add(region);
            resizeObserver.observe(region);
        });
    }

    // Many page updates can arrive in one frame; one scan per frame is enough.
    function scheduleScan() {
        if (scanScheduled) {
            return;
        }

        scanScheduled = true;
        requestAnimationFrame(watchNewRegions);
    }

    watchNewRegions();
    new MutationObserver(scheduleScan).observe(document.body, { childList: true, subtree: true });
})();
