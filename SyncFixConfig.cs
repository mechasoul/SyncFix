using System;
using BepInEx.Configuration;
using UnityEngine;

namespace SyncFix
{
    public class SyncFixConfig
    {
        private static SyncFixConfig instance;
        public static SyncFixConfig Instance { get => instance; private set => instance = value; }

        private readonly ConfigEntry<bool> enabled;
        private readonly ConfigEntry<bool> showDebugInfo;
        private readonly ConfigEntry<KeyCode> debugInfoKey;
        private readonly ConfigEntry<bool> recordDebugInfo;


        private SyncFixConfig(ConfigFile configFile)
        {
            enabled = configFile.Bind(new ConfigDefinition("Sync Fix", "Enable host advantage fix"), true);
            showDebugInfo = configFile.Bind(new ConfigDefinition("Sync Fix", "Show debug info ingame"), false);
            debugInfoKey = configFile.Bind("Sync Fix", "Toggle debug info key", KeyCode.None);
            recordDebugInfo = configFile.Bind(new ConfigDefinition("Sync Fix", "Save debug info to disk at match end"), false);
        }

        public bool Enabled { get => enabled.Value; set => enabled.Value = value; }
        public bool ShowDebugInfo { get => showDebugInfo.Value; set => showDebugInfo.Value = value; }
        public KeyCode DebugInfoKey { get => debugInfoKey.Value; set => debugInfoKey.Value = value; }
        public bool RecordDebugInfo { get => recordDebugInfo.Value; set => recordDebugInfo.Value = value; }

        internal static void LoadConfig(ConfigFile configFile)
        {
            if (Instance != null) throw new InvalidOperationException("config already loaded");
            configFile.SaveOnConfigSet = true;
            Instance = new SyncFixConfig(configFile);
        }
    }
}
