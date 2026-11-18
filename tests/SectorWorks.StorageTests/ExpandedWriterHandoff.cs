using System.Text.Json;
using SectorWorks.Core;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

// Separate catalog/schema/environment from the retained 84-row pipeline. A
// Windows managed pass always leaves independent output validation pending.
internal static class ExpandedWriterHandoff
{
    private static readonly object Gate = new();
    internal static string Row(byte major, byte minor, string feature, string container, string layout, int physical) =>
        PortableWriterHandoff.Row(container, layout, $"ntfs{major}-{minor}-{feature.Replace(':', '-')}", physical, false);

    internal static string? Import(WorkDirectory work, string row)
    {
        string? root = Environment.GetEnvironmentVariable("SECTORWORKS_EXPANDED_INPUTS");
        if (root is null) { return null; }
        if (Environment.GetEnvironmentVariable("SECTORWORKS_EXPANDED_EXPORT") is not null
            || Environment.GetEnvironmentVariable("SECTORWORKS_WRITER_INPUTS") is not null
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SECTORWORKS_EXPANDED_OUTPUTS")))
        { throw new InvalidDataException("Expanded handoff requires its own separate input/output roots and cannot author inputs."); }
        string expected = Environment.GetEnvironmentVariable("SECTORWORKS_EXPANDED_CATALOG_SHA256")
            ?? throw new InvalidDataException("A separately supplied expanded catalog SHA256 is required.");
        string destination = work.PathFor("expanded-handoff-source.disk");
        Import(root, expected, row, destination);
        return destination;
    }

    internal static void Import(string root, string expected, string row, string destination)
    {
        string catalogPath = PortableWriterHandoff.Child(root, "catalog.json");
        if (PortableWriterHandoff.Hash(catalogPath) != expected.ToUpperInvariant() || new FileInfo(catalogPath).Length > 1024 * 1024)
        { throw new InvalidDataException("Expanded catalog changed or exceeds its bound."); }
        PortableWriterHandoff.Catalog catalog = JsonSerializer.Deserialize<PortableWriterHandoff.Catalog>(File.ReadAllText(catalogPath), PortableWriterHandoff.Json)
            ?? throw new InvalidDataException("Expanded catalog is empty.");
        var required = NtfsExpandedWorkflowTests.DiskRows().Select(r => Row((byte)r[0], (byte)r[1], (string)r[2], (string)r[3], (string)r[4], (int)r[5]))
            .ToHashSet(StringComparer.Ordinal);
        if (catalog.Inputs is null || catalog.Inputs.Length != 441 || !required.SetEquals(catalog.Inputs.Select(i => i.Row)))
        { throw new InvalidDataException("Expanded catalog does not contain the exact 441 required rows."); }
        PortableWriterHandoff.Import(root, expected, row, destination, 441, 2, "linux-expanded-inputs-verified");
    }

    internal static void Export(string row, string source)
    {
        string? root = Environment.GetEnvironmentVariable("SECTORWORKS_EXPANDED_EXPORT");
        if (root is null) { return; }
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("SECTORWORKS_EXPANDED_INPUTS") is not null)
        { throw new InvalidDataException("Expanded input authoring must be independently checked on Linux."); }
        lock (Gate)
        {
            string destination = PortableWriterHandoff.Child(root, row + ".disk");
            File.Copy(source, destination, overwrite: false);
            var input = new PortableWriterHandoff.Input(row, row + ".disk", new FileInfo(destination).Length, PortableWriterHandoff.Hash(destination));
            File.WriteAllText(PortableWriterHandoff.Child(root, row + ".json"), JsonSerializer.Serialize(input, PortableWriterHandoff.Json));
        }
    }

    internal static bool Queue(string source, string output, string row, string workflow)
    {
        if (Environment.GetEnvironmentVariable("SECTORWORKS_EXPANDED_INPUTS") is not string inputs) { return false; }
        string root = Environment.GetEnvironmentVariable("SECTORWORKS_EXPANDED_OUTPUTS") ?? throw new InvalidDataException("Missing expanded output root.");
        string catalogHash = Environment.GetEnvironmentVariable("SECTORWORKS_EXPANDED_CATALOG_SHA256") ?? throw new InvalidDataException("Missing expanded catalog SHA256.");
        PortableWriterHandoff.Retain(inputs, root, catalogHash, source, output, row, workflow);
        Console.WriteLine($"INDEPENDENT_EXPANDED_VALIDATION_PENDING {row} {workflow}");
        return true;
    }
}
