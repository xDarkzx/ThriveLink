namespace ThriveLink.Domain.Tests;

public class ClaimRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 28); // a Wednesday

    [Theory]
    [InlineData("2026-10-26", "2026-11-02")] // Monday -> next Monday
    [InlineData("2026-10-30", "2026-11-06")] // Friday -> next Friday
    [InlineData("2026-10-31", "2026-11-06")] // Saturday -> next Friday
    public void Due_date_is_five_working_days_later(string submitted, string expected) =>
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            ClaimRules.DueBy(DateOnly.Parse(submitted, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Clean_reimbursement_has_no_flags_and_is_likely_approve()
    {
        var flags = ClaimRules.Flags(ClaimType.Reimbursement, Today.AddDays(-10), 180m, documentCount: 1, duplicateOf: null, Today);
        Assert.Empty(flags);
        Assert.Equal(TriageRecommendation.LikelyApprove, ClaimRules.RecommendationFor(flags));
    }

    [Fact]
    public void Reimbursement_without_receipt_needs_info()
    {
        var flags = ClaimRules.Flags(ClaimType.Reimbursement, Today.AddDays(-3), 90m, documentCount: 0, duplicateOf: null, Today);
        Assert.Contains(flags, f => f.StartsWith("No receipt", StringComparison.Ordinal));
        Assert.Equal(TriageRecommendation.NeedsInfo, ClaimRules.RecommendationFor(flags));
    }

    [Fact]
    public void High_value_duplicate_and_late_pre_approval_are_flagged_for_review()
    {
        var flags = ClaimRules.Flags(ClaimType.PreApproval, Today.AddDays(-1), 12_000m, documentCount: 1, duplicateOf: "CL-261020-0001", Today);
        Assert.Equal(3, flags.Count);
        Assert.Equal(TriageRecommendation.Review, ClaimRules.RecommendationFor(flags));
    }

    [Fact]
    public void Old_treatment_is_flagged() =>
        Assert.Contains(ClaimRules.Flags(ClaimType.Reimbursement, Today.AddMonths(-13), 50m, 1, null, Today),
            f => f.Contains("12 months", StringComparison.Ordinal));
}

public class RedactorTests
{
    [Theory]
    [InlineData("Call me on 021 555 0199 please", "Call me on [number] please")]
    [InlineData("phone +64 9 555 0123", "phone [number]")]
    [InlineData("email aroha.n@example.co.nz", "email [email]")]
    [InlineData("member TL100001 policy POL-1234567", "member [member-number] policy [policy-number]")]
    [InlineData("NHI ZZZ0016 and new NHI ZAA00AA", "NHI [nhi] and new NHI [nhi]")]
    [InlineData("born 03/07/1986", "born [date]")]
    public void Removes_identifiers(string input, string expected) =>
        Assert.Equal(expected, Redactor.Redact(input));

    [Fact]
    public void Keeps_clinical_meaning() =>
        Assert.Equal("Knee MRI after rugby injury, specialist referral attached", Redactor.Redact("Knee MRI after rugby injury, specialist referral attached"));

    [Fact]
    public void Handles_empty() => Assert.Equal(string.Empty, Redactor.Redact(null));
}
