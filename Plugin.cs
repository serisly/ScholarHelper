using System.Collections.Generic;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using ScholarHelper.Core;
using ScholarHelper.Modules;
using ScholarHelper.Rules;
using ScholarHelper.UI;

namespace ScholarHelper;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandShort = "/sch";
    private const string CommandLong = "/scholarhelper";
    private const string ParseToggleCommand = "/parse";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commandManager;
    private readonly WindowSystem windowSystem = new("ScholarHelper");
    private readonly AlertEngine alertEngine;
    private readonly HotbarGlowHook hotbarGlowHook;
    private readonly AlertBannerOverlay bannerOverlay;
    private readonly SettingsWindow settingsWindow;
    private readonly IReadOnlyList<IHelperModule> modules;

    public Configuration Configuration { get; }

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        IFramework framework,
        IObjectTable objectTable,
        IPartyList partyList,
        IBuddyList buddyList,
        IJobGauges jobGauges,
        ITargetManager targetManager,
        ICondition condition,
        IGameGui gameGui,
        ITextureProvider textureProvider,
        IGameInteropProvider gameInteropProvider,
        ICommandManager commandManager,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commandManager = commandManager;

        Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        var rules = ScholarAlertRules.CreateAll();
        alertEngine = new AlertEngine(
            framework,
            objectTable,
            partyList,
            buddyList,
            jobGauges,
            targetManager,
            condition,
            Configuration,
            rules,
            log);
        alertEngine.Enable();

        hotbarGlowHook = new HotbarGlowHook(gameInteropProvider, alertEngine, log);
        hotbarGlowHook.Enable();

        var mitStripModule = new ActiveMitigationStripModule(
            pluginInterface,
            gameGui,
            textureProvider,
            alertEngine,
            Configuration);

        modules =
        [
            new AbilityRangeTooltipModule(
                gameGui,
                objectTable,
                textureProvider,
                alertEngine,
                Configuration),
            mitStripModule,
            new PartyListAnnotationModule(
                gameGui,
                alertEngine,
                Configuration),
        ];

        foreach (var module in modules)
        {
            module.Enable();
        }

        bannerOverlay = new AlertBannerOverlay(pluginInterface, textureProvider, alertEngine, Configuration);
        settingsWindow = new SettingsWindow(
            pluginInterface,
            Configuration,
            alertEngine,
            bannerOverlay,
            mitStripModule,
            modules);
        windowSystem.AddWindow(bannerOverlay);
        windowSystem.AddWindow(settingsWindow);

        this.pluginInterface.UiBuilder.Draw += DrawUi;
        this.pluginInterface.UiBuilder.OpenConfigUi += ToggleSettingsWindow;
        this.pluginInterface.UiBuilder.OpenMainUi += ToggleSettingsWindow;

        this.commandManager.AddHandler(CommandShort, new CommandInfo(OnSettingsCommand)
        {
            HelpMessage = "Open Scholar Helper settings (/sch).",
        });
        this.commandManager.AddHandler(CommandLong, new CommandInfo(OnSettingsCommand)
        {
            HelpMessage = "Open Scholar Helper settings (/scholarhelper).",
        });
        this.commandManager.AddHandler(ParseToggleCommand, new CommandInfo(OnParseToggleCommand)
        {
            HelpMessage = "Toggle LMeter Raid DPS & Healer Compare windows (/parse).",
        });
    }

    private void DrawUi()
    {
        windowSystem.Draw();
        foreach (var module in modules)
        {
            module.DrawOverlay();
        }
    }

    private void ToggleSettingsWindow() => settingsWindow.Toggle();

    private void OnSettingsCommand(string command, string args) => settingsWindow.Toggle();

    private unsafe void OnParseToggleCommand(string command, string args)
    {
        var uiModule = UIModule.Instance();
        if (uiModule == null)
        {
            return;
        }

        ExecuteChatCommand(uiModule, "/lm toggle 1");
        ExecuteChatCommand(uiModule, "/lm toggle 2");
    }

    private static unsafe void ExecuteChatCommand(UIModule* uiModule, string commandText)
    {
        var cmd = Utf8String.FromString(commandText);
        if (cmd == null)
        {
            return;
        }

        try
        {
            uiModule->ProcessChatBoxEntry(cmd, nint.Zero, false);
        }
        finally
        {
            cmd->Dtor(true);
        }
    }

    public void Dispose()
    {
        commandManager.RemoveHandler(CommandShort);
        commandManager.RemoveHandler(CommandLong);
        commandManager.RemoveHandler(ParseToggleCommand);
        pluginInterface.UiBuilder.Draw -= DrawUi;
        pluginInterface.UiBuilder.OpenConfigUi -= ToggleSettingsWindow;
        pluginInterface.UiBuilder.OpenMainUi -= ToggleSettingsWindow;
        windowSystem.RemoveAllWindows();

        foreach (var module in modules)
        {
            module.Dispose();
        }

        hotbarGlowHook.Dispose();
        alertEngine.Dispose();
    }
}
