using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using FitEdu.Infrastructure.Data;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

public class ApplicationDbContextTests
{
    private static ApplicationDbContext CtxWithUser(string? userId, string? dbName = null)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        if (userId is not null)
        {
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"))
            };
            accessor.HttpContext.Returns(http);
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options, accessor);
    }

    [Fact]
    public async Task SaveChanges_StampsAuditFieldsFromPrincipal()
    {
        await using var db = CtxWithUser("auditor-1");
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };

        db.Courses.Add(course);
        await db.SaveChangesAsync();

        course.CreatedByUserId.ShouldBe("auditor-1");
        // DB-3: UpdatedAt/UpdatedByUserId are no longer stamped on Added — a freshly created
        // row hasn't been "updated" yet, so both stay null until a real Modified save.
        course.UpdatedByUserId.ShouldBeNull();
        course.UpdatedAt.ShouldBeNull();
    }

    [Fact]
    public void SaveChanges_WithAcceptAllChangesOnSuccessFalse_StillStampsAuditFields()
    {
        // The overload with acceptAllChangesOnSuccess is what both parameterless SaveChanges call; a caller
        // (or the EF Core internals) that uses it directly must not skip the audit stamp.
        using var db = CtxWithUser("direct-caller");
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
        db.Courses.Add(course);

        db.SaveChanges(acceptAllChangesOnSuccess: false);

        course.CreatedByUserId.ShouldBe("direct-caller");
    }

    [Fact]
    public async Task SaveChangesAsync_WithAcceptAllChangesOnSuccessFalse_StillStampsAuditFields()
    {
        await using var db = CtxWithUser("direct-caller");
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
        db.Courses.Add(course);

        await db.SaveChangesAsync(acceptAllChangesOnSuccess: false);

        course.CreatedByUserId.ShouldBe("direct-caller");
    }

    [Fact]
    public async Task SaveChanges_Added_KeepsACreatedAtTheCallerSet_AndStampsOneWhenNoneWasSet()
    {
        // A seed row carries its own creation date (AssessmentSeeder), which the save must not replace.
        var seededCreatedAt = new DateTimeOffset(2026, 10, 1, 8, 30, 0, TimeSpan.Zero);
        var beforeSave = DateTimeOffset.UtcNow;
        await using var db = CtxWithUser("seeder");
        var seeded = new Course { Id = Guid.NewGuid(), TitleEn = "Seeded", CreatedAt = seededCreatedAt };
        var fresh = new Course { Id = Guid.NewGuid(), TitleEn = "Fresh" };

        db.Courses.AddRange(seeded, fresh);
        await db.SaveChangesAsync();

        seeded.CreatedAt.ShouldBe(seededCreatedAt);
        fresh.CreatedAt.ShouldBeGreaterThanOrEqualTo(beforeSave);
    }

    [Fact]
    public async Task SaveChanges_Modified_CannotChangeCreatedAtOrCreatedByUserId()
    {
        var originalCreatedAt = new DateTimeOffset(2026, 10, 1, 8, 30, 0, TimeSpan.Zero);
        var dbName = Guid.NewGuid().ToString("N");
        Guid courseId;
        await using (var seedDb = CtxWithUser("creator", dbName))
        {
            var course = new Course { Id = Guid.NewGuid(), TitleEn = "C", CreatedAt = originalCreatedAt };
            courseId = course.Id;
            seedDb.Courses.Add(course);
            await seedDb.SaveChangesAsync();
        }

        await using (var editorDb = CtxWithUser("editor", dbName))
        {
            var tracked = await editorDb.Courses.SingleAsync(c => c.Id == courseId);
            tracked.TitleEn = "Edited";
            tracked.CreatedAt = originalCreatedAt.AddYears(5);
            tracked.CreatedByUserId = "someone-else";
            await editorDb.SaveChangesAsync();
        }

        await using var freshDb = CtxWithUser("reader", dbName);
        var saved = await freshDb.Courses.AsNoTracking().SingleAsync(c => c.Id == courseId);
        saved.TitleEn.ShouldBe("Edited");
        saved.CreatedAt.ShouldBe(originalCreatedAt);
        saved.CreatedByUserId.ShouldBe("creator");
    }

    [Fact]
    public async Task SaveChanges_NoHttpContext_StampsTimestampsButNullUser()
    {
        await using var db = CtxWithUser(null);
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };

        db.Courses.Add(course);
        await db.SaveChangesAsync();

        course.CreatedByUserId.ShouldBeNull();
        course.UpdatedAt.ShouldBeNull();
    }

    [Fact]
    public async Task SaveChanges_Modify_DoesNotOverwriteCreatedBy()
    {
        await using var db = CtxWithUser("creator");
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
        db.Courses.Add(course);
        await db.SaveChangesAsync();

        course.TitleEn = "Updated";
        await db.SaveChangesAsync();

        course.CreatedByUserId.ShouldBe("creator");
    }

    [Fact]
    public async Task SaveChanges_Modify_StampsUpdatedAtAndUpdatedByUserId()
    {
        await using var db = CtxWithUser("creator");
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
        db.Courses.Add(course);
        await db.SaveChangesAsync();

        course.TitleEn = "Updated";
        await db.SaveChangesAsync();

        course.UpdatedByUserId.ShouldBe("creator");
        course.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task SaveChanges_ModifyByDifferentUser_UpdatesUpdatedByUserId()
    {
        // DB-3: this is the bug — `??=` meant UpdatedByUserId was stuck on the first writer
        // forever, once it had been non-null since insertion. A later edit by someone else
        // (a separate DbContext instance, matching a separate HTTP request) must now correctly
        // overwrite it, not keep showing the creator.
        var dbName = Guid.NewGuid().ToString("N");
        Guid courseId;
        await using (var db = CtxWithUser("creator", dbName))
        {
            var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
            courseId = course.Id;
            db.Courses.Add(course);
            await db.SaveChangesAsync();
        }

        await using var editorDb = CtxWithUser("editor-2", dbName);
        var tracked = await editorDb.Courses.FirstAsync(c => c.Id == courseId);
        tracked.TitleEn = "Updated again";
        await editorDb.SaveChangesAsync();

        tracked.UpdatedByUserId.ShouldBe("editor-2");
        tracked.CreatedByUserId.ShouldBe("creator");
    }

    [Fact]
    public async Task SoftDeleteFilter_HidesDeletedEntities()
    {
        await using var db = CtxWithUser("admin");
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
        db.Courses.Add(course);
        await db.SaveChangesAsync();

        course.IsDeleted = true;
        await db.SaveChangesAsync();

        (await db.Courses.CountAsync()).ShouldBe(0);
        (await db.Courses.IgnoreQueryFilters().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task DependentFilter_HidesEnrollmentsOfSoftDeletedCourse()
    {
        await using var db = CtxWithUser("admin");
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
        db.Courses.Add(course);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = "u1", CourseId = course.Id });
        await db.SaveChangesAsync();

        course.IsDeleted = true;
        await db.SaveChangesAsync();

        (await db.Enrollments.CountAsync()).ShouldBe(0);
        (await db.Enrollments.IgnoreQueryFilters().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Certificate_SurvivesCourseSoftDelete_WithNullCourseNavigation()
    {
        // Course is a required navigation the way Certificate was configured before this fix, so
        // once the course is soft-deleted, Include(c => c.Course) inner-joins it away and the
        // whole certificate row vanishes — even though the certificate itself is still valid and
        // should keep being visible (e.g. on the student's certificates page) after the course is
        // gone. The LEFT JOIN this test relies on only shows up against a real relational
        // provider (SQLite here) — EF Core's InMemory provider doesn't apply query filters the
        // same way through Include, so it would pass regardless of whether the fix is present.
        await using var db = FitEdu.TestSupport.DbContextFactory.CreateSqlite();
        var user = new ApplicationUser { Id = "u1", UserName = "u@x.com", Email = "u@x.com", FirstName = "A", LastName = "B" };
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
        var enrollment = new Enrollment { Id = Guid.NewGuid(), UserId = user.Id, CourseId = course.Id };
        var certificate = new Certificate
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CourseId = course.Id,
            EnrollmentId = enrollment.Id,
            RecipientName = "A B",
            CourseTitleEn = "C",
            Status = CertificateStatus.Active
        };
        db.Users.Add(user);
        db.Courses.Add(course);
        db.Enrollments.Add(enrollment);
        db.Certificates.Add(certificate);
        await db.SaveChangesAsync();

        course.IsDeleted = true;
        await db.SaveChangesAsync();

        // Clear the tracker so the reload below can't navigation-fixup Course from the
        // already-tracked `course` instance — a real request gets a fresh DbContext with nothing
        // tracked, so only the query filter (not in-memory fixup) should decide whether Course
        // comes back.
        db.ChangeTracker.Clear();
        var reloaded = await db.Certificates.Include(c => c.Course).FirstOrDefaultAsync(c => c.Id == certificate.Id);

        reloaded.ShouldNotBeNull();
        reloaded.Course.ShouldBeNull();
    }

    [Fact]
    public async Task CourseReview_SurvivesCourseSoftDelete_WithNullCourseNavigation()
    {
        // Same required-navigation problem as Certificate: a review should stay visible to
        // moderation/history views even after its course is soft-deleted, not disappear from
        // Include(r => r.Course) queries. Uses SQLite for the same reason as the test above —
        // InMemory doesn't exercise the LEFT JOIN/filter interaction this fix depends on.
        await using var db = FitEdu.TestSupport.DbContextFactory.CreateSqlite();
        var user = new ApplicationUser { Id = "u1", UserName = "u@x.com", Email = "u@x.com", FirstName = "A", LastName = "B" };
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
        var review = new CourseReview { Id = Guid.NewGuid(), CourseId = course.Id, UserId = user.Id, Rating = 5, Body = "Great course" };
        db.Users.Add(user);
        db.Courses.Add(course);
        db.CourseReviews.Add(review);
        await db.SaveChangesAsync();

        course.IsDeleted = true;
        await db.SaveChangesAsync();

        // See the comment on the Certificate test above — clearing the tracker forces the reload
        // to rely on the query filter instead of in-memory navigation fixup.
        db.ChangeTracker.Clear();
        var reloaded = await db.CourseReviews.Include(r => r.Course).FirstOrDefaultAsync(r => r.Id == review.Id);

        reloaded.ShouldNotBeNull();
        reloaded.Course.ShouldBeNull();
    }

    [Fact]
    public async Task CategoryNameEn_Duplicate_ThrowsOnSave()
    {
        // DB-5: the filtered unique index is a DB-level backstop to the racy
        // check-then-insert in AdminCategoryService — verify it actually exists and fires,
        // against SQLite (the same provider the earlier "breaks SQLite" rationale doubted).
        await using var db = FitEdu.TestSupport.DbContextFactory.CreateSqlite();
        db.Categories.Add(new Category { Id = Guid.NewGuid(), NameEn = "Strength" });
        await db.SaveChangesAsync();

        db.Categories.Add(new Category { Id = Guid.NewGuid(), NameEn = "Strength" });
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task CategoryNameEn_DuplicateButOneSoftDeleted_Allowed()
    {
        // The filter is `[IsDeleted] = 0` — a soft-deleted category must not block reusing
        // its name for a new, active one.
        await using var db = FitEdu.TestSupport.DbContextFactory.CreateSqlite();
        db.Categories.Add(new Category { Id = Guid.NewGuid(), NameEn = "Strength", IsDeleted = true });
        await db.SaveChangesAsync();

        db.Categories.Add(new Category { Id = Guid.NewGuid(), NameEn = "Strength" });
        await db.SaveChangesAsync();

        (await db.Categories.IgnoreQueryFilters().CountAsync(c => c.NameEn == "Strength")).ShouldBe(2);
    }

    [Fact]
    public async Task Exercise_DuplicateSlug_ThrowsOnSave()
    {
        // The slug is the exercise's public key (/exercises/{slug}, image folder, seed JSON), so the
        // unique index must exist at the DB level — SQLite enforces it, InMemory would not.
        await using var db = FitEdu.TestSupport.DbContextFactory.CreateSqlite();
        db.Exercises.Add(NewExercise("push-ups", "Push-Ups"));
        db.Exercises.Add(NewExercise("push-ups", "Duplicate"));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task BillingTransaction_UserDeleted_KeepsTheRecordWithoutTheUser()
    {
        // Billing records are tax records: they are kept for the legal period, so deleting the account
        // detaches them (UserId becomes null, set by the database) instead of cascading to them.
        await using var db = FitEdu.TestSupport.DbContextFactory.CreateSqlite();
        var user = new ApplicationUser { Id = "u1", UserName = "u@x.com", Email = "u@x.com", FirstName = "A", LastName = "B" };
        var plan = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Pro", Tier = SubscriptionTier.Pro };
        var transaction = new BillingTransaction
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            SubscriptionPlanId = plan.Id,
            Amount = 19.90m,
            Type = BillingTransactionType.Purchase,
            Description = "Purchased Pro",
            StripeSessionId = "cs_test_1"
        };
        db.Users.Add(user);
        db.SubscriptionPlans.Add(plan);
        db.BillingTransactions.Add(transaction);
        await db.SaveChangesAsync();
        // Reload the user alone: EF then does not track the transaction and cannot detach it itself, so
        // only the database's ON DELETE SET NULL can keep the row.
        db.ChangeTracker.Clear();

        db.Users.Remove(await db.Users.SingleAsync(u => u.Id == user.Id));
        await db.SaveChangesAsync();

        var kept = await db.BillingTransactions.AsNoTracking().SingleAsync();
        kept.Id.ShouldBe(transaction.Id);
        kept.UserId.ShouldBeNull();
        kept.Amount.ShouldBe(19.90m);
        kept.StripeSessionId.ShouldBe("cs_test_1");
    }

    [Fact]
    public async Task LessonExercise_LessonDeleted_CascadesPrescriptions()
    {
        await using var db = FitEdu.TestSupport.DbContextFactory.CreateSqlite();
        var lesson = SeedLessonWithExercise(db, NewExercise("squat", "Squat"));
        await db.SaveChangesAsync();
        // Reload the lesson alone: EF then does not track its prescriptions and cannot delete them
        // itself, so only the database's ON DELETE CASCADE can remove them.
        db.ChangeTracker.Clear();

        db.Lessons.Remove(await db.Lessons.SingleAsync(l => l.Id == lesson.Id));
        await db.SaveChangesAsync();

        (await db.LessonExercises.CountAsync()).ShouldBe(0);
        (await db.Exercises.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task LessonExercise_ExerciseUsedByLesson_CannotBeDeleted()
    {
        // Exercise → LessonExercise is Restrict: removing library data must never silently strip
        // a prescription out of a published lesson.
        await using var db = FitEdu.TestSupport.DbContextFactory.CreateSqlite();
        var exercise = NewExercise("plank", "Plank");
        SeedLessonWithExercise(db, exercise);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        db.Exercises.Remove(await db.Exercises.SingleAsync(e => e.Id == exercise.Id));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task FitnessColumns_ReadBackFromTheDatabase_WithTheirValuesAndDefaults()
    {
        // The user goal, the course fitness fields and the Greek testimonial and plan texts are
        // read back from SQLite after clearing the tracker, so each value really went through its column.
        await using var db = FitEdu.TestSupport.DbContextFactory.CreateSqlite();
        db.Users.Add(new ApplicationUser { Id = "with-goal", UserName = "a@x.com", FirstName = "A", LastName = "B", Goal = FitnessGoal.MuscleGain });
        db.Users.Add(new ApplicationUser { Id = "without-goal", UserName = "c@x.com", FirstName = "C", LastName = "D" });
        var fitnessCourse = new Course
        {
            Id = Guid.NewGuid(), TitleEn = "Fitness", Level = CourseLevel.Advanced, Goal = FitnessGoal.Endurance,
            Equipment = "bands,mat", EstimatedWeeks = 6, SessionsPerWeek = 3, CoverExerciseSlug = "plank"
        };
        var plainCourse = new Course { Id = Guid.NewGuid(), TitleEn = "Plain" };
        db.Courses.AddRange(fitnessCourse, plainCourse);
        db.Testimonials.Add(new Testimonial { FullName = "Maria", QuoteText = "Great", QuoteTextEl = "Τέλειο", RoleOrTitleEl = "Δρομέας" });
        db.SubscriptionPlans.Add(new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Pro", Tier = SubscriptionTier.Pro, DescriptionEl = "Όλα τα μαθήματα" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var goalsByUser = await db.Users.AsNoTracking().ToDictionaryAsync(user => user.Id, user => user.Goal);
        var savedFitnessCourse = await db.Courses.AsNoTracking().SingleAsync(course => course.Id == fitnessCourse.Id);
        var savedPlainCourse = await db.Courses.AsNoTracking().SingleAsync(course => course.Id == plainCourse.Id);
        var testimonial = await db.Testimonials.AsNoTracking().SingleAsync();
        var plan = await db.SubscriptionPlans.AsNoTracking().SingleAsync();

        goalsByUser["with-goal"].ShouldBe(FitnessGoal.MuscleGain);
        goalsByUser["without-goal"].ShouldBeNull();
        savedFitnessCourse.Level.ShouldBe(CourseLevel.Advanced);
        savedFitnessCourse.Goal.ShouldBe(FitnessGoal.Endurance);
        savedFitnessCourse.Equipment.ShouldBe("bands,mat");
        savedFitnessCourse.EstimatedWeeks.ShouldBe(6);
        savedFitnessCourse.SessionsPerWeek.ShouldBe(3);
        savedFitnessCourse.CoverExerciseSlug.ShouldBe("plank");
        savedPlainCourse.Level.ShouldBe(CourseLevel.Beginner);
        savedPlainCourse.Goal.ShouldBe(FitnessGoal.GeneralFitness);
        savedPlainCourse.Equipment.ShouldBeNull();
        savedPlainCourse.CoverExerciseSlug.ShouldBeNull();
        testimonial.QuoteTextEl.ShouldBe("Τέλειο");
        testimonial.RoleOrTitleEl.ShouldBe("Δρομέας");
        plan.DescriptionEl.ShouldBe("Όλα τα μαθήματα");
    }

    private static Exercise NewExercise(string slug, string nameEn) => new()
    {
        Id = Guid.NewGuid(),
        Slug = slug,
        NameEn = nameEn,
        Category = "strength",
        Level = "beginner",
        PrimaryMuscles = "chest",
        SecondaryMuscles = "",
        InstructionsEn = "Lower.\nPush."
    };

    private static Lesson SeedLessonWithExercise(ApplicationDbContext db, Exercise exercise)
    {
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
        var module = new Module { Id = Guid.NewGuid(), TitleEn = "M", CourseId = course.Id };
        var lesson = new Lesson { Id = Guid.NewGuid(), TitleEn = "L", ModuleId = module.Id };
        var prescription = new LessonExercise
        {
            Id = Guid.NewGuid(),
            LessonId = lesson.Id,
            ExerciseId = exercise.Id,
            SortOrder = 1,
            Sets = 3,
            Reps = "8-12",
            RestSeconds = 60
        };
        db.Courses.Add(course);
        db.Modules.Add(module);
        db.Lessons.Add(lesson);
        db.Exercises.Add(exercise);
        db.LessonExercises.Add(prescription);
        return lesson;
    }

    [Fact]
    public async Task DateTimeProperty_MaterializesWithUtcKind()
    {
        // DB-6: was Kind=Unspecified on read (only *.UtcNow-by-convention, nothing pinned the
        // Kind on materialization) — a fresh context instance re-reading a persisted row is the
        // only way to prove the converter runs on read, not just echoes back the in-memory value.
        var dbName = Guid.NewGuid().ToString("N");
        Guid courseId;
        await using (var db = CtxWithUser("u", dbName))
        {
            var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
            courseId = course.Id;
            db.Courses.Add(course);
            db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = "u1", CourseId = course.Id });
            await db.SaveChangesAsync();
        }

        await using var freshDb = CtxWithUser("u", dbName);
        var enrollment = await freshDb.Enrollments.AsNoTracking().FirstAsync(e => e.CourseId == courseId);

        enrollment.EnrolledAt.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public async Task DateTimeProperty_WrittenAsLocalTime_IsStoredAsTheSameUtcMoment()
    {
        // The column holds UTC and materialization says Utc, so a Local value must be converted on write.
        // The local value is built from a fixed UTC moment, so the check holds in any machine time zone.
        var utcMoment = new DateTime(2026, 7, 1, 9, 30, 0, DateTimeKind.Utc);
        var localMoment = utcMoment.ToLocalTime();
        await using var db = FitEdu.TestSupport.DbContextFactory.CreateSqlite();
        var user = new ApplicationUser { Id = "u1", UserName = "u@x.com", Email = "u@x.com", FirstName = "A", LastName = "B" };
        var course = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
        db.Users.Add(user);
        db.Courses.Add(course);
        db.Enrollments.Add(new Enrollment { Id = Guid.NewGuid(), UserId = user.Id, CourseId = course.Id, EnrolledAt = localMoment });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var enrollment = await db.Enrollments.AsNoTracking().SingleAsync();

        enrollment.EnrolledAt.ShouldBe(utcMoment);
        enrollment.EnrolledAt.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public async Task ConcurrentEdit_StaleRowVersion_ThrowsConcurrencyException()
    {
        // DB-7: RowVersion is DB-generated in production (SQL Server's rowversion type is
        // auto-incremented by the engine itself on every UPDATE — not something the app sets,
        // and not something the InMemory test provider can simulate on its own). This proves
        // EF's optimistic-concurrency check is correctly wired for Lesson — the same WHERE-clause
        // comparison AdminLessonsController.UpdateLesson's try/catch relies on — by manufacturing
        // the "stale original value" state a real concurrent SQL Server write would leave behind.
        var dbName = Guid.NewGuid().ToString("N");
        Guid lessonId;
        await using (var seedDb = CtxWithUser("seed", dbName))
        {
            var seedCourse = new Course { Id = Guid.NewGuid(), TitleEn = "C" };
            var seedModule = new Module { Id = Guid.NewGuid(), TitleEn = "M", CourseId = seedCourse.Id };
            var seedLesson = new Lesson { Id = Guid.NewGuid(), TitleEn = "L", ModuleId = seedModule.Id };
            lessonId = seedLesson.Id;
            seedDb.Courses.Add(seedCourse);
            seedDb.Modules.Add(seedModule);
            seedDb.Lessons.Add(seedLesson);
            await seedDb.SaveChangesAsync();
        }

        await using var db = CtxWithUser("editor", dbName);
        var lesson = await db.Lessons.FirstAsync(l => l.Id == lessonId);
        lesson.TitleEn = "Edited";

        // Simulate another writer having already changed the row version in between this
        // request's read and its save (what SQL Server would really do on a concurrent UPDATE).
        db.Entry(lesson).OriginalValues[nameof(Lesson.RowVersion)] = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void LessonCompletions_HaveNoPlainUserIdIndex_TheUniqueUserAndLessonIndexServesIt()
    {
        // DB-12: the unique (UserId, LessonId) index already serves a seek on the UserId prefix.
        using var db = CtxWithUser(null);

        var indexes = db.Model.FindEntityType(typeof(LessonCompletion))!.GetIndexes().ToList();

        indexes.ShouldNotContain(index => index.Properties.Count == 1 && index.Properties[0].Name == nameof(LessonCompletion.UserId));
        indexes.ShouldContain(index => index.IsUnique && index.Properties.Count == 2);
    }

    [Fact]
    public void UserSubscriptions_HaveOneUserIdIndex_TheFilteredUniqueOne()
    {
        // DB-9: a plain UserId index declared next to the filtered unique one was merged into it by EF,
        // so the model (and every migration) holds just the filtered unique index; the configuration says so.
        using var db = CtxWithUser(null);

        var userIdIndexes = db.Model.FindEntityType(typeof(UserSubscription))!.GetIndexes()
            .Where(index => index.Properties.Select(property => property.Name).SequenceEqual([nameof(UserSubscription.UserId)]))
            .ToList();

        var only = userIdIndexes.ShouldHaveSingleItem();
        only.IsUnique.ShouldBeTrue();
        only.GetFilter().ShouldBe("[IsActive] = 1");
    }

    [Fact]
    public async Task SecondActiveSubscriptionForOneUser_IsRefusedByTheUniqueIndex_OnSqlite()
    {
        // DQ-6: SQLite builds the filtered unique index ([IsActive] = 1) too, so the "at most one
        // active subscription per user" rule is enforced by the database under test, not only in
        // the service code. A second inactive (history) row for the same user stays allowed.
        await using var db = FitEdu.TestSupport.DbContextFactory.CreateSqlite();
        var user = new ApplicationUser { Id = "u1", UserName = "u@x.com", Email = "u@x.com", FirstName = "A", LastName = "B" };
        var plan = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Pro", Tier = SubscriptionTier.Pro };
        db.Users.Add(user);
        db.SubscriptionPlans.Add(plan);
        db.UserSubscriptions.Add(new UserSubscription { Id = Guid.NewGuid(), UserId = user.Id, SubscriptionPlanId = plan.Id, IsActive = true });
        await db.SaveChangesAsync();

        db.UserSubscriptions.Add(new UserSubscription { Id = Guid.NewGuid(), UserId = user.Id, SubscriptionPlanId = plan.Id, IsActive = false });
        await db.SaveChangesAsync();

        db.UserSubscriptions.Add(new UserSubscription { Id = Guid.NewGuid(), UserId = user.Id, SubscriptionPlanId = plan.Id, IsActive = true });
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
