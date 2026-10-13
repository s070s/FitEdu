using System.ComponentModel.DataAnnotations;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Application.DTOs;
/// <summary>
/// User profile information.
/// </summary>
public record ProfileDto(
    string UserId,
    string Email,
    string FirstName,
    string LastName,
    string? DisplayName,
    string? AvatarPath,
    DateOnly? DateOfBirth,
    UserStatus Status,
    FitnessGoal? Goal = null
);

/// <summary>
/// Request to update user profile. Status is the training level; a null Goal clears the
/// user's training goal.
/// </summary>
public record UpdateProfileRequest(
    [Required, MaxLength(100)] string FirstName,
    [Required, MaxLength(100)] string LastName,
    [MaxLength(100)] string? DisplayName,
    DateOnly? DateOfBirth,
    UserStatus Status,
    [EnumDataType(typeof(FitnessGoal))] FitnessGoal? Goal = null
);

/// <summary>
/// Request to change password.
/// </summary>
public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required(ErrorMessageResourceType = typeof(ErrorMessagesRes), ErrorMessageResourceName = nameof(ErrorMessagesRes.Auth_PasswordRequired))]
    [RegularExpression(@"^(?=.*[A-Z])(?=.*\d).{8,128}$", ErrorMessageResourceType = typeof(ErrorMessagesRes), ErrorMessageResourceName = nameof(ErrorMessagesRes.Auth_PasswordComplexity))]
    string NewPassword
);

/// <summary>
/// COMP-4: full personal-data export for the "download my data" GDPR access/portability
/// endpoint. Chat messages are limited to ones the user themselves sent — the other party's
/// messages in a shared conversation are their own personal data, not exportable through this
/// user's request.
/// </summary>
public record MyDataExportDto(
    ProfileExportDto Profile,
    List<EnrollmentExportDto> Enrollments,
    List<AssessmentSubmissionExportDto> AssessmentSubmissions,
    List<CertificateExportDto> Certificates,
    List<BillingTransactionExportDto> BillingTransactions,
    List<ChatMessageExportDto> ChatMessages
);

public record ProfileExportDto(
    string Email,
    string FirstName,
    string LastName,
    string? DisplayName,
    DateOnly? DateOfBirth,
    string Status,
    DateTime AccountCreatedAt,
    bool GdprConsentGiven,
    DateTime? GdprConsentDate,
    bool ParentalConsentGiven,
    string? Goal = null
);

public record EnrollmentExportDto(
    string CourseTitle,
    DateTime EnrolledAt,
    string Status,
    DateTime? CompletedAt
);

public record AssessmentSubmissionExportDto(
    string AssessmentTitle,
    string AnswersJson,
    string? SummaryJson,
    DateTimeOffset SubmittedAt
);

public record CertificateExportDto(
    string CourseTitle,
    Guid VerificationId,
    DateTime IssuedAt,
    string Status
);

public record BillingTransactionExportDto(
    string PlanName,
    decimal Amount,
    string Currency,
    string Type,
    string Description,
    DateTime CreatedAt
);

public record ChatMessageExportDto(
    Guid ConversationId,
    string Content,
    DateTime SentAt
);

/// <summary>
/// The member's training at a glance for the home dashboard. WorkoutsCompleted counts completed
/// lessons, MinutesTrained adds up their planned minutes, and CurrentStreak counts consecutive UTC
/// days with a completion up to today (or yesterday, while today's workout is still to come).
/// CurrentCourseId is the most recently enrolled programme that is still in progress; Goal and Level
/// are the member's own choices, which the dashboard recommends courses for.
/// </summary>
public record TrainingStatsDto(
    int WorkoutsCompleted,
    int MinutesTrained,
    int CurrentStreak,
    Guid? CurrentCourseId,
    FitnessGoal? Goal,
    CourseLevel? Level
);
