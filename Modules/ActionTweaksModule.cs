using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using ScholarHelper.Core;

namespace ScholarHelper.Modules;

public sealed unsafe class ActionTweaksModule : IHelperModule
{
    private const uint SacredSoilActionId = 188;

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate byte UseActionDelegate(
        ActionManager* actionManager,
        ActionType actionType,
        uint actionId,
        ulong targetId,
        uint extraParam,
        ActionManager.UseActionMode mode,
        uint comboRouteId,
        byte* outOptAreaTargeted);

    private readonly IGameInteropProvider gameInteropProvider;
    private readonly IObjectTable objectTable;
    private readonly Configuration config;
    private readonly IPluginLog log;
    private readonly HashSet<uint> friendlySingleTargetActionIds = new();

    private Hook<UseActionDelegate>? hook;
    private bool hasLoggedDetourError;

    public string Id => "action_tweaks";
    public string DisplayName => "Actions";
    public string Tooltip => "Optional hotbar casting helpers.";

    public ActionTweaksModule(
        IGameInteropProvider gameInteropProvider,
        IDataManager dataManager,
        IObjectTable objectTable,
        Configuration config,
        IPluginLog log)
    {
        this.gameInteropProvider = gameInteropProvider;
        this.objectTable = objectTable;
        this.config = config;
        this.log = log;

        CacheFriendlyActions(dataManager);
    }

    private void CacheFriendlyActions(IDataManager dataManager)
    {
        var sheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        if (sheet == null) return;

        foreach (var act in sheet)
        {
            if (!act.IsPlayerAction || act.TargetArea || act.CanTargetHostile) continue;
            if (act.CanTargetParty || act.CanTargetAlly)
            {
                friendlySingleTargetActionIds.Add(act.RowId);
            }
        }
    }

    public void Enable()
    {
        try
        {
            hook ??= gameInteropProvider.HookFromAddress<UseActionDelegate>(
                (nint)ActionManager.MemberFunctionPointers.UseAction,
                Detour);
            hook.Enable();
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to initialize Scholar Helper UseAction hook.");
        }
    }

    public void DrawOverlay() { }

    public bool DrawSettings()
    {
        var changed = false;

        var selfCast = config.SacredSoilAtSelf;
        if (ImGui.Checkbox("Cast Sacred Soil at self", ref selfCast))
        {
            config.SacredSoilAtSelf = selfCast;
            changed = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Drops Sacred Soil directly at your feet instead of opening ground targeting.");
        }

        var moHeal = config.MouseoverPartyHealing;
        if (ImGui.Checkbox("Mouseover healing on Party List", ref moHeal))
        {
            config.MouseoverPartyHealing = moHeal;
            changed = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Casts single-target heals, shields, Esuna, and Raise on whichever party member your mouse is over in the Party List without changing targets.");
        }

        return changed;
    }

    private byte Detour(
        ActionManager* actionManager,
        ActionType actionType,
        uint actionId,
        ulong targetId,
        uint extraParam,
        ActionManager.UseActionMode mode,
        uint comboRouteId,
        byte* outOptAreaTargeted)
    {
        if (config.Enabled && actionType == ActionType.Action && mode == ActionManager.UseActionMode.None)
        {
            try
            {
                var adjustedId = actionManager != null ? actionManager->GetAdjustedActionId(actionId) : actionId;

                if (config.SacredSoilAtSelf && (actionId == SacredSoilActionId || adjustedId == SacredSoilActionId))
                {
                    var player = objectTable.LocalPlayer;
                    if (player != null && actionManager != null)
                    {
                        if (outOptAreaTargeted != null) *outOptAreaTargeted = 0;
                        var pos = player.Position;
                        return actionManager->UseActionLocation(
                            ActionType.Action,
                            SacredSoilActionId,
                            player.GameObjectId,
                            &pos,
                            extraParam)
                            ? (byte)1
                            : (byte)0;
                    }
                }

                if (config.MouseoverPartyHealing
                    && (friendlySingleTargetActionIds.Contains(adjustedId) || friendlySingleTargetActionIds.Contains(actionId)))
                {
                    var pronoun = PronounModule.Instance();
                    var moObj = pronoun != null ? pronoun->UiMouseOverTarget : null;
                    if (moObj != null
                        && moObj->ObjectKind == ObjectKind.Pc
                        && ActionManager.CanUseActionOnTarget(adjustedId, moObj))
                    {
                        var moId = (ulong)moObj->GetGameObjectId();
                        if (moId != 0 && moId != 0xE0000000)
                        {
                            return hook!.Original(
                                actionManager,
                                actionType,
                                actionId,
                                moId,
                                extraParam,
                                mode,
                                comboRouteId,
                                outOptAreaTargeted);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!hasLoggedDetourError)
                {
                    hasLoggedDetourError = true;
                    log.Warning(ex, "Skipped Scholar Helper UseAction redirect.");
                }
            }
        }

        return hook!.Original(
            actionManager,
            actionType,
            actionId,
            targetId,
            extraParam,
            mode,
            comboRouteId,
            outOptAreaTargeted);
    }

    public void Dispose()
    {
        hook?.Dispose();
        hook = null;
    }
}
