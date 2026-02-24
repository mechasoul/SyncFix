using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BlazeDevNet.FrameRecorder;
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
        
        private TimeSync[] timeSync = [new TimeSync(0), new TimeSync(1), new TimeSync(2), new TimeSync(3)];
        private int nextAdvantageUpdate = int.MaxValue;
        private int lastAdvantageUpdate = -1;
        private int nextRecommendedSleep = int.MaxValue;
        private int lastSleep = -1;
        

        public int NextAdvantageUpdate { get => nextAdvantageUpdate; }
        public int LastAdvantageUpdate { get => lastAdvantageUpdate; }
        

        public Stopwatch stopwatch = new Stopwatch();
        public void StartTimer()
        {
            stopwatch.Start();
        }
        public void StopTimer()
        {
            stopwatch.Stop();
            FrameRecorders.GetFrameRecorder<float>("actualwait").Record(Sync.curFrame, stopwatch.ElapsedMilliseconds / 1000f);
            stopwatch.Reset();
        }

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

            if (StateManager.IsUsingGGPO())
            {
                float maxPing = GetMaxPing();
                ForAllValidOthers(i => timeSync[i].SetInitialValues(maxPing));
            }
            else if (!StateManager.IsUsingGGPO() && P2P.isHost)
            {
                float maxPing = GetMaxPing();
                for (int i = 0; i < Sync.nPlayers; i++)
                {
                    timeSync[i].SetInitialValues(maxPing);
                }
            }
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

        public void OnSleep(float sleepDuration)
        {
            UpdateNextRecommendedSleep();
            UpdateLastSleep();
            ForAllValidOthers(i =>
            {
                if (StateManager.IsUsingGGPO())
                {
                    float sleepFrames = sleepDuration * World.FPS;
                    timeSync[i].OnSleep(sleepFrames);
                    SendLocalAdvantageToPlayer(i, true);
                }
                else if (P2P.isHost)
                {
                    //?
                }
            });
            UpdateNextAdvantageTime();
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

            return timeSync[playerIndex].GetCurrentLocalAdvantage();
        }

        public void SendLocalAdvantageToPlayer(int i, bool notifySleep = false)
        {
            if (!SyncFixConfig.Instance.Enabled) return;

            byte[] bytes = BitConverter.GetBytes(GetCurrentLocalAdvantage(i));
            Message toSend = new Message((Msg)SyncFixMessages.GAME_LOCAL_ADVANTAGE, P2P.localPeer.playerNr, notifySleep ? 1 : 0,
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
            if (message.index == 1)
            {
                ForAllValidOthers(i => timeSync[i].UpdateNextSmallSleepTime());
            }
        }

        /// <summary>
        /// update the current remote advantage vs us for the given remote player index. call immediately whenever a peer provides their advantage vs us
        /// </summary>
        /// <param name="playerIndex"></param>
        /// <param name="remoteAdvantage">the provided remote advantage, in frames</param>
        public void UpdateRemoteAdvantage(int playerIndex, float remoteAdvantage)
        {
            timeSync[playerIndex].UpdateRemoteFrameAdvantage(remoteAdvantage);
        }


        //replacement for llb's Sync.AlignTimes based on the ggpo time sync algorithm
        public void GGPOAlignTimes()
        {
            ForAllValidOthers(i => timeSync[i].FrameUpdate());
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
            ForAllValidOthers(i => timeSync[i].FrameUpdate());
            //update every client's remote frame and get minimum frame
            float minimumFrame = float.MaxValue;
            for (int i = 0; i < Sync.nPlayers; i++)
            {
                minimumFrame = System.Math.Min(minimumFrame, timeSync[i].GetCurrentFrameEstimate());
            }

            //for every player (including us), update the estimate of how far ahead they are of the slowest peer, and send them a sleep if they're too far ahead
            for (int i = 0; i < Sync.nPlayers; i++)
            {
                timeSync[i].UpdateRunAheadEstimate(minimumFrame);
                if (Sync.curFrame > timeSync[i].GetNextRecommendedSleep())
                {
                    float sleep = timeSync[i].GetSleepInterval();
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
                        .Average(player => player.KLEEADMGHNE.ping); //player.peer.ping
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

        private static float GetMaxPing()
        {
            float maxPing = Player.EPlayers()
                    .Where(player => player.NGLDMOLLPLK && Sync.IsValidOther(player.CJFLMDNNMIE)) //player.inMatch && Sync.IsValidOther(player.nr)
                    .Max(player => player.KLEEADMGHNE.ping); //player.peer.ping
            return maxPing;
        }
    }
}
