using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using BlazeSyncFix.Utils;
using HarmonyLib;
using LLBML.Players;
using Multiplayer;
using UnityEngine;

namespace BlazeSyncFix.Patches
{
    /// <summary>
    /// contains harmony patches used for implementing the new "solo" time sync logic. while generally less effective than
    /// the group algorithm, this has the advantage of being able to fix host advantage even when not all players have the
    /// mod installed
    /// </summary>
    [HarmonyPatch]
    public class TimeSyncSolo_Patches
    {
        //public static float PING_ESTIMATE = 0.5f;


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
                Plugin.Logger.LogInfo($"sending self-timesync with time: {aheadTime}");
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
                Plugin.Logger.LogInfo("dropping remote time alignment message");
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


        //      i just redirected aligntimes lmao

        //have to factor the config option into every change so we can run vanilla if toggled off. adding a toggle was a mistake
        //adds a check to skip sleeping too early because that has weird consequences,
        //uses a more generous factor for ping when estimating remote frame,
        //replaces the vanilla align times factor with our own and moves it after the min sleep check,
        //and adds logic to delay time alignment messages sent to ourselves, to try to even that out with peers
        //[HarmonyPatch(typeof(Sync), nameof(Sync.AlignTimes))]
        //[HarmonyTranspiler]
        //public static IEnumerable<CodeInstruction> AlignTimesTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator iLGenerator)
        //{
        //    CodeMatcher cm = new CodeMatcher(instructions);
        //    //add check to skip method if Sync.curFrame <= 0
        //    cm.End();
        //    Label retLabel = cm.Instruction.labels[0];
        //    cm.Start();
        //    cm.MatchStartForward(new CodeMatch(OpCodes.Brtrue))
        //        .Advance(1);
        //    Label continueLabel = iLGenerator.DefineLabel();
        //    cm.Instruction.labels.Add(continueLabel);
        //    cm.Insert(new InstructionBuilder()
        //        .OpCode(OpCodes.Call).Operand(AccessTools.DeclaredPropertyGetter(typeof(SyncFixConfig), nameof(SyncFixConfig.Instance)))
        //        .OpCode(OpCodes.Call).Operand(AccessTools.DeclaredPropertyGetter(typeof(SyncFixConfig), nameof(SyncFixConfig.Enabled)))
        //        .OpCode(OpCodes.Brfalse).Operand(continueLabel)
        //        .OpCode(OpCodes.Call).Operand(AccessTools.DeclaredPropertyGetter(typeof(Sync), nameof(Sync.curFrame)))
        //        .OpCode(OpCodes.Ldc_I4_S).Operand((sbyte)60)
        //        .OpCode(OpCodes.Blt).Operand(retLabel)
        //        .Build());
        //    //replaces 0.5 ping factor with ours
        //    cm.MatchStartForward(new CodeMatch(OpCodes.Ldc_R4, 0.5f))
        //        .SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TimeSyncSolo_Patches), nameof(TimeSyncSolo_Patches.GetPingFactor))));
        //    //add conditional to jump over num3 *= 0.75f line
        //    cm.MatchStartForward(new InstructionBuilder()
        //        //TODO why was this match not working? what's the correct type for ldloc.s operand?
        //        //.OpCode(OpCodes.Ldloc_S).Operand((sbyte)8)
        //        .OpCode(OpCodes.Ldc_R4).Operand(0.02f)
        //        .BuildAsMatch());
        //    cm.Advance(-1);
        //    Label sleepCheckLabel = iLGenerator.DefineLabel();
        //    cm.Instruction.labels.Add(sleepCheckLabel);
        //    cm.MatchStartBackwards(new CodeMatch(OpCodes.Ldc_R4, 0.75f))
        //        .Advance(-1);
        //    cm.Insert(new InstructionBuilder()
        //        .OpCode(OpCodes.Call).Operand(AccessTools.DeclaredPropertyGetter(typeof(SyncFixConfig), nameof(SyncFixConfig.Instance)))
        //        .OpCode(OpCodes.Call).Operand(AccessTools.DeclaredPropertyGetter(typeof(SyncFixConfig), nameof(SyncFixConfig.Enabled)))
        //        .OpCode(OpCodes.Brtrue).Operand(sleepCheckLabel)
        //        .Build());
        //    cm.MatchStartForward(new CodeMatch(OpCodes.Ldc_R4, 0.5f))
        //        .Advance(-1);
        //    //there's a jump to this line & we're inserting before it. move the label; insert our num3 *= factor line
        //    Label existingLabel = cm.Instruction.labels[0];
        //    cm.Instruction.labels.Clear();
        //    Label newLabel = iLGenerator.DefineLabel();
        //    cm.Instruction.labels.Add(newLabel);
        //    cm.Insert(new InstructionBuilder()
        //        .OpCode(OpCodes.Call).Operand(AccessTools.DeclaredPropertyGetter(typeof(SyncFixConfig), nameof(SyncFixConfig.Instance)))
        //        .OpCode(OpCodes.Call).Operand(AccessTools.DeclaredPropertyGetter(typeof(SyncFixConfig), nameof(SyncFixConfig.Enabled)))
        //        .OpCode(OpCodes.Brfalse).Operand(newLabel)
        //        .OpCode(OpCodes.Ldloc_S).Operand((sbyte)8)
        //        .OpCode(OpCodes.Ldc_R4).Operand(SyncFixManager.SIMPLE_VANILLA_ALIGN_TIMES_FACTOR)
        //        .OpCode(OpCodes.Mul)
        //        .OpCode(OpCodes.Stloc_S).Operand((sbyte)8)
        //        .Build());
        //    cm.Instruction.labels.Add(existingLabel);
        //    //remove the entire P2P.SendToPlayerNr call and replace it with our new method
        //    cm.MatchStartForward(new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(P2P), nameof(P2P.SendToPlayerNr))));
        //    int endPos = cm.Pos;
        //    cm.MatchStartBackwards(new CodeMatch(OpCodes.Ldc_I4, (int)Msg.P2P_TIME_ALIGN))
        //        .Advance(-1)
        //        .RemoveInstructionsInRange(cm.Pos, endPos);
        //    cm.Insert(new InstructionBuilder()
        //        .OpCode(OpCodes.Ldloc_S).Operand((sbyte)7) //loop variable = player index
        //        .OpCode(OpCodes.Ldloc_S).Operand((sbyte)8) //the calculated delay
        //        .OpCode(OpCodes.Call).Operand(AccessTools.Method(typeof(TimeSyncSolo_Patches), nameof(TimeSyncSolo_Patches.SendTimeAlignMessage)))
        //        .Build());
        //    return cm.InstructionEnumeration();
        //}

        //private static float GetPingFactor()
        //{
        //    if (SyncFixConfig.Instance.Enabled) return PING_ESTIMATE;
        //    else return 0.5f;
        //}

        //public static void SendTimeAlignMessage(int playerIndex, float time)
        //{
        //    if (!StateManager.IsUsingGroup())
        //    {
        //        if (SyncFixConfig.Instance.Enabled && playerIndex == P2P.localPeer.playerNr)
        //        {
        //            //send message to self. artifically delay the send. use average ping.
        //            //median might be preferable but it's probably negligible
        //            float selfDelay = Player.EPlayers()
        //                .Where(player => player.NGLDMOLLPLK && Sync.IsValidOther(player.CJFLMDNNMIE)) //player.inMatch && Sync.IsValidOther(player.nr)
        //                .Select(player => player.KLEEADMGHNE.ping) //player.peer.ping
        //                .Average();
        //            selfDelay /= 2; //adjust for one-way trip time
        //            Plugin.Logger.LogInfo($"sleeping self for {time}");
        //            P2P.instance.StartCoroutine(CSendSelfTimeAlignAfterDelay(selfDelay, time));
        //        }
        //        else
        //        {
        //            //send message as normal to other player
        //            Plugin.Logger.LogInfo($"sleeping p{playerIndex + 1} for {time}");
        //            P2P.SendToPlayerNr(playerIndex, new Message(Msg.P2P_TIME_ALIGN, Sync.matchNr, Mathf.RoundToInt(time * 1000f), null, -1));
        //        }
        //    }
        //    else
        //    {
        //        throw new InvalidOperationException("tried to send P2P_ALIGN_TIMES when using GGPO?");
        //    }
        //}

        //private static IEnumerator CSendSelfTimeAlignAfterDelay(float initialDelay, float time)
        //{
        //    Plugin.Logger.LogInfo($"delaying self-timesync by {initialDelay}");
        //    yield return new WaitForSeconds(initialDelay);
        //    P2P.SendToPlayerNr(P2P.localPeer.playerNr, new Message(Msg.P2P_TIME_ALIGN, Sync.matchNr, Mathf.RoundToInt(time * 1000f), null, -1));
        //    yield break;
        //}
    }
}
