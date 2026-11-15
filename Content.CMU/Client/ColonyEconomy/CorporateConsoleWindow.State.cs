using System;
using System.Linq;
using Content.Client.CMU14.Interface;
using Content.Shared.CMU14.ColonyEconomy;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.CMU14.ColonyEconomy;

public sealed partial class CorporateConsoleWindow
{
    public void UpdateState(CorporateConsoleBuiState state)
    {
        // Operations.
        CrtConsoleKit.SetTag(_budgetTag, _budgetText, CrtConsoleKit.Money(state.CorporateBudget),
            state.CorporateBudget > 0 ? CrtTerminalPalette.Accent : CrtTerminalPalette.Alert);

        _tariff.Text = $"{MathF.Round(state.TransitTariffPercent):0}%";
        if (!_tariffInput.HasKeyboardFocus())
            _tariffInput.Text = $"{MathF.Round(state.TransitTariffPercent):0}";

        var incomeTax = state.EconomyStatus.IncomeTaxPercent;
        _taxNote.Text = incomeTax > 0
            ? Loc.GetString("cmu-corporate-withdraw-tax", ("percent", $"{MathF.Round(incomeTax):0}"))
            : Loc.GetString("cmu-corporate-withdraw-no-tax");

        _operationsKit.EconomyOverview(_economy, state.EconomyStatus);

        // Science.
        _chemistryPoints.Text = state.ChemistryPoints.ToString();
        _roboticsPoints.Text = state.RoboticsPoints.ToString();
        _companyValue.Text = state.CompanyValue.ToString();
        _scienceLevel.Text = state.ScienceLevel.ToString();
        _scienceDirection.Text = DirectionName(state.Direction);

        // Missions.
        _missionChemistry.Text = state.ChemistryPoints.ToString();
        _missionRobotics.Text = state.RoboticsPoints.ToString();
        _missionCompanyValue.Text = state.CompanyValue.ToString();
        var missionSignature = $"{state.MissionStatus}|" +
            string.Join(';', state.ActiveMissions.Select(MissionSignature)) + "|" +
            string.Join(';', state.AvailableMissions.Select(MissionSignature)) + "|" +
            string.Join(';', state.MissionHistory.Select(MissionSignature));
        if (missionSignature != _missionSignature)
        {
            _missionSignature = missionSignature;
            BuildMissions(state);
        }

        // Company support.
        _pmcCompanyValue.Text = state.CompanyValue.ToString();
        _pmcCostValue = state.PmcCostCompanyValue;
        _pmcCost.Text = state.PmcCostCompanyValue.ToString();
        _pmcCallsLeft = state.PmcCallsLeft;
        _pmcCooldownSeconds = state.PmcCooldownSeconds;
        _pmcCalls.Text = state.PmcCallsLeft.ToString();
        if (state.PmcCallsLeft <= 0)
            _pmcCooldown.Text = Loc.GetString("cmu-corporate-pmc-no-calls");
        else if (_pmcCooldownSeconds > 0)
            _pmcCooldown.Text = Loc.GetString("cmu-corporate-pmc-cooldown",
                ("seconds", _pmcCooldownSeconds));
        else
            _pmcCooldown.Text = Loc.GetString("cmu-corporate-pmc-ready");

        _pmcButton.Disabled = _pmcCallsLeft <= 0
                              || state.CompanyValue < _pmcCostValue
                              || _pmcCooldownSeconds > 0;

        _guardCostValue = state.GuardCostCompanyValue;
        _guardCost.Text = state.GuardCostCompanyValue.ToString();
        _guardCallsLeft = state.GuardCallsLeft;
        _guardCalls.Text = state.GuardCallsLeft.ToString();
        _guardCooldownSeconds = state.GuardCooldownSeconds;
        if (_guardCallsLeft <= 0)
            _guardCooldown.Text = Loc.GetString("cmu-corporate-guard-no-calls");
        else if (_guardCooldownSeconds > 0)
            _guardCooldown.Text = Loc.GetString("cmu-corporate-guard-cooldown",
                ("seconds", _guardCooldownSeconds));
        else
            _guardCooldown.Text = Loc.GetString("cmu-corporate-guard-ready");

        _guardButton.Disabled = _guardCallsLeft <= 0
                                || state.CompanyValue < _guardCostValue
                                || _guardCooldownSeconds > 0;

        _creditCostValue = state.CreditCostCompanyValue;
        _creditCost.Text = state.CreditCostCompanyValue.ToString();
        _creditAmount.Text = CrtConsoleKit.Money(state.CreditAmount);
        _creditButton.Disabled = state.CompanyValue < _creditCostValue;

        _alienTechCostValue = state.AlienTechCostCompanyValue;
        _alienTechCostValueLabel.Text = state.AlienTechCostCompanyValue.ToString();
        _alienTechCostValueLabel.FontColorOverride = state.CompanyValue < _alienTechCostValue
            ? CrtTerminalPalette.Alert
            : CrtTerminalPalette.Caution;
        _alienTechButton.Disabled = state.CompanyValue < _alienTechCostValue;

        SetRichText(_supportStatus, state.SupportStatus, CrtTerminalPalette.TextDim);

        // Direction.
        _directionCurrent.Text = DirectionName(state.Direction);
        _roboticsDirection.Disabled = state.Direction == CorporateResearchDirection.Robotics;
        _bioengineeringDirection.Disabled = state.Direction == CorporateResearchDirection.Bioengineering;
    }
}
