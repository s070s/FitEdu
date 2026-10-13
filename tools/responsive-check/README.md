# Responsive check

A sweep over every page of a running FitEdu app. Each page is opened as a **guest**, a **student**
and an **admin** (where that role may open it), at **375×812** (phone, touch) and **1280×800**
(desktop), in **English** and **Greek**. For every visit it checks:

- no sideways scrolling (`document.documentElement.scrollWidth <= innerWidth`), naming the
  elements that stick out;
- every visible image loaded, after scrolling to the bottom so lazy images load too;
- no console errors, failed requests or HTTP errors, and no Blazor error bar;
- the phone tab bar (`.bottom-nav`) shows below 768 px and is hidden from 768 px up;
- navigation links and primary buttons are at least 44×44 px on the phone;
- no raw resource keys (such as `HeroTitleLine1`) on screen;
- on Greek pages, English-looking text is listed for a person to review (content such as blog tags
  is English by choice, so these are not counted as defects).

The page list is in `routes.mjs`. The script also reads every `@page` route in
`src/FitEdu.Web/Pages` and fails when a route is in no role's list, so a new page cannot be missed.
Route ids (a course, a workout lesson, an exercise, a path, an assessment, a blog article, a
certificate) are read from the app's JSON API with each role's sign-in cookie.

| File | What it does |
|---|---|
| `check-pages.mjs` | Signs in, finds the route ids, visits every page and writes the report. |
| `routes.mjs` | Pages per role, where each route id comes from, and the `@page` coverage check. |
| `page-checks.mjs` | Waits for Blazor to finish rendering and measures the page inside the browser. |
| `report.mjs` | Writes `report.json` (everything) and `report.md` (summary and defects). |

## Requirements

- Node.js (tested with 24). Nothing is installed: the script reuses `tools/assets/browser.mjs`,
  which loads an existing **playwright-core** (`PLAYWRIGHT_CORE`) and starts the installed
  **Chrome** (`CHROME_PATH`); see [`tools/assets/README.md`](../assets/README.md).
- The app running with the Development seed data, for example the `fitedu-main` preview server on
  `http://localhost:5090`.

## Running

```bash
FITEDU_STUDENT_PASSWORD=... FITEDU_ADMIN_PASSWORD=... \
  node tools/responsive-check/check-pages.mjs http://localhost:5090 /path/to/output
```

- Argument 1: the app's base URL (default `http://localhost:5090`).
- Argument 2: the output folder (default: `fitedu-responsive-check` in the system temp folder). It
  gets `report.json`, `report.md` and `screenshots/` (a first-screen and a full-page JPEG per
  visit). Keep it outside the repo.
- `FITEDU_STUDENT_PASSWORD`, `FITEDU_ADMIN_PASSWORD`: the seeded passwords (the
  `SeedData:StudentPassword` and `AdminUser:Password` settings). They are only typed into the
  login form and never logged. A role whose password is missing is skipped and reported.
- Optional: `FITEDU_STUDENT_EMAIL` (default `alice.johnson@fitedu.local`), `FITEDU_ADMIN_EMAIL`
  (default `admin@fitedu.local`), and `FITEDU_CHECK_PAGES`, a regular expression that limits the
  visit to matching page URLs for a quick re-run (for example `FITEDU_CHECK_PAGES='^/profile'`).

The exit code is 0 only when there are no defects and every route is covered.

## Notes

- Each page opens in a fresh tab: a full-page screenshot resets the phone emulation of its tab,
  so a reused tab would measure the next page as a desktop with a mouse.
- In the full-page screenshots, fixed bars (the tab bar, the assistant button, sticky action bars)
  sit at the bottom of the first screen. That is how the capture works, not a layout defect; the
  `-top` screenshot shows the first screen as a phone shows it.
- `net::ERR_ABORTED` request failures are ignored: moving to the next page cancels requests that
  are still in flight.
