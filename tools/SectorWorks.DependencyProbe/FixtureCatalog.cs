using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DiscUtils;
using DiscUtils.Fat;
using DiscUtils.Iso9660;
using DiscUtils.Ntfs;
using DiscUtils.Partitions;
using DiscUtils.Streams;

// Deliberately exercise the candidate's Windows-annotated NTFS API on Linux.
// This is a feasibility experiment, not a cross-platform support declaration.
#pragma warning disable CA1416

namespace SectorWorks.DependencyProbe;

public sealed record FixtureEntry(string Name, long Length, string Sha256, long CompressedLength, string CompressedSha256);

/// <summary>Foundation-only direct-library experiments; no application storage API.</summary>
public static class FixtureCatalog
{
    public const string Sentinel = "SectorWorks foundation sentinel v1\n";
    private const long MiB = 1024 * 1024;
    private static readonly string[] Names =
    [
        "fat12.img", "fat16.img", "fat32.img", "mbr.img", "gpt.img",
        "ntfs.img", "joliet.iso", "dynamic.vhd", "dynamic.vhdx",
        "truncated-fat.img", "corrupt-fat.img", "truncated.iso", "truncated.vhd", "truncated.vhdx",
    ];

    public static IReadOnlyList<FixtureEntry> ReadManifest(string fixtureRoot) =>
        JsonSerializer.Deserialize<FixtureEntry[]>(File.ReadAllText(Path.Combine(fixtureRoot, "manifest.json")))
        ?? throw new InvalidDataException("Missing fixture manifest.");

    public static void Seed(string fixtureRoot, string output)
    {
        Directory.CreateDirectory(fixtureRoot);
        Directory.CreateDirectory(output);
        if (File.Exists(Path.Combine(fixtureRoot, "manifest.json")))
        {
            throw new IOException("Immutable catalog already exists. Seeding must target a new fixture directory.");
        }

        foreach (string name in Names)
        {
            if (name == "ntfs.img")
            {
                Require(File.Exists(Path.Combine(output, name)), "Seed requires independently authored ntfs.img; see docs/fixtures.md.");
                continue;
            }
            using FileStream stream = NewFile(output, name);
            switch (name)
            {
                case "fat12.img":
                    using (FatFileSystem fs = FatFileSystem.FormatFloppy(stream, FloppyDiskType.HighDensity, "SECTORWORKS"))
                    {
                        Populate(fs);
                    }
                    break;
                case "fat16.img":
                case "fat32.img":
                    long capacity = name == "fat16.img" ? 16 * MiB : 512 * MiB;
                    stream.SetLength(capacity);
                    using (FatFileSystem fs = FatFileSystem.FormatPartition(stream, "SECTORWORKS",
                        Geometry.FromCapacity(capacity), 0, checked((int)(capacity / 512)), 0))
                    {
                        Populate(fs);
                        Console.WriteLine($"{name}: {fs.FatVariant}");
                    }
                    break;
                case "mbr.img":
                case "gpt.img":
                    stream.SetLength(32 * MiB);
                    using (var disk = new DiscUtils.Raw.Disk(stream, Ownership.None))
                    {
                        PartitionTable table = name == "mbr.img"
                            ? BiosPartitionTable.Initialize(disk, WellKnownPartitionType.WindowsFat)
                            : GuidPartitionTable.Initialize(disk, WellKnownPartitionType.WindowsFat);
                        using FatFileSystem fs = FatFileSystem.FormatPartition(disk, 0, "SECTORWORKS");
                        Populate(fs);
                    }
                    break;
                case "joliet.iso":
                    var builder = new CDBuilder { UseJoliet = true, VolumeIdentifier = "SECTORWORKS" };
                    builder.AddFile("SENTINEL.TXT", Encoding.UTF8.GetBytes(Sentinel));
                    builder.AddFile(@"DOCS\Unicode-λ.txt", Encoding.UTF8.GetBytes(Sentinel));
                    using (SparseStream iso = builder.Build())
                    {
                        iso.CopyTo(stream);
                    }
                    break;
                case "dynamic.vhd":
                    using (var disk = DiscUtils.Vhd.Disk.InitializeDynamic(stream, Ownership.None, 32 * MiB))
                    {
                        using FatFileSystem fs = FatFileSystem.FormatFloppy(disk.Content, FloppyDiskType.HighDensity, "SECTORWORKS");
                        Populate(fs);
                    }
                    break;
                case "dynamic.vhdx":
                    using (var disk = DiscUtils.Vhdx.Disk.InitializeDynamic(stream, Ownership.None, 32 * MiB))
                    {
                        using FatFileSystem fs = FatFileSystem.FormatFloppy(disk.Content, FloppyDiskType.HighDensity, "SECTORWORKS");
                        Populate(fs);
                    }
                    break;
                default:
                    string sourceName = name switch
                    {
                        "truncated.iso" => "joliet.iso",
                        "truncated.vhd" => "dynamic.vhd",
                        "truncated.vhdx" => "dynamic.vhdx",
                        _ => "fat12.img",
                    };
                    using (FileStream original = File.OpenRead(Path.Combine(output, sourceName)))
                    {
                        byte[] damaged = new byte[name == "corrupt-fat.img" ? 512 : 31];
                        original.ReadExactly(damaged);
                        if (name == "corrupt-fat.img")
                        {
                            damaged[11] = damaged[12] = 0;
                        }
                        stream.Write(damaged);
                    }
                    break;
            }
        }

        var entries = new List<FixtureEntry>();
        foreach (string name in Names)
        {
            string rawPath = Path.Combine(output, name);
            string compressedPath = Path.Combine(fixtureRoot, name + ".gz");
            using (FileStream source = File.OpenRead(rawPath))
            using (FileStream target = new(compressedPath, FileMode.CreateNew))
            using (var gzip = new GZipStream(target, CompressionLevel.SmallestSize))
            {
                source.CopyTo(gzip);
            }
            entries.Add(new(name, new FileInfo(rawPath).Length, Hash(rawPath),
                new FileInfo(compressedPath).Length, Hash(compressedPath)));
        }
        File.WriteAllText(Path.Combine(fixtureRoot, "manifest.json"),
            JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    public static string Materialize(string fixtureRoot, string output, FixtureEntry entry)
    {
        Directory.CreateDirectory(output);
        string compressed = Path.Combine(fixtureRoot, entry.Name + ".gz");
        Require(new FileInfo(compressed).Length == entry.CompressedLength, "Compressed length mismatch: " + entry.Name);
        Require(Hash(compressed) == entry.CompressedSha256, "Compressed hash mismatch: " + entry.Name);
        string destination = Path.Combine(output, entry.Name);
        using (FileStream source = File.OpenRead(compressed))
        using (var gzip = new GZipStream(source, CompressionMode.Decompress))
        using (FileStream target = new(destination, FileMode.CreateNew))
        {
            byte[] buffer = new byte[64 * 1024];
            long written = 0;
            int count;
            while ((count = gzip.Read(buffer)) != 0)
            {
                written = checked(written + count);
                Require(written <= entry.Length, "Fixture expands beyond manifest.");
                target.Write(buffer, 0, count);
            }
            Require(written == entry.Length, "Fixture length mismatch.");
        }
        Require(Hash(destination) == entry.Sha256, "Expanded hash mismatch: " + entry.Name);
        return destination;
    }

    public static void Verify(string fixtureRoot, string output)
    {
        foreach (FixtureEntry entry in ReadManifest(fixtureRoot))
        {
            string path = Materialize(fixtureRoot, output, entry);
            if (!entry.Name.StartsWith("truncated", StringComparison.Ordinal) && entry.Name != "corrupt-fat.img")
            {
                VerifyImage(path);
            }
            Console.WriteLine($"VERIFIED {entry.Name}: {entry.Length} bytes sha256={entry.Sha256} (malformed entries: hash only; parser rejection tested separately)");
        }
    }

    public static void VerifyImage(string path)
    {
        using FileStream stream = File.OpenRead(path);
        switch (Path.GetFileName(path))
        {
            case "joliet.iso":
                using (var fs = new CDReader(stream, true))
                {
                    CheckSentinel(fs);
                    using Stream unicode = fs.OpenFile(@"DOCS\Unicode-λ.txt", FileMode.Open, FileAccess.Read);
                    Require(new StreamReader(unicode).ReadToEnd() == Sentinel, "Joliet name/content mismatch.");
                }
                using (var fs = new CDReader(stream, false))
                {
                    CheckSentinel(fs);
                }
                break;
            case "ntfs.img":
                using (var fs = new NtfsFileSystem(stream))
                {
                    using (Stream file = fs.OpenFile("SENTINEL.TXT", FileMode.Open, FileAccess.Read))
                    {
                        Require(new StreamReader(file).ReadToEnd() == Sentinel, "NTFS sentinel mismatch.");
                    }
                    using Stream large = fs.OpenFile("LARGE.BIN", FileMode.Open, FileAccess.Read);
                    Require(large.Length == 8192, "Nonresident NTFS file length mismatch.");
                    for (int i = 0; i < 8192; i++)
                    {
                        Require(large.ReadByte() == i % 251, "Nonresident NTFS file content mismatch.");
                    }
                }
                break;
            case "mbr.img":
            case "gpt.img":
                using (var disk = new DiscUtils.Raw.Disk(stream, Ownership.None))
                {
                    PartitionTable table = Path.GetFileName(path) == "mbr.img"
                        ? new BiosPartitionTable(stream, disk.Geometry)
                        : new GuidPartitionTable(stream, disk.Geometry);
                    Require(table.Count == 1, "Partition count mismatch.");
                    Require(table[0].FirstSector > 0 &&
                        checked((table[0].LastSector + 1) * 512) <= stream.Length, "Partition bounds mismatch.");
                    using SparseStream partition = table[0].Open();
                    using var fs = new FatFileSystem(partition);
                    CheckSentinel(fs);
                }
                break;
            case "dynamic.vhd":
                using (var disk = new DiscUtils.Vhd.Disk(stream, Ownership.None))
                {
                    Require(disk.Capacity == 32 * MiB, "VHD virtual capacity mismatch.");
                    using var fs = new FatFileSystem(disk.Content);
                    CheckSentinel(fs);
                }
                break;
            case "dynamic.vhdx":
                using (var disk = new DiscUtils.Vhdx.Disk(stream, Ownership.None))
                {
                    Require(disk.Capacity == 32 * MiB, "VHDX virtual capacity mismatch.");
                    using var fs = new FatFileSystem(disk.Content);
                    CheckSentinel(fs);
                }
                break;
            default:
                using (var fs = new FatFileSystem(stream))
                {
                    string expected = Path.GetFileName(path) switch
                    {
                        "fat12.img" => "Fat12",
                        "fat16.img" => "Fat16",
                        "fat32.img" => "Fat32",
                        _ => throw new ArgumentException("Unknown fixture: " + path),
                    };
                    Require(fs.FatVariant.ToString() == expected, "FAT variant mismatch.");
                    CheckSentinel(fs);
                }
                break;
        }
    }

    public static void RoundTripFat(string path)
    {
        using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var fs = new FatFileSystem(stream))
        {
            fs.CreateDirectory("ROUND");
            using (Stream file = fs.OpenFile(@"ROUND\NEW.TXT", FileMode.CreateNew, FileAccess.Write))
            {
                file.Write(Encoding.UTF8.GetBytes("round trip\n"));
            }
            fs.MoveFile(@"ROUND\NEW.TXT", @"ROUND\MOVED.TXT");
        }
        using FileStream reopened = File.OpenRead(path);
        using var read = new FatFileSystem(reopened);
        using Stream result = read.OpenFile(@"ROUND\MOVED.TXT", FileMode.Open, FileAccess.Read);
        Require(new StreamReader(result).ReadToEnd() == "round trip\n", "Round-trip content mismatch.");
        CheckSentinel(read);
    }

    public static void OpenMalformed(string path)
    {
        using FileStream stream = File.OpenRead(path);
        switch (Path.GetExtension(path))
        {
            case ".iso":
                using (var fs = new CDReader(stream, true))
                {
                    _ = fs.GetFiles("");
                }
                break;
            case ".vhd":
                using (var disk = new DiscUtils.Vhd.Disk(stream, Ownership.None))
                {
                    _ = disk.Content.ReadByte();
                }
                break;
            case ".vhdx":
                using (var disk = new DiscUtils.Vhdx.Disk(stream, Ownership.None))
                {
                    _ = disk.Content.ReadByte();
                }
                break;
            default:
                using (var fs = new FatFileSystem(stream))
                {
                    _ = fs.GetFiles("");
                }
                break;
        }
    }

    private static FileStream NewFile(string output, string name) =>
        new(Path.Combine(output, name), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

    private static void Populate(DiscFileSystem fs)
    {
        fs.CreateDirectory("DOCS");
        using Stream file = fs.OpenFile("SENTINEL.TXT", FileMode.CreateNew, FileAccess.Write);
        file.Write(Encoding.UTF8.GetBytes(Sentinel));
    }

    private static void CheckSentinel(DiscFileSystem fs)
    {
        Require(fs.DirectoryExists("DOCS"), "Missing DOCS directory.");
        using Stream file = fs.OpenFile("SENTINEL.TXT", FileMode.Open, FileAccess.Read);
        Require(new StreamReader(file).ReadToEnd() == Sentinel, "Sentinel mismatch.");
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
