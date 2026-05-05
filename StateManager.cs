using System;
using System.Linq;
using LLBML.Messages;
using LLBML.Players;
using Multiplayer;

namespace SyncFix
{
    
    /// <summary>
    /// manages mod state in lobby. determines what mode to operate in, based on the status of other players & whether they have the mod installed
    /// </summary>
    public class StateManager
    {
        /// <summary>
        /// represents the current mode that the lobby is running in. determined by whether all other players have this mod installed.
        /// if all players have the mod installed, then group mode (based on ggpo) can be used, for a slightly more elaborate and 
        /// slightly more stable experience. if at least one other player does not have the mod installed, then solo mode is used, which 
        /// is a less perfect solution but doesn't require other players' cooperation
        /// </summary>
        public enum SyncFixMode
        {
            SOLO,
            GROUP
        }

        public enum LobbyPeerModStatus
        {
            UNKNOWN,
            CONFIRMED,
        }

        public static SyncFixMode CurrentMode = SyncFixMode.SOLO;
        private static readonly LobbyPeerModStatus[] peerModStatus = [LobbyPeerModStatus.UNKNOWN, LobbyPeerModStatus.UNKNOWN, LobbyPeerModStatus.UNKNOWN, LobbyPeerModStatus.UNKNOWN];
        public static bool HostHasSyncFix = false;

        /// <summary>
        /// registers mod-specific message callbacks with LLBML
        /// </summary>
        public static void RegisterLobbyMessages()
        {
            MessageApi.RegisterCustomMessage(Plugin.Instance.Info, (ushort)SyncFixMessages.LOBBY_MOD_CHECK, SyncFixMessages.LOBBY_MOD_CHECK.ToString(), ReceiveModCheck);
            MessageApi.RegisterCustomMessage(Plugin.Instance.Info, (ushort)SyncFixMessages.LOBBY_MOD_REPLY, SyncFixMessages.LOBBY_MOD_REPLY.ToString(), ReceiveModReply);
            MessageApi.RegisterCustomMessage(Plugin.Instance.Info, (ushort)SyncFixMessages.GAME_USE_GROUP, SyncFixMessages.GAME_USE_GROUP.ToString(), ReceiveGroupMessage);
        }

        /// <summary>
        /// call when a peer joins lobby to manage mod state. initializes the handshake to register that peer as having syncfix installed
        /// </summary>
        /// <param name="playerIndex"></param>
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

        /// <summary>
        /// call when a peer leaves lobby to manage mod state
        /// </summary>
        /// <param name="playerIndex"></param>
        public static void PeerLeft(int playerIndex)
        {
            if (!ShouldManageState()) return;

            SetPeerModStatus(playerIndex, LobbyPeerModStatus.UNKNOWN);
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
            //use group if no player exists that is both in the match and unconfirmed
            bool value = !peerModStatus.Where((status, index) => Player.GetPlayer(index).IsInMatch && status == LobbyPeerModStatus.UNKNOWN).Any();
            Plugin.Logger.LogInfo($"all peers confirmed: {value}");
            return value;
        }

        public static void SendAllGroupMessage()
        {
            if (!ShouldManageState()) return;

            ForAllRemotePeersInMatch(i => P2P.SendToPlayerNr(i, new Message((Msg)SyncFixMessages.GAME_USE_GROUP, P2P.localPeer.playerNr, -1, null, -1)));
            CurrentMode = SyncFixMode.GROUP;
            Plugin.Logger.LogInfo($"sent all peers group message");
        }

        public static void ReceiveGroupMessage(Message message)
        {
            if (!SyncFixConfig.Instance.Enabled) return;

            CurrentMode = SyncFixMode.GROUP;
            Plugin.Logger.LogInfo("received group message");
        }

        public static bool IsUsingGroup()
        {
            return CurrentMode == SyncFixMode.GROUP;
        }

        private static void SetPeerModStatus(int playerIndex, LobbyPeerModStatus status)
        {
            if (!ShouldManageState()) return;

            peerModStatus[playerIndex] = status;
        }

        /// <summary>
        /// total mod state reset. use when entering a new online lobby
        /// </summary>
        public static void ResetState()
        {
            ResetPeerModStatus();
            ResetMode();
        }

        /// <summary>
        /// resets the mod state of all players
        /// </summary>
        public static void ResetPeerModStatus()
        {
            if (!SyncFixConfig.Instance.Enabled) return;

            //probably not needed with the better KAfterOpen checking but might as well
            if (!P2P.isConnected) return;

            for (int i = 0; i < peerModStatus.Length; i++)
            {
                SetPeerModStatus(i, i == P2P.localPeer.playerNr ? LobbyPeerModStatus.CONFIRMED : LobbyPeerModStatus.UNKNOWN);
            }
            if (P2P.isHost) HostHasSyncFix = true;
            else HostHasSyncFix = false;
            Plugin.Logger.LogInfo($"reset status: {String.Join(", ", peerModStatus.Select(status => status.ToString()).ToArray())}");
        }

        /// <summary>
        /// resets currently chosen syncfix mode. can use this instead of ResetState when reentering a lobby
        /// </summary>
        public static void ResetMode()
        {
            CurrentMode = SyncFixMode.SOLO;
        }

        private static bool IsLocalPeer(int playerIndex)
        {
            return playerIndex == P2P.localPeer.playerNr;
        }

        /// <summary>
        /// convenience method for determining if we should be managing other players' mod states. true if we're host and mod is enabled (clients don't manage state;
        /// they just respond to any potential mod checks sent by host and switch to group mode if the host says to)
        /// </summary>
        /// <returns></returns>
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
