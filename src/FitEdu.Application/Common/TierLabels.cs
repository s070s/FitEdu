using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;

namespace FitEdu.Application.Common;

/// <summary>
/// The member-facing name of a <see cref="SubscriptionTier"/> in the current UI language
/// ("Free" is "Δωρεάν" in Greek). Pages and the Application's own messages print tiers through this,
/// never through the enum name; CSS classes keep using the enum name.
/// </summary>
public static class TierLabels
{
    public static string Name(SubscriptionTier tier) => tier switch
    {
        SubscriptionTier.Free => PricingRes.TierFree,
        SubscriptionTier.Plus => PricingRes.TierPlus,
        SubscriptionTier.Pro => PricingRes.TierPro,
        _ => tier.ToString()
    };
}
