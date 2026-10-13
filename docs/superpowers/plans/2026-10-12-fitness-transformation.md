# FitEdu Fitness Transformation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Execution model: Opus 5.5, high effort, fresh subagent per task, tests green at every commit. Section 9 gives the orchestration script that runs the whole plan agentically.

**Goal:** Turn the FitEdu codebase (ex career-counselling LMS) into a professional, mobile-first fitness-courses application: every seeded dataset, image, copy string and visual becomes fitness content, a public-domain exercise library is wired into lessons, and every existing feature (courses, assessments, paths, blog, testimonials, live sessions, certificates, chat/calls, subscriptions, admin, AI assistant, search, EN/EL) keeps working.

**Architecture:** Keep Blazor Server + loopback API, EF Core/SQL Server, seeders and resx exactly as they are. Add one new aggregate (`Exercise` + `LessonExercise`), fitness columns on `Course` and `ApplicationUser`, three small seeders (exercises, learning paths, testimonials+live classes), one new public page pair (`/exercises`, `/exercises/{slug}`) and one layout component (`BottomNav`). Replace seed JSON / resx / copy in place. Layer a mobile-first design system on the existing CSS tokens.

**Tech Stack:** .NET 10 / Blazor Server, EF Core 10 (SQL Server; SQLite + InMemory in tests), xUnit + Shouldly, Playwright (tests/e2e, Node 22), Bootstrap 5 (vendored), Bootstrap Icons, QuestPDF, HtmlSanitizer, resx with hand-maintained `*.Designer.cs`. **No new NuGet or npm dependencies.** Image conversion uses Playwright's Chromium (already installed under `tests/e2e/node_modules`).

---

## 1. Context

- `master` already carries the rename to FitEdu and the volt-lime/charcoal palette (`src/FitEdu.Web/wwwroot/css/app.css` `:root`). Everything else — 19 career courses, 19 career assessments, 4 career blog posts, hero copy, legal pages, assistant prompt, classroom hero photo, compass loader — is still the old product.
- Decisions locked with the user in this session:
  - Reuse existing features/code; **schema and features may change** to fit a fitness app.
  - Exercise media = **photo pairs from Free Exercise DB** (public-domain dedication), animated start→end in the UI. No videos (no verified CC0 exercise videos exist; Pexels/Pixabay licences are not CC0 and are excluded).
  - **Pre-approved**: one batched download from **GitHub `yuhonas/free-exercise-db`** and **Wikimedia Commons (CC0 / public-domain files only)** into a staging folder outside the repo. Nothing else may be downloaded. The licence table is still written to the manifest before import.
  - All new seeded content is **English + Greek**.
  - Catalogue stays **one-for-one: 19 courses, 19 assessments** (file names change, counts don't).
  - exrx.net is a **factual reference only** (muscle names, exercise mechanics, technique principles). Its text and images are copyrighted and are never copied. Free Exercise DB `instructions` are public domain and may be reused verbatim.
  - No human steps beyond the approval already given.

## 2. Confirmed codebase facts the plan relies on

| Fact | Where |
|---|---|
| Course seed JSON: `title/titleEl/description/descriptionEl/requiredTier/isPublished/category/modules[]{title,titleEl,description,descriptionEl,sortOrder,lessons[]{title,titleEl,content,contentEl,pdfPath,videoPath,durationMinutes,sortOrder}}` | `src/FitEdu.Application/DTOs/Seed/CourseSeedDtos.cs`, `src/FitEdu.Infrastructure/Seeding/CourseSeeder.cs:71-140` |
| Categories are created on demand from `course.category` | `CategorySeedHelper.ResolveCategoryId` |
| Assessment seed file: `Key, Category, Title, TitleEl, Description, DescriptionEl, IsPublished, SchemaJson` (SchemaJson is a JSON **string** of `{id, version, title, titleEl, sections[{id,title,titleEl,questions[{id,type: rating|choice|multiselect|text, label, labelEl, min,max,minLabel,maxLabel,minLabelEl,maxLabelEl, options?, required}]}]}`) | `src/FitEdu.Shared/JSON/Assessments/*.json` |
| Students seed = 10 `{firstName,lastName}` → `first.last@fitedu.local`; e2e logs in as alice.johnson / bob.smith | `src/FitEdu.Shared/JSON/Students/students.json`, `tests/e2e/helpers.mjs:12-13` |
| Blog seed = 4 records in code, seeded once (`SeederTests:72` asserts no reseed) | `src/FitEdu.Application/DTOs/Blog/BlogSeedData.cs`, `Seeding/BlogArticleSeeder.cs` |
| Subscription plans: 3, seeded in code (`SeederTests:42`) | `Seeding/SubscriptionPlanSeeder.cs` |
| Learning paths, testimonials, scheduled sessions are **not seeded today** (admin-created only) | `Seeding/` has no seeder for them |
| Dev seeding order: roles → plans → admin user → (Development + `SeedData:Enabled`) blog → courses → assessments → students | `src/FitEdu.Web/Startup/DatabaseSeedingExtensions.cs:95-175` |
| Lesson markdown renderer supports `#/##/###`, `- ` and `1.` lists, paragraphs, inline bold — **no images** → exercise media must be structured data | `src/FitEdu.Web/Pages/LessonViewer.razor.cs:232-310` |
| Lesson content types: 1 text, 2 video (signed `/api/lessons/{id}/asset?type=video` or YouTube), 3 PDF | `LessonViewer.razor:39-82` |
| `Lesson`, `Module`, `Course`, `Category`, `LearningPath` inherit `AuditableEntity` (IsPublished, IsDeleted, RowVersion…); `Testimonial`, `ScheduledSession` do not | `src/FitEdu.Domain/Domain/Entities/*.cs` |
| Adding a column = entity → EF config (`Infrastructure/Data/Configurations/*Configuration.cs`) → `dotnet ef migrations add` → DTO (append with default) → mapping expression **and** materialised twin → consumer → page → resx ×3 → tests; `MigrationChainTests` fails on pending model changes | `docs/ADDING-A-FIELD.md` |
| resx `*.Designer.cs` are **hand-edited** (VS generator doesn't run under `dotnet build`) | `docs/ADDING-A-FIELD.md:34-36` |
| Static images: `wwwroot/images/background.png` (1.45 MB classroom photo; hero in `Pages/Home.razor.css:53` + `Home.razor.cs:36`), `images/og-home.jpg`, `favicon.png`, `icon-192.png` | — |
| CSS: `app.css` 446 lines (tokens, boot compass loader), `shared-components.css` 780, `Home.razor.css` 968, per-page `.razor.css` | — |
| Course list page uses `CategoryFilterBar` (chips + search), `PaginationNav`, `StarRating`, `SkeletonBlock`; cards are `.course-card` with `a.course-card-link` (e2e asserts these two selectors) | `Pages/Courses.razor`, `tests/e2e/pages.spec.mjs` |
| Tooling: Python 3.14, Node + Playwright (`tests/e2e`), dotnet 10.0.401. No ImageMagick/ffmpeg/cwebp | — |
| CI: `dotnet format style --verify-no-changes --severity warn` (only rule: file-scoped namespaces in new `.cs`), build, tests; a `migrations` job applies the chain on SQL Server 2022 | `.github/workflows/tests.yml` |
| **xUnit hosts never seed the JSON folders** (paths resolve against the test bin dir) → replacing course/assessment/student JSON breaks no xUnit test; only e2e and a fresh dev DB see it. Blog + plans **are** seeded in test hosts (SQLite enforces unique slug / NOT NULL) | tests sweep H1–H3 |
| bUnit is used (`cut.Find(...)` in `LessonViewerTests.cs:69` asserts `.compass-spinner`) | — |
| `UserStatus` enum (`Unknown=0, Student=1, Graduate=2, NEET=3, Other=99`) is the career-era "Status" field on register/profile, mapped by `Web/Extensions/UserStatusLabels.cs`, asserted in `EnumValueTests.cs:64-71`, `AuthServiceTests.cs:222-248`, `AuthApiServiceTests.cs:119-145`, `ProfileIntegrationTests.cs:46,52`, `TokenServiceTests.cs:35,69` | — |
| `CategorySeedHelper` never sets `NameEl` (Greek UI shows English category names); `Testimonial` and `SubscriptionPlan` have no EL columns | localisation sweep |
| Seeders are all-or-nothing on table emptiness (no slug/key matching) → a reseed needs an empty table or a fresh DB | `CourseSeeder.cs:30` etc. |
| Blog `CoverImageUrl` values that are relative upload paths render broken (`<img src="blog/covers/…">`); absolute `/images/...` paths work. CSP is `img-src 'self' data: blob:` and blocks inline handlers (`Home.razor:337 onerror`, `LessonViewer.razor:59 oncontextmenu`) | assets sweep |
| Compass boot CSS in `app.css:190-284` is dead (no markup); `LoadingSpinner` compass is live in 25 pages. Font Awesome is loaded only for the password-eye icon. Emoji used as icons in Assessments/UpgradePrompt/CourseDetail/LessonViewer/AssistantWidget | ui sweep |
| Known mobile defects: header trailing block overflows at 320 px; `NotificationBell` panel spills left; `SiteSearchBox` flyout `min-width:320px`; `AssessmentForm` rating `btn-group` doesn't wrap; `ScrollableTable` nests a 60 vh scroll inside the mobile card-table; `.testimonials-grid`, `.plan-limit-banner`, `.upgrade-prompt`, `.enroll-*-notice`, `.assessment-description` have no CSS; `LessonViewer .text-content` has no `max-width` for media; `main.content` is capped at 1440 px so the hero is not full-bleed | ui sweep |
| Leftover old-name identifiers: `MigrationChainTests.cs:51` DB prefix `RyfMigrationChainTest_`, env var `RYF_OLLAMA_LIVE` (`AssistantLiveSmokeTests.cs`, `README.md:655`) | tests sweep |
| Site search fallback indexes Courses/Assessments/Blog titles only; assistant semantic index builds from course/lesson text | domain sweep |

---

## 3. Target design

### 3.1 Data model (new / changed)

```
Exercise (new, AuditableEntity)            LessonExercise (new join, no base)
  Id            Guid                          Id            Guid
  Slug          string(120) unique            LessonId      Guid  → Lesson.Exercises (cascade)
  NameEn        string(150) required          ExerciseId    Guid  → Exercise (restrict)
  NameEl        string(150)?                  SortOrder     int
  Category      string(40)   strength|stretching|cardio|plyometrics|powerlifting|strongman|olympic weightlifting
  Level         string(20)   beginner|intermediate|expert     Sets  int?
  Force         string(20)?  push|pull|static                 Reps  string(40)?   "8–12", "30 s", "AMRAP"
  Mechanic      string(20)?  compound|isolation               RestSeconds int?
  Equipment     string(40)?  dumbbell|barbell|body only|…     NotesEn string(500)?
  PrimaryMuscles   string(200)  comma-joined, lowercase       NotesEl string(500)?
  SecondaryMuscles string(300)
  InstructionsEn string(4000)  "\n"-joined steps
  InstructionsEl string(4000)?
  ImageCount    int (2)      images at wwwroot/images/exercises/{Slug}/0.webp, 1.webp

Course (+)  Level: CourseLevel (Beginner=0, Intermediate=1, Advanced=2)
            Goal: FitnessGoal (GeneralFitness=0, Strength=1, MuscleGain=2, FatLoss=3, Endurance=4, Mobility=5, Recovery=6, Nutrition=7)
            Equipment string(120)?  comma-joined codes: none,dumbbells,barbell,kettlebell,bands,mat,bike,chair,cables,machine
            EstimatedWeeks int?, SessionsPerWeek int?
            CoverExerciseSlug string(120)?   → cover image = images/exercises/{slug}/0.webp (duotone CSS)
Lesson (+)  ICollection<LessonExercise> Exercises
ApplicationUser (+) FitnessGoal? Goal            (experience level = repurposed UserStatus, below)
UserStatus  (repurposed, same ints) Unknown=0, Beginner=1, Intermediate=2, Advanced=3, Other=99
            → labels in GlobalRes become "Training level": Beginner / Intermediate / Advanced / Prefer not to say
Testimonial (+) QuoteTextEl string(1000)?, RoleOrTitleEl string(150)?
SubscriptionPlan (+) DescriptionEl string(500)?
Category    NameEl now filled by the seeder from course/assessment JSON `categoryEl`
```

One migration for all of the above: `FIT1_ExerciseLibraryAndFitnessFields`.

### 3.2 Content catalogue (all EN + EL)

**Categories (7, created by `CategorySeedHelper` from course JSON):** Strength · Cardio · Mobility · Core · Nutrition · Recovery · Performance (EL: Δύναμη · Καρδιο · Κινητικότητα · Κορμός · Διατροφή · Αποκατάσταση · Απόδοση).

**Courses (19)** — file `src/FitEdu.Shared/JSON/Courses/<slug>.json` (old files deleted):

| # | slug | Title EN / EL | Category | Level | Goal | Equipment | Tier | Wks×/wk | Cover exercise |
|---|---|---|---|---|---|---|---|---|---|
| 1 | bodyweight-basics | Bodyweight Basics / Βασικές Ασκήσεις με Βάρος Σώματος | Strength | Beginner | GeneralFitness | none | Free | 4×3 | push-ups |
| 2 | strength-foundations | Strength Foundations / Θεμέλια Δύναμης | Strength | Beginner | Strength | dumbbells,barbell | Free | 6×3 | barbell-squat |
| 3 | dumbbell-home-strength | Dumbbell Home Strength / Δύναμη στο Σπίτι με Αλτήρες | Strength | Intermediate | MuscleGain | dumbbells | Plus | 6×3 | dumbbell-bench-press |
| 4 | kettlebell-fundamentals | Kettlebell Fundamentals / Βασικές Αρχές Kettlebell | Strength | Intermediate | Strength | kettlebell | Plus | 4×3 | kettlebell-swings |
| 5 | core-and-stability | Core & Stability / Κορμός & Σταθερότητα | Core | Beginner | GeneralFitness | mat | Free | 4×3 | plank |
| 6 | mobility-and-flexibility | Mobility & Flexibility / Κινητικότητα & Ευλυγισία | Mobility | Beginner | Mobility | mat | Free | 4×4 | hip-flexor-stretch (nearest DB slug) |
| 7 | desk-posture-reset | Desk Posture Reset / Επαναφορά Στάσης Γραφείου | Mobility | Beginner | Mobility | none | Free | 3×5 | chin-to-chest-stretch |
| 8 | couch-to-5k | Couch to 5K Running / Από τον Καναπέ στα 5 χλμ | Cardio | Beginner | Endurance | none | Free | 8×3 | running-treadmill |
| 9 | hiit-fat-loss | HIIT for Fat Loss / HIIT για Απώλεια Λίπους | Cardio | Intermediate | FatLoss | none | Plus | 6×3 | burpee |
| 10 | cycling-endurance | Indoor Cycling Endurance / Αντοχή στο Στατικό Ποδήλατο | Cardio | Intermediate | Endurance | bike | Plus | 6×3 | bicycling-stationary |
| 11 | upper-body-hypertrophy | Upper Body Hypertrophy / Υπερτροφία Άνω Κορμού | Strength | Intermediate | MuscleGain | barbell,dumbbells,cables | Pro | 8×4 | barbell-bench-press-medium-grip |
| 12 | lower-body-power | Lower Body Power / Δύναμη Κάτω Άκρων | Strength | Advanced | Strength | barbell | Pro | 8×3 | barbell-deadlift |
| 13 | push-pull-legs-program | Push Pull Legs Programme / Πρόγραμμα Push Pull Legs | Strength | Advanced | MuscleGain | barbell,dumbbells,cables,machine | Pro | 8×6 | pullups |
| 14 | plyometrics-for-athletes | Plyometrics for Athletes / Πλειομετρικές Ασκήσεις για Αθλητές | Performance | Advanced | Endurance | none | Pro | 6×2 | box-jump-multiple-response |
| 15 | nutrition-basics | Nutrition Basics for Training / Βασικές Αρχές Διατροφής για Προπόνηση | Nutrition | Beginner | Nutrition | none | Free | 4×1 | (null → category icon) |
| 16 | recovery-and-sleep | Recovery & Sleep / Αποκατάσταση & Ύπνος | Recovery | Beginner | Recovery | none | Free | 3×1 | (null) |
| 17 | beginner-yoga-and-stretching | Beginner Yoga & Stretching / Γιόγκα & Διατάσεις για Αρχάριους | Mobility | Beginner | Mobility | mat | Plus | 4×3 | cat-stretch |
| 18 | strength-and-balance-60-plus | Strength & Balance 60+ / Δύναμη & Ισορροπία 60+ | Strength | Beginner | GeneralFitness | chair,dumbbells | Free | 6×2 | bodyweight-squat (nearest) |
| 19 | training-around-injuries | Training Around Injuries / Προπόνηση με Τραυματισμούς | Recovery | Intermediate | Recovery | none,bands | Pro | 4×2 | (null) |

Cover-exercise slugs are **resolved by the executor against the imported `exercises.json`** (Task B2 produces `docs/assets/exercise-slugs.txt`); the column above gives the intended exercise, and the content-integrity test (Task C1) fails on any slug that does not exist.

Each course: 2–3 modules, 4–8 lessons (≥ 95 lessons total). Lesson kinds: **workout** (markdown intro + `exercises[]` with sets/reps/rest), **technique** (one exercise explained in depth: muscles, mechanics, cues, errors, regressions/progressions) and **knowledge** (nutrition/recovery/programming text). Every lesson has `durationMinutes` (workout 20–45, technique 8–15, knowledge 6–12).

**Lesson text rules:** written originally from exrx.net's classifications (Exercise & Muscle Directory for muscle names and mechanics; Weight Training Guide / Kinesiology articles for principles) and Free Exercise DB `instructions` (public domain, may be quoted). Markdown limited to what the renderer supports: `#`, `##`, `###`, `- ` lists, `1.` lists, `**bold**`, blank-line paragraphs. No links, images or tables. Each workout lesson ends with `## Safety` (3 bullets) and every course description states "Not medical advice — consult a professional before starting if you have a condition."

**Assessments (19)** — file `src/FitEdu.Shared/JSON/Assessments/<key>.json`, `Category` reused as the assessment grouping (Readiness, Mobility, Nutrition, Recovery, Safety, Cardio, Strength, Mindset, Setup):

| key | Title EN / EL | Category |
|---|---|---|
| training_readiness_v1 | Training Readiness / Ετοιμότητα για Προπόνηση | Readiness |
| mobility_self_check_v1 | Mobility Self-Check / Αυτοέλεγχος Κινητικότητας | Mobility |
| posture_desk_check_v1 | Desk Posture Check / Έλεγχος Στάσης στο Γραφείο | Mobility |
| flexibility_baseline_v1 | Flexibility Baseline / Βάση Ευλυγισίας | Mobility |
| nutrition_habits_v1 | Nutrition Habits / Διατροφικές Συνήθειες | Nutrition |
| hydration_check_v1 | Hydration Check / Έλεγχος Ενυδάτωσης | Nutrition |
| sleep_recovery_v1 | Sleep & Recovery / Ύπνος & Αποκατάσταση | Recovery |
| stress_load_v1 | Stress & Training Load / Στρες & Φορτίο Προπόνησης | Recovery |
| injury_risk_screen_v1 | Injury Risk Screen / Έλεγχος Κινδύνου Τραυματισμού | Safety |
| balance_falls_risk_v1 | Balance & Falls Risk / Ισορροπία & Κίνδυνος Πτώσης | Safety |
| cardio_baseline_v1 | Cardio Baseline / Βάση Καρδιοαναπνευστικής Αντοχής | Cardio |
| running_readiness_v1 | Running Readiness / Ετοιμότητα για Τρέξιμο | Cardio |
| strength_baseline_v1 | Strength Baseline / Βάση Δύναμης | Strength |
| core_stability_v1 | Core Stability / Σταθερότητα Κορμού | Strength |
| goal_clarity_v1 | Goal Clarity / Σαφήνεια Στόχων | Mindset |
| motivation_consistency_v1 | Motivation & Consistency / Κίνητρο & Συνέπεια | Mindset |
| body_awareness_v1 | Body Awareness / Σωματική Επίγνωση | Mindset |
| equipment_access_v1 | Equipment & Space / Εξοπλισμός & Χώρος | Setup |
| time_availability_v1 | Time Availability / Διαθέσιμος Χρόνος | Setup |

Each: 2–3 sections, 8–12 questions, mix of `rating`/`choice`/`multiselect`/`text` (same four types as today), all `*El` fields filled.

**Learning paths (4, new `LearningPathSeeder`, seed-once):** Beginner Start (1→5→6→2) · Fat Loss (8→9→15→16) · Build Muscle (2→3→11→13) · Move Better (7→6→17→5). Numbers = course rows above; steps resolve courses by `TitleEn`.

**Blog (4, replace `BlogSeedData.cs` records, same slugs count):** `progressive-overload-explained`, `how-much-protein-do-you-need`, `sleep-is-your-best-recovery-tool`, `five-minute-warm-up-routine`; author "FitEdu Coaching Team"; tags from {Strength, Nutrition, Recovery, Mobility, Beginners}; `CoverImageUrl` = `/images/blog/<slug>.webp` (duotone crop of an exercise photo, Task B4).

**Testimonials (6) + Live classes (6)** — new `ShowcaseSeeder` (seed-once; sessions hosted by the admin user, `StartsAtUtc` = next 6 weekdays 18:00 UTC, 45 min, 12 participants, titles like "Live Mobility Flow", "Kettlebell Technique Clinic"). Testimonial avatars: none (initials fallback already exists).

**Students:** keep the 10 `students.json` names (neutral; e2e depends on alice/bob).

**Subscription plans:** keep Free/Plus/Pro; descriptions and feature lines become fitness copy (in `SubscriptionPlanSeeder` + `PricingRes`).

### 3.3 Assets and licensing (one visual family)

| Asset family | Source | Licence (verify on the source page on download day, quote + date into manifest) | Use |
|---|---|---|---|
| ~870 exercise photo pairs (`<id>/0.jpg`, `1.jpg`, white background, same model) + `exercises.json` metadata | `https://github.com/yuhonas/free-exercise-db` (ZIP of `main`) | `LICENSE` in repo — expected **Unlicense** (public-domain dedication) | exercise library, lesson cards, course covers (duotone), blog covers, OG image collage |
| 1 hero photo for Home | Wikimedia Commons, `Category:Fitness training` / `Category:Weight training`, filter **CC0** or **PD** (licence box on the file page) | file page licence template, quoted | Home hero (`images/hero-home.webp`, 1920 px, ≤ 250 KB) |
| Logo, icons, loader, empty-state art | authored in-repo as SVG in the palette | own work (MIT, repo licence) | favicon.svg, icon-192.png, nav brand, PulseLoader, category icons |

Excluded with reasons (recorded in `docs/assets/ASSET-MANIFEST.md`): Pexels / Unsplash / Pixabay (own licences, not CC0); wger exercise images (CC-BY-SA); Everkinetic (CC-BY-SA); any Commons file whose page shows CC-BY/CC-BY-SA/GFDL; exrx.net media (copyright).

Staging: `C:/Users/GS/Desktop/Dev/_asset-staging/fitedu/free-exercise-db/` and `.../wikimedia/` (outside the repo, fresh folders). Import targets: `src/FitEdu.Web/wwwroot/images/exercises/<slug>/{0,1}.webp` (max 480 px wide, WebP q=0.8, converted by `tools/assets/convert-exercise-images.mjs`), `images/hero-home.webp`, `images/blog/*.webp`, `images/og-home.jpg` (1200×630), `favicon.svg`, `favicon.png` (64), `icon-192.png`. Delete `images/background.png` only after the hero references the new file and tests are green.

Manifest `docs/assets/asset-manifest.json` (machine-readable, one row per family with a path glob) + `docs/assets/ASSET-MANIFEST.md` (human table + exclusion list). Guard test `AssetManifestTests` fails when a file under `wwwroot/images` (or the two icons) matches no row, or a row's licence is not in `{"Unlicense","CC0-1.0","Public domain","MIT (own work)"}`.

### 3.4 UI system (mobile-first)

- **Tokens kept**, new layout primitives added to `app.css`: fluid type scale (`--fs-1…--fs-6` via `clamp()`), `.page` (max-width 1200, `padding-inline: clamp(1rem, 4vw, 2rem)`), `.grid-auto` (`repeat(auto-fill, minmax(min(100%, 17rem), 1fr))`), `.card-surface` + `.card-surface__body` (surface, 1px `--border-subtle`, `--radius-lg`; hover lift only on links and `.card-surface--interactive`; built in D1 in `shared-components.css` under its own name because a lift on every Bootstrap `.card` would move admin forms, so `.card` only takes the radius), `.chip` / `.chip--lime` / `.chip--coral` / `.chip--teal`, `.stat-tile`, `.section-head`, `.sticky-bottom-bar` (mobile CTA bar with `env(safe-area-inset-bottom)`), `.duotone` (grayscale + lime multiply overlay for photos), `prefers-reduced-motion` resets.
- **Navigation:** `NavMenu` becomes a sticky glass bar (`backdrop-filter: blur(12px)`, `--bg-primary` at 80 %) with an inline SVG dumbbell logo; **new `Layout/BottomNav.razor`** (5 tabs, `< 768px` only, role-aware: Student = Home · Courses · Exercises · Paths · Profile; Admin = Users · Courses · Assessments · Analytics · Blog; Guest = Home · Exercises · Pricing · Login). `MainLayout` adds `<BottomNav />` and `main` gets `padding-bottom: calc(4.5rem + env(safe-area-inset-bottom))` on mobile.
- **Loader:** `LoadingSpinner.razor` keeps its API (`Size`, `Label`, `Overlay`) but renders a **pulse/ECG line** SVG (`pulse-loader__*` classes); the boot loader in `App.razor` + `app.css` (`compass-boot__*`) is replaced by the same art.
- **Components added** under `Shared/Components/Fitness/`: `ExerciseCard.razor` (start/end crossfade, chips, sets×reps, expandable instructions), `ExerciseMedia.razor` (the two-frame animator, tap-to-toggle, reduced-motion = side-by-side), `ProgressRing.razor` (SVG ring), `StatTile.razor`, `LevelBadge.razor`, `MuscleChips.razor`, `CourseCover.razor` (duotone cover or category icon fallback).
- **Pages redesigned** (each keeps its selectors used by tests): Home (hero + stats strip + goal cards + "how it works" + featured courses + testimonials + blog + CTA; dashboard = "Today's plan" continue card, stats tiles, recommended courses by goal/level), Courses (filter chips goal/level/equipment + search, cover cards), CourseDetail (cover hero, chips, outcomes, module accordion with exercise counts, sticky enrol CTA), LessonViewer (workout layout: exercise cards, sets/reps, rest, intro text, sticky "Mark complete" bar, prev/next), **Exercises** (library grid, filters by muscle/equipment/level/category, search, pagination), **ExerciseDetail**, Paths (vertical timeline with progress), Assessments (cards + progress ring), AssessmentForm (one section per screen on mobile), Pricing (polish), Profile (goal/level selectors + stats), Sessions (schedule list), Blog (magazine grid), Login/Register (centered card), Admin lists (scroll wrappers; course edit gets the new fields; lesson editor gets an exercise picker).
- **Brand assets:** `favicon.svg` (dumbbell mark in lime on charcoal), PNG icons rendered from it, OG image rendered from an HTML template (`tools/assets/render-brand-images.mjs`).

### 3.5 Copy, assistant, PDF, email, SEO

- Every career-specific resx key gets new EN + EL values (**full key list in Appendix A**, filled from the localisation sweep). Designer.cs files are edited only when a key is added/removed.
- `AssistantService` system prompt → "FitEdu Coach": scope = courses, exercises, training basics; explicit non-medical disclaimer; new tool `search_exercises` (via `IExerciseService`). `AssistantRes` "RYF Assistant" → "FitEdu Coach".
- `CertificateService` PDF: title "Programme Completion Certificate", subtitle from course title, palette colours `#0E1116` / `#C6F432` / `#2EC4B6`, SVG dumbbell mark.
- `SmtpEmailService` / `StubEmailService`: "Welcome to FitEdu — let's get moving" etc.
- `SeoHead` defaults, sitemap, `HomeRes.PageTitle` "FitEdu | Fitness Courses & Exercise Library", OG image.
- `LegalRes` privacy/terms rewritten for a fitness platform (health data note on assessments, no medical advice).

---

## 4. Phase A — Assets & licence (serial, first)

### Task A1: Download pre-approved sources into staging

**Files:** none in repo. Staging folders outside repo.

- [ ] **Step 1:** Create `C:/Users/GS/Desktop/Dev/_asset-staging/fitedu/free-exercise-db/` and `.../wikimedia/` (fresh, empty).
- [ ] **Step 2:** Fetch the licence page text and record it: `curl -sL https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/LICENSE -o staging/free-exercise-db/LICENSE.txt`. Read it. It must be the Unlicense ("This is free and unencumbered software released into the public domain"). If it is not, **stop the task and report** — nothing else is approved.
- [ ] **Step 3:** `curl -sL https://github.com/yuhonas/free-exercise-db/archive/refs/heads/main.zip -o staging/free-exercise-db/main.zip`; verify size > 10 MB and not HTML (`rg -il "<html" main.zip` must find nothing). Extract into `staging/free-exercise-db/extracted/` (`Expand-Archive` in pwsh). Record ZIP size.
- [ ] **Step 4:** Count: `jq length extracted/free-exercise-db-main/dist/exercises.json` (expect ≈ 870) and `ls extracted/free-exercise-db-main/exercises | wc -l`. Save the count and the `exercises.json` top-level field list to `staging/free-exercise-db/NOTES.txt`.
- [ ] **Step 5 (hero photo):** Use the Commons API (no login): `https://commons.wikimedia.org/w/api.php?action=query&list=search&srsearch=gym%20barbell%20incategory:"CC-Zero"&srnamespace=6&format=json` and similar queries (`dumbbells`, `kettlebell`, `running track`). For up to 5 candidates fetch `action=query&prop=imageinfo&iiprop=extmetadata|url|size&titles=File:<name>` and keep only files whose `LicenseShortName` is `CC0` or `Public domain` and width ≥ 1600. Download the best one (landscape, people exercising or gym interior) to `staging/wikimedia/<File name>` and save the extmetadata JSON beside it. If none qualifies after 5 queries, record "no Commons hero found" and proceed with the fallback in Task B3 (collage hero).
- [ ] **Step 6:** Write `staging/DOWNLOAD-LOG.md`: table of name, URL, licence text seen, where stated, date, size. No commit (outside repo).

### Task A2: Asset manifest + guard test (before any import)

**Files:**
- Create: `docs/assets/asset-manifest.json`, `docs/assets/ASSET-MANIFEST.md`
- Create: `tests/FitEdu.Web.Tests/AssetManifestTests.cs`

- [ ] **Step 1: manifest JSON** (rows for existing files first, so the test is green before import):

```json
{
  "allowedLicences": ["Unlicense", "CC0-1.0", "Public domain", "MIT (own work)"],
  "assets": [
    { "glob": "images/exercises/**/*.webp", "name": "Free Exercise DB photo pairs", "sourceUrl": "https://github.com/yuhonas/free-exercise-db", "licence": "Unlicense", "licenceStatedAt": "https://github.com/yuhonas/free-exercise-db/blob/main/LICENSE", "checkedOn": "<download date>", "type": "image/webp", "notes": "converted from JPG, max 480px wide" },
    { "glob": "images/hero-home.webp", "name": "<Commons file name>", "sourceUrl": "<file page URL>", "licence": "CC0-1.0", "licenceStatedAt": "<file page URL>#Licensing", "checkedOn": "<date>", "type": "image/webp" },
    { "glob": "images/blog/*.webp", "name": "Blog covers (duotone crops of Free Exercise DB photos)", "sourceUrl": "https://github.com/yuhonas/free-exercise-db", "licence": "Unlicense", "licenceStatedAt": "https://github.com/yuhonas/free-exercise-db/blob/main/LICENSE", "checkedOn": "<date>", "type": "image/webp" },
    { "glob": "images/og-home.jpg", "name": "OG image rendered from tools/assets/og-template.html", "sourceUrl": "repo", "licence": "MIT (own work)", "licenceStatedAt": "LICENSE", "checkedOn": "<date>", "type": "image/jpeg" },
    { "glob": "favicon.svg", "name": "FitEdu mark", "sourceUrl": "repo", "licence": "MIT (own work)", "licenceStatedAt": "LICENSE", "checkedOn": "<date>", "type": "image/svg+xml" },
    { "glob": "favicon.png", "name": "FitEdu mark 64px", "sourceUrl": "repo", "licence": "MIT (own work)", "licenceStatedAt": "LICENSE", "checkedOn": "<date>", "type": "image/png" },
    { "glob": "icon-192.png", "name": "FitEdu mark 192px", "sourceUrl": "repo", "licence": "MIT (own work)", "licenceStatedAt": "LICENSE", "checkedOn": "<date>", "type": "image/png" }
  ]
}
```
(The `<…>` values are filled by the executor from `DOWNLOAD-LOG.md`; the guard test rejects any value that still starts with `<`.)

- [ ] **Step 2: guard test** (`tests/FitEdu.Web.Tests/AssetManifestTests.cs`):

```csharp
using System.Text.Json;
using Microsoft.Extensions.FileSystemGlobbing;
using Shouldly;

namespace FitEdu.Web.Tests;

/// <summary>Every shipped image must have a manifest row with an allowed licence (asset policy: CC0/PD only).</summary>
public class AssetManifestTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string WwwRoot = Path.Combine(RepoRoot, "src", "FitEdu.Web", "wwwroot");
    private static readonly string ManifestPath = Path.Combine(RepoRoot, "docs", "assets", "asset-manifest.json");

    private sealed record ManifestRow(string Glob, string Name, string SourceUrl, string Licence, string LicenceStatedAt, string CheckedOn, string Type);
    private sealed record Manifest(string[] AllowedLicences, ManifestRow[] Assets);

    private static Manifest LoadManifest() =>
        JsonSerializer.Deserialize<Manifest>(File.ReadAllText(ManifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    [Fact]
    public void EveryImageUnderWwwroot_HasAManifestRow()
    {
        var manifest = LoadManifest();
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude("images/**/*");
        matcher.AddInclude("favicon.*");
        matcher.AddInclude("icon-*.png");
        var shipped = matcher.GetResultsInFullPath(WwwRoot).Select(p => Path.GetRelativePath(WwwRoot, p).Replace('\\', '/')).ToList();

        var covered = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var row in manifest.Assets) covered.AddInclude(row.Glob);
        var coveredFiles = covered.GetResultsInFullPath(WwwRoot).Select(p => Path.GetRelativePath(WwwRoot, p).Replace('\\', '/')).ToHashSet();

        shipped.Where(f => !coveredFiles.Contains(f)).ShouldBeEmpty("files without a manifest row");
    }

    [Fact]
    public void EveryManifestRow_HasAnAllowedLicenceAndNoPlaceholders()
    {
        var manifest = LoadManifest();
        foreach (var row in manifest.Assets)
        {
            manifest.AllowedLicences.ShouldContain(row.Licence, $"{row.Glob} licence");
            new[] { row.Name, row.SourceUrl, row.LicenceStatedAt, row.CheckedOn }
                .ShouldAllBe(v => !string.IsNullOrWhiteSpace(v) && !v.StartsWith('<'), $"{row.Glob} has a placeholder");
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FitEdu.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("FitEdu.sln not found above test directory");
    }
}
```
`Microsoft.Extensions.FileSystemGlobbing` ships with the ASP.NET Core shared framework referenced by `FitEdu.Web.Tests` — if the compiler says otherwise, replace the two `Matcher` usages with `Directory.EnumerateFiles` + `FileSystemName.MatchesSimpleExpression` (System.IO.Enumeration), still no new package.

- [ ] **Step 3:** Run `dotnet test tests/FitEdu.Web.Tests --filter AssetManifestTests` → both FAIL (background.png/og-home.jpg have no row and placeholders exist). Add temporary rows for `images/background.png` and `images/og-home.jpg` with licence `"Public domain"`? **No** — their provenance is unknown; instead the executor completes Task B3/B4 (which delete/replace them) before running the suite, and this test is the gate. Mark the test `[Trait("Category","Assets")]` so it can be run alone.
- [ ] **Step 4:** Write `ASSET-MANIFEST.md` (table from DOWNLOAD-LOG + exclusion list from §3.3). Commit: `docs: add asset manifest and licence guard test`.

---

## 5. Phase B — Schema, exercise library and media pipeline

### Task B1: Exercise domain + migration

**Files:**
- Create: `src/FitEdu.Domain/Domain/Entities/Exercise.cs`, `LessonExercise.cs`; `src/FitEdu.Domain/Domain/Enums/CourseLevel.cs`, `FitnessGoal.cs`
- Modify: `Course.cs` (+6 fields), `Lesson.cs` (+`Exercises`), `Domain/Identity/ApplicationUser.cs` (+`Goal`), `Domain/Enums/UserStatus.cs` (rename members, keep ints), `Testimonial.cs` (+2 EL), `SubscriptionPlan.cs` (+`DescriptionEl`), `Web/Extensions/UserStatusLabels.cs` + `GlobalRes` (`StatusStudent→StatusBeginner`, `StatusGraduate→StatusIntermediate`, `StatusNEET→StatusAdvanced`, `Label_Info_Status` = "Training level" / "Επίπεδο προπόνησης")
- Create: `src/FitEdu.Infrastructure/Data/Configurations/ExerciseConfiguration.cs`, `LessonExerciseConfiguration.cs`; modify `CourseConfiguration.cs`, `TestimonialConfiguration.cs`, `SubscriptionPlanConfiguration.cs`
- Tests to update for the enum rename: `EnumValueTests.cs:64-71`, `AuthServiceTests.cs:222,225,245,248`, `AuthApiServiceTests.cs:119-130,142-145`, `ProfileIntegrationTests.cs:46,52` (ints unchanged, so `TokenServiceTests` stays)
- Modify: `ApplicationDbContext.cs` + `IApplicationDbContext.cs` (`DbSet<Exercise> Exercises`, `DbSet<LessonExercise> LessonExercises`)
- Migration: `src/FitEdu.Infrastructure/Data/Migrations/<ts>_FIT1_ExerciseLibraryAndCourseFitnessFields.cs`
- Tests: `tests/FitEdu.Infrastructure.Tests/ApplicationDbContextTests.cs` (+2 facts), `MigrationChainTests` (existing, must pass)

- [ ] **Step 1: failing test** — in `ApplicationDbContextTests.cs`:
```csharp
[Fact]
public async Task Exercise_SlugIsUnique()
{
    await using var db = DbContextFactory.CreateSqlite();
    db.Exercises.Add(new Exercise { Id = Guid.NewGuid(), Slug = "push-ups", NameEn = "Push-Ups", Category = "strength", Level = "beginner", PrimaryMuscles = "chest", SecondaryMuscles = "", InstructionsEn = "Lower.\nPush." });
    db.Exercises.Add(new Exercise { Id = Guid.NewGuid(), Slug = "push-ups", NameEn = "Dup", Category = "strength", Level = "beginner", PrimaryMuscles = "chest", SecondaryMuscles = "", InstructionsEn = "x" });
    await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
}
```
(`DbContextFactory.CreateSqlite` exists in `tests/FitEdu.TestSupport/DbContextFactory.cs`; use the name actually present — `CreateInMemory` does not enforce unique indexes, so use the SQLite factory.)
- [ ] **Step 2:** Run → compile error (`Exercise` missing).
- [ ] **Step 3: entities**
```csharp
namespace FitEdu.Domain.Entities;

/// <summary>One exercise from the public-domain Free Exercise DB, with optional Greek text.</summary>
public class Exercise : AuditableEntity
{
    public Guid Id { get; set; }
    public required string Slug { get; set; }
    public required string NameEn { get; set; }
    public string? NameEl { get; set; }
    public required string Category { get; set; }
    public required string Level { get; set; }
    public string? Force { get; set; }
    public string? Mechanic { get; set; }
    public string? Equipment { get; set; }
    public required string PrimaryMuscles { get; set; }
    public required string SecondaryMuscles { get; set; }
    public required string InstructionsEn { get; set; }
    public string? InstructionsEl { get; set; }
    public int ImageCount { get; set; } = 2;
    public ICollection<LessonExercise> LessonExercises { get; set; } = [];
}

/// <summary>Prescription of one exercise inside a lesson (sets, reps, rest).</summary>
public class LessonExercise
{
    public Guid Id { get; set; }
    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;
    public Guid ExerciseId { get; set; }
    public Exercise Exercise { get; set; } = null!;
    public int SortOrder { get; set; }
    public int? Sets { get; set; }
    public string? Reps { get; set; }
    public int? RestSeconds { get; set; }
    public string? NotesEn { get; set; }
    public string? NotesEl { get; set; }
}
```
Enums: `public enum CourseLevel { Beginner = 0, Intermediate = 1, Advanced = 2 }` and `public enum FitnessGoal { GeneralFitness = 0, Strength = 1, MuscleGain = 2, FatLoss = 3, Endurance = 4, Mobility = 5, Recovery = 6, Nutrition = 7 }`.
`Course` adds: `public CourseLevel Level { get; set; } = CourseLevel.Beginner; public FitnessGoal Goal { get; set; } = FitnessGoal.GeneralFitness; public string? Equipment { get; set; } public int? EstimatedWeeks { get; set; } public int? SessionsPerWeek { get; set; } public string? CoverExerciseSlug { get; set; }`. `Lesson` adds `public ICollection<LessonExercise> Exercises { get; set; } = [];`. `ApplicationUser` adds `public FitnessGoal? Goal { get; set; } public CourseLevel? ExperienceLevel { get; set; }`.
- [ ] **Step 4: EF configuration** (`ExerciseConfiguration`): key, `Slug` required max 120 + unique index, `NameEn` 150 required, `NameEl` 150, `Category` 40, `Level` 20, `Force`/`Mechanic` 20, `Equipment` 40, `PrimaryMuscles` 200, `SecondaryMuscles` 300, `InstructionsEn/El` 4000, index on `(Category, Level)`. `LessonExerciseConfiguration`: key; `Lesson` → `WithMany(l => l.Exercises)` cascade; `Exercise` → `WithMany(e => e.LessonExercises)` restrict; `Reps` 40; `NotesEn/El` 500; index `(LessonId, SortOrder)`. `CourseConfiguration`: `Equipment` 120, `CoverExerciseSlug` 120, index on `(Level, Goal)`.
- [ ] **Step 5:** `dotnet ef migrations add FIT1_ExerciseLibraryAndFitnessFields --project src/FitEdu.Infrastructure --startup-project src/FitEdu.Web`. Check `git status` shows no csproj / `Directory.Packages.props` rewrite (revert if it does).
- [ ] **Step 6:** `dotnet test tests/FitEdu.Infrastructure.Tests` → green incl. `MigrationChainTests`. Commit `feat: add Exercise library schema and course fitness fields`.

### Task B2: Exercise seed data + `ExerciseSeeder`

**Files:**
- Create: `src/FitEdu.Shared/JSON/Exercises/exercises.json` (trimmed copy of the DB), `exercises.el.json` (Greek overlay), `docs/assets/exercise-slugs.txt`
- Create: `src/FitEdu.Application/DTOs/Seed/ExerciseSeedDtos.cs`, `src/FitEdu.Infrastructure/Seeding/ExerciseSeeder.cs`, `src/FitEdu.Application/Common/SlugHelper.cs`
- Modify: `DatabaseSeedingExtensions.cs` (seed exercises **before** courses), `appsettings.Development.json` (`SeedData:JsonPaths:Exercises`)
- Tests: `tests/FitEdu.Infrastructure.Tests/SeederTests.cs` (+3), `tests/FitEdu.Application.Tests/SlugHelperTests.cs`

- [ ] **Step 1: SlugHelper test** — `SlugHelper.FromExerciseId("3_4_Sit-Up").ShouldBe("3-4-sit-up")`, `FromExerciseId("Alternate_Incline_Dumbbell_Curl").ShouldBe("alternate-incline-dumbbell-curl")`, `FromExerciseId("90/90 Hamstring").ShouldBe("90-90-hamstring")`.
- [ ] **Step 2:** implement: lower-case, replace every run of non `[a-z0-9]` with `-`, trim `-`.
- [ ] **Step 3: trim the DB** with a jq one-liner run from staging (keeps the public-domain data, drops nothing we use): `jq '[.[] | {id, name, force, level, mechanic, equipment, primaryMuscles, secondaryMuscles, instructions, category, images}]' dist/exercises.json > <repo>/src/FitEdu.Shared/JSON/Exercises/exercises.json` and `jq -r '.[].id' … | <SlugHelper rules via jq ascii_downcase/gsub> > docs/assets/exercise-slugs.txt`. Add header comment file `src/FitEdu.Shared/JSON/Exercises/README.md`: source URL, licence, date, "do not hand-edit exercises.json".
- [ ] **Step 4: seed DTO** `ExerciseSeedDto { Id, Name, Force?, Level, Mechanic?, Equipment?, PrimaryMuscles[], SecondaryMuscles[], Instructions[], Category, Images[] }` and `ExerciseGreekSeedDto { Slug, NameEl, InstructionsEl[] }`.
- [ ] **Step 5: seeder** (`ExerciseSeeder.SeedFromJsonAsync(db, folder, logger, ct)`): no-op if `db.Exercises.Any()`; read `exercises.json`, optional `exercises.el.json` keyed by slug; map with `SlugHelper`, join arrays with `", "` (muscles) and `"\n"` (instructions); `ImageCount = Images.Length`; add all, one `SaveChangesAsync`. Log count.
- [ ] **Step 6: SeederTests** — valid file seeds N rows with slug/muscles joined; second run is a no-op; Greek overlay fills `NameEl`. Use `TempDirWith(...)` helper already in the file.
- [ ] **Step 7:** wire in `DatabaseSeedingExtensions` right after blog seeding: `var exerciseJsonPath = config.GetValue<string>("SeedData:JsonPaths:Exercises") ?? Path.Combine(app.Environment.ContentRootPath, "..", "FitEdu.Shared", "JSON", "Exercises"); await ExerciseSeeder.SeedFromJsonAsync(db, exerciseJsonPath, startupLogger);` and add `"Exercises": "../FitEdu.Shared/JSON/Exercises"` to `appsettings.Development.json`. Also add the path to `tests/FitEdu.Web.Tests/CustomWebAppFactory.cs` / `SqliteWebAppFactory.cs` wherever the Courses path is set.
- [ ] **Step 8:** tests green; commit `feat: seed public-domain exercise library`.

### Task B3: Media pipeline — convert and import images, brand assets

**Files:**
- Create: `tools/assets/convert-exercise-images.mjs`, `tools/assets/render-brand-images.mjs`, `tools/assets/og-template.html`, `tools/assets/README.md`
- Create: `src/FitEdu.Web/wwwroot/favicon.svg`; regenerate `favicon.png`, `icon-192.png`, `images/og-home.jpg`; add `images/hero-home.webp`, `images/exercises/**`
- Delete: `images/background.png` (after Task D1 switches the hero)

- [ ] **Step 1: converter** (`convert-exercise-images.mjs`, run with `node tools/assets/convert-exercise-images.mjs <staging/extracted/free-exercise-db-main/exercises> <repo>/src/FitEdu.Web/wwwroot/images/exercises`):
```js
// Converts Free Exercise DB JPG pairs to 480px-wide WebP using Playwright's Chromium (no extra deps).
import { chromium } from '../../tests/e2e/node_modules/playwright/index.mjs';
import { readdirSync, mkdirSync, writeFileSync, existsSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const MAX_WIDTH = 480;
const QUALITY = 0.8;
const [sourceDir, targetDir] = process.argv.slice(2).map(p => resolve(p));
const toSlug = id => id.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');

const browser = await chromium.launch();
const page = await browser.newPage();
let converted = 0;
for (const exerciseId of readdirSync(sourceDir)) {
    const slug = toSlug(exerciseId);
    const outDir = join(targetDir, slug);
    mkdirSync(outDir, { recursive: true });
    for (const file of readdirSync(join(sourceDir, exerciseId)).filter(f => f.endsWith('.jpg'))) {
        const outPath = join(outDir, file.replace('.jpg', '.webp'));
        if (existsSync(outPath)) continue;
        const dataUrl = await page.evaluate(async ({ src, maxWidth, quality }) => {
            const img = new Image();
            img.src = src;
            await img.decode();
            const scale = Math.min(1, maxWidth / img.width);
            const canvas = document.createElement('canvas');
            canvas.width = Math.round(img.width * scale);
            canvas.height = Math.round(img.height * scale);
            canvas.getContext('2d').drawImage(img, 0, 0, canvas.width, canvas.height);
            return canvas.toDataURL('image/webp', quality);
        }, { src: pathToFileURL(join(sourceDir, exerciseId, file)).href, maxWidth: MAX_WIDTH, quality: QUALITY });
        writeFileSync(outPath, Buffer.from(dataUrl.split(',')[1], 'base64'));
        converted++;
    }
}
await browser.close();
console.log(`converted ${converted} images`);
```
Launch Chromium with `--allow-file-access-from-files` if `img.decode()` rejects file URLs: `chromium.launch({ args: ['--allow-file-access-from-files'] })`.
- [ ] **Step 2:** run it; verify ≈ 1 740 `.webp` files, total size (expect 20–35 MB), spot-check 3 slugs match `docs/assets/exercise-slugs.txt`.
- [ ] **Step 3: brand SVG** `favicon.svg` (viewBox 0 0 64 64: rounded square `#0E1116`, dumbbell = two `rect` plates `#C6F432` + bar `#2EC4B6`). `render-brand-images.mjs`: opens `favicon.svg` at 64 and 192 px → `favicon.png`, `icon-192.png` (`page.screenshot({ omitBackground: true })`), and `og-template.html` (1200×630, charcoal, lime headline "FitEdu", subline "Fitness courses & exercise library", a 3×2 duotone collage of `images/exercises/{push-ups,barbell-squat,kettlebell-swings,plank,burpee,pullups}/0.webp`) → `images/og-home.jpg` (`type: 'jpeg', quality: 85`).
- [ ] **Step 4: hero** — convert the Commons file to `images/hero-home.webp` (1920 px, q 0.8) with the same canvas approach (add a `--single <in> <out> <maxWidth>` mode to the converter). Fallback when no Commons file qualified: `render-brand-images.mjs --hero` renders a 1920×1080 collage of 8 duotone exercise frames → `hero-home.webp`.
- [ ] **Step 5: blog covers** — 4 duotone 1200×675 crops (`images/blog/<slug>.webp`) via the same renderer from exercise frames (`barbell-squat`, `dumbbell-bench-press`, `plank`, `jumping-jacks`).
- [ ] **Step 6:** fill the manifest placeholders from `DOWNLOAD-LOG.md`; `dotnet test --filter AssetManifestTests` → green only after `background.png` is removed (Task D1). Commit the images in **two commits** (`assets: import exercise library images (public domain)` and `assets: brand and hero images`) to keep diffs reviewable. Write `tools/assets/README.md` (how to re-run; sources; licences).

### Task B4: Course seed schema extension + lesson exercises

**Files:**
- Modify: `CourseSeedDtos.cs` (+`Level`, `Goal`, `Equipment`, `EstimatedWeeks`, `SessionsPerWeek`, `CoverExerciseSlug` on course; `Exercises` list on lesson), `CourseSeeder.cs` (resolve slugs), `SeederTests.cs`

- [ ] **Step 1: failing test** — sample JSON with `"level":"Intermediate","goal":"FatLoss","equipment":"none","coverExerciseSlug":"push-ups"` and a lesson `"exercises":[{"slug":"push-ups","sets":3,"reps":"10","restSeconds":60,"notes":"Elbows 45°"}]`; seed an `Exercise{Slug="push-ups"}` first; assert `course.Level == CourseLevel.Intermediate`, lesson has 1 `LessonExercise` with `Sets == 3`; a second test asserts an **unknown slug throws `InvalidOperationException`** naming the slug (fail loudly: content integrity).
- [ ] **Step 2:** `LessonExerciseSeedDto { Slug, Sets?, Reps?, RestSeconds?, Notes?, NotesEl? }`; `CourseSeedDto` gets `string Level = "Beginner"`, `string Goal = "GeneralFitness"`, `string? Equipment`, `int? EstimatedWeeks`, `int? SessionsPerWeek`, `string? CoverExerciseSlug`, `string? CategoryEl`; `LessonSeedDto.Exercises = []`. `AssessmentSeedDto` gets `CategoryEl` too. `CategorySeedHelper.ResolveCategoryId(db, cache, name, nameEl)` sets `NameEl` when creating and back-fills it when the cached row has none (test: `CategorySeedHelper_SetsGreekName`).
- [ ] **Step 3:** `CourseSeeder.SeedFromJsonAsync` loads `var exercisesBySlug = await db.Exercises.ToDictionaryAsync(e => e.Slug, ct)` once; `MapToLesson(dto, exercisesBySlug)` builds `LessonExercise` rows with `SortOrder` = index+1; unknown slug → `throw new InvalidOperationException($"Lesson '{dto.Title}' references unknown exercise slug '{x.Slug}'")`. Enum parse with `Enum.TryParse(ignoreCase: true)` falling back to defaults like `RequiredTier` does today.
- [ ] **Step 4:** tests green; commit `feat: course seed supports fitness fields and lesson exercises`.

### Task B5: Exercise read API, consumer and lesson DTO

**Files:**
- Create: `src/FitEdu.Application/DTOs/Exercises/ExerciseDtos.cs` (`ExerciseListItemDto(Guid Id, string Slug, string Name, string Category, string Level, string? Equipment, string[] PrimaryMuscles, string Image0Url, string Image1Url)`, `ExerciseDetailDto(… + string[] SecondaryMuscles, string? Force, string? Mechanic, string[] Instructions)`, `ExerciseFilterDto(string? Search, string? Muscle, string? Equipment, string? Level, string? Category)`, `LessonExerciseDto(Guid ExerciseId, string Slug, string Name, string? Equipment, string[] PrimaryMuscles, int? Sets, string? Reps, int? RestSeconds, string? Notes, string Image0Url, string Image1Url, string[] Instructions)`)
- Create: `ApiInterfaces/IExerciseService.cs`, `ApiServices/ExerciseService.cs`, `Mappings/ExerciseMappings.cs` (expression + materialised twin, `Localized` for NameEl/InstructionsEl), `Web/Controllers/ExercisesController.cs` (`GET /api/exercises?page&pageSize&search&muscle&equipment&level&category`, `GET /api/exercises/{slug}`, `GET /api/exercises/facets` → distinct muscles/equipment/levels with counts), `Web/Interfaces/IExerciseConsumer.cs`, `Web/Consumers/ExerciseConsumer.cs`; register both in `ServiceRegistrationExtensions` next to `ICourseService`/`ICourseConsumer`
- Modify: `LessonDetailDto` (append `IReadOnlyList<LessonExerciseDto>? Exercises = null`), `CourseService` lesson query (`Include(l => l.Exercises).ThenInclude(x => x.Exercise)`, ordered by `SortOrder`), `CourseListItemDto` + `CourseDetailDto` (append `CourseLevel Level = default, FitnessGoal Goal = default, string? Equipment = null, int? EstimatedWeeks = null, int? SessionsPerWeek = null, string? CoverImageUrl = null`) and their projections in `CourseService` / `Mappings`
- Tests: `tests/FitEdu.Application.Tests/ExerciseServiceTests.cs` (list filters by muscle/equipment/level/search + paging; detail by slug; facets), `CourseServiceTests.cs` (+ lesson includes exercises in order; course DTO carries level/goal/cover URL), `tests/FitEdu.Web.Tests/ExercisesControllerTests.cs` (anonymous GET allowed — the library is public; 404 unknown slug)

Image URL rule (single helper `ExerciseImagePaths.Url(slug, index) => $"/images/exercises/{slug}/{index}.webp"` in `FitEdu.Application/Common`). Course cover URL = `CoverExerciseSlug is null ? null : ExerciseImagePaths.Url(slug, 0)`.

Also in this task: `SiteSearchService` fallback adds Exercises (`NameEn/NameEl` contains → `exercises/{slug}`, new `SearchResultType.Exercise`), `SiteSearchBox.razor.cs:83-89` icon map gets `bi-activity`; `SiteSearchServiceTests` + 2 facts. `robots.txt`: allow `/exercises`; sitemap generator adds `/exercises` and `/exercises/{slug}`.

- [ ] Steps: write the three test files first (red), implement service → controller → consumer, `dotnet test` green, `dotnet format style`, commit `feat: exercise library API and lesson exercise DTOs`.

### Task B6: Admin — course fitness fields and lesson exercise picker

**Files:** `Pages/AdminCourseEdit.razor(.cs)` (+ selects for Level/Goal, inputs Equipment/EstimatedWeeks/SessionsPerWeek/CoverExerciseSlug with slug validation message), `Shared/Components/Forms/LessonEditorModal.razor(.cs)` (+ exercise rows: search box calling `IExerciseConsumer.ListAsync(search)`, add/remove/reorder, sets/reps/rest/notes EN+EL), `DTOs/AdminDtos.cs` (`AdminLessonDto` + `List<LessonExerciseEditDto>`; `SaveCourseRequest` + fields), `AdminCourseService` save paths (replace lesson exercises wholesale), `AdminRes` keys (Level, Goal, Equipment, EstimatedWeeks, SessionsPerWeek, CoverExercise, Exercises, AddExercise, Sets, Reps, Rest, Notes + EL), tests `AdminCourseServiceTests` (+ save with exercises replaces previous rows; invalid slug → validation error).

- [ ] Steps: red tests → implement → green → commit `feat: admin can edit fitness fields and lesson exercises`.

---

## 6. Phase C — Content (parallelisable: each task touches disjoint files)

### Task C1: Content-integrity tests (write first; they gate every content task)

**File:** `tests/FitEdu.Infrastructure.Tests/SeedContentIntegrityTests.cs`

- [ ] Facts (all read the real JSON under `src/FitEdu.Shared/JSON` via the repo-root finder from Task A2):
  1. `Courses_AreExactlyNineteen_AndSlugsMatchFileNames` (file name = kebab of title? no — assert count 19 and every file has `title`, `titleEl`, `description`, `descriptionEl`, `category`, `level`, `goal`).
  2. `Courses_EveryLessonHasGreekTitleAndContentOrExercises` (`titleEl` non-empty; `contentEl` non-empty whenever `content` is; every exercise entry has `notesEl` whenever `notes` is).
  3. `Courses_EveryExerciseSlugExistsInExerciseLibrary` (slugs ⊆ `exercises.json` ids mapped by `SlugHelper`) and `CoverExerciseSlug` likewise.
  4. `Courses_ReferencedExercisesHaveImagesOnDisk` (`wwwroot/images/exercises/<slug>/0.webp` and `1.webp` exist).
  5. `Courses_ReferencedExercisesHaveGreekOverlay` (`exercises.el.json` contains every slug referenced by any course).
  6. `Courses_NoCareerVocabularyRemains` — no file (courses, assessments, blog seed, resx) contains, case-insensitively: `career`, `job`, `interview`, `résumé|resume`, `salary`, `linkedin`, `psychosocial`, `counsel`.
  7. `Assessments_AreExactlyNineteen_AndBilingual` (count 19; keys match file names; every question has `labelEl`; rating questions have `minLabelEl`/`maxLabelEl`).
  8. `Resx_EveryEnglishKeyHasGreekValue` for every `*.resx`/`*.el.resx` pair under `src/FitEdu.Shared/Resources` (parse with `XDocument`, compare `data/@name` sets; a Greek value equal to the English one is allowed only when it contains no letters — numbers/symbols).
- [ ] Run → facts 1–7 red (old content), fact 8 may be green. Commit `test: seed content integrity gates`.

### Task C2: Course template + 19 course JSON files (one subagent per course, parallel)

**Files:** `src/FitEdu.Shared/JSON/Courses/<slug>.json` ×19 (old 19 deleted in the first commit of this task).

- [ ] **Template** (copy exactly; replace values) — `bodyweight-basics.json`, module 1 of 2 shown:
```json
{
  "title": "Bodyweight Basics",
  "titleEl": "Βασικές Ασκήσεις με Βάρος Σώματος",
  "description": "Build strength, control and confidence with no equipment: push, pull, squat, hinge and core patterns taught step by step. Not medical advice — consult a professional before starting if you have a health condition.",
  "descriptionEl": "Χτίσε δύναμη, έλεγχο και αυτοπεποίθηση χωρίς εξοπλισμό: τα μοτίβα ώθησης, έλξης, καθίσματος, κάμψης ισχίου και κορμού βήμα-βήμα. Δεν αποτελεί ιατρική συμβουλή — συμβουλέψου επαγγελματία πριν ξεκινήσεις αν έχεις κάποια πάθηση.",
  "requiredTier": "Free",
  "isPublished": true,
  "category": "Strength",
  "level": "Beginner",
  "goal": "GeneralFitness",
  "equipment": "none",
  "estimatedWeeks": 4,
  "sessionsPerWeek": 3,
  "coverExerciseSlug": "push-ups",
  "modules": [
    {
      "title": "Week 1–2: Learn the Patterns",
      "titleEl": "Εβδομάδες 1–2: Μάθε τα Μοτίβα",
      "description": "Technique first. Short sessions that teach the five movement patterns.",
      "descriptionEl": "Πρώτα η τεχνική. Σύντομες προπονήσεις που διδάσκουν τα πέντε μοτίβα κίνησης.",
      "sortOrder": 1,
      "lessons": [
        {
          "title": "The Push-Up, Done Right",
          "titleEl": "Το Σωστό Push-Up",
          "durationMinutes": 10,
          "sortOrder": 1,
          "content": "# The Push-Up, Done Right\n\nThe push-up is a horizontal pushing pattern. The chest (pectoralis major) and the front of the shoulders (anterior deltoid) move the arms, the triceps straighten the elbows, and the core keeps the body in one line.\n\n## Set-up\n\n- Hands slightly wider than shoulders, fingers forward\n- Body in a straight line from head to heels\n- Squeeze glutes and brace the core before you move\n\n## Common errors\n\n1. Hips sagging — brace harder or regress to incline push-ups\n2. Elbows flaring to 90° — keep them at roughly 45°\n3. Head dropping — look at a point just ahead of your hands\n\n## Progressions\n\n- Easier: hands on a bench or wall\n- Harder: feet elevated, slow 3-second lowering\n\n## Safety\n\n- Stop if you feel sharp wrist or shoulder pain\n- Keep the range you can control; depth grows with practice\n- Rest at least a day between push sessions this week",
          "contentEl": "# Το Σωστό Push-Up\n\nΤο push-up είναι ένα οριζόντιο μοτίβο ώθησης. Το στήθος (μείζων θωρακικός) και το μπροστινό μέρος του ώμου (πρόσθιος δελτοειδής) κινούν τα χέρια, οι τρικέφαλοι τεντώνουν τους αγκώνες και ο κορμός κρατά το σώμα σε μία ευθεία.\n\n## Τοποθέτηση\n\n- Παλάμες λίγο πιο ανοιχτά από τους ώμους, δάχτυλα μπροστά\n- Σώμα σε ευθεία γραμμή από το κεφάλι ως τις φτέρνες\n- Σφίξε γλουτούς και κορμό πριν κινηθείς\n\n## Συνηθισμένα λάθη\n\n1. Λεκάνη που «πέφτει» — σφίξε περισσότερο ή κάνε push-ups σε κλίση\n2. Αγκώνες ανοιχτοί στις 90° — κράτα τους περίπου στις 45°\n3. Κεφάλι που κρέμεται — κοίτα ένα σημείο λίγο μπροστά από τα χέρια\n\n## Προοδευτικότητα\n\n- Ευκολότερο: χέρια σε πάγκο ή τοίχο\n- Δυσκολότερο: πόδια ανυψωμένα, αργό κατέβασμα 3 δευτερολέπτων\n\n## Ασφάλεια\n\n- Σταμάτα αν νιώσεις οξύ πόνο σε καρπό ή ώμο\n- Κράτα το εύρος που ελέγχεις· το βάθος μεγαλώνει με την εξάσκηση\n- Ξεκούραση τουλάχιστον μία μέρα ανάμεσα στις προπονήσεις ώθησης αυτή την εβδομάδα",
          "exercises": [
            { "slug": "push-ups", "sets": 3, "reps": "6–10", "restSeconds": 60, "notes": "Elbows at 45°, body in one line.", "notesEl": "Αγκώνες στις 45°, σώμα σε μία ευθεία." }
          ]
        }
      ]
    }
  ]
}
```
- [ ] Per course: 2–3 modules, 4–8 lessons; workout lessons list 4–8 exercises; technique lessons list 1–2; knowledge lessons list none. Slugs must come from `docs/assets/exercise-slugs.txt`. Greek text written by the same subagent (fluent, not transliterated). Run `dotnet test --filter SeedContentIntegrityTests` after each file.
- [ ] Add `exercises.el.json` rows (`slug`, `nameEl`, `instructionsEl[]`) for every exercise the course references (Task C3 merges the 19 fragments — each course subagent writes `src/FitEdu.Shared/JSON/Exercises/el/<course-slug>.el.json`; C3 merges them into `exercises.el.json` with a jq `add`).
- [ ] Commit per course: `content: <slug> course (EN/EL)`.

### Task C3: Greek exercise overlay merge + seeder support for a folder of fragments
- [ ] `ExerciseSeeder` reads `exercises.el.json` **and** every `el/*.el.json` (so fragments need no merge step; remove the merge idea from C2) — update `SeederTests` accordingly. Commit.

### Task C4: 19 assessment JSON files (parallel, one per subagent)
- [ ] Template = existing `wellbeing_check_v1.json` structure with new key/title/sections; **ensure `SchemaJson` is a serialised string**, `id` equals file key, `version` "1.0". Example section for `training_readiness_v1`: "Current activity" (rating 1–5 "How many days per week did you exercise in the last month?" with EL), "Health screening" (multiselect: known joint pain / dizziness during exertion / medication affecting heart rate / none — with EL), "Goals" (choice: lose fat / build muscle / move better / run further / general health), "Notes" (text, optional). Delete the 19 old files in the first commit of this task. Commit per file.

### Task C5: Learning-path, testimonial and live-class seeders
**Files:** `Seeding/LearningPathSeeder.cs`, `Seeding/ShowcaseSeeder.cs`, `src/FitEdu.Shared/JSON/Paths/paths.json`, `JSON/Showcase/testimonials.json`, `JSON/Showcase/live-classes.json`, `DatabaseSeedingExtensions.cs` (after students: paths → showcase), `SeederTests.cs` (+4).
- [ ] Paths JSON: `[{ "title","titleEl","description","descriptionEl","category","categoryEl","displayOrder","courseTitles":[…] }]`; seeder resolves `CourseId` by `TitleEn`, throws on unknown title (integrity). Testimonials JSON: `fullName, roleOrTitle, roleOrTitleEl, companyOrContext, quoteText, quoteTextEl` (EL columns added in B1); `TestimonialDto` gains `QuoteTextEl`/`RoleOrTitleEl` appended with defaults, `TestimonialConsumer.GetActiveAsync(lang)` passes `?lang=` like `CourseConsumer`, `TestimonialService` picks via `Localized.Pick`. Live classes JSON: `title, titleEl, courseTitle?, weekdayOffset, hourUtc, durationMinutes, maxParticipants`; host = admin user (look up by `AdminUser:Email`). Both seed-once; both seeders are **skipped in test hosts** the same way the JSON seeders are (relative path), so no xUnit fixture changes.
- [ ] Commit `feat: seed learning paths, testimonials and live classes`.

### Task C6: Blog, plans, students check
- [ ] Replace the 4 records in `BlogSeedData.cs` (fields unchanged; EN + EL bodies; unique slugs — SQLite test hosts enforce `IX_BlogArticles_Slug`; `CoverImageUrl` = `/images/blog/<slug>.webp`). `SeederTests:61-73` only needs the list non-empty. Update `SubscriptionPlanSeeder` descriptions (EN + new `DescriptionEl`): Free "Bodyweight and mobility courses, exercise library, 1 active course" / Plus "All courses and assessments, 10 active courses" / Pro "Everything, certificates, live classes and priority coaching" (+ EL); keep tiers/prices/flags (`CustomWebAppFactory.cs:159`, `CertificatesIntegrationTests`, `AssessmentsIntegrationTests` depend on them). `Pricing.razor` feature rows re-labelled via `PricingRes`. Confirm `students.json` untouched. Commit.

### Task C7: Resources (resx EN/EL) sweep
- [ ] Apply Appendix A (every key, new EN and EL value). Add new keys for the new UI (NavMenuRes: `Exercises`, `Profile`; CourseRes: `Level`, `Goal`, `Equipment`, `Weeks`, `SessionsPerWeek`, `Beginner/Intermediate/Advanced`, goal names ×8, equipment names ×10; LessonRes: `Exercises`, `Sets`, `Reps`, `Rest`, `ShowInstructions`, `Start/EndPosition`; new `ExerciseRes` family: `PageTitle`, `Library`, `SearchPlaceholder`, `Muscle`, `Equipment`, `Level`, `Category`, `AllMuscles`, `Instructions`, `PrimaryMuscles`, `SecondaryMuscles`, `UsedInLessons`, `NoResults`; ProfileRes: `Goal`, `ExperienceLevel`, `WorkoutsCompleted`, `MinutesTrained`, `CurrentStreak`; HomeRes: full rewrite). Three edits per family (`.resx`, `.el.resx`, hand-edited `.Designer.cs` in the existing style). `SeedContentIntegrityTests.Resx_EveryEnglishKeyHasGreekValue` green. Commit per resource family.

### Task C8: Assistant, certificate, email, SEO, legal
- [ ] `AssistantService.cs:170-195` prompt (replace the career sentences, **keep** the language name "Greek"/"English", the tier words and the exact phrase "Use your tools" — `AssistantServiceTests.cs:120-121,226,247` assert them): "You are FitEdu Coach, a helpful guide embedded in FitEdu, an online fitness-courses platform with an exercise library. Help members find courses, understand exercises (muscles, equipment, technique cues), plan their week and stay consistent. You are not a medical professional: for pain, injury, pregnancy, heart or other conditions, tell the member to consult a doctor or physiotherapist before training. Never prescribe diets or supplements beyond general, evidence-based guidance." Add tool `search_exercises(query, muscle?, equipment?, maxResults?)` in `AssistantTools` (same AsNoTracking / 5-hit cap pattern as `search_courses`) using `IExerciseService`; `AssistantIndexingService.BuildLessonSource` appends the lesson's exercise names + primary muscles so semantic search finds workouts; `AssistantRes.Title` "FitEdu Coach" / "Προπονητής FitEdu", `Disclaimer` "AI-generated — not medical or professional advice." / EL; suggested questions → "Which course should I start with as a beginner?", "Show me exercises for my back", "How do I progress my squat?". Update `AssistantToolsTests` + `AssistantServiceTests`.
- [ ] `CertificateService.cs`: texts + colours per §3.5; `CertificateServiceTests` assertions updated.
- [ ] `SmtpEmailService.cs` / `StubEmailService.cs` copy + their tests.
- [ ] `SeoHead.razor` default description/site name; `Home.razor` canonical/OG; sitemap entries add `/exercises`.
- [ ] `LegalRes` + `Privacy.razor`/`Terms.razor` structure unchanged, text rewritten.
- [ ] Commit per file group.

---

## 7. Phase D — UI (D1–D2 serial; D3–D12 parallel in worktrees, one page family each)

### Task D1: Design-system primitives, nav, bottom nav, loader, brand
**Files:** `wwwroot/css/app.css` (+primitives, pulse boot loader, remove compass CSS), `wwwroot/css/shared-components.css` (chip/card/stat/sticky-bar), `Layout/NavMenu.razor(.cs,.css)` (logo, glass bar, hide student links `< 768px`), `Layout/BottomNav.razor(.cs,.css)` (new), `Layout/MainLayout.razor`, `Shared/Components/Layout/LoadingSpinner.razor(.css)`, `App.razor` (favicon.svg link, boot loader markup, `theme-color` meta `#0E1116`, `<link rel="manifest">` only if one exists), `Pages/Home.razor.css` hero → `hero-home.webp`; delete `background.png`.
- [ ] CSS primitives (add to `app.css` after `:root`):
```css
:root {
    --fs-1: clamp(2rem, 1.2rem + 3.5vw, 3.5rem);
    --fs-2: clamp(1.5rem, 1.1rem + 2vw, 2.25rem);
    --fs-3: clamp(1.25rem, 1rem + 1vw, 1.5rem);
    --fs-body: clamp(1rem, 0.95rem + 0.25vw, 1.0625rem);
    --nav-height: 3.75rem;
    --bottom-nav-height: 4rem;
    --tap-min: 44px;
}
.page { width: min(100%, 1200px); margin-inline: auto; padding-inline: clamp(1rem, 4vw, 2rem); }
.grid-auto { display: grid; gap: var(--space-4); grid-template-columns: repeat(auto-fill, minmax(min(100%, 17rem), 1fr)); }
.card { background: var(--bg-surface); border: 1px solid var(--border-subtle); border-radius: var(--radius-lg); padding: var(--space-4); transition: transform var(--duration-base) var(--ease-out), border-color var(--duration-base) var(--ease-out); }
.card:hover { transform: translateY(-2px); border-color: rgba(var(--text-strong-accent-rgb), 0.5); }
.chip { display: inline-flex; align-items: center; gap: 0.35rem; min-height: 1.75rem; padding: 0.15rem 0.65rem; border-radius: var(--radius-pill); font-size: 0.8125rem; background: var(--bg-neutral-subtle); color: var(--text-light-normal); }
.chip--lime { background: rgba(var(--text-strong-accent-rgb), 0.15); color: var(--text-strong-accent); }
.chip--coral { background: rgba(255, 107, 61, 0.15); color: var(--color-attention); }
.chip--teal { background: var(--bg-info-subtle); color: #2EC4B6; }
.stat-tile { display: grid; gap: 0.25rem; padding: var(--space-4); background: var(--bg-surface); border-radius: var(--radius-lg); }
.stat-tile__value { font-size: var(--fs-2); font-weight: 700; color: var(--text-strong-accent); line-height: 1; }
.stat-tile__label { font-size: 0.8125rem; color: var(--text-light-muted); }
.duotone { position: relative; overflow: hidden; background: var(--bg-table-header); }
.duotone > img { width: 100%; height: 100%; object-fit: cover; filter: grayscale(1) contrast(1.15); mix-blend-mode: luminosity; opacity: 0.9; }
.duotone::after { content: ""; position: absolute; inset: 0; background: linear-gradient(160deg, rgba(var(--text-strong-accent-rgb), 0.35), rgba(var(--bg-primary-rgb), 0.85)); }
.sticky-bottom-bar { position: sticky; bottom: calc(var(--bottom-nav-height) + env(safe-area-inset-bottom)); z-index: 5; display: flex; gap: var(--space-3); padding: var(--space-3); background: rgba(var(--bg-surface-rgb), 0.92); backdrop-filter: blur(10px); border-top: 1px solid var(--border-subtle); }
@media (min-width: 768px) { .sticky-bottom-bar { position: static; background: transparent; border: 0; padding: 0; } }
@media (prefers-reduced-motion: reduce) { *, *::before, *::after { animation-duration: 0.01ms !important; transition-duration: 0.01ms !important; } }
```
- [ ] `BottomNav.razor` (markup; role lists via `AuthorizeView` like NavMenu; each `NavLink` has `aria-label`, icon `bi-*`, label text; `.bottom-nav { position: fixed; inset-inline: 0; bottom: 0; height: calc(var(--bottom-nav-height) + env(safe-area-inset-bottom)); padding-bottom: env(safe-area-inset-bottom); display: grid; grid-auto-flow: column; background: rgba(var(--bg-surface-rgb), 0.92); backdrop-filter: blur(12px); border-top: 1px solid var(--border-subtle); z-index: 1030; } .bottom-nav a { min-height: var(--tap-min); display: grid; place-items: center; font-size: 0.7rem; color: var(--text-light-normal); } .bottom-nav a.active { color: var(--text-strong-accent); } @media (min-width: 768px) { .bottom-nav { display: none; } }`). `main.content { padding-bottom: calc(var(--bottom-nav-height) + env(safe-area-inset-bottom) + 1rem); } @media (min-width: 768px) { main.content { padding-bottom: 2rem; } }`.
- [ ] Pulse loader SVG (`LoadingSpinner.razor`, same parameters): `<svg viewBox="0 0 120 40"><path class="pulse-loader__track" d="M0 20 H120"/><path class="pulse-loader__line" d="M0 20 H30 L38 20 L44 6 L52 34 L58 20 H120" pathLength="100"/></svg>` with CSS `stroke-dasharray: 100; stroke-dashoffset: 100; animation: pulse-draw 1.2s linear infinite;` `@keyframes pulse-draw { to { stroke-dashoffset: -100; } }`, lime stroke, teal track. Same markup inline in `App.razor` boot loader with `.pulse-boot__*` classes in `app.css`. Grep tests for `compass` and update selectors.
- [ ] Also in D1: delete the dead `.compass-boot*` CSS (`app.css:190-284`, reduced-motion/`tab-hidden` selectors 419-426, 442-446) and the keyframes once the spinner no longer uses them; `LessonViewerTests.cs:69` `.compass-spinner` → `.pulse-loader`; header trailing block: `SiteSearchBox` collapses to an icon button that expands a full-width flyout below 576 px (`.site-search-flyout { left: 0; right: 0; min-width: 0 }`), `NotificationBell` panel `right: 0; max-width: calc(100vw - 1.5rem)`; fix `.impersonation-banner` by moving its rule from `MainLayout.razor.css` to a new `ImpersonationBanner.razor.css`; `main.content` becomes full-width with `.page` applied per page so the hero can bleed; `Program.cs` `UseStaticFiles(new StaticFileOptions { OnPrepareResponse = ctx => { if (ctx.Context.Request.Path.StartsWithSegments("/images")) ctx.Context.Response.Headers.CacheControl = "public,max-age=604800"; } })` (PERF-12); `App.razor` adds `<meta name="theme-color" content="#0E1116">`, `<link rel="icon" type="image/svg+xml" href="favicon.svg">`, `<link rel="apple-touch-icon" href="icon-192.png">`, `<link rel="manifest" href="manifest.webmanifest">` with a new `wwwroot/manifest.webmanifest` (name FitEdu, display standalone, theme/background `#0E1116`, icon 192) — add a manifest row for it (`"MIT (own work)"`), `<link rel="preload" as="image" href="images/hero-home.webp" fetchpriority="high">` only on Home via `HeadContent`; replace the two Font Awesome eye icons in `Login.razor.cs`/`Register.razor.cs` with `bi-eye`/`bi-eye-slash` and remove the Font Awesome `<link>` from `App.razor`; replace emoji icons (📋 🔒 ✓ ▶ 📄 ⏱ 💬) with `bi-*` glyphs; `AssistantWidget` launcher moves up on mobile (`bottom: calc(var(--bottom-nav-height) + 1rem)`), `IncomingCallToast` likewise.
- [ ] Verify: `dotnet build`, `dotnet test`, open at 375 px and 1280 px (browser tool), screenshot. Commit `ui: design primitives, bottom nav, pulse loader, brand`.

### Task D2: Fitness components
**Files (new):** `Shared/Components/Fitness/ExerciseMedia.razor(.css)`, `ExerciseCard.razor(.css)`, `ProgressRing.razor(.css)`, `StatTile.razor`, `LevelBadge.razor`, `MuscleChips.razor`, `CourseCover.razor(.css)`; tests `tests/FitEdu.Web.Tests/FitnessComponentTests.cs` (bUnit is **not** referenced today — check `ChatComponentTests.cs` for the harness used; if it renders through `TestContext` from bUnit, reuse; otherwise test the pure helpers only).
- [ ] `ExerciseMedia.razor`:
```razor
@* Two-frame exercise animation: start (0) and end (1) position crossfade. Tap/click toggles; reduced motion shows both frames side by side. *@
<div class="exercise-media @(_paused ? "exercise-media--paused" : "")" @onclick="Toggle" role="img" aria-label="@AltText">
    <img class="exercise-media__frame exercise-media__frame--start" src="@StartUrl" alt="" loading="lazy" decoding="async" />
    <img class="exercise-media__frame exercise-media__frame--end" src="@EndUrl" alt="" loading="lazy" decoding="async" />
</div>
@code {
    [Parameter, EditorRequired] public string StartUrl { get; set; } = string.Empty;
    [Parameter, EditorRequired] public string EndUrl { get; set; } = string.Empty;
    [Parameter] public string AltText { get; set; } = string.Empty;
    private bool _paused;
    private void Toggle() => _paused = !_paused;
}
```
CSS: `.exercise-media { position: relative; aspect-ratio: 4 / 3; background: #fff; border-radius: var(--radius-md); overflow: hidden; } .exercise-media__frame { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: contain; } .exercise-media__frame--end { animation: exercise-crossfade 2.4s ease-in-out infinite; } @keyframes exercise-crossfade { 0%, 40% { opacity: 0; } 50%, 90% { opacity: 1; } 100% { opacity: 0; } } .exercise-media--paused .exercise-media__frame--end { animation-play-state: paused; } @media (prefers-reduced-motion: reduce) { .exercise-media { display: grid; grid-template-columns: 1fr 1fr; aspect-ratio: 8 / 3; } .exercise-media__frame { position: static; animation: none; } }`
- [ ] `ExerciseCard` composes `ExerciseMedia` + name + `MuscleChips` + `LevelBadge` + sets×reps + rest + expandable instructions (`<details>`). `ProgressRing` = SVG circle with `stroke-dasharray` from `Percent`. `CourseCover` = `.duotone` image or category icon (`bi-*` map by category name) fallback.
- [ ] Commit `ui: fitness components`.

### Tasks D3–D12 (parallel, one worktree each; shared primitives from D1/D2 only)

Content cards use `.card-surface` (with `.card-surface__body` for the padded text), not `.card`.
| Task | Files | Spec |
|---|---|---|
| D3 Home | `Pages/Home.razor(.cs,.css)` | Guest: hero (`hero-home.webp` as `<img>` with `fetchpriority="high"` + overlay, `--fs-1` headline from HomeRes, two CTAs: Start free / Browse exercises), stats strip (`StatTile` ×4 from `/api/courses` count, exercise count via consumer, lessons, members), "Pick your goal" cards (8 goals → `/courses?goal=`), "How it works" 3 steps, featured courses (6, `CourseCover`), testimonials (existing; add the missing `.testimonials-grid` CSS; replace the CSP-blocked inline `onerror` with Blazor `@onerror` + a bool), blog (existing; cover `<img>` gets `width`/`height`/`loading="lazy"`), CTA. Student: "Today's plan" card (first incomplete lesson of the most recent enrolment — reuse `ICourseConsumer` lesson data), stats (`WorkoutsCompleted` = completions count, `MinutesTrained` = sum of completed `DurationMinutes`, `CurrentStreak` = consecutive days with a completion; computed in a new `ProfileService.GetTrainingStatsAsync` → `ProfileDtos.TrainingStatsDto`, unit-tested with fixed `DateTimeOffset`s in UTC), recommended courses (filter by user goal/level). Remove the career "problem/method/story" sections and their CSS; remove the duplicated inline hero style in `Home.razor.cs:36-39`. |
| D4 Courses | `Pages/Courses.razor(.cs,.css)`, `CategoryFilterBar` (+optional second chip row via `RenderFragment ExtraFilters`) | goal/level/equipment chips (query string `goal`, `level`, `equipment` → `ICourseConsumer.ListAsync` extended; `CoursesController` filters), cover cards with `LevelBadge`, weeks×sessions, lessons, rating; keep `.course-card` + `a.course-card-link`. |
| D5 CourseDetail | `Pages/CourseDetail.razor(.cs,.css)` | cover hero, chips row, outcomes list (first 4 module descriptions), module accordion (`<details>` per module, lesson rows with kind icon + minutes + exercise count), sticky enrol/continue CTA (`.sticky-bottom-bar`), reviews unchanged. |
| D6 LessonViewer | `Pages/LessonViewer.razor(.cs,.css)` | header breadcrumb (`flex-wrap`), progress bar (completed/total), intro markdown, `ExerciseCard` list when `Exercises` non-empty, video/PDF branches unchanged (drop the CSP-blocked `oncontextmenu`), `.text-content :is(img, table, pre, iframe) { max-width: 100% }`, sticky "Mark complete" bar + prev/next; keep selectors `.module-name`, `.btn-success`, `.completed-indicator`, `.progress-update`, `.lesson-actions .text-danger`; `LessonViewerTests` extended for exercises render. |
| D7 Exercises + ExerciseDetail | `Pages/Exercises.razor(.cs,.css)`, `Pages/ExerciseDetail.razor(.cs,.css)` (new, anonymous), `Routes.razor` untouched (routes via `@page`) | library grid (`grid-auto`, `ExerciseCard` compact), filter chips (facets endpoint), search debounce (copy the pattern from `Courses.razor.cs`), `PaginationNav`; detail = large `ExerciseMedia`, muscles, equipment, level/force/mechanic, instructions `<ol>`, "Used in lessons" list (new service method `GetLessonsUsingExerciseAsync`). e2e: `pages.spec.mjs` + "exercises page renders library cards" (`.exercise-card`). |
| D8 Paths | `Pages/Paths.razor(.css)`, `PathDetail.razor(.css)` | timeline (vertical on mobile, horizontal ≥ 992 px), `ProgressRing` per path. |
| D9 Assessments | `Pages/Assessments.razor(.css)`, `AssessmentForm.razor(.cs,.css)`, `AssessmentHistory.razor`, `Shared/Components/Layout/UpgradePrompt.razor(.css)` | cards with category icon + ring; header row wraps; form = one section per step on mobile with "Next section" button (sections already exist in schema), rating scale as a wrapping `flex` group of ≥ 44 px buttons (fixes the horizontal overflow), summary bar; style `.assessment-description` and `.upgrade-prompt`. Bootstrap `bg-primary text-white` header in `AssessmentHistory` → palette classes. |
| D10 Profile + Pricing + Billing + SubscriptionSuccess | `Pages/Profile.razor(.cs,.css)`, `Pricing.razor(.css)`, `Billing.razor(.css)` | profile: goal/level selects (new `UpdateProfileRequest` fields → `ProfileService` → `ApplicationUser`), stats tiles; pricing: feature lines from `PricingRes` (fitness), card polish; billing: table in scroll wrapper. |
| D11 Sessions + Blog + Chat/Calls + Certificates + Notifications + Search | `Pages/Sessions.razor(.css)`, `Blog.razor(.css)`, `BlogArticle.razor(.css)`, `MyCertificates.razor(.css)`, `Notifications.razor`, `Search.razor`, `Chat.razor(.css)`, new `Web/Extensions/MediaUrls.cs` | schedule list grouped by day with "Join" CTA ≥ 44 px; blog magazine grid with cover images — one shared `BlogCard.razor` replaces the duplicated markup in Home/Blog, and `MediaUrls.Resolve(path)` maps relative upload paths (`blog/covers/…`) to `/api/media/…` while passing `/images/...` and `http(s)` through (fixes the broken uploaded covers; og:image made absolute); certificates cards; chat/calls: only copy + spacing (keep selectors used by `call.spec.mjs`: `.conversation-item`, `.btn-video-call`, `.incoming-call-toast`, `.active-call-overlay`, `.participant-tile`, `.control-btn.hang-up`). |
| D12 Auth + Admin + Legal | `Login/Register/ForgotPassword.razor(.css)`, `Admin*.razor(.css)`, `Shared/Components/Data/ScrollableTable.razor`, `Privacy/Terms.razor`, `SiteFooter.razor` | centered auth card; register's Status select becomes "Training level" (repurposed enum) plus an optional Goal select (`RegisterRequestDto.Goal`, `AuthService.RegisterAsync`), keep `#email`, `#password`, `button[type=submit]`, `.form-check .form-text a[href="/privacy"|"/terms"]`, `.legal-content h1`; `ScrollableTable` drops the inner `max-height` below 768 px so the card-table doesn't nest-scroll; admin toolbars wrap; `AdminCourseEdit` new fields (B6) get mobile layout; placeholders `AdminTestimonialEditor.razor:28` → "e.g. Marathon finisher", `AdminBlogEditor.razor:105` → "Strength, Nutrition, Recovery", `AdminRes.FieldKeyHint` → "training_readiness_v1", `OpenApiExtensions.cs:406-408` sample → fitness; footer columns stack, `FooterAuthorRole` → "Head Coach". |

Each D-task: red test (page test in `tests/FitEdu.Web.Tests` where one exists), implement, `dotnet format style`, build, run, **screenshot at 375×812 and 1280×800** with the browser tool, check no horizontal scroll (`document.documentElement.scrollWidth <= innerWidth`), commit `ui(<page>): …`.

---

## 8. Phase E — Verification, docs, cleanup

### Task E1: Full gates
- [ ] `dotnet format style FitEdu.sln --verify-no-changes`; `dotnet build FitEdu.sln` (no new warnings vs baseline 364); `dotnet test FitEdu.sln` all green (expect ≈ 1 050 tests); `cd tests/e2e && npx playwright test` (needs the dev server per `tests/e2e/README.md` — start it with the preview tool, not a shell).
- [ ] Browser sweep (375 px + 1280 px): `/`, `/courses`, `/courses/{id}`, `/lessons/{id}` (workout lesson), `/exercises`, `/exercises/push-ups`, `/paths`, `/assessments`, `/assessments/training_readiness_v1`, `/pricing`, `/profile`, `/sessions`, `/blog`, `/login`, `/admin/courses`; EN and EL for `/`, `/courses`, `/exercises`. Save screenshots to `docs/assets/screenshots/` (own work, add a manifest row with glob `docs/...`? — no: screenshots live under docs, not wwwroot, so the guard ignores them).
- [ ] Reseed check: drop `FitEduDb` (LocalDB) and start once; log must show 19 courses, 19 assessments, ≈ 870 exercises, 4 paths, 6 testimonials, 6 live classes, 10 students, 0 warnings about unknown slugs.

### Task E2: Docs
- [ ] `README.md`: stack unchanged; new sections "Exercise library (public domain)", "Assets & licences" (link manifest), "Seed data" (new folders/paths, env `SeedData:JsonPaths:Exercises`), screenshots; replace every remaining career sentence.
- [ ] `docs/ADDING-A-FIELD.md` unchanged; `docs/plans/32-audit-user-interface.md` / `33-audit-ux.md`: mark the items resolved by this work; `docs/runbook.md`: reseed note for the exercise library.
- [ ] Delete `docs/superpowers/plans/2026-06-25-email-service-auth-flows.md`? **No** — unrelated, keep. This plan file lives outside the repo; copy it to `docs/superpowers/plans/2026-10-12-fitness-transformation.md` at the start of execution and delete it when E1 passes (project rule: remove a driving plan once done).

### Task E3: Old-name leftovers and career fixtures in tests
- [ ] Rename `RyfMigrationChainTest_` → `FitEduMigrationChainTest_` (`MigrationChainTests.cs:51`), env var `RYF_OLLAMA_LIVE` → `FITEDU_OLLAMA_LIVE` (`AssistantLiveSmokeTests.cs:13-41`, `README.md:655`), test DB/issuer strings stay. `git grep -i -E 'ryf|reset ?your'` must return nothing.
- [ ] Re-theme self-contained test fixtures that still say career/job/CV (behaviour unchanged, keep sort-order relationships noted in the tests sweep: `CategorySearchExtensionsTests` EN/EL alphabetical order, `BillingTransactionSearchExtensionsTests` plan order): `AssistantToolsTests`, `AssistantServiceTests`, `AssistantIndexingServiceTests`, `LearningPathServiceTests`, `CourseReviewServiceTests`, `SiteSearchServiceTests` (InMemory `Contains` is case-sensitive — keep query casing = title casing), `AdminCategoryServiceTests`, `CategoryServiceTests`, `CourseServiceTests`, `AdminCourseServiceTests`, `ApplicationDbContextTests`, `AssessmentSubmissionSearchExtensionsTests`. `CrossCuttingTests.cs:68` asserts the new `HomeRes.PageTitle` via the resource property instead of a literal. Commit `test: fitness fixtures`.

### Task E4: Review and ship
- [ ] `superpowers:requesting-code-review` on the full diff (adversarial: licence rows, EL completeness, mobile overflow, selectors used by e2e). Fix findings. Merge the execution branch into `master` with `--no-ff` (push only if the user asks).

---

## 9. Agentic execution (Workflow script outline)

Run from `master` on a branch `fitedu-fitness`. Serial where files overlap, parallel where disjoint:

```
Phase A  A1 → A2                                   (1 agent, serial)
Phase B  B1 → B2 → B3 → B4 → B5 → B6               (1 agent each, serial; B3 may run in parallel with B4–B6 — disjoint files)
Phase C  C1 → [C2 ×19 courses ∥ C4 ×19 assessments ∥ C5 ∥ C6 ∥ C7 ∥ C8]   (pipeline; content agents write disjoint files in the SAME worktree; C7 and C8 touch resx/services — give them their own worktrees and merge --no-ff)
Phase D  D1 → D2 → [D3 … D12 in worktrees] → merge in table order
Phase E  E1 → E2 → E3 → E4
```
Every agent: fresh context, the task text from this plan, `superpowers:test-driven-development`, `superpowers:verification-before-completion`; returns the commit hash and test counts. A verifier agent re-runs `dotnet test` + the browser check for each merged D-task (adversarial: "find the overflow / blank render"). Loop-until-dry on `SeedContentIntegrityTests` + grep for career vocabulary.

## 10. Verification summary (what "done" means)

1. `dotnet format style --verify-no-changes`, `dotnet build`, `dotnet test` green; e2e green (auth, pages incl. `/exercises`, call).
2. `AssetManifestTests` green with `background.png` gone and every image covered by a CC0/PD/own-work row; `docs/assets/ASSET-MANIFEST.md` has the quoted licence text, URL and date for Free Exercise DB and the hero.
3. `SeedContentIntegrityTests` green: 19/19 courses and assessments bilingual; all exercise slugs exist with images; zero career vocabulary in seeds and resx.
4. Fresh DB seeds without warnings; screenshots at 375 px show no horizontal scroll, bottom nav present, touch targets ≥ 44 px; 1280 px shows desktop nav.
5. Lesson viewer shows animated exercise cards with sets/reps; `/exercises` filters by muscle/equipment/level; assistant answers as FitEdu Coach with the disclaimer.

---

## Appendix A — resx keys (Task C7). Every family: `.resx` + `.el.resx` + hand-edited `.Designer.cs`

All 24 families today have EN = EL = Designer key sets (verified by the sweep); keep that invariant — `SeedContentIntegrityTests.Resx_EveryEnglishKeyHasGreekValue` enforces it.

### HomeRes — delete (Home sections removed in D3, remove the Designer properties too)
`ProblemTitle, ProblemTitleHighlight, ProblemCard1..4, ProblemConclusion, ProblemConclusionHighlight, SolutionTitle, SolutionTitleHighlight, SolutionCard1Title..3Title, SolutionCard1Text..3Text, MethodTitleFrom, MethodTitleZeroState, MethodTitleTo, MethodTitleHeroState, ZeroStateTitle, ZeroState1..4, HeroStateTitle, HeroState1..5, StoryTitle, StoryTitleHighlight, StoryQuote, StoryAuthorInitials, StoryAuthorName, StoryAuthorTitle`

### HomeRes — rewrite
| Key | EN | EL |
|---|---|---|
| PageTitle | FitEdu \| Fitness Courses & Exercise Library | FitEdu \| Μαθήματα Fitness & Βιβλιοθήκη Ασκήσεων |
| HeroTitleLine1 | Train with a plan. | Προπονήσου με σχέδιο. |
| HeroTitleLine2 | Move better every week. | Κινήσου καλύτερα κάθε εβδομάδα. |
| HeroSubtitle | Structured fitness courses, a public-domain exercise library with start-and-finish photos, and self-checks that keep you training safely — in English and Greek. | Δομημένα μαθήματα fitness, βιβλιοθήκη ασκήσεων με φωτογραφίες αρχικής και τελικής θέσης και αυτοέλεγχοι για ασφαλή προπόνηση — στα Ελληνικά και στα Αγγλικά. |
| HeroCtaButton | Start free | Ξεκίνα δωρεάν |
| TestimonialsTitle | Member | Ιστορίες |
| TestimonialsTitleHighlight | stories | μελών |
| CtaTitle | Ready for your first | Έτοιμος/η για την πρώτη σου |
| CtaTitleHighlight | workout? | προπόνηση; |
| CtaText | Create a free account, pick a goal and follow a programme built around your level. | Φτιάξε δωρεάν λογαριασμό, διάλεξε στόχο και ακολούθησε ένα πρόγραμμα για το επίπεδό σου. |
| CtaButton | Create free account | Δημιουργία δωρεάν λογαριασμού |
| FooterAuthorName | FitEdu Coaching Team | Ομάδα Προπονητών FitEdu |
| FooterAuthorRole | Fitness education platform | Πλατφόρμα εκπαίδευσης fitness |

### HomeRes — add
| Key | EN | EL |
|---|---|---|
| HeroCtaSecondary | Browse exercises | Δες τις ασκήσεις |
| StatsCourses | Courses | Μαθήματα |
| StatsExercises | Exercises | Ασκήσεις |
| StatsLessons | Workouts & lessons | Προπονήσεις & μαθήματα |
| StatsMembers | Members | Μέλη |
| GoalsTitle | Pick your | Διάλεξε τον |
| GoalsTitleHighlight | goal | στόχο σου |
| HowTitle | How FitEdu | Πώς δουλεύει |
| HowTitleHighlight | works | το FitEdu |
| HowStep1Title | Check your baseline | Έλεγξε το επίπεδό σου |
| HowStep1Text | Short self-assessments show where to start and what to watch out for. | Σύντομοι αυτοέλεγχοι δείχνουν από πού να ξεκινήσεις και τι να προσέξεις. |
| HowStep2Title | Follow a programme | Ακολούθησε ένα πρόγραμμα |
| HowStep2Text | Every workout lists its exercises with sets, reps, rest and form cues. | Κάθε προπόνηση έχει τις ασκήσεις της με σετ, επαναλήψεις, διαλείμματα και οδηγίες τεχνικής. |
| HowStep3Title | Track and progress | Παρακολούθησε την πρόοδό σου |
| HowStep3Text | Mark workouts done, keep your streak and earn a certificate at the end. | Σημείωσε τις προπονήσεις, κράτα το σερί σου και πάρε πιστοποιητικό στο τέλος. |
| FeaturedTitle | Popular | Δημοφιλή |
| FeaturedTitleHighlight | programmes | προγράμματα |
| TodayPlanTitle | Today's plan | Το σημερινό πλάνο |
| TodayPlanContinue | Continue workout | Συνέχισε την προπόνηση |
| TodayPlanEmpty | No programme yet — pick one to get your first workout. | Δεν έχεις πρόγραμμα ακόμη — διάλεξε ένα για την πρώτη σου προπόνηση. |
| RecommendedTitle | Recommended for you | Προτεινόμενα για σένα |
| DashboardContinueLearning (rewrite) | Your programmes | Τα προγράμματά σου |

### GlobalRes
| Key | EN | EL |
|---|---|---|
| Label_Info_Status | Training level | Επίπεδο προπόνησης |
| StatusBeginner (was StatusStudent) | Beginner | Αρχάριος/α |
| StatusIntermediate (was StatusGraduate) | Intermediate | Μεσαίο επίπεδο |
| StatusAdvanced (was StatusNEET) | Advanced | Προχωρημένος/η |
| StatusOther | Prefer not to say | Προτιμώ να μην απαντήσω |

### LegalRes (structure unchanged; `LastUpdated` → "Last updated: October 2026" / "Τελευταία ενημέρωση: Οκτώβριος 2026")
| Key | EN (summary of the new text; the executor writes the full paragraph in both languages) |
|---|---|
| EducationalNotice / TermsIntro | FitEdu is a university certificate project demonstrating a fitness-education platform; content is for general education, not medical advice. |
| PrivacyIntro | FitEdu is an online fitness-courses platform. This policy explains what we collect when you create an account, enrol in programmes, complete workouts, answer self-assessments or use chat, calls and the AI coach. |
| PrivacyDataBody | Account data (name, email, date of birth, training level, goal), enrolments and completed workouts, assessment answers, certificates, billing records, chat/call metadata. |
| PrivacySpecialTitle | Health-related assessment data |
| PrivacySpecialBody | Some assessments ask about injuries, pain, sleep and nutrition. Answers are encrypted at rest, used only to show your results, and can be deleted with your account. |
| TermsServiceBody | FitEdu offers online fitness courses, an exercise library, self-assessments, certificates, live classes and chat/video features. |
| TermsAssessmentsTitle | Not medical advice |
| TermsAssessmentsBody | Courses, assessments and the AI coach give general fitness education. They are not medical, physiotherapy or nutrition advice; consult a professional before training with a condition, injury or during pregnancy, and stop if you feel pain. |

### Other families
| Family.Key | EN | EL |
|---|---|---|
| AssistantRes.Title | FitEdu Coach | Προπονητής FitEdu |
| AssistantRes.Disclaimer | AI-generated — not medical or professional advice. | Παράγεται από AI — δεν αποτελεί ιατρική ή επαγγελματική συμβουλή. |
| AssistantRes.SuggestedQuestion1 | Which course should I start with as a beginner? | Με ποιο μάθημα να ξεκινήσω ως αρχάριος; |
| AssistantRes.SuggestedQuestion2 | Show me exercises for my back | Δείξε μου ασκήσεις για την πλάτη |
| AssistantRes.SuggestedQuestion3 | How do I progress my squat? | Πώς προχωράω στο squat; |
| AdminRes.FieldKeyHint | unique identifier, e.g. training_readiness_v1 | μοναδικό αναγνωριστικό, π.χ. training_readiness_v1 |
| AdminRes.ColRoleCompany | Role / Context | Ρόλος / Πλαίσιο |
| AdminRes.FieldCompanyContext | Gym / Context | Γυμναστήριο / Πλαίσιο |
| SessionRes.CancelConfirmFormat | Cancel "{0}"? Registered members will no longer see it as upcoming. | Ακύρωση του "{0}"; Τα εγγεγραμμένα μέλη δεν θα το βλέπουν πλέον ως επερχόμενο. |
| PricingRes.Subtitle | Train smarter with the plan that fits your goals. | Προπονήσου έξυπνα με το πλάνο που ταιριάζει στους στόχους σου. |
| NavMenuRes.Sessions | Live classes | Ζωντανά μαθήματα |
| NavMenuRes.Paths | Training paths | Διαδρομές προπόνησης |
| PathRes.PathsTitle | Training paths | Διαδρομές προπόνησης |

### New keys
| Family.Key | EN | EL |
|---|---|---|
| NavMenuRes.Exercises | Exercises | Ασκήσεις |
| NavMenuRes.Profile | Profile | Προφίλ |
| CourseRes.Level / Goal / Equipment | Level / Goal / Equipment | Επίπεδο / Στόχος / Εξοπλισμός |
| CourseRes.WeeksFormat | {0} weeks | {0} εβδομάδες |
| CourseRes.SessionsPerWeekFormat | {0}× per week | {0}× την εβδομάδα |
| CourseRes.Level_Beginner / Level_Intermediate / Level_Advanced | Beginner / Intermediate / Advanced | Αρχάριο / Μεσαίο / Προχωρημένο |
| CourseRes.Goal_GeneralFitness | General fitness | Γενική φυσική κατάσταση |
| CourseRes.Goal_Strength | Strength | Δύναμη |
| CourseRes.Goal_MuscleGain | Muscle gain | Μυϊκή ανάπτυξη |
| CourseRes.Goal_FatLoss | Fat loss | Απώλεια λίπους |
| CourseRes.Goal_Endurance | Endurance | Αντοχή |
| CourseRes.Goal_Mobility | Mobility | Κινητικότητα |
| CourseRes.Goal_Recovery | Recovery | Αποκατάσταση |
| CourseRes.Goal_Nutrition | Nutrition | Διατροφή |
| CourseRes.Equipment_none / dumbbells / barbell / kettlebell / bands / mat / bike / chair / cables / machine | No equipment / Dumbbells / Barbell / Kettlebell / Bands / Mat / Bike / Chair / Cables / Machine | Χωρίς εξοπλισμό / Αλτήρες / Μπάρα / Kettlebell / Λάστιχα / Στρώμα / Ποδήλατο / Καρέκλα / Τροχαλίες / Μηχάνημα |
| CourseRes.AllGoals / AllLevels / AllEquipment | All goals / All levels / Any equipment | Όλοι οι στόχοι / Όλα τα επίπεδα / Οποιοσδήποτε εξοπλισμός |
| LessonRes.Exercises / Sets / Reps / Rest | Exercises / Sets / Reps / Rest | Ασκήσεις / Σετ / Επαναλήψεις / Διάλειμμα |
| LessonRes.RestSecondsFormat | {0} s rest | {0} δευτ. διάλειμμα |
| LessonRes.ShowInstructions | How to do it | Πώς γίνεται |
| LessonRes.StartPosition / EndPosition | Start / Finish | Αρχή / Τέλος |
| LessonRes.WorkoutProgressFormat | Workout {0} of {1} | Προπόνηση {0} από {1} |
| ExerciseRes.PageTitle | Exercise Library - FitEdu | Βιβλιοθήκη Ασκήσεων - FitEdu |
| ExerciseRes.Library | Exercise library | Βιβλιοθήκη ασκήσεων |
| ExerciseRes.Subtitle | Public-domain photos of every exercise — filter by muscle, equipment and level. | Φωτογραφίες δημόσιου τομέα για κάθε άσκηση — φίλτρα ανά μυ, εξοπλισμό και επίπεδο. |
| ExerciseRes.SearchPlaceholder | Search exercises… | Αναζήτηση ασκήσεων… |
| ExerciseRes.Muscle / Equipment / Level / Category | Muscle / Equipment / Level / Type | Μυς / Εξοπλισμός / Επίπεδο / Τύπος |
| ExerciseRes.AllMuscles | All muscles | Όλοι οι μύες |
| ExerciseRes.PrimaryMuscles / SecondaryMuscles | Primary muscles / Secondary muscles | Κύριοι μύες / Δευτερεύοντες μύες |
| ExerciseRes.Instructions | Instructions | Οδηγίες |
| ExerciseRes.UsedInLessons | Used in these workouts | Χρησιμοποιείται σε αυτές τις προπονήσεις |
| ExerciseRes.NoResults | No exercises match these filters. | Καμία άσκηση δεν ταιριάζει με τα φίλτρα. |
| ExerciseRes.ResultsFormat | {0} exercises | {0} ασκήσεις |
| ExerciseRes.Force_push / pull / static | Push / Pull / Static | Ώθηση / Έλξη / Στατική |
| ExerciseRes.Mechanic_compound / isolation | Compound / Isolation | Πολυαρθρική / Μονοαρθρική |
| ExerciseRes.Source | Photos and instructions: Free Exercise DB (public domain). | Φωτογραφίες και οδηγίες: Free Exercise DB (δημόσιος τομέας). |
| ProfileRes.Goal | Training goal | Στόχος προπόνησης |
| ProfileRes.WorkoutsCompleted | Workouts completed | Ολοκληρωμένες προπονήσεις |
| ProfileRes.MinutesTrained | Minutes trained | Λεπτά προπόνησης |
| ProfileRes.CurrentStreak | Current streak | Τρέχον σερί |
| ProfileRes.StreakDaysFormat | {0} days | {0} ημέρες |
| AdminRes.Level / Goal / Equipment / EstimatedWeeks / SessionsPerWeek / CoverExercise / Exercises / AddExercise / RemoveExercise / Sets / Reps / RestSeconds / Notes / UnknownExerciseSlug | Level / Goal / Equipment / Estimated weeks / Sessions per week / Cover exercise (slug) / Exercises / Add exercise / Remove / Sets / Reps / Rest (s) / Notes / No exercise with this slug | Επίπεδο / Στόχος / Εξοπλισμός / Εκτιμώμενες εβδομάδες / Προπονήσεις ανά εβδομάδα / Άσκηση εξωφύλλου (slug) / Ασκήσεις / Προσθήκη άσκησης / Αφαίρεση / Σετ / Επαναλήψεις / Διάλειμμα (δευτ.) / Σημειώσεις / Δεν υπάρχει άσκηση με αυτό το slug |
| ErrorMessagesRes.Exercise_NotFound | Exercise not found. | Η άσκηση δεν βρέθηκε. |

Muscle names shown in chips come from the DB values (`abdominals, abductors, adductors, biceps, calves, chest, forearms, glutes, hamstrings, lats, lower back, middle back, neck, quadriceps, shoulders, traps, triceps`) mapped through `ExerciseRes.Muscle_<key>` (17 keys, EN = title-cased value, EL = κοιλιακοί, απαγωγοί, προσαγωγοί, δικέφαλοι, γάμπες, στήθος, πήχεις, γλουτοί, οπίσθιοι μηριαίοι, πλατιαίοι, κάτω πλάτη, μέση πλάτη, αυχένας, τετρακέφαλοι, ώμοι, τραπεζοειδείς, τρικέφαλοι); equipment values likewise through `ExerciseRes.Equipment_<key>` (`body only, dumbbell, barbell, kettlebells, cable, machine, bands, medicine ball, exercise ball, e-z curl bar, foam roll, other` → EL: μόνο σώμα, αλτήρας, μπάρα, kettlebell, τροχαλία, μηχάνημα, λάστιχα, ιατρική μπάλα, μπάλα γυμναστικής, μπάρα E-Z, αφρώδης κύλινδρος, άλλο). Unknown DB values fall back to the raw string.
