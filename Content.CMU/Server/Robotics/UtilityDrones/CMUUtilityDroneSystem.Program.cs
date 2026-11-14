using System.Text;
using Content.Shared.CMU14.Robotics.UtilityDrones;
using Content.Shared.Item;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server.CMU14.Robotics.UtilityDrones;

public sealed partial class CMUUtilityDroneSystem
{
    public bool ValidateProgram(
        EntityUid console,
        CMUUtilityProgramInfo? info,
        out CMUUtilityProgram program,
        out string error)
    {
        program = new CMUUtilityProgram();
        error = string.Empty;

        if (!TryComp<CMUUtilityDroneConsoleComponent>(console, out _))
        {
            error = Loc.GetString("cmu-utility-drone-error-controller");
            return false;
        }

        if (info == null)
        {
            error = Loc.GetString("cmu-utility-drone-error-no-program");
            return false;
        }

        if (info.Steps == null)
        {
            error = Loc.GetString("cmu-utility-drone-error-no-steps");
            return false;
        }

        if (info.Steps.Count is < 1 or > CMUUtilityDroneLimits.MaxSteps)
        {
            error = Loc.GetString("cmu-utility-drone-error-step-count",
                ("min", 1),
                ("max", CMUUtilityDroneLimits.MaxSteps));
            return false;
        }

        if (info.Rules == null)
            info.Rules = new List<CMUUtilityRuleInfo>();

        if (info.Rules.Count > CMUUtilityDroneLimits.MaxRules)
        {
            error = Loc.GetString("cmu-utility-drone-error-rule-count",
                ("max", CMUUtilityDroneLimits.MaxRules));
            return false;
        }

        if (!float.IsFinite(info.RepeatCooldown) ||
            info.RepeatCooldown is < 1f or > 3600f)
        {
            error = Loc.GetString("cmu-utility-drone-error-repeat-cooldown");
            return false;
        }

        var bytes = 0;
        for (var i = 0; i < info.Steps.Count; i++)
        {
            if (info.Steps[i] is not { } step)
            {
                error = Loc.GetString("cmu-utility-drone-error-step-invalid", ("index", i + 1));
                return false;
            }

            if (!Enum.IsDefined(step.Kind))
            {
                error = Loc.GetString("cmu-utility-drone-error-step-kind", ("index", i + 1));
                return false;
            }

            if (!float.IsFinite(step.Radius) ||
                step.Radius is < CMUUtilityDroneLimits.MinRadius or > CMUUtilityDroneLimits.MaxRadius)
            {
                error = Loc.GetString("cmu-utility-drone-error-radius",
                    ("index", i + 1),
                    ("min", CMUUtilityDroneLimits.MinRadius),
                    ("max", CMUUtilityDroneLimits.MaxRadius));
                return false;
            }

            if (!float.IsFinite(step.Seconds) || step.Seconds is < 0f or > 600f)
            {
                error = Loc.GetString("cmu-utility-drone-error-seconds", ("index", i + 1));
                return false;
            }

            var note = step.Note ?? string.Empty;
            if (note.Length > CMUUtilityDroneLimits.MaxStringLength)
            {
                error = Loc.GetString("cmu-utility-drone-error-string",
                    ("index", i + 1),
                    ("max", CMUUtilityDroneLimits.MaxStringLength));
                return false;
            }

            bytes += 32 + Encoding.UTF8.GetByteCount(note);

            EntityUid? marker = null;
            if (RequiresMarker(step.Kind, step.Target != NetEntity.Invalid) &&
                step.Marker == NetEntity.Invalid)
            {
                error = Loc.GetString("cmu-utility-drone-error-marker-required", ("index", i + 1));
                return false;
            }

            if (step.Marker != NetEntity.Invalid)
            {
                if (!TryGetEntity(step.Marker, out var markerUid) ||
                    markerUid is not { } resolved ||
                    !TryValidateMarker(console, resolved, i + 1, out error))
                {
                    if (string.IsNullOrEmpty(error))
                    {
                        error = Loc.GetString("cmu-utility-drone-error-marker",
                            ("index", i + 1),
                            ("marker", step.Marker));
                    }

                    return false;
                }

                marker = resolved;
            }

            EntityUid? target = null;
            if (step.Kind == CMUUtilityStepKind.Pull && step.Target == NetEntity.Invalid)
            {
                error = Loc.GetString("cmu-utility-drone-error-target-required", ("index", i + 1));
                return false;
            }

            if (step.Target != NetEntity.Invalid)
            {
                if (step.Kind is not (CMUUtilityStepKind.Fetch or CMUUtilityStepKind.Pull))
                {
                    error = Loc.GetString("cmu-utility-drone-error-target-kind", ("index", i + 1));
                    return false;
                }

                if (!TryGetEntity(step.Target, out var targetUid) ||
                    targetUid is not { } resolvedTarget ||
                    !TryValidateTarget(console, step.Kind, resolvedTarget, i + 1, out error))
                {
                    return false;
                }

                target = resolvedTarget;
                bytes += 8;
            }

            program.Steps.Add(new CMUUtilityStep
            {
                Kind = step.Kind,
                Marker = marker,
                Target = target,
                Radius = step.Radius,
                Seconds = step.Seconds,
                Note = note,
            });
        }

        for (var i = 0; i < info.Rules.Count; i++)
        {
            if (info.Rules[i] is not { } rule)
            {
                error = Loc.GetString("cmu-utility-drone-error-rule-invalid", ("index", i + 1));
                return false;
            }

            if (!Enum.IsDefined(rule.Trigger) || !Enum.IsDefined(rule.Action))
            {
                error = Loc.GetString("cmu-utility-drone-error-rule-kind", ("index", i + 1));
                return false;
            }

            var message = rule.Message ?? string.Empty;
            if (message.Length > CMUUtilityDroneLimits.MaxStringLength)
            {
                error = Loc.GetString("cmu-utility-drone-error-string",
                    ("index", i + 1),
                    ("max", CMUUtilityDroneLimits.MaxStringLength));
                return false;
            }

            bytes += 16 + Encoding.UTF8.GetByteCount(message);
            program.Rules.Add(new CMUUtilityRule
            {
                Trigger = rule.Trigger,
                Action = rule.Action,
                Message = message,
            });
        }

        if (bytes > CMUUtilityDroneLimits.MaxProgramBytes)
        {
            error = Loc.GetString("cmu-utility-drone-error-program-size",
                ("max", CMUUtilityDroneLimits.MaxProgramBytes));
            return false;
        }

        program.Repeat = info.Repeat;
        program.RepeatCooldown = info.RepeatCooldown;
        return true;
    }

    /// <summary>
    /// Revalidates a self-contained board program at installation and at
    /// runtime. Markers are resolved entity references and only need to remain
    /// physically present and anchored.
    /// </summary>
    public bool ValidateBoardProgram(CMUUtilityProgram program, out string error)
    {
        error = string.Empty;

        if (program.Steps.Count is < 1 or > CMUUtilityDroneLimits.MaxSteps)
        {
            error = Loc.GetString("cmu-utility-drone-error-step-count",
                ("min", 1),
                ("max", CMUUtilityDroneLimits.MaxSteps));
            return false;
        }

        if (program.Rules.Count > CMUUtilityDroneLimits.MaxRules)
        {
            error = Loc.GetString("cmu-utility-drone-error-rule-count",
                ("max", CMUUtilityDroneLimits.MaxRules));
            return false;
        }

        if (!float.IsFinite(program.RepeatCooldown) ||
            program.RepeatCooldown is < 1f or > 3600f)
        {
            error = Loc.GetString("cmu-utility-drone-error-repeat-cooldown");
            return false;
        }

        var bytes = 0;
        for (var i = 0; i < program.Steps.Count; i++)
        {
            if (program.Steps[i] is not { } step)
            {
                error = Loc.GetString("cmu-utility-drone-error-step-invalid", ("index", i + 1));
                return false;
            }

            if (!Enum.IsDefined(step.Kind))
            {
                error = Loc.GetString("cmu-utility-drone-error-step-kind", ("index", i + 1));
                return false;
            }

            if (!float.IsFinite(step.Radius) ||
                step.Radius is < CMUUtilityDroneLimits.MinRadius or > CMUUtilityDroneLimits.MaxRadius)
            {
                error = Loc.GetString("cmu-utility-drone-error-radius",
                    ("index", i + 1),
                    ("min", CMUUtilityDroneLimits.MinRadius),
                    ("max", CMUUtilityDroneLimits.MaxRadius));
                return false;
            }

            if (!float.IsFinite(step.Seconds) || step.Seconds is < 0f or > 600f)
            {
                error = Loc.GetString("cmu-utility-drone-error-seconds", ("index", i + 1));
                return false;
            }

            var note = step.Note ?? string.Empty;
            if (note.Length > CMUUtilityDroneLimits.MaxStringLength)
            {
                error = Loc.GetString("cmu-utility-drone-error-string",
                    ("index", i + 1),
                    ("max", CMUUtilityDroneLimits.MaxStringLength));
                return false;
            }

            bytes += 32 + Encoding.UTF8.GetByteCount(note);

            if (RequiresMarker(step.Kind, step.Target != null) && step.Marker == null)
            {
                error = Loc.GetString("cmu-utility-drone-error-marker-required", ("index", i + 1));
                return false;
            }

            if (step.Marker is { } marker &&
                !TryValidatePhysicalMarker(marker, i + 1, out error))
            {
                return false;
            }

            if (step.Kind == CMUUtilityStepKind.Pull && step.Target == null)
            {
                error = Loc.GetString("cmu-utility-drone-error-target-required", ("index", i + 1));
                return false;
            }

            if (step.Target is { } target)
            {
                if (step.Kind is not (CMUUtilityStepKind.Fetch or CMUUtilityStepKind.Pull) ||
                    !TryValidatePhysicalTarget(step.Kind, target, i + 1, out error))
                {
                    return false;
                }

                bytes += 8;
            }
        }

        for (var i = 0; i < program.Rules.Count; i++)
        {
            if (program.Rules[i] is not { } rule)
            {
                error = Loc.GetString("cmu-utility-drone-error-rule-invalid", ("index", i + 1));
                return false;
            }

            if (!Enum.IsDefined(rule.Trigger) || !Enum.IsDefined(rule.Action))
            {
                error = Loc.GetString("cmu-utility-drone-error-rule-kind", ("index", i + 1));
                return false;
            }

            var message = rule.Message ?? string.Empty;
            if (message.Length > CMUUtilityDroneLimits.MaxStringLength)
            {
                error = Loc.GetString("cmu-utility-drone-error-string",
                    ("index", i + 1),
                    ("max", CMUUtilityDroneLimits.MaxStringLength));
                return false;
            }

            bytes += 16 + Encoding.UTF8.GetByteCount(message);
        }

        if (bytes > CMUUtilityDroneLimits.MaxProgramBytes)
        {
            error = Loc.GetString("cmu-utility-drone-error-program-size",
                ("max", CMUUtilityDroneLimits.MaxProgramBytes));
            return false;
        }

        return true;
    }

    private bool TryValidateMarker(EntityUid console, EntityUid marker, int index, out string error)
    {
        if (!TryValidatePhysicalMarker(marker, index, out error))
            return false;

        if (!TryComp<CMUUtilityTaskMarkerComponent>(marker, out var markerData))
            return false;

        if (markerData.Console != console)
        {
            error = Loc.GetString("cmu-utility-drone-error-marker-unlinked");
            return false;
        }

        if (!SameZNetwork(console, marker))
        {
            error = Loc.GetString("cmu-utility-drone-error-marker-network",
                ("index", index),
                ("marker", GetNetEntity(marker)));
            return false;
        }

        return true;
    }

    private bool TryValidateTarget(
        EntityUid console,
        CMUUtilityStepKind kind,
        EntityUid target,
        int index,
        out string error)
    {
        if (!TryValidatePhysicalTarget(kind, target, index, out error))
            return false;

        if (!SameZNetwork(console, target))
        {
            error = Loc.GetString("cmu-utility-drone-error-target-network", ("index", index));
            return false;
        }

        return true;
    }

    private bool TryValidatePhysicalTarget(
        CMUUtilityStepKind kind,
        EntityUid target,
        int index,
        out string error)
    {
        error = Loc.GetString("cmu-utility-drone-error-target", ("index", index));

        if (TerminatingOrDeleted(target) || HasComp<CMUUtilityDroneAgentComponent>(target))
            return false;

        if (Transform(target).Anchored)
        {
            error = Loc.GetString("cmu-utility-drone-error-target-anchored", ("index", index));
            return false;
        }

        if (kind == CMUUtilityStepKind.Pull)
        {
            if (!TryComp<PullableComponent>(target, out _) ||
                !TryComp<PhysicsComponent>(target, out var physics) ||
                physics.BodyType == BodyType.Static)
            {
                error = Loc.GetString("cmu-utility-drone-error-target-not-pullable", ("index", index));
                return false;
            }
        }
        else if (!HasComp<ItemComponent>(target))
        {
            error = Loc.GetString("cmu-utility-drone-error-target-not-item", ("index", index));
            return false;
        }

        return true;
    }

    private bool TryValidatePhysicalMarker(EntityUid marker, int index, out string error)
    {
        error = Loc.GetString("cmu-utility-drone-error-marker",
            ("index", index),
            ("marker", GetNetEntity(marker)));

        if (TerminatingOrDeleted(marker) ||
            !HasComp<CMUUtilityTaskMarkerComponent>(marker))
        {
            return false;
        }

        if (!Transform(marker).Anchored)
        {
            error = Loc.GetString("cmu-utility-drone-error-marker-unanchored");
            return false;
        }

        return true;
    }

    private static bool RequiresMarker(CMUUtilityStepKind kind, bool hasTarget)
    {
        if (kind is CMUUtilityStepKind.Fetch or CMUUtilityStepKind.Pull)
            return kind == CMUUtilityStepKind.Fetch && !hasTarget;

        return kind is CMUUtilityStepKind.MoveTo
            or CMUUtilityStepKind.Guard
            or CMUUtilityStepKind.Patrol
            or CMUUtilityStepKind.Deliver
            or CMUUtilityStepKind.Repair
            or CMUUtilityStepKind.Report;
    }

    public CMUUtilityProgramInfo ToProgramInfo(CMUUtilityProgram program)
    {
        var steps = new List<CMUUtilityStepInfo>(program.Steps.Count);
        foreach (var step in program.Steps)
        {
            var marker = step.Marker is { } markerUid && !TerminatingOrDeleted(markerUid)
                ? GetNetEntity(markerUid)
                : NetEntity.Invalid;
            var target = step.Target is { } targetUid && !TerminatingOrDeleted(targetUid)
                ? GetNetEntity(targetUid)
                : NetEntity.Invalid;
            steps.Add(new CMUUtilityStepInfo(
                step.Kind,
                marker,
                step.Radius,
                step.Seconds,
                step.Note,
                target));
        }

        var rules = new List<CMUUtilityRuleInfo>(program.Rules.Count);
        foreach (var rule in program.Rules)
            rules.Add(new CMUUtilityRuleInfo(rule.Trigger, rule.Action, rule.Message));

        return new CMUUtilityProgramInfo(steps, rules, program.Repeat, program.RepeatCooldown);
    }
}
