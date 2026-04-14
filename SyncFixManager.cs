using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BlazeDevNet.FrameRecorder;
using BlazeSyncFix.Patches;
using BlazeSyncFix.Utils;
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

        //TODO move this group-specific stuff to groupcomponent or something. left here from old structure
        public static readonly int GROUP_SLEEP_CHECK_INTERVAL = 120; //frames in between sleep checks
        public static readonly int GROUP_ADVANTAGE_UPDATE_INTERVAL = 60; //frames in between sending advantage to peers
        

        static SyncFixManager() { }

        public static SyncFixManager Instance { get { return instance; } }

        //timesync objects responsible for actual timesync management. one for each player
        public TimeSync[] timeSync = [new TimeSync(0), new TimeSync(1), new TimeSync(2), new TimeSync(3)];

        private int nextAdvantageUpdate = int.MaxValue;
        private int nextRecommendedSleep = int.MaxValue;
        private int lastSleep = -1;

        //public Stopwatch stopwatch = new Stopwatch();
        //public void StartTimer()
        //{
        //    stopwatch.Start();
        //}
        //public void StopTimer()
        //{
        //    stopwatch.Stop();
        //    FrameRecorders.GetFrameRecorder<float>("actualwait").Record(Sync.curFrame, stopwatch.ElapsedMilliseconds / 1000f);
        //    stopwatch.Reset();
        //}
        
        //TODO move this stuff and related logic to groupcomponent
        public int NextAdvantageUpdate { get => nextAdvantageUpdate; }
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

        public void MidMatchReset()
        {
            if (!SyncFixConfig.Instance.Enabled) return;

            for (int i = 0; i < Sync.nPlayers; i++)
            {
                timeSync[i].ResetActiveComponent();
            }
            UpdateNextAdvantageTime();
            UpdateNextRecommendedSleep();
            lastSleep = -1;
        }

        /// <summary>
        /// call sometime after game has started (eg on Sync.Start, except i think that gets inlined so don't do that) to start time sync checks. 
        /// also call whenever a sleep is performed
        /// </summary>
        public void UpdateNextRecommendedSleep()
        {
            nextRecommendedSleep = Sync.curFrame + GROUP_SLEEP_CHECK_INTERVAL;
        }

        public void UpdateLastSleep()
        {
            lastSleep = Sync.curFrame;
        }

        /// <summary>
        /// call when a sleep occurs (preferably when the sleep is initialized, without waiting for it to complete first)
        /// </summary>
        /// <param name="sleepDuration"></param>
        public void OnSleep(float sleepDuration)
        {
            UpdateNextRecommendedSleep();
            UpdateLastSleep();
            if (StateManager.IsUsingGroup())
            {
                ForAllValidOthers(i =>
                {
                    float sleepFrames = sleepDuration * World.FPS;
                    timeSync[i].OnSleep(sleepFrames);
                    //update all players with our new local advantage post-sleep
                    SendLocalAdvantageToPlayer(i, true);
                });
            }
            UpdateNextAdvantageTime();
        }

        /// <summary>
        /// call sometime after game has started (eg on Sync.Start, except i think that gets inlined so don't do that) to start sending advantage.
        /// also call whenever advantage is sent
        /// </summary>
        public void UpdateNextAdvantageTime()
        {
            nextAdvantageUpdate = Sync.curFrame + GROUP_ADVANTAGE_UPDATE_INTERVAL;
        }

        //
        /// <summary>
        /// returns the current recommended sleep interval (in seconds) based on local advantage vs the given remote player index and their provided advantage vs us.
        /// </summary>
        /// <param name="playerIndex"></param>
        /// <returns>the recommended sleep duration to even out advantage vs this player. if the recommended sleep duration is lower than TimeSync.MIN_SLEEP_DURATION,
        /// then 0 is returned instead. the returned value can't be higher than TimeSync.MAX_SLEEP_DURATION</returns>
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

        //notifysleep can be used to tell the remote player if this advantage message was triggered by a sleep, in case any special action
        //should be taken in that case
        public void SendLocalAdvantageToPlayer(int i, bool notifySleep = false)
        {
            if (!SyncFixConfig.Instance.Enabled) return;

            byte[] bytes = BitConverter.GetBytes(GetCurrentLocalAdvantage(i));
            Message toSend = new Message((Msg)SyncFixMessages.GAME_LOCAL_ADVANTAGE, P2P.localPeer.playerNr, notifySleep ? 1 : 0,
                bytes, bytes.Length);
            P2P.SendToPlayerNr(i, toSend);
        }

        public void ReceiveRemoteAdvantage(Message message)
        {
            if (!SyncFixConfig.Instance.Enabled) return;

            float adv = BitConverter.ToSingle((byte[])message.ob, 0);
            UpdateRemoteAdvantage(message.playerNr, adv);
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
        public void GroupAlignTimes()
        {
            if (Sync.isAwaiting) return;

            ForAllValidOthers(i => timeSync[i].FrameUpdate());
            //sleep logic: sleep if enough time has passed or if we need to emergency sleep
            bool shouldSleep = Sync.curFrame > NextRecommendedSleep;
            for (int i = 0; i < Sync.nPlayers; i++)
            {
                if (Sync.IsValidOther(i))
                {
                    shouldSleep = shouldSleep || timeSync[i].ShouldEmergencySleep();
                    if (shouldSleep) break;
                }
            }
            if (shouldSleep && !Sync.isAwaiting)
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
                }
            }
        }

        //replacement for Sync.AlignTimes for when at least one player doesn't have syncfix. similar to vanilla, but fairer
        public void SoloHostAlignTimes()
        {
            if (Sync.isAwaiting) return;

            //update every client's remote frame and get minimum frame
            float minimumFrame = float.MaxValue;
            for (int i = 0; i < Sync.nPlayers; i++)
            {
                timeSync[i].FrameUpdate();
                if (timeSync[i].GetCurrentFrameEstimate() < minimumFrame)
                {
                    minimumFrame = timeSync[i].GetCurrentFrameEstimate();
                }
            }

            //for every player (including us), update the estimate of how far ahead they are of the slowest peer, and send them a sleep if they're too far ahead
            for (int i = 0; i < Sync.nPlayers; i++)
            {
                timeSync[i].UpdateRunAheadEstimate(minimumFrame);
                if (timeSync[i].CanSleep())
                {
                    float sleep = timeSync[i].GetSleepInterval();
                    if (sleep > 0)
                    {
                        Plugin.Logger.LogInfo($"sleeping p{i + 1} for {sleep}");
                        timeSync[i].OnSleep(sleep * World.FPS);
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

        /// <summary>
        /// delays wait messages to self by approximately the same delay that a remote player would experience, for fairness
        /// </summary>
        /// <param name="time"></param>
        private static void SendSelfTimeAlignAfterDelay(float time)
        {
            float selfDelay = Player.EPlayers()
                        .Where(player => player.NGLDMOLLPLK && Sync.IsValidOther(player.CJFLMDNNMIE)) //player.inMatch && Sync.IsValidOther(player.nr)
                        .Average(player => player.KLEEADMGHNE.ping); //player.peer.ping
            selfDelay *= 0.49f; //adjust for one-way trip time. 0.49f because...?
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
    }
}
