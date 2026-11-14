using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Content.Server.CMU14.Robotics.UtilityDrones;
using Content.Shared.CMU14.Robotics.UtilityDrones;
using Content.Tests;
using NUnit.Framework;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Tests.CMU14.Robotics;

[TestFixture]
public sealed class CMUUtilityScriptVmTest : ContentUnitTest
{
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
    public void StackRoundTripsValues()
    {
        var frame = new CMUUtilityScriptFrame(
            new CMUUtilityScriptExecution(),
            new CMUUtilityScriptProgram());

        frame.Push(CMUUtilityScriptValue.FromNumber(12.5));
        frame.Push(CMUUtilityScriptValue.FromString("drone"));
        frame.Push(CMUUtilityScriptValue.FromBool(true));

        Assert.That(frame.TryPop(out var boolean), Is.True);
        Assert.That(boolean.Type, Is.EqualTo(CMUUtilityScriptValueType.Bool));
        Assert.That(boolean.Bool, Is.True);

        Assert.That(frame.TryPop(out var text), Is.True);
        Assert.That(text.AsText, Is.EqualTo("drone"));

        Assert.That(frame.TryPopNumber(out var number), Is.True);
        Assert.That(number, Is.EqualTo(12.5));
    }

    [Test]
    public void EmptyStackFailsTheFrame()
    {
        var frame = new CMUUtilityScriptFrame(
            new CMUUtilityScriptExecution(),
            new CMUUtilityScriptProgram());

        Assert.That(frame.TryPop(out _), Is.False);
        Assert.That(frame.Failed, Is.True);
        Assert.That(frame.Failure, Is.Not.Empty);
    }

    [Test]
    public void StackIsBounded()
    {
        var frame = new CMUUtilityScriptFrame(
            new CMUUtilityScriptExecution(),
            new CMUUtilityScriptProgram());

        for (var i = 0; i < 128; i++)
            frame.Push(CMUUtilityScriptValue.FromNumber(i));

        Assert.That(frame.Failed, Is.False);
        frame.Push(CMUUtilityScriptValue.FromNumber(129));
        Assert.That(frame.Failed, Is.True);
        Assert.That(frame.Stack, Has.Count.EqualTo(128));
    }

    [Test]
    public void ArithmeticComparisonAndConcatenationWork()
    {
        var frame = new CMUUtilityScriptFrame(
            new CMUUtilityScriptExecution(),
            new CMUUtilityScriptProgram());

        frame.Push(CMUUtilityScriptValue.FromNumber(2));
        frame.Push(CMUUtilityScriptValue.FromNumber(3));
        CMUUtilityScriptVm.ExecuteBinary(frame, "-");
        Assert.That(frame.TryPopNumber(out var difference), Is.True);
        Assert.That(difference, Is.EqualTo(-1));

        frame.Push(CMUUtilityScriptValue.FromNumber(1));
        frame.Push(CMUUtilityScriptValue.FromNumber(2));
        CMUUtilityScriptVm.ExecuteBinary(frame, "<");
        Assert.That(frame.TryPop(out var comparison), Is.True);
        Assert.That(comparison.Bool, Is.True);

        frame.Push(CMUUtilityScriptValue.FromString("hello"));
        frame.Push(CMUUtilityScriptValue.FromString(" world"));
        CMUUtilityScriptVm.ExecuteBinary(frame, "+");
        Assert.That(frame.TryPop(out var concatenated), Is.True);
        Assert.That(concatenated.AsText, Is.EqualTo("hello world"));
    }

    [Test]
    public void DivisionByZeroFailsWithoutPushing()
    {
        var frame = new CMUUtilityScriptFrame(
            new CMUUtilityScriptExecution(),
            new CMUUtilityScriptProgram());

        frame.Push(CMUUtilityScriptValue.FromNumber(4));
        frame.Push(CMUUtilityScriptValue.FromNumber(0));
        CMUUtilityScriptVm.ExecuteBinary(frame, "/");

        Assert.That(frame.Failed, Is.True);
        Assert.That(frame.Stack, Is.Empty);
    }

    [Test]
    public void EventQueuesDeduplicateAndStayBounded()
    {
        var execution = new CMUUtilityScriptExecution();

        execution.EnqueueEvent(CMUUtilityTrigger.OnDamaged, null, "same");
        execution.EnqueueEvent(CMUUtilityTrigger.OnDamaged, null, "same");
        Assert.That(execution.Events, Has.Count.EqualTo(1));

        for (var i = 0; i < CMUUtilityDroneLimits.MaxQueuedEvents + 5; i++)
            execution.EnqueueEvent(CMUUtilityTrigger.OnHear, null, $"line-{i}");

        Assert.That(execution.Events, Has.Count.EqualTo(CMUUtilityDroneLimits.MaxQueuedEvents));

        for (var i = 0; i < CMUUtilityDroneLimits.MaxQueuedEventSpeech + 5; i++)
            execution.EnqueueEventSpeech($"speech-{i}");

        Assert.That(execution.EventSpeech,
            Has.Count.EqualTo(CMUUtilityDroneLimits.MaxQueuedEventSpeech));
    }

    [Test]
    public void HandlerFrameSharesVariablesAndMarkers()
    {
        var marker = new EntityUid(42);
        var execution = new CMUUtilityScriptExecution(
            new CMUUtilityScriptProgram(),
            markers: new List<EntityUid> { marker });
        var handler = new CMUUtilityScriptFrame(execution, new CMUUtilityScriptProgram())
        {
            EventContext = true,
            EventText = "код",
        };

        handler.Variables["message"] = CMUUtilityScriptValue.FromString("alarm");

        Assert.That(execution.Main.Variables["message"].AsText, Is.EqualTo("alarm"));
        Assert.That(handler.Markers, Has.Count.EqualTo(1));
        Assert.That(handler.Markers[0], Is.EqualTo(marker));
        Assert.That(handler.EventContext, Is.True);
        Assert.That(handler.EventText, Is.EqualTo("код"));
    }

    [Test]
    public void ActionStateCarriesContext()
    {
        ICMUUtilityScriptAction action = new CMUUtilityScriptActionState(
            CMUUtilityScriptActionKind.MoveCoordinates)
        {
            Coordinates = new EntityCoordinates(new EntityUid(1), new System.Numerics.Vector2(2, 3)),
        };

        Assert.That(action.Kind, Is.EqualTo(CMUUtilityScriptActionKind.MoveCoordinates));
        Assert.That(action, Is.TypeOf<CMUUtilityScriptActionState>());
    }

    [Test]
    public void NativeCatalogCoversEventAndWorldHelpers()
    {
        var names = CMUUtilityScriptAnalyzer.FunctionArity;
        Assert.That(names.Keys, Does.Contain("heardspeaker"));
        Assert.That(names.Keys, Does.Contain("backpackfits"));
        Assert.That(names.Keys, Does.Contain("itemname"));
        Assert.That(names.Keys, Does.Contain("itemid"));
        Assert.That(names.Keys, Does.Contain("itemdisplay"));
        Assert.That(names.Keys, Does.Contain("health"));
        Assert.That(names.Keys, Does.Contain("maxhealth"));
        Assert.That(names.Keys, Does.Contain("healthpercent"));
        Assert.That(names.Keys, Does.Contain("alive"));
        Assert.That(names.Keys, Does.Contain("dead"));
        Assert.That(names.Keys, Does.Contain("critical"));
        Assert.That(names.Keys, Does.Contain("nearesthostile"));
        Assert.That(names.Keys, Does.Contain("hostiles"));
        Assert.That(names.Keys, Does.Contain("hostilecount"));
        Assert.That(names.Keys, Does.Contain("nearestally"));
        Assert.That(names.Keys, Does.Contain("ammo"));
        Assert.That(names.Keys, Does.Contain("maxammo"));
        Assert.That(names.Keys, Does.Contain("incombat"));
        Assert.That(names["health"], Is.EqualTo((0, 1)));
        Assert.That(names["incombat"], Is.EqualTo((0, 0)));
        Assert.That(names["worncontains"], Is.EqualTo((2, 2)));
        Assert.That(names["activehand"], Is.EqualTo((0, 0)));
        Assert.That(names["itemdisplay"], Is.EqualTo((1, 2)));
        Assert.That(names.Values.All(arity => arity.Min <= arity.Max), Is.True);
    }

    [Test]
    public void RussianItemNamesMatchByWholeWord()
    {
        Assert.That(CMUUtilityScriptNameMatcher.Matches("лом", "лом"), Is.True);
        Assert.That(CMUUtilityScriptNameMatcher.Matches("аварийный лом", "лом"), Is.True);
        Assert.That(CMUUtilityScriptNameMatcher.Matches("ЛОМ", "лом"), Is.True);
        Assert.That(CMUUtilityScriptNameMatcher.Matches("ломик", "лом"), Is.False);

        Assert.That(CMUUtilityScriptNameMatcher.Matches("базовая зажигалка", "зажигалка"), Is.True);
        Assert.That(CMUUtilityScriptNameMatcher.Matches("ЗАЖИГАЛКА", "зажигалка"), Is.True);
        Assert.That(CMUUtilityScriptNameMatcher.Matches("зажигалочка", "зажигалка"), Is.False);

        Assert.That(CMUUtilityScriptNameMatcher.Matches("лист бумаги", "бумага"), Is.False);
        Assert.That(CMUUtilityScriptNameMatcher.Matches("бумага (3)", "бумага"), Is.True);
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
