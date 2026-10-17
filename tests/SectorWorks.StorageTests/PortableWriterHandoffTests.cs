using System.Text.Json;
using static SectorWorks.StorageTests.StorageTests;

namespace SectorWorks.StorageTests;

public sealed class PortableWriterHandoffTests
{
    private const string Row = "raw.filesystem.fat12.p512.lfn";

    [Fact]
    public void ExactCatalogAndInputAreVerifiedBeforeCopying()
    {
        using var work = new WorkDirectory();
        string root = Catalog(work);
        string destination = work.PathFor("copied.disk");
        PortableWriterHandoff.Import(root, PortableWriterHandoff.Hash(Path.Combine(root, "catalog.json")), Row, destination);
        Assert.Equal("legal generated payload", File.ReadAllText(destination));
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("input")]
    [InlineData("missing-input")]
    [InlineData("duplicate-row")]
    [InlineData("pending-catalog")]
    public void IncompleteOrChangedHandoffFailsBeforeAWorkflow(string failure)
    {
        using var work = new WorkDirectory();
        string root = Catalog(work);
        string catalogPath = Path.Combine(root, "catalog.json");
        string sha = PortableWriterHandoff.Hash(catalogPath);
        if (failure == "catalog") { File.AppendAllText(catalogPath, " "); }
        if (failure == "input") { File.WriteAllText(Path.Combine(root, Row + ".disk"), "tampered"); }
        if (failure == "missing-input") { File.Delete(Path.Combine(root, Row + ".disk")); }
        if (failure is "duplicate-row" or "pending-catalog")
        {
            var catalog = JsonSerializer.Deserialize<PortableWriterHandoff.Catalog>(
                File.ReadAllText(catalogPath), PortableWriterHandoff.Json)!;
            if (failure == "duplicate-row") { catalog.Inputs[1] = catalog.Inputs[0]; }
            else { catalog = catalog with { Status = "pending" }; }
            File.WriteAllText(catalogPath, JsonSerializer.Serialize(catalog, PortableWriterHandoff.Json));
            sha = PortableWriterHandoff.Hash(catalogPath);
        }
        string destination = work.PathFor("must-not-exist.disk");
        if (failure == "missing-input")
        {
            Assert.Throws<FileNotFoundException>(() => PortableWriterHandoff.Import(root, sha, Row, destination));
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => PortableWriterHandoff.Import(root, sha, Row, destination));
        }
        Assert.False(File.Exists(destination));
    }

    [Theory]
    [InlineData("../elsewhere")]
    [InlineData("..\\elsewhere")]
    [InlineData("C:device")]
    [InlineData("/absolute")]
    [InlineData(".")]
    public void UnsafeHandoffPathsAreRejected(string name)
    {
        using var work = new WorkDirectory();
        Assert.Throws<InvalidDataException>(() => PortableWriterHandoff.Child(
            Path.GetDirectoryName(work.PathFor("unused"))!, name));
    }

    [Fact]
    public void RetainedOutputSurvivesWorkDisposalAndIsExplicitlyPending()
    {
        using var outer = new WorkDirectory();
        string inputs = Catalog(outer);
        string outputs = outer.PathFor("outputs");
        Directory.CreateDirectory(outputs);
        using (var work = new WorkDirectory())
        {
            string source = work.Golden("fat12.img");
            string catalogPath = Path.Combine(inputs, "catalog.json");
            var catalog = JsonSerializer.Deserialize<PortableWriterHandoff.Catalog>(
                File.ReadAllText(catalogPath), PortableWriterHandoff.Json)!;
            int index = Array.FindIndex(catalog.Inputs, input => input.Row == Row);
            catalog.Inputs[index] = catalog.Inputs[index] with
            {
                Length = new FileInfo(source).Length,
                Sha256 = PortableWriterHandoff.Hash(source),
            };
            File.Copy(source, Path.Combine(inputs, Row + ".disk"), overwrite: true);
            File.WriteAllText(catalogPath, JsonSerializer.Serialize(catalog, PortableWriterHandoff.Json));
            string sha = PortableWriterHandoff.Hash(catalogPath);
            PortableWriterHandoff.Retain(inputs, outputs, sha, source, source, Row, "saveas");
            Assert.Throws<IOException>(() =>
                PortableWriterHandoff.Retain(inputs, outputs, sha, source, source, Row, "saveas"));
        }
        var pending = JsonSerializer.Deserialize<PortableWriterHandoff.Pending>(
            File.ReadAllText(Path.Combine(outputs, Row + ".saveas.json")), PortableWriterHandoff.Json)!;
        Assert.Equal("pending-independent-linux-validation", pending.Status);
        Assert.Equal(Row, pending.Row);
        PortableWriterHandoff.Verify(Path.Combine(outputs, pending.File), pending.Length, pending.Sha256);
    }

    private static string Catalog(WorkDirectory work)
    {
        string root = work.PathFor("inputs");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, Row + ".disk"), "legal generated payload");
        var input = new PortableWriterHandoff.Input(Row, Row + ".disk",
            new FileInfo(Path.Combine(root, Row + ".disk")).Length,
            PortableWriterHandoff.Hash(Path.Combine(root, Row + ".disk")));
        var rows = DiskWriterTests.FatRows().Select(row =>
                PortableWriterHandoff.Row((string)row[0], (string)row[1], (string)row[2], (int)row[3], true))
            .Concat(NtfsStagedWorkflowTests.Rows().Select(row =>
                PortableWriterHandoff.Row((string)row[0], (string)row[1], "ntfs", (int)row[2], false)))
            .Select(row => input with { Row = row, File = row + ".disk" }).ToArray();
        File.WriteAllText(Path.Combine(root, "catalog.json"), JsonSerializer.Serialize(
            new PortableWriterHandoff.Catalog(1, "linux-inputs-verified", rows), PortableWriterHandoff.Json));
        return root;
    }
}
