// PERF-6: read/clear an input's value on demand so message/assistant inputs don't have to
// two-way bind every keystroke through the SignalR circuit. The value is read only when the
// user actually sends (Enter or the Send button).
window.inputInterop = {
    read: function (el) {
        return el ? el.value : '';
    },
    clear: function (el) {
        if (el) {
            el.value = '';
            el.style.height = '';
        }
    },
    // Brings a message into the middle of the screen, clear of the sticky header and the phone tab
    // bar, and moves focus to it (the element needs tabindex="-1") so the next Tab starts there.
    // 'instant' because Bootstrap makes the page scroll smoothly, which a later scroll can overtake.
    scrollIntoView: function (el) {
        if (el) {
            el.focus({ preventScroll: true });
            el.scrollIntoView({ block: 'center', behavior: 'instant' });
        }
    }
};

// iOS Safari opens the on-screen keyboard only for a focus() made inside the tap itself, and
// Blazor's own focus call (SiteSearchBox.OnAfterRenderAsync) arrives a server round trip later.
// So the header search button opens its panel and focuses the field right here; the component
// then renders the same open state.
document.addEventListener('click', function (event) {
    var toggle = event.target.closest ? event.target.closest('.site-search-toggle') : null;
    if (!toggle) {
        return;
    }

    var searchBox = toggle.closest('.site-search');
    if (!searchBox || searchBox.classList.contains('site-search--expanded')) {
        return; // this tap closes the panel
    }

    searchBox.classList.add('site-search--expanded');
    var input = searchBox.querySelector('.site-search-input');
    if (input) {
        input.focus();
    }
});
