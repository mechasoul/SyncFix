using System.Collections;
using SyncFix.Utils;
using HarmonyLib;
using Multiplayer;

namespace SyncFix.Patches
{
    /// <summary>
    /// contains patches for monitoring player mod state in lobby, so we know which time sync fix to use
    /// </summary>
    [HarmonyPatch]
    public class LobbyState_Patches
    {
        //TODO: is this ALWAYS called? should look into if eg a peer can dc in a way that this isn't called, which would leave their slot confirmed.
        //maybe not an issue tho since either their slot is empty and not factored into anything or a new player takes their slot and the state is reset?
        [HarmonyPatch(typeof(LocalHost), nameof(LocalHost.OnOtherLeft))]
        [HarmonyPostfix]
        public static void OnOtherLeftPostfix(LocalHost __instance, Peer otherPeer)
        {
            StateManager.PeerLeft(otherPeer.playerNr);
        }

        [HarmonyPatch(typeof(LocalHost), nameof(LocalHost.OnOtherJoined))]
        [HarmonyPostfix]
        public static void OnOtherJoinedPostfix(LocalHost __instance, string otherPeerId, string otherPeerName, int otherPlayerNr)
        {
            StateManager.PeerJoined(otherPlayerNr);
        }

        //parameterless StartGame is only called by host on setup, so send group syncfix message here if appropriate
        //GameStatesLobbyOnline.StartGame
        [HarmonyPatch(typeof(HDLIJDBFGKN), nameof(HDLIJDBFGKN.OAACLLGMFLH), [])]
        [HarmonyPostfix]
        public static void OAACLLGMFLHPostfix(HDLIJDBFGKN __instance)
        {
            if (StateManager.AllPeersConfirmed())
            {
                StateManager.SendAllGroupMessage();
            }
        }

        //resets peer mod state when entering an online lobby
        //GameStatesLobbyOnline.KAfterOpen
        [HarmonyPatch(typeof(HDLIJDBFGKN), nameof(HDLIJDBFGKN.DJLJONJDDDO))]
        [HarmonyPostfix]
        public static IEnumerator DJLJONJDDDOPostfix(IEnumerator __result, HDLIJDBFGKN __instance)
        {
            while (__result.MoveNext()) yield return __result.Current;

            //GameStatesLobbyOnline.startOnline; is set when loading into an online lobby for the first time
            if (__instance.FBJIDODJNFN)
            {
                StateManager.ResetState();
            }
            else
            {
                StateManager.ResetMode();
            }
            NetUtils.ResetMaxPing();
        }

        //default behaviour adds a ping of 80 when resetting ping, which gives a bad reading for the first 4 seconds.
        //need to use ping to calculate some stuff, so we change that
        //TODO does this even work? idk. not really needed anyway since i think loading the game always takes at least 4 seconds anyway
        [HarmonyPatch(typeof(Peer), nameof(Peer.ResetPing))]
        [HarmonyPostfix]
        public static void ResetPingPostfix(Peer __instance)
        {
            __instance.pingsPrev[0] = -1;
            __instance.ping = 0f;
        }

    }
}
