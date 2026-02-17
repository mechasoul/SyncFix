using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using LLBML.Messages;
using LLBML.Networking;
using LLBML.Players;
using LLBML.States;
using Multiplayer;

namespace BlazeSyncFix
{
    
    /// <summary>
    /// manages mod state. determines what mode to operate in, based on the status of other players & whether they have the mod installed.
    /// </summary>
    public class StateManager
    {
        /// <summary>
        /// represents the current mode that the lobby is running in. determined by whether all other players have this mod installed.
        /// if all players have the mod installed, then GGPO mode (based on ggpo duh) can be used, for a slightly more elaborate and 
        /// slightly more stable experience. if at least one other player does not have the mod installed, then SIMPLE mode is used, which 
        /// fixes LLB's host advantage when playing as the client.
        /// </summary>
        //TODO test adding artifical delay as host to see if we can eliminate client disadvantage for client players without the plugin
        //when we're host. would offset the innate advantage gained by being able to fix host advantage when the player is client but
        //not when the player is host
        public enum SyncFixMode
        {
            SIMPLE,
            GGPO
        }

        public enum LobbyPeerModStatus
        {
            UNKNOWN,
            CONFIRMED,
        }

        public static SyncFixMode CurrentMode = SyncFixMode.SIMPLE;
        private static LobbyPeerModStatus[] peerModStatus = [LobbyPeerModStatus.UNKNOWN, LobbyPeerModStatus.UNKNOWN, LobbyPeerModStatus.UNKNOWN, LobbyPeerModStatus.UNKNOWN];
        public static bool HostHasSyncFix = false;

        public static void RegisterLobbyMessages()
        {
            MessageApi.RegisterCustomMessage(Plugin.Instance.Info, (ushort)SyncFixMessages.LOBBY_MOD_CHECK, SyncFixMessages.LOBBY_MOD_CHECK.ToString(), ReceiveModCheck);
            MessageApi.RegisterCustomMessage(Plugin.Instance.Info, (ushort)SyncFixMessages.LOBBY_MOD_REPLY, SyncFixMessages.LOBBY_MOD_REPLY.ToString(), ReceiveModReply);
            MessageApi.RegisterCustomMessage(Plugin.Instance.Info, (ushort)SyncFixMessages.GAME_USE_GGPO, SyncFixMessages.GAME_USE_GGPO.ToString(), ReceiveGGPOMessage);
        }

        public static void PeerJoined(int playerIndex)
        {
            if (!ShouldManageState()) return;

            if (IsLocalPeer(playerIndex))
            {
                SetPeerModStatus(playerIndex, LobbyPeerModStatus.CONFIRMED);
            }
            else
            {
                SendModCheck(playerIndex);
            }
        }

        public static void PeerLeft(int playerIndex)
        {
            if (!ShouldManageState()) return;

            SetPeerModStatus(playerIndex, LobbyPeerModStatus.UNKNOWN);
        }

        public static void SendModCheck()
        {
            if (!ShouldManageState()) return;

            ForAllRemotePeersInMatch(i => P2P.SendToPlayerNr(i, new Message((Msg)SyncFixMessages.LOBBY_MOD_CHECK, P2P.localPeer.playerNr, -1, null, -1)));
        }

        public static void SendModCheck(int playerIndex)
        {
            if (!ShouldManageState()) return;

            P2P.SendToPlayerNr(playerIndex, new Message((Msg)SyncFixMessages.LOBBY_MOD_CHECK, P2P.localPeer.playerNr, -1, null, -1));
            Plugin.Logger.LogInfo($"sent mod check to player {playerIndex}");
        }

        public static void ReceiveModCheck(Message message)
        {
            if (!SyncFixConfig.Instance.Enabled) return;

            if (message.playerNr == 0) HostHasSyncFix = true; // message.playerNr should always be 0 but check anyway i guess
            P2P.SendToPlayerNr(message.playerNr, new Message((Msg)SyncFixMessages.LOBBY_MOD_REPLY, P2P.localPeer.playerNr, -1, null, -1));
            Plugin.Logger.LogInfo($"received mod check from player {message.playerNr}, sent reply");
        }

        public static void ReceiveModReply(Message message)
        {
            if (!ShouldManageState()) return;

            SetPeerModStatus(message.playerNr, LobbyPeerModStatus.CONFIRMED);
            Plugin.Logger.LogInfo($"received mod reply from player {message.playerNr}, set CONFIRMED");
        }

        public static bool AllPeersConfirmed()
        {
            bool value = !peerModStatus.Where((status, index) => Player.GetPlayer(index).IsInMatch && status == LobbyPeerModStatus.UNKNOWN).Any();
            Plugin.Logger.LogInfo($"all peers confirmed: {value}");
            return value;
        }

        public static void SendAllGGPOMessage()
        {
            if (!ShouldManageState()) return;

            ForAllRemotePeersInMatch(i => P2P.SendToPlayerNr(i, new Message((Msg)SyncFixMessages.GAME_USE_GGPO, P2P.localPeer.playerNr, -1, null, -1)));
            CurrentMode = SyncFixMode.GGPO;
            Plugin.Logger.LogInfo($"sent all peers GGPO message");
        }

        public static void ReceiveGGPOMessage(Message message)
        {
            if (!SyncFixConfig.Instance.Enabled) return;

            CurrentMode = SyncFixMode.GGPO;
            Plugin.Logger.LogInfo("received ggpo message");
        }

        public static bool IsUsingGGPO()
        {
            return CurrentMode == SyncFixMode.GGPO;
        }

        public static LobbyPeerModStatus GetPeerModStatus(int playerIndex)
        {
            return peerModStatus[playerIndex];
        }

        private static void SetPeerModStatus(int playerIndex, LobbyPeerModStatus status)
        {
            if (!ShouldManageState()) return;

            peerModStatus[playerIndex] = status;
        }

        public static bool PeerHasMod(int playerIndex)
        {
            return GetPeerModStatus(playerIndex) == LobbyPeerModStatus.CONFIRMED;
        }

        public static void ResetState()
        {
            ResetPeerModStatus();
            ResetMode();
        }

        public static void ResetPeerModStatus()
        {
            if (!SyncFixConfig.Instance.Enabled) return;

            for (int i = 0; i < peerModStatus.Length; i++)
            {
                SetPeerModStatus(i, i == P2P.localPeer.playerNr ? LobbyPeerModStatus.CONFIRMED : LobbyPeerModStatus.UNKNOWN);
            }
            if (P2P.isHost) HostHasSyncFix = true;
            else HostHasSyncFix = false;
            Plugin.Logger.LogInfo($"reset status: {String.Join(", ", peerModStatus.Select(status => status.ToString()).ToArray())}");
        }

        public static void ResetMode()
        {
            CurrentMode = SyncFixMode.SIMPLE;
        }

        private static bool IsLocalPeer(int playerIndex)
        {
            return playerIndex == P2P.localPeer.playerNr;
        }

        private static bool ShouldManageState()
        {
            return P2P.isHost && SyncFixConfig.Instance.Enabled;
        }

        //note we can't use Sync methods in lobby
        private static void ForAllRemotePeersInMatch(Action<int> action)
        {
            Player.ForAllInMatch((Player player) =>
            {
                if (!IsLocalPeer(player.nr))
                {
                    action(player.nr);
                }
            });
        }
    }
}
