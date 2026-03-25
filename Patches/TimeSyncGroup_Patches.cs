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
    /// contains harmony patches used for implementing the new ggpo-based time sync logic. ggpo's time sync algorithm requires
    /// all players to keep track of their current local advantage vs other players (based on the player's current frame
    /// and estimates of the other players' current frames), and to also periodically send this current local advantage
    /// to all other players. this gives better results than having a single player handle everyone's timing as in llb,
    /// but it does require all players to participate. this means that ggpo's algorithm can't be used unless all players
    /// have this mod installed, hence i'm calling this the group algorithm.
    /// 
    /// we do make a best-effort attempt to even things out in the case that not all players have the mod installed. for
    /// those patches, see TimeSyncSolo_Patches.
    /// </summary>
    [HarmonyPatch]
    public class TimeSyncGroup_Patches
    {
        //main entry point for most changed logic. a few cases here, depending on whether other players have the patch or not
        [HarmonyPatch(typeof(Sync), nameof(Sync.AlignTimes))]
        [HarmonyPrefix]
        public static bool RedirectAlignTimes()
        {
            if (!SyncFixConfig.Instance.Enabled) return true;

            if (StateManager.IsUsingGroup())
            {
                //entirely replace the original Sync.AlignTimes with our new version
                SyncFixManager.Instance.GroupAlignTimes();
                return false;
            }
            else if (P2P.isHost)
            {
                if (Sync.curFrame < 60) return false;
                SyncFixManager.Instance.SoloHostAlignTimes();
                return false;
            }
            /*
             * else, we're client and not using group syncfix. we have the following cases:
             * - if we're client and host doesn't have sync fix:
             * defensively manage our own time alignment so we're not punished by host advantage. can't do anything about 
             * the other clients, every man for himself
             * manage by running our own self-only version of AlignTimes in postfix and ignoring received time alignments.
             * - if we're client and host has sync fix:
             * don't do anything. host will handle it via the solo host syncfix logic, to try to ensure fairness for all 
             * clients regardless of whether they have sync fix.
             * 
             * in any case, we don't need to do anything special here.
             */
            return true;
        }

        [HarmonyPatch(typeof(Sync), nameof(Sync.Init))]
        [HarmonyPostfix]
        public static void InitPostfix()
        {
            SyncFixManager.Instance.Reset();
        }

        //fix for incorrect initial time alignment (minor cause of unfairness alongside AlignTimes)
        /*
         * note on this one: ping is a measure of round trip time. it's the time it takes to send a message to a peer
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
         * but it does give host a slight advantage at the start of the match. (could also have a measurable impact at certain
         * low pings where the incorrect timings isn't enough to trigger a sleep but is enough to give host a consistent advantage).
         * fix it by halving the delays used for all 
         * P2P_TIMED messages (bonus: this doesn't require any client-side changes so it'll work for all peers even if they 
         * don't have the mod, so long as host does)
         * 
         * i'd like to patch LocalPeer.SendAllTimed directly but the time is calculated in a delegate and patching those 
         * seems kind of dicey so we'll just do this instead
         */
        [HarmonyPatch(typeof(LocalPeer), nameof(LocalPeer.SendToPlayerNr))]
        [HarmonyPrefix]
        public static bool RedirectSendToPlayerNr(LocalPeer __instance, int receiverPlayerNr, ref Message message)
        {
            if (!SyncFixConfig.Instance.Enabled) return true;

            //hopefully no one else has made this change somewhere or this would overcorrect.
            //could be fixed by recalculating everything but im sure its fine
            if (message.msg == Msg.P2P_TIMED)
            {
                JKMAAHELEMF vector2i = (JKMAAHELEMF)message.ob;
                //time here is in milliseconds. one frame is World.DELTA_TIME * 1000. extra 30f helps stabilize things at game start
                // -> impact may actually be pretty minor. could maybe remove the extra delay...?
                vector2i.CGJJEHPPOAN = (int)(vector2i.CGJJEHPPOAN * 0.5f + 30000f * World.DELTA_TIME);
                message = new Message(message.msg, message.playerNr, message.index, vector2i, message.obSize);
            }
            return true;
        }

        //patches GameStatesGame.ProcessMsgGame. sets the time for first recommended sleep / advantage send
        //wanted to patch this as Sync.Start postfix but i think Start gets inlined because it's so simple. just transpiler it in instead
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
         * ggpo sends local advantage as a part of ping messages. technically we could do the same and avoid adding any extra
         * network traffic by like, replacing obSize or something? but that's really hacky and might interfere with stuff.
         * just send them at the same timing (ie, from P2P.Update)
         */
        [HarmonyPatch(typeof(P2P), nameof(P2P.Update))]
        [HarmonyPostfix]
        public static void UpdatePostfix(P2P __instance)
        {
            if (!SyncFixConfig.Instance.Enabled) return;

            //require Sync.curFrame > SyncExtended.LastSleep + 1 so we don't send outdated advantage immediately after a sleep in edge cases
            if (P2P.isPinging && Sync.isActive && Sync.curFrame > SyncFixManager.Instance.NextAdvantageUpdate && Sync.curFrame > SyncFixManager.Instance.LastSleep + 1)
            {
                if (StateManager.IsUsingGroup())
                {
                    //send advantage to all players
                    SyncFixManager.ForAllValidOthers(i => SyncFixManager.Instance.SendLocalAdvantageToPlayer(i));
                    SyncFixManager.Instance.UpdateNextAdvantageTime();
                }
            }
        }

        //debug for recording actual wait times
        //[HarmonyPatch(typeof(P2P), nameof(P2P.CWait))]
        //[HarmonyPostfix]
        //public static IEnumerator CWaitPostfix(IEnumerator __result, P2P __instance)
        //{
        //    while (__result.MoveNext()) yield return __result.Current;

        //    if (SyncFixConfig.Instance.Enabled) SyncFixManager.Instance.StopTimer();
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
                        //SyncFixManager.Instance.StartTimer();
                        SyncFixManager.Instance.OnSleep(f);
                    }
                }));
            return cm.InstructionEnumeration();
        }
    }
}
