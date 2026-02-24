using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Multiplayer;

namespace BlazeSyncFix.Patches
{
    /// <summary>
    /// contains patches for monitoring player mod state in lobby, so we know which time sync fix to use
    /// </summary>
    [HarmonyPatch]
    public class LobbyState_Patches
    {
        ////patches PlatformSteam.Notify, PlatformDev.Notify (for devnet)
        //[HarmonyPatch(typeof(KKMGLMJABKH), nameof(KKMGLMJABKH.BANACEJFPKH))]
        //[HarmonyPatch(typeof(LBLHOCKALBK), nameof(LBLHOCKALBK.BANACEJFPKH))]
        //[HarmonyPostfix]
        //public static void BANACEJFPKHPostfix(KKMGLMJABKH __instance, PlatformNotification JGLJENHAMIM, float BKPKJLGNIMB = -1f)
        //{
        //    //note that we also do stuff on peer join/leave, except we do it from elsewhere since we need info for the relevant peer.
        //    //see LocalHost patches below
        //    switch (JGLJENHAMIM)
        //    {
        //        case PlatformNotification.LOBBY_ENTERED:
        //            StateManager.ResetState();
        //            break;
        //        case PlatformNotification.LOBBY_REENTERED:
        //            //keep player state when reentering lobby. just reset mode (it'll be resent at game start if still valid)
        //            StateManager.ResetMode();
        //            break;
        //        default:
        //            break;
        //    }
        //}

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

        //GameStatesLobbyOnline.StartGame
        [HarmonyPatch(typeof(HDLIJDBFGKN), nameof(HDLIJDBFGKN.OAACLLGMFLH), [])]
        [HarmonyPostfix]
        public static void OAACLLGMFLHPostfix(HDLIJDBFGKN __instance)
        {
            //parameterless StartGame is only called by host on setup, so send ggpo message here if appropriate
            if (StateManager.AllPeersConfirmed())
            {
                StateManager.SendAllGGPOMessage();
            }
        }

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
        }

        //default behaviour adds a ping of 80 when resetting ping, which gives a bad reading for the first 4 seconds.
        //need to use ping to calculate some stuff, so we change that
        [HarmonyPatch(typeof(Peer), nameof(Peer.ResetPing))]
        [HarmonyPostfix]
        public static void ResetPingPostfix(Peer __instance)
        {
            __instance.pingsPrev[0] = -1;
            __instance.ping = 0f;
        }

    }
}
