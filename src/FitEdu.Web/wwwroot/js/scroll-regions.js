// Swipe rows (marked data-scroll-region) are a keyboard tab stop only while they really scroll.
// On phones such a row scrolls sideways, so a keyboard user must be able to focus it to scroll it
// with the arrow keys; on wider screens the row wraps into a grid and a tab stop there leads nowhere.
// The markup starts each row as a tab stop (tabindex="0"), so it is reachable before this runs.
// Blazor adds and replaces these rows as pages render, so rows added later are watched too, and
// rows it removes are let go so the observer does not keep them alive.
(function () {
    var regionSelector = '[data-scroll-region]';
    var watchedRegions = new Set();
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

    // Rows that left the page are no longer watched; the rest are re-checked, because cards added
    // to or removed from a row change whether it scrolls without changing the row's own size.
    function refreshWatchedRegions() {
        watchedRegions.forEach(function (region) {
            if (!region.isConnected) {
                resizeObserver.unobserve(region);
                watchedRegions.delete(region);
                return;
            }

            updateTabStop(region);
        });
    }

    function watchNewRegions() {
        document.querySelectorAll(regionSelector).forEach(function (region) {
            if (watchedRegions.has(region)) {
                return;
            }

            watchedRegions.add(region);
            resizeObserver.observe(region);
        });
    }

    function scanPage() {
        scanScheduled = false;
        refreshWatchedRegions();
        watchNewRegions();
    }

    // Many page updates can arrive in one frame; one scan per frame is enough.
    function scheduleScan() {
        if (scanScheduled) {
            return;
        }

        scanScheduled = true;
        requestAnimationFrame(scanPage);
    }

    scanPage();
    new MutationObserver(scheduleScan).observe(document.body, { childList: true, subtree: true });
})();
