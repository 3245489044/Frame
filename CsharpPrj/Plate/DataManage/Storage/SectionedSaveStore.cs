using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace UniversalWorkflowFramework.DataManage;

/// <summary>一个存档段：键不透明，载荷由上层编解码。</summary>
public readonly record struct SectionRecord(long Key, byte[] Payload);

/// <summary>存档头：格式版本与结构版本分离——格式管二进制布局，结构版本管内容 schema（如方块的 catalogVersion）。</summary>
public readonly record struct SectionSaveHeader(
    int FormatVersion,
    int SchemaVersion,
    string Tag,
    long CreatedUtcTicks,
    int SectionCount)
{
    public static SectionSaveHeader Create(int formatVersion, int schemaVersion, string tag, int sectionCount)
        => new(formatVersion, schemaVersion, tag, DateTime.UtcNow.Ticks, sectionCount);
}

/// <summary>段级迁移：只在格式版本落后时按 FromVersion→ToVersion 顺序应用。</summary>
public interface ISectionMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    byte[] Migrate(long key, byte[] payload);
}

/// <summary>存档读取结果。</summary>
public sealed class SectionSaveFile
{
    public SectionSaveHeader Header { get; }
    public IReadOnlyDictionary<long, byte[]> Sections { get; }
    public string SourcePath { get; }
    public bool UsedBackup { get; }

    internal SectionSaveFile(SectionSaveHeader header, IReadOnlyDictionary<long, byte[]> sections, string sourcePath, bool usedBackup)
    {
        Header = header;
        Sections = sections;
        SourcePath = sourcePath;
        UsedBackup = usedBackup;
    }

    public byte[]? GetSection(long key) => Sections.TryGetValue(key, out var payload) ? payload : null;
}

/// <summary>存档结构/校验失败：带路径与具体原因，避免把损坏存档静默当成空世界。</summary>
public sealed class SectionStoreException : Exception
{
    public string Path { get; }
    public string Reason { get; }

    public SectionStoreException(string path, string reason, Exception? inner = null)
        : base($"存档 {path} 无效：{reason}", inner)
    {
        Path = path;
        Reason = reason;
    }
}

/// <summary>
/// 版本化分区存档：头版本 + 段级迁移 + SHA-256 校验 + 原子写（temp → replace，保留 .bak）。
/// <para>与体素/游戏语义无关：键和段载荷完全由调用方解释。</para>
/// </summary>
public sealed class SectionedSaveStore
{
    private const int Magic = 0x53565331; // "SVS1"
    private const int HeaderBytes = 4 + 4 + 4 + 8 + 4 + 4 + 32;

    private readonly int _formatVersion;

    public SectionedSaveStore(int formatVersion = 1)
    {
        if (formatVersion <= 0) throw new ArgumentOutOfRangeException(nameof(formatVersion));
        _formatVersion = formatVersion;
    }

    public int FormatVersion => _formatVersion;

    public void Save(string path, SectionSaveHeader header, IEnumerable<SectionRecord> sections)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(sections);

        var records = new List<SectionRecord>();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var record in sections)
        {
            if (record.Payload is null)
                throw new SectionStoreException(path, $"段 {record.Key} 载荷为 null");

            records.Add(record);
            Span<byte> keyBytes = stackalloc byte[sizeof(long)];
            BitConverter.TryWriteBytes(keyBytes, record.Key);
            hash.AppendData(keyBytes);
            hash.AppendData(BitConverter.GetBytes(record.Payload.Length));
            hash.AppendData(record.Payload);
        }

        byte[] checksum = hash.GetHashAndReset();
        var finalHeader = header with
        {
            FormatVersion = _formatVersion,
            SectionCount = records.Count,
            CreatedUtcTicks = header.CreatedUtcTicks == 0 ? DateTime.UtcNow.Ticks : header.CreatedUtcTicks
        };

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
            {
                WriteHeader(writer, finalHeader, checksum);
                foreach (var record in records)
                {
                    writer.Write(record.Key);
                    writer.Write(record.Payload.Length);
                    writer.Write(record.Payload);
                }
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            ReplaceAtomically(temp, path);
        }
        catch (Exception ex) when (ex is not SectionStoreException)
        {
            TryDelete(temp);
            throw new SectionStoreException(path, $"写入失败：{ex.Message}", ex);
        }
    }

    public SectionSaveFile Load(string path, int? expectedFormatVersion = null, IEnumerable<ISectionMigration>? migrations = null)
    {
        var (file, _) = LoadCore(path, expectedFormatVersion ?? _formatVersion, migrations);
        return file;
    }

    /// <summary>主存档损坏时回退 .bak；两者都失败才抛出（带主/备两条原因）。</summary>
    public SectionSaveFile LoadWithRecovery(string path, int? expectedFormatVersion = null, IEnumerable<ISectionMigration>? migrations = null)
    {
        string backup = path + ".bak";
        SectionStoreException? primaryError = null;

        if (File.Exists(path))
        {
            try
            {
                var (file, _) = LoadCore(path, expectedFormatVersion ?? _formatVersion, migrations);
                return new SectionSaveFile(file.Header, file.Sections, path, usedBackup: false);
            }
            catch (SectionStoreException ex)
            {
                primaryError = ex;
            }
        }

        if (File.Exists(backup))
        {
            var (file, _) = LoadCore(backup, expectedFormatVersion ?? _formatVersion, migrations);
            return new SectionSaveFile(file.Header, file.Sections, backup, usedBackup: true);
        }

        throw primaryError ?? new SectionStoreException(path, "文件不存在且无可用备份");
    }

    private (SectionSaveFile File, int RawFormatVersion) LoadCore(
        string path, int expectedFormatVersion, IEnumerable<ISectionMigration>? migrations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new SectionStoreException(path, "文件不存在");

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            throw new SectionStoreException(path, $"读取失败：{ex.Message}", ex);
        }

        if (bytes.Length < HeaderBytes)
            throw new SectionStoreException(path, $"文件截断：仅 {bytes.Length} 字节，头部至少 {HeaderBytes} 字节");

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

            int magic = reader.ReadInt32();
            if (magic != Magic)
                throw new SectionStoreException(path, "魔数不匹配（不是本格式存档）");

            int formatVersion = reader.ReadInt32();
            int schemaVersion = reader.ReadInt32();
            long createdTicks = reader.ReadInt64();
            int sectionCount = reader.ReadInt32();
            int tagLength = reader.ReadInt32();
            if (sectionCount < 0 || sectionCount > 1_000_000)
                throw new SectionStoreException(path, $"段数非法：{sectionCount}");
            if (tagLength < 0 || tagLength > 4096)
                throw new SectionStoreException(path, $"标签长度非法：{tagLength}");

            byte[] expectedChecksum = reader.ReadBytes(32);
            if (expectedChecksum.Length != 32)
                throw new SectionStoreException(path, "校验和字段截断");

            string tag = tagLength == 0 ? "" : Encoding.UTF8.GetString(reader.ReadBytes(tagLength));
            if (stream.Position > bytes.Length)
                throw new SectionStoreException(path, "头部截断");

            var sections = new Dictionary<long, byte[]>(sectionCount);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (int i = 0; i < sectionCount; i++)
            {
                if (stream.Position + sizeof(long) + sizeof(int) > bytes.Length)
                    throw new SectionStoreException(path, $"第 {i} 段头部截断");

                long key = reader.ReadInt64();
                int length = reader.ReadInt32();
                if (length < 0 || length > bytes.Length)
                    throw new SectionStoreException(path, $"第 {i} 段长度非法：{length}");
                if (stream.Position + length > bytes.Length)
                    throw new SectionStoreException(path, $"第 {i} 段截断：需要 {length} 字节，剩余 {bytes.Length - stream.Position}");

                byte[] payload = reader.ReadBytes(length);
                sections[key] = payload;

                Span<byte> keyBytes = stackalloc byte[sizeof(long)];
                BitConverter.TryWriteBytes(keyBytes, key);
                hash.AppendData(keyBytes);
                hash.AppendData(BitConverter.GetBytes(length));
                hash.AppendData(payload);
            }

            byte[] actualChecksum = hash.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(expectedChecksum, actualChecksum))
                throw new SectionStoreException(path, "校验和不匹配（内容损坏或被截断）");

            if (formatVersion > expectedFormatVersion)
                throw new SectionStoreException(
                    path, $"存档格式版本 {formatVersion} 高于当前支持 {expectedFormatVersion}（拒绝静默降级）");

            if (formatVersion < expectedFormatVersion)
            {
                var ordered = new List<ISectionMigration>();
                if (migrations != null) ordered.AddRange(migrations);
                ordered.Sort((a, b) => a.FromVersion.CompareTo(b.FromVersion));

                int current = formatVersion;
                foreach (var migration in ordered)
                {
                    if (migration.FromVersion != current) continue;
                    foreach (var key in new List<long>(sections.Keys))
                        sections[key] = migration.Migrate(key, sections[key]);
                    current = migration.ToVersion;
                }

                if (current != expectedFormatVersion)
                    throw new SectionStoreException(
                        path, $"缺少 {current}→{expectedFormatVersion} 的段迁移");
            }

            var header = new SectionSaveHeader(formatVersion, schemaVersion, tag, createdTicks, sectionCount);
            return (new SectionSaveFile(header, sections, path, usedBackup: false), formatVersion);
        }
        catch (SectionStoreException)
        {
            throw;
        }
        catch (EndOfStreamException ex)
        {
            throw new SectionStoreException(path, "文件截断（读取越界）", ex);
        }
        catch (Exception ex)
        {
            throw new SectionStoreException(path, $"解析失败：{ex.Message}", ex);
        }
    }

    private static void WriteHeader(BinaryWriter writer, SectionSaveHeader header, byte[] checksum)
    {
        byte[] tagBytes = Encoding.UTF8.GetBytes(header.Tag ?? "");
        writer.Write(Magic);
        writer.Write(header.FormatVersion);
        writer.Write(header.SchemaVersion);
        writer.Write(header.CreatedUtcTicks);
        writer.Write(header.SectionCount);
        writer.Write(tagBytes.Length);
        writer.Write(checksum);
        writer.Write(tagBytes);
    }

    private static void ReplaceAtomically(string temp, string target)
    {
        string backup = target + ".bak";
        if (File.Exists(target))
        {
            File.Replace(temp, target, backup, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, target);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 清理失败不掩盖原始错误 */ }
    }
}
