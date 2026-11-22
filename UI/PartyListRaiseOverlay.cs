using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using ScholarHelper.Core;
using ScholarHelper.Rules;

namespace ScholarHelper.UI;

public sealed class PartyListRaiseOverlay
{
    private static readonly uint BgDark = ImGui.ColorConvertFloat4ToU32(new Vector4(0.07f, 0.08f, 0.10f, 0.88f));
    private static readonly uint ColCoral = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.44f, 0.40f, 1f));
    private static readonly uint ColMint = ImGui.ColorConvertFloat4ToU32(new Vector4(0.54f, 0.92f, 0.66f, 1f));
    private static readonly uint ColGold = ImGui.ColorConvertFloat4ToU32(new Vector4(0.98f, 0.84f, 0.42f, 1f));

    private readonly IGameGui gameGui;
    private readonly AlertEngine alertEngine;
    private readonly Configuration config;
    private readonly List<PartyRowBounds> rowScratch = new(8);

    public PartyListRaiseOverlay(IGameGui gameGui, AlertEngine alertEngine, Configuration config)
    {
        this.gameGui = gameGui;
        this.alertEngine = alertEngine;
        this.config = config;
    }

    public void Draw()
    {
        if (!config.Enabled) return;
        if (!config.RuleConfigs.TryGetValue(ScholarAlertRules.RaiseTrackerRuleId, out var raiseCfg) || !raiseCfg.Enabled)
        {
            return;
        }

        var showTags = raiseCfg.Flag;
        var showRowGlow = raiseCfg.EnableGlow;
        if (!showTags && !showRowGlow) return;

        var state = alertEngine.State;
        if (!state.IsLoggedIn
            || !state.IsAlive
            || state.Party.Count <= 1
            || !ScholarAlertRules.CanJobRaise(state.ClassJobId, state.Level))
        {
            return;
        }

        if (!PartyListGeometry.TryGetPartyBounds(gameGui, out _, out _, rowScratch) || rowScratch.Count == 0) return;

        var vp = ImGui.GetMainViewport();
        var draw = ImGui.GetForegroundDrawList(vp);
        var pulse = 0.65f + (0.35f * MathF.Sin((float)ImGui.GetTime() * 6f));
        var rowGlowBorder = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.42f, 0.36f, pulse));
        var rowGlowFill = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.36f, 0.30f, 0.14f * pulse));

        foreach (var row in rowScratch)
        {
            if (!PartyListGeometry.TryMatchMember(state.Party, row, out var m) || !m.IsDead || m.IsLocalPlayer) continue;

            var min = new Vector2(vp.Pos.X + row.Min.X, vp.Pos.Y + row.Min.Y);
            var max = new Vector2(vp.Pos.X + row.Max.X, vp.Pos.Y + row.Max.Y);

            if (m.RaisingByName != null)
            {
                if (showTags) PartyListGeometry.DrawRowPill(draw, max.X + 6f, (min.Y + max.Y) * 0.5f, "RAISING", BgDark, ColMint);
            }
            else if (m.HasPendingRaise)
            {
                if (showTags) PartyListGeometry.DrawRowPill(draw, max.X + 6f, (min.Y + max.Y) * 0.5f, "PENDING", BgDark, ColGold);
            }
            else if (m.IsRaisable)
            {
                if (showRowGlow)
                {
                    draw.AddRectFilled(min, max, rowGlowFill, 3f);
                    draw.AddRect(min, max, rowGlowBorder, 3f, ImDrawFlags.None, 2f);
                }

                if (showTags)
                {
                    PartyListGeometry.DrawRowPill(draw, max.X + 6f, (min.Y + max.Y) * 0.5f, "NEEDS RAISE", BgDark, ColCoral);
                }
            }
        }
    }
}
