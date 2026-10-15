using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using DiscUtils;
using DiscUtils.Fat;
using SectorWorks.Core;
using SectorWorks.Platform;
using SectorWorks.Services;
using SectorWorks.UI;
using SectorWorks.UI.ViewModels;

namespace SectorWorks.UITests;

internal static class Phase2DesktopSmoke
{
    internal const string OriginalText = "Generated Phase 2 source content.\n";
    internal const string UnchangedText = "This file must survive Save As unchanged.\n";
    internal const string ReplacementText = "Replacement staged through the physical desktop UI.\n";

    public static void Initialize(IClassicDesktopStyleApplicationLifetime desktop)
    {
        bool minimum = DesktopSmoke.Arguments.Length == 4 && DesktopSmoke.Arguments[3] == "minimum";
        var window = new MainWindow { ShowActivated = false, Width = minimum ? 1000 : 1200, Height = minimum ? 640 : 800 };
        string directory = Path.GetFullPath(DesktopSmoke.Arguments[1]);
        var dialogs = new Phase2SmokeDialogs(directory);
        var service = new ExplorerService(stagingDirectory: Path.Combine(directory, "workspace-data"));
        var main = new MainWindowViewModel(service, dialogs, new MemoryLayout());
        window.DataContext = main;
        desktop.MainWindow = window;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
        timeout.Tick += (_, _) =>
        {
            Console.Error.WriteLine("FAIL physical Phase 2: 90-second deadline exceeded.");
            main.Dispose();
            desktop.Shutdown(124);
        };
        timeout.Start();
        var smoke = new AsyncRelayCommand(async () =>
        {
            int exitCode = 1;
            try
            {
                string variant = DesktopSmoke.Arguments[0] == "--phase2b"
                    ? DesktopSmoke.Arguments.Length == 4 ? DesktopSmoke.Arguments[3] : "raw-mbr"
                    : "raw";
                string source = CreateFixture(directory, variant);
                string stagedDirectory = variant == "raw" ? "STAGED" : "Mixed directory";
                string addedName = variant == "raw" ? "ADDED.TXT" : "Added-\u03bb.txt";
                string renamedName = variant == "raw" ? "RENAMED.TXT" : "Renamed-\u03bb.txt";
                string sourceHash = Hash(source);
                dialogs.ImportPath = Path.Combine(directory, "import.txt");
                File.WriteAllText(dialogs.ImportPath, "Added through the physical desktop UI.\n", new UTF8Encoding(false));
                string replacement = Path.Combine(directory, "replacement.txt");
                File.WriteAllText(replacement, ReplacementText, new UTF8Encoding(false));
                await main.OpenPathsCommand.ExecuteAsync(new[] { source });
                Check(main.Error.Length == 0, main.Error);
                WorkspaceViewModel workspace = main.ActiveWorkspace ?? throw new InvalidOperationException("No source workspace.");
                Check(workspace.CanAdd && workspace.CanCreateDirectory, workspace.AddReason);
                Check(!workspace.Info.Capabilities.CommitInPlace.Available && !workspace.Info.Capabilities.Rebuild.Available,
                    "Phase 3/4 operations must remain unavailable.");
                await Task.Delay(300);
                window.UpdateLayout();

                dialogs.Name = stagedDirectory;
                await InvokeToolbarAsync(window, "New Folder");
                CheckWorkspace(workspace);
                Check(workspace.Entries.Any(e => e.Name == stagedDirectory && e.IsChanged && e.IsDirectory), "New directory is not marked staged.");
                await workspace.NavigateAsync("/" + stagedDirectory);
                CheckWorkspace(workspace);
                dialogs.Name = addedName;
                await InvokeToolbarAsync(window, "Add");
                CheckWorkspace(workspace);
                await SelectFileAsync(window, workspace, addedName);
                Check(workspace.PreviewText.Contains("Added through", StringComparison.Ordinal), "Added-file preview does not use staged bytes.");
                dialogs.ImportPath = replacement;
                await InvokeToolbarAsync(window, "Replace");
                CheckWorkspace(workspace);
                await SelectFileAsync(window, workspace, addedName);
                Check(workspace.PreviewText.Contains(ReplacementText, StringComparison.Ordinal), "Replacement preview does not use staged bytes.");
                dialogs.Name = renamedName;
                await InvokeToolbarAsync(window, "Rename");
                CheckWorkspace(workspace);
                await SelectFileAsync(window, workspace, renamedName);
                Check(workspace.Selection.Single().IsChanged, "Renamed entry is not marked staged.");
                await InvokeToolbarAsync(window, "Extract");
                CheckWorkspace(workspace);
                string extracted = Path.Combine(dialogs.ExtractionDirectory, renamedName);
                Check(File.Exists(extracted) && Hash(extracted) == Hash(replacement), "Staged extraction differs from replacement content.");

                await workspace.NavigateAsync("/");
                await SelectFileAsync(window, workspace, "DELETE.TXT");
                await InvokeToolbarAsync(window, "Delete");
                CheckWorkspace(workspace);
                Check(workspace.Entries.All(e => e.Name != "DELETE.TXT"), "Staged delete is still visible.");
                await InvokeToolbarAsync(window, "Undo");
                CheckWorkspace(workspace);
                Check(workspace.Entries.Any(e => e.Name == "DELETE.TXT"), "Undo did not restore the deleted entry.");
                await InvokeToolbarAsync(window, "Redo");
                CheckWorkspace(workspace);
                Check(workspace.Entries.All(e => e.Name != "DELETE.TXT"), "Redo did not delete the entry again.");
                Check(workspace.IsModified && workspace.PendingChanges.Count == 5, "Expected five ordered staged changes.");
                Check(workspace.PendingChanges.All(c => c.VolumeId == workspace.Volume!.Id), "Pending changes lost their volume context.");
                Check(Hash(source) == sourceHash, "Source changed while staging.");

                await InvokeToolbarAsync(window, "Save As");
                CheckWorkspace(workspace);
                Check(File.Exists(dialogs.OutputPath) && workspace.SaveResult.Contains("Validated new image:", StringComparison.Ordinal),
                    "Save As did not report a validated new image.");
                Check(workspace.IsModified && workspace.PendingChanges.Count == 5, "Save As unexpectedly discarded pending changes.");
                Check(Hash(source) == sourceHash, "Source changed during Save As.");
                await main.OpenPathsCommand.ExecuteAsync(new[] { dialogs.OutputPath });
                Check(main.Error.Length == 0, main.Error);
                WorkspaceViewModel reopened = main.ActiveWorkspace ?? throw new InvalidOperationException("No reopened output workspace.");
                Check(reopened != workspace && !reopened.IsModified, "Saved output was not reopened as a clean workspace.");
                Check(reopened.Entries.All(e => e.Name != "DELETE.TXT"), "Deleted file survived in saved output.");
                await SelectFileAsync(window, reopened, "KEEP.TXT");
                Check(reopened.PreviewText.Contains(UnchangedText, StringComparison.Ordinal), "Unchanged file content was not preserved.");
                await reopened.NavigateAsync("/" + stagedDirectory);
                await SelectFileAsync(window, reopened, renamedName);
                Check(reopened.PreviewText.Contains(ReplacementText, StringComparison.Ordinal), "Reopened output content differs from staged replacement.");
                await main.CloseCommand.ExecuteAsync(reopened);
                Check(main.Error.Length == 0 && main.Workspaces.Count == 1, "Reopened workspace did not close cleanly.");

                main.ActiveWorkspace = workspace;
                await InvokeToolbarAsync(window, "Changes");
                await Task.Delay(200);
                window.UpdateLayout();
                WorkspaceView view = window.GetVisualDescendants().OfType<WorkspaceView>().Single(v => v.IsVisible);
                Check(view.Bounds.Width > 0 && workspace.ToolIndex == 8, "Pending Changes view did not render.");
                Check(view.GetVisualDescendants().OfType<ListBox>().Any(list => list.ItemsSource == workspace.PendingChanges),
                    "Rendered pending list does not expose current changes.");
                using (var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.Bounds.Size, window.RenderScaling),
                    new Vector(96 * window.RenderScaling, 96 * window.RenderScaling)))
                {
                    bitmap.Render(window);
                    bitmap.Save(DesktopSmoke.Arguments[2], PngBitmapEncoderOptions.Default);
                }
                dialogs.Confirm = false;
                await main.CloseCommand.ExecuteAsync(workspace);
                Check(main.Workspaces.Count == 1 && workspace.IsModified, "Cancelled close lost pending changes.");
                dialogs.Confirm = true;
                await workspace.DiscardCommand.ExecuteAsync(null);
                CheckWorkspace(workspace);
                Check(!workspace.IsModified && workspace.PendingChanges.Count == 0, "Discard did not clear staged changes.");
                Check(workspace.Entries.All(e => e.Name != stagedDirectory) && workspace.Entries.Any(e => e.Name == "DELETE.TXT"),
                    "Discard did not restore the original source tree.");
                Check(Hash(source) == sourceHash, "Source changed after discard.");
                await main.CloseCommand.ExecuteAsync(workspace);
                Check(main.Workspaces.Count == 0 && main.Error.Length == 0, "Source workspace did not close cleanly.");
                string filesystem = variant.StartsWith("ntfs-", StringComparison.Ordinal) ? "NTFS" : "FAT12";
                Console.WriteLine($"PASS physical staged editing: generated {variant} {filesystem}; add/replace/rename/delete/new-directory; changed markers, ordered volume-scoped pending list, undo/redo, staged preview/extraction, Save As validation/output reopen, cancelled close, discard; scaling={window.RenderScaling}");
                Console.WriteLine($"PASS source unchanged: {source}; SHA-256 {sourceHash}");
                Console.WriteLine($"PASS staged extraction/output: {extracted}; SHA-256 {Hash(extracted)}; output={dialogs.OutputPath}");
                Console.WriteLine($"PASS app-window-only screenshot: {DesktopSmoke.Arguments[2]}; generated disposable artifacts={directory}");
                exitCode = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL physical Phase 2: " + ex); }
            finally { timeout.Stop(); main.Dispose(); desktop.Shutdown(exitCode); }
        });
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => smoke.Execute(null));
    }

    internal static string CreateFixture(string directory, string variant = "raw")
    {
        bool ntfs = variant.StartsWith("ntfs-", StringComparison.Ordinal);
        if (ntfs) { variant = variant[5..]; }
        if (variant is not ("raw" or "raw-mbr" or "vhd-fixed" or "vhd-dynamic" or "vhdx-fixed" or "vhdx-dynamic"))
        {
            throw new ArgumentException("Unsupported disposable desktop fixture variant.", nameof(variant));
        }
        if (Directory.Exists(directory) || File.Exists(directory)) { throw new IOException("Phase 2 smoke requires a NEW disposable fixture directory."); }
        Directory.CreateDirectory(directory);
        string image = Path.Combine(directory, "source.img");
        if (ntfs)
        {
            using (FileStream stream = File.Create(image)) { stream.SetLength(32 * 1024 * 1024); }
            RunFixtureTool("mkntfs", "-F", "-Q", "-T", "-L", "SW_NTFS", "-s", "512", "-p", "0", "-H", "4", "-S", "32", image);
            string payload = Path.Combine(directory, "fixture-payload.txt");
            foreach ((string name, string text) in new[]
            {
                ("README.TXT", OriginalText), ("KEEP.TXT", UnchangedText), ("DELETE.TXT", "Generated file for staged deletion.\n")
            })
            {
                File.WriteAllText(payload, text, new UTF8Encoding(false));
                RunFixtureTool("ntfscp", "-f", image, payload, "/" + name);
            }
            File.Delete(payload);
        }
        else
        {
            using (var stream = new FileStream(image, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (FatFileSystem fileSystem = FatFileSystem.FormatFloppy(stream, FloppyDiskType.HighDensity, "SW_PHASE2"))
                {
                    WriteFile(fileSystem, "README.TXT", OriginalText);
                    WriteFile(fileSystem, "KEEP.TXT", UnchangedText);
                    WriteFile(fileSystem, "DELETE.TXT", "Generated file for staged deletion.\n");
                }
                stream.Flush(true);
            }
            GeneratedFatFixtureMetadata.Complete(image);
        }
        if (variant == "raw") { return image; }
        string disk = Path.Combine(directory, "partitioned.img");
        using (var output = new FileStream(disk, FileMode.CreateNew, FileAccess.ReadWrite))
        {
            output.SetLength(ntfs ? 36 * 1024 * 1024 : 16 * 1024 * 1024);
            byte[] mbr = new byte[512];
            "PHASE2B-OPAQUE-BOOT"u8.CopyTo(mbr);
            mbr[450] = ntfs ? (byte)7 : (byte)1;
            BinaryPrimitives.WriteUInt32LittleEndian(mbr.AsSpan(454), 2048);
            BinaryPrimitives.WriteUInt32LittleEndian(mbr.AsSpan(458), checked((uint)(new FileInfo(image).Length / 512)));
            mbr[510] = 0x55;
            mbr[511] = 0xaa;
            output.Write(mbr);
            output.Position = 2048 * 512L;
            using var input = File.OpenRead(image);
            input.CopyTo(output);
            output.Position = 2048 * 512L + 28;
            byte[] hidden = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(hidden, 2048);
            output.Write(hidden);
            if (ntfs)
            {
                output.Position = 2048 * 512L + input.Length - 512 + 28;
                output.Write(hidden);
            }
            output.Flush(true);
        }
        if (variant == "raw-mbr") { return disk; }
        bool vhdx = variant.StartsWith("vhdx", StringComparison.Ordinal);
        string container = Path.Combine(directory, vhdx ? "source.vhdx" : "source.vhd");
        string options = "subformat=" + (variant.EndsWith("fixed", StringComparison.Ordinal) ? "fixed" : "dynamic")
            + (vhdx ? "" : ",force_size=on");
        RunFixtureTool("qemu-img", "convert", "-f", "raw", "-O", vhdx ? "vhdx" : "vpc", "-o", options, disk, container);
        return container;
    }

    private static void RunFixtureTool(string command, params string[] arguments)
    {
        var start = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using Process process = Process.Start(start) ?? throw new IOException("Could not start independent fixture author: " + command);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000))
        {
            process.Kill(true);
            process.WaitForExit();
            throw new IOException("Independent fixture author exceeded its 60-second deadline: " + command);
        }
        if (process.ExitCode != 0)
        {
            throw new IOException("Independent fixture author failed: " + command + ": " + stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
        }
    }

    private static void WriteFile(FatFileSystem fileSystem, string name, string content)
    {
        using Stream file = fileSystem.OpenFile(name, FileMode.CreateNew, FileAccess.Write);
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        file.Write(bytes);
    }

    private static async Task InvokeToolbarAsync(MainWindow window, string label)
    {
        window.UpdateLayout();
        Button button = window.GetVisualDescendants().OfType<Button>().First(b =>
            b.Parent is StackPanel { Orientation: Avalonia.Layout.Orientation.Horizontal }
            && (Equals(b.Content, label) || b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == label)));
        Check(button.IsEffectivelyEnabled, $"Rendered {label} command is disabled.");
        if (button.Command is IAsyncRelayCommand command) { await command.ExecuteAsync(button.CommandParameter); }
        else { button.Command?.Execute(button.CommandParameter); }
    }

    private static async Task SelectFileAsync(MainWindow window, WorkspaceViewModel workspace, string name)
    {
        await Task.Delay(50);
        window.UpdateLayout();
        FileEntry file = workspace.Entries.Single(e => e.Name == name && !e.IsDirectory);
        WorkspaceView view = window.GetVisualDescendants().OfType<WorkspaceView>().Single(v => v.IsVisible);
        view.FindControl<DataGrid>("FileGrid")!.SelectedItem = file;
        Check(workspace.Selection.Count == 1 && workspace.Selection[0].Path == file.Path, "Rendered file-grid selection did not reach the view model.");
        if (workspace.PreviewCommand.ExecutionTask is Task preview) { await preview; }
        CheckWorkspace(workspace);
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    private static void CheckWorkspace(WorkspaceViewModel workspace) => Check(workspace.Error.Length == 0, workspace.Error);
    private static void Check(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}

internal sealed class Phase2SmokeDialogs(string directory) : IPlatformDialogs, IImageEditingDialogs
{
    public string? ImportPath { get; set; }
    public string? Name { get; set; }
    public bool Confirm { get; set; } = true;
    public string OutputPath => Path.Combine(directory, "modified.img");
    public string ExtractionDirectory => Path.Combine(directory, "staged-extraction");
    public Task<IReadOnlyList<string>> PickImagesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<string?> PickExtractionFolderAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ExtractionDirectory);
        return Task.FromResult<string?>(ExtractionDirectory);
    }
    public Task<string?> PickImportFileAsync(CancellationToken cancellationToken = default) => Task.FromResult(ImportPath);
    public Task<string?> PickNewImagePathAsync(string suggestedName, CancellationToken cancellationToken = default) => Task.FromResult<string?>(OutputPath);
    public Task<string?> RequestNameAsync(string title, string initialName, CancellationToken cancellationToken = default) => Task.FromResult(Name);
    public Task<bool> ConfirmAsync(string title, string message, string acceptLabel, CancellationToken cancellationToken = default) => Task.FromResult(Confirm);
}
