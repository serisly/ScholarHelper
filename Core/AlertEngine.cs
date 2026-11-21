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
using ScholarHelper.Rules;

namespace ScholarHelper.Core;

public sealed unsafe class AlertEngine : IDisposable
{
    private const uint WellFedStatusId = 48;
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

    private static readonly Dictionary<uint, (string Name, uint IconId)> BossMitDebuffs = new()
    {
        [1193] = ("Reprisal", 213901),
        [1203] = ("Addle", 213917),
        [1195] = ("Feint", 213904),
        [860]  = ("Dismantle", 213009),
    };

    private static readonly Dictionary<uint, (string Name, uint IconId)> PartyMitBuffs = new()
    {
        [299]  = ("Soil", 212803),
        [1944] = ("Soil", 212802),
        [2638] = ("Soil", 212803),
        [317]  = ("Illum", 212828),
        [1875] = ("Seraphic", 212847),
        [2711] = ("Expedient", 212816),
        [2618] = ("Kerachole", 212964),
        [3003] = ("Holos", 212971),
        [1873] = ("Temperance", 212633),
        [1872] = ("Temperance", 212634),
        [849]  = ("CU", 213226),
        [1839] = ("Heart", 213609),
        [1894] = ("Missionary", 213122),
        [1175] = ("Passage", 212512),
        [1934] = ("Troubadour", 212615),
        [1951] = ("Tactician", 213021),
        [1826] = ("Samba", 213715),
        [2707] = ("Magick", 213408),
    };

    private readonly IFramework framework;
    private readonly IObjectTable objectTable;
    private readonly IPartyList partyList;
    private readonly IBuddyList buddyList;
    private readonly IJobGauges jobGauges;
    private readonly ITargetManager targetManager;
    private readonly ICondition condition;
    private readonly Configuration config;
    private readonly IPluginLog log;
    private readonly IReadOnlyList<AlertRule> rules;

    private readonly HashSet<HotbarGlowTarget> activeGlows = new();
    private readonly List<AlertBannerLine> activeBanners = new();
    private readonly Dictionary<string, bool> wasActive = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> lastEdgeKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> lastSoundUtc = new(StringComparer.Ordinal);
    private readonly List<PartyMemberState> partyScratch = new(8);
    private readonly List<ActiveMitigation> mitScratch = new(12);
    private readonly HashSet<string> mitDedupScratch = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, (string Name, uint JobId)> raiserScratch = new(4);

    private bool isSubscribed;
    private bool hasLoggedError;

    public IReadOnlyList<AlertRule> Rules => rules;
    public IReadOnlyList<AlertBannerLine> ActiveBanners => activeBanners;
    public FrameState State { get; private set; } = FrameState.Empty;

    public AlertEngine(
        IFramework framework,
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
        this.objectTable = objectTable;
        this.partyList = partyList;
        this.buddyList = buddyList;
        this.jobGauges = jobGauges;
        this.targetManager = targetManager;
        this.condition = condition;
        this.config = config;
        this.rules = rules;
        this.log = log;

        EnsureRuleConfigs();
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

        try
        {
            var state = BuildFrameState();
            State = state;

            if (!state.IsLoggedIn)
            {
                wasActive.Clear();
                lastEdgeKey.Clear();
                return;
            }

            if (!config.Enabled)
            {
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
                    wasActive[rule.Id] = false;
                    lastEdgeKey[rule.Id] = null;
                    continue;
                }

                var eval = rule.Evaluate(state, cfg);
                if (!eval.IsActive)
                {
                    wasActive[rule.Id] = false;
                    lastEdgeKey[rule.Id] = null;
                    continue;
                }

                if ((rule.Capabilities & AlertCapabilities.Glow) != 0 && cfg.EnableGlow && eval.Glows != null)
                {
                    foreach (var g in eval.Glows)
                    {
                        activeGlows.Add(g);
                    }
                }

                if ((rule.Capabilities & AlertCapabilities.Banner) != 0 && cfg.EnableBanner && eval.Banners != null)
                {
                    activeBanners.AddRange(eval.Banners);
                }

                var prevActive = wasActive.GetValueOrDefault(rule.Id, false);
                var prevKey = lastEdgeKey.GetValueOrDefault(rule.Id, null);
                var keyChanged = eval.EdgeKey != null && !string.Equals(eval.EdgeKey, prevKey, StringComparison.Ordinal);

                if ((rule.Capabilities & AlertCapabilities.Sound) != 0 && cfg.EnableSound && (!prevActive || keyChanged))
                {
                    var cd = Math.Clamp(cfg.SoundCooldownSeconds, 1f, 60f);
                    var last = lastSoundUtc.GetValueOrDefault(rule.Id, DateTime.MinValue);
                    if ((now - last).TotalSeconds >= cd)
                    {
                        SoundEffects.Play(cfg.SoundId);
                        lastSoundUtc[rule.Id] = now;
                    }
                }

                wasActive[rule.Id] = true;
                lastEdgeKey[rule.Id] = eval.EdgeKey;
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

        IBattleNpc? hostileNpc = null;
        var bioRemaining = 0f;
        if (player.TargetObject is IBattleNpc npc
            && npc.BattleNpcKind is BattleNpcSubKind.Combatant or BattleNpcSubKind.BNpcPart
            && npc.IsTargetable
            && npc.CurrentHp > 0)
        {
            hostileNpc = npc;
            var selfId = player.EntityId;
            foreach (var st in npc.StatusList)
            {
                if (st.SourceId == selfId
                    && st.StatusId is BioStatusId or Bio2StatusId or BiolysisStatusId or BiolysisAltStatusId)
                {
                    bioRemaining = st.RemainingTime;
                    break;
                }
            }
        }

        var focusObj = targetManager.FocusTarget;
        BuildMitigations(player, hostileNpc, focusObj as IBattleNpc);
        BuildParty(player);

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
            InCombat = condition[ConditionFlag.InCombat],
            InDuty = condition[ConditionFlag.BoundByDuty]
                     || condition[ConditionFlag.BoundByDuty56]
                     || condition[ConditionFlag.BoundByDuty95],
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

    private void BuildMitigations(IPlayerCharacter player, IBattleNpc? hostileTarget, IBattleNpc? focusTarget)
    {
        mitScratch.Clear();
        mitDedupScratch.Clear();

        var boss = hostileTarget
                   ?? (focusTarget != null
                       && focusTarget.BattleNpcKind is BattleNpcSubKind.Combatant or BattleNpcSubKind.BNpcPart
                       && focusTarget.CurrentHp > 0
                           ? focusTarget
                           : null);
        if (boss != null)
        {
            foreach (var st in boss.StatusList)
            {
                if (BossMitDebuffs.TryGetValue(st.StatusId, out var info) && mitDedupScratch.Add($"D:{info.Name}"))
                {
                    mitScratch.Add(new ActiveMitigation(st.StatusId, info.IconId, info.Name, st.RemainingTime, IsBossDebuff: true));
                }
            }
        }

        foreach (var st in player.StatusList)
        {
            if (PartyMitBuffs.TryGetValue(st.StatusId, out var info) && mitDedupScratch.Add($"B:{info.Name}"))
            {
                mitScratch.Add(new ActiveMitigation(st.StatusId, info.IconId, info.Name, st.RemainingTime, IsBossDebuff: false));
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
                raiserScratch[(uint)bc.CastTargetObjectId] = (m.Name.TextValue, m.ClassJob.RowId);
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
                if (raiserScratch.TryGetValue(m.EntityId, out var r) || raiserScratch.TryGetValue((uint)goId, out r))
                {
                    raiserName = r.Name;
                    raiserJob = r.JobId;
                }
            }

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
                IsDead: m.CurrentHP == 0,
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
