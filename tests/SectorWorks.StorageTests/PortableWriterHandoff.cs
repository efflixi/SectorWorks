using System.Security.Cryptography;
using System.Text.Json;
using SectorWorks.Core;
using SectorWorks.Storage;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

// Test passes in handoff mode cover managed assertions only. The retained queue
// stays pending until the Linux verifier checks every exact Windows output.
internal static class PortableWriterHandoff
{
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly object Gate = new();
    internal sealed record Input(string Row, string File, long Length, string Sha256);
    internal sealed record Catalog(int Schema, string Status, Input[] Inputs);
    internal sealed record Pending(int Schema, string Status, string Row, string Workflow,
        string CatalogSha256, string InputSha256, string File, long Length, string Sha256,
        long VolumeOffset, long VolumeLength, string Platform);

    internal static string Row(string container, string layout, string filesystem, int physical, bool longNames) =>
        $"{container}.{layout}.{filesystem}.p{physical}.{(longNames ? "lfn" : "short")}";

    internal static string? Import(WorkDirectory work, string row)
    {
        string? root = Environment.GetEnvironmentVariable("SECTORWORKS_WRITER_INPUTS");
        if (root is null) { return null; }
        if (Environment.GetEnvironmentVariable("SECTORWORKS_WRITER_EXPORT") is not null ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SECTORWORKS_WRITER_OUTPUTS")))
        {
            throw new InvalidDataException("Handoff requires separate input/output roots and cannot export inputs.");
        }
        string expected = Environment.GetEnvironmentVariable("SECTORWORKS_WRITER_CATALOG_SHA256")
            ?? throw new InvalidDataException("A separately supplied catalog SHA256 is required.");
        string destination = work.PathFor("handoff-source.disk");
        Import(root, expected, row, destination);
        return destination;
    }

    internal static void Import(string root, string expectedCatalogHash, string row, string destination,
        int expectedCount = 84, int expectedSchema = 1, string expectedStatus = "linux-inputs-verified")
    {
        string catalogPath = Child(root, "catalog.json");
        if (!string.Equals(Hash(catalogPath), expectedCatalogHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Input catalog SHA256 mismatch.");
        }
        if (new FileInfo(catalogPath).Length > 1024 * 1024) { throw new InvalidDataException("Oversized catalog."); }
        Catalog catalog = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(catalogPath), Json)
            ?? throw new InvalidDataException("Missing input catalog.");
        if (catalog.Schema != expectedSchema || catalog.Status != expectedStatus || catalog.Inputs.Length != expectedCount ||
            catalog.Inputs.Select(input => input.Row).Distinct(StringComparer.Ordinal).Count() != expectedCount)
        {
            throw new InvalidDataException("Incomplete or duplicate input catalog.");
        }
        Input input = catalog.Inputs.Single(item => item.Row == row);
        if (input.File != row + ".disk") { throw new InvalidDataException("Input row/file identity mismatch."); }
        string source = Child(root, input.File);
        Verify(source, input.Length, input.Sha256);
        File.Copy(source, destination, overwrite: false);
        Verify(destination, input.Length, input.Sha256);
    }

    internal static void Export(string row, string source)
    {
        string? root = Environment.GetEnvironmentVariable("SECTORWORKS_WRITER_EXPORT");
        if (root is null) { return; }
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("SECTORWORKS_WRITER_INPUTS") is not null)
        {
            throw new InvalidDataException("Input authoring must run independently on Linux.");
        }
        lock (Gate)
        {
            string metadata = Child(root, row + ".json");
            if (File.Exists(metadata)) { return; }
            string destination = Child(root, row + ".disk");
            File.Copy(source, destination, overwrite: false);
            var input = new Input(row, row + ".disk", new FileInfo(destination).Length, Hash(destination));
            File.WriteAllText(metadata, JsonSerializer.Serialize(input, Json));
        }
    }

    internal static bool Queue(string source, string output, string row, string workflow)
    {
        if (Environment.GetEnvironmentVariable("SECTORWORKS_WRITER_INPUTS") is not string inputs) { return false; }
        string root = Environment.GetEnvironmentVariable("SECTORWORKS_WRITER_OUTPUTS")
            ?? throw new InvalidDataException("Missing pending-output root.");
        string catalogHash = Environment.GetEnvironmentVariable("SECTORWORKS_WRITER_CATALOG_SHA256")
            ?? throw new InvalidDataException("Missing catalog SHA256.");
        Retain(inputs, root, catalogHash, source, output, row, workflow);
        Console.WriteLine($"INDEPENDENT_VALIDATION_PENDING {row} {workflow}");
        return true;
    }

    internal static void Retain(string inputs, string root, string catalogHash, string source,
        string output, string row, string workflow)
    {
        string catalogPath = Child(inputs, "catalog.json");
        if (!string.Equals(Hash(catalogPath), catalogHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Catalog changed during managed workflow.");
        }
        Catalog catalog = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(catalogPath), Json)!;
        Input input = catalog.Inputs.Single(item => item.Row == row);
        Verify(Child(inputs, input.File), input.Length, input.Sha256);
        if (workflow is not ("saveas" or "commit" or "growth-first" or "growth-second"))
        {
            throw new InvalidDataException("Unknown workflow.");
        }
        if (workflow == "saveas") { Verify(source, input.Length, input.Sha256); }
        string name = row + "." + workflow;
        lock (Gate)
        {
            string destination = Child(root, name + ".disk");
            File.Copy(output, destination, overwrite: false);
            string hash = Hash(destination);
            if (hash != Hash(output)) { throw new InvalidDataException("Output changed during retention."); }
            using IImageSession image = new ImageOpener().OpenReadOnly(destination);
            var volume = Assert.Single(image.Volumes);
            var pending = new Pending(1, "pending-independent-linux-validation", row, workflow,
                catalogHash, input.Sha256, name + ".disk", new FileInfo(destination).Length, hash,
                volume.Partition?.Offset ?? 0, volume.Partition?.Length ?? image.Device.Length,
                System.Runtime.InteropServices.RuntimeInformation.OSDescription);
            File.WriteAllText(Child(root, name + ".json"), JsonSerializer.Serialize(pending, Json));
        }
    }

    internal static string Child(string root, string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." ||
            name.IndexOfAny(['/', '\\', ':']) >= 0 || Path.GetFileName(name) != name)
        {
            throw new InvalidDataException("Handoff filenames must be single ordinary path components.");
        }
        string fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot)) { throw new DirectoryNotFoundException(fullRoot); }
        var directory = new DirectoryInfo(fullRoot);
        for (DirectoryInfo? current = directory; current is not null; current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Linked handoff directories are not allowed.");
            }
        }
        string path = Path.Combine(fullRoot, name);
        if ((File.Exists(path) || Directory.Exists(path)) &&
            (File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
        {
            throw new InvalidDataException("Linked or directory handoff files are not allowed.");
        }
        return path;
    }

    internal static void Verify(string path, long length, string expected)
    {
        if (length <= 0 || length > 1024L * 1024 * 1024 || new FileInfo(path).Length != length ||
            !string.Equals(Hash(path), expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Handoff file length/SHA256 mismatch.");
        }
    }

    internal static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
}
