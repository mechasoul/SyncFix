using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BlazeDevNet.FrameRecorder;
using LLBML.Players;
using Multiplayer;
using UnityEngine;

namespace BlazeSyncFix
{
    /// <summary>
    /// based on ggpo's time sync. represents a sync state with another player. mostly a combination of ggpo's TimeSync and UdpProtocol
    /// </summary>
    //TODO test slight algo changes (EMA), update docs
    public class TimeSync
    {
        //max duration of a sleep, in seconds
        protected static readonly float MAX_SLEEP_DURATION = 9 * World.DELTA_TIME;
        //minimum duration of a sleep. vanilla llb requires 0.02s
        protected static readonly float MIN_SLEEP_FRAMES = 1f;
        protected static readonly float MIN_SMALL_SLEEP_ADVANTAGE = 0.5f;
        protected static readonly int SMALL_ADVANTAGE_MAINTAINED_DURATION = 10;
        protected static readonly float LOCAL_ADVANTAGE_SMOOTHING_FACTOR = 0.1f;

        protected static readonly int ESTIMATE_SLEEP_CHECK_INTERVAL = 240;
        protected static readonly float RUNAHEAD_ESTIMATE_FRAMES_UPPER = 0.65f;
        protected static readonly float RUNAHEAD_ESTIMATE_FRAMES_LOWER = 0.25f;
        protected static readonly float RUN_AHEAD_SMOOTHING_FACTOR = 0.1f;
        protected static readonly float RUN_AHEAD_ACCUMULATOR_THRESHOLD = 1.5f;
        protected static readonly float CURRENT_FRAME_ESTIMATE_PING_FACTOR = 0.5f;

        //the other (remote) player's playerIndex
        protected readonly int playerIndex;

        /*
         * holds the current local frame advantage vs this player. this represents how many frames we are behind the other player (further behind->more advantaged. 
         * being behind is advantaged because receiving an incorrect frame doesn't force as deep of a rollback). this updates every frame, at the same timing as 
         * Sync.AlignTimes. note that despite being behind->being advantaged, these values are positive and more positive is more advantaged. slightly confusing...
         */
        protected float currentLocalAdvantage;
        //same as above, but represents the remote player's advantage vs us. this is sent by the remote player at regular intervals.
        //each player keeps track of their own advantage vs each other player in this way
        protected float currentRemoteAdvantage;
        protected float lastRemoteAdvantage;
        protected float minSleepThreshold = 0f;
        protected float smallSleepThreshold = 0f;
        protected int smallSleepThresholdFrames;
        //for estimating timesync when peers don't have the mod installed
        private int nextRecommendedSleep = int.MaxValue;
        private int lastSleep = -1;
        private float currentFrameEstimate = -1;
        private int lastRemoteFrameEstimate = -1;
        private float runAheadEstimate;
        protected float runAheadAccumulator = 0f;
        protected int noRunAheadUpdatesUntil = -1;
        protected float runAheadEstimateFramesUpper = 0f;
        protected float runAheadEstimateFramesLower = 0f;


        public float CurrentLocalAdvantage { get => currentLocalAdvantage; }
        public float CurrentRemoteAdvantage { get => currentRemoteAdvantage; }
        public float MinSleepThreshold { get => minSleepThreshold; }
        public float SmallSleepThreshold { get => smallSleepThreshold; }
        public int NextRecommendedSleep { get => nextRecommendedSleep; }
        public int LastSleep { get => lastSleep; }
        //public float RemoteFrameEstimate
        //{
        //    get
        //    {
        //        UpdateRemoteFrameEstimateIfNeeded();
        //        return remoteFrameEstimate;
        //    }
        //}
        public float CurrentFrameEstimate { get => currentFrameEstimate; }
        public float RunAheadEstimate { get => runAheadEstimate; }
        public float RunAheadEstimateUpperBound { get => runAheadEstimateFramesUpper; }
        public float RunAheadEstimateLowerBound { get => runAheadEstimateFramesLower; }

        public TimeSync(int playerIndex)
        {
            this.playerIndex = playerIndex;
        }

        public virtual void Reset()
        {
            currentLocalAdvantage = 0f;
            currentRemoteAdvantage = 0f;
            lastRemoteAdvantage = 0f;
            minSleepThreshold = 0f;
            smallSleepThreshold = 0f;
            smallSleepThresholdFrames = 0;
            nextRecommendedSleep = SyncFixManager.SLEEP_CHECK_INTERVAL;
            lastSleep = -1;

            currentFrameEstimate = -1;
            lastRemoteFrameEstimate = -1;
            runAheadEstimate = 0f;
            runAheadAccumulator = 0f;
            noRunAheadUpdatesUntil = -1;
            runAheadEstimateFramesUpper = 0f;
            runAheadEstimateFramesLower = 0f;
        }

        public void SetSleepThreshold(float ping)
        {
            minSleepThreshold = Mathf.Clamp(ping / 0.1f, 1f, 2f);
            smallSleepThreshold = Mathf.Clamp(ping / 0.15f * 0.5f, 0.5f, 0.75f);
            Plugin.Logger.LogInfo($"set min sleep threshold: {minSleepThreshold}, small sleep threshold: {smallSleepThreshold}");
        }

        public void SetRunAheadEstimateBounds(float ping)
        {
            runAheadEstimateFramesUpper = Mathf.Clamp(ping / 0.1f * 0.65f, 0.65f, 1.5f);
            runAheadEstimateFramesLower = Mathf.Clamp(ping / 0.1f * 0.25f, 0.25f, 0.75f);
            Plugin.Logger.LogInfo($"set lower bound: {runAheadEstimateFramesLower}, upper bound: {runAheadEstimateFramesUpper}");
        }

        //updates the current local frame advantage. runs every frame. estimate the other player's frame based on the most recent input received from them + their ping.
        //i'm not totally convinced on why we use round-trip-time here instead of half rtt, but it ends up being corrected later when calculating sleep time, so it's not wrong...
        //just seems like an unituitive choice. i kinda get it but not totally
        public void UpdateCurrentFrameAdvantage(int localFrame)
        {
            float receivedFrame = Sync.statusInput.otherReceived[playerIndex];
            if (receivedFrame < 0) return;
            float estimatedTravelFrames = Sync.othersInfo[playerIndex].peer.ping * World.FPS / 2 + 0.5f;
            float remoteFrame = receivedFrame + estimatedTravelFrames;
            float localAdvantage = remoteFrame - localFrame;
            float prevAdvantage = CurrentLocalAdvantage;
            currentLocalAdvantage = Mathf.Lerp(CurrentLocalAdvantage, localAdvantage, LOCAL_ADVANTAGE_SMOOTHING_FACTOR);
            Plugin.Logger.LogInfo($"updating local frame adv; local frame: {localFrame}, received input: {receivedFrame}, estimated travel frames: {estimatedTravelFrames}, remote frame: {remoteFrame}. " +
                $"updated local advantage {prevAdvantage} -> {CurrentLocalAdvantage}");
            //update remote
            currentRemoteAdvantage = Mathf.Lerp(CurrentRemoteAdvantage, lastRemoteAdvantage, 0.2f);
            FrameRecorders.GetFrameRecorder<float>("local").Record(Sync.curFrame, CurrentLocalAdvantage);
            FrameRecorders.GetFrameRecorder<float>("remote").Record(Sync.curFrame, CurrentRemoteAdvantage);
        }

        public void UpdateRemoteFrameAdvantage(float remoteAdvantage)
        {
            lastRemoteAdvantage = remoteAdvantage;
        }

        //calculates how long to sleep based on local vs remote frame advantage
        public float GetSleepInterval(bool allowSmallSleep)
        {
            float localAverage = CurrentLocalAdvantage;
            float remoteAverage = CurrentRemoteAdvantage;
            Plugin.Logger.LogInfo($"sleep calculation: local advantage avg: {localAverage}, remote advantage avg: {remoteAverage}");
            if (localAverage >= remoteAverage)
            {
                return 0;
            }
            float sleepFrames = (remoteAverage - localAverage) / 2;
            CheckSmallSleepThreshold(sleepFrames);
            if (sleepFrames >= MinSleepThreshold)
            {
                float sleepDuration = sleepFrames * World.DELTA_TIME;
                Plugin.Logger.LogInfo($"sleep duration: {sleepDuration}");
                return System.Math.Min(sleepDuration, MAX_SLEEP_DURATION);
            }
            else if (allowSmallSleep && smallSleepThresholdFrames >= SMALL_ADVANTAGE_MAINTAINED_DURATION)
            {
                Plugin.Logger.LogInfo($"small sleep triggered on frame advantage: {sleepFrames}");
                return 0.012f;
            }
            return 0f;
        }

        private void CheckSmallSleepThreshold(float sleepFrames)
        {
            if (sleepFrames >= SmallSleepThreshold)
            {
                smallSleepThresholdFrames++;
            }
            else
            {
                smallSleepThresholdFrames = 0;
            }
        }

        public void OnSleep(float frames)
        {
            currentLocalAdvantage += frames;
            currentRemoteAdvantage -= frames;
            lastRemoteAdvantage -= frames;
            smallSleepThresholdFrames = 0;

        }

        public float UpdateCurrentFrameEstimate()
        {
            currentFrameEstimate = EstimateCurrentFrame();
            lastRemoteFrameEstimate = Sync.curFrame;
            return currentFrameEstimate;
        }

        private void UpdateRemoteFrameEstimateIfNeeded()
        {
            if (lastRemoteFrameEstimate < Sync.curFrame)
            {
                UpdateCurrentFrameEstimate();
            }
        }

        public virtual float EstimateCurrentFrame()
        {
            float estimate = (Player.GetPlayer(playerIndex).peer.ping * CURRENT_FRAME_ESTIMATE_PING_FACTOR + (World.DELTA_TIME * 1.0f)) * World.FPS;
            Plugin.Logger.LogInfo($"current frame: {Sync.curFrame}, received: {Sync.statusInput.otherReceived[playerIndex]}, estimated travel time: {estimate}, est remote: {estimate + Sync.statusInput.otherReceived[playerIndex]}");
            return Sync.statusInput.otherReceived[playerIndex] + estimate;
        }

        public virtual float UpdateRunAheadEstimate(float minimumFrame)
        {
            if (Sync.curFrame < noRunAheadUpdatesUntil) return 0f;
            else if (Sync.curFrame == noRunAheadUpdatesUntil) noRunAheadUpdatesUntil = -1;

            float estimate = CurrentFrameEstimate - minimumFrame;
            float prevEstimate = RunAheadEstimate;
            runAheadEstimate = Mathf.Lerp(RunAheadEstimate, estimate, RUN_AHEAD_SMOOTHING_FACTOR);
            UpdateRunAheadFrames();
            FrameRecorders.Record($"runahead_p{playerIndex+1}", Sync.curFrame, RunAheadEstimate);
            Plugin.Logger.LogInfo($"{Sync.curFrame} runahead estimate for p{playerIndex}: estimated run ahead {estimate}, prev: {prevEstimate}, new: {RunAheadEstimate}");
            return RunAheadEstimate;
        }

        public void UpdateRunAheadFrames()
        {

            if (RunAheadEstimate > RunAheadEstimateUpperBound)
            {
                float added = RunAheadEstimate - RunAheadEstimateUpperBound;
                runAheadAccumulator += added;
                Plugin.Logger.LogInfo($"added {added} to runahead frames, now: {runAheadAccumulator}");
            }
            else if (RunAheadEstimate < RunAheadEstimateLowerBound)
            {
                float added = RunAheadEstimate - RunAheadEstimateLowerBound;
                runAheadAccumulator = System.Math.Max(0f, runAheadAccumulator + added);
                Plugin.Logger.LogInfo($"added {added} to runahead frames, now: {runAheadAccumulator}");
            } 
        }

        public float GetSleepIntervalEstimate()
        {
            if (runAheadAccumulator < RUN_AHEAD_ACCUMULATOR_THRESHOLD) return 0;

            float sleep = System.Math.Max(RunAheadEstimate * World.DELTA_TIME, World.DELTA_TIME);
            noRunAheadUpdatesUntil = (int)(Sync.curFrame + System.Math.Ceiling((sleep + Player.GetPlayer(playerIndex).peer.ping) * World.FPS) + 2); 
            runAheadEstimate = 0f;
            lastSleep = Sync.curFrame;
            nextRecommendedSleep = Sync.curFrame + ESTIMATE_SLEEP_CHECK_INTERVAL;
            runAheadAccumulator = 0f;
            return sleep;
        }
    }
}
