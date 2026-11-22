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
        if (!config.RuleConfigs.TryGetValue(ScholarAlertRules.RaiseTrackerRuleId, out var raiseCfg)
            || !raiseCfg.Enabled
            || !raiseCfg.Flag)
        {
            return;
        }

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

        foreach (var row in rowScratch)
        {
            if (!PartyListGeometry.TryMatchMember(state.Party, row, out var m) || !m.IsDead || m.IsLocalPlayer) continue;

            var x = vp.Pos.X + row.Max.X + 6f;
            var centerY = vp.Pos.Y + ((row.Min.Y + row.Max.Y) * 0.5f);

            if (m.RaisingByName != null) PartyListGeometry.DrawRowPill(draw, x, centerY, "RAISING", BgDark, ColMint);
            else if (m.HasPendingRaise) PartyListGeometry.DrawRowPill(draw, x, centerY, "PENDING", BgDark, ColGold);
            else PartyListGeometry.DrawRowPill(draw, x, centerY, "NEEDS RAISE", BgDark, ColCoral);
        }
    }
}
