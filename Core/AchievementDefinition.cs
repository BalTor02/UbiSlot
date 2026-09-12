namespace UbiSlot.Core;

public class AchievementDefinition
{
    public uint AchievementId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string? IconPath { get; init; }

    public bool IsUnlocked { get; set; }

    public DateTime? UnlockTimeUtc { get; set; }
}