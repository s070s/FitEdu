// In-page checks for one rendered page: overflow, images, bottom nav, tap targets and text.

const RENDER_TIMEOUT_MS = 20_000;
const IMAGE_TIMEOUT_MS = 10_000;
const SETTLE_DELAY_MS = 800;
const SCROLL_STEP_DELAY_MS = 120;
// Above this many scroll steps the page is very long (the exercise library); stop there.
const MAX_SCROLL_STEPS = 40;

/** Waits until the Blazor circuit is up and no loading pulse is on screen. */
export async function waitForRender(page) {
    await page.waitForFunction(() => window.Blazor !== undefined, null, { timeout: RENDER_TIMEOUT_MS }).catch(() => { });
    await page.waitForFunction(() => {
        const loaders = [...document.querySelectorAll('.pulse-loader')];
        return loaders.every(loader => !loader.checkVisibility());
    }, null, { timeout: RENDER_TIMEOUT_MS, polling: 200 }).catch(() => { });
    await page.waitForTimeout(SETTLE_DELAY_MS);
}

/** Scrolls to the bottom in screen-sized steps so lazy images start loading, then back to the top. */
export async function scrollThroughPage(page) {
    for (let step = 0; step < MAX_SCROLL_STEPS; step++) {
        const reachedBottom = await page.evaluate(() => {
            window.scrollBy(0, window.innerHeight);
            return window.scrollY + window.innerHeight >= document.documentElement.scrollHeight - 1;
        });
        await page.waitForTimeout(SCROLL_STEP_DELAY_MS);
        if (reachedBottom) {
            break;
        }
    }

    await page.waitForFunction(() => [...document.images]
        .filter(image => image.checkVisibility())
        .every(image => image.complete), null, { timeout: IMAGE_TIMEOUT_MS }).catch(() => { });
    await page.evaluate(() => window.scrollTo(0, 0));
}

/**
 * Runs inside the page. Returns raw measurements; the caller decides what counts as a defect.
 * Kept self-contained because page.evaluate serializes it into the browser.
 */
function measurePage({ isMobile, isGreek, minimumTapSize }) {
    const GREEK_LETTER = /[Ͱ-Ͽἀ-῿]/;
    const RESOURCE_KEY = /^[A-Z][a-z]+(?:[A-Z][a-z0-9]+)+$/;
    const ALLOWED_CAMEL_CASE = new Set(['FitEdu', 'YouTube', 'LinkedIn', 'PayPal', 'TikTok', 'WhatsApp', 'GitHub', 'OpenStreetMap']);
    const SKIPPED_TEXT_PARENTS = 'script, style, code, pre, noscript, textarea, select, option, [translate="no"], .site-brand';
    const TAP_TARGET_SELECTOR = '.bottom-nav__link, .site-links a, .site-header button, .btn-primary, .btn-cta, main button[type=submit]';
    const MAXIMUM_TEXT_FINDINGS = 12;

    function describe(element) {
        const classes = [...element.classList].slice(0, 3).join('.');
        const text = (element.innerText || element.getAttribute('aria-label') || '').trim().slice(0, 40);
        return `${element.tagName.toLowerCase()}${classes ? '.' + classes : ''} "${text}"`;
    }

    function hasScrollingAncestor(element) {
        for (let parent = element.parentElement; parent && parent !== document.body; parent = parent.parentElement) {
            const overflowX = getComputedStyle(parent).overflowX;
            if (overflowX === 'auto' || overflowX === 'scroll' || overflowX === 'hidden' || overflowX === 'clip') {
                return true;
            }
        }

        return false;
    }

    function findOverflowingElements() {
        const viewportWidth = window.innerWidth;
        return [...document.body.querySelectorAll('*')]
            .filter(element => element.getBoundingClientRect().right > viewportWidth + 1)
            .filter(element => element.checkVisibility() && !hasScrollingAncestor(element))
            .slice(0, 5)
            .map(element => `${describe(element)} right=${Math.round(element.getBoundingClientRect().right)}`);
    }

    function findBrokenImages() {
        return [...document.images]
            .filter(image => image.checkVisibility())
            .filter(image => !image.complete || image.naturalWidth === 0)
            .map(image => image.currentSrc || image.src);
    }

    function isBottomNavVisible() {
        const bottomNav = document.querySelector('.bottom-nav');
        if (!bottomNav || !bottomNav.checkVisibility()) {
            return false;
        }

        return bottomNav.getBoundingClientRect().height > 0;
    }

    function findSmallTapTargets() {
        if (!isMobile) {
            return [];
        }

        return [...document.querySelectorAll(TAP_TARGET_SELECTOR)]
            .filter(element => element.checkVisibility())
            .filter(element => {
                const box = element.getBoundingClientRect();
                return box.width > 0 && (box.height < minimumTapSize - 0.5 || box.width < minimumTapSize - 0.5);
            })
            .slice(0, 8)
            .map(element => {
                const box = element.getBoundingClientRect();
                return `${describe(element)} ${Math.round(box.width)}x${Math.round(box.height)}`;
            });
    }

    function visibleTextNodes() {
        const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
        const texts = [];
        for (let node = walker.nextNode(); node; node = walker.nextNode()) {
            const parent = node.parentElement;
            const text = node.textContent.trim();
            if (!text || !parent || parent.closest(SKIPPED_TEXT_PARENTS) || !parent.checkVisibility()) {
                continue;
            }

            texts.push(text);
        }

        return texts;
    }

    function looksEnglish(text) {
        if (GREEK_LETTER.test(text) || /@|https?:|www\./.test(text)) {
            return false;
        }

        const englishWords = text.match(/\b[A-Za-z]{2,}\b/g) ?? [];
        return englishWords.length >= 3;
    }

    const texts = visibleTextNodes();
    const rawKeys = texts.filter(text => RESOURCE_KEY.test(text) && !ALLOWED_CAMEL_CASE.has(text));
    const englishTexts = isGreek ? texts.filter(looksEnglish).slice(0, MAXIMUM_TEXT_FINDINGS) : [];
    const errorPanel = document.querySelector('#blazor-error-ui');

    return {
        path: location.pathname + location.search,
        title: document.title,
        pointerCoarse: matchMedia('(pointer: coarse)').matches,
        scrollWidth: document.documentElement.scrollWidth,
        innerWidth: window.innerWidth,
        overflowingElements: findOverflowingElements(),
        brokenImages: findBrokenImages(),
        imageCount: document.images.length,
        bottomNavVisible: isBottomNavVisible(),
        smallTapTargets: findSmallTapTargets(),
        rawKeys: [...new Set(rawKeys)].slice(0, MAXIMUM_TEXT_FINDINGS),
        englishTexts: [...new Set(englishTexts)],
        errorPanelVisible: Boolean(errorPanel && errorPanel.checkVisibility()),
    };
}

export function measure(page, options) {
    return page.evaluate(measurePage, options);
}
