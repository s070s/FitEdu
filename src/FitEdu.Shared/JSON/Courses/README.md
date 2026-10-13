# Course seed data

Each `*.json` file in this folder is one course. `CourseSeeder` loads them in Development when
`SeedData:Enabled` is true, after the exercise library (`../Exercises/`). It seeds only when the
`Courses` table is empty, so a change here needs an empty table or a fresh database. The field
names match `CourseSeedDto` (`src/FitEdu.Application/DTOs/Seed/CourseSeedDtos.cs`); case does not
matter.

```json
{
  "title": "Bodyweight Basics", "titleEl": "…", "description": "…", "descriptionEl": "…",
  "requiredTier": "Free", "isPublished": true,
  "category": "Strength", "categoryEl": "Δύναμη",
  "level": "Beginner", "goal": "FatLoss", "equipment": "none,mat",
  "estimatedWeeks": 6, "sessionsPerWeek": 3, "coverExerciseSlug": "pushups",
  "modules": [
    { "title": "Week 1", "titleEl": "…", "sortOrder": 1,
      "lessons": [
        { "title": "Workout A", "titleEl": "…", "content": "…", "contentEl": "…",
          "durationMinutes": 30, "sortOrder": 1,
          "exercises": [
            { "slug": "pushups", "sets": 3, "reps": "8-12", "restSeconds": 60,
              "notes": "Elbows at 45°", "notesEl": "…" }
          ] }
      ] }
  ]
}
```

| Field | Rule |
|---|---|
| `requiredTier` | `Free`, `Plus` or `Pro`; anything else becomes `Free`. |
| `level` | `Beginner`, `Intermediate` or `Advanced`; anything else becomes `Beginner`. |
| `goal` | `GeneralFitness`, `Strength`, `MuscleGain`, `FatLoss`, `Endurance`, `Mobility`, `Recovery` or `Nutrition`; anything else becomes `GeneralFitness`. |
| `category`, `categoryEl` | The category is created on first use. `categoryEl` sets its Greek name, or fills it in when the category has none yet; an existing Greek name is kept. |
| `equipment` | Comma-joined codes with no spaces (the catalogue filter matches `,code,` exactly): `none`, `dumbbells`, `barbell`, `kettlebell`, `bands`, `mat`, `bike`, `chair`, `cables`, `machine`. |
| `coverExerciseSlug`, `exercises[].slug` | Must be a slug from `docs/assets/exercise-slugs.txt`. An unknown or missing slug stops the seed with an error naming the course or lesson and the slug (and saying so when the exercise library itself is empty). Start-up retries the seed five times over about 30 seconds and then the app refuses to start, so nothing after the courses (assessments, students, paths, showcase) is seeded until the slug is fixed. A file that is not valid JSON is only logged and skipped. |
| `exercises` | Shown in list order. `sets`, `reps`, `restSeconds`, `notes` and `notesEl` are optional. `reps` has no Greek twin, so it holds only numbers, ranges, symbols and the unit symbols `s`, `min`, `m`, `rpm`, `RPE`, `AMRAP` (`8–12`, `30 s`, `6 min @ RPE 7 + 3 min`), with no decimals; words such as "each side" go at the start of `notes` and `notesEl`. |

Every field except `title` (course, module and lesson) and `slug` is optional; older files without
the fitness fields seed with the defaults above.

The seeder accepts files with fields left out, but the shipped catalogue is held to a stricter rule:
`SeedContentIntegrityTests` (`tests/FitEdu.Infrastructure.Tests`) fails when a course here lacks a
Greek text, misspells a field name, uses a level, goal, tier or equipment code outside the lists
above, uses a category other than Strength (Δύναμη), Cardio (Κάρντιο), Mobility (Κινητικότητα), Core
(Κορμός), Nutrition (Διατροφή), Recovery (Αποκατάσταση) or Performance (Απόδοση) with exactly that
`categoryEl`, writes words in `reps`, or points at an exercise with no photos or no Greek name in
`../Exercises/exercises.el.json` or `../Exercises/el/*.el.json`.
