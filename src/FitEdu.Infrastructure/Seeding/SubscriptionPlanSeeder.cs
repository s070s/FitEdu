using Microsoft.EntityFrameworkCore;
using FitEdu.Infrastructure.Data;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Application.DTOs;
using System.Text.Json;

namespace FitEdu.Infrastructure.Seeding;

/// <summary>
/// Seeds the three default subscription plans (Free, Plus, Pro), each described in English and Greek.
/// Idempotent: skips if plans already exist.
/// Live classes are open to every signed-in member, so no plan description sells them.
/// </summary>
public static class SubscriptionPlanSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db, ILogger logger)
    {
        if (await db.SubscriptionPlans.AnyAsync())
        {
            logger.LogInformation("Subscription plans already seeded. Skipping.");
            return;
        }

        var plans = new List<SubscriptionPlan>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Free",
                Description = "Start free with bodyweight, mobility and other starter programmes, the full exercise library and 1 active programme at a time.",
                DescriptionEl = "Ξεκίνα δωρεάν με προγράμματα βάρους σώματος, κινητικότητας και άλλα προγράμματα για αρχάριους, την πλήρη βιβλιοθήκη ασκήσεων και 1 ενεργό πρόγραμμα ταυτόχρονα.",
                Price = 0m,
                BillingPeriod = BillingPeriod.Lifetime,
                Tier = SubscriptionTier.Free,
                FeaturesJson = JsonSerializer.Serialize(new PlanFeaturesDto
                {
                    MaxCourses = 1,
                    AssessmentAccess = false,
                    CertificateAccess = false,
                    PrioritySupport = false
                }),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Plus",
                Description = "Every Free and Plus programme, all self-assessments and up to 10 active programmes at a time.",
                DescriptionEl = "Όλα τα προγράμματα των πλάνων Δωρεάν και Plus, όλες οι αξιολογήσεις και έως 10 ενεργά προγράμματα ταυτόχρονα.",
                Price = 9.99m,
                BillingPeriod = BillingPeriod.Monthly,
                Tier = SubscriptionTier.Plus,
                FeaturesJson = JsonSerializer.Serialize(new PlanFeaturesDto
                {
                    MaxCourses = 10,
                    AssessmentAccess = true,
                    CertificateAccess = false,
                    PrioritySupport = false
                }),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Pro",
                Description = "Everything in Plus, plus the advanced Pro programmes, unlimited active programmes, completion certificates and priority support.",
                DescriptionEl = "Όλα όσα περιλαμβάνει το Plus, μαζί με τα προχωρημένα προγράμματα Pro, απεριόριστα ενεργά προγράμματα, πιστοποιητικά ολοκλήρωσης και υποστήριξη με προτεραιότητα.",
                Price = 19.99m,
                BillingPeriod = BillingPeriod.Monthly,
                Tier = SubscriptionTier.Pro,
                FeaturesJson = JsonSerializer.Serialize(new PlanFeaturesDto
                {
                    MaxCourses = int.MaxValue,
                    AssessmentAccess = true,
                    CertificateAccess = true,
                    PrioritySupport = true
                }),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            }
        };

        db.SubscriptionPlans.AddRange(plans);
        await db.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} subscription plans (Free, Plus, Pro).", plans.Count);
    }
}
