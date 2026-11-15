using Content.Server.Administration.Logs;
using Content.Server.Antag;
using Content.Server._RMC14.Language.Systems;
using Content.Shared._RMC14.Language;
using Content.Shared._RMC14.Language.Prototypes;
using Content.Server.GameTicking;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Popups;
using Content.Server.Roles;
using Content.Shared._RMC14.Emote;
using Content.Shared._RMC14.Intel;
using Content.Shared._RMC14.Synth;
using Content.Shared._RMC14.Xenonids;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Events;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.CMU14.Chemistry;
using Content.Shared.CMU14.Chemistry.Effects;
using Content.Shared.CMU14.Round.Antags.Cannibal;
using Content.Shared.CMU14.Threats.Mobs.Wendigo.Lab;
using Content.Shared.Chat.Prototypes;
using Content.Shared.CMU14.Medical.Anatomy.Organs;
using Content.Shared.CMU14.Medical.Anatomy.Organs.Brain;
using Content.Shared.CMU14.Medical.Anatomy.Organs.Events;
using Content.Shared.CMU14.Medical.Core;
using Content.Shared.CMU14.Medical.Diagnostics;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Jittering;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Nutrition.Prototypes;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Tag;
using Content.Shared.Zombies;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.CMU14.Threats.Mobs.Wendigo.Lab;

// Subject examination, scanner output and final-dose readiness.
public sealed partial class CMUWendigoTransformationSystem : EntitySystem
{
    private void OnSubjectExamined(Entity<CMUWendigoSubjectComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Stage == CMUWendigoSubjectStage.Mutating)
        {
            args.PushMarkup(Loc.GetString("cmu-wendigo-lab-examine-mutating"));
            return;
        }

        if (IsReadyForFinalDose(ent.Comp))
            args.PushMarkup(Loc.GetString("cmu-wendigo-lab-examine-ready"));

        if (ent.Comp.Instability >= UnstableThreshold)
            args.PushMarkup(Loc.GetString("cmu-wendigo-lab-examine-unstable-severe"));
        else if (ent.Comp.Instability > 0)
            args.PushMarkup(Loc.GetString("cmu-wendigo-lab-examine-unstable"));
    }

    private void OnScannerReading(Entity<CMUWendigoSubjectComponent> ent, ref CMUHealthScannerReadingEvent args)
    {
        args.State.CMUWendigoReading = BuildScannerReading(ent.Comp);
    }

    /// <summary>
    /// Health analyzer line: stage, overall progress, what the subject is waiting for, and instability.
    /// </summary>
    public string BuildScannerReading(CMUWendigoSubjectComponent subject)
    {
        var now = _timing.CurTime;
        var stageIndex = (int) subject.Stage;
        var stageLength = subject.StageEndsAt - subject.StageStartedAt;
        var stageFraction = stageLength <= TimeSpan.Zero
            ? 1f
            : Math.Clamp((float) ((now - subject.StageStartedAt) / stageLength), 0f, 1f);
        var stageCount = Enum.GetValues<CMUWendigoSubjectStage>().Length;
        var progress = (int) MathF.Round((stageIndex + stageFraction) / stageCount * 100f);

        string status;
        if (subject.Stage == CMUWendigoSubjectStage.Mutating)
            status = Loc.GetString("cmu-wendigo-lab-scanner-mutating");
        else if (now < subject.StageEndsAt)
            status = Loc.GetString(
                "cmu-wendigo-lab-scanner-developing",
                ("time", FormatTime(subject.StageEndsAt - now)));
        else
            status = Loc.GetString("cmu-wendigo-lab-scanner-ready");

        return Loc.GetString("cmu-wendigo-lab-scanner",
            ("stage", Loc.GetString($"cmu-wendigo-lab-stage-{subject.Stage.ToString().ToLowerInvariant()}")),
            ("progress", progress),
            ("status", status),
            ("instability", subject.Instability),
            ("max", MaxInstability));
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;

        return $"{(int) time.TotalMinutes}:{time.Seconds:D2}";
    }

    /// <summary>
    /// Gestation is over and the subject will take MH-32 or MH-33.
    /// </summary>
    public bool IsReadyForFinalDose(CMUWendigoSubjectComponent subject)
    {
        return subject.Stage == CMUWendigoSubjectStage.Gestation && _timing.CurTime >= subject.StageEndsAt;
    }
}
