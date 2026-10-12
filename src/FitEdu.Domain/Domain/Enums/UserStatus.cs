namespace FitEdu.Domain.Enums;

/// <summary>
/// The user's self-reported training level (the "Status" field on register/profile).
/// Stored as int in DB: the values are kept from the earlier career-era names
/// (Student/Graduate/NEET), so existing rows map onto Beginner/Intermediate/Advanced.
/// Add values as needed; never renumber or remove existing ones.
/// </summary>
public enum UserStatus
{
    Unknown = 0,
    Beginner = 1,
    Intermediate = 2,
    Advanced = 3,
    Other = 99
}
