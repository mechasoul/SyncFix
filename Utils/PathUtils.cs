using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using LLBML.Players;
using Multiplayer;

namespace BlazeSyncFix.Utils
{
    internal class PathUtils
    {
        public static DirectoryInfo ModdingFolder { get; private set; }
        public static string ModdingFolderName { get; private set; }

        public static void Init(PluginInfo info)
        {
            ModdingFolder = LLBML.Utils.ModdingFolder.GetModSubFolder(info);
            ModdingFolderName = ModdingFolder.FullName;
        }

        public static string GetFilepath(string resourceName)
        {
            return Utility.CombinePaths(ModdingFolderName, resourceName);
        }

        public static string GetCurrentGameDebugPath()
        {
            return Utility.CombinePaths(ModdingFolderName, GetCurrentUserId(), GetCurrentGameString());
        }

        private static string GetCurrentUserId()
        {
            //Platform.current.GetUserId()
            return CGLLJHHAJAK.GIGAKBJGFDI?.ECEAOMHNGOL() ?? "no_id";
        }

        private static string GetCurrentGameString()
        {
            if (!Sync.isActive) return "";

            StringBuilder sb = new StringBuilder();
            sb.Append(Sync.matchNr);
            sb.Append("_");
            for (int i = 0; i < Sync.nPlayers; i++)
            {
                if (Sync.IsValidOther(i))
                {
                    sb.Append(Player.GetPlayer(i).peer.peerId);
                    sb.Append("_");
                }
            }
            sb.Append($"P{P2P.localPeer.playerNr}");
            sb.Append("_");
            sb.Append(StateManager.IsUsingGroup() ? "GROUP" : "SOLO");
            return sb.ToString();
        }
    }
}
