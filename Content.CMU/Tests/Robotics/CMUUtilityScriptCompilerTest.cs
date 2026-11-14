using System.Globalization;
using System.IO;
using System.Linq;
using Content.Server.CMU14.Robotics.UtilityDrones;
using Content.Tests;
using NUnit.Framework;
using Robust.Shared.ContentPack;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Utility;

namespace Content.Tests.CMU14.Robotics;

[TestFixture]
public sealed class CMUUtilityScriptCompilerTest : ContentUnitTest
{
    private readonly CMUUtilityScriptCompiler _compiler = new();

    [OneTimeSetUp]
    public void SetupLocalization()
    {
        var path = Find("Content.CMU", "Resources", "Locale", "en-US", "CMU14", "utility-drones.ftl");
        var root = new MemoryContentRoot();
        root.AddOrUpdateFile(
            new ResPath("Locale/en-US/CMU14/utility-drones.ftl"),
            File.ReadAllBytes(path));
        IoCManager.Resolve<IResourceManager>().AddRoot(new ResPath("/"), root);

        var localization = IoCManager.Resolve<ILocalizationManager>();
        localization.Initialize();
        localization.LoadCulture(new CultureInfo("en-US", false));
    }

    [Test]
    public void CompilesMainLabelsAndFullHandlerActions()
    {
        var source = """
            on heard "код" {
              take "Crowbar" into left
              goto main.alarm
            }

            main {
              patrol:
                move marker 1
                wait 1
                goto patrol

              alarm:
                say "alarm"
                stop
            }
            """;

        Assert.That(_compiler.TryCompile(source, out var program, out var error), Is.True, error);
        Assert.That(program.Labels.Keys, Does.Contain("patrol"));
        Assert.That(program.Labels.Keys, Does.Contain("alarm"));
        Assert.That(program.EventHandlers, Has.Count.EqualTo(1));
    }

    [Test]
    public void RejectsUnknownMainLabel()
    {
        var source = """
            on heard "код" {
              goto main.missing
            }

            main {
              stop
            }
            """;

        Assert.That(_compiler.TryCompile(source, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("main.missing"));
    }

    [Test]
    public void RejectsUnknownLocalLabel()
    {
        Assert.That(
            _compiler.TryCompile("main { goto missing }", out _, out var error),
            Is.False);
        Assert.That(error, Does.Not.Empty);
    }

    [Test]
    public void RejectsDuplicateLabels()
    {
        var source = """
            main {
              loop:
                stop
              loop:
                stop
            }
            """;

        Assert.That(_compiler.TryCompile(source, out _, out var error), Is.False);
        Assert.That(error, Is.Not.Empty);
    }

    [Test]
    public void RejectsUnknownNativeFunction()
    {
        Assert.That(
            _compiler.TryCompile("main { missingnative() }", out _, out var error),
            Is.False);
        Assert.That(error, Does.Contain("missingnative"));
    }

    [Test]
    public void RejectsWrongNativeArity()
    {
        Assert.That(
            _compiler.TryCompile("main { marker() }", out _, out var error),
            Is.False);
        Assert.That(error, Does.Contain("marker"));
    }

    [Test]
    public void RejectsWrongHandlerNativeArity()
    {
        const string source = """
            on damaged {
              worncontains("wey")
            }

            main {
              stop
            }
            """;

        Assert.That(_compiler.TryCompile(source, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("worncontains"));
    }

    [Test]
    public void AllowsMainAndHandlerVariables()
    {
        var source = """
            let hit_message = "1"

            on damaged {
              say hit_message
              return
            }

            main {
              set hit_message = "2"
              stop
            }
            """;

        Assert.That(_compiler.TryCompile(source, out _, out var error), Is.True, error);
    }

    [Test]
    public void CompilesCombatAndRepairCommands()
    {
        var source = """
            on damaged {
              disengage
            }

            main {
              wield
              aim at person("Urist")
              shoot 3
              reload
              hold fire
              free fire
              fight nearesthostile(12) within 10
              disengage
              repair self within 8
              repair ally
              repair
              autorepair
              stop
            }
            """;

        Assert.That(_compiler.TryCompile(source, out var program, out var error), Is.True, error);
        Assert.That(
            program.Instructions.Exists(instruction => instruction.Op == CMUUtilityScriptOp.Fight),
            Is.True);
        Assert.That(
            program.Instructions.Exists(instruction => instruction.Op == CMUUtilityScriptOp.Repair),
            Is.True);
    }

    [Test]
    public void RejectsCombatNativeArity()
    {
        Assert.That(
            _compiler.TryCompile("main { health(1, 2) }", out _, out var error),
            Is.False);
        Assert.That(error, Does.Contain("health"));
    }

    private static string Find(params string[] parts)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            var path = parts.Aggregate(directory.FullName, Path.Combine);
            if (File.Exists(path))
                return path;

            directory = directory.Parent;
        }

        Assert.Fail("Utility drone localization resource not found");
        return string.Empty;
    }
}
