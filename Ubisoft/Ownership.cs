using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UbiSlot.Games;

namespace UbiSlot.Ubisoft;

public sealed class OwnershipManager
{
    private const int ProtobufOffset = 264;

    private readonly GameDatabase _gameDatabase;

    public OwnershipManager(GameDatabase gameDatabase)
    {
        _gameDatabase = gameDatabase;
    }

    public List<uint> GetOwnedGameIds()
    {
        try
        {
            string? ownershipFile =
                LocateOwnershipFile();

            if (ownershipFile == null)
            {
                Console.WriteLine(
                    "[Ownership] Ownership cache not found.");

                return [];
            }

            Console.WriteLine(
                $"[Ownership] Cache: {ownershipFile}");

            byte[] data =
                File.ReadAllBytes(ownershipFile);

            List<OwnershipRecord> records =
                ParseOwnershipFile(data);

            Console.WriteLine(
                $"[Ownership] Records parsed: {records.Count}");

            var candidateIds =
                new SortedSet<uint>();

            foreach (OwnershipRecord record in records)
            {
                if (record.Field1.HasValue)
                {
                    candidateIds.Add(
                        record.Field1.Value);
                }

                if (record.Field2.HasValue)
                {
                    candidateIds.Add(
                        record.Field2.Value);
                }
            }

            var ownedGameIds =
                new SortedSet<uint>();

            foreach (uint id in candidateIds)
            {
                if (_gameDatabase.GetGameName(
                        id.ToString()) != null)
                {
                    ownedGameIds.Add(id);
                }
            }

            Console.WriteLine(
                $"[Ownership] Candidate IDs: {candidateIds.Count}");

            Console.WriteLine(
                $"[Ownership] Matched game IDs: {ownedGameIds.Count}");

            foreach (uint id in ownedGameIds)
            {
                Console.WriteLine(
                    $"[Ownership] Owned Game ID: {id}");
            }

            return ownedGameIds.ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Ownership] ERROR: {ex}");

            return [];
        }
    }

    public void PrintOwnedGames()
    {
        Console.WriteLine();
        Console.WriteLine(
            "======================================");
        Console.WriteLine(
            "       UBISOFT OWNED GAMES");
        Console.WriteLine(
            "======================================");
        Console.WriteLine();

        List<uint> gameIds =
            GetOwnedGameIds();

        Console.WriteLine(
            $"Owned IDs found: {gameIds.Count}");

        Console.WriteLine();

        foreach (uint gameId in gameIds)
        {
            string id =
                gameId.ToString();

            string name =
                _gameDatabase.GetGameName(id)
                ?? "[UNKNOWN GAME]";

            Console.WriteLine(
                $"{gameId,-6} -> {name}");
        }

        Console.WriteLine();
    }

    private string? LocateOwnershipFile()
    {
        string localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        string ownershipDirectory =
            Path.Combine(
                localAppData,
                "Ubisoft Game Launcher",
                "cache",
                "ownership");

        if (!Directory.Exists(
                ownershipDirectory))
        {
            return null;
        }

        var candidates =
            new List<(string Path, int Score, long Size)>();

        IEnumerable<string> files;

        try
        {
            files =
                Directory.EnumerateFiles(
                    ownershipDirectory,
                    "*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        ReturnSpecialDirectories = false
                    });
        }
        catch
        {
            return null;
        }

        foreach (string file in files)
        {
            long size =
                GetFileSize(file);

            if (size <= ProtobufOffset)
                continue;

            try
            {
                byte[] data =
                    File.ReadAllBytes(file);

                List<OwnershipRecord> records =
                    ParseOwnershipFile(data);

                if (records.Count == 0)
                    continue;

                int score = 0;

                foreach (OwnershipRecord record in records)
                {
                    if (record.Field1.HasValue &&
                        _gameDatabase.GetGameName(
                            record.Field1.Value.ToString()) != null)
                    {
                        score++;
                    }

                    if (record.Field2.HasValue &&
                        _gameDatabase.GetGameName(
                            record.Field2.Value.ToString()) != null)
                    {
                        score++;
                    }
                }

                candidates.Add(
                    (file, score, size));
            }
            catch
            {
                // Ignore unrelated files.
            }
        }

        return candidates
            .OrderByDescending(
                candidate => candidate.Score)
            .ThenByDescending(
                candidate => candidate.Size)
            .Select(
                candidate => candidate.Path)
            .FirstOrDefault();
    }

    private static long GetFileSize(
        string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static List<OwnershipRecord>
        ParseOwnershipFile(byte[] data)
    {
        if (data.Length <= ProtobufOffset)
            return [];

        return ParseTopLevelRecords(
            data[ProtobufOffset..]);
    }

    private static List<OwnershipRecord>
        ParseTopLevelRecords(byte[] data)
    {
        var records =
            new List<OwnershipRecord>();

        int position = 0;

        while (position < data.Length)
        {
            int fieldStart =
                position;

            try
            {
                ulong tag =
                    ReadVarint(
                        data,
                        ref position);

                int fieldNumber =
                    checked(
                        (int)(tag >> 3));

                int wireType =
                    (int)(tag & 7);

                if (fieldNumber == 1 &&
                    wireType == 2)
                {
                    ulong length =
                        ReadVarint(
                            data,
                            ref position);

                    if (length > int.MaxValue ||
                        position + (long)length >
                        data.Length)
                    {
                        break;
                    }

                    int recordLength =
                        (int)length;

                    byte[] recordData =
                        new byte[recordLength];

                    Buffer.BlockCopy(
                        data,
                        position,
                        recordData,
                        0,
                        recordLength);

                    position +=
                        recordLength;

                    OwnershipRecord? record =
                        ParseRecord(recordData);

                    if (record != null &&
                        (record.Field1.HasValue ||
                         record.Field2.HasValue))
                    {
                        records.Add(record);
                    }
                }
                else
                {
                    SkipField(
                        data,
                        ref position,
                        wireType);
                }
            }
            catch
            {
                position =
                    fieldStart + 1;
            }
        }

        return records;
    }

    private static OwnershipRecord?
        ParseRecord(byte[] data)
    {
        var record =
            new OwnershipRecord();

        int position = 0;

        while (position < data.Length)
        {
            ulong tag =
                ReadVarint(
                    data,
                    ref position);

            int fieldNumber =
                checked(
                    (int)(tag >> 3));

            int wireType =
                (int)(tag & 7);

            switch (fieldNumber)
            {
                case 1 when wireType == 0:

                    record.Field1 =
                        checked(
                            (uint)ReadVarint(
                                data,
                                ref position));

                    break;

                case 2 when wireType == 0:

                    record.Field2 =
                        checked(
                            (uint)ReadVarint(
                                data,
                                ref position));

                    break;

                default:

                    SkipField(
                        data,
                        ref position,
                        wireType);

                    break;
            }
        }

        return record.Field1.HasValue ||
               record.Field2.HasValue
            ? record
            : null;
    }

    private static ulong ReadVarint(
        byte[] data,
        ref int position)
    {
        ulong result = 0;
        int shift = 0;

        while (position < data.Length)
        {
            byte b =
                data[position++];

            result |=
                (ulong)(b & 0x7F) <<
                shift;

            if ((b & 0x80) == 0)
                return result;

            shift += 7;

            if (shift >= 64)
            {
                throw new InvalidDataException(
                    "Invalid protobuf varint.");
            }
        }

        throw new EndOfStreamException(
            "Unexpected end of protobuf data.");
    }

    private static void SkipField(
        byte[] data,
        ref int position,
        int wireType)
    {
        switch (wireType)
        {
            case 0:

                ReadVarint(
                    data,
                    ref position);

                break;

            case 1:

                EnsureAvailable(
                    data,
                    position,
                    8);

                position += 8;

                break;

            case 2:

                ulong length =
                    ReadVarint(
                        data,
                        ref position);

                if (length > int.MaxValue)
                {
                    throw new InvalidDataException(
                        "Invalid protobuf length.");
                }

                EnsureAvailable(
                    data,
                    position,
                    (int)length);

                position +=
                    (int)length;

                break;

            case 5:

                EnsureAvailable(
                    data,
                    position,
                    4);

                position += 4;

                break;

            default:

                throw new InvalidDataException(
                    $"Unsupported protobuf wire type: {wireType}");
        }
    }

    private static void EnsureAvailable(
        byte[] data,
        int position,
        int count)
    {
        if (count < 0 ||
            position > data.Length - count)
        {
            throw new EndOfStreamException(
                "Protobuf field extends beyond file.");
        }
    }

    private sealed class OwnershipRecord
    {
        public uint? Field1 { get; set; }

        public uint? Field2 { get; set; }
    }
}