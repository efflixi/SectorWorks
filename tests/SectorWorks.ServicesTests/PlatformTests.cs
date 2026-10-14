using System.Text;
using SectorWorks.Platform;
using SectorWorks.Services;

namespace SectorWorks.ServicesTests;

public sealed class PlatformTests
{
    [Theory]
    [InlineData("../escape")]
    [InlineData("one/../escape")]
    [InlineData("/absolute")]
    [InlineData("C:/drive")]
    [InlineData("back\\slash")]
    [InlineData("file:stream")]
    [InlineData("NUL.txt")]
    [InlineData("com1")]
    [InlineData("COM\u00b9.txt")]
    [InlineData("LPT\u00b2")]
    [InlineData("CONIN$")]
    [InlineData("CON .txt")]
    [InlineData("space ")]
    [InlineData("dot.")]
    [InlineData("one//two")]
    [InlineData("bad\0name")]
    public void UnsafeNamesAreRejectedBeforeCreatingOutput(string path)
    {
        using var directory = new TestDirectory();
        using var target = new SafeExtractionTarget(directory.Path);
        Assert.Throws<InvalidDataException>(() => target.CreateFile(path));
        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));
    }

    [Fact]
    public void CreateNewAndIncompleteCleanupAreVerified()
    {
        using var directory = new TestDirectory();
        using var target = new SafeExtractionTarget(directory.Path);
        using (ExtractionFile incomplete = target.CreateFile("temporary.txt"))
        {
            incomplete.Stream.WriteByte(42);
        }
        Assert.False(File.Exists(Path.Combine(directory.Path, "temporary.txt")));
        using (ExtractionFile complete = target.CreateFile("DOCS/completed.txt"))
        {
            complete.Stream.Write(Encoding.UTF8.GetBytes("completed"));
            complete.Complete();
        }
        Assert.Equal("completed", File.ReadAllText(Path.Combine(directory.Path, "DOCS", "completed.txt")));
        Assert.Throws<IOException>(() => target.CreateFile("DOCS/completed.txt"));
        Assert.Equal("completed", File.ReadAllText(Path.Combine(directory.Path, "DOCS", "completed.txt")));
    }

    [Fact]
    public void UnixSymlinkDestinationAncestorAndExistingChildAreNotFollowed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        using var directory = new TestDirectory();
        using var outside = new TestDirectory();
        string link = Path.Combine(directory.Path, "link");
        Directory.CreateSymbolicLink(link, outside.Path);
        Assert.Throws<IOException>(() => new SafeExtractionTarget(link));
        Directory.CreateDirectory(Path.Combine(outside.Path, "nested"));
        Assert.Throws<IOException>(() => new SafeExtractionTarget(Path.Combine(link, "nested")));
        using var target = new SafeExtractionTarget(directory.Path);
        Assert.Throws<IOException>(() => target.CreateFile("link/escape.txt"));
        Assert.False(File.Exists(Path.Combine(outside.Path, "escape.txt")));
        File.CreateSymbolicLink(Path.Combine(directory.Path, "file.txt"), Path.Combine(outside.Path, "escape.txt"));
        Assert.Throws<IOException>(() => target.CreateFile("file.txt"));
        Assert.False(File.Exists(Path.Combine(outside.Path, "escape.txt")));
    }

    [Fact]
    public void UnixRootPathReplacementCannotRedirectAlreadyOpenedTarget()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        using var directory = new TestDirectory();
        using var outside = new TestDirectory();
        string selected = Path.Combine(directory.Path, "selected");
        string original = Path.Combine(directory.Path, "original");
        Directory.CreateDirectory(selected);
        using var target = new SafeExtractionTarget(selected);
        Directory.Move(selected, original);
        Directory.CreateSymbolicLink(selected, outside.Path);
        using (ExtractionFile file = target.CreateFile("safe.txt"))
        {
            file.Stream.WriteByte(42);
            file.Complete();
        }
        Assert.True(File.Exists(Path.Combine(original, "safe.txt")));
        Assert.Empty(Directory.GetFileSystemEntries(outside.Path));
    }

    [Fact]
    public void JsonLayoutRoundTripAndInvalidSettingsAreExplicit()
    {
        using var directory = new TestDirectory();
        string path = Path.Combine(directory.Path, "layout.json");
        var store = new JsonLayoutStore(path);
        Assert.Equal(new LayoutSettings(), store.Load());
        var settings = new LayoutSettings(280, 310, 240, false);
        store.Save(settings);
        Assert.Equal(settings, store.Load());
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp-*"));
        Assert.Throws<InvalidDataException>(() => store.Save(new LayoutSettings(double.NaN)));
        File.WriteAllText(path, "{\"LeftWidth\":20}");
        Assert.Throws<InvalidDataException>(() => store.Load());
        File.WriteAllText(path, "{invalid");
        Assert.Throws<System.Text.Json.JsonException>(() => store.Load());
    }

    [Fact]
    public void PreviewIsBoundedDoesNotExecuteAndBinaryIsExplicit()
    {
        var preview = new TextPreviewHandler();
        using var large = new MemoryStream(Enumerable.Repeat((byte)'A', 100000).ToArray(), writable: false);
        var text = preview.Preview(large, "script.sh", 65536);
        Assert.True(text.Truncated);
        Assert.Equal(65536, text.Text.Length);
        Assert.Equal(65537, large.Position);
        using var binary = new MemoryStream([0x01, 0x02, 0x03]);
        Assert.Contains("Binary", preview.Preview(binary, "run.exe", 65536).Description);
        using var invalid = new MemoryStream([0xff, 0xab, 0xfd]);
        Assert.Contains("Unsupported", preview.Preview(invalid, "text.txt", 65536).Description);
        using var utf16 = new MemoryStream(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Unicode \u03bb")).ToArray());
        Assert.Equal("Unicode \u03bb", preview.Preview(utf16, "unicode.txt", 65536).Text);
        byte[] boundaryBytes = Enumerable.Repeat((byte)'A', 65535).Concat(Encoding.UTF8.GetBytes("\u03bb!")).ToArray();
        using var boundary = new MemoryStream(boundaryBytes);
        var boundaryResult = preview.Preview(boundary, "boundary.txt", 65536);
        Assert.True(boundaryResult.Truncated);
        Assert.Equal(new string('A', 65535), boundaryResult.Text);
    }

    [Fact]
    public void StructuredLogRecordsMetadataNotContentsAndRotates()
    {
        using var directory = new TestDirectory();
        string path = Path.Combine(directory.Path, "log.jsonl");
        var log = new JsonExplorerLog(path);
        log.Write("Opening", "sample.img", "Read-only");
        string text = File.ReadAllText(path);
        Assert.Contains("\"Stage\":\"Opening\"", text);
        Assert.Contains("\"TimestampUtc\"", text);
        File.WriteAllText(path, new string('x', 4 * 1024 * 1024 + 1));
        log.Write("Ready", "sample.img", "FAT12");
        Assert.True(File.Exists(path + ".previous"));
        Assert.True(new FileInfo(path).Length < 1024);
    }
}
