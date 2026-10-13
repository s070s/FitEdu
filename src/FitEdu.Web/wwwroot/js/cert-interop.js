window.downloadFile = (fileName, contentType, bytes) => {
    const blob = new Blob([new Uint8Array(bytes)], { type: contentType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};

// Visual-polish plan (WI-3/WI-7): scroll-reveal + tab-hidden animation pause. Loaded on every
// page (see App.razor), so it applies site-wide without a dedicated bundle. Blazor Server
// re-renders can replace .reveal/.stagger nodes, so this re-scans on every DOM mutation
// (debounced) rather than running once at load.
(function () {
    var revealObserver = null;

    function ensureObserver() {
        if (revealObserver) return revealObserver;
        // A share-of-the-element threshold never fires for a grid taller than the screen (a phone
        // page of 12 programme cards is about 6 screens), so reveal once the top edge is a little
        // way into the viewport instead.
        revealObserver = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    entry.target.classList.add('is-visible');
                    revealObserver.unobserve(entry.target);
                }
            });
        }, { threshold: 0, rootMargin: '0px 0px -10% 0px' });
        return revealObserver;
    }

    function scanForRevealTargets() {
        var targets = document.querySelectorAll('.reveal:not(.is-visible), .stagger:not(.is-visible)');
        if (targets.length === 0) return;
        document.documentElement.classList.add('reveal-ready');
        var observer = ensureObserver();
        targets.forEach(function (el) { observer.observe(el); });
    }

    var rescanTimer = null;
    function scheduleRescan() {
        clearTimeout(rescanTimer);
        rescanTimer = setTimeout(scanForRevealTargets, 150);
    }

    document.addEventListener('DOMContentLoaded', scanForRevealTargets);
    new MutationObserver(scheduleRescan).observe(document.body, { childList: true, subtree: true });

    document.addEventListener('visibilitychange', function () {
        document.documentElement.classList.toggle('tab-hidden', document.hidden);
    });
})();
