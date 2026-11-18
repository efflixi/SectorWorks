using System.Text.Json;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class ExpandedWriterHandoffTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("catalog")]
    [InlineData("input")]
    [InlineData("duplicate")]
    [InlineData("wrong-row")]
    [InlineData("schema")]
    [InlineData("pending")]
    public void ExpandedCatalogRequiresExactRowsAndSeparateVerifiedSchemaBeforeCopy(string change)
    {
        using var work = new WorkDirectory();
        string root = work.PathFor("inputs"); Directory.CreateDirectory(root);
        string[] rows = NtfsExpandedWorkflowTests.DiskRows().Select(r => ExpandedWriterHandoff.Row(
            (byte)r[0], (byte)r[1], (string)r[2], (string)r[3], (string)r[4], (int)r[5])).ToArray();
        string source = Path.Combine(root, rows[0] + ".disk"); File.WriteAllText(source, "bounded legal payload");
        var inputs = rows.Select(row => new PortableWriterHandoff.Input(row, row + ".disk", new FileInfo(source).Length, PortableWriterHandoff.Hash(source))).ToArray();
        var catalog = new PortableWriterHandoff.Catalog(2, "linux-expanded-inputs-verified", inputs);
        if (change == "duplicate") { inputs[1] = inputs[0]; }
        if (change == "wrong-row") { inputs[1] = inputs[1] with { Row = "unverified-row" }; }
        if (change == "schema") { catalog = catalog with { Schema = 1 }; }
        if (change == "pending") { catalog = catalog with { Status = "pending" }; }
        string path = Path.Combine(root, "catalog.json");
        File.WriteAllText(path, JsonSerializer.Serialize(catalog, PortableWriterHandoff.Json));
        string hash = PortableWriterHandoff.Hash(path);
        if (change == "catalog") { File.AppendAllText(path, " "); }
        if (change == "input") { File.AppendAllText(source, "changed"); }
        string destination = work.PathFor("imported.disk");
        if (change == "valid")
        {
            ExpandedWriterHandoff.Import(root, hash, rows[0], destination);
            Assert.Equal("bounded legal payload", File.ReadAllText(destination));
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => ExpandedWriterHandoff.Import(root, hash, rows[0], destination));
            Assert.False(File.Exists(destination));
        }
    }
}
