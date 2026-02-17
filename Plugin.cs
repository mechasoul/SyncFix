using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace BlazeSyncFix;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency(LLBML.PluginInfos.PLUGIN_ID, BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("no.mrgentle.plugins.llb.modmenu", BepInDependency.DependencyFlags.SoftDependency)]
[BepInProcess("LLBlaze.exe")]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    public static Plugin Instance { get; private set; }


    private Harmony _harmony;

    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Instance = this;
        SyncFixConfig.LoadConfig(Config);
        _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        _harmony.PatchAll();
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    private void Start()
    {
        StateManager.RegisterLobbyMessages();
        SyncFixManager.RegisterGameMessages();
        LLBML.Utils.ModDependenciesUtils.RegisterToModMenu(this.Info, new List<String>
        {
            "Patches time sync to fix host advantage"
        });
    }

    private void OnDestroy()
    {
        // Cleanup
        _harmony?.UnpatchSelf();
    }
}
