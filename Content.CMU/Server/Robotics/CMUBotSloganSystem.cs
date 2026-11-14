using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.CMU14.Robotics;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.CMU14.Robotics;

/// <summary>
/// Makes Weyland-Yutani bots occasionally recite their slogan.
/// </summary>
public sealed partial class CMUBotSloganSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;

    private TimeSpan _nextUpdate;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + TimeSpan.FromSeconds(1);

        var query = EntityQueryEnumerator<CMUBotSloganComponent>();
        while (query.MoveNext(out var uid, out var slogan))
        {
            if (slogan.NextSpeak == TimeSpan.Zero)
            {
                slogan.NextSpeak = now + NextInterval(slogan);
                continue;
            }

            if (now < slogan.NextSpeak)
                continue;

            slogan.NextSpeak = now + NextInterval(slogan);

            if (TryComp<MobStateComponent>(uid, out var mobState)
                && mobState.CurrentState == MobState.Dead)
                continue;

            _chat.TrySendInGameICMessage(
                uid,
                Loc.GetString(slogan.Phrase),
                InGameICChatType.Speak,
                ChatTransmitRange.Normal,
                hideLog: false,
                checkRadioPrefix: false,
                ignoreActionBlocker: true);
        }
    }

    private TimeSpan NextInterval(CMUBotSloganComponent slogan)
    {
        var min = slogan.MinInterval.TotalSeconds;
        var max = slogan.MaxInterval.TotalSeconds;
        return TimeSpan.FromSeconds(_random.NextFloat((float) min, (float) max));
    }
}
