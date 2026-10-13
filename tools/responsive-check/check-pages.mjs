// Responsive sweep: opens every routable page as guest, student and admin, at phone and desktop
// size, in English and Greek, and reports layout, image, navigation and translation defects.
//
// Usage: node tools/responsive-check/check-pages.mjs [baseUrl] [outputFolder]
// Passwords come from FITEDU_STUDENT_PASSWORD and FITEDU_ADMIN_PASSWORD (never logged). A role
// whose password is missing is skipped. See README.md.
import { mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { launchBrowser } from '../assets/browser.mjs';
import { PAGES_BY_ROLE, PARAMETER_SOURCES, fillTemplate, findUncoveredRoutes, readPageRoutes } from './routes.mjs';
import { measure, scrollThroughPage, waitForRender } from './page-checks.mjs';
import { writeReports } from './report.mjs';

const DEFAULT_BASE_URL = 'http://localhost:5090';
const BASE_URL = (process.argv[2] || DEFAULT_BASE_URL).replace(/\/$/, '');
const OUTPUT_FOLDER = process.argv[3] || join(tmpdir(), 'fitedu-responsive-check');
const SCREENSHOT_FOLDER = join(OUTPUT_FOLDER, 'screenshots');
const PAGES_FOLDER = join(dirname(fileURLToPath(import.meta.url)), '../../src/FitEdu.Web/Pages');

const BOTTOM_NAV_BREAKPOINT = 768;
const MINIMUM_TAP_SIZE = 44;
const NAVIGATION_TIMEOUT_MS = 45_000;
const LOGIN_ATTEMPTS = 3;
const SCREENSHOT_QUALITY = 70;
// Optional regular expression: only page URLs that match it are visited (for quick re-runs).
const PAGE_FILTER = process.env.FITEDU_CHECK_PAGES ? new RegExp(process.env.FITEDU_CHECK_PAGES) : null;

const ROLES = [
    { name: 'guest' },
    { name: 'student', email: process.env.FITEDU_STUDENT_EMAIL || 'alice.johnson@fitedu.local', password: process.env.FITEDU_STUDENT_PASSWORD },
    { name: 'admin', email: process.env.FITEDU_ADMIN_EMAIL || 'admin@fitedu.local', password: process.env.FITEDU_ADMIN_PASSWORD },
];

const VIEWPORTS = [
    { name: '375', width: 375, height: 812, isMobile: true, hasTouch: true },
    { name: '1280', width: 1280, height: 800, isMobile: false, hasTouch: false },
];

const LANGUAGES = [
    { name: 'en', culture: 'en-GB' },
    { name: 'el', culture: 'el-GR' },
];

// Problems the browser reports that are not app defects, each with the reason it is safe.
const BENIGN_FAILED_REQUESTS = [
    { pattern: /net::ERR_ABORTED/, reason: 'moving to the next page cancels requests still in flight' },
];

/** Logs in through the real /login page and returns the signed-in cookies. */
async function signIn(browser, role) {
    const context = await browser.newContext();
    const page = await context.newPage();
    try {
        for (let attempt = 0; attempt < LOGIN_ATTEMPTS; attempt++) {
            await page.goto(`${BASE_URL}/login`, { timeout: NAVIGATION_TIMEOUT_MS });
            await waitForRender(page);
            await page.locator('#email').fill(role.email);
            await page.keyboard.press('Tab');
            await page.locator('#password').fill(role.password);
            await page.keyboard.press('Tab');
            await page.locator('button[type=submit]').click();
            const signedIn = await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 10_000 })
                .then(() => true, () => false);
            if (signedIn) {
                return await context.storageState();
            }
        }

        throw new Error(`login failed for ${role.name} (${role.email})`);
    } finally {
        await context.close();
    }
}

/** Fetches one API path with the role's cookies; returns null when the call fails. */
async function fetchJson(browser, storageState, apiPath) {
    const context = await browser.newContext({ storageState });
    try {
        const response = await context.request.get(BASE_URL + apiPath, { timeout: NAVIGATION_TIMEOUT_MS });
        return response.ok() ? await response.json() : null;
    } finally {
        await context.close();
    }
}

async function resolveParameters(browser, sessions) {
    const parameters = {};
    for (const source of PARAMETER_SOURCES) {
        const apiPath = fillTemplate(source.apiPath, parameters);
        if (!(source.role in sessions) || apiPath === null) {
            continue;
        }

        const json = await fetchJson(browser, sessions[source.role], apiPath);
        const value = (json && source.pick(json)) || source.fallback;
        if (value) {
            parameters[source.name] = String(value);
        }
    }

    return parameters;
}

/** Collects console errors and failed requests for the page currently being checked. */
function watchProblems(page) {
    const problems = { consoleErrors: [], failedRequests: [], ignored: [] };

    page.on('console', message => {
        if (message.type() === 'error') {
            problems.consoleErrors.push(message.text().slice(0, 300));
        }
    });
    page.on('pageerror', error => problems.consoleErrors.push(`page error: ${error.message.slice(0, 300)}`));
    page.on('requestfailed', request => {
        const failure = `${request.failure()?.errorText} ${request.url()}`;
        const benign = BENIGN_FAILED_REQUESTS.find(rule => rule.pattern.test(failure));
        if (benign) {
            problems.ignored.push(`${failure} (${benign.reason})`);
            return;
        }

        problems.failedRequests.push(failure);
    });
    page.on('response', response => {
        if (response.status() >= 400) {
            problems.failedRequests.push(`HTTP ${response.status()} ${response.url()}`);
        }
    });

    return problems;
}

function screenshotName(combination, url) {
    const pageName = url.replace(/^\//, '').replace(/[^a-z0-9]+/gi, '-').replace(/-$/, '') || 'home';
    return `${combination.role}-${combination.language}-${combination.viewport}-${pageName}.jpg`;
}

/** Turns the raw measurements into a list of defects. */
function findDefects(measurement, combination, url, problems) {
    const defects = [];
    const isMobile = Number(combination.viewport) < BOTTOM_NAV_BREAKPOINT;
    const expectedNotFound = url === '/not-found';

    if (measurement.path.startsWith('/login') && !url.startsWith('/login')) {
        defects.push({ type: 'redirected-to-login', detail: measurement.path });
    }
    if (measurement.scrollWidth > measurement.innerWidth) {
        defects.push({ type: 'horizontal-overflow', detail: `${measurement.scrollWidth} > ${measurement.innerWidth}: ${measurement.overflowingElements.join('; ')}` });
    }
    for (const image of measurement.brokenImages) {
        defects.push({ type: 'broken-image', detail: image });
    }
    if (isMobile && !measurement.bottomNavVisible) {
        defects.push({ type: 'bottom-nav-missing', detail: 'bottom nav hidden below 768 px' });
    }
    if (!isMobile && measurement.bottomNavVisible) {
        defects.push({ type: 'bottom-nav-on-desktop', detail: 'bottom nav visible at 768 px or wider' });
    }
    for (const target of measurement.smallTapTargets) {
        defects.push({ type: 'small-tap-target', detail: target });
    }
    for (const key of measurement.rawKeys) {
        defects.push({ type: 'raw-resource-key', detail: key });
    }
    if (measurement.errorPanelVisible) {
        defects.push({ type: 'blazor-error-panel', detail: 'the "An error has occurred" bar is showing' });
    }
    for (const error of problems.consoleErrors) {
        defects.push({ type: 'console-error', detail: error });
    }
    for (const failure of problems.failedRequests) {
        const isExpected404 = expectedNotFound && failure.startsWith('HTTP 404') && failure.endsWith('/not-found');
        if (!isExpected404) {
            defects.push({ type: 'failed-request', detail: failure });
        }
    }

    return defects;
}

/**
 * Checks one page in a fresh tab: a full-page screenshot resets the phone emulation of its tab
 * (touch and coarse pointer), so reusing one tab would measure later pages as a desktop.
 */
async function checkPage(context, combination, url) {
    const page = await context.newPage();
    const problems = watchProblems(page);
    try {
        await page.goto(BASE_URL + url, { waitUntil: 'load', timeout: NAVIGATION_TIMEOUT_MS });
        await waitForRender(page);
        await scrollThroughPage(page);
        const measurement = await measure(page, {
            isMobile: combination.viewport === '375',
            isGreek: combination.language === 'el',
            minimumTapSize: MINIMUM_TAP_SIZE,
        });
        // The first screen shows fixed bars where a phone shows them; the full page shows everything
        // (fixed bars then sit at the first screen's bottom edge, which is a capture artefact).
        const screenshot = screenshotName(combination, url);
        await page.screenshot({ path: join(SCREENSHOT_FOLDER, screenshot.replace('.jpg', '-top.jpg')), type: 'jpeg', quality: SCREENSHOT_QUALITY });
        await page.screenshot({ path: join(SCREENSHOT_FOLDER, screenshot), fullPage: true, type: 'jpeg', quality: SCREENSHOT_QUALITY });
        const defects = findDefects(measurement, combination, url, problems);
        return { ...combination, url, title: measurement.title, pointerCoarse: measurement.pointerCoarse, screenshot, defects, englishTexts: measurement.englishTexts, ignored: [...problems.ignored] };
    } catch (error) {
        return { ...combination, url, defects: [{ type: 'check-failed', detail: error.message.slice(0, 300) }], englishTexts: [], ignored: [] };
    } finally {
        await page.close();
    }
}

/** The culture endpoint stores the language in a cookie that every later tab of the context sends. */
async function setCulture(context, culture) {
    const page = await context.newPage();
    try {
        await page.goto(`${BASE_URL}/culture/set?culture=${culture}&returnUrl=/`, { timeout: NAVIGATION_TIMEOUT_MS });
    } finally {
        await page.close();
    }
}

async function sweepCombination(browser, storageState, combination, urls) {
    const viewport = VIEWPORTS.find(candidate => candidate.name === combination.viewport);
    const language = LANGUAGES.find(candidate => candidate.name === combination.language);
    const context = await browser.newContext({
        storageState,
        viewport: { width: viewport.width, height: viewport.height },
        isMobile: viewport.isMobile,
        hasTouch: viewport.hasTouch,
    });
    const results = [];
    try {
        await setCulture(context, language.culture);
        for (const url of urls) {
            const result = await checkPage(context, combination, url);
            console.log(`[${combination.role} ${combination.language} ${combination.viewport}] ${url} ${result.defects.length === 0 ? 'ok' : result.defects.length + ' defect(s)'}`);
            results.push(result);
        }
    } finally {
        await context.close();
    }

    return results;
}

async function sweepRole(browser, role, storageState, urls) {
    const results = [];
    for (const language of LANGUAGES) {
        for (const viewport of VIEWPORTS) {
            const combination = { role: role.name, language: language.name, viewport: viewport.name };
            results.push(...await sweepCombination(browser, storageState, combination, urls));
        }
    }

    return results;
}

async function signInRoles(browser) {
    const sessions = {};
    const skippedRoles = [];
    for (const role of ROLES) {
        if (role.name === 'guest') {
            sessions.guest = undefined;
            continue;
        }
        if (!role.password) {
            skippedRoles.push(`${role.name}: password environment variable not set`);
            continue;
        }

        sessions[role.name] = await signIn(browser, role);
    }

    return { sessions, skippedRoles };
}

/** Fills each role's page templates; templates with an unresolved parameter are reported, not visited. */
function buildPageLists(sessions, parameters) {
    const urlsByRole = {};
    const skippedPages = [];
    for (const roleName of Object.keys(sessions)) {
        urlsByRole[roleName] = [];
        for (const template of PAGES_BY_ROLE[roleName]) {
            const url = fillTemplate(template, parameters);
            if (url === null) {
                skippedPages.push(`${roleName} ${template}: the API returned no value for its parameter`);
                continue;
            }
            if (PAGE_FILTER && !PAGE_FILTER.test(url)) {
                continue;
            }

            urlsByRole[roleName].push(url);
        }
    }

    return { urlsByRole, skippedPages };
}

async function main() {
    mkdirSync(SCREENSHOT_FOLDER, { recursive: true });
    const uncoveredRoutes = findUncoveredRoutes(readPageRoutes(PAGES_FOLDER));
    const browser = await launchBrowser();
    try {
        const { sessions, skippedRoles } = await signInRoles(browser);
        const parameters = await resolveParameters(browser, sessions);
        const { urlsByRole, skippedPages } = buildPageLists(sessions, parameters);
        const roleSweeps = ROLES
            .filter(role => role.name in sessions)
            .map(role => sweepRole(browser, role, sessions[role.name], urlsByRole[role.name]));
        const results = (await Promise.all(roleSweeps)).flat();

        const summary = writeReports(OUTPUT_FOLDER, { baseUrl: BASE_URL, parameters, uncoveredRoutes, skippedRoles, skippedPages, results });
        console.log(`\n${summary.visits} visits, ${summary.defects} defect(s). Report: ${join(OUTPUT_FOLDER, 'report.md')}`);
        process.exitCode = summary.defects === 0 && uncoveredRoutes.length === 0 ? 0 : 1;
    } finally {
        await browser.close();
    }
}

await main();
