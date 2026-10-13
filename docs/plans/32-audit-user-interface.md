# Audit: User Interface

| | |
|---|---|
| Finding prefix | UI |
| Created | 2026-07-11 |
| Scope | Component-level visual correctness, consistency, and accessibility across the 32 routable pages, 30 shared components, layout components, `wwwroot/css/app.css`, `wwwroot/css/shared-components.css`, and 36 scoped `.razor.css` files |
| Delegated | Flow-level UX (navigation, feedback, empty/error states, bilingual experience) → 33 (UX). Hardcoded/missing localized *strings* → 33 (UX); localization *infrastructure* → 39 (CFG). Render-tree/CSS performance → 34 (PERF). Motion, tokens, skeletons, focus-ring rollout already implemented (see the plan-13 note in [00-INDEX.md](00-INDEX.md); not re-reported). Table sorting → implemented across all admin/student tables (former plan 10). |

## 1. Methodology

Static analysis only — the app was not launched. Examined:

- All 32 routable pages in `src/FitEdu.Web/Pages/` (markup read in full for AdminUsers, AdminCourses, AdminBlog, AdminCategories, AdminAssessmentSubmissions, AdminCourseEdit (partial), Billing, Courses, Assessments, AssessmentForm, AssessmentHistory, CourseDetail, LessonViewer, MyCertificates, VerifyCertificate, Pricing, Home, Login, Register, ForgotPassword, Profile, Chat, Disabled, SubscriptionSuccess).
- Shared components: ScrollableTable, SortableColumnHeader, AdminPaginationToolbar, PaginationNav, ConfirmModal, FormModal, DismissibleAlert, StatusBadge, LoadingSpinner, PageStateContainer, CategoryFilterBar, UpgradePrompt, PresenceIndicator, MessagePane, AssistantWidget, IncomingCallToast.
- Layout: MainLayout, NavMenu (+ scoped CSS), AvatarDropdown, CultureSelector, ImpersonationBanner; `App.razor`, `Routes.razor`.
- CSS: `app.css` (309 lines), `shared-components.css` (653 lines), targeted reads/greps of scoped `.razor.css` files; repo-wide sweeps for `style="`, `data-label`, `aria-*`, `role=`, `<caption`, `.sort-arrow`, `tier-badge`.
- resx files were checked only to verify values that CSS/markup couples to (e.g. `ColActions`).

NOT examined: runtime rendering, real contrast measurements in a browser, JS interop behavior, Bootstrap vendored sources (except to confirm `bg-light` semantics) — static audit per scope. Visual-polish work items (tokens, `:focus-visible` rollout, micro-interactions, skeletons, reduced-motion) were handled by the since-implemented visual-polish plan (see the plan-13 note in 00-INDEX.md) and are not repeated as findings here.

## 2. Summary Scorecard

| Severity | Count |
|----------|-------|
| Critical | 0 |
| High | 0 |
| Medium | 0 |
| Low | 3 |
| Info | 1 |

The UI layer is in better shape than most student projects: a coherent dark-theme token set in `app.css`, a documented CSS consolidation rule that is followed throughout, a genuinely good mobile card-table transformation with 100% `data-label` coverage on all 12 data tables, a skip link, `aria-sort` on sortable headers (now styled — see UI-8, fixed), `aria-pressed` on the culture selector and category chips, visually-hidden text for icon-only cells, `role="log"`/`aria-live` on the chat pane, focus-trapped/focus-restoring modals, and a global `prefers-reduced-motion` kill-switch already in place. All six Medium findings (keyboard operability of the admin collapsible header, modal focus trap/restore, `.tier-badge` duplication, unstyled sort-indicator classes, Chat's unlabeled spinner, and unlabeled search inputs/filter chips) are fixed. What remains is Low/Info polish: static inline styles, table captions, stray Bootstrap palette leaks, and one dropdown's keyboard model.

## 3. Findings

### UI-11: Static inline styles scattered through markup instead of scoped CSS  [Low] [Effort: S]
- **Evidence:** ~34 `style="` occurrences in `.razor` files. Legitimately dynamic ones aside (progress width `CourseDetail.razor:40`, parameterized `ScrollableTable.razor:6`), the static offenders include: `SortableColumnHeader.razor:11` (cursor/user-select/white-space), `MessagePane.razor:7` (font-size), `AdminCourseEdit.razor:162-167` (five column widths).
- **Impact:** Bypasses the documented consolidation rule and the token system, so theme adjustments (like the deferred hardcoded-value token sweep — see the plan-13 note in 00-INDEX.md) miss these; `#444` is invisible against the dark card in practice.
- **Recommendation:** Fold each static style into the owning component's `.razor.css`; do it alongside the deferred token sweep to avoid touching the same lines twice.
- **Progress:** D11 removed `BlogArticle.razor`'s container padding (now in `BlogArticle.razor.css`) and `ConversationSidebar.razor`'s list max-height (the list already fills the sidebar through its scoped CSS).
- **Progress (D13):** the static styles in `AdminTestimonialEditor` (avatar preview, file input, form width), `VerifyCertificate`, `AdminLearningPathEditor` and `AdminBlogEditor` moved into scoped CSS.

### UI-12: Data tables have no caption or accessible name  [Low] [Effort: S]
- **Evidence:** `ScrollableTable.razor:6-18` renders a bare `<table>` with no `<caption>`, `aria-label`, or `aria-labelledby` parameter; no `<caption>` exists anywhere in the repo (grep). Every ScrollableTable consumer (now including Billing) is affected; AdminAnalytics' two inline tables are now named by their panel headings (`aria-labelledby`, D13).
- **Impact:** Screen-reader table navigation announces an anonymous table.
- **Recommendation:** Add an optional `AriaLabel`/`Caption` parameter to ScrollableTable (visually-hidden `<caption>`) and pass the page heading string at each call site.
- **Status (D12):** `ScrollableTable` has a `Caption` parameter (visually-hidden `<caption>`), passed by the ten admin list pages. Still open: Billing, Notifications and AssessmentHistory (the AdminAnalytics inline tables are named by their panel headings, D13).

### UI-14: Bootstrap default palette leaks into the themed UI  [Low] [Effort: S]
- **Evidence:** `AssessmentHistory.razor:22` — `card-header bg-primary text-white` renders Bootstrap's stock blue header on the otherwise volt-lime (`--text-strong-accent`) theme; `AdminCourseEdit.razor:124` — `badge bg-light text-dark`; `ImpersonationBanner.razor:9` — `btn-light` (acceptable on the coral banner, but the only `btn-light` in the app).
- **Impact:** The one blue element in the app reads as foreign; light badges are near-white blobs on dark cards.
- **Recommendation:** Replace `bg-primary text-white` with the themed card-header (already styled globally at `shared-components.css:68-72`) plus a border accent; use `--bg-neutral-subtle` badges as elsewhere.

### UI-15: `role="menu"` dropdown without arrow-key or Escape handling  [Info] [Effort: S]
- **Evidence:** `src/FitEdu.Web/Layout/AvatarDropdown.razor:5-38` — declares `aria-haspopup="menu"`/`role="menu"`/`role="menuitem"` and closes on focus-out, but implements no ArrowUp/ArrowDown/Escape/Home/End keyboard model that the `menu` role promises.
- **Impact:** Minor: only two items, Tab still works because they are real links/buttons; but the announced role sets expectations the widget doesn't meet.
- **Recommendation:** Either downgrade to the disclosure pattern (`aria-expanded` on the button, plain links — no `menu` roles) or add the key handling. The disclosure pattern is the cheaper, correct fix here.

## 4. Prioritized Action List

| ID | Severity | Effort | Action |
|----|----------|--------|--------|
| UI-11 | Low | S | Fold static inline styles into scoped CSS (with the deferred token sweep) |
| UI-12 | Low | S | Add caption/aria-label parameter to ScrollableTable and pass at call sites |
| UI-14 | Low | S | Purge `bg-primary text-white` / `bg-light text-dark` leaks |
| UI-15 | Info | S | Downgrade AvatarDropdown to the disclosure pattern (or implement menu keys) |

## 5. Related Findings Elsewhere

- **Former UX-1 (33, fixed)** — the hardcoded English strings that ship inside these components (DismissibleAlert/ConfirmModal/PaginationNav/StatusBadge defaults, `blazor-error-ui` text, `ItemLabel` values) were quantified there; the banner's colors (former UI-2) are fixed.
- **Former UX-2 / UX-6 (33, fixed)** — silent failure and infinite-spinner *flows* behind the loading components (now consistently `LoadingSpinner`, see former UI-9, fixed).
- **Former UX-5 (33, fixed)** — where and how the `DismissibleAlert` messages appear (placement, severity styling, dismissibility) was flow-level and lived there.
- **Former UX-12 (33, fixed)** — inconsistent date/time formats rendered inside the tables audited here.
- **Former plans 10 and 13 (both implemented)** — table sorting is rolled out across every admin/student table (including Billing, whose bespoke table/toolbar was migrated to the shared components), and the visual-polish system (tokens, `:focus-visible`, skeletons, reduced-motion) shipped; only the cosmetic token sweep remains deferred (see the plan-13 note in [00-INDEX.md](00-INDEX.md)).
- **PERF (34)** — `ScrollableTable` renders all rows without virtualization (by design for paged lists); any render-cost concerns belong there.
