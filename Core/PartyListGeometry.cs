using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ScholarHelper.Core;

public readonly record struct PartyRowBounds(
    int RowIndex,
    string DisplayName,
    Vector2 Min,
    Vector2 Max);

public static unsafe class PartyListGeometry
{
    public static bool TryGetPartyBounds(
        IGameGui gameGui,
        out Vector2 min,
        out Vector2 max,
        List<PartyRowBounds>? outRows = null)
    {
        min = Vector2.Zero;
        max = Vector2.Zero;
        outRows?.Clear();

        var ptr = gameGui.GetAddonByName("_PartyList", 1);
        if (ptr.IsNull)
        {
            return false;
        }

        var addon = (AddonPartyList*)ptr.Address;
        if (addon == null || !addon->IsVisible || !addon->IsReady)
        {
            return false;
        }

        var count = Math.Clamp(addon->MemberCount, 0, addon->PartyMembers.Length);
        if (count == 0)
        {
            return false;
        }

        var foundAny = false;
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;

        for (var i = 0; i < count; i++)
        {
            ref readonly var member = ref addon->PartyMembers[i];
            var comp = member.PartyMemberComponent;
            if (comp == null || comp->OwnerNode == null)
            {
                continue;
            }

            var ownerRes = &comp->OwnerNode->AtkResNode;
            if (!ownerRes->IsVisible())
            {
                continue;
            }

            if (!TryGetNodeScreenRect((AtkUnitBase*)addon, ownerRes, out var rMin, out var rMax))
            {
                continue;
            }

            foundAny = true;
            if (rMin.X < minX) minX = rMin.X;
            if (rMin.Y < minY) minY = rMin.Y;
            if (rMax.X > maxX) maxX = rMax.X;
            if (rMax.Y > maxY) maxY = rMax.Y;

            if (outRows != null)
            {
                var name = ReadText(member.Name);
                outRows.Add(new PartyRowBounds(i, name, rMin, rMax));
            }
        }

        if (!foundAny)
        {
            return false;
        }

        min = new Vector2(minX, minY);
        max = new Vector2(maxX, maxY);
        return true;
    }

    public static bool TryGetNodeScreenRect(AtkUnitBase* addon, AtkResNode* node, out Vector2 min, out Vector2 max)
    {
        min = Vector2.Zero;
        max = Vector2.Zero;
        if (addon == null || node == null)
        {
            return false;
        }

        var posX = node->X;
        var posY = node->Y;
        var scaleX = node->ScaleX;
        var scaleY = node->ScaleY;
        var cur = node->ParentNode;
        var guard = 0;
        while (cur != null && cur->ParentNode != null && guard++ < 64)
        {
            posX = (posX * cur->ScaleX) + cur->X;
            posY = (posY * cur->ScaleY) + cur->Y;
            scaleX *= cur->ScaleX;
            scaleY *= cur->ScaleY;
            cur = cur->ParentNode;
        }

        var addonScale = addon->Scale > 0.01f ? addon->Scale : 1f;
        var left = addon->X + (posX * addonScale);
        var top = addon->Y + (posY * addonScale);
        var width = node->Width * scaleX * addonScale;
        var height = node->Height * scaleY * addonScale;
        if (width < 4f || height < 4f)
        {
            return false;
        }

        min = new Vector2(left, top);
        max = new Vector2(left + width, top + height);
        return true;
    }

    private static string ReadText(AtkTextNode* textNode)
    {
        if (textNode == null || textNode->NodeText.StringPtr.Value == null)
        {
            return string.Empty;
        }

        return MemoryHelper.ReadSeStringNullTerminated((nint)textNode->NodeText.StringPtr.Value)
            .TextValue
            .Trim();
    }

    public static bool TryMatchMember(IReadOnlyList<PartyMemberState> party, PartyRowBounds row, out PartyMemberState matched)
    {
        var clean = CleanDisplayName(row.DisplayName);
        if (!string.IsNullOrEmpty(clean))
        {
            foreach (var m in party)
            {
                if (string.Equals(m.Name, clean, StringComparison.OrdinalIgnoreCase))
                {
                    matched = m;
                    return true;
                }
            }

            foreach (var m in party)
            {
                if (m.Name.StartsWith(clean, StringComparison.OrdinalIgnoreCase)
                    || clean.EndsWith(m.Name, StringComparison.OrdinalIgnoreCase))
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

    private static string CleanDisplayName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var s = raw.Trim();
        if (s.StartsWith("Lv", StringComparison.OrdinalIgnoreCase))
        {
            var idx = 2;
            while (idx < s.Length && char.IsDigit(s[idx])) idx++;
            while (idx < s.Length && char.IsWhiteSpace(s[idx])) idx++;
            s = s[idx..];
        }

        s = s.TrimEnd('.', '\u2026', ' ');
        return s;
    }

    public static float DrawRowPill(ImDrawListPtr draw, float x, float centerY, string text, uint bgColor, uint accentColor)
    {
        var size = ImGui.CalcTextSize(text);
        const float padX = 6f;
        const float padY = 2f;
        var min = new Vector2(x, centerY - (size.Y * 0.5f) - padY);
        var max = new Vector2(x + size.X + (padX * 2f), centerY + (size.Y * 0.5f) + padY);
        draw.AddRectFilled(min, max, bgColor, 3.5f);
        draw.AddRect(min, max, accentColor, 3.5f, ImDrawFlags.None, 1.1f);
        draw.AddText(new Vector2(min.X + padX, min.Y + padY), accentColor, text);
        return max.X + 5f;
    }
}
