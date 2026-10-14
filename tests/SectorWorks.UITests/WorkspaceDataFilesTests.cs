using SectorWorks.Platform;

namespace SectorWorks.UITests;

public sealed class WorkspaceDataFilesTests
{
    [Fact]
    public void PayloadIsPrivateBeforeWritingAndDeletedOnClose()
    {
        using var files = new EditingFiles();
        string directory = Path.Combine(files.Path, "private");
        WorkspaceDataFiles.CreatePrivateDirectory(directory);
        string path = Path.Combine(directory, "content");
        using (FileStream payload = WorkspaceDataFiles.CreatePayload(path))
        {
            Assert.Equal(0, payload.Length);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(payload.SafeFileHandle));
            }
            payload.Write([1, 2, 3]);
            payload.Flush();
            Assert.Equal(3, new FileInfo(path).Length);
        }
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ExistingUnixDirectoryIsTightenedAndLinkedFinalDirectoryRejected()
    {
        if (OperatingSystem.IsWindows()) { return; }
        using var files = new EditingFiles();
        string directory = Path.Combine(files.Path, "private");
        Directory.CreateDirectory(directory);
        UnixFileMode publicMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(directory, publicMode);
        Assert.Throws<IOException>(() => WorkspaceDataFiles.CreatePayload(Path.Combine(directory, "not-created")));
        Assert.Empty(Directory.GetFiles(directory));
        WorkspaceDataFiles.CreatePrivateDirectory(directory);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
        File.SetUnixFileMode(directory, publicMode);
        string link = Path.Combine(files.Path, "linked-directory");
        Directory.CreateSymbolicLink(link, directory);
        Assert.Throws<IOException>(() => WorkspaceDataFiles.CreatePrivateDirectory(link));
        Assert.Equal(publicMode, File.GetUnixFileMode(directory));
        Assert.Throws<IOException>(() => WorkspaceDataFiles.CreatePayload(Path.Combine(link, "not-created")));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void CreatePayloadNeverOverwritesAnExistingFile()
    {
        using var files = new EditingFiles();
        string directory = Path.Combine(files.Path, "private");
        WorkspaceDataFiles.CreatePrivateDirectory(directory);
        string existing = Path.Combine(directory, "existing");
        File.WriteAllText(existing, "must remain");
        Assert.Throws<IOException>(() => WorkspaceDataFiles.CreatePayload(existing));
        Assert.Equal("must remain", File.ReadAllText(existing));
    }
}
