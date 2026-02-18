using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BlazeSyncFix.Patches;
using LLBML.Messages;
using LLBML.Players;
using Multiplayer;
using UnityEngine;

namespace BlazeSyncFix
{
    /// <summary>
    /// functions similarly to vanilla Sync class, but with some extended functionality. manages sync information
    /// in-game
    /// </summary>
    public class SyncFixManager
    {
        private static readonly SyncFixManager instance = new SyncFixManager();

        public static readonly int SLEEP_CHECK_INTERVAL = 60; //frames in between sleep checks
        public static readonly int ADVANTAGE_UPDATE_INTERVAL = 60; //frames in between sending advantage to peers
        private TimeSync[] timeSync = [new TimeSyncHost(0), new TimeSync(1), new TimeSync(2), new TimeSync(3)];
        private int nextAdvantageUpdate = int.MaxValue;
        private int lastAdvantageUpdate = -1;
        public int NextAdvantageUpdate { get => nextAdvantageUpdate; }
        public int LastAdvantageUpdate { get => lastAdvantageUpdate; }
        private int nextRecommendedSleep = int.MaxValue;
        private int lastSleep = -1;

        static SyncFixManager() { }

        public static SyncFixManager Instance { get { return instance; } }

        public int NextRecommendedSleep { get => nextRecommendedSleep; }
        public int LastSleep { get => lastSleep; }

        public static void RegisterGameMessages()
        {
            MessageApi.RegisterCustomMessage(Plugin.Instance.Info, (ushort)SyncFixMessages.GAME_LOCAL_ADVANTAGE, SyncFixMessages.GAME_LOCAL_ADVANTAGE.ToString(),
                SyncFixManager.Instance.ReceiveRemoteAdvantage);
        }

        /// <summary>
        /// call before doing stuff ingame, eg from Sync.Init
        /// </summary>
        public void Reset()
        {
            if (!SyncFixConfig.Instance.Enabled) return;

            for (int i = 0; i < Sync.nPlayers; i++)
            {
                timeSync[i].Reset();
            }
            nextAdvantageUpdate = int.MaxValue;
            lastAdvantageUpdate = -1;
            nextRecommendedSleep = int.MaxValue;
            lastSleep = -1;
        }

        /// <summary>
        /// sets important values at start time. corresponds to Sync.Start
        /// </summary>
        public void Start()
        {
            if (!SyncFixConfig.Instance.Enabled) return;

            UpdateNextRecommendedSleep();
            UpdateNextAdvantageTime();
        }

        /// <summary>
        /// call sometime after game has started (eg on Sync.Start, except i think that gets inlined so don't do that) to start time sync checks. 
        /// also call whenever a sleep is performed
        /// </summary>
        public void UpdateNextRecommendedSleep()
        {
            nextRecommendedSleep = Sync.curFrame + SLEEP_CHECK_INTERVAL;
        }

        public void UpdateLastSleep()
        {
            lastSleep = Sync.curFrame;
        }

        public void OnSleep()
        {
            UpdateNextRecommendedSleep();
            UpdateLastSleep();
        }

        /// <summary>
        /// call sometime after game has started (eg on Sync.Start, except i think that gets inlined so don't do that) to start sending advantage.
        /// also call whenever advantage is sent
        /// </summary>
        public void UpdateNextAdvantageTime()
        {
            nextAdvantageUpdate = Sync.curFrame + ADVANTAGE_UPDATE_INTERVAL;
        }

        //
        /// <summary>
        /// returns the current recommended sleep interval (in seconds) based on local advantage vs the given remote player index and their provided advantage vs us.
        /// </summary>
        /// <param name="playerIndex"></param>
        /// <returns>the recommended sleep duration to even out advantage vs this player. if the recommended sleep duration is lower than TimeSync.MIN_SLEEP_DURATION,
        /// then 0 is returned instead. the returned value can't be higher than TimeSync.MAX_SLEEP_DURATION</returns>
        /// <remarks>note it's assumed that any nonzero value returned by this method will immediately be used for a sleep. consequently, if this returns nonzero, then
        /// current local advantage will be reset, under the assumption that a sleep is occurring and so the value is now stale. something to be aware of</remarks>
        public float GetRecommendedSleepInterval(int playerIndex)
        {
            if (!SyncFixConfig.Instance.Enabled) throw new InvalidOperationException("asked for sleep interval when sync fix disabled?");

            return timeSync[playerIndex].GetSleepInterval();
        }

        /// <summary>
        /// returns the current local advantage (in frames) vs the given remote player index
        /// </summary>
        /// <param name="playerIndex"></param>
        /// <returns></returns>
        public float GetCurrentLocalAdvantage(int playerIndex)
        {
            if (!SyncFixConfig.Instance.Enabled) throw new InvalidOperationException("asked for local advantage when sync fix disabled?");

            return timeSync[playerIndex].CurrentLocalAdvantage;
        }

        /// <summary>
        /// update current local advantage vs all players for the given frame. ggpo calls after input/rollback checking, so a corresponding timing for llb is like Sync.AlignTimes prefix
        /// </summary>
        /// <param name="frame"></param>
        public void UpdateLocalAdvantage(int frame)
        {
            if (!SyncFixConfig.Instance.Enabled) return;

            ForAllValidOthers(i => timeSync[i].UpdateCurrentLocalFrameAdvantage(frame));
        }

        public void SendLocalAdvantageToPlayer(int i)
        {
            if (!SyncFixConfig.Instance.Enabled) return;

            byte[] bytes = BitConverter.GetBytes(GetCurrentLocalAdvantage(i));
            Message toSend = new Message((Msg)SyncFixMessages.GAME_LOCAL_ADVANTAGE, P2P.localPeer.playerNr, -1,
                bytes, bytes.Length);
            Plugin.Logger.LogInfo($"sending local advantage: {toSend}");
            P2P.SendToPlayerNr(i, toSend);
        }

        public void ReceiveRemoteAdvantage(Message message)
        {
            if (!SyncFixConfig.Instance.Enabled) return;

            float adv = BitConverter.ToSingle((byte[])message.ob, 0);
            Plugin.Logger.LogInfo($"received remote adv: {adv}");
            UpdateRemoteAdvantage(message.playerNr, adv);
        }

        /// <summary>
        /// update the current remote advantage vs us for the given remote player index. call immediately whenever a peer provides their advantage vs us
        /// </summary>
        /// <param name="playerIndex"></param>
        /// <param name="remoteAdvantage">the provided remote advantage, in frames</param>
        public void UpdateRemoteAdvantage(int playerIndex, float remoteAdvantage)
        {
            timeSync[playerIndex].UpdateCurrentRemoteFrameAdvantage(remoteAdvantage);
        }

        /// <summary>
        /// record the local and remote frame advantage for a given remote player index for the given frame. uses current local and remote advantage. 
        /// these records are used to calculate an average. these averages are used to determine the final numbers for sleep intervals. ggpo calls whenever input is sent, so hook into like Sync.SendSync
        /// </summary>
        /// <param name="playerIndex"></param>
        /// <param name="frame"></param>
        //
        public void RecordAdvantage(int playerIndex, int frame)
        {
            if (!SyncFixConfig.Instance.Enabled) return;

            timeSync[playerIndex].RecordFrameAdvantage(frame);
        }



        //replacement for llb's Sync.AlignTimes based on the ggpo time sync algorithm
        public void GGPOAlignTimes()
        {
            //Console.WriteLine($"align times; curFrame: {Sync.curFrame}, next sleep: {SyncExtended.NextRecommendedSleep}");
            if (Sync.curFrame > NextRecommendedSleep)
            {
                float interval = 0;
                for (int i = 0; i < Sync.nPlayers; i++)
                {
                    if (Sync.othersInfo[i] != null)
                    {
                        //align with the most advantaged player
                        interval = System.Math.Max(interval, GetRecommendedSleepInterval(i));
                    }
                }
                if (interval > 0)
                {
                    Plugin.Logger.LogInfo($"waiting for {interval}s");
                    P2P.Wait(interval);
                    /*
                     * next recommended sleep is updated in P2P.Wait via transpiler.
                     * note the logic here: we only update next recommended sleep when a sleep occurs. if we reach the next recommended sleep frame and decide not to sleep,
                     * then we continue checking every single frame until a sleep occurs. this seems a bit strange to me compared to skipping the sleep and rescheduling 
                     * another check? but as far as i can tell it's what ggpo does, so i assume it's better this way. they use a fairly large period in between sleep checks
                     * (240 frames), so that could be part of why
                     */
                }
            }
        }

        public void SimpleAlignTimes()
        {
            //update every client's remote frame and get minimum frame
            float minimumFrame = float.MaxValue;
            for (int i = 0; i < Sync.nPlayers; i++)
            {
                minimumFrame = System.Math.Min(minimumFrame, timeSync[i].UpdateRemoteFrameEstimate());
            }

            //for every player (including us), update the estimate of how far ahead they are of the slowest peer, and send them a sleep if they're too far ahead
            for (int i = 0; i < Sync.nPlayers; i++)
            {
                timeSync[i].UpdateRunAheadEstimate(minimumFrame);
                if (Sync.curFrame > timeSync[i].NextRecommendedSleep)
                {
                    float sleep = timeSync[i].GetSleepIntervalEstimate();
                    if (sleep > 0)
                    {
                        sleep *= TimeSyncSimple_Patches.SIMPLE_VANILLA_ALIGN_TIMES_FACTOR;
                        sleep = System.Math.Min(sleep, 0.5f);
                        Plugin.Logger.LogInfo($"sleeping p{i + 1} for {sleep}");
                        if (i == 0)
                        {
                            SendSelfTimeAlignAfterDelay(sleep);
                        }
                        else
                        {
                            P2P.SendToPlayerNr(i, new Message(Msg.P2P_TIME_ALIGN, Sync.matchNr, Mathf.RoundToInt(sleep * 1000f), null, -1));
                        }
                    }
                }
            }
        }

        private static void SendSelfTimeAlignAfterDelay(float time)
        {
            float selfDelay = Player.EPlayers()
                        .Where(player => player.NGLDMOLLPLK && Sync.IsValidOther(player.CJFLMDNNMIE)) //player.inMatch && Sync.IsValidOther(player.nr)
                        .Select(player => player.KLEEADMGHNE.ping) //player.peer.ping
                        .Average();
            selfDelay /= 2; //adjust for one-way trip time
            P2P.instance.StartCoroutine(CSendSelfTimeAlignAfterDelay(selfDelay, time));
        }

        private static IEnumerator CSendSelfTimeAlignAfterDelay(float initialDelay, float time)
        {
            Plugin.Logger.LogInfo($"delaying self-timesync by {initialDelay}");
            yield return new WaitForSeconds(initialDelay);
            P2P.SendToPlayerNr(P2P.localPeer.playerNr, new Message(Msg.P2P_TIME_ALIGN, Sync.matchNr, Mathf.RoundToInt(time * 1000f), null, -1));
            yield break;
        }

        /*
         * ok so
         * if everyone has mod, we use ggpo no problem
         * if client has mod, client can just use the same logic as host for aligntimes and they even each other out. no problem
         * if only host has mod, it's a bit troublesome. sync being exclusively decided by host creates problems in two ways that i can tell:
         * 1. host processes its own aligntimes messages immediately, while client has to wait for the message to arrive (~ping/2)
         *      easily fixed by delaying self-message, though in 3/4p games it's a bit iffy because host needs to delay self-message by like
         *      average or median ping or something? slightly awkward but not a big deal
         * 2. host decides who's running ahead purely by its own perspective. this is the more substantial problem and also is harder to
         *      correct. i didn't understand for a while why this is an issue but i think it's roughly because network traffic can run 
         *      behind but not ahead; messages can take a bit longer to arrive due to network conditions or whatever, but can never go super
         *      fast and arrive faster than ping dictates (technically there's probably some fluctuation both ways but i imagine its much 
         *      more common for stuff to be delayed). this means that from p1's perspective p2 will occasionally be slightly behind where 
         *      they actually are, but will virtually never be slightly ahead of where they actually are. this causes p1 to slightly over-
         *      compensate for itself and pause a bit more often than necessary. this is presumably why ggpo has both players independently
         *      calculate their advantage and share it with each other and only pause when both players agree on who's ahead. 
         *      so having said all of that, how do we implement a more fair algorithm as host under the assumption that clients only have
         *      vanilla capabilities? if it's like 3/4p and a client has the mod then they can send us their data as in ggpo and that helps,
         *      but in 2p or for clients w/o the mod, what's our approach? the best thing i can think of currently is to run an algorithm 
         *      that's sort of like the ggpo solution, but for players who can't send us their advantage, we make a more generous estimate
         *      of what their local advantage looks like, and use that in place of their actual advantage. something like, periodically
         *      estimate their current advantage under the assumption that they're a bit ahead of our best estimate; like instead of 
         *      lastReceived + ping * 0.5 use lastReceived + ping * 0.75 or something, and use this to calculate a local advantage number
         *      for them, from their perspective. additionally, use averaged numbers for advantages as in ggpo. this fixes 
         *      the other problem in Sync.AlignTimes of it using snapshotted advantages for only that one frame. this should reduce 
         *      variance
         *      
         * 
         */
        public void MockGGPOAlignTimes()
        {
            if (!P2P.isHost) return;

            ForAllValidOthers(i =>
            {
                if (Sync.curFrame > timeSync[i].NextRecommendedSleep)
                {

                }
            });
        }

        public void RecordAdvantage()
        {
            if (!SyncFixConfig.Instance.Enabled) return;

            //check to make sure we're using ggpo, since we don't make that check from the caller
            if (StateManager.IsUsingGGPO())
            {
                RecordAdvantage(Sync.sendToPlayerNr, Sync.curFrame);
            }
        }

        public static void ForAllValidOthers(Action<int> action)
        {
            for (int i = 0; i < Sync.nPlayers; i++)
            {
                if (Sync.IsValidOther(i))
                {
                    action(i);
                }
            }
        }
    }
}
