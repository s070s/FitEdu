using Microsoft.AspNetCore.Identity;
using FitEdu.Domain.Identity;

namespace FitEdu.Application.Common;

/// <summary>
/// Spends the same time on an unknown email as a real password check takes. Without it, a sign-in for an
/// email that has no account answers measurably faster than one that hashes the password, which tells
/// a visitor which emails have an account.
/// </summary>
public static class UnknownUserPasswordCheck
{
    private static readonly PasswordHasher<ApplicationUser> Hasher = new();

    private static readonly ApplicationUser PlaceholderUser = new() { FirstName = string.Empty, LastName = string.Empty };

    private static readonly string PlaceholderHash = Hasher.HashPassword(PlaceholderUser, "placeholder-password-never-used");

    /// <summary>Hashes <paramref name="password"/> against a placeholder hash; the result is never used.</summary>
    public static void Run(string? password) =>
        Hasher.VerifyHashedPassword(PlaceholderUser, PlaceholderHash, password ?? string.Empty);
}
