using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.JobGauge.Enums;
using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using NativeGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using ScholarHelper.Rules;

namespace ScholarHelper.Core;

public sealed unsafe class AlertEngine : IDisposable
{
    private const uint WellFedStatusId = 48;
    private const uint MealItemCategoryId = 46;
    private const uint DissipationStatusId = 791;
    private const uint DissipationAltStatusId = 2069;
    private const uint BioStatusId = 179;
    private const uint Bio2StatusId = 189;
    private const uint BiolysisStatusId = 1895;
    private const uint BiolysisAltStatusId = 3089;
    private const uint RaisePendingStatusId = 148;
    private const uint RaisePendingAltStatusId = 1140;
    private const uint CatalyzeStatusId = 1918;
    private const uint InvalidEntityId = 0xE0000000;

    private static readonly HashSet<uint> BossMitDebuffIds =
    [
        1193, 1203, 1195, 860,
    ];

    private static readonly HashSet<uint> PartyMitBuffIds =
    [
        299, 1944, 2638, 317, 1875, 2711, 2618, 3003,
        1873, 1872, 849, 1839, 1894, 1175, 1934, 1951, 1826, 2707,
    ];

    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IPartyList partyList;
    private readonly IBuddyList buddyList;
    private readonly IJobGauges jobGauges;
    private readonly ITargetManager targetManager;
    private readonly ICondition condition;
    private readonly Configuration config;
    private readonly IPluginLog log;
    private readonly IReadOnlyList<AlertRule> rules;
    private readonly ExcelSheet<TerritoryType>? territorySheet;
    private readonly ExcelSheet<BNpcBase>? bnpcBaseSheet;
    private readonly ExcelSheet<Item>? itemSheet;
    private readonly HashSet<uint> mealItemIds = new();

    private readonly HashSet<HotbarGlowTarget> activeGlows = new();
    private readonly List<AlertBannerLine> activeBanners = new();
    private readonly Dictionary<string, bool> soundWasActive = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> lastEdgeKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> lastSoundUtc = new(StringComparer.Ordinal);
    private readonly List<PartyMemberState> partyScratch = new(8);
    private readonly List<ActiveMitigation> mitScratch = new(12);
    private readonly HashSet<string> mitDedupScratch = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, (string Name, uint JobId)> raiserScratch = new(4);

    private uint cachedTerritoryId = uint.MaxValue;
    private bool cachedIsRaidOrTrial;
    private bool cachedIsDungeon;
    private bool isSubscribed;
    private bool hasLoggedError;

    public IReadOnlyList<AlertRule> Rules => rules;
    public IReadOnlyList<AlertBannerLine> ActiveBanners => activeBanners;
    public bool IsFoodGlowActive { get; private set; }
    public FrameState State { get; private set; } = FrameState.Empty;

    public AlertEngine(
        IFramework framework,
        IClientState clientState,
        IDataManager dataManager,
        IObjectTable objectTable,
        IPartyList partyList,
        IBuddyList buddyList,
        IJobGauges jobGauges,
        ITargetManager targetManager,
        ICondition condition,
        Configuration config,
        IReadOnlyList<AlertRule> rules,
        IPluginLog log)
    {
        this.framework = framework;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.partyList = partyList;
        this.buddyList = buddyList;
        this.jobGauges = jobGauges;
        this.targetManager = targetManager;
        this.condition = condition;
        this.config = config;
        this.rules = rules;
        this.log = log;

        territorySheet = dataManager.GetExcelSheet<TerritoryType>();
        bnpcBaseSheet = dataManager.GetExcelSheet<BNpcBase>();
        itemSheet = dataManager.GetExcelSheet<Item>();
        CacheMealItems();

        EnsureRuleConfigs();
    }

    private void CacheMealItems()
    {
        if (itemSheet == null) return;
        foreach (var item in itemSheet)
        {
            if (item.ItemUICategory.RowId != MealItemCategoryId) continue;
            var act = item.ItemAction.ValueNullable;
            if (act.HasValue && act.Value.Data[0] == WellFedStatusId)
            {
                mealItemIds.Add(item.RowId);
            }
        }
    }

    public bool IsMealItem(uint itemId)
    {
        var baseId = itemId >= 1_000_000 ? itemId - 1_000_000 : itemId;
        return mealItemIds.Contains(baseId);
    }

    public void EnsureRuleConfigs()
    {
        foreach (var rule in rules)
        {
            if (!config.RuleConfigs.ContainsKey(rule.Id))
            {
                config.RuleConfigs[rule.Id] = rule.CreateDefaultConfig();
            }
        }
    }

    public AlertRuleConfig GetRuleConfig(AlertRule rule)
    {
        if (!config.RuleConfigs.TryGetValue(rule.Id, out var cfg))
        {
            cfg = rule.CreateDefaultConfig();
            config.RuleConfigs[rule.Id] = cfg;
        }

        return cfg;
    }

    public void ResetAllRuleConfigs()
    {
        config.RuleConfigs.Clear();
        EnsureRuleConfigs();
    }

    public void Enable()
    {
        if (isSubscribed) return;
        framework.Update += OnFrameworkUpdate;
        isSubscribed = true;
    }

    public bool IsHighlighted(ActionType actionType, uint actionId)
        => config.Enabled && activeGlows.Contains(new HotbarGlowTarget(actionType, actionId));

    private void OnFrameworkUpdate(IFramework _)
    {
        activeGlows.Clear();
        activeBanners.Clear();
        IsFoodGlowActive = false;

        try
        {
            if (!config.Enabled)
            {
                State = FrameState.Empty;
                soundWasActive.Clear();
                lastEdgeKey.Clear();
                return;
            }

            var state = BuildFrameState();
            State = state;

            if (!state.IsLoggedIn)
            {
                soundWasActive.Clear();
                lastEdgeKey.Clear();
                return;
            }

            var now = DateTime.UtcNow;

            foreach (var rule in rules)
            {
                var cfg = GetRuleConfig(rule);
                if (!cfg.Enabled
                    || state.Level < rule.MinLevel
                    || (rule.RequireAlive && !state.IsAlive)
                    || (rule.ScholarOnly && !state.IsScholar && !(rule.AllowAllJobsOption && cfg.AllJobs)))
                {
                    soundWasActive[rule.Id] = false;
                    lastEdgeKey[rule.Id] = null;
                    continue;
                }

                var eval = rule.Evaluate(state, cfg);
                if (!eval.IsActive)
                {
                    soundWasActive[rule.Id] = false;
                    lastEdgeKey[rule.Id] = null;
                    continue;
                }

                if ((rule.Capabilities & AlertCapabilities.Glow) != 0 && cfg.EnableGlow)
                {
                    if (eval.Glows != null)
                    {
                        foreach (var g in eval.Glows)
                        {
                            activeGlows.Add(g);
                        }
                    }

                    if (eval.GlowMeals)
                    {
                        IsFoodGlowActive = true;
                    }
                }

                if ((rule.Capabilities & AlertCapabilities.Banner) != 0 && cfg.EnableBanner && eval.Banners != null)
                {
                    activeBanners.AddRange(eval.Banners);
                }

                if (eval.SuppressSound)
                {
                    soundWasActive[rule.Id] = false;
                    lastEdgeKey[rule.Id] = null;
                }
                else
                {
                    var prevSoundActive = soundWasActive.GetValueOrDefault(rule.Id, false);
                    var prevKey = lastEdgeKey.GetValueOrDefault(rule.Id, null);
                    var keyChanged = eval.EdgeKey != null && !string.Equals(eval.EdgeKey, prevKey, StringComparison.Ordinal);

                    if ((rule.Capabilities & AlertCapabilities.Sound) != 0 && cfg.EnableSound && (!prevSoundActive || keyChanged))
                    {
                        var cd = Math.Clamp(config.SoundCooldownSeconds, 1, 30);
                        var last = lastSoundUtc.GetValueOrDefault(rule.Id, DateTime.MinValue);
                        if ((now - last).TotalSeconds >= cd)
                        {
                            SoundEffects.Play(cfg.SoundId, config.SoundVolume);
                            lastSoundUtc[rule.Id] = now;
                        }
                    }

                    soundWasActive[rule.Id] = true;
                    lastEdgeKey[rule.Id] = eval.EdgeKey;
                }
            }

            if (activeBanners.Count > 1)
            {
                activeBanners.Sort(static (a, b) => a.Priority.CompareTo(b.Priority));
            }
        }
        catch (Exception ex)
        {
            if (!hasLoggedError)
            {
                hasLoggedError = true;
                log.Warning(ex, "Skipped Scholar Helper frame evaluation.");
            }
        }
    }

    private FrameState BuildFrameState()
    {
        var player = objectTable.LocalPlayer;
        if (player == null)
        {
            partyScratch.Clear();
            mitScratch.Clear();
            return FrameState.Empty;
        }

        var jobRow = player.ClassJob.ValueNullable;
        var classJobId = player.ClassJob.RowId;
        var isSch = classJobId == FrameState.ScholarClassJobId;

        byte afStacks = 0;
        byte feyGauge = 0;
        short seraphTimer = 0;
        var gaugeDismissed = false;
        if (isSch)
        {
            var g = jobGauges.Get<SCHGauge>();
            afStacks = g.Aetherflow;
            feyGauge = g.FairyGauge;
            seraphTimer = g.SeraphTimer;
            gaugeDismissed = g.SeraphTimer > 0 || g.DismissedFairy is DismissedFairy.Eos or DismissedFairy.Selene;
        }

        var isDissipated = false;
        var hasFood = false;
        var foodMinutes = 0f;
        foreach (var st in player.StatusList)
        {
            if (st.StatusId is DissipationStatusId or DissipationAltStatusId)
            {
                isDissipated = true;
            }
            else if (st.StatusId == WellFedStatusId)
            {
                hasFood = true;
                foodMinutes = st.RemainingTime / 60f;
            }
        }

        var pet = buddyList.PetBuddy;
        var hasPet = pet != null && pet.EntityId is not (0 or InvalidEntityId);
        uint petEntityId = hasPet ? pet!.EntityId : 0;
        var petPos = Vector3.Zero;
        var fairyDist = -1f;
        if (hasPet)
        {
            var petObj = pet!.GameObject ?? objectTable.SearchByEntityId(pet.EntityId);
            if (petObj != null)
            {
                petPos = petObj.Position;
                fairyDist = Vector3.Distance(player.Position, petPos);
            }
        }

        var focusObj = targetManager.FocusTarget;
        IBattleNpc? hostileNpc = ResolveHostileNpc(player.TargetObject) ?? ResolveHostileNpc(focusObj);
        var bioRemaining = 0f;
        if (hostileNpc != null)
        {
            var selfId = player.EntityId;
            foreach (var st in hostileNpc.StatusList)
            {
                if (st.SourceId == selfId
                    && st.StatusId is BioStatusId or Bio2StatusId or BiolysisStatusId or BiolysisAltStatusId)
                {
                    bioRemaining = st.RemainingTime;
                    break;
                }
            }
        }

        if (config.EnableMitigationStrip)
        {
            BuildMitigations(player, hostileNpc);
        }
        else
        {
            mitScratch.Clear();
        }
        BuildParty(player);

        var inCombat = condition[ConditionFlag.InCombat];
        var inDuty = condition[ConditionFlag.BoundByDuty]
                     || condition[ConditionFlag.BoundByDuty56]
                     || condition[ConditionFlag.BoundByDuty95];
        UpdateTerritoryCache();
        var isBossEnemyActive = cachedIsDungeon && config.RemindersInDungeons && HasActiveBossEnemy(inDuty, inCombat, hostileNpc);
        var lowestGearPct = inCombat ? 100 : ReadLowestGearConditionPercent();

        return new FrameState
        {
            IsLoggedIn = true,
            SelfEntityId = player.EntityId,
            ClassJobId = classJobId,
            Role = jobRow?.Role ?? 0,
            Level = player.Level,
            IsAlive = player.CurrentHp > 0,
            CurrentMp = player.CurrentMp,
            Position = player.Position,
            IsCasting = player.IsCasting,
            CastActionId = player.CastActionId,
            InCombat = inCombat,
            InDuty = inDuty,
            IsRaidOrTrial = cachedIsRaidOrTrial,
            IsDungeon = cachedIsDungeon,
            IsBossEnemyActive = isBossEnemyActive,
            RemindersInRaidsAndTrials = config.RemindersInRaidsAndTrials,
            RemindersInDungeons = config.RemindersInDungeons,
            IsPetSuppressed = condition[ConditionFlag.Mounted]
                              || condition[ConditionFlag.RidingPillion]
                              || condition[ConditionFlag.Mounting]
                              || condition[ConditionFlag.Mounting71]
                              || condition[ConditionFlag.MountOrOrnamentTransition]
                              || condition[ConditionFlag.Swimming]
                              || condition[ConditionFlag.Diving]
                              || condition[ConditionFlag.BetweenAreas]
                              || condition[ConditionFlag.BetweenAreas51]
                              || condition[ConditionFlag.WatchingCutscene]
                              || condition[ConditionFlag.WatchingCutscene78]
                              || condition[ConditionFlag.OccupiedInCutSceneEvent]
                              || condition[ConditionFlag.Unconscious]
                              || condition[ConditionFlag.RolePlaying],
            HasFocusTarget = focusObj != null,
            HasFoodBuff = hasFood,
            FoodRemainingMinutes = foodMinutes,
            LowestGearConditionPercent = lowestGearPct,
            AetherflowStacks = afStacks,
            FairyGauge = feyGauge,
            SeraphTimer = seraphTimer,
            HasActivePet = hasPet,
            IsDissipated = isDissipated,
            IsFairyDismissed = gaugeDismissed || isDissipated,
            PetEntityId = petEntityId,
            PetPosition = petPos,
            FairyDistanceYalms = fairyDist,
            HasHostileTarget = hostileNpc != null,
            OwnBioRemainingSeconds = bioRemaining,
            Party = partyScratch,
            ActiveMitigations = mitScratch,
            ActionManagerPtr = ActionManager.Instance(),
        };
    }

    private static IBattleNpc? ResolveHostileNpc(IGameObject? obj)
    {
        if (obj is IBattleNpc npc
            && npc.BattleNpcKind is BattleNpcSubKind.Combatant or BattleNpcSubKind.BNpcPart
            && npc.IsTargetable
            && npc.CurrentHp > 0
            && npc.Address != nint.Zero
            && ActionManager.CanUseActionOnTarget(ScholarAlertRules.BioActionId, (NativeGameObject*)npc.Address))
        {
            return npc;
        }

        return null;
    }

    private void UpdateTerritoryCache()
    {
        var territoryId = clientState.TerritoryType;
        if (territoryId == cachedTerritoryId) return;

        cachedTerritoryId = territoryId;
        cachedIsRaidOrTrial = false;
        cachedIsDungeon = false;

        var row = territorySheet?.GetRowOrDefault(territoryId);
        if (!row.HasValue) return;

        var intendedUse = row.Value.TerritoryIntendedUse.RowId;
        var cfc = row.Value.ContentFinderCondition.ValueNullable;
        var contentType = cfc.HasValue ? cfc.Value.ContentType.RowId : 0;

        cachedIsRaidOrTrial = intendedUse is 8 or 10 or 16 or 17 or 36
                              || contentType is 4 or 5 or 28 or 37;
        cachedIsDungeon = !cachedIsRaidOrTrial
                          && (intendedUse is 3 or 4 or 31 or 57 or 58
                              || contentType is 2 or 21 or 30);
    }

    private bool HasActiveBossEnemy(bool inDuty, bool inCombat, IBattleNpc? hostileTarget)
    {
        if (!inDuty) return false;
        if (hostileTarget != null && IsBossNpc(hostileTarget)) return true;
        if (!inCombat) return false;

        foreach (var obj in objectTable)
        {
            if (obj is IBattleNpc npc
                && npc.BattleNpcKind is BattleNpcSubKind.Combatant or BattleNpcSubKind.BNpcPart
                && npc.IsTargetable
                && npc.CurrentHp > 0
                && (npc.StatusFlags & StatusFlags.InCombat) != 0
                && IsBossNpc(npc))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsBossNpc(IBattleNpc npc)
    {
        var row = bnpcBaseSheet?.GetRowOrDefault(npc.BaseId);
        return row.HasValue && row.Value.Rank is 2 or 6;
    }

    private int ReadLowestGearConditionPercent()
    {
        var invMgr = InventoryManager.Instance();
        if (invMgr == null) return 100;

        var equipped = invMgr->GetInventoryContainer(InventoryType.EquippedItems);
        if (equipped == null || !equipped->IsLoaded) return 100;

        var lowest = 100;
        for (var i = 0; i < equipped->Size; i++)
        {
            var slot = equipped->GetInventorySlot(i);
            if (slot == null || slot->ItemId == 0) continue;

            if (itemSheet != null)
            {
                var row = itemSheet.GetRowOrDefault(slot->ItemId);
                if (row.HasValue && row.Value.ClassJobRepair.RowId == 0) continue;
            }

            var pct = Math.Clamp(slot->Condition / 300, 0, 100);
            if (pct < lowest) lowest = pct;
        }

        return lowest;
    }

    private void BuildMitigations(IPlayerCharacter player, IBattleNpc? boss)
    {
        mitScratch.Clear();
        mitDedupScratch.Clear();

        if (boss != null)
        {
            foreach (var st in boss.StatusList)
            {
                if (!BossMitDebuffIds.Contains(st.StatusId)) continue;
                var row = st.GameData.ValueNullable;
                if (!row.HasValue || row.Value.Icon == 0) continue;
                var name = row.Value.Name.ExtractText();
                if (mitDedupScratch.Add($"D:{name}"))
                {
                    mitScratch.Add(new ActiveMitigation(st.StatusId, row.Value.Icon, name, st.RemainingTime, IsBossDebuff: true));
                }
            }
        }

        foreach (var st in player.StatusList)
        {
            if (!PartyMitBuffIds.Contains(st.StatusId)) continue;
            var row = st.GameData.ValueNullable;
            if (!row.HasValue || row.Value.Icon == 0) continue;
            var name = row.Value.Name.ExtractText();
            if (mitDedupScratch.Add($"B:{name}"))
            {
                mitScratch.Add(new ActiveMitigation(st.StatusId, row.Value.Icon, name, st.RemainingTime, IsBossDebuff: false));
            }
        }
    }

    private void BuildParty(IPlayerCharacter localPlayer)
    {
        partyScratch.Clear();
        raiserScratch.Clear();
        var selfId = localPlayer.EntityId;

        if (partyList.Length == 0)
        {
            var selfJob = localPlayer.ClassJob.ValueNullable;
            var hpPct = localPlayer.MaxHp > 0 ? (localPlayer.CurrentHp * 100f / localPlayer.MaxHp) : 100f;
            partyScratch.Add(new PartyMemberState(
                EntityId: localPlayer.EntityId,
                GameObjectId: localPlayer.GameObjectId,
                Name: localPlayer.Name.TextValue,
                ClassJobId: localPlayer.ClassJob.RowId,
                Role: selfJob?.Role ?? 0,
                CurrentHp: localPlayer.CurrentHp,
                MaxHp: localPlayer.MaxHp,
                HpPercent: hpPct,
                ShieldPercentage: localPlayer.ShieldPercentage,
                Position: localPlayer.Position,
                IsLocalPlayer: true,
                IsDead: localPlayer.CurrentHp == 0,
                IsRaisable: false,
                HasPendingRaise: HasStatus(localPlayer.StatusList, RaisePendingStatusId, RaisePendingAltStatusId),
                RaisingByName: null,
                RaisingByJobId: 0,
                HasOwnCatalyze: HasOwnStatus(localPlayer.StatusList, CatalyzeStatusId, selfId),
                DispellableDebuffName: FindDispellableDebuff(localPlayer.StatusList)));
            return;
        }

        foreach (var m in partyList)
        {
            if (m.GameObject is IBattleChara bc && bc.IsCasting && ScholarAlertRules.IsRaiseSpell(bc.CastActionId))
            {
                raiserScratch[bc.CastTargetObjectId] = (m.Name.TextValue, m.ClassJob.RowId);
            }
        }

        foreach (var m in partyList)
        {
            var jobRow = m.ClassJob.ValueNullable;
            var bc = m.GameObject as IBattleChara;
            var statuses = bc?.StatusList ?? m.Statuses;
            var hpPct = m.MaxHP > 0 ? (m.CurrentHP * 100f / m.MaxHP) : 100f;
            var pos = m.GameObject?.Position ?? m.Position;
            var goId = m.GameObject?.GameObjectId ?? m.EntityId;

            string? raiserName = null;
            uint raiserJob = 0;
            if (m.CurrentHP == 0)
            {
                if (raiserScratch.TryGetValue(goId, out var r) || raiserScratch.TryGetValue(m.EntityId, out r))
                {
                    raiserName = r.Name;
                    raiserJob = r.JobId;
                }
            }

            var isDead = m.MaxHP > 0 && m.CurrentHP == 0;
            var isRaisable = isDead
                             && m.GameObject != null
                             && m.GameObject.IsTargetable
                             && MathF.Abs(pos.Y - localPlayer.Position.Y) < 20f;

            partyScratch.Add(new PartyMemberState(
                EntityId: m.EntityId,
                GameObjectId: goId,
                Name: m.Name.TextValue,
                ClassJobId: m.ClassJob.RowId,
                Role: jobRow?.Role ?? 0,
                CurrentHp: m.CurrentHP,
                MaxHp: m.MaxHP,
                HpPercent: hpPct,
                ShieldPercentage: bc?.ShieldPercentage ?? 0,
                Position: pos,
                IsLocalPlayer: m.EntityId == selfId,
                IsDead: isDead,
                IsRaisable: isRaisable,
                HasPendingRaise: HasStatus(statuses, RaisePendingStatusId, RaisePendingAltStatusId),
                RaisingByName: raiserName,
                RaisingByJobId: raiserJob,
                HasOwnCatalyze: HasOwnStatus(statuses, CatalyzeStatusId, selfId),
                DispellableDebuffName: FindDispellableDebuff(statuses)));
        }
    }

    private static bool HasStatus(Dalamud.Game.ClientState.Statuses.StatusList? statuses, uint id1, uint id2)
    {
        if (statuses == null) return false;
        foreach (var st in statuses)
        {
            if (st.StatusId == id1 || st.StatusId == id2) return true;
        }
        return false;
    }

    private static bool HasOwnStatus(Dalamud.Game.ClientState.Statuses.StatusList? statuses, uint id, uint selfId)
    {
        if (statuses == null) return false;
        foreach (var st in statuses)
        {
            if (st.StatusId == id && st.SourceId == selfId) return true;
        }
        return false;
    }

    private static string? FindDispellableDebuff(Dalamud.Game.ClientState.Statuses.StatusList? statuses)
    {
        if (statuses == null) return null;
        foreach (var st in statuses)
        {
            var data = st.GameData.ValueNullable;
            if (data.HasValue && data.Value.CanDispel)
            {
                var name = data.Value.Name.ExtractText();
                return string.IsNullOrWhiteSpace(name) ? "Debuff" : name;
            }
        }
        return null;
    }

    public void Dispose()
    {
        if (isSubscribed)
        {
            framework.Update -= OnFrameworkUpdate;
            isSubscribed = false;
        }

        activeGlows.Clear();
        activeBanners.Clear();
    }
}
