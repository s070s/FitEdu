using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;
using Shouldly;
using Xunit;

namespace FitEdu.Domain.Tests;

/// <summary>
/// Enums are persisted as ints and cast to/from ints in DTOs. These tests lock the
/// numeric values so a reorder or insert can't silently corrupt stored data or API output.
/// </summary>
public class EnumValueTests
{
    [Fact]
    public void SubscriptionTier_HasStableValues()
    {
        ((int)SubscriptionTier.Free).ShouldBe(0);
        ((int)SubscriptionTier.Plus).ShouldBe(1);
        ((int)SubscriptionTier.Pro).ShouldBe(2);
    }

    [Fact]
    public void SubscriptionTier_OrdersFreeBelowPlusBelowPro()
    {
        // Upgrade/downgrade logic in SubscriptionService relies on this ordering.
        (SubscriptionTier.Free < SubscriptionTier.Plus).ShouldBeTrue();
        (SubscriptionTier.Plus < SubscriptionTier.Pro).ShouldBeTrue();
    }

    [Fact]
    public void BillingPeriod_HasStableValues()
    {
        ((int)BillingPeriod.Lifetime).ShouldBe(0);
        ((int)BillingPeriod.Monthly).ShouldBe(1);
        ((int)BillingPeriod.Quarterly).ShouldBe(3);
        ((int)BillingPeriod.Yearly).ShouldBe(12);
    }

    [Fact]
    public void ContentType_HasStableValues()
    {
        ((int)ContentType.Text).ShouldBe(1);
        ((int)ContentType.Video).ShouldBe(2);
        ((int)ContentType.Pdf).ShouldBe(3);
    }

    [Fact]
    public void EnrollmentStatus_HasStableValues()
    {
        ((int)EnrollmentStatus.Active).ShouldBe(1);
        ((int)EnrollmentStatus.Completed).ShouldBe(2);
        ((int)EnrollmentStatus.Dropped).ShouldBe(3);
    }

    [Fact]
    public void CertificateStatus_HasStableValues()
    {
        ((int)CertificateStatus.Active).ShouldBe(1);
        ((int)CertificateStatus.Revoked).ShouldBe(2);
    }

    [Fact]
    public void UserStatus_HasStableValues()
    {
        ((int)UserStatus.Unknown).ShouldBe(0);
        ((int)UserStatus.Beginner).ShouldBe(1);
        ((int)UserStatus.Intermediate).ShouldBe(2);
        ((int)UserStatus.Advanced).ShouldBe(3);
        ((int)UserStatus.Other).ShouldBe(99);
    }

    [Fact]
    public void CourseLevel_HasStableValues()
    {
        ((int)CourseLevel.Beginner).ShouldBe(0);
        ((int)CourseLevel.Intermediate).ShouldBe(1);
        ((int)CourseLevel.Advanced).ShouldBe(2);
    }

    [Fact]
    public void FitnessGoal_HasStableValues()
    {
        ((int)FitnessGoal.GeneralFitness).ShouldBe(0);
        ((int)FitnessGoal.Strength).ShouldBe(1);
        ((int)FitnessGoal.MuscleGain).ShouldBe(2);
        ((int)FitnessGoal.FatLoss).ShouldBe(3);
        ((int)FitnessGoal.Endurance).ShouldBe(4);
        ((int)FitnessGoal.Mobility).ShouldBe(5);
        ((int)FitnessGoal.Recovery).ShouldBe(6);
        ((int)FitnessGoal.Nutrition).ShouldBe(7);
    }

    [Fact]
    public void AssistantSourceType_HasStableValues()
    {
        ((int)AssistantSourceType.Course).ShouldBe(1);
        ((int)AssistantSourceType.Lesson).ShouldBe(2);
        ((int)AssistantSourceType.Assessment).ShouldBe(3);
        ((int)AssistantSourceType.BlogArticle).ShouldBe(4);
    }

    [Fact]
    public void BillingTransactionType_HasStableValues()
    {
        ((int)BillingTransactionType.Purchase).ShouldBe(0);
        ((int)BillingTransactionType.Upgrade).ShouldBe(1);
        ((int)BillingTransactionType.Downgrade).ShouldBe(2);
        ((int)BillingTransactionType.Renewal).ShouldBe(3);
        ((int)BillingTransactionType.Refund).ShouldBe(4);
        ((int)BillingTransactionType.FreePlanAssignment).ShouldBe(5);
        ((int)BillingTransactionType.PlanSwitch).ShouldBe(6);
        ((int)BillingTransactionType.Expired).ShouldBe(7);
    }

    [Fact]
    public void CallEndReason_HasStableValues()
    {
        ((int)CallEndReason.Completed).ShouldBe(1);
        ((int)CallEndReason.Missed).ShouldBe(2);
        ((int)CallEndReason.Declined).ShouldBe(3);
        ((int)CallEndReason.Cancelled).ShouldBe(4);
    }

    [Fact]
    public void CallEventKind_HasStableValues()
    {
        ((int)CallEventKind.Started).ShouldBe(1);
        ((int)CallEventKind.Missed).ShouldBe(2);
        ((int)CallEventKind.Ended).ShouldBe(3);
    }

    [Fact]
    public void CallParticipantStatus_HasStableValues()
    {
        ((int)CallParticipantStatus.Invited).ShouldBe(1);
        ((int)CallParticipantStatus.Joined).ShouldBe(2);
        ((int)CallParticipantStatus.Declined).ShouldBe(3);
        ((int)CallParticipantStatus.Missed).ShouldBe(4);
        ((int)CallParticipantStatus.Left).ShouldBe(5);
    }

    [Fact]
    public void NotificationType_HasStableValues()
    {
        ((int)NotificationType.ChatMessage).ShouldBe(1);
        ((int)NotificationType.CertificateIssued).ShouldBe(2);
        ((int)NotificationType.SubscriptionActivated).ShouldBe(3);
        ((int)NotificationType.SubscriptionExpiring).ShouldBe(4);
        ((int)NotificationType.ReviewApproved).ShouldBe(5);
        ((int)NotificationType.SessionReminder).ShouldBe(6);
    }

    [Fact]
    public void ReviewStatus_HasStableValues()
    {
        ((int)ReviewStatus.Pending).ShouldBe(1);
        ((int)ReviewStatus.Approved).ShouldBe(2);
        ((int)ReviewStatus.Rejected).ShouldBe(3);
    }

    [Fact]
    public void ScheduledSessionStatus_HasStableValues()
    {
        ((int)ScheduledSessionStatus.Scheduled).ShouldBe(1);
        ((int)ScheduledSessionStatus.Live).ShouldBe(2);
        ((int)ScheduledSessionStatus.Ended).ShouldBe(3);
        ((int)ScheduledSessionStatus.Cancelled).ShouldBe(4);
    }
}
