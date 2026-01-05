using PartyAIControls.ViewModels.Components;
using PartyAIControls.ViewModels.Dialogs;
using PartyAIControls.ViewModels.Dropdowns;
using System;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace PartyAIControls.ViewModels.MenuOptionVMs
{
  public class PartyAIPartyOptionsVM : ViewModel
  {
    private readonly Action _onClosePartyOptions;
    private readonly PartyAIClanPartySettings _settings;
    private int _buyHorsesBudget;
    private readonly PAICustomOrder _currentFallbackOrder;
    private float _autoRecruitmentPercentage;
    private float _dismissUnwantedTroopsPercentage;
    private float _patrolRadius;

    public PartyAIPartyOptionsVM(PartyAIClanPartySettings settings, Action callback)
    {
      if (settings == null) { return; }
      _settings = settings;

      if (_settings.Hero != null)
      {
        TitleText = new TextObject("{=PAIANrHs1Zb}Edit Party Options for {HERO}'s party").SetTextVariable("HERO", _settings.Hero.Name.ToString()).ToString();
      }
      else
      {
        TitleText = new TextObject("{=PAIgiGgAlAm}Edit Party Options").ToString();
      }

      AllowJoinArmiesToggle = new PartyAIOptionToggleVM(new TextObject("{=PAClaZBMEprx}May Join Armies"), _settings.AllowAllowJoinArmies, new TextObject("{=PAD5Oih6uaW}Allow this party to join kingdom armies. Even with this setting disabled the party will be allowed to join your army."));
      AllowDonateTroopsToggle = new PartyAIOptionToggleVM(new TextObject("{=PAhInSCxPlc}May Donate Troops To Garrisons"), _settings.AllowDonateTroops, new TextObject("{=PAIYcYomRnV}Allow this party to donate troops to friendly garrisons."));
      AllowTakeTroopsFromSettlementToggle = new PartyAIOptionToggleVM(new TextObject("{=PAhQoukaUbN}May Take Troops From Settlements"), _settings.AllowTakeTroopsFromSettlement, new TextObject("{=PAIRCSZxGNl}Allow this party to take troops from your garrisons."));
      AllowSiegingToggle = new PartyAIOptionToggleVM(new TextObject("{=PAIJB8EuhJN}May Besiege"), _settings.AllowSieging, new TextObject("{=PAIHANhrppA}Allow this party to besiege towns and castles. If disabled, parties will leave armies that are sieging, refunding the influence to the army leader."));
      AllowRaidVillagesToggle = new PartyAIOptionToggleVM(new TextObject("{=PArB6kGmInk}May Raid Villages"), _settings.AllowRaidVillages, new TextObject("{=PAIG8Ela5BJ}Allow this party to raid hostile villages. If disabled, parties will leave armies that are raiding, refunding the influence to the army leader."));
      AllowLordPrisonersToggle = new PartyAIOptionToggleVM(new TextObject("{=PAIv3zQDvLn}May Take Lords Prisoner"), _settings.AllowLordPrisoners, new TextObject("{=PAIgE8T3Qxh}Allow this party to take enemy lords prisoner after battle. If disabled, parties will release captured lords."));
      AllowRecruitmentToggle = new PartyAIOptionToggleVM(new TextObject("{=PAIBMzCXm1l}May Recruit Troops"), _settings.AllowRecruitment, new TextObject("{=PAIhSoz6d1X}Allow this party to recruit troops. If you disable this setting, you will have to supply the party with troops manually. If you do not, you can expect the AI to behave stupidly."));
      RecruitFromEnemySettlementsToggle = new PartyAIOptionToggleVM(new TextObject("{=PAIY2oX1c1Y}Recruit From Enemy Settlements"), _settings.RecruitFromEnemySettlements, new TextObject("{=PAIu0g5Z5Yk}Allow this party to recruit troops from enemy settlements. This can be useful for parties that are operating behind enemy lines, but is also risky."));
      BuyHorsesToggle = new PartyAIOptionToggleVM(new TextObject("{=PAIWXzJxqgi}Buy Horses"), _settings.BuyHorses, new TextObject("{=PAIK3xNilPb}Buy enough horses to mount your troops on foot in order to increase movement speed. If you set the budget to zero, this will still prevent unncessary selling of horses. Please note that some horse types do not count towards the speed bonus, like Sumpter Horses in native. This feature won't treat a horse as providing a speed bonus unless the game treats it that way in native or whatever overhaul you play."));
      RecruitmentToggle = new PartyAIOptionToggleVM(new TextObject("{=PAIpTraw5lq}Recruit when below "), _settings.AutoRecruitment, new TextObject("{=PAIMah8wd6z}Automatically set an order to go recruiting when party is below X% of it's max party size. The order will only be added if there is not an existing order to recruit troops in the queue and you are not directly commanding the party."));
      DismissUnwantedTroopsToggle = new PartyAIOptionToggleVM(new TextObject("{=PAIQVkiTiSf}Dismiss unwanted troops when above "), _settings.DismissUnwantedTroops, new TextObject("{=PAIrFBBz1kW}Dismiss troops that either don't fit your party template or don't fit your chosen party composition percentages. This will only happen when aboved the specified party size percentage so that the party won't leave itself vulnerable."));
      _buyHorsesBudget = _settings.BuyHorsesBudget;
      MaxTroopTierDropdown = new(settings.MaxTroopTier, null);
      _currentFallbackOrder = _settings.FallbackOrder;
      _autoRecruitmentPercentage = _settings.AutoRecruitmentPercentage;
      _dismissUnwantedTroopsPercentage = _settings.DismissUnwantedTroopsPercentage;
      _patrolRadius = _settings.PatrolRadius;
      UpdatePatrolRadiusText();

      // disable dismiss troops setting if troop conversion is enabled
      if (SubModule.PartySettingsManager.AllowTroopConversion)
      {
        DismissUnwantedTroopsToggle.IsSelected = false;
        DismissUnwantedTroopsToggle.IsDisabled = true;
        DismissUnwantedTroopsToggle.Hint = new(new("{=PAIh3IMyAgt}It doesn't make sense to dismiss troops when troop conversion is enabled--they can be converted into whatever troop you want."));
      }

      _onClosePartyOptions = callback;

      RefreshValues();
    }

    [DataSourceProperty] public string AcceptText => new TextObject("{=bV75iwKa}Save").ToString();
    [DataSourceProperty] public string CancelText => GameTexts.FindText("str_cancel").ToString();
    [DataSourceProperty] public string TitleText { get; private set; }
    [DataSourceProperty] public PartyAIOptionToggleVM AllowJoinArmiesToggle { get; private set; }
    [DataSourceProperty] public PartyAIOptionToggleVM AllowDonateTroopsToggle { get; private set; }
    [DataSourceProperty] public PartyAIOptionToggleVM AllowTakeTroopsFromSettlementToggle { get; private set; }
    [DataSourceProperty] public PartyAIOptionToggleVM AllowSiegingToggle { get; private set; }
    [DataSourceProperty] public PartyAIOptionToggleVM AllowRaidVillagesToggle { get; private set; }
    [DataSourceProperty] public PartyAIOptionToggleVM AllowLordPrisonersToggle { get; private set; }
    [DataSourceProperty] public PartyAIOptionToggleVM AllowRecruitmentToggle { get; private set; }
    [DataSourceProperty] public PartyAIOptionToggleVM RecruitFromEnemySettlementsToggle { get; private set; }
    [DataSourceProperty] public PartyAIOptionToggleVM BuyHorsesToggle { get; private set; }
    [DataSourceProperty] public PartyAIOptionToggleVM RecruitmentToggle { get; private set; }
    [DataSourceProperty] public PartyAIOptionToggleVM DismissUnwantedTroopsToggle { get; private set; }
    [DataSourceProperty] public string BuyHorsesBudgetText => new TextObject("{=PAIFq36oGcZ} - Daily Budget: ").ToString();
    [DataSourceProperty] public string BuyHorsesBudgetAmount => _buyHorsesBudget.ToString();
    [DataSourceProperty] public string RecruitmentPercentageText => ((int)(_autoRecruitmentPercentage * 100f)).ToString() + "%";
    [DataSourceProperty] public string DismissUnwantedTroopsPercentageText => ((int)(_dismissUnwantedTroopsPercentage * 100f)).ToString() + "%";
    [DataSourceProperty] public string PatrolRadiusText { get; private set; }
    [DataSourceProperty] public string RecruitmentPostText { get; private set; } = new TextObject("{=PAITKllkp3Y} of max party size").ToString();
    [DataSourceProperty] public HintViewModel BuyHorsesBudgetHint => new(new TextObject("{=PAIcyDMxg8t}Horse purchase budget per day."));
    [DataSourceProperty] public PartyAIMaxTroopTierDropdownVM MaxTroopTierDropdown { get; private set; }
    [DataSourceProperty] public string MaxTroopTierText => new TextObject("{=PAIn4UJJg3a}Max Troop Tier").ToString();
    [DataSourceProperty] public HintViewModel MaxTroopTierHint => new(new TextObject("{=PAIKeTFa2PX}Maximum troop tier to upgrade troops to. If you lower this setting while there are higher tier troops in the party, they will be downgraded."));
    [DataSourceProperty] public HintViewModel PatrolRadiusHint => new(new TextObject("{=PAIMaf6ECHe}Change radius for patrol orders. You'll just have to play with it and find a value that gives you the results you want. Percentage is a percentage of the default radius."));
    [DataSourceProperty] public string FallbackOrderLabel => new TextObject("{=PAIqGqAFj9G}Fallback Order: ").ToString();
    [DataSourceProperty] public HintViewModel FallbackOrderHint => new(new TextObject("{=PAIDJ1aQnLC}Order to issue when the party is not in an army and has no current order."));
    [DataSourceProperty] public string FallbackOrder => (_settings?.FallbackOrder?.Text ?? new TextObject("{=PAIZZ1tGdbA}No Active Order")).ToString();

    private void ChangeFallbackOrder() => CreateOrder.Create(_settings, () => OnPropertyChanged("FallbackOrder"), true);

    public void EditRecruitmentPercentage()
    {
      string titleText = new TextObject("{=PAIn8Kp9KPp}Enter Automatic Recruitment Percentage").ToString();
      string detailText = new TextObject("{=PAIdpT9SuVN}Party will go on a recruitment run when under this percentage of its maximum troops.").ToString();
      SubModule.InformationManager.ShowNumberPickerInquiry((int)(_autoRecruitmentPercentage * 100f), 1, 99, titleText, detailText, (int value) =>
      {
        _autoRecruitmentPercentage = value / 100f;
        OnPropertyChanged("RecruitmentPercentageText");
      });
    }

    private void EditDismissUnwantedTroopsPercentage()
    {
      string titleText = new TextObject("{=PAIi6it5mnL}Enter Dismiss Unwanted Troops Percentage").ToString();
      string detailText = new TextObject("{=PAItU2CGhot}Party will start dismissing troops that don't fit its party template once over this percentage of its max troops").ToString();
      SubModule.InformationManager.ShowNumberPickerInquiry((int)(_dismissUnwantedTroopsPercentage * 100f), 1, 99, titleText, detailText, (int value) =>
      {
        _dismissUnwantedTroopsPercentage = value / 100f;
        OnPropertyChanged("DismissUnwantedTroopsPercentageText");
      });
    }

    public void EditBuyHorsesBudget()
    {
      string titleText = new TextObject("{=PAID8JkoxK0}Buy Horses Budget").ToString();
      SubModule.InformationManager.ShowNumberPickerInquiry((int)(_dismissUnwantedTroopsPercentage * 100f), 0, 50000, titleText, string.Empty, (int value) =>
      {
        _buyHorsesBudget = value;
        OnPropertyChanged("BuyHorsesBudgetAmount");
      }, isPercentage: false);
    }

    public void EditPatrolRadius()
    {
      string titleText = new TextObject("{=PAIGHyxwrgx}Patrol Radius").ToString();
      SubModule.InformationManager.ShowNumberPickerInquiry((int)(_patrolRadius * 100f), 10, 200, titleText, string.Empty, (int result) =>
      {
        _patrolRadius = result / 100f;
        UpdatePatrolRadiusText();
      }, isPercentage: true);
    }

    private void UpdatePatrolRadiusText()
    {
      PatrolRadiusText = new TextObject("{=PAIhtLyrJ96}Patrol Radius: {PERCENTAGE}%").SetTextVariable("PERCENTAGE", (int)(_patrolRadius * 100f)).ToString();
      OnPropertyChanged("PatrolRadiusText");
    }

    public void AcceptEditPartyOptions()
    {
      _settings.AllowAllowJoinArmies = AllowJoinArmiesToggle.IsSelected;
      _settings.AllowDonateTroops = AllowDonateTroopsToggle.IsSelected;
      _settings.AllowTakeTroopsFromSettlement = AllowTakeTroopsFromSettlementToggle.IsSelected;
      _settings.AllowSieging = AllowSiegingToggle.IsSelected;
      _settings.AllowRaidVillages = AllowRaidVillagesToggle.IsSelected;
      _settings.AllowLordPrisoners = AllowLordPrisonersToggle.IsSelected;
      _settings.AllowRecruitment = AllowRecruitmentToggle.IsSelected;
      _settings.RecruitFromEnemySettlements = RecruitFromEnemySettlementsToggle.IsSelected;
      _settings.BuyHorses = BuyHorsesToggle.IsSelected;
      _settings.BuyHorsesBudget = _buyHorsesBudget;
      _settings.AutoRecruitmentPercentage = _autoRecruitmentPercentage;
      _settings.AutoRecruitment = RecruitmentToggle.IsSelected;
      _settings.DismissUnwantedTroopsPercentage = _dismissUnwantedTroopsPercentage;
      _settings.DismissUnwantedTroops = DismissUnwantedTroopsToggle.IsSelected;
      _settings.MaxTroopTier = MaxTroopTierDropdown.SortOptions.SelectedItem.Max;
      _settings.PatrolRadius = _patrolRadius;

      if (_currentFallbackOrder != _settings.FallbackOrder && (_settings.Order == _currentFallbackOrder || !_settings.HasActiveOrder))
      {
        if (_settings.Hero?.PartyBelongedTo != null && _settings.Hero.PartyBelongedTo.Army == null)
        {
          _settings.SetOrder(_settings.FallbackOrder);
        }
      }

      _onClosePartyOptions?.Invoke();
    }

    public void CancelEditPartyOptions()
    {
      // revert to original order if we're cancelling
      _settings.FallbackOrder = _currentFallbackOrder;
      _onClosePartyOptions?.Invoke();
    }
  }
}
