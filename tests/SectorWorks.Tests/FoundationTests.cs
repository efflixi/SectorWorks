using System.Xml.Linq;
using SectorWorks.UI.ViewModels;

namespace SectorWorks.Tests;

public sealed class FoundationTests
{
    [Theory]
    [InlineData("Core")]
    [InlineData("Storage")]
    [InlineData("Services")]
    [InlineData("Platform")]
    [InlineData("ManagedNtfs")]
    [InlineData("NtfsSecurity")]
    public void NonUiProjectsHaveNoUiDependencies(string layer)
    {
        XDocument project = LoadProject(layer);
        Assert.All(project.Descendants("PackageReference"), reference =>
        {
            string package = reference.Attribute("Include")!.Value;
            Assert.False(package.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase));
            Assert.NotEqual("CommunityToolkit.Mvvm", package);
        });
        Assert.All(project.Descendants("ProjectReference"), reference =>
            Assert.DoesNotContain("SectorWorks.UI", reference.Attribute("Include")!.Value));
    }

    [Theory]
    [InlineData("Core", "")]
    [InlineData("Storage", "SectorWorks.Core,SectorWorks.ManagedNtfs")]
    [InlineData("ManagedNtfs", "SectorWorks.NtfsSecurity")]
    [InlineData("NtfsSecurity", "")]
    [InlineData("Platform", "SectorWorks.Core")]
    [InlineData("Services", "SectorWorks.Core,SectorWorks.Platform,SectorWorks.Storage")]
    [InlineData("UI", "SectorWorks.Core,SectorWorks.Platform,SectorWorks.Services")]
    public void ProjectReferencesFollowTheLayeredArchitecture(string layer, string expected)
    {
        string[] references = LoadProject(layer).Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries), references);
    }

    [Fact]
    public void ViewModelReportsPropertyChangesOnlyWhenValuesChange()
    {
        var viewModel = new TestViewModel();
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        viewModel.Name = "SectorWorks";
        viewModel.Name = "SectorWorks";

        Assert.Equal(["Name"], changes);
    }

    private static XDocument LoadProject(string layer)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Architecture",
            $"SectorWorks.{layer}", $"SectorWorks.{layer}.csproj");
        return XDocument.Load(path);
    }

    private sealed class TestViewModel : ViewModelBase
    {
        private string name = string.Empty;

        public string Name
        {
            get => name;
            set => SetProperty(ref name, value);
        }
    }
}
