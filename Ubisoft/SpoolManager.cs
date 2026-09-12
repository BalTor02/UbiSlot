using System.IO;
using UbiSlot.Core;

namespace UbiSlot.Ubisoft;

public class SpoolManager
{
    private const string BackupFolderName = "UbiSlot_Backup";
    private const string PermanentBackupFolderName = "DO_NOT_DELETE_UBISLOT";

    public string GetSpoolRoot()
    {
        string localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(
            localAppData,
            "Ubisoft Game Launcher",
            "spool");
    }

    public List<string> FindSpoolFiles()
    {
        string spoolRoot =
            GetSpoolRoot();

        if (!Directory.Exists(spoolRoot))
        {
            return [];
        }

        string backupRoot =
            Path.Combine(
                Directory.GetParent(spoolRoot)!.FullName,
                "UbiSlot_Backup");

        return Directory
            .GetFiles(
                spoolRoot,
                "*.spool",
                SearchOption.AllDirectories)
            .Where(
                file =>
                    !file.StartsWith(
                        backupRoot,
                        StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                file =>
                    Path.GetFileNameWithoutExtension(file),
                StringComparer.OrdinalIgnoreCase)
            .Select(
                group =>
                    group.First())
            .ToList();
    }

    public string GetGameId(
        string spoolFile)
    {
        return Path.GetFileNameWithoutExtension(
            spoolFile);
    }

    public List<SpoolRecord> Parse(
        byte[] data)
    {
        var records =
            new List<SpoolRecord>();

        int offset = 0;

        while (offset < data.Length)
        {
            int recordStart =
                offset;

            if (data[offset++] != 0x0A)
            {
                break;
            }

            ulong recordLength =
                ReadVarint(
                    data,
                    ref offset);

            int recordEnd =
                offset + (int)recordLength;

            if (recordEnd > data.Length)
            {
                break;
            }

            uint? achievementId = null;
            long? timestamp = null;

            while (offset < recordEnd)
            {
                byte field =
                    data[offset++];

                if (field == 0x0A)
                {
                    ulong fieldLength =
                        ReadVarint(
                            data,
                            ref offset);

                    int fieldEnd =
                        offset + (int)fieldLength;

                    if (fieldEnd > recordEnd)
                    {
                        break;
                    }

                    if (offset < fieldEnd &&
                        data[offset] == 0x08)
                    {
                        offset++;

                        ulong id =
                            ReadVarint(
                                data,
                                ref offset);

                        achievementId =
                            (uint)id;
                    }

                    offset =
                        fieldEnd;
                }
                else if (field == 0x10)
                {
                    ulong value =
                        ReadVarint(
                            data,
                            ref offset);

                    timestamp =
                        (long)value;
                }
                else
                {
                    ReadVarint(
                        data,
                        ref offset);
                }
            }

            byte[] rawBytes =
                data[
                    recordStart..recordEnd];

            if (achievementId.HasValue &&
                timestamp.HasValue)
            {
                records.Add(
                    new SpoolRecord
                    {
                        AchievementId =
                            achievementId.Value,

                        UnlockTimestamp =
                            timestamp.Value,

                        RawBytes =
                            rawBytes
                    });
            }

            offset =
                recordEnd;
        }

        return records;
    }

    public List<SpoolRecord> GetRecords(
        string spoolFile)
    {
        byte[] data =
            File.ReadAllBytes(
                spoolFile);

        return Parse(data);
    }

    public SpoolRecord CreateAchievementRecord(
        uint achievementId)
    {
        long unlockTimestamp =
            DateTimeOffset.UtcNow
                .ToUnixTimeSeconds();

        byte[] achievementIdVarint =
            WriteVarint(
                achievementId);

        byte[] timestampVarint =
            WriteVarint(
                (ulong)unlockTimestamp);

        using var inner =
            new MemoryStream();

        inner.WriteByte(0x08);

        inner.Write(
            achievementIdVarint,
            0,
            achievementIdVarint.Length);

        byte[] innerBytes =
            inner.ToArray();

        using var achievementField =
            new MemoryStream();

        achievementField.WriteByte(0x0A);

        byte[] innerLength =
            WriteVarint(
                (ulong)innerBytes.Length);

        achievementField.Write(
            innerLength,
            0,
            innerLength.Length);

        achievementField.Write(
            innerBytes,
            0,
            innerBytes.Length);

        achievementField.WriteByte(0x10);

        achievementField.Write(
            timestampVarint,
            0,
            timestampVarint.Length);

        byte[] recordBody =
            achievementField.ToArray();

        using var record =
            new MemoryStream();

        record.WriteByte(0x0A);

        byte[] recordLength =
            WriteVarint(
                (ulong)recordBody.Length);

        record.Write(
            recordLength,
            0,
            recordLength.Length);

        record.Write(
            recordBody,
            0,
            recordBody.Length);

        return new SpoolRecord
        {
            AchievementId =
                achievementId,

            UnlockTimestamp =
                unlockTimestamp,

            RawBytes =
                record.ToArray()
        };
    }

    public byte[] SerializeRecords(
        List<SpoolRecord> records)
    {
        using var stream =
            new MemoryStream();

        foreach (SpoolRecord record in records)
        {
            if (record.RawBytes.Length == 0)
            {
                throw new InvalidDataException(
                    $"Achievement {record.AchievementId} has no raw record data.");
            }

            stream.Write(
                record.RawBytes,
                0,
                record.RawBytes.Length);
        }

        return stream.ToArray();
    }

    public void ValidateSerializedRecords(
        byte[] data)
    {
        List<SpoolRecord> records =
            Parse(data);

        if (records.Count == 0 &&
            data.Length > 0)
        {
            throw new InvalidDataException(
                "Serialized spool data could not be parsed.");
        }

        if (records.Any(
                record =>
                    record.RawBytes.Length == 0))
        {
            throw new InvalidDataException(
                "Serialized spool data contains an invalid record.");
        }
    }

    public List<SpoolRecord> SortRecordsByAchievementId(
        List<SpoolRecord> records)
    {
        return records
            .OrderBy(
                record =>
                    record.AchievementId)
            .ToList();
    }

    public List<SpoolRecord> RemoveDuplicateRecords(
        List<SpoolRecord> records)
    {
        return records
            .GroupBy(
                record =>
                    record.AchievementId)
            .Select(
                group =>
                    group.First())
            .ToList();
    }

    public bool IsSortedByAchievementId(
        List<SpoolRecord> records)
    {
        for (int i = 1;
             i < records.Count;
             i++)
        {
            if (records[i].AchievementId <
                records[i - 1].AchievementId)
            {
                return false;
            }
        }

        return true;
    }

    public List<SpoolRecord> PrepareRecordsForWrite(
        List<SpoolRecord> existingRecords,
        List<SpoolRecord> newRecords)
    {
        List<SpoolRecord> combinedRecords =
            new List<SpoolRecord>(
                existingRecords);

        combinedRecords.AddRange(
            newRecords);

        combinedRecords =
            RemoveDuplicateRecords(
                combinedRecords);

        if (IsSortedByAchievementId(
                existingRecords))
        {
            return SortRecordsByAchievementId(
                combinedRecords);
        }

        return combinedRecords;
    }

    public void WriteSpoolFile(
        string spoolFile,
        byte[] data)
    {
        string spoolRoot =
            GetSpoolRoot();

        CreatePermanentBackups(
            [spoolFile],
            spoolRoot);

        string temporaryFile =
            spoolFile +
            ".ubislot.tmp";

        try
        {
            File.WriteAllBytes(
                temporaryFile,
                data);

            byte[] writtenData =
                File.ReadAllBytes(
                    temporaryFile);

            ValidateSerializedRecords(
                writtenData);

            File.Move(
                temporaryFile,
                spoolFile,
                true);
        }
        finally
        {
            if (File.Exists(
                    temporaryFile))
            {
                File.Delete(
                    temporaryFile);
            }
        }
    }

    public List<uint> GetUnlockedAchievementIds(
        string spoolFile)
    {
        List<SpoolRecord> records =
            GetRecords(
                spoolFile);

        return records
            .Select(
                record =>
                    record.AchievementId)
            .Distinct()
            .OrderBy(
                id =>
                    id)
            .ToList();
    }

    public void CreatePermanentBackups(
        List<string> spoolFiles,
        string spoolRoot)
    {
        string ubisoftLauncherDirectory =
            Directory
                .GetParent(
                    spoolRoot)!
                .FullName;

        string spoolBackupDirectory =
            Path.Combine(
                ubisoftLauncherDirectory,
                BackupFolderName,
                PermanentBackupFolderName);

        string ubislotBackupDirectory =
            Path.Combine(
                AppContext.BaseDirectory,
                BackupFolderName,
                PermanentBackupFolderName);

        Directory.CreateDirectory(
            spoolBackupDirectory);

        Directory.CreateDirectory(
            ubislotBackupDirectory);

        foreach (string spoolFile in spoolFiles)
        {
            string fileName =
                Path.GetFileName(
                    spoolFile);

            string spoolBackupPath =
                Path.Combine(
                    spoolBackupDirectory,
                    fileName);

            string ubislotBackupPath =
                Path.Combine(
                    ubislotBackupDirectory,
                    fileName);

            CreateBackupFile(
                spoolFile,
                spoolBackupPath);

            CreateBackupFile(
                spoolFile,
                ubislotBackupPath);
        }

        CreateReadme(
            spoolBackupDirectory);

        CreateReadme(
            ubislotBackupDirectory);
    }

    public void RestoreOriginal(
        string spoolFile)
    {
        string spoolRoot =
            GetSpoolRoot();

        string ubisoftLauncherDirectory =
            Directory
                .GetParent(
                    spoolRoot)!
                .FullName;

        string backupDirectory =
            Path.Combine(
                ubisoftLauncherDirectory,
                BackupFolderName,
                PermanentBackupFolderName);

        string backupFile =
            Path.Combine(
                backupDirectory,
                Path.GetFileName(
                    spoolFile));

        if (!File.Exists(
                backupFile))
        {
            throw new FileNotFoundException(
                "The original backup file could not be found.",
                backupFile);
        }

        byte[] backupData =
            File.ReadAllBytes(
                backupFile);

        ValidateSerializedRecords(
            backupData);

        string temporaryFile =
            spoolFile +
            ".ubislot.restore.tmp";

        try
        {
            File.WriteAllBytes(
                temporaryFile,
                backupData);

            byte[] writtenData =
                File.ReadAllBytes(
                    temporaryFile);

            ValidateSerializedRecords(
                writtenData);

            File.Move(
                temporaryFile,
                spoolFile,
                true);
        }
        finally
        {
            if (File.Exists(
                    temporaryFile))
            {
                File.Delete(
                    temporaryFile);
            }
        }
    }

    private void CreateBackupFile(
        string source,
        string destination)
    {
        if (File.Exists(
                destination))
        {
            return;
        }

        File.Copy(
            source,
            destination);
    }

    private void CreateReadme(
        string directory)
    {
        string readmePath =
            Path.Combine(
                directory,
                "README.txt");

        if (File.Exists(
                readmePath))
        {
            return;
        }

        File.WriteAllText(
            readmePath,
            """
            This is your original spool files, keep them saved.

            If the achievements data from UbiSlot gets corrupted,
            you can use these files to recover your original file.

            DO NOT DELETE THIS FOLDER UNLESS YOU ARE CERTAIN
            YOU NO LONGER NEED YOUR ORIGINAL SPOOL FILES.
            """);
    }

    private byte[] WriteVarint(
        ulong value)
    {
        using var stream =
            new MemoryStream();

        while (value >= 0x80)
        {
            stream.WriteByte(
                (byte)(
                    (value & 0x7F) |
                    0x80));

            value >>= 7;
        }

        stream.WriteByte(
            (byte)value);

        return stream.ToArray();
    }

    private ulong ReadVarint(
        byte[] data,
        ref int offset)
    {
        ulong result = 0;
        int shift = 0;

        while (offset < data.Length)
        {
            byte current =
                data[offset++];

            result |=
                (ulong)(
                    current & 0x7F)
                << shift;

            if ((current & 0x80) == 0)
            {
                return result;
            }

            shift += 7;

            if (shift >= 64)
            {
                throw new InvalidDataException(
                    "Invalid varint encountered in spool file.");
            }
        }

        throw new EndOfStreamException(
            "Unexpected end of spool file.");
    }
}