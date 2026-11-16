using System;
using System.Collections.Generic;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.CMU14.Chemistry.Research;

[Serializable, NetSerializable]
public sealed partial class ResearchDocumentReadDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class ResearchDocumentWriteDoAfterEvent : SimpleDoAfterEvent
{
    [DataField]
    public string Title = string.Empty;

    [DataField]
    public ResearchDocumentType Type = ResearchDocumentType.Report;

    [DataField]
    public List<string> FactIds = [];
}
