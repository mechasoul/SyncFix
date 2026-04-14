using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace BlazeSyncFix
{
    public class SyncFixConfig
    {
        private static SyncFixConfig instance;
        public static SyncFixConfig Instance { get => instance; private set => instance = value; }

        private ConfigFile _configFile;
        private ConfigEntry<bool> enabled;
        private ConfigEntry<bool> showDebugInfo;
        private ConfigEntry<KeyCode> debugInfoKey;


        private SyncFixConfig(ConfigFile configFile)
        {
            _configFile = configFile;
            enabled = configFile.Bind(new ConfigDefinition("Sync Fix", "Enable host advantage fix"), true);
            showDebugInfo = configFile.Bind(new ConfigDefinition("Sync Fix", "Show debug info ingame"), false);
            debugInfoKey = configFile.Bind("Sync Fix", "Toggle debug info key", KeyCode.None);
        }

        public bool Enabled { get => enabled.Value; set => enabled.Value = value; }
        public bool ShowDebugInfo { get => showDebugInfo.Value; set => showDebugInfo.Value = value; }
        public KeyCode DebugInfoKey { get => debugInfoKey.Value; set => debugInfoKey.Value = value; }

        internal static void LoadConfig(ConfigFile configFile)
        {
            if (Instance != null) throw new InvalidOperationException("config already loaded");
            configFile.SaveOnConfigSet = true;
            Instance = new SyncFixConfig(configFile);
        }
    }
}
