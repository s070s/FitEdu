# Learning path seed data

Each `*.json` file in this folder holds a list of learning paths. `LearningPathSeeder` loads them
in Development when `SeedData:Enabled` is true, after the courses (`../Courses/`), because every
step names its course by English title. It seeds only when the `LearningPaths` table is empty, so
a change here needs an empty table or a fresh database. The folder path is
`SeedData:JsonPaths:Paths` in `appsettings.Development.json`. The field names match
`LearningPathSeedDto` (`src/FitEdu.Application/DTOs/Seed/LearningPathSeedDto.cs`); case does not
matter.

```json
[
  {
    "title": "Beginner Start", "titleEl": "…", "description": "…", "descriptionEl": "…",
    "category": "Strength", "categoryEl": "Δύναμη",
    "displayOrder": 1, "isPublished": true,
    "courseTitles": ["Bodyweight Basics", "Core & Stability", "Mobility & Flexibility", "Strength Foundations"]
  }
]
```

| Field | Rule |
|---|---|
| `title`, `titleEl` | Up to 200 characters. |
| `description`, `descriptionEl` | Up to 2000 characters. |
| `category`, `categoryEl` | Reuses the course category with the same English name (or creates it) and fills its Greek name when it has none. |
| `displayOrder` | Order on the `/paths` page, lowest first. |
| `isPublished` | Defaults to `true`: the public `/paths` page shows only published paths. |
| `courseTitles` | The courses in step order (step 1, 2, 3…). Each must equal the English `title` of a seeded course; an unknown title stops the seed with an error naming the path and the title. When two courses share a title, the first one read is used. |
