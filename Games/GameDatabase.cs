using System.IO;
using System.Text;

namespace UbiSlot.Games;

public class GameDatabase
{
    private readonly Dictionary<string, string> _games =
        new(StringComparer.OrdinalIgnoreCase);

    public string DatabasePath { get; private set; } = string.Empty;

    public int GameCount =>
        _games.Count;

    public GameDatabase()
    {
        Load();
    }

    public string? GetGameName(
        string gameId)
    {
        return _games.TryGetValue(
            gameId,
            out string? name)
                ? name
                : null;
    }

    public string GetDebugInfo(
        string gameId)
    {
        string exists =
            File.Exists(DatabasePath)
                ? "YES"
                : "NO";

        string lookup =
            GetGameName(gameId)
            ?? "NULL";

        return
            $"Path:\n{DatabasePath}\n\n" +
            $"File exists: {exists}\n\n" +
            $"Games loaded: {GameCount}\n\n" +
            $"Lookup ID {gameId}:\n{lookup}";
    }

    private void Load()
    {
        DatabasePath =
            Path.Combine(
                AppContext.BaseDirectory,
                "Games",
                "UplayGames.txt");

        if (!File.Exists(
                DatabasePath))
        {
            return;
        }

        foreach (string line in File.ReadLines(
                     DatabasePath,
                     Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(
                    line))
            {
                continue;
            }

            string[] parts =
                line.Split(
                    '|',
                    2);

            if (parts.Length != 2)
            {
                continue;
            }

            string gameId =
                parts[0].Trim();

            string gameName =
                parts[1].Trim();

            if (!uint.TryParse(
                    gameId,
                    out _))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(
                    gameName))
            {
                continue;
            }

            _games[gameId] =
                gameName;
        }
    }
}