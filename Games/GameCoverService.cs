using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace UbiSlot.Games;

public class GameCoverService
{
    private readonly HttpClient _httpClient;

    private readonly string _coverDirectory;

    public GameCoverService()
    {
        _httpClient =
            new HttpClient();

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "UbiSlot/0.1");

        _coverDirectory =
            Path.Combine(
                AppContext.BaseDirectory,
                "UbiSlot_Cache",
                "Covers");

        Directory.CreateDirectory(
            _coverDirectory);
    }

    public async Task<string?> GetCoverAsync(
        string gameName)
    {
        string actualName =
            CleanGameName(
                gameName);

        string safeName =
            MakeSafeFileName(
                actualName);

        string cachedPath =
            Path.Combine(
                _coverDirectory,
                $"{safeName}.jpg");

        if (File.Exists(
                cachedPath))
        {
            return cachedPath;
        }

        int? appId =
            await FindSteamAppIdAsync(
                actualName);

        if (!appId.HasValue)
        {
            return null;
        }

        return await DownloadCoverAsync(
            appId.Value,
            cachedPath);
    }

    private string CleanGameName(
        string gameName)
    {
        string actualName =
            gameName.Trim();

        int bracketIndex =
            actualName.IndexOf('(');

        if (bracketIndex >= 0)
        {
            actualName =
                actualName[
                    ..bracketIndex]
                .Trim();
        }

        actualName =
            actualName
                .Replace(
                    "®",
                    string.Empty)
                .Replace(
                    "™",
                    string.Empty)
                .Replace(
                    "©",
                    string.Empty);

        actualName =
            actualName.Replace(
                "-",
                " ");

        while (actualName.Contains("  "))
        {
            actualName =
                actualName.Replace(
                    "  ",
                    " ");
        }

        return actualName.Trim();
    }

    private async Task<string?> DownloadCoverAsync(
        int appId,
        string destinationPath)
    {
        string[] imageUrls =
        [
            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900_2x.jpg",

            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg",

            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_capsule.jpg",

            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg"
        ];

        foreach (string imageUrl in imageUrls)
        {
            try
            {
                using HttpResponseMessage response =
                    await _httpClient.GetAsync(
                        imageUrl);

                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                byte[] imageData =
                    await response.Content.ReadAsByteArrayAsync();

                if (imageData.Length == 0)
                {
                    continue;
                }

                await File.WriteAllBytesAsync(
                    destinationPath,
                    imageData);

                return destinationPath;
            }
            catch
            {
            }
        }

        return null;
    }

    private async Task<int?> FindSteamAppIdAsync(
        string gameName)
    {
        string encodedName =
            Uri.EscapeDataString(
                gameName);

        string url =
            $"https://store.steampowered.com/api/storesearch/" +
            $"?term={encodedName}" +
            $"&cc=us" +
            $"&l=english";

        try
        {
            string json =
                await _httpClient.GetStringAsync(
                    url);

            using JsonDocument document =
                JsonDocument.Parse(
                    json);

            if (!document.RootElement.TryGetProperty(
                    "items",
                    out JsonElement items))
            {
                return null;
            }

            string normalizedSearch =
                NormalizeForComparison(
                    gameName);

            int? bestAppId =
                null;

            int bestScore =
                0;

            foreach (JsonElement item in
                     items.EnumerateArray())
            {
                if (!item.TryGetProperty(
                        "id",
                        out JsonElement idElement))
                {
                    continue;
                }

                if (!idElement.TryGetInt32(
                        out int appId))
                {
                    continue;
                }

                if (!item.TryGetProperty(
                        "name",
                        out JsonElement nameElement))
                {
                    continue;
                }

                string? resultName =
                    nameElement.GetString();

                if (string.IsNullOrWhiteSpace(
                        resultName))
                {
                    continue;
                }

                int score =
                    GetMatchScore(
                        normalizedSearch,
                        NormalizeForComparison(
                            resultName));

                if (score > bestScore)
                {
                    bestScore =
                        score;

                    bestAppId =
                        appId;
                }
            }

            return bestAppId;
        }
        catch
        {
            return null;
        }
    }

    private int GetMatchScore(
        string searchName,
        string resultName)
    {
        if (string.Equals(
                searchName,
                resultName,
                StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (resultName.StartsWith(
                searchName,
                StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        if (resultName.Contains(
                searchName,
                StringComparison.OrdinalIgnoreCase))
        {
            return 60;
        }

        if (searchName.Contains(
                resultName,
                StringComparison.OrdinalIgnoreCase))
        {
            return 50;
        }

        return 0;
    }

    private string NormalizeForComparison(
        string value)
    {
        string normalized =
            CleanGameName(
                value);

        normalized =
            normalized
                .Replace(
                    ":",
                    " ")
                .Replace(
                    "'",
                    string.Empty)
                .Replace(
                    "’",
                    string.Empty);

        while (normalized.Contains("  "))
        {
            normalized =
                normalized.Replace(
                    "  ",
                    " ");
        }

        return normalized.Trim();
    }

    private string MakeSafeFileName(
        string name)
    {
        foreach (char invalidCharacter in
                 Path.GetInvalidFileNameChars())
        {
            name =
                name.Replace(
                    invalidCharacter,
                    '_');
        }

        return name.Trim();
    }
}