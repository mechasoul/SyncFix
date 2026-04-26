using SyncFix.Utils;
using HarmonyLib;
using Multiplayer;
using UnityEngine;

namespace SyncFix.Patches
{
    /// <summary>
    /// contains harmony patches used for implementing the new "solo" time sync logic. while generally less effective than
    /// the group algorithm, this has the advantage of being able to fix host advantage even when not all players have the
    /// mod installed
    /// </summary>
    [HarmonyPatch]
    public class TimeSyncSolo_Patches
    {
        //half of the fix for the case where the player is client and the host doesn't have this mod.
        //just run vanilla AlignTimes ourselves. for the other half see LocalPeer.OnReceiveMessage patch
        /*
         * some more indepth remarks on how this works:
         * in a 2p game, it's pretty straightforward. if we're the host, then we:
         *   - improve initial time alignment
         *   - delay self-timesync messages by approximately the same amount as the client
         *   - use a more generous estimate for client's remote frame to reduce inherent self-bias
         * together, these 3 things significantly improve/effectively eliminate host advantage in most cases.
         * if we're the client, then we just emulate what the vanilla host is doing: we calculate our own time sync
         * based on our own estimates, and ignore any time sync messages from the host. we don't fix the asymmetry at match
         * start, unfortunately. TODO this is maybe fixable by adding delay to received timed messages proportional to the erroneous 
         * added delay to the host?
         * in a 3/4p game, it's a bit more complicated. if we're the host, it works the same, and any clients with the mod
         * will just operate as if they didn't, since the host fixes host advantage by themselves. if we're the client and 
         * host does not have the mod, then we calculate our own time sync as above. this removes host advantage for us, 
         * but other clients without the mod will still experience host advantage. i don't think there's anything we can
         * do about that?
         */
        [HarmonyPatch(typeof(Sync), nameof(Sync.AlignTimes))]
        [HarmonyPostfix]
        public static void AlignTimesPostfix(Sync __instance)
        {
            if (!SyncFixConfig.Instance.Enabled) return;

            //curFrame % 60 == 0 check is just there because it's there in vanilla
            if (!P2P.isHost && !StateManager.HostHasSyncFix && Sync.curFrame % 60 == 0)
            {
                SelfVanillaAlignTimes();
            }
        }

        //exact same logic as vanilla AlignTimes, applied only to ourselves
        private static void SelfVanillaAlignTimes()
        {
            float fixedDeltaTime = Time.fixedDeltaTime;
            float currentTime = Sync.curFrame * fixedDeltaTime;
            float minTime = currentTime;
            for (int j = 0; j < Sync.nPlayers; j++)
            {
                Sync.OtherInfo otherInfo = Sync.othersInfo[j];
                if (otherInfo != null)
                {
                    float otherTime = Sync.statusInput.otherReceived[j] * fixedDeltaTime;
                    otherTime += otherInfo.peer.ping * 0.5f;
                    if (otherTime < minTime)
                    {
                        minTime = otherTime;
                    }
                }
            }

            float aheadTime = currentTime - minTime;
            aheadTime *= 0.75f;
            if (aheadTime >= 0.02f)
            {
                aheadTime = Mathf.Min(aheadTime, 0.5f);
                //Plugin.Logger.LogInfo($"sending self-timesync with time: {aheadTime}");
                P2P.SendToPlayerNr(P2P.localPeer.playerNr, new Message(Msg.P2P_TIME_ALIGN, Sync.matchNr, Mathf.RoundToInt(aheadTime * 1000f), null, -1));
            }
        }

        //drops P2P_TIME_ALIGN messages from other players if host doesn't have sync fix (we manage our own time alignment)
        [HarmonyPatch(typeof(LocalPeer), nameof(LocalPeer.OnReceiveMessage))]
        [HarmonyPrefix]
        public static bool RedirectOnReceiveMessage(LocalPeer __instance, Envelope envelope)
        {
            if (!SyncFixConfig.Instance.Enabled) return true;

            if (envelope.message.msg == Msg.P2P_TIME_ALIGN && envelope.sender != P2P.localPeer.peerId && !StateManager.HostHasSyncFix)
            {
                //Plugin.Logger.LogInfo("dropping remote time alignment message");
                return false;
            }
            return true;
        }

        //float overload is where the ping actually gets updated, but int overload is the more top-level method, so use that
        [HarmonyPatch(typeof(Peer), nameof(Peer.ResolvePing), [typeof(int)])]
        [HarmonyPostfix]
        public static void ResolvePingPostfix(Peer __instance)
        {
            NetUtils.UpdateMaxPing(__instance);
        }
    }
}
