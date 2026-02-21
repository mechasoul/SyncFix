using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using BlazeSyncFix.Utils;
using HarmonyLib;
using LLBML.Players;
using LLScreen;
using Multiplayer;
using UnityEngine;
using UnityEngine.Assertions;

namespace BlazeSyncFix.Patches
{
    /// <summary>
    /// contains harmony patches used for implementing the new ggpo time sync logic. ggpo's time sync algorithm requires
    /// all players to keep track of their current local advantage vs other players (based on the player's current frame
    /// and estimates of the other players' current frames), and to also periodically send this current local advantage
    /// to all other players. this gives better results than having a single player handle everyone's timing as in llb,
    /// but it does require all players to participate. this means that ggpo algorithm can't be used unless all players
    /// have this mod installed.
    /// 
    /// we do make a best-effort attempt to even things out in the case that not all players have the mod installed. for
    /// those patches, see TimeSyncSimple_Patches.
    /// </summary>
    [HarmonyPatch]
    public class TimeSyncGGPO_Patches
    {
        //this is the big one. a few cases here, depending on whether other players have the patch or not
        [HarmonyPatch(typeof(Sync), nameof(Sync.AlignTimes))]
        [HarmonyPrefix]
        public static bool RedirectAlignTimes()
        {
            if (!SyncFixConfig.Instance.Enabled) return true;

            if (StateManager.IsUsingGGPO())
            {
                //entirely replace the original Sync.AlignTimes with our new version
                SyncFixManager.Instance.UpdateFrameAdvantage(Sync.curFrame);
                SyncFixManager.Instance.GGPOAlignTimes();
                return false;
            }
            else if (P2P.isHost)
            {
                SyncFixManager.Instance.SimpleAlignTimes();
                return false;
            }
            /*
             * - if we're client and host doesn't have sync fix:
             * defensively manage our own time alignment so we're not punished by host advantage. can't do anything about 
             * the other clients, every man for himself
             * manage by running our own self-only version of AlignTimes in postfix and ignoring received time alignments.
             * - if we're client and host has sync fix:
             * don't do anything. host will handle it via their self-applied delay, to try to ensure fairness for all 
             * clients regardless of whether they have sync fix.
             * 
             * in any case, we run AlignTimes.
             */
            return true;
        }

        [HarmonyPatch(typeof(Sync), nameof(Sync.Init))]
        [HarmonyPostfix]
        public static void InitPostfix()
        {
            SyncFixManager.Instance.Reset();
        }

        /*
         * quick note on this one: ping is a measure of round trip time. it's the time it takes to send a message to a peer
         * plus the time it takes for their response to arrive. so when we send a message to a peer, our best estimate for
         * how long that will take is ping / 2 (realistically it won't be exactly this because of network asymmetry but it's
         * the best we can do).
         * LocalPeer.SendAllTimed calculates delay by ping (round trip time) instead of half ping, which causes delays to
         * be roughly twice what they should be.
         * ex p1 is host and has 70 ping to p2 and 200 ping to p3 and sends a timed message. the game calculates delay as
         * (max ping - player ping): ie, 200 for p1, 130 for p2, and 0 for p3. but as described above, these messages will
         * arrive after roughly 0ms, 35ms, and 100ms; so from the SendAllTimed call, these messages are fired after a 
         * total of roughly 200ms, 165ms, and 100ms. bad
         * if we divide the final number by 2, then we end up with delays of 100 for p1, 65 for p2, and 0 for p3. add their
         * one-way-trip time of 0, 35, and 100 and we get 100 for all 3 players. great!
         * this impacts all timed messages but we're mainly concerned about the effect it has on the MP_START_SYNC message
         * (LocalHost.SendStartSignal). this is what actually triggers Sync to start -> the game to start running in an 
         * online match, so this timing inaccuracy will cause host to run behind everyone for a bit at game start (this is
         * an advantage; running behind is beneficial in rollback).
         * 
         * ultimately the effects are minor because time sync will kick in soon enough and fix any large difference in timing,
         * but it does give host a slight advantage at the start of the match. fix it by halving the delays used for all 
         * P2P_TIMED messages (bonus: this doesn't require any client-side changes so it'll work for all peers even if they 
         * don't have the mod, so long as host does)
         * 
         * i'd like to patch LocalPeer.SendAllTimed directly but the time is calculated in a delegate and patching those 
         * seems kind of dicey so we'll just do this instead
         */
        [HarmonyPatch(typeof(LocalPeer), nameof(LocalPeer.SendToPlayerNr))]
        [HarmonyPrefix]
        public static bool RedirectSendToPlayerNr(LocalPeer __instance, ref Message message)
        {
            //hopefully no one else has made this change somewhere or this would overcorrect.
            //could be fixed by recalculating everything but im sure its fine
            if (message.msg == Msg.P2P_TIMED)
            {
                JKMAAHELEMF vector2i = (JKMAAHELEMF)message.ob;
                //adding the extra frame is beneficial from my testing. not sure why. time taken for unity to poll received messages?
                vector2i.CGJJEHPPOAN = (vector2i.CGJJEHPPOAN / 2) + (int)(World.DELTA_TIME * 1000);
                message = new Message(message.msg, message.playerNr, message.index, vector2i, message.obSize);
            }
            return true;
        }

        //patches GameStatesGame.ProcessMsgGame. sets the time for first recommended sleep / advantage send
        //wanted to patch this as Sync.Start postfix but i think Start gets inlined because it's so simple. so just transpiler it in instead
        [HarmonyPatch(typeof(OGONAGCFDPK), nameof(OGONAGCFDPK.IMEGGOOAADG))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> IMEGGOOAADGTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            CodeMatcher cm = new CodeMatcher(instructions);
            cm.MatchStartForward(new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Sync), nameof(Sync.Start))));
            cm.Advance(1).Insert(
                Transpilers.EmitDelegate(() =>
                {
                    if (SyncFixConfig.Instance.Enabled)
                    {
                        SyncFixManager.Instance.Start();
                    }
                }));
            return cm.InstructionEnumeration();
        }

        /*
         * ggpo sends local advantage as a part of ping messages. i'm just going to send them separately, because piggybacking them onto
         * ping messages is very invasive & hacky (have to replace obSize or something) and might interfere with stuff. 
         * but still send them at the same timing (ie, from P2P.Update)
         */
        [HarmonyPatch(typeof(P2P), nameof(P2P.Update))]
        [HarmonyPostfix]
        public static void UpdatePostfix(P2P __instance)
        {
            //require Sync.curFrame > SyncExtended.LastSleep + 1 so we don't send outdated advantage immediately after a sleep in edge cases
            if (P2P.isPinging && Sync.isActive && Sync.curFrame > SyncFixManager.Instance.NextAdvantageUpdate && Sync.curFrame > SyncFixManager.Instance.LastSleep + 1)
            {
                if (StateManager.IsUsingGGPO())
                {
                    //send advantage to all players
                    SyncFixManager.ForAllValidOthers(i => SyncFixManager.Instance.SendLocalAdvantageToPlayer(i));
                    SyncFixManager.Instance.UpdateNextAdvantageTime();
                }
                //else if (!P2P.isHost && StateManager.HostHasSyncFix)
                //{
                //    //send advantage to host
                //    SyncFixManager.Instance.SendLocalAdvantageToPlayer(0);
                //    SyncFixManager.Instance.UpdateNextAdvantageTime();
                //}
                //else if (P2P.isHost)
                //{
                //    SyncFixManager.ForAllValidOthers(i =>
                //    {
                //        if (!StateManager.PeerHasMod(i))
                //        {
                //            float remoteFrame = SyncFixManager.EstimateRemoteFrame(i);
                //            //this is (host's frame - peer i's local frame); ie, peer i's local advantage
                //            float remoteAdvantage = Sync.curFrame - remoteFrame;
                //            SyncFixManager.UpdateRemoteAdvantage(i, remoteAdvantage);
                //        }
                //    });
                //}
            }
        }

        [HarmonyPatch(typeof(P2P), nameof(P2P.CWait))]
        [HarmonyPostfix]
        public static IEnumerator CWaitPostfix(IEnumerator __result, P2P __instance)
        {
            while (__result.MoveNext()) yield return __result.Current;

            SyncFixManager.Instance.StopTimer();
        }


        //[HarmonyPatch(typeof(P2P), nameof(P2P.CWait))]
        //[HarmonyPrefix]
        //public static bool RedirectCWait(P2P __instance, float wait, ref IEnumerator __result)
        //{
        //    Console.WriteLine("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        //    __result = CWait_Helper(__instance, wait);
        //    return false;
        //}

        //private static IEnumerator CWait_Helper(P2P instance, float wait)
        //{
        //    SyncFixManager.Instance.StartTimer();
        //    yield return new WaitForSeconds(wait);
        //    SyncFixManager.Instance.StopTimer();
        //    Console.WriteLine("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB");
        //    if (!Sync.isAwaiting)
        //    {
        //        OGONAGCFDPK.PFDNCNGCEDB(false);
        //    }
        //    P2P.coroutineWait = null;
        //    yield break;
        //}

        //insert call to update stuff on sleep. we transpiler instead of postfix for edge-case scenarios where the sleep is skipped, 
        //so we only update if a sleep actually occurs
        [HarmonyPatch(typeof(P2P), nameof(P2P.Wait))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> WaitTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            CodeMatcher cm = new CodeMatcher(instructions);
            cm.End();
            cm.Insert(
                new CodeInstruction(OpCodes.Ldarg_0),
                Transpilers.EmitDelegate((float f) =>
                {
                    if (SyncFixConfig.Instance.Enabled)
                    {
                        SyncFixManager.Instance.StartTimer();
                        SyncFixManager.Instance.OnSleep(f);
                    }
                }));
            return cm.InstructionEnumeration();
        }
    }
}
