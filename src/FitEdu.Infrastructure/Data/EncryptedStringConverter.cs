using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FitEdu.Infrastructure.Data;

/// <summary>
/// EF Core value converter that transparently encrypts a string column at rest using ASP.NET
/// Core Data Protection (COMP-2). Applied to the special-category assessment answer/summary
/// columns so their plaintext never touches the database file, an unfiltered backup, or an
/// admin's raw table view — only the application, holding the DataProtection key ring, can read
/// them.
///
/// The ciphertext is non-deterministic (a fresh IV per write), so a converted column must never
/// be filtered, ordered, or indexed on. That holds here: the only reads are whole-value
/// projections and <c>AssessmentSubmissionSearchExtensions.ApplySort</c> deliberately excludes
/// these columns. Storing a string as a (longer) string keeps the column type <c>nvarchar</c>,
/// so no schema migration is required.
///
/// Reads are tolerant of legacy plaintext: a value written before this converter existed will
/// fail <see cref="IDataProtector.Unprotect"/> and is returned as-is, so an existing database is
/// not broken. A legacy plaintext value is not upgraded by merely reading the row: EF Core writes
/// only the columns that changed, so the value becomes ciphertext only when that column itself is
/// written again, and it stays plaintext until then.
/// </summary>
public sealed class EncryptedStringConverter : ValueConverter<string, string>
{
    public EncryptedStringConverter(IDataProtector protector)
        : base(
            plaintext => protector.Protect(plaintext),
            stored => Decrypt(protector, stored))
    {
    }

    private static string Decrypt(IDataProtector protector, string stored)
    {
        try
        {
            return protector.Unprotect(stored);
        }
        catch (CryptographicException)
        {
            // Value predates encryption (or the key ring can't read it) — return the raw
            // string rather than crashing the read. It becomes ciphertext only when this column is
            // written again. The fallback is silent: an unreadable ciphertext (a lost key) also comes
            // back as-is, and the converter does not tell the two cases apart.
            return stored;
        }
    }
}
