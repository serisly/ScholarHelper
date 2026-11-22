using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game;
using ScholarHelper.Core;
using ScholarHelper.UI;

namespace ScholarHelper.Rules;

public static class ScholarAlertRules
{
    public const uint LucidDreamingActionId = 7562;
    public const uint AetherflowActionId = 166;
    public const uint SummonEosActionId = 17215;
    public const uint BioActionId = 17864;
    public const uint Bio2ActionId = 17865;
    public const uint BiolysisActionId = 16540;
    public const uint WhisperingDawnActionId = 16537;
    public const uint ChainStratagemActionId = 7436;
    public const uint RecitationActionId = 16542;
    public const uint AetherpactActionId = 7437;
    public const uint AdloquiumActionId = 185;
    public const uint SuccorActionId = 186;
    public const uint SacredSoilActionId = 188;
    public const uint LustrateActionId = 189;
    public const uint IndomitabilityActionId = 3583;
    public const uint DeploymentTacticsActionId = 3585;
    public const uint ExpedientActionId = 25868;
    public const uint ConcitationActionId = 37013;
    public const uint EsunaActionId = 7568;
    public const uint HeelPetActionId = 2;

    public const uint RaiseWhmActionId = 125;
    public const uint ResurrectionActionId = 173;
    public const uint AscendActionId = 3603;
    public const uint VerraiseActionId = 7523;
    public const uint AngelWhisperActionId = 18317;
    public const uint EgeiroActionId = 24287;

    private static readonly Vector4 ColorGold = new(0.98f, 0.84f, 0.42f, 1f);
    private static readonly Vector4 ColorAmber = new(1.00f, 0.72f, 0.28f, 1f);
    private static readonly Vector4 ColorCoral = new(1.00f, 0.44f, 0.40f, 1f);
    private static readonly Vector4 ColorCyan = new(0.48f, 0.88f, 1.00f, 1f);
    private static readonly Vector4 ColorMint = new(0.54f, 0.92f, 0.66f, 1f);
    private static readonly Vector4 ColorWhite = new(0.95f, 0.96f, 0.98f, 1f);
    private static readonly Vector4 ColorMuted = new(0.72f, 0.75f, 0.80f, 1f);

    public const string RaiseTrackerRuleId = "raise_tracker";

    public static IReadOnlyList<AlertRule> CreateAll() =>
    [
        new RaiseTrackerRule(),
        new TankLowHpRule(),

        new EsunaRule(),
        new FocusTargetRule(),
        new RaidFoodRule(),

        new BioDotRule(),
        new LucidDreamingRule(),
        new WhisperingDawnRule(),
        new AetherflowRule(),
        new CritloDeployRule(),
        new ChainStratagemRule(),
        new FeyGaugeCapRule(),
        new RecitationRule(),

        new MissingFairyRule(),
        new FairyLeashRule(),
    ];

    public static bool IsRaiseSpell(uint actionId)
        => actionId is RaiseWhmActionId
            or ResurrectionActionId
            or AscendActionId
            or VerraiseActionId
            or AngelWhisperActionId
            or EgeiroActionId;

    private static uint GetRaiseActionForJob(uint classJobId) => classJobId switch
    {
        6 or 24 => RaiseWhmActionId,
        26 or 27 or 28 => ResurrectionActionId,
        33 => AscendActionId,
        35 => VerraiseActionId,
        36 => AngelWhisperActionId,
        40 => EgeiroActionId,
        _ => 0,
    };

    public static bool CanJobRaise(uint classJobId, byte level) => classJobId switch
    {
        6 or 24 or 26 or 27 or 28 or 33 or 40 => level >= 12,
        35 => level >= 64,
        36 => true,
        _ => false,
    };

    private sealed class RaiseTrackerRule : AlertRule
    {
        public override string Id => RaiseTrackerRuleId;
        public override string DisplayName => "Raise Tracker";
        public override string Tooltip => "Shows dead party members, who is raising them, and pending raises.";
        public override AlertCategory Category => AlertCategory.Party;
        public override byte MinLevel => 12;
        public override bool ScholarOnly => false;
        public override AlertCapabilities Capabilities => AlertCapabilities.Glow | AlertCapabilities.Banner | AlertCapabilities.Sound;

        public override AlertRuleConfig CreateDefaultConfig() => new()
        {
            Enabled = true, EnableGlow = true, EnableBanner = true, SoundId = 6, Flag = false,
        };

        public override bool DrawOptions(AlertRuleConfig cfg)
        {
            var tags = cfg.Flag;
            if (ImGui.Checkbox("Party List tags##rt", ref tags)) { cfg.Flag = tags; return true; }
            return false;
        }

        public override AlertEvaluation Evaluate(FrameState state, AlertRuleConfig cfg)
        {
            if (!CanJobRaise(state.ClassJobId, state.Level) || state.Party.Count <= 1)
            {
                return AlertEvaluation.Inactive;
            }

            List<AlertBannerLine>? banners = null;
            var hasUncoveredDead = false;
            var edgeKey = string.Empty;

            foreach (var m in state.Party)
            {
                if (!m.IsDead || m.IsLocalPlayer) continue;
                banners ??= new List<AlertBannerLine>(4);

                if (m.RaisingByName != null)
                {
                    edgeKey += $"R:{m.EntityId}:{m.RaisingByName};";
                    banners.Add(new AlertBannerLine(Id, 12, ColorMint,
                    [
                        BannerSegment.Job(m.RaisingByJobId),
                        BannerSegment.Plain(m.RaisingByName, ColorWhite),
                        BannerSegment.Plain("  ->  Raising  ", ColorMint),
                        BannerSegment.Job(m.ClassJobId),
                        BannerSegment.Plain(m.Name, ColorWhite),
                    ]));
                }
                else if (m.HasPendingRaise)
                {
                    edgeKey += $"P:{m.EntityId};";
                    banners.Add(new AlertBannerLine(Id, 14, ColorGold,
                    [
                        BannerSegment.Job(m.ClassJobId),
                        BannerSegment.Plain(m.Name, ColorWhite),
                        BannerSegment.Plain(": Raise pending", ColorGold),
                    ]));
                }
                else
                {
                    hasUncoveredDead = true;
                    edgeKey += $"D:{m.EntityId};";
                    banners.Add(new AlertBannerLine(Id, 10, ColorCoral,
                    [
                        BannerSegment.Job(m.ClassJobId),
                        BannerSegment.Plain(m.Name, ColorWhite),
                        BannerSegment.Plain(": DEAD (needs Raise)", ColorCoral),
                    ]));
                }
            }

            if (banners == null) return AlertEvaluation.Inactive;

            HotbarGlowTarget[]? glows = null;
            if (hasUncoveredDead)
            {
                var raiseId = GetRaiseActionForJob(state.ClassJobId);
                if (raiseId != 0) glows = [new HotbarGlowTarget(ActionType.Action, raiseId)];
            }

            return new AlertEvaluation(true, glows, banners, edgeKey);
        }
    }

    private sealed class TankLowHpRule : AlertRule
    {
        public override string Id => "tank_low_hp";
        public override string DisplayName => "Tank Low HP";
        public override string Tooltip => "Tank drops below the HP threshold in combat.";
        public override AlertCategory Category => AlertCategory.Party;
        public override byte MinLevel => 1;
        public override bool ScholarOnly => false;
        public override AlertCapabilities Capabilities => AlertCapabilities.Glow | AlertCapabilities.Banner | AlertCapabilities.Sound;

        public override AlertRuleConfig CreateDefaultConfig() => new()
        {
            Enabled = true, EnableGlow = false, EnableBanner = true, SoundId = 5, Threshold = 45,
        };

        public override bool DrawOptions(AlertRuleConfig cfg)
        {
            var v = cfg.Threshold;
            if (UiControls.SliderIntOption("##hp", "HP below", ref v, 15, 80, $"{v}%", step: 5))
            {
                cfg.Threshold = v;
                return true;
            }
            return false;
        }

        public override AlertEvaluation Evaluate(FrameState state, AlertRuleConfig cfg)
        {
            if (!state.InCombat || !state.IsHealer) return AlertEvaluation.Inactive;

            var threshold = Math.Clamp(cfg.Threshold, 15, 80);
            List<AlertBannerLine>? banners = null;
            var edgeKey = string.Empty;

            foreach (var m in state.Party)
            {
                if (m.Role != FrameState.RoleTank || m.IsDead || m.MaxHp == 0 || m.HpPercent > threshold) continue;
                banners ??= new List<AlertBannerLine>(2);
                edgeKey += $"{m.EntityId};";
                banners.Add(new AlertBannerLine(Id, 15, ColorCoral,
                [
                    BannerSegment.Job(m.ClassJobId),
                    BannerSegment.Plain(m.Name, ColorWhite),
                    BannerSegment.Plain($": {(int)MathF.Round(m.HpPercent)}% HP", ColorCoral),
                ]));
            }

            if (banners == null) return AlertEvaluation.Inactive;

            HotbarGlowTarget[]? glows = null;
            if (state.IsScholar)
            {
                if (state.Level >= 45 && state.AetherflowStacks > 0)
                    glows = [new HotbarGlowTarget(ActionType.Action, LustrateActionId)];
                else if (state.Level >= 30)
                    glows = [new HotbarGlowTarget(ActionType.Action, AdloquiumActionId)];
            }

            return new AlertEvaluation(true, glows, banners, edgeKey);
        }
    }

    private sealed class EsunaRule : AlertRule
    {
        public override string Id => "esuna";
        public override string DisplayName => "Esuna";
        public override string Tooltip => "Party member has a dispellable debuff (Lv10+).";
        public override AlertCategory Category => AlertCategory.Reminders;
        public override byte MinLevel => 10;
        public override bool ScholarOnly => false;
        public override AlertCapabilities Capabilities => AlertCapabilities.Glow | AlertCapabilities.Banner | AlertCapabilities.Sound;

        public override AlertRuleConfig CreateDefaultConfig() => new()
        {
            Enabled = true, EnableGlow = true, EnableBanner = true, SoundId = 7,
        };

        public override AlertEvaluation Evaluate(FrameState state, AlertRuleConfig cfg)
        {
            if (!state.IsHealer) return AlertEvaluation.Inactive;

            List<AlertBannerLine>? banners = null;
            var edgeKey = string.Empty;
            foreach (var m in state.Party)
            {
                if (m.IsDead || string.IsNullOrEmpty(m.DispellableDebuffName)) continue;
                banners ??= new List<AlertBannerLine>(3);
                edgeKey += $"{m.EntityId}:{m.DispellableDebuffName};";
                banners.Add(new AlertBannerLine(Id, 20, ColorCyan,
                [
                    BannerSegment.Plain("Esuna: ", ColorCyan),
                    BannerSegment.Job(m.ClassJobId),
                    BannerSegment.Plain(m.Name, ColorWhite),
                    BannerSegment.Plain($"  ({m.DispellableDebuffName})", ColorMuted),
                ]));
            }

            return banners == null
                ? AlertEvaluation.Inactive
                : new AlertEvaluation(true, [new HotbarGlowTarget(ActionType.Action, EsunaActionId)], banners, edgeKey);
        }
    }

    private sealed class WhisperingDawnRule : AlertRule
    {
        public override string Id => "whispering_dawn";
        public override string DisplayName => "Whispering Dawn";
        public override string Tooltip => "Off cooldown in combat with injured party members (Lv20+).";
        public override AlertCategory Category => AlertCategory.Cooldowns;
        public override byte MinLevel => 20;
        public override AlertCapabilities Capabilities => AlertCapabilities.Glow | AlertCapabilities.Sound;

        public override AlertRuleConfig CreateDefaultConfig() => new()
        {
            Enabled = true, EnableGlow = true, EnableBanner = false, SoundId = 1, Threshold = 85, MinCount = 2,
        };

        public override bool DrawOptions(AlertRuleConfig cfg)
        {
            var changed = false;
            var hp = cfg.Threshold;
            if (UiControls.SliderIntOption("##wdhp", "HP below", ref hp, 30, 98, $"{hp}%", step: 5))
            {
                cfg.Threshold = hp;
                changed = true;
            }

            ImGui.SameLine(0f, 12f);
            var cnt = cfg.MinCount;
            if (UiControls.CompactStepperInt("wdcnt", "Min", ref cnt, 1, 8, "+"))
            {
                cfg.MinCount = cnt;
                changed = true;
            }
            return changed;
        }

        public override AlertEvaluation Evaluate(FrameState state, AlertRuleConfig cfg)
        {
            if (!state.InCombat || state.IsDissipated || (!state.HasActivePet && state.SeraphTimer <= 0))
                return AlertEvaluation.Inactive;
            if (!state.IsOffCooldown(WhisperingDawnActionId))
                return AlertEvaluation.Inactive;

            var hpPct = Math.Clamp(cfg.Threshold, 30, 98);
            var minInjured = state.Party.Count == 1 ? 1 : Math.Clamp(cfg.MinCount, 1, 8);
            var injured = 0;
            foreach (var m in state.Party)
            {
                if (!m.IsDead && m.MaxHp > 0 && m.HpPercent <= hpPct) injured++;
            }

            if (injured < minInjured) return AlertEvaluation.Inactive;

            return AlertEvaluation.Active(new HotbarGlowTarget(ActionType.Action, WhisperingDawnActionId));
        }
    }

    private sealed class FocusTargetRule : AlertRule
    {
        public override string Id => "focus_target";
        public override string DisplayName => "Focus Target";
        public override string Tooltip => "Reminds you in raids, trials, and boss fights when no Focus Target is set.";
        public override AlertCategory Category => AlertCategory.Reminders;
        public override byte MinLevel => 1;
        public override bool ScholarOnly => false;
        public override AlertCapabilities Capabilities => AlertCapabilities.Banner | AlertCapabilities.Sound;

        public override AlertRuleConfig CreateDefaultConfig() => new()
        {
            Enabled = true, EnableGlow = false, EnableBanner = true, SoundId = 9,
        };

        public override AlertEvaluation Evaluate(FrameState state, AlertRuleConfig cfg)
        {
            if (!state.IsBossEncounter || state.IsPetSuppressed || state.HasFocusTarget)
                return AlertEvaluation.Inactive;

            return AlertEvaluation.Active(
                banner: new AlertBannerLine(Id, 60, ColorGold, [BannerSegment.Plain("No Focus Target set", ColorGold)]));
        }
    }

    private sealed class RaidFoodRule : AlertRule
    {
        public override string Id => "raid_food";
        public override string DisplayName => "Raid Food";
        public override string Tooltip => "Pre-pull check in duties when Well Fed is missing or expiring soon.";
        public override AlertCategory Category => AlertCategory.Reminders;
        public override byte MinLevel => 1;
        public override bool ScholarOnly => false;
        public override AlertCapabilities Capabilities => AlertCapabilities.Banner | AlertCapabilities.Sound;

        public override AlertRuleConfig CreateDefaultConfig() => new()
        {
            Enabled = true, EnableGlow = false, EnableBanner = true, SoundId = 1, Threshold = 5,
        };

        public override bool DrawOptions(AlertRuleConfig cfg)
        {
            var m = cfg.Threshold;
            if (UiControls.SliderIntOption("##food", "Under", ref m, 1, 15, $"{m} min"))
            {
                cfg.Threshold = m;
                return true;
            }
            return false;
        }

        public override AlertEvaluation Evaluate(FrameState state, AlertRuleConfig cfg)
        {
            if (!state.InDuty || state.InCombat || state.IsPetSuppressed) return AlertEvaluation.Inactive;
            var warnMin = Math.Clamp(cfg.Threshold, 1, 15);
            if (state.HasFoodBuff && state.FoodRemainingMinutes >= warnMin) return AlertEvaluation.Inactive;

            var text = !state.HasFoodBuff
                ? "Raid food missing"
                : $"Raid food expiring ({Math.Max(1, (int)MathF.Ceiling(state.FoodRemainingMinutes))}m)";
            return AlertEvaluation.Active(banner: new AlertBannerLine(Id, 65, ColorAmber, [BannerSegment.Plain(text, ColorAmber)]));
        }
    }

    private sealed class LucidDreamingRule : AlertRule
    {
        public override string Id => "lucid";
        public override string DisplayName => "Lucid Dreaming";
        public override string Tooltip => "Off cooldown and MP below threshold (Lv14+).";
        public override AlertCategory Category => AlertCategory.Cooldowns;
        public override byte MinLevel => 14;
        public override bool AllowAllJobsOption => true;
        public override AlertCapabilities Capabilities => AlertCapabilities.Glow | AlertCapabilities.Sound;

        public override AlertRuleConfig CreateDefaultConfig() => new()
        {
            Enabled = true, EnableGlow = true, SoundId = 1, Threshold = 7000, CombatOnly = true, AllJobs = true,
        };

        public override bool DrawOptions(AlertRuleConfig cfg)
        {
            var changed = false;
            var mp = cfg.Threshold;
            if (UiControls.SliderIntOption("##mp", "MP below", ref mp, 1000, 9900, $"{mp}", step: 200))
            {
                cfg.Threshold = mp;
                changed = true;
            }

            ImGui.SameLine(0f, 12f);
            var co = cfg.CombatOnly;
            if (ImGui.Checkbox("Combat##lc", ref co)) { cfg.CombatOnly = co; changed = true; }
            ImGui.SameLine(0f, 10f);
            var aj = cfg.AllJobs;
            if (ImGui.Checkbox("All jobs##la", ref aj)) { cfg.AllJobs = aj; changed = true; }
            return changed;
        }

        public override AlertEvaluation Evaluate(FrameState state, AlertRuleConfig cfg)
        {
            if (cfg.CombatOnly && !state.InCombat) return AlertEvaluation.Inactive;
            if (state.CurrentMp >= (uint)Math.Clamp(cfg.Threshold, 1000, 9900) || !state.IsOffCooldown(LucidDreamingActionId))
                return AlertEvaluation.Inactive;

            return AlertEvaluation.Active(new HotbarGlowTarget(ActionType.Action, LucidDreamingActionId));
        }
    }

    private sealed class BioDotRule : AlertRule
    {
        public override string Id => "bio";
        public override string DisplayName => "Bio / Biolysis";
        public override string Tooltip => "Own DoT missing or expiring on your hostile target in combat (Lv2/26/72+).";
        public override AlertCategory Category => AlertCategory.Cooldowns;
        public override byte MinLevel => 2;
        public override AlertCapabilities Capabilities => AlertCapabilities.Glow | AlertCapabilities.Sound;

        public override AlertRuleConfig CreateDefaultConfig() => new()
        {
            Enabled = true, EnableGlow = true, SoundId = 16, FloatThreshold = 3.0f,
        };

        public override bool DrawOptions(AlertRuleConfig cfg)
        {
            var sec = cfg.FloatThreshold;
            if (UiControls.SliderFloatOption("##bio", "Refresh", ref sec, 0.5f, 10.0f, $"{sec:F1}s", step: 0.5f))
            {
                cfg.FloatThreshold = sec;
                return true;
            }
            return false;
        }

        public override AlertEvaluation Evaluate(FrameState state, AlertRuleConfig cfg)
        {
            if (!state.InCombat || !state.HasHostileTarget) return AlertEvaluation.Inactive;
            if (state.OwnBioRemainingSeconds > Math.Clamp(cfg.FloatThreshold, 0.5f, 10.0f)) return AlertEvaluation.Inactive;

            var glows = new List<HotbarGlowTarget>(3) { new(ActionType.Action, BioActionId) };
            if (state.Level >= 26) glows.Add(new(ActionType.Action, Bio2ActionId));
            if (state.Level >= 72) glows.Add(new(ActionType.Action, BiolysisActionId));

            return new AlertEvaluation(true, glows);
        }
    }

    private sealed class AetherflowRule : AlertRule
    {
        public override string Id => "aetherflow";
        public override string DisplayName => "Aetherflow";
        public override string Tooltip => "Off cooldown in combat (Lv45+).";
        public override AlertCategory Category => AlertCategory.Cooldowns;
        public override byte MinLevel => 45;
        public override AlertCapabilities Capabilities => AlertCapabilities.Glow | AlertCapabilities.Sound;

        public override AlertRuleConfig CreateDefaultConfig() => new()
        {
            Enabled = true, EnableGlow = true, SoundId = 2, Flag = false,
        };

        public override bool DrawOptions(AlertRuleConfig cfg)
        {
            var zero = cfg.Flag;
            if (ImGui.Checkbox("Only at 0 stacks##af", ref zero)) { cfg.Flag = zero; return true; }
            return false;
        }

        public override AlertEvaluation Evaluate(FrameState state, AlertRuleConfig cfg)
        {
            if (!state.InCombat || !state.IsOffCooldown(AetherflowActionId)) return AlertEvaluation.Inactive;
            if (cfg.Flag && state.AetherflowStacks > 0) return AlertEvaluation.Inactive;

            return AlertEvaluation.Active(new HotbarGlowTarget(ActionType.Action, AetherflowActionId));
        }
    }

    private sealed class CritloDeployRule : AlertRule
    {
        public override string Id => "critlo_spread";
        public override string DisplayName => "Critlo Deploy";
        public override string Tooltip => "Catalyze crit shield active while Deployment Tactics is ready (Lv56+).";
        public override AlertCategory Category => AlertCategory.Cooldowns;
        public override byte MinLevel => 56;
        public override AlertCapabilities Capabilities => AlertCapabilities.Glow | AlertCapabilities.Sound;

        public override AlertRuleConfig CreateDefaultConfig() => new()
        {
            Enabled = true, EnableGlow = true, SoundId = 6,
        };

        public override AlertEvaluation Evaluate(FrameState state, AlertRuleConfig cfg)
        {
            if (!state.IsOffCooldown(DeploymentTacticsActionId)) return AlertEvaluation.Inactive;

            foreach (var m in state.Party)
            {
                if (!m.IsDead && m.HasOwnCatalyze)
                {
                    return AlertEvaluation.Active(
                        new HotbarGlowTarget(ActionType.Action, DeploymentTacticsActionId),
                        edgeKey: $"C:{m.EntityId}");
                }
            }

            return AlertEvaluation.Inactive;
        }
    }

    private sealed class ChainStratagemRule : AlertRule
    {
        public override string Id => "chain_stratagem";
        public override string DisplayName => "Chain Stratagem";
        public override string Tooltip => "Off cooldown in combat (Lv66+).";
        public override AlertCategory Category => AlertCategory.Cooldowns;
        public override byte MinLevel => 66;
        public override AlertCapabilities Capabilities => AlertCapabilities.Glow | AlertCapabilities.Sound;

        public override AlertRuleConfig CreateDefaultConfig() => new()
        {
            Enabled = true, EnableGlow = true, SoundId = 4,
        };

        public override AlertEvaluation Evaluate(FrameState state, AlertRuleConfig cfg)
        {
            if (!state.InCombat || !state.IsOffCooldown(ChainStratagemActionId)) return AlertEvaluation.Inactive;
            return AlertEvaluation.Active(new HotbarGlowTarget(ActionType.Action, ChainStratagemActionId));
        }
    }

    private sealed class FeyGaugeCapRule : AlertRule
    {
        public override string Id => "fey_gauge_cap";
        public override string DisplayName => "Fey Gauge Cap";
        public override string Tooltip => "Fey Gauge reaches threshold in combat (Lv70+).";
        public override AlertCategory Category => AlertCategory.Cooldowns;
        public override byte MinLevel => 70;
        public override AlertCapabilities Capabilities => AlertCapabilities.Glow | AlertCapabilities.Sound;

        public override AlertRuleConfig CreateDefaultConfig() => new()
        {
            Enabled = true, EnableGlow = true, SoundId = 9, Threshold = 100,
        };

        public override bool DrawOptions(AlertRuleConfig cfg)
        {
            var g = cfg.Threshold;
            if (UiControls.SliderIntOption("##fg", "At or over", ref g, 50, 100, $"{g}", step: 10))
            {
                cfg.Threshold = g;
                return true;
            }
            return false;
        }

        public override AlertEvaluation Evaluate(FrameState state, AlertRuleConfig cfg)
        {
            if (!state.InCombat || !state.HasActivePet || state.IsDissipated) return AlertEvaluation.Inactive;
            if (state.FairyGauge < Math.Clamp(cfg.Threshold, 50, 100)) return AlertEvaluation.Inactive;

            return AlertEvaluation.Active(new HotbarGlowTarget(ActionType.Action, AetherpactActionId));
        }
    }

    private sealed class RecitationRule : AlertRule
    {
        public override string Id => "recitation";
        public override string DisplayName => "Recitation";
        public override string Tooltip => "Off cooldown in combat (Lv74+).";
        public override AlertCategory Category => AlertCategory.Cooldowns;
        public override byte MinLevel => 74;
        public override AlertCapabilities Capabilities => AlertCapabilities.Glow | AlertCapabilities.Sound;

        public override AlertRuleConfig CreateDefaultConfig() => new()
        {
            Enabled = true, EnableGlow = true, SoundId = 8,
        };

        public override AlertEvaluation Evaluate(FrameState state, AlertRuleConfig cfg)
        {
            if (!state.InCombat || !state.IsOffCooldown(RecitationActionId)) return AlertEvaluation.Inactive;
            return AlertEvaluation.Active(new HotbarGlowTarget(ActionType.Action, RecitationActionId));
        }
    }

    private sealed class MissingFairyRule : AlertRule
    {
        public override string Id => "missing_fairy";
        public override string DisplayName => "Missing Fairy";
        public override string Tooltip => "No fairy active outside mounts, cutscenes, or Dissipation (Lv30+).";
        public override AlertCategory Category => AlertCategory.Fairy;
        public override byte MinLevel => 30;
        public override AlertCapabilities Capabilities => AlertCapabilities.Glow | AlertCapabilities.Banner | AlertCapabilities.Sound;

        public override AlertRuleConfig CreateDefaultConfig() => new()
        {
            Enabled = true, EnableGlow = true, SoundId = 3, Flag = false,
        };

        public override bool DrawOptions(AlertRuleConfig cfg)
        {
            var d = cfg.Flag;
            if (ImGui.Checkbox("Duties & combat only##mf", ref d)) { cfg.Flag = d; return true; }
            return false;
        }

        public override AlertEvaluation Evaluate(FrameState state, AlertRuleConfig cfg)
        {
            if (cfg.Flag && !state.InCombat && !state.InDuty) return AlertEvaluation.Inactive;
            if (state.IsPetSuppressed || state.HasActivePet || state.IsFairyDismissed) return AlertEvaluation.Inactive;
            if (state.IsCasting && state.CastActionId == SummonEosActionId) return AlertEvaluation.Inactive;

            return AlertEvaluation.Active(
                new HotbarGlowTarget(ActionType.Action, SummonEosActionId),
                new AlertBannerLine(Id, 25, ColorAmber, [BannerSegment.Plain("Fairy missing (Summon Eos)", ColorAmber)]));
        }
    }

    private sealed class FairyLeashRule : AlertRule
    {
        public override string Id => "fairy_leash";
        public override string DisplayName => "Fairy Leash";
        public override string Tooltip => "Eos is beyond your leash distance (Lv30+).";
        public override AlertCategory Category => AlertCategory.Fairy;
        public override byte MinLevel => 30;
        public override AlertCapabilities Capabilities => AlertCapabilities.Glow | AlertCapabilities.Banner | AlertCapabilities.Sound;

        public override AlertRuleConfig CreateDefaultConfig() => new()
        {
            Enabled = true, EnableGlow = true, EnableBanner = true, SoundId = 11, Threshold = 22,
        };

        public override bool DrawOptions(AlertRuleConfig cfg)
        {
            var y = cfg.Threshold;
            if (UiControls.SliderIntOption("##fl", "Over", ref y, 12, 29, $"{y}y"))
            {
                cfg.Threshold = y;
                return true;
            }
            return false;
        }

        public override AlertEvaluation Evaluate(FrameState state, AlertRuleConfig cfg)
        {
            if (state.IsPetSuppressed || state.IsDissipated || !state.HasActivePet || state.FairyDistanceYalms < 0f)
                return AlertEvaluation.Inactive;
            if (state.FairyDistanceYalms < Math.Clamp(cfg.Threshold, 12, 29))
                return AlertEvaluation.Inactive;

            return AlertEvaluation.Active(
                new HotbarGlowTarget(ActionType.PetAction, HeelPetActionId),
                    new AlertBannerLine(Id, 28, ColorAmber, [BannerSegment.Plain($"Eos is {state.FairyDistanceYalms:F1}y away (Heel)", ColorAmber)]));
        }
    }
}
