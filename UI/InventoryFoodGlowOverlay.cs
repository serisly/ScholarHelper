using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ScholarHelper.Core;

namespace ScholarHelper.UI;

public sealed unsafe class InventoryFoodGlowOverlay
{
    private const int VisualBagSlotsPerPage = 35;
    private const uint GridSlotBaseNodeId = 3;

    private static readonly string[] ExpansionGrids = ["InventoryGrid0E", "InventoryGrid1E", "InventoryGrid2E", "InventoryGrid3E"];
    private static readonly string[] LargeGrids = ["InventoryGrid0", "InventoryGrid1"];

    private readonly IGameGui gameGui;
    private readonly AlertEngine alertEngine;
    private readonly Configuration config;
    private readonly List<(string AddonName, uint NodeId, InventoryType Container, int PhysicalSlot)> slotScratch = new(140);
    private readonly List<(Vector2 Min, Vector2 Max)> occluderScratch = new(16);

    public InventoryFoodGlowOverlay(IGameGui gameGui, AlertEngine alertEngine, Configuration config)
    {
        this.gameGui = gameGui;
        this.alertEngine = alertEngine;
        this.config = config;
    }

    public void Draw()
    {
        if (!config.Enabled || !alertEngine.IsFoodGlowActive) return;

        var invMgr = InventoryManager.Instance();
        if (invMgr == null) return;

        slotScratch.Clear();
        CollectVisibleBagSlots(slotScratch);
        if (slotScratch.Count == 0) return;

        var vp = ImGui.GetMainViewport();
        var draw = ImGui.GetForegroundDrawList(vp);
        var pulse = 0.65f + (0.35f * MathF.Sin((float)ImGui.GetTime() * 5.5f));
        var colOuter = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.85f, 0.32f, 0.95f * pulse));
        var colInner = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.94f, 0.65f, 0.20f * pulse));

        string? lastAddonName = null;
        AtkUnitBase* addon = null;

        foreach (var entry in slotScratch)
        {
            var container = invMgr->GetInventoryContainer(entry.Container);
            if (container == null || entry.PhysicalSlot < 0 || entry.PhysicalSlot >= container->Size) continue;

            var item = container->GetInventorySlot(entry.PhysicalSlot);
            if (item == null || item->ItemId == 0 || !alertEngine.IsMealItem(item->ItemId)) continue;

            if (entry.AddonName != lastAddonName)
            {
                lastAddonName = entry.AddonName;
                addon = GetReadyAddon(entry.AddonName);
                CollectHigherZRects(addon, vp.Pos, vp.Size, occluderScratch);
            }

            if (addon == null || !TryGetNodeScreenRect(addon, entry.NodeId, vp.Pos, out var min, out var max)) continue;
            if (IsOccluded((min + max) * 0.5f, occluderScratch)) continue;

            draw.AddRectFilled(min, max, colInner, 3f);
            draw.AddRect(min, max, colOuter, 3f, ImDrawFlags.None, 2.2f);
        }
    }

    private void CollectVisibleBagSlots(List<(string AddonName, uint NodeId, InventoryType Container, int PhysicalSlot)> output)
    {
        var expOpen = GetReadyAddon("InventoryExpansion") != null;
        var largeAddon = !expOpen ? GetReadyAddon("InventoryLarge") : null;
        var normalAddon = !expOpen && largeAddon == null ? GetReadyAddon("Inventory") : null;
        if (!expOpen && largeAddon == null && normalAddon == null) return;

        var tabOffset = largeAddon != null && largeAddon->AtkValuesCount > 0 && largeAddon->AtkValues[0].Int == 1 ? 2 : 0;
        var activeTab = normalAddon != null && normalAddon->AtkValuesCount > 0 ? normalAddon->AtkValues[0].Int : 0;

        var orderModule = ItemOrderModule.Instance();
        var sorter = orderModule != null ? orderModule->InventorySorter : null;

        if (sorter != null && sorter->Items.Count > 0)
        {
            for (var v = 0; v < sorter->Items.Count; v++)
            {
                var entryPtr = sorter->Items[v].Value;
                if (entryPtr == null) continue;

                var physType = PageToContainer(entryPtr->Page);
                if (physType == InventoryType.Invalid) continue;

                AddSlotForView(v / VisualBagSlotsPerPage, v % VisualBagSlotsPerPage, physType, entryPtr->Slot, expOpen, largeAddon != null, tabOffset, activeTab, output);
            }
        }
        else
        {
            for (var p = 0; p < 4; p++)
            {
                var physType = PageToContainer(p);
                for (var s = 0; s < VisualBagSlotsPerPage; s++)
                {
                    AddSlotForView(p, s, physType, s, expOpen, largeAddon != null, tabOffset, activeTab, output);
                }
            }
        }
    }

    private static void AddSlotForView(
        int page,
        int slot,
        InventoryType container,
        int physSlot,
        bool expOpen,
        bool largeOpen,
        int tabOffset,
        int activeTab,
        List<(string AddonName, uint NodeId, InventoryType Container, int PhysicalSlot)> output)
    {
        var nodeId = (uint)(slot + GridSlotBaseNodeId);
        if (expOpen)
        {
            if (page is >= 0 and < 4) output.Add((ExpansionGrids[page], nodeId, container, physSlot));
        }
        else if (largeOpen)
        {
            var rel = page - tabOffset;
            if (rel is >= 0 and < 2) output.Add((LargeGrids[rel], nodeId, container, physSlot));
        }
        else if (page == activeTab)
        {
            output.Add(("InventoryGrid", nodeId, container, physSlot));
        }
    }

    private static InventoryType PageToContainer(int page) => page switch
    {
        0 => InventoryType.Inventory1,
        1 => InventoryType.Inventory2,
        2 => InventoryType.Inventory3,
        3 => InventoryType.Inventory4,
        _ => InventoryType.Invalid,
    };

    private AtkUnitBase* GetReadyAddon(string name)
    {
        var ptr = gameGui.GetAddonByName(name, 1);
        if (ptr == nint.Zero) return null;
        var addon = (AtkUnitBase*)ptr.Address;
        return addon != null && addon->IsVisible && addon->IsReady ? addon : null;
    }

    private static bool TryGetNodeScreenRect(AtkUnitBase* addon, uint nodeId, Vector2 vpPos, out Vector2 min, out Vector2 max)
    {
        min = Vector2.Zero;
        max = Vector2.Zero;
        var node = addon->GetNodeById(nodeId);
        if (node == null || !node->IsVisible()) return false;
        if (!PartyListGeometry.TryGetNodeScreenRect(addon, node, out var rawMin, out var rawMax)) return false;

        min = vpPos + rawMin;
        max = vpPos + rawMax;
        return (max.X - min.X) >= 8f && (max.Y - min.Y) >= 8f;
    }

    private static void CollectHigherZRects(AtkUnitBase* targetAddon, Vector2 vpPos, Vector2 vpSize, List<(Vector2 Min, Vector2 Max)> output)
    {
        output.Clear();
        if (targetAddon == null) return;

        var unitManager = RaptureAtkUnitManager.Instance();
        if (unitManager == null) return;

        var targetLayer = targetAddon->DepthLayer;
        var targetOrder = targetAddon->DrawOrderIndex;
        var targetHost = targetAddon->HostId != 0 ? targetAddon->HostId : targetAddon->Id;

        var list = unitManager->AllLoadedUnitsList;
        var count = Math.Min((int)list.Count, list.Entries.Length);
        for (var i = 0; i < count; i++)
        {
            var other = list.Entries[i].Value;
            if (other == null
                || other == targetAddon
                || other->Id == targetHost
                || (other->HostId != 0 && other->HostId == targetHost))
            {
                continue;
            }

            if (other->DepthLayer < targetLayer
                || (other->DepthLayer == targetLayer && other->DrawOrderIndex <= targetOrder))
            {
                continue;
            }

            if (other->RootNode == null || !other->IsVisible || !other->RootNode->IsVisible()) continue;
            var scale = other->Scale > 0.01f ? other->Scale : 1f;
            var w = other->RootNode->Width * scale;
            var h = other->RootNode->Height * scale;
            if (w < 8f || h < 8f) continue;
            if (vpSize.X > 0f && vpSize.Y > 0f && w >= vpSize.X * 0.88f && h >= vpSize.Y * 0.88f) continue;
            if (other->WindowCollisionNode == null && other->DepthLayer <= targetLayer) continue;

            var min = new Vector2(vpPos.X + other->X, vpPos.Y + other->Y);
            output.Add((min, new Vector2(min.X + w, min.Y + h)));
        }
    }

    private static bool IsOccluded(Vector2 center, List<(Vector2 Min, Vector2 Max)> rects)
    {
        for (var i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            if (center.X >= r.Min.X && center.X <= r.Max.X && center.Y >= r.Min.Y && center.Y <= r.Max.Y)
            {
                return true;
            }
        }
        return false;
    }
}
