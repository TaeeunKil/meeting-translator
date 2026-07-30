using MeetingTranslator.Services;

namespace MeetingTranslator.Tests;

public class MonthlyUsageGuardTests
{
    [Fact]
    public void TryReserve_BlocksBeforeConfiguredLimitIsExceeded()
    {
        var path = TemporaryUsagePath();
        try
        {
            var guard = new MonthlyUsageGuard(
                path,
                new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero)));

            Assert.True(guard.TryReserve(4, 5));
            Assert.False(guard.TryReserve(2, 5));
            Assert.Equal(4, guard.CharactersUsed);

            guard.Release(2);
            Assert.Equal(2, guard.CharactersUsed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CharactersUsed_ResetsWhenUtcMonthChanges()
    {
        var path = TemporaryUsagePath();
        try
        {
            var july = new MonthlyUsageGuard(
                path,
                new FixedTimeProvider(new DateTimeOffset(2026, 7, 31, 0, 0, 0, TimeSpan.Zero)));
            Assert.True(july.TryReserve(100, 490_000));

            var august = new MonthlyUsageGuard(
                path,
                new FixedTimeProvider(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)));
            Assert.Equal(0, august.CharactersUsed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CountBillableCharacters_CountsUnicodeCodePoints()
    {
        Assert.Equal(3, MonthlyUsageGuard.CountBillableCharacters("A😀한"));
    }

    private static string TemporaryUsagePath() =>
        Path.Combine(Path.GetTempPath(), $"meeting-translator-usage-{Guid.NewGuid():N}.json");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
