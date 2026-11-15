namespace FitEdu.Application.Common;

/// <summary>
/// A small list of the passwords people choose most often, kept in the code so the check needs no
/// download and no call to an outside service. Passwords that Identity's length rule already turns away
/// are left out; the compare ignores letter case.
/// </summary>
public static class CommonPasswords
{
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "12345678", "123456789", "1234567890", "12345678910", "123123123", "123321123", "111111111", "1111111111",
        "11111111", "22222222", "33333333", "44444444", "55555555", "66666666", "77777777", "88888888", "99999999",
        "00000000", "12121212", "12341234", "11223344", "12344321", "87654321", "98765432", "987654321", "0987654321",
        "1q2w3e4r", "1q2w3e4r5t", "1q2w3e4r5t6y", "1qaz2wsx", "1qaz2wsx3edc", "1qazxsw2", "zaq12wsx", "zaq1zaq1",
        "q1w2e3r4", "q1w2e3r4t5", "q1w2e3r4t5y6", "123qweasd", "123qwe123", "1234qwer", "12qwaszx", "qazwsxedc",
        "qazwsx123", "qwertyui", "qwertyuiop", "qwertyuiop123", "qwerty12", "qwerty123", "qwerty1234", "qwerty12345",
        "qwerty123456", "qwertyqwerty", "asdfghjk", "asdfghjkl", "asdf1234", "asdfasdf", "asdfgh123", "zxcvbnm1",
        "zxcvbnm123", "zxcvbnmasdf", "1234abcd", "abcd1234", "abc12345", "abc123456", "abcdefgh", "abcdefg1",
        "abcdefgh1", "abcd12345", "a1234567", "a12345678", "a123456789", "aa123456", "aa12345678", "aaaaaaaa",
        "bbbbbbbb", "aaaa1111", "123456aa", "123456ab", "123456abc", "1234567a", "12345678a", "123456qwerty",
        "password", "password1", "password12", "password123", "password1234", "password12345", "password123456",
        "passw0rd", "passw0rd1", "p@ssw0rd", "p@ssword", "p@ssword1", "pa$$word", "password!", "password1!!",
        "pass1234", "pass12345", "passpass", "mypassword", "mypassword1", "mypassword123", "newpassword",
        "newpassword1", "letmein1", "letmein123", "letmein12", "welcome1", "welcome12", "welcome123", "welcome1234",
        "welcome@123", "admin123", "admin1234", "admin12345", "administrator", "administrator1", "adminadmin",
        "root1234", "rootroot", "toor1234", "changeme", "changeme1", "changeme123", "secret123", "secret12",
        "default1", "default123", "guest123", "guest1234", "test1234", "test12345", "testtest", "testing123",
        "testing1", "hello123", "hello1234", "hello12345", "helloworld", "helloworld1", "iloveyou", "iloveyou1",
        "iloveyou12", "iloveyou123", "iloveyou2", "iloveyou!", "loveyou123", "lovelove", "love1234", "monkey123",
        "monkey12", "dragon123", "dragon12", "football", "football1", "football123", "baseball", "baseball1",
        "basketball", "soccer123", "superman", "superman1", "batman123", "michael1", "jordan23", "jennifer",
        "jessica1", "charlie1", "sunshine", "sunshine1", "princess", "princess1", "princess123", "starwars",
        "starwars1", "trustno1", "whatever", "whatever1", "freedom1", "shadow123", "master123", "mustang1",
        "computer", "computer1", "internet", "internet1", "samsung1", "google123", "facebook1", "fitedu123",
        "fitness1", "fitness123", "workout1", "gymrat123", "summer2020", "summer2021", "summer2022", "summer2023",
        "summer2024", "summer2025", "winter2020", "winter2024", "winter2025", "spring2024", "spring2025",
        "autumn2024", "january1", "february1", "september1", "october1", "december1", "greece123", "athens123",
        "ellada123", "kalimera1", "agapi123", "kodikos1", "kodikos123"
    };

    /// <summary>True when <paramref name="password"/> is on the list (compared without regard to letter case).</summary>
    public static bool IsCommon(string? password) =>
        !string.IsNullOrEmpty(password) && Known.Contains(password);

    /// <summary>The list itself, for tests that check it.</summary>
    public static IReadOnlyCollection<string> All => Known;
}
