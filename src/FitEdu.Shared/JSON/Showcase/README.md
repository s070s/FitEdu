# Showcase seed data

`ShowcaseSeeder` loads the two files in this folder in Development when `SeedData:Enabled` is
true, after the courses and learning paths. Each file seeds only while its table is empty
(`Testimonials`, `ScheduledSessions`), so a change here needs an empty table or a fresh database.
The folder path is `SeedData:JsonPaths:Showcase` in `appsettings.Development.json`. The field
names match `TestimonialSeedDto` and `LiveClassSeedDto`
(`src/FitEdu.Application/DTOs/Seed/ShowcaseSeedDtos.cs`); case does not matter.

## testimonials.json

```json
[
  { "fullName": "Eleni Papadopoulou", "roleOrTitle": "Ran her first 5K", "roleOrTitleEl": "…",
    "quoteText": "…", "quoteTextEl": "…", "displayOrder": 1, "isActive": true }
]
```

| Field | Rule |
|---|---|
| `fullName` | Up to 150 characters, shown in both languages. |
| `roleOrTitle`, `roleOrTitleEl` | Up to 150 characters. The testimonial has no Greek company/context column, so any context (course, age, situation) goes here and the company/context stays empty. |
| `quoteText`, `quoteTextEl` | Up to 1000 characters, without quote marks (the Home page adds «»). |
| `displayOrder`, `isActive` | Order on the Home page, lowest first; `isActive` defaults to `true`. |

There are no avatars: the Home page shows the initials instead.

## live-classes.json

```json
[
  { "title": "Live Mobility Flow", "titleEl": "…", "courseTitle": "Mobility & Flexibility",
    "weekdayOffset": 1, "hourUtc": 18, "durationMinutes": 45, "maxParticipants": 6 }
]
```

| Field | Rule |
|---|---|
| `title`, `titleEl` | Up to 200 characters. |
| `courseTitle` | The English `title` of a seeded course, or `null`. An unknown title stops the seed. |
| `weekdayOffset`, `hourUtc` | The class starts on the N-th Monday-to-Friday day after the seed date (the seed date itself does not count) at `hourUtc`:00 UTC. Seeding on Monday 12 October 2026 gives offsets 1 to 6 the dates 13, 14, 15, 16, 19 and 20 October. |
| `durationMinutes` | Positive. |
| `maxParticipants` | 1 to 6: a group video call holds six people (`ScheduledSession.MaxParticipantsLimit`). A larger value stops the seed. |

The admin account (`AdminUser:Email`) hosts every class.
