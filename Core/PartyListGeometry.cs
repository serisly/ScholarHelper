using System;
using System.Collections.Generic;
using System.Numerics;
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

    private static bool TryGetNodeScreenRect(AtkUnitBase* addon, AtkResNode* node, out Vector2 min, out Vector2 max)
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
}
