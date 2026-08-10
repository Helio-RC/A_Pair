using System.Text.Json;
using SeatFlow.Infrastructure.Serialization;

namespace SeatFlow.Application.Tests.Plugins;

public class PluginPackageManifestTests
{

    [Fact]
    public void DefaultValues_ShouldBeExpected ()
    {
        var manifest = new PluginPackageManifest();
        manifest.Name.Should().BeEmpty();
        manifest.Id.Should().BeEmpty();
        manifest.Version.Should().Be("1.0.0");
        manifest.Author.Should().BeEmpty();
        manifest.Description.Should().BeEmpty();
        manifest.Type.Should().Be("strategy");
        manifest.Plugins.Should().BeEmpty();
        manifest.Repository.Should().BeNull();
        manifest.Website.Should().BeNull();
    }

    [Fact]
    public void Serialize_ShouldUseCamelCasePropertyNames ()
    {
        var manifest = new PluginPackageManifest
        {
            Id = "test-pkg" ,
            Name = "Test Package" ,
            Version = "1.0.0" ,
            Plugins =
            [
                new PluginEntry
                {
                    Kind = PluginKind.Strategy ,
                    Path = "my_strategy" ,
                    Manifest = "my_strategy/manifest.json" ,
                    Assembly = "MyStrategy.dll" ,
                    EntryType = "MyPlugin.MyStrategy"
                }
            ]
        };

        var json = JsonSerializer.Serialize(manifest , JsonOptions.CaseInsensitiveRead);

        json.Should().Contain("\"id\":");
        json.Should().Contain("\"name\":");
        json.Should().Contain("\"plugins\":");
        json.Should().Contain("\"kind\":");
        json.Should().Contain("\"path\":");
        json.Should().Contain("\"manifest\":");
        json.Should().Contain("\"assembly\":");
        json.Should().Contain("\"entryType\":");
    }

    [Fact]
    public void Deserialize_FromJson_ShouldSetProperties ()
    {
        const string json = """
        {
            "id": "my-pkg",
            "name": "My Package",
            "version": "2.0.0",
            "author": "Author",
            "description": "A test package",
            "type": "strategy",
            "plugins": [
                {
                    "kind": "strategy",
                    "path": "strat1",
                    "manifest": "strat1/manifest.json",
                    "assembly": "Strat1.dll",
                    "entryType": "MyPlugin.Strategy1"
                },
                {
                    "kind": "strategy",
                    "path": "strat2",
                    "manifest": "strat2/manifest.json",
                    "scriptFile": "script.lua",
                    "scriptType": "lua"
                }
            ]
        }
        """;

        var manifest = JsonSerializer.Deserialize<PluginPackageManifest>(json , JsonOptions.CaseInsensitiveRead);
        manifest.Should().NotBeNull();
        manifest!.Id.Should().Be("my-pkg");
        manifest.Name.Should().Be("My Package");
        manifest.Version.Should().Be("2.0.0");
        manifest.Author.Should().Be("Author");
        manifest.Description.Should().Be("A test package");
        manifest.Type.Should().Be("strategy");
        manifest.Plugins.Should().HaveCount(2);

        manifest.Plugins[0].Kind.Should().Be(PluginKind.Strategy);
        manifest.Plugins[0].Path.Should().Be("strat1");
        manifest.Plugins[0].Assembly.Should().Be("Strat1.dll");
        manifest.Plugins[0].EntryType.Should().Be("MyPlugin.Strategy1");

        manifest.Plugins[1].Kind.Should().Be(PluginKind.Strategy);
        manifest.Plugins[1].Path.Should().Be("strat2");
        manifest.Plugins[1].ScriptFile.Should().Be("script.lua");
        manifest.Plugins[1].ScriptType.Should().Be("lua");
    }

    [Fact]
    public void PluginEntry_Defaults_ShouldBeExpected ()
    {
        var entry = new PluginEntry();
        entry.Kind.Should().Be(PluginKind.Strategy);
        entry.Path.Should().BeEmpty();
        entry.Manifest.Should().BeEmpty();
        entry.Assembly.Should().BeNull();
        entry.EntryType.Should().BeNull();
        entry.ScriptFile.Should().BeNull();
        entry.ScriptType.Should().BeNull();
    }

    [Fact]
    public void PluginEntry_UnknownKind_ShouldBePreserved ()
    {
        const string json = """{"id":"p","plugins":[{"kind":"data-provider","path":"x"}]}""";
        var manifest = JsonSerializer.Deserialize<PluginPackageManifest>(json , JsonOptions.CaseInsensitiveRead);
        manifest!.Plugins[0].Kind.Should().Be("data-provider");
    }

    [Fact]
    public void PluginEnables_Defaults_ShouldBeExpected ()
    {
        var enables = new PluginEnables();
        enables.Enabled.Should().BeTrue();
        enables.Type.Should().Be("strategy");
        enables.Strategies.Should().BeEmpty();
    }

    [Fact]
    public void PluginEnables_SerializeDeserialize_RoundTrip ()
    {
        var enables = new PluginEnables
        {
            Enabled = false ,
            Type = "strategy" ,
            Strategies = new Dictionary<string , bool>
            {
                ["strat-a"] = true ,
                ["strat-b"] = false
            }
        };

        var options = JsonOptions.CamelCaseReadWrite;
        var json = JsonSerializer.Serialize(enables , options);
        var deserialized = JsonSerializer.Deserialize<PluginEnables>(json , options);

        deserialized.Should().NotBeNull();
        deserialized!.Enabled.Should().BeFalse();
        deserialized.Strategies["strat-a"].Should().BeTrue();
        deserialized.Strategies["strat-b"].Should().BeFalse();
    }
}
