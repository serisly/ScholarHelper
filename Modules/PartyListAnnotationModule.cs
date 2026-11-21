using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using ScholarHelper.Core;

namespace ScholarHelper.Modules;

/// <summary>
/// Opt-in inline badges drawn directly beside native _PartyList rows (alternative to floating banners).
/// </summary>
public sealed class PartyListAnnotationModule : IHelperModule
{
    private const float EosWhisperingDawnRadius = 15f;

    private static readonly uint BgDark = ImGui.ColorConvertFloat4ToU32(new Vector4(0.07f, 0.08f, 0.10f, 0.88f));
    private static readonly uint ColCoral = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.44f, 0.40f, 1f));
    private static readonly uint ColMint = ImGui.ColorConvertFloat4ToU32(new Vector4(0.54f, 0.92f, 0.66f, 1f));
    private static readonly uint ColGold = ImGui.ColorConvertFloat4ToU32(new Vector4(0.98f, 0.84f, 0.42f, 1f));
    private static readonly uint ColAmber = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.72f, 0.28f, 1f));

    private readonly IGameGui gameGui;
    private readonly AlertEngine alertEngine;
    private readonly Configuration config;
    private readonly List<PartyRowBounds> rowScratch = new(8);

    public string Id => "party_list_annotations";
    public string DisplayName => "Party List Inline Badges";
    public string Tooltip => "Draws compact status tags directly beside Party List rows (alternative to floating banners).";

    public PartyListAnnotationModule(IGameGui gameGui, AlertEngine alertEngine, Configuration config)
    {
        this.gameGui = gameGui;
        this.alertEngine = alertEngine;
        this.config = config;
    }

    public void Enable() { }

    public void DrawOverlay()
    {
        if (!config.Enabled || (!config.EnablePartyRaiseBadges && !config.EnablePartyOutOfEosTag)) return;

        var state = alertEngine.State;
        if (!state.IsLoggedIn || state.Party.Count <= 1) return;
        if (!PartyListGeometry.TryGetPartyBounds(gameGui, out _, out _, rowScratch) || rowScratch.Count == 0) return;

        var checkEosRange = config.EnablePartyOutOfEosTag
                            && state.InCombat
                            && state.IsScholar
                            && state.Level >= 30
                            && state.HasActivePet
                            && !state.IsDissipated;

        var vp = ImGui.GetMainViewport();
        var draw = ImGui.GetForegroundDrawList(vp);

        foreach (var row in rowScratch)
        {
            if (!TryMatchMember(state.Party, row, out var m)) continue;

            var x = vp.Pos.X + row.Max.X + 6f;
            var centerY = vp.Pos.Y + ((row.Min.Y + row.Max.Y) * 0.5f);

            if (config.EnablePartyRaiseBadges && m.IsDead && !m.IsLocalPlayer)
            {
                if (m.RaisingByName != null) x = DrawPill(draw, x, centerY, "RAISING", ColMint);
                else if (m.HasPendingRaise) x = DrawPill(draw, x, centerY, "PENDING", ColGold);
                else x = DrawPill(draw, x, centerY, "NEEDS RAISE", ColCoral);
            }

            if (checkEosRange && !m.IsDead)
            {
                var dist = Vector3.Distance(m.Position, state.PetPosition);
                if (dist > EosWhisperingDawnRadius)
                {
                    DrawPill(draw, x, centerY, $"EOS {dist:F0}y", ColAmber);
                }
            }
        }
    }

    private static bool TryMatchMember(IReadOnlyList<PartyMemberState> party, PartyRowBounds row, out PartyMemberState matched)
    {
        if (!string.IsNullOrEmpty(row.DisplayName))
        {
            foreach (var m in party)
            {
                if (string.Equals(m.Name, row.DisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    matched = m;
                    return true;
                }
            }
        }

        if (row.RowIndex >= 0 && row.RowIndex < party.Count)
        {
            matched = party[row.RowIndex];
            return true;
        }

        matched = default;
        return false;
    }

    private static float DrawPill(ImDrawListPtr draw, float x, float centerY, string text, uint accent)
    {
        var size = ImGui.CalcTextSize(text);
        const float padX = 6f;
        const float padY = 2f;
        var min = new Vector2(x, centerY - (size.Y * 0.5f) - padY);
        var max = new Vector2(x + size.X + (padX * 2f), centerY + (size.Y * 0.5f) + padY);
        draw.AddRectFilled(min, max, BgDark, 3.5f);
        draw.AddRect(min, max, accent, 3.5f, ImDrawFlags.None, 1.1f);
        draw.AddText(new Vector2(min.X + padX, min.Y + padY), accent, text);
        return max.X + 5f;
    }

    public bool DrawSettings()
    {
        var changed = false;

        var raiseBadges = config.EnablePartyRaiseBadges;
        if (ImGui.Checkbox("Raise status badges beside dead party rows (NEEDS RAISE / RAISING / PENDING)", ref raiseBadges))
        {
            config.EnablePartyRaiseBadges = raiseBadges;
            changed = true;
        }

        var eosTag = config.EnablePartyOutOfEosTag;
        if (ImGui.Checkbox("Out-of-Eos-range tag beside party rows (> 15y in combat)", ref eosTag))
        {
            config.EnablePartyOutOfEosTag = eosTag;
            changed = true;
        }

        return changed;
    }

    public void Dispose() { }
}
