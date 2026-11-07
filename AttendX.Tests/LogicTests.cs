using AttendX.Models;
using AttendX.Services;
using Xunit;

namespace AttendX.Tests;

public class YearHelperTests
{
    [Theory]
    [InlineData(1, 1)] [InlineData(2, 1)] [InlineData(3, 2)] [InlineData(4, 2)]
    [InlineData(5, 3)] [InlineData(6, 3)] [InlineData(7, 4)] [InlineData(8, 4)]
    public void Semester_maps_to_year(int sem, int year) => Assert.Equal(year, YearHelper.FromSemester(sem));

    [Theory] [InlineData(0)] [InlineData(9)] [InlineData(-1)]
    public void Invalid_semester_gives_zero(int sem) => Assert.Equal(0, YearHelper.FromSemester(sem));
}

public class AttendanceMathTests
{
    [Fact] public void Percent_counts_present_only_when_late_weight_is_zero() => Assert.Equal(60.0m, AttendanceMath.Percent(6, 2, 10, 0m));
    [Fact] public void Percent_counts_late_as_present_when_weight_is_one() => Assert.Equal(80.0m, AttendanceMath.Percent(6, 2, 10, 1m));
    [Fact] public void Percent_half_weight() => Assert.Equal(70.0m, AttendanceMath.Percent(6, 2, 10, 0.5m));
    [Fact] public void Percent_of_zero_total_is_zero() => Assert.Equal(0m, AttendanceMath.Percent(0, 0, 0));

    [Theory]
    [InlineData(90, 75, "Safe")] [InlineData(85, 75, "Safe")] [InlineData(80, 75, "Watch")] [InlineData(75, 75, "Watch")]
    [InlineData(70, 75, "At Risk")] [InlineData(65, 75, "At Risk")] [InlineData(60, 75, "Critical")]
    public void Risk_levels(double pct, double th, string expected) => Assert.Equal(expected, AttendanceMath.Risk((decimal)pct, (decimal)th));

    [Fact]
    public void ClassesNeeded_reaches_threshold_exactly()
    {
        // 6 present of 10 (60%) needs x more: (6+x)/(10+x) >= 0.75  => x >= 6
        Assert.Equal(6, AttendanceMath.ClassesNeeded(6, 10, 75));
        Assert.True((6m + 6) / (10 + 6) >= 0.75m);
        Assert.False((6m + 5) / (10 + 5) >= 0.75m);
    }
    [Fact] public void ClassesNeeded_zero_when_already_above() => Assert.Equal(0, AttendanceMath.ClassesNeeded(9, 10, 75));
    [Fact] public void ClassesNeeded_impossible_for_100_percent() => Assert.Equal(-1, AttendanceMath.ClassesNeeded(9, 10, 100));
}

public class ThresholdResolverTests
{
    [Fact]
    public void Section_rule_beats_semester_and_branch_rule()
    {
        var rules = new[]
        {
            new ThresholdRule { BranchId = 1, Percent = 70 },
            new ThresholdRule { BranchId = 1, Semester = 5, Percent = 72 },
            new ThresholdRule { BranchId = 1, Semester = 5, SectionId = 9, Percent = 80 }
        };
        Assert.Equal(80, ThresholdResolver.Resolve(rules, 1, 5, 9, 75));
        Assert.Equal(72, ThresholdResolver.Resolve(rules, 1, 5, 8, 75));
        Assert.Equal(70, ThresholdResolver.Resolve(rules, 1, 3, 8, 75));
        Assert.Equal(75, ThresholdResolver.Resolve(rules, 2, 5, 9, 75));   // other branch -> default
    }
}

public class AttendanceWindowTests
{
    private static readonly DateTime Day = new(2026, 10, 12);
    private static readonly TimeSpan S = new(10, 0, 0), E = new(10, 55, 0);

    [Fact] public void Open_inside_period() => Assert.True(AttendanceWindow.IsOpen(Day.AddHours(10).AddMinutes(20), Day, S, E, 10));
    [Fact] public void Open_during_grace_before_start() => Assert.True(AttendanceWindow.IsOpen(Day.AddHours(9).AddMinutes(51), Day, S, E, 10));
    [Fact] public void Closed_before_grace() => Assert.False(AttendanceWindow.IsOpen(Day.AddHours(9).AddMinutes(49), Day, S, E, 10));
    [Fact] public void Closed_after_grace() => Assert.False(AttendanceWindow.IsOpen(Day.AddHours(11).AddMinutes(6), Day, S, E, 10));
    [Fact] public void Closed_on_another_day() => Assert.False(AttendanceWindow.IsOpen(Day.AddDays(1).AddHours(10), Day, S, E, 10));
    [Fact] public void State_names() { Assert.Equal("Upcoming", AttendanceWindow.State(Day.AddHours(8), Day, S, E, 10)); Assert.Equal("Open", AttendanceWindow.State(Day.AddHours(10), Day, S, E, 10)); Assert.Equal("Closed", AttendanceWindow.State(Day.AddHours(13), Day, S, E, 10)); }
}

public class StudentIdTests
{
    [Fact] public void Prefix_format() => Assert.Equal("3EC2-ECE-", StudentIdService.Prefix(3, "EC2", "ECE"));
    [Fact] public void First_id_is_001() => Assert.Equal("1EV1-CSE-001", StudentIdService.Format("1EV1-CSE-", StudentIdService.NextSequence([], "1EV1-CSE-")));
    [Fact]
    public void Next_sequence_uses_highest_matching_number()
    {
        var ids = new[] { "3EC2-ECE-001", "3EC2-ECE-013", "3EC2-ECE-007", "3EC1-ECE-099", "5EC2-ECE-050" };
        Assert.Equal(14, StudentIdService.NextSequence(ids, "3EC2-ECE-"));
        Assert.Equal("3EC2-ECE-014", StudentIdService.Format("3EC2-ECE-", 14));
    }
}

public class QrSignerTests
{
    private readonly QrSigner _s = new("unit-test-secret");

    [Fact]
    public void Student_payload_roundtrip()
    {
        var p = _s.BuildStudentPayload(42, "3EC2-ECE-014");
        Assert.True(_s.TryParseStudentPayload(p, out var id, out var code));
        Assert.Equal(42, id); Assert.Equal("3EC2-ECE-014", code);
    }
    [Fact] public void Tampered_id_is_rejected() { var p = _s.BuildStudentPayload(42, "A-001").Replace("AX1.42.", "AX1.43."); Assert.False(_s.TryParseStudentPayload(p, out _, out _)); }
    [Fact] public void Tampered_code_is_rejected() { var p = _s.BuildStudentPayload(42, "A-001").Replace("A-001", "A-002"); Assert.False(_s.TryParseStudentPayload(p, out _, out _)); }
    [Fact] public void Other_secret_is_rejected() { var p = new QrSigner("another").BuildStudentPayload(1, "X-1"); Assert.False(_s.TryParseStudentPayload(p, out _, out _)); }
    [Theory] [InlineData("")] [InlineData("hello")] [InlineData("AX1.1.2")] [InlineData("AX2.1.A.B")]
    public void Garbage_is_rejected(string p) => Assert.False(_s.TryParseStudentPayload(p, out _, out _));

    [Fact]
    public void Session_token_is_valid_now_and_for_the_previous_window_only()
    {
        var t0 = new DateTimeOffset(2026, 10, 12, 10, 0, 5, TimeSpan.Zero);
        var token = _s.CreateSessionToken(7, t0, 30);
        Assert.True(_s.ValidateSessionToken(7, token, t0.AddSeconds(10), 30));
        Assert.True(_s.ValidateSessionToken(7, token, t0.AddSeconds(30), 30));    // previous window allowed
        Assert.False(_s.ValidateSessionToken(7, token, t0.AddSeconds(95), 30));   // expired -> replay refused
        Assert.False(_s.ValidateSessionToken(8, token, t0, 30));                  // token of another session
    }
}

public class CsvParserTests
{
    [Fact]
    public void Parses_quotes_commas_and_crlf()
    {
        var rows = AttendX.Infrastructure.CsvParser.Parse("a,b,c\r\n1,\"x, y\",\"he said \"\"hi\"\"\"\r\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal("x, y", rows[1][1]);
        Assert.Equal("he said \"hi\"", rows[1][2]);
    }

    [Fact]
    public void TestSupabaseRegions()
    {
        string[] regions = ["us-east-1", "us-west-1", "us-west-2", "eu-central-1", "eu-west-1", "ap-south-1", "ap-southeast-1", "ap-southeast-2"];
        foreach (var r in regions)
        {
            string host = $"aws-0-{r}.pooler.supabase.com";
            string cs = $"Host={host};Port=6543;Database=postgres;Username=postgres.eyhrnixctudjjbokyjzk;Password=KOTAREDDY9493;SslMode=Require;Timeout=3";
            try
            {
                using var conn = new Npgsql.NpgsqlConnection(cs);
                conn.Open();
                System.Console.WriteLine($"SUCCESS REGION FOUND: {r}");
                return;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Failed {r}: {ex.Message}");
            }
        }
    }
}
