namespace UbiSlot.Core;

public class SpoolRecord
{
    public uint AchievementId { get; init; }

    public long UnlockTimestamp { get; init; }

    public byte[] RawBytes { get; init; } = [];

    public DateTime UnlockTimeUtc =>
        DateTimeOffset
            .FromUnixTimeSeconds(UnlockTimestamp)
            .UtcDateTime;
}