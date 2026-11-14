using System.Globalization;
using System.Numerics;
using System.Text;
using Content.Client.CMU14.Robotics.UtilityDrones;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client.CMU14.Robotics.UtilityDrones;

public sealed class CMUUtilityDroneCommandWindow : DefaultWindow
{
    public event Action<string>? CommandReady;

    private enum CommandKind
    {
        MoveMarker,
        MoveItem,
        MoveHand,
        MoveCoords,
        Follow,
        Patrol,
        Return,
        Wait,
        Say,
        SayHands,
        SayWorn,
        SayBackpack,
        SayPeople,
        SayItems,
        SayEntities,
        SayDamage,
        SayPulling,
        SayAim,
        SwitchHand,
        TakeItem,
        Drop,
        TakeBackpack,
        PutBackpack,
        PullItem,
        Release,
        Let,
        Set,
        If,
        While,
        Repeat,
        Stop,
        MainBlock,
        OnDamaged,
        OnHeard,
        GotoLabel,
        UseSelf,
        UseOnTarget,
        UseItemOnTarget,
    }

    private static readonly string[] CategoryOrder =
    [
        "Program",
        "Movement",
        "Speech",
        "Hands",
        "Backpack",
        "Pull",
        "Information",
        "Flow",
    ];

    private static readonly Dictionary<string, CommandKind[]> Categories = new()
    {
        ["Program"] =
        [
            CommandKind.MainBlock,
            CommandKind.OnDamaged,
            CommandKind.OnHeard,
            CommandKind.GotoLabel,
            CommandKind.UseSelf,
            CommandKind.UseOnTarget,
            CommandKind.UseItemOnTarget,
        ],
        ["Movement"] =
        [
            CommandKind.MoveMarker,
            CommandKind.MoveItem,
            CommandKind.MoveHand,
            CommandKind.MoveCoords,
            CommandKind.Follow,
            CommandKind.Patrol,
            CommandKind.Return,
            CommandKind.Wait,
        ],
        ["Speech"] =
        [
            CommandKind.Say,
        ],
        ["Hands"] =
        [
            CommandKind.SwitchHand,
            CommandKind.TakeItem,
            CommandKind.Drop,
        ],
        ["Backpack"] =
        [
            CommandKind.TakeBackpack,
            CommandKind.PutBackpack,
        ],
        ["Pull"] =
        [
            CommandKind.PullItem,
            CommandKind.Release,
        ],
        ["Information"] =
        [
            CommandKind.SayHands,
            CommandKind.SayWorn,
            CommandKind.SayBackpack,
            CommandKind.SayPeople,
            CommandKind.SayItems,
            CommandKind.SayEntities,
            CommandKind.SayDamage,
            CommandKind.SayPulling,
            CommandKind.SayAim,
        ],
        ["Flow"] =
        [
            CommandKind.Let,
            CommandKind.Set,
            CommandKind.If,
            CommandKind.While,
            CommandKind.Repeat,
            CommandKind.Stop,
        ],
    };

    private readonly OptionButton _category = new();
    private readonly OptionButton _command = new();
    private readonly LineEdit _marker = new() { Text = "1" };
    private readonly LineEdit _name = new() { Text = "crowbar" };
    private readonly LineEdit _radius = new() { Text = "8" };
    private readonly LineEdit _seconds = new() { Text = "5" };
    private readonly LineEdit _text = new();
    private readonly OptionButton _hand = new();
    private readonly LineEdit _variable = new() { Text = "x" };
    private readonly LineEdit _value = new() { Text = "0" };
    private readonly LineEdit _condition = new() { Text = "1 == 1" };
    private readonly LineEdit _label = new() { Text = "alarm" };
    private readonly LineEdit _x = new() { Text = "0" };
    private readonly LineEdit _y = new() { Text = "0" };
    private readonly TextEdit _raw = new() { MinHeight = 100 };
    private readonly BoxContainer _markerRow;
    private readonly BoxContainer _nameRow;
    private readonly BoxContainer _radiusRow;
    private readonly BoxContainer _secondsRow;
    private readonly BoxContainer _textRow;
    private readonly BoxContainer _handRow;
    private readonly BoxContainer _variableRow;
    private readonly BoxContainer _valueRow;
    private readonly BoxContainer _conditionRow;
    private readonly BoxContainer _labelRow;
    private readonly BoxContainer _xRow;
    private readonly BoxContainer _yRow;
    private bool _updating;
    private bool _rawModified;

    public CMUUtilityDroneCommandWindow()
    {
        Title = Loc.GetString("cmu-utility-drone-command-window-title");
        MinSize = new Vector2(500, 520);
        SetSize = new Vector2(580, 620);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 6,
        };
        Contents.AddChild(root);

        _category.AddItem(Loc.GetString("cmu-utility-drone-command-category-program"), 0);
        _category.AddItem(Loc.GetString("cmu-utility-drone-command-category-movement"), 1);
        _category.AddItem(Loc.GetString("cmu-utility-drone-command-category-speech"), 2);
        _category.AddItem(Loc.GetString("cmu-utility-drone-command-category-hands"), 3);
        _category.AddItem(Loc.GetString("cmu-utility-drone-command-category-backpack"), 4);
        _category.AddItem(Loc.GetString("cmu-utility-drone-command-category-pull"), 5);
        _category.AddItem(Loc.GetString("cmu-utility-drone-command-category-information"), 6);
        _category.AddItem(Loc.GetString("cmu-utility-drone-command-category-flow"), 7);
        _category.OnItemSelected += args =>
        {
            _category.SelectId(args.Id);
            if (!_updating)
                PopulateCommands();
        };
        root.AddChild(FormRow(Loc.GetString("cmu-utility-drone-command-category"), _category));

        _command.HorizontalExpand = true;
        _command.OnItemSelected += args =>
        {
            _command.SelectId(args.Id);
            if (_updating)
                return;

            _rawModified = false;
            UpdateFieldVisibility();
            RefreshRaw();
        };
        root.AddChild(FormRow(Loc.GetString("cmu-utility-drone-command-name"), _command));

        _markerRow = FormRow(Loc.GetString("cmu-utility-drone-marker"), _marker);
        _nameRow = FormRow(Loc.GetString("cmu-utility-drone-target"), _name);
        _radiusRow = FormRow(Loc.GetString("cmu-utility-drone-radius"), _radius);
        _secondsRow = FormRow(Loc.GetString("cmu-utility-drone-seconds"), _seconds);
        _textRow = FormRow(Loc.GetString("cmu-utility-drone-message"), _text);
        _handRow = FormRow(Loc.GetString("cmu-utility-drone-hand"), _hand);
        _variableRow = FormRow(Loc.GetString("cmu-utility-drone-variable"), _variable);
        _valueRow = FormRow(Loc.GetString("cmu-utility-drone-value"), _value);
        _conditionRow = FormRow(Loc.GetString("cmu-utility-drone-condition"), _condition);
        _labelRow = FormRow(Loc.GetString("cmu-utility-drone-label"), _label);
        _xRow = FormRow(Loc.GetString("cmu-utility-drone-x"), _x);
        _yRow = FormRow(Loc.GetString("cmu-utility-drone-y"), _y);

        _hand.AddItem(Loc.GetString("cmu-utility-drone-hand-left"), 0);
        _hand.AddItem(Loc.GetString("cmu-utility-drone-hand-right"), 1);
        _hand.SelectId(0);
        _hand.OnItemSelected += args => _hand.SelectId(args.Id);

        root.AddChild(_markerRow);
        root.AddChild(_nameRow);
        root.AddChild(_radiusRow);
        root.AddChild(_secondsRow);
        root.AddChild(_textRow);
        root.AddChild(_handRow);
        root.AddChild(_variableRow);
        root.AddChild(_valueRow);
        root.AddChild(_conditionRow);
        root.AddChild(_labelRow);
        root.AddChild(_xRow);
        root.AddChild(_yRow);

        root.AddChild(new Label { Text = Loc.GetString("cmu-utility-drone-command-raw") });
        _raw.VerticalExpand = true;
        _raw.OnTextChanged += _ =>
        {
            if (!_updating)
                _rawModified = true;
        };
        root.AddChild(_raw);

        _marker.OnTextChanged += _ => RegenerateIfNotRaw();
        _name.OnTextChanged += _ => RegenerateIfNotRaw();
        _radius.OnTextChanged += _ => RegenerateIfNotRaw();
        _seconds.OnTextChanged += _ => RegenerateIfNotRaw();
        _text.OnTextChanged += _ => RegenerateIfNotRaw();
        _variable.OnTextChanged += _ => RegenerateIfNotRaw();
        _value.OnTextChanged += _ => RegenerateIfNotRaw();
        _condition.OnTextChanged += _ => RegenerateIfNotRaw();
        _label.OnTextChanged += _ => RegenerateIfNotRaw();
        _x.OnTextChanged += _ => RegenerateIfNotRaw();
        _y.OnTextChanged += _ => RegenerateIfNotRaw();
        _hand.OnItemSelected += _ =>
        {
            if (!_updating)
                RegenerateIfNotRaw();
        };

        var buttons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
        };
        var insert = new Button { Text = Loc.GetString("cmu-utility-drone-command-insert"), HorizontalExpand = true };
        insert.OnPressed += _ => Commit();
        buttons.AddChild(insert);
        var refresh = new Button { Text = Loc.GetString("cmu-utility-drone-command-refresh"), HorizontalExpand = true };
        refresh.OnPressed += _ =>
        {
            _rawModified = false;
            RefreshRaw();
        };
        buttons.AddChild(refresh);
        var cancel = new Button { Text = Loc.GetString("cmu-utility-drone-cancel"), HorizontalExpand = true };
        cancel.OnPressed += _ => Close();
        buttons.AddChild(cancel);
        root.AddChild(buttons);

        PopulateCommands();
    }

    public void SetCommandText(string text)
    {
        _updating = true;
        try
        {
            _raw.TextRope = new Rope.Leaf(text);
            _rawModified = true;
        }
        finally
        {
            _updating = false;
        }
    }

    private void PopulateCommands()
    {
        _updating = true;
        try
        {
            _command.Clear();
            if (_category.SelectedId < 0 || _category.SelectedId >= CategoryOrder.Length)
                _category.SelectId(0);

            var kinds = Categories[CategoryOrder[Math.Max(0, _category.SelectedId)]];
            for (var i = 0; i < kinds.Length; i++)
                _command.AddItem(CommandName(kinds[i]), (int) kinds[i]);

            if (_command.ItemCount > 0)
                _command.SelectId((int) kinds[0]);

            _rawModified = false;
            UpdateFieldVisibility();
            RefreshRaw();
        }
        finally
        {
            _updating = false;
        }
    }

    private void RegenerateIfNotRaw()
    {
        if (_updating || _rawModified)
            return;

        RefreshRaw();
    }

    private void RefreshRaw()
    {
        _updating = true;
        try
        {
            _raw.TextRope = new Rope.Leaf(BuildCommand());
        }
        finally
        {
            _updating = false;
        }

        _rawModified = false;
    }

    private void UpdateFieldVisibility()
    {
        var kind = (CommandKind) _command.SelectedId;
        _markerRow.Visible = kind is CommandKind.MoveMarker or CommandKind.Patrol;
        _nameRow.Visible = kind is
            CommandKind.MoveItem or
            CommandKind.TakeItem or
            CommandKind.TakeBackpack or
            CommandKind.PullItem or
            CommandKind.SayDamage or
            CommandKind.SayPulling or
            CommandKind.SayAim or
            CommandKind.UseOnTarget or
            CommandKind.UseItemOnTarget;
        _radiusRow.Visible = kind is
            CommandKind.MoveItem or
            CommandKind.TakeItem or
            CommandKind.PullItem or
            CommandKind.SayPeople or
            CommandKind.SayItems or
            CommandKind.SayEntities or
            CommandKind.Patrol;
        _secondsRow.Visible = kind is CommandKind.Follow or CommandKind.Wait or CommandKind.Patrol;
        _textRow.Visible = kind is CommandKind.Say or CommandKind.OnHeard;
        _handRow.Visible = kind is
            CommandKind.MoveHand or
            CommandKind.SwitchHand or
            CommandKind.TakeItem or
            CommandKind.TakeBackpack;
        _variableRow.Visible = kind is CommandKind.Let or CommandKind.Set;
        _valueRow.Visible = kind is CommandKind.Let or CommandKind.Set or CommandKind.Repeat;
        _conditionRow.Visible = kind is CommandKind.If or CommandKind.While;
        _labelRow.Visible = kind is CommandKind.GotoLabel;
        _xRow.Visible = kind is CommandKind.MoveCoords;
        _yRow.Visible = kind is CommandKind.MoveCoords;
    }

    private string BuildCommand()
    {
        var kind = (CommandKind) _command.SelectedId;
        var marker = SanitizeNumber(_marker.Text, 1);
        var radius = SanitizeNumber(_radius.Text, 8);
        var seconds = SanitizeNumber(_seconds.Text, 5);
        var hand = _hand.SelectedId == 1 ? "right" : "left";
        var name = EscapeString(_name.Text);
        var text = EscapeString(_text.Text);
        var variable = SanitizeVariable(_variable.Text);
        var value = _value.Text;
        var condition = _condition.Text;
        var hasTarget = !string.IsNullOrWhiteSpace(_name.Text);
        var targetExpression = hasTarget ? $"person(\"{name}\")" : "nearestperson(8)";
        var label = SanitizeVariable(_label.Text);
        var x = SanitizeSignedNumber(_x.Text);
        var y = SanitizeSignedNumber(_y.Text);

        return kind switch
        {
            CommandKind.MainBlock => "main {\n\n}",
            CommandKind.OnDamaged => "on damaged {\n\n}",
            CommandKind.OnHeard => $"on heard \"{text}\" {{\n\n}}",
            CommandKind.GotoLabel => $"goto {label}",
            CommandKind.UseSelf => "use self",
            CommandKind.UseOnTarget => $"use on {targetExpression}",
            CommandKind.UseItemOnTarget => $"use activehand() on {targetExpression}",
            CommandKind.MoveMarker => $"move marker {marker}",
            CommandKind.MoveItem => $"move item \"{name}\" within {radius}",
            CommandKind.MoveHand => $"move hand {hand}",
            CommandKind.MoveCoords => $"move coords {x} {y}",
            CommandKind.Follow => $"follow {seconds}",
            CommandKind.Patrol => $"patrol marker {marker} radius {radius} seconds {seconds}",
            CommandKind.Return => "return",
            CommandKind.Wait => $"wait {seconds}",
            CommandKind.Say => $"say \"{text}\"",
            CommandKind.SayHands => "say \"Hands: \" + hands()",
            CommandKind.SayWorn => "say \"Worn: \" + worn()",
            CommandKind.SayBackpack => "say \"Backpack: \" + backpacknames()",
            CommandKind.SayPeople => $"say \"People nearby: \" + people({radius})",
            CommandKind.SayItems => $"say \"Items nearby: \" + items({radius})",
            CommandKind.SayEntities => $"say \"Around: \" + entities({radius})",
            CommandKind.SayDamage => hasTarget
                ? $"say \"Damage: \" + damage({targetExpression})"
                : "say \"Damage: \" + damage()",
            CommandKind.SayPulling => hasTarget
                ? $"say \"Pulling: \" + name(pulling({targetExpression}))"
                : "say \"Pulling: \" + name(pulling())",
            CommandKind.SayAim => hasTarget
                ? $"say \"Aim: \" + name(aimtarget({targetExpression}))"
                : "say \"Aim: \" + name(aimtarget())",
            CommandKind.SwitchHand => $"switch {hand}",
            CommandKind.TakeItem => $"take item \"{name}\" within {radius} into {hand}",
            CommandKind.Drop => "drop",
            CommandKind.TakeBackpack => $"take from backpack \"{name}\" into {hand}",
            CommandKind.PutBackpack => "put in backpack activehand()",
            CommandKind.PullItem => $"pull item(\"{name}\", {radius})",
            CommandKind.Release => "release",
            CommandKind.Let => $"let {variable} = {value}",
            CommandKind.Set => $"set {variable} = {value}",
            CommandKind.If => $"if {condition} {{\n\n}}",
            CommandKind.While => $"while {condition} {{\n\n}}",
            CommandKind.Repeat => $"repeat {value} {{\n\n}}",
            CommandKind.Stop => "stop",
            _ => string.Empty,
        };
    }

    private void Commit()
    {
        if (_rawModified)
            RefreshRaw();

        var command = Rope.Collapse(_raw.TextRope).Trim();
        if (command.Length == 0)
            return;

        CommandReady?.Invoke(command);
        Close();
    }

    private static BoxContainer FormRow(string label, Control control)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
        };
        row.AddChild(new Label { Text = label, MinWidth = 130 });
        control.HorizontalExpand = true;
        row.AddChild(control);
        return row;
    }

    private static string CommandName(CommandKind kind)
    {
        var raw = kind.ToString();
        var key = new StringBuilder(raw.Length + 8);
        for (var i = 0; i < raw.Length; i++)
        {
            var character = raw[i];
            if (i > 0 && char.IsUpper(character))
                key.Append('-');

            key.Append(char.ToLowerInvariant(character));
        }

        return Loc.GetString($"cmu-utility-drone-command-kind-{key}");
    }

    private static string SanitizeNumber(string value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number.ToString(CultureInfo.InvariantCulture)
            : fallback.ToString(CultureInfo.InvariantCulture);
    }

    private static string SanitizeSignedNumber(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : "0";
    }

    private static string SanitizeVariable(string value)
    {
        var result = value.Trim();
        if (result.Length == 0)
            return "x";

        var builder = new System.Text.StringBuilder(result.Length);
        for (var i = 0; i < result.Length; i++)
        {
            var character = result[i];
            if (char.IsLetterOrDigit(character) || character == '_')
                builder.Append(character);
        }

        if (builder.Length == 0)
            return "x";

        if (char.IsDigit(builder[0]))
            builder.Insert(0, '_');

        return builder.ToString();
    }

    private static string EscapeString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n");
    }
}
