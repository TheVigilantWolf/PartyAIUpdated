//NEW
using Helpers;
using PartyAIControls.HarmonyPatches;
using PartyAIControls.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement.Categories;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using static PartyAIControls.PAICustomOrder;
using static TaleWorlds.CampaignSystem.Party.MobileParty;

namespace PartyAIControls.CampaignBehaviors
{
  public class PAISettlementVisitLog
  {
    [SaveableProperty(1)] public Settlement Settlement { get; private set; }
    [SaveableProperty(2)] public CampaignTime Visited { get; private set; }
    [SaveableProperty(3)] public MobileParty Party { get; private set; }
    public PAISettlementVisitLog(Settlement settlement, CampaignTime visited, MobileParty party)
    {
      Settlement = settlement;
      Visited = visited;
      Party = party;
    }
  }

  internal class PartyAIThinker : CampaignBehaviorBase
  {
    private List<MobileParty> _assumingDirectControl = new();
    private List<PAISettlementVisitLog> _recentlyRecruitedFromSettlements = new();

    private static CampaignVec2 TryGetMapPointPosition(IMapPoint mapPoint)
    {
      if (mapPoint == null)
        return CampaignVec2.Zero;
      return SafeGet(() => mapPoint.Position, CampaignVec2.Zero);
    }

    internal MBReadOnlyList<MobileParty> AssumingDirectControl { get => _assumingDirectControl.ToMBList(); }

    internal void ClearAssumingDirectControl() => _assumingDirectControl.Clear();
    internal void AddToAssumingDirectControl(MobileParty party) => _assumingDirectControl.Add(party);

    public override void RegisterEvents()
    {
      CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, new Action<MobileParty, PartyBase>(OnMobilePartyDestroyed));
      CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, new Action<PartyBase, Hero>(OnHeroPrisonerTaken));
      CampaignEvents.OnPartyJoinedArmyEvent.AddNonSerializedListener(this, new Action<MobileParty>(OnPartyJoinedArmy));
      CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, new Action<Settlement, bool, Hero, Hero, Hero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail>(OnSettlementOwnerChanged));
      CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, new Action<MobileParty>(OnHourlyTickParty));
      CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, new Action(ImplementAutoCreateClanParties));
      CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, new Action(OnDailyTick));
      CampaignEvents.MobilePartyCreated.AddNonSerializedListener(this, new(OnMobilePartyCreated));
      CampaignEvents.SettlementEntered.AddNonSerializedListener(this, new(OnSettlementEntered));
    }

    private void OnDailyTick()
    {
      _recentlyRecruitedFromSettlements.RemoveAll(l => l.Visited.ElapsedDaysUntilNow > 10f);
    }

    private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
    {
      if (party?.LeaderHero == null || settlement == null)
      {
        return;
      }
      if (!SubModule.PartySettingsManager.IsHeroManageable(party.LeaderHero))
      {
        return;
      }
      PartyAIClanPartySettings settings = SubModule.PartySettingsManager.Settings(party.LeaderHero);
      if (settings != null && settings.HasActiveOrder && (settings.Order.Behavior == OrderType.RecruitFromTemplate || settings.Order.Behavior == OrderType.VisitSettlement))
      {
        if (settlement == settings.Order.Target)
        {
          if (settings.Order.Behavior == OrderType.VisitSettlement)
          {
            settings.ClearOrder();
          }
          else
          {
            settings.Order.Target = null;
          }
        }
      }
    }

    private void ImplementAutoCreateClanParties()
    {
      if (!SubModule.PartySettingsManager.AutoCreateClanParties)
      {
        return;
      }

      if (SubModule.PartySettingsManager.AutoCreateClanPartiesMax > 0 && ActiveClanParties(Clan.PlayerClan).Count() >= SubModule.PartySettingsManager.AutoCreateClanPartiesMax)
      {
        return;
      }

      do
      {
        ClanPartiesVM stockVM = new(() => { }, null, () => { }, (i) => { });

        if (!stockVM.CanCreateNewParty)
        {
          return;
        }

        IEnumerable<Hero> eligibleLeaders = Clan.PlayerClan.Heroes.Where((Hero h) => !h.IsDisabled).Union(Clan.PlayerClan.Companions).Where(h =>
          h.IsActive && !h.IsReleased && !h.IsFugitive && !h.IsPrisoner && !h.IsChild && h != Hero.MainHero && h.CanLeadParty() && !h.IsPartyLeader && h.GovernorOf == null && h.PartyBelongedTo == null && (!h.CurrentSettlement?.IsUnderSiege ?? true)
        );

        if (SubModule.PartySettingsManager.AutoCreateClanPartiesRoster.Count > 0)
        {
          eligibleLeaders = eligibleLeaders.Where(h => SubModule.PartySettingsManager.AutoCreateClanPartiesRoster.Contains(h));
        }

        if (eligibleLeaders.Count() == 0)
        {
          return;
        }

        Hero leader = TaleWorlds.Core.Extensions.GetRandomElementInefficiently(eligibleLeaders);
        Settlement settlement = FindNearestSettlement(s => true, leader.GetMapPoint());
        MobileParty newParty = MobilePartyHelper.CreateNewClanMobileParty(leader, Clan.PlayerClan);
        InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=PAIJPxU5978}{HERO} has created a new party near {SETTLEMENT}").SetTextVariable("HERO", leader.Name).SetTextVariable("SETTLEMENT", settlement?.Name).ToString(), Colors.Gray));

        if (SubModule.PartySettingsManager.AutoCreateClanPartiesMax > 0 && ActiveClanParties(Clan.PlayerClan).Count() >= SubModule.PartySettingsManager.AutoCreateClanPartiesMax)
        {
          break;
        }
      } while (Clan.PlayerClan.WarPartyComponents.Count < Clan.PlayerClan.CommanderLimit);
    }

    private void OnHourlyTickParty(MobileParty party)
    {
      if (party?.LeaderHero == null) { return; }
      if (!SubModule.PartySettingsManager.IsHeroManageable(party.LeaderHero))
      {
        if (party.Ai != null && party.Ai.DoNotMakeNewDecisions && party.DefaultBehavior == AiBehavior.Hold && party.IsLordParty)
        {
          party.Ai.SetDoNotMakeNewDecisions(false);
        }
        return;
      }
      else if (SubModule.PartyThinker.AssumingDirectControl.Contains(party) && !SubModule.PartySettingsManager.Settings(party.LeaderHero)?.HasActiveOrder == true)
      {
        if (party.DefaultBehavior == AiBehavior.Hold)
        {
          var directControlSettings = SubModule.PartySettingsManager.Settings(party.LeaderHero);
          if (directControlSettings != null)
            directControlSettings.SetOrder(new(MobileParty.MainParty, OrderType.EscortParty));
        }
      }

      // buy horses while waiting in settlements
      if (party.CurrentSettlement != null)
      {
        PartiesBuyHorseCampaignBehaviorPatch.Prefix(party, party.CurrentSettlement, party.LeaderHero);
      }
      var partySettings = SubModule.PartySettingsManager.Settings(party.LeaderHero);
      if (partySettings == null) return;
      PartyAIClanPartySettings settings = partySettings;
      if (settings.AutoRecruitment && party.PartySizeRatio < settings.AutoRecruitmentPercentage && !SubModule.PartyThinker.AssumingDirectControl.Contains(party) && party.Army == null)
      {
        if (settings.HasActiveOrder)
        {
          if (settings.Order.Behavior != OrderType.RecruitFromTemplate && !settings.OrderQueue.Any(o => o.Behavior == OrderType.RecruitFromTemplate))
          {
            settings.SetOrder(new(null, OrderType.RecruitFromTemplate));
          }
        }
        else
        {
          settings.SetOrder(new(null, OrderType.RecruitFromTemplate));
        }
      }
      if (settings.DismissUnwantedTroops && party.PartySizeRatio > settings.DismissUnwantedTroopsPercentage)
      {
        int max = (int)((party.PartySizeRatio - settings.DismissUnwantedTroopsPercentage) * party.Party.PartySizeLimit);
        if (max > 0)
        {
          SubModule.PartyTroopRecruiter.DismissUnwantedTroops(settings, party, max);
        }
      }
      if (settings.HasActiveOrder)
      {
        // DON'T abandon patrol orders for low food - patrol logic handles it internally
        if (settings.Order.Behavior != OrderType.PatrolAroundPoint &&
            settings.Order.Behavior != OrderType.PatrolClanLands)
        {
          if (party.GetNumDaysForFoodToLast() < 3 && party.GetNumDaysForFoodToLast() > 0)
          {
            AbandonOrderForNoFood(party, settings);
            return;
          }
        }
        if (settings.Order.Behavior == OrderType.DefendSettlement)
        {
          ImplementDefendSettlement(settings, party, out _);
          return;
        }
        if (settings.Order.Behavior == OrderType.StayInSettlement)
        {
          ImplementStayInSettlement(settings, party, out _);
          return;
        }
        if (settings.Order.Behavior == OrderType.VisitSettlement)
        {
          ImplementVisitSettlement(settings, party, out _);
          return;
        }
        if (settings.Order.Behavior == OrderType.AttackParty)
        {
          ImplementAttackParty(settings, party, settings.Order.Target, out _);
          return;
        }
        if (settings.Order.Behavior == OrderType.EscortParty)
        {
          ImplementEscortParty(settings, party, settings.Order.Target, out _);
          return;
        }
        if (settings.Order.Behavior == OrderType.RecruitFromTemplate)
        {
          ImplementRecruitFromTemplate(settings, party);
          return;
        }
      }
      else if (settings.FallbackOrder != null && settings.FallbackOrder.Behavior != OrderType.None && party.Army == null)
      {
        settings.SetOrder(settings.FallbackOrder);
      }
    }

    private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
    {
      foreach (PartyAIClanPartySettings settings in SubModule.PartySettingsManager.HeroesWithOrders)
      {
        if (settings.Order.Behavior == OrderType.BesiegeSettlement && settings.Order.Target == settlement)
        {
          if (!FactionManager.IsAtWarAgainstFaction(settings.Hero.MapFaction, settlement.MapFaction))
          {
            settings.ClearOrder();
          }
        }
      }
    }

    private void OnHeroPrisonerTaken(PartyBase party, Hero prisoner)
    {
      if (SubModule.PartySettingsManager.IsHeroManageable(prisoner))
      {
        PartyAIClanPartySettings settings = SubModule.PartySettingsManager.Settings(prisoner);
        settings.ClearOrder();
        settings.OrderQueue.Clear();
      }
    }

    private void OnPartyJoinedArmy(MobileParty mobileParty)
    {
      if (mobileParty?.LeaderHero == null) return;
      if (SubModule.PartySettingsManager.IsHeroManageable(mobileParty.LeaderHero))
      {
        if (!SubModule.PartySettingsManager.HasActiveOrder(mobileParty.LeaderHero))
        {
          return;
        }
        TextObject text = new TextObject("{=PAIOEWao2aI}{PARTY} is no longer {ORDER} because they were called to {ARMY}")
          .SetTextVariable("PARTY", mobileParty.Name)
          .SetTextVariable("ORDER", SubModule.PartySettingsManager.GetOrderText(mobileParty.LeaderHero))
          .SetTextVariable("ARMY", (mobileParty.Army != null && mobileParty.Army.Name != null) ? mobileParty.Army.Name.ToString() : "an army");
        InformationManager.DisplayMessage(new InformationMessage(text.ToString(), Colors.Magenta));
        PartyAIClanPartySettings settings = SubModule.PartySettingsManager.Settings(mobileParty.LeaderHero);
        if (settings != null)
        {
          settings.ClearOrder();
          settings.OrderQueue.Clear();
        }
      }
    }

    private void OnMobilePartyDestroyed(MobileParty mobileParty, PartyBase destroyerParty)
    {
      if (mobileParty?.LeaderHero != null && SubModule.PartySettingsManager.IsHeroManageable(mobileParty.LeaderHero))
      {
        PartyAIClanPartySettings settings = SubModule.PartySettingsManager.Settings(mobileParty.LeaderHero);
        if (settings != null)
        {
          settings.ClearOrder();
          settings.OrderQueue.Clear();
        }
      }
      foreach (PartyAIClanPartySettings settings in SubModule.PartySettingsManager.HeroesWithOrders)
      {
        PAICustomOrder order = settings.Order;
        switch (order.Behavior)
        {
          case OrderType.AttackParty:
          case OrderType.EscortParty:
            if (order.Target is not MobileParty m || m != mobileParty)
            {
              continue;
            }
            settings.ClearOrder();
            if (SubModule.PartyThinker.AssumingDirectControl.Contains(settings.Hero?.PartyBelongedTo))
            {
              settings.SetOrder(new(MainParty, OrderType.EscortParty));
              MobileParty escortingParty = settings.Hero?.PartyBelongedTo;
              if (escortingParty != null)
              {
                SetPartyAiAction.GetActionForEscortingParty(
                    escortingParty,
                    MainParty,
                    MainParty.DesiredAiNavigationType,
                    false,
                    false);
                escortingParty.Ai.SetDoNotMakeNewDecisions(true);
              }
              else
              {
                settings.ClearOrder();
              }
            }
            break;
          default:
            break;
        }
      }
    }

    private void OnMobilePartyCreated(MobileParty mobileParty)
    {
      if (mobileParty?.LeaderHero == null) return;
      if (SubModule.PartySettingsManager.IsHeroManageable(mobileParty.LeaderHero))
      {
        PartyAIClanPartySettings settings = SubModule.PartySettingsManager.Settings(mobileParty.LeaderHero);
        if (settings != null)
        {
          settings.ClearOrder();
          settings.OrderQueue.Clear();
          settings.ResetBudgets();
          if (settings.FallbackOrder != null && settings.FallbackOrder.Behavior != OrderType.None)
          {
            settings.SetOrder(settings.FallbackOrder);
          }
        }
      }
    }

    internal void ProcessOrder(MobileParty party, PartyThinkParams thinkParams)
    {
      if (party?.LeaderHero == null) return;
      if (!SubModule.PartySettingsManager.IsHeroManageable(party.LeaderHero))
      {
        return;
      }
      if (party.Army != null && party.Army.LeaderParty != party)
      {
        return;
      }
      PartyAIClanPartySettings settings = SubModule.PartySettingsManager.Settings(party.LeaderHero);
      if (settings == null) return;
      ImplementAllowRaidingVillages(party, thinkParams, settings);
      ImplementAllowJoiningArmies(party, thinkParams, settings);
      ImplementAllowBesieging(party, thinkParams, settings);
      if (!settings.HasActiveOrder)
      {
        return;
      }
      if (settings.Order.Behavior != OrderType.PatrolAroundPoint &&
          settings.Order.Behavior != OrderType.PatrolClanLands)
      {
        if (party.GetNumDaysForFoodToLast() < 3 && party.GetNumDaysForFoodToLast() > 0)
        {
          AbandonOrderForNoFood(party, settings);
          return;
        }
      }
      IMapPoint target = settings.Order.Target;
      PartyObjective existingObjective = party.Objective;
      List<(AIBehaviorData, float)> newParams;
      switch (settings.Order.Behavior)
      {
        case OrderType.EscortParty:
          ImplementEscortParty(settings, party, target, out newParams);
          break;
        case OrderType.RecruitFromTemplate:
          ImplementRecruitFromTemplate(settings, party);
          newParams = new(thinkParams.AIBehaviorScores);
          break;
        case OrderType.AttackParty:
          ImplementAttackParty(settings, party, target, out newParams);
          break;
        case OrderType.PatrolAroundPoint:
          ImplementPatrolAroundSettlement(settings, party, target, thinkParams, out newParams, distanceFactor: settings.PatrolRadius);
          break;
        case OrderType.BesiegeSettlement:
          ImplementBesiegeSettlement(settings, party, target, thinkParams, out newParams);
          break;
        case OrderType.StayInSettlement:
          ImplementStayInSettlement(settings, party, out newParams);
          break;
        case OrderType.VisitSettlement:
          ImplementVisitSettlement(settings, party, out newParams);
          break;
        case OrderType.DefendSettlement:
          ImplementDefendSettlement(settings, party, out newParams);
          break;
        case OrderType.PatrolClanLands:
          ImplementPatrolClanLands(settings.Hero, party, target, thinkParams, out newParams);
          break;
        default:
          return;
      }
      SwapParams(thinkParams, party, newParams);
      if (existingObjective != party.Objective)
      {
        settings.CachedPartyObjective = existingObjective;
      }
    }

    private void SwapParams(PartyThinkParams thinkParams, MobileParty party, List<(AIBehaviorData, float)> newParams)
    {
      thinkParams.Reset(party);
      float threshold = 0.3f;
      bool aboveThreshold = newParams.Any(p => p.Item2 > threshold);
      for (int i = 0; i < newParams.Count; i++)
      {
        (AIBehaviorData, float) param = newParams[i];
        if (!aboveThreshold)
        {
          param.Item2 += threshold;
        }
        thinkParams.AddBehaviorScore(param);
      }
    }

    private void ImplementStayInSettlement(PartyAIClanPartySettings settings, MobileParty party, out List<(AIBehaviorData, float)> newParams)
    {
      newParams = new List<(AIBehaviorData, float)>();
      IMapPoint target = settings.Order.Target;
      Settlement settlement = (Settlement)target;

      if (FactionManager.IsAtWarAgainstFaction(target.MapFaction, party.MapFaction))
      {
        settings.ClearOrder();
        return;
      }

      party.Ai.SetDoNotMakeNewDecisions(true);

      // Low on food -> go to nearest friendly/neutral town
      if (party.GetNumDaysForFoodToLast() < 4 && party.GetNumDaysForFoodToLast() > 0)
      {
        Settlement town = FindNearestSettlement(
            s =>
                s.IsTown && // same faction OR neutral
                (s.MapFaction == party.MapFaction ||
                 FactionManager.IsNeutralWithFaction(party.MapFaction, s.MapFaction)) &&
                (!(target is Settlement ts) || s != ts),
            target
        );
        if (town != null && TryGetBestNavigationDataForSettlement(party, town, out MobileParty.NavigationType navType, out bool isFromPort, out bool isTargetingPort))
        {
          SetPartyAiAction.GetActionForVisitingSettlement(
            party,
            town,
            navType,
            isFromPort,
            isTargetingPort);
        }
        return;
      }

      if (party.CurrentSettlement != target)
      {
        if (settlement.IsUnderSiege)
        {
          settings.ClearOrder();
        }
        else if (target is Settlement targetSettlement)
        {
          if (TryGetBestNavigationDataForSettlement(party, targetSettlement, out MobileParty.NavigationType navType, out bool isFromPort, out bool isTargetingPort))
          {
            SetPartyAiAction.GetActionForVisitingSettlement(
              party,
              targetSettlement,
              navType,
              isFromPort,
              isTargetingPort
            );
          }
        }
      }
    }

    private void ImplementVisitSettlement(PartyAIClanPartySettings settings, MobileParty party, out List<(AIBehaviorData, float)> newParams)
    {
      newParams = new List<(AIBehaviorData, float)>();
      IMapPoint target = settings.Order.Target;
      Settlement settlement = (Settlement)target;

      if (FactionManager.IsAtWarAgainstFaction(target.MapFaction, party.MapFaction))
      {
        settings.ClearOrder();
        return;
      }

      party.Ai.SetDoNotMakeNewDecisions(true);

      // Low on food -> go to nearest friendly/neutral town
      if (party.GetNumDaysForFoodToLast() < 4 && party.GetNumDaysForFoodToLast() > 0)
      {
        Settlement town = FindNearestSettlement(
            s =>
                s.IsTown && // same faction OR neutral
                (s.MapFaction == party.MapFaction ||
                 FactionManager.IsNeutralWithFaction(party.MapFaction, s.MapFaction)) &&
                (!(target is Settlement ts) || s != ts),
            target
        );
        if (town != null && TryGetBestNavigationDataForSettlement(party, town, out MobileParty.NavigationType navType, out bool isFromPort, out bool isTargetingPort))
        {
          SetPartyAiAction.GetActionForVisitingSettlement(
            party,
            town,
            navType,
            isFromPort,
            isTargetingPort);
        }
        return;
      }

      if (party.CurrentSettlement != target)
      {
        if (settlement.IsUnderSiege)
        {   
          settings.ClearOrder();
        }
         else if (target is Settlement targetSettlement)
         {
          if (TryGetBestNavigationDataForSettlement(party, targetSettlement, out MobileParty.NavigationType navType, out bool isFromPort, out bool isTargetingPort))
          {
            SetPartyAiAction.GetActionForVisitingSettlement(
              party,
              targetSettlement,
              navType,
              isFromPort,
              isTargetingPort
            );
         }
         }
      }
    }

        private void ImplementDefendSettlement(PartyAIClanPartySettings settings, MobileParty party, out List<(AIBehaviorData, float)> newParams)
        {
            newParams = new List<(AIBehaviorData, float)>();
            IMapPoint target = settings.Order.Target;
            Settlement settlement = (Settlement)target;

            if (target.MapFaction != party.MapFaction)
            {
                settings.ClearOrder();
                return;
            }

            party.Ai.SetDoNotMakeNewDecisions(true);

            // Low on food -> go to nearest friendly/neutral town
            if (party.GetNumDaysForFoodToLast() < 4 && party.GetNumDaysForFoodToLast() > 0)
            {
                Settlement town = FindNearestSettlement(
                    s =>
                        s.IsTown &&
                        (s.MapFaction == party.MapFaction ||
                         FactionManager.IsNeutralWithFaction(party.MapFaction, s.MapFaction)) &&
                        (!(target is Settlement ts) || s != ts),
                    target
                );

                if (town != null && TryGetBestNavigationDataForSettlement(party, town, out MobileParty.NavigationType navType, out bool isFromPort, out bool isTargetingPort))
                {
                    SetPartyAiAction.GetActionForVisitingSettlement(
                        party,
                        town,
                        navType,
                        isFromPort,
                        isTargetingPort
                    );
                }

                return;
            }

            // Not in the target settlement yet -> move there
            if (party.CurrentSettlement != settlement)
            {
                if (TryGetBestNavigationDataForSettlement(party, settlement, out MobileParty.NavigationType navType, out bool isFromPort, out bool isTargetingPort))
                {
                    if (settlement.IsUnderSiege)
                    {
                        SetPartyAiAction.GetActionForDefendingSettlement(
                            party,
                            settlement,
                            navType,
                            isFromPort,
                            isTargetingPort
                        );
                    }
                    else
                    {
                        SetPartyAiAction.GetActionForVisitingSettlement(
                            party,
                            settlement,
                            navType,
                            isFromPort,
                            isTargetingPort
                        );
                    }
                }
            }
        }

        private void ImplementEscortParty(PartyAIClanPartySettings settings, MobileParty party, IMapPoint target, out List<(AIBehaviorData, float)> newParams)
        {
            newParams = new();

            if (target is not MobileParty targetParty || targetParty == null)
            {
                settings.ClearOrder();
                ResetPartyAi(party);
                return;
            }

            if (FactionManager.IsAtWarAgainstFaction(party.MapFaction, targetParty.MapFaction))
            {
                settings.ClearOrder();
                ResetPartyAi(party);
                return;
            }

            bool navMismatch = party.DesiredAiNavigationType != targetParty.DesiredAiNavigationType;

            // Allow issuing the escort action when the AI is unlocked OR default hold OR navigation mode changed
            if (!party.Ai.DoNotMakeNewDecisions || party.DefaultBehavior == AiBehavior.Hold || navMismatch)
            {
                SetPartyAiAction.GetActionForEscortingParty(
                    party,
                    targetParty,
                    targetParty.DesiredAiNavigationType, // use target's navigation type
                    false, // isFromPort
                    false  // isTargetingPort
                );
                party.Ai.SetDoNotMakeNewDecisions(true);
            }
        }

        private void ImplementAttackParty(PartyAIClanPartySettings settings, MobileParty party, IMapPoint target, out List<(AIBehaviorData, float)> newParams)
        {
            newParams = new();

            if (target is not MobileParty targetParty || targetParty == null)
            {
                settings.ClearOrder();
                ResetPartyAi(party);
                return;
            }

            if (!FactionManager.IsAtWarAgainstFaction(party.MapFaction, targetParty.MapFaction))
            {
                settings.ClearOrder();
                ResetPartyAi(party);
                return;
            }

            bool navMismatch = party.DesiredAiNavigationType != targetParty.DesiredAiNavigationType;

            // Allow issuing the engage action when the AI is unlocked OR default hold OR navigation mode changed
            if (!party.Ai.DoNotMakeNewDecisions || party.DefaultBehavior == AiBehavior.Hold || navMismatch)
            {
                SetPartyAiAction.GetActionForEngagingParty(
                    party,
                    targetParty,
                    targetParty.DesiredAiNavigationType, // use target's navigation type
                    false // isFromPort
                );

                party.Ai.SetDoNotMakeNewDecisions(true);
            }
        }

        private void ImplementRecruitFromTemplate(PartyAIClanPartySettings settings, MobileParty party)
        {
      int freeSlots = (int)((1f - party.PartySizeRatio) * party.Party.PartySizeLimit);
      if (freeSlots < 1)
      {
        settings.ClearOrder();
        return;
      }

      if (settings.Order.Target is not Settlement currentTarget || party.CurrentSettlement == currentTarget)
      {
        IEnumerable<Settlement> settlements = Settlement.All.Where(s =>
          (s.IsVillage || s.IsTown) &&
          (settings.RecruitFromEnemySettlements || !FactionManager.IsAtWarAgainstFaction(party.MapFaction, s.MapFaction)) &&
          (settings.PartyTemplate?.TroopCultures.Contains(s.Culture) ?? true));

        currentTarget = FindNearestSettlement(s =>
        {
          if (s == party.CurrentSettlement || s.GetPosition2D.Distance(party.GetPosition2D) < 2f)
            return false;

          if (_recentlyRecruitedFromSettlements.Any(l => l.Settlement == s && l.Party == party))
            return false;

          int count = ComputeRecruitableVolunteersCount(party, s, settings);

          if (count < 3 && freeSlots > 3)
            return false;

          if (count == 0)
            return false;

          return true;
        }, party, settlements);

        if (currentTarget != null)
        {
          _recentlyRecruitedFromSettlements.Add(new(currentTarget, CampaignTime.Now, party));
          settings.Order.Target = currentTarget;
        }
      }

      if (currentTarget == null)
      {
        if (party.Ai.DoNotMakeNewDecisions)
        {
          party.Ai.SetDoNotMakeNewDecisions(false);
          ResetPartyAi(party);
        }
        return;
      }

      party.Ai.SetDoNotMakeNewDecisions(true);

      // Use ALL navigation for subsequent target selection when the party can use naval routes.
      // AI parties can embark/disembark from shore without a port, so prefer allowing naval paths.
      var navType = party.HasNavalNavigationCapability ? MobileParty.NavigationType.All : party.DesiredAiNavigationType;
      party.DesiredAiNavigationType = navType;

    SetPartyAiAction.GetActionForVisitingSettlement(
        party,
        currentTarget,
        navType,
        false, // isFromPort
        false  // isTargetingPort
    );
        }

        private int ComputeRecruitableVolunteersCount(
            MobileParty party,
            Settlement settlement,
            PartyAIClanPartySettings heroSettings)
        {
            if (party?.LeaderHero == null)
                return 0;
            
            if (settlement.IsVillage)
            {
                var village = settlement.Village;
                if (village == null)
                    return 0;

                if (village.VillageState == Village.VillageStates.Looted ||
                    village.VillageState == Village.VillageStates.BeingRaided)
                {
                    return 0;
                }
            }

            Hero hero = party.LeaderHero;

            if (!SubModule.PartySettingsManager.IsHeroManageable(hero) ||
                hero.PartyBelongedTo == null ||
                hero.PartyBelongedTo.LeaderHero != hero)
            { 
                return 0; 
            }

            if (!heroSettings.AllowRecruitment)
            {
                return 0;
            }

            // Old comment: "if we're going to convert the troop anyway, it doesn't matter"
            // In the original mod this early-return left __result at 0, so we preserve that.
            if (SubModule.PartySettingsManager.AllowTroopConversion && heroSettings.PartyTemplate != null)
            {
                return 0;
            }

            PartyCompositionObect comp =
                SubModule.PartyTroopRecruiter.GetPartyComposition(party.Party, heroSettings);

            int count = 0;

            foreach (Hero notable in settlement.Notables)
            {
                if (!notable.IsAlive)
                    continue;

                int max = Campaign.Current.Models.VolunteerModel
                    .MaximumIndexHeroCanRecruitFromHero(
                        party.IsGarrison ? party.Party.Owner : party.LeaderHero,
                        notable);

                for (int i = 0; i <= max && i < notable.VolunteerTypes.Length; i++)
                {
                    CharacterObject troop = notable.VolunteerTypes[i];
                    if (troop != null &&
                        SubModule.PartyTroopRecruiter.ShouldRecruit(comp, heroSettings, troop, party.Party))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private void ImplementBesiegeSettlement(PartyAIClanPartySettings settings, MobileParty party, IMapPoint target, in PartyThinkParams thinkParams, out List<(AIBehaviorData, float)> newParams)
        {
            newParams = new List<(AIBehaviorData, float)>();

            if (!FactionManager.IsAtWarAgainstFaction(party.MapFaction, target.MapFaction))
            {
                settings.ClearOrder();
                ResetPartyAi(party);
                return;
            }

            if (!party.Ai.DoNotMakeNewDecisions || party.DefaultBehavior == AiBehavior.Hold)
            {
                if (target is Settlement targetSettlement)
                {
                    SetPartyAiAction.GetActionForBesiegingSettlement(
                        party,
                        targetSettlement,
                        party.DesiredAiNavigationType,
                        false // isFromPort
                    );

                    party.Ai.SetDoNotMakeNewDecisions(true);
                }
                else
                {
                    // Safety fallback if target somehow isn't a settlement
                    settings.ClearOrder();
                    ResetPartyAi(party);
                }
            }
        }

        private void ImplementPatrolClanLands(Hero hero, MobileParty party, IMapPoint target, in PartyThinkParams thinkParams, out List<(AIBehaviorData, float)> newParams, float distanceFactor = 1.0f, bool useQuickDistance = false)
        {
            newParams = new List<(AIBehaviorData, float)>();
            float range = Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(party.DesiredAiNavigationType) * 0.9f * distanceFactor;

            if (hero?.Clan?.Settlements?.Count == 0)
            {
                newParams = thinkParams.AIBehaviorScores.ConvertAll(s => (s.Item1, s.Item2));
                return;
            }

            // === PRIORITY: Low on food -> go get food ===
            int daysOfFood = party.GetNumDaysForFoodToLast();
            if (daysOfFood <= 8)
            {
                Settlement town = FindNearestReachableSettlement(
                    party,
                    s => (s.IsTown || s.IsVillage) &&
                         (s.MapFaction == party.MapFaction ||
                          FactionManager.IsNeutralWithFaction(party.MapFaction, s.MapFaction)),
                    party.DesiredAiNavigationType
                );

                if (town != null && TryGetBestNavigationDataForSettlement(party, town, out MobileParty.NavigationType townNavType, out bool townIsFromPort, out bool townIsTargetingPort))
                {
                    newParams.Add((
                        new AIBehaviorData(town, AiBehavior.GoToSettlement, townNavType, false, townIsFromPort, townIsTargetingPort),
                        10f
                    ));
                }
                return;
            }

            if (hero?.Clan == null)
                return;

            // Find nearest clan settlement to patrol around
            Settlement nearestClan = FindNearestSettlement(s => s.OwnerClan == hero.Clan, party);
            if (nearestClan == null)
                return;

            // 5% chance to switch to a random clan settlement (variety in patrol)
            if (MBRandom.RandomFloat < 0.05f && hero.Clan.Settlements.Count > 0)
            {
                nearestClan = hero.Clan.Settlements.GetRandomElementInefficiently();
            }

            Vec2 clanPos = nearestClan.GetPosition2D;

            // === PRIORITY: React to clan settlements in danger ===
            foreach (Settlement clanSettlement in hero.Clan.Settlements)
            {
                float distToClanSettlement = party.GetPosition2D.Distance(clanSettlement.GetPosition2D);

                if (distToClanSettlement > range * 8)
                    continue;

                if (clanSettlement.IsFortification && clanSettlement.IsUnderSiege)
                {
                    if (TryGetBestNavigationDataForSettlement(party, clanSettlement, out MobileParty.NavigationType navType, out bool isFromPort, out bool isTargetingPort))
                    {
                        newParams.Add((
                            new AIBehaviorData(clanSettlement, AiBehavior.DefendSettlement, navType, false, isFromPort, isTargetingPort),
                            8f
                        ));
                    }

                    if (party.Objective != PartyObjective.Defensive)
                    {
                        party.SetPartyObjective(PartyObjective.Defensive);
                    }
                    return;
                }

                if (clanSettlement.IsVillage && clanSettlement.Village?.VillageState == Village.VillageStates.BeingRaided)
                {
                    if (TryGetBestNavigationDataForSettlement(party, clanSettlement, out MobileParty.NavigationType navType, out bool isFromPort, out bool isTargetingPort))
                    {
                        newParams.Add((
                            new AIBehaviorData(clanSettlement, AiBehavior.DefendSettlement, navType, false, isFromPort, isTargetingPort),
                            8f
                        ));
                    }

                    if (party.Objective != PartyObjective.Defensive)
                    {
                        party.SetPartyObjective(PartyObjective.Defensive);
                      }
                      return;
                }
            }

            // === If too far from clan lands, issue command to walk there ===
            if (party.GetPosition2D.Distance(clanPos) > range * 4)
            {
                if (TryGetBestNavigationDataForSettlement(party, nearestClan, out MobileParty.NavigationType navType, out bool isFromPort, out bool isTargetingPort))
                {
                    newParams.Add((
                        new AIBehaviorData(nearestClan, AiBehavior.GoToSettlement, navType, false, isFromPort, isTargetingPort),
                        5f
                    ));
                }
            }

            // === ALWAYS filter vanilla AI behaviors by distance ===
            foreach ((AIBehaviorData behavior, float weight) in thinkParams.AIBehaviorScores)
            {
                CampaignVec2 behaviorTarget = CampaignVec2.Zero;

                if (behavior.Position != CampaignVec2.Zero)
                {
                    behaviorTarget = behavior.Position;
                }
                else if (behavior.Party != null)
                {
                    behaviorTarget = TryGetMapPointPosition(behavior.Party);
                    if (behaviorTarget == CampaignVec2.Zero)
                        continue;
                }

                if (behaviorTarget == CampaignVec2.Zero)
                    continue;

                float distToTarget = behaviorTarget.ToVec2().Distance(clanPos);
                if (distToTarget < range)
                {
                    newParams.Add((behavior, weight));
                }
            }

            if (party.Objective != PartyObjective.Aggressive)
            {
                party.SetPartyObjective(PartyObjective.Aggressive);
            }
        }

        private void ImplementPatrolAroundSettlement(PartyAIClanPartySettings settings, MobileParty party, IMapPoint target, in PartyThinkParams thinkParams, out List<(AIBehaviorData, float)> newParams, float distanceFactor = 1.0f)
        {
            newParams = new List<(AIBehaviorData, float)>();

            Settlement centerSettlement = (Settlement)target;

            // Range is a filtering/"too far" heuristic; navigation routing uses vanilla-derived data.
            float range = Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.Default) * 0.9f * distanceFactor;
            Vec2 centerPos = TryGetSettlementPos2D(centerSettlement);
            if (centerPos == Vec2.Zero)
            {
                // Skip this tick if position is unavailable
                newParams = thinkParams.AIBehaviorScores.ConvertAll(s => (s.Item1, s.Item2));
                return;
            }

            // Compute best navigation and port flags for reaching the patrol center.
            if (!TryGetBestNavigationDataForSettlement(party, centerSettlement, out MobileParty.NavigationType centerNavType, out bool centerIsFromPort, out bool centerIsTargetingPort))
            {
                newParams = thinkParams.AIBehaviorScores.ConvertAll(s => (s.Item1, s.Item2));
                return;
            }

            // === PRIORITY: Low on food -> go get food ===
            int daysOfFood = party.GetNumDaysForFoodToLast();
            if (daysOfFood <= 8)
            {
                Settlement town = FindNearestReachableSettlement(
                    party,
                    s => (s.IsTown || s.IsVillage) &&
                         (s.MapFaction == party.MapFaction ||
                          FactionManager.IsNeutralWithFaction(party.MapFaction, s.MapFaction)),
                    party.DesiredAiNavigationType
                );

                if (town != null && TryGetBestNavigationDataForSettlement(party, town, out MobileParty.NavigationType townNavType, out bool townIsFromPort, out bool townIsTargetingPort))
                {
                    newParams.Add((
                        new AIBehaviorData(town, AiBehavior.GoToSettlement, townNavType, false, townIsFromPort, townIsTargetingPort),
                        2f
                    ));
                }
                return;
            }

            // === PRIORITY: Defend nearby same-faction settlements under attack ===
            foreach (Settlement s in Settlement.All)
            {
                if (s.MapFaction != party.MapFaction)
                    continue;

                Vec2 sPos = TryGetSettlementPos2D(s);
                if (sPos == Vec2.Zero)
                    continue;

                float distToSettlement = sPos.Distance(centerPos);
                if (distToSettlement > range)
                    continue;

                if ((s.IsFortification && s.IsUnderSiege) || (s.IsVillage && s.Village?.VillageState == Village.VillageStates.BeingRaided))
                {
                    if (TryGetBestNavigationDataForSettlement(party, s, out MobileParty.NavigationType defendNavType, out bool defendIsFromPort, out bool defendIsTargetingPort))
                    {
                        SetPartyAiAction.GetActionForDefendingSettlement(
                            party,
                            s,
                            defendNavType,
                            defendIsFromPort,
                            defendIsTargetingPort
                        );
                    }
                    return;
                }
            }

            // If too far from patrol center: walk there
            if (party.GetPosition2D.Distance(centerPos) > range * 4)
            {
                SetPartyAiAction.GetActionForVisitingSettlement(
                    party,
                    centerSettlement,
                    centerNavType,
                    centerIsFromPort,
                    centerIsTargetingPort
                );

                newParams.Add((
                    new AIBehaviorData(centerSettlement, AiBehavior.GoToSettlement, centerNavType, false, centerIsFromPort, centerIsTargetingPort),
                    5f
                ));

                return;
            }

            // Anchor patrol on the intended settlement.
            newParams.Add((
                new AIBehaviorData(centerSettlement, AiBehavior.PatrolAroundPoint, centerNavType, false, centerIsFromPort, centerIsTargetingPort),
                6f
            ));

            // In range: filter vanilla behavior scores by distance to center
            foreach ((AIBehaviorData behavior, float weight) in thinkParams.AIBehaviorScores)
            {
                CampaignVec2 behaviorTarget = CampaignVec2.Zero;

                if (behavior.Position != CampaignVec2.Zero)
                {
                    behaviorTarget = behavior.Position;
                }
                else if (behavior.Party != null)
                {
                    behaviorTarget = TryGetMapPointPosition(behavior.Party);
                    if (behaviorTarget == CampaignVec2.Zero)
                        continue;
                }

                if (behaviorTarget == CampaignVec2.Zero)
                    continue;

                float distToTarget = behaviorTarget.ToVec2().Distance(centerPos);
                if (distToTarget < range)
                {
                    newParams.Add((behavior, weight));
                }
            }

            if (party.Objective != PartyObjective.Aggressive)
            {
                party.SetPartyObjective(PartyObjective.Aggressive);
            }
        }

    /// <summary>
    /// Find the nearest settlement that is actually reachable using the specified navigation type
    /// </summary>
    private Settlement FindNearestReachableSettlement(
        MobileParty party,
        Func<Settlement, bool> condition,
        MobileParty.NavigationType navigationType)
    {
        Settlement result = null;
        float bestDistance = float.MaxValue;

        foreach (Settlement settlement in Settlement.All)
        {
            if (condition != null && !condition(settlement))
                continue;

            // Use SafeGet to check if settlement.GatePosition is available
            CampaignVec2 gatePos = TryGetSettlementGatePosition(settlement);
            if (gatePos == CampaignVec2.Zero)
                continue;

            float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(
                party,
                gatePos,
                navigationType,
                out float _
            );

            // Skip unreachable settlements (float.MaxValue means no path exists)
            if (distance >= float.MaxValue - 1f)
                continue;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                result = settlement;
            }
        }

        return result;
    }

    private void ImplementAllowRaidingVillages(MobileParty party, PartyThinkParams thinkParams, PartyAIClanPartySettings settings)
    {
        if (settings.AllowRaidVillages)
        {
            return;
        }

        // prevent raiding in army (leave if they raid)
        // The other half of this is in HarmonyPatches.AiMilitaryBehaviorPatches
        if (party.Army != null && !party.Army.LeaderParty.LeaderHero.Equals(party.LeaderHero))
        {
            if (PAIArmyManagementCalculationModel.IsArmyRaiding(party.Army))
            {
                // refund influence
                int influence = Campaign.Current.Models.ArmyManagementCalculationModel.CalculatePartyInfluenceCost(party.Army.LeaderParty, party);
                ChangeClanInfluenceAction.Apply(party.Army.LeaderParty.LeaderHero.Clan, influence);

                LeaveArmy(party, thinkParams);
            }
        }
    }

    private void ImplementAllowJoiningArmies(MobileParty party, PartyThinkParams thinkParams, PartyAIClanPartySettings settings)
    {
        if (settings.AllowAllowJoinArmies)
        {
            return;
        }

        // leave army if setting is disabled
        if (party.Army != null && !party.Army.LeaderParty.LeaderHero.Equals(party.LeaderHero) && !party.Army.LeaderParty.LeaderHero.Equals(Hero.MainHero))
        {
            LeaveArmy(party, thinkParams);
        }
    }

    private void ImplementAllowBesieging(MobileParty party, PartyThinkParams thinkParams, PartyAIClanPartySettings settings)
    {
        if (settings.AllowSieging)
        {
            return;
        }

        // prevent besieging in army (leave if they besiege)
        // The other half of this is in HarmonyPatches.AiMilitaryBehaviorPatches
        if (party.Army != null && !party.Army.LeaderParty.LeaderHero.Equals(party.LeaderHero))
        {
            if (PAIArmyManagementCalculationModel.IsArmyBesieging(party.Army))
            {
                LeaveArmy(party, thinkParams);
            }
        }
    }

    private void AbandonOrderForNoFood(MobileParty party, PartyAIClanPartySettings settings)
    {
      TextObject text = new TextObject("{=PAIw38SHHlH}{PARTY} is no longer {ORDER} because their food supplies ran low.").SetTextVariable("PARTY", party.Name).SetTextVariable("ORDER", SubModule.PartySettingsManager.GetOrderText(party.LeaderHero));
      InformationManager.DisplayMessage(new InformationMessage(text.ToString(), Colors.Magenta));
      settings.ClearOrder();
      ResetPartyAi(party);
    }

    private void LeaveArmy(MobileParty party, PartyThinkParams thinkParams)
    {
      // refund influence
      int influence = Campaign.Current.Models.ArmyManagementCalculationModel
          .CalculatePartyInfluenceCost(party.Army.LeaderParty, party);
      ChangeClanInfluenceAction.Apply(party.Army.LeaderParty.LeaderHero.Clan, influence);

      party.Army = null;

      // Find nearest friendly/neutral fortification to send the party to
      Settlement nearestFort = FindNearestSettlement(
          s =>
              s.IsFortification &&
              (s.MapFaction == party.MapFaction ||
               FactionManager.IsNeutralWithFaction(party.MapFaction, s.MapFaction)),
          party
      );

      if (nearestFort != null && TryGetBestNavigationDataForSettlement(party, nearestFort, out MobileParty.NavigationType navType, out bool isFromPort, out bool isTargetingPort))
      {
        SetPartyAiAction.GetActionForVisitingSettlement(
          party,
          nearestFort,
          navType,
          isFromPort,
          isTargetingPort
        );
      }

      ResetPartyAi(party);
      thinkParams.Reset(party);
    }

    private void ResetPartyAi(MobileParty party)
    {
      party.Ai.RethinkAtNextHourlyTick = true;
      party.Ai.SetDoNotMakeNewDecisions(false);
    }

    private IEnumerable<WarPartyComponent> ActiveClanParties(Clan c) => c.WarPartyComponents.Where(p => p.MobileParty != MobileParty.MainParty);

    // made my own, native one sucks
    public static Settlement FindNearestSettlement(
      Func<Settlement, bool> condition,
      IMapPoint toMapPoint,
      IEnumerable<Settlement> settlements = null)
    {
      Settlement result = null;
      settlements ??= Settlement.All;

      // Get the "origin" position from the map point
      Vec2 originPos;

      if (toMapPoint is Settlement originSettlement)
      {
        originPos = originSettlement.GetPosition2D;
      }
      else if (toMapPoint is MobileParty originParty)
      {
        originPos = originParty.GetPosition2D;
      }
      else
      {
        // Fallback: use main party position if don't recognize the IMapPoint type
        originPos = MobileParty.MainParty.GetPosition2D;
      }

      float bestDistSq = float.MaxValue;

      foreach (Settlement item in settlements)
      {
        if (condition != null && !condition(item))
          continue;

        // Distance in 2D map space
        float distSq = originPos.DistanceSquared(item.GetPosition2D);

        if (distSq < bestDistSq)
        {
          bestDistSq = distSq;
          result = item;
        }
      }

      return result;
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData("_assumingDirectControl", ref _assumingDirectControl);
        dataStore.SyncData("_recentlyRecruitedFromSettlements", ref _recentlyRecruitedFromSettlements);
    }

    /// <summary>
    /// Mirrors vanilla AiVisitSettlementBehavior.GetBestNavigationDataForVisitingSettlement.
    /// Computes the best navigation type and port transition flags for reaching a settlement.
    /// </summary>
    private static bool TryGetBestNavigationDataForSettlement(
      MobileParty party,
      Settlement settlement,
      out MobileParty.NavigationType navigationType,
      out bool isFromPort,
      out bool isTargetingPort)
    {
      navigationType = MobileParty.NavigationType.None;
      isFromPort = false;
      isTargetingPort = false;

      if (party == null || settlement == null)
      {
        return false;
      }

      // Normalize to canonical Settlement instance to avoid cache-key identity mismatches
      Settlement normalizedSettlement = Settlement.Find(settlement.StringId) ?? settlement;

      MobileParty.NavigationType bestNavType = MobileParty.NavigationType.None;
      float bestDistance = float.MaxValue;
      bool bestIsFromPort = false;

      bool portBlocked = normalizedSettlement.HasPort &&
                         normalizedSettlement.SiegeEvent != null &&
                         normalizedSettlement.SiegeEvent.IsBlockadeActive;

      // Try non-port targeting first (unless blockade prevents portless approach for naval-capable parties).
      if (!portBlocked || !party.HasNavalNavigationCapability)
      {
        AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(
          party,
          normalizedSettlement,
          false,
          out bestNavType,
          out bestDistance,
          out bestIsFromPort);
      }

      // If the party can travel by sea and the settlement has a port, also try targeting the port.
      if (party.HasNavalNavigationCapability && normalizedSettlement.HasPort)
      {
        AiHelper.GetBestNavigationTypeAndAdjustedDistanceOfSettlementForMobileParty(
          party,
          normalizedSettlement,
          true,
          out MobileParty.NavigationType portNavType,
          out float portDistance,
          out bool portIsFromPort);

        if (portDistance < bestDistance)
        {
          navigationType = portNavType;
          isFromPort = portIsFromPort;
          isTargetingPort = true;
          return navigationType != MobileParty.NavigationType.None;
        }
      }

      navigationType = bestNavType;
      isFromPort = bestIsFromPort;
      isTargetingPort = false;

      return navigationType != MobileParty.NavigationType.None;
    }

    /// <summary>
    /// Safely gets the 2D position of a settlement, falling back if GatePosition throws.
    /// </summary>
    private static Vec2 TryGetSettlementPos2D(Settlement settlement)
    {
        if (settlement == null)
            return Vec2.Zero;
        return SafeGet(() => settlement.GatePosition.ToVec2(), SafeGet(() => settlement.GetPosition2D, Vec2.Zero));
    }

    /// <summary>
    /// Safely gets the CampaignVec2 gate position of a settlement, falling back if GatePosition throws.
    /// </summary>
    private static CampaignVec2 TryGetSettlementGatePosition(Settlement settlement)
    {
        if (settlement == null)
            return CampaignVec2.Zero;
        return SafeGet(() => settlement.GatePosition, SafeGet(() => new CampaignVec2(settlement.GetPosition2D, true), CampaignVec2.Zero));
    }

    /// <summary>
    /// Generic safe property access helper. Returns fallback if exception occurs.
    /// </summary>
    internal static T SafeGet<T>(Func<T> getter, T fallback = default(T))
    {
        try { return getter(); }
        catch { return fallback; }
    }
  }
}
