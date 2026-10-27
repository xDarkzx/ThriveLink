using System.Text.RegularExpressions;

namespace ThriveLink.Domain;

/// <summary>
/// Removes personal identifiers from free text before it leaves our systems (e.g. to an AI model).
/// Errs on the side of over-redacting: a redacted provider phone number costs nothing, a leaked one does.
/// </summary>
public static partial class Redactor
{
    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    // NZ National Health Index numbers: old format AAA9999, new format AAA99AA.
    [GeneratedRegex(@"\b[A-HJ-NP-Z]{3}(\d{4}|\d{2}[A-HJ-NP-Z]{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex Nhi();

    [GeneratedRegex(@"\bTL\d{6}\b", RegexOptions.IgnoreCase)]
    private static partial Regex MemberNumber();

    [GeneratedRegex(@"\bPOL-\d{5,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex PolicyNumber();

    // Phone numbers: +64 / 0 prefixes, spaces, dashes, brackets; 7+ digits overall.
    [GeneratedRegex(@"(\+?\d[\d\s\-()]{6,}\d)")]
    private static partial Regex PhoneOrLongNumber();

    // Dates of birth written as d/m/yyyy or d-m-yyyy.
    [GeneratedRegex(@"\b\d{1,2}[/-]\d{1,2}[/-](19|20)\d{2}\b")]
    private static partial Regex SlashDate();

    public static string Redact(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var s = Email().Replace(text, "[email]");
        s = MemberNumber().Replace(s, "[member-number]");
        s = PolicyNumber().Replace(s, "[policy-number]");
        s = Nhi().Replace(s, "[nhi]");
        s = SlashDate().Replace(s, "[date]");
        s = PhoneOrLongNumber().Replace(s, "[number]");
        return s;
    }
}
