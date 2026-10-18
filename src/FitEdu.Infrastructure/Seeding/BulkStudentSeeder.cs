using Microsoft.AspNetCore.Identity;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;

namespace FitEdu.Infrastructure.Seeding;

/// <summary>
/// Generates a large cohort of realistic Greek-named student accounts for development and load-testing.
/// Idempotent: skips emails that already exist.
/// </summary>
public static partial class BulkStudentSeeder
{
    private static readonly string[] MaleFirstNames =
    [
        "Αλέξανδρος", "Νίκος", "Δημήτρης", "Γιώργης", "Κώστας",
        "Παναγιώτης", "Βασίλης", "Χρήστος", "Ανδρέας", "Σταύρος",
        "Θανάσης", "Μιχάλης", "Σπύρος", "Γιάννης", "Λεωνίδας",
        "Πέτρος", "Θεόδωρος", "Ιωάννης", "Αντώνης", "Στέλιος",
        "Φώτης", "Τάσος", "Κλεάνθης", "Ηρακλής", "Δαμιανός",
        "Ευάγγελος", "Μάριος", "Αριστείδης", "Ορέστης", "Αγησίλαος",
        "Ελευθέριος", "Ξενοφώντας", "Αχιλλέας", "Σωκράτης", "Περικλής",
        "Ζήσης", "Κυριάκος", "Τηλέμαχος", "Ανέστης", "Γεράσιμος"
    ];

    private static readonly string[] FemaleFirstNames =
    [
        "Μαρία", "Ελένη", "Σοφία", "Αναστασία", "Κατερίνα",
        "Δήμητρα", "Χριστίνα", "Γεωργία", "Ευαγγελία", "Νικολέτα",
        "Ιωάννα", "Αθηνά", "Αλεξάνδρα", "Παρασκευή", "Βασιλική",
        "Αγγελική", "Στεφανία", "Θεοδώρα", "Φωτεινή", "Μαγδαληνή",
        "Πηνελόπη", "Αρετή", "Ζωή", "Καλλιόπη", "Χαρίκλεια",
        "Ολυμπία", "Ρόζα", "Ειρήνη", "Σταματία", "Σοφοκλέα",
        "Δέσποινα", "Νεφέλη", "Αριάδνη", "Μελπομένη", "Θάλεια",
        "Ευρυδίκη", "Ανθή", "Αλκμήνη", "Κλεοπάτρα", "Ηρώ"
    ];

    private static readonly string[] LastNames =
    [
        "Παπαδόπουλος", "Παπαδημητρίου", "Αλεξίου", "Νικολάου", "Κωνσταντίνου",
        "Παπαγεωργίου", "Γεωργίου", "Χριστοδούλου", "Δημητρίου", "Αντωνίου",
        "Παπαδάκης", "Σταυρόπουλος", "Μαρκόπουλος", "Καλογερόπουλος", "Θεοδωρόπουλος",
        "Οικονόμου", "Ιωάννου", "Κεφαλάς", "Τσιώλης", "Βαρδάκας",
        "Μητρόπουλος", "Κωνσταντίνου", "Χατζής", "Ζαφειρόπουλος", "Λεωνίδου",
        "Τζανετάκης", "Μαρινόπουλος", "Κολοκοτρώνης", "Βενιζέλος", "Τσούκαλης",
        "Παπανικολάου", "Σαμαράς", "Κωστόπουλος", "Παπαμιχαήλ", "Γκίκας",
        "Παπαχρήστου", "Ρούσσος", "Φιλίππου", "Κυριαζής", "Λάμπρου",
        "Μπακογιάννης", "Τριανταφύλλου", "Στεργίου", "Ζαχαρόπουλος", "Νούσιας",
        "Μπούρας", "Πολίτης", "Σταματόπουλος", "Παπαφιλίππου", "Δελής",
        "Κατσαρός", "Βουλγαράκης", "Μανωλόπουλος", "Ζερβός", "Πετράκης",
        "Σκουλάς", "Κουτσούμπας", "Βλάχος", "Χαλκιάς", "Γλυνός"
    ];

    public static async Task SeedAsync(
        UserManager<ApplicationUser> userManager,
        int count,
        string password,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var seededCount = 0;
        var skippedCount = 0;
        var allFirstNames = MaleFirstNames.Concat(FemaleFirstNames).ToArray();
        var rng = new Random(42); // deterministic seed for reproducibility

        LogGenerating(logger, count);

        for (var i = 1; i <= count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var firstName = allFirstNames[rng.Next(allFirstNames.Length)];
            var lastName = LastNames[rng.Next(LastNames.Length)];

            // Transliterate to ASCII-safe email using index to guarantee uniqueness
            var emailBase = Transliterate(firstName) + "." + Transliterate(lastName);
            var email = $"{emailBase}{i}@fitedu.local".ToLowerInvariant();

            if (await userManager.FindByEmailAsync(email) is not null)
            {
                skippedCount++;
                continue;
            }

            var student = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                Status = UserStatus.Beginner,
                EmailConfirmed = true,
                IsEnabled = true,
                GdprConsentGiven = true,
                GdprConsentDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(student, password);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(student, "Student");
                seededCount++;
            }
            else
            {
                LogCreateFailed(logger, email, string.Join(", ", result.Errors.Select(e => e.Description)));
            }

            if (seededCount % 1000 == 0 && seededCount > 0)
                LogProgress(logger, seededCount, count);
        }

        LogDone(logger, seededCount, skippedCount);
    }

    /// <summary>
    /// Simple Greek → Latin transliteration for building email addresses.
    /// </summary>
    private static string Transliterate(string greek)
    {
        var map = new Dictionary<char, string>()
        {
            { 'α' , "a" } , { 'β' , "v" } , { 'γ' , "g" } , { 'δ' , "d" } ,
            { 'ε' , "e" } , { 'ζ' , "z" } , { 'η' , "i" } , { 'θ' , "th" } ,
            { 'ι' , "i" } , { 'κ' , "k" } , { 'λ' , "l" } , { 'μ' , "m" } ,
            { 'ν' , "n" } , { 'ξ' , "x" } , { 'ο' , "o" } , { 'π' , "p" } ,
            { 'ρ' , "r" } , { 'σ' , "s" } , { 'ς' , "s" } , { 'τ' , "t" } ,
            { 'υ' , "y" } , { 'φ' , "f" } , { 'χ' , "ch" } , { 'ψ' , "ps" } ,
            { 'ω' , "o" } ,
            { 'ά' , "a" } , { 'έ' , "e" } , { 'ή' , "i" } , { 'ί' , "i" } ,
            { 'ό' , "o" } , { 'ύ' , "y" } , { 'ώ' , "o" } , { 'ϊ' , "i" } ,
            { 'ΐ' , "i" } , { 'ϋ' , "y" } , { 'ΰ' , "y" }
        };

        var sb = new System.Text.StringBuilder(greek.Length * 2);
        foreach (var ch in greek.ToLowerInvariant())
        {
            if (map.TryGetValue(ch, out var latin))
                sb.Append(latin);
            else if (char.IsAsciiLetterOrDigit(ch))
                sb.Append(ch);
            // skip everything else (spaces, diacritics not in map)
        }
        return sb.ToString();
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.

    [LoggerMessage(Level = LogLevel.Information, Message = "BulkStudentSeeder: generating up to {Count} students...")]
    private static partial void LogGenerating(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "BulkStudentSeeder: failed to create '{Email}': {Errors}")]
    private static partial void LogCreateFailed(ILogger logger, string email, string errors);

    [LoggerMessage(Level = LogLevel.Information, Message = "BulkStudentSeeder: {Count}/{Total} students created...")]
    private static partial void LogProgress(ILogger logger, int count, int total);

    [LoggerMessage(Level = LogLevel.Information, Message = "BulkStudentSeeder: done — {Seeded} created, {Skipped} skipped (already existed).")]
    private static partial void LogDone(ILogger logger, int seeded, int skipped);}
