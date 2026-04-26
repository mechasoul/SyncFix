using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using BlazeDevNet.FrameRecorder;
using SyncFix.Utils;
using LLBML.Players;
using Multiplayer;
using UnityEngine;
using UnityEngine.Bindings;

namespace SyncFix
{
    /// <summary>
    /// timesync used to align times when not all players have sync fix
    /// </summary>
    public class TimeSyncSoloHostComponent : TimeSyncComponentBase
    {
        //max duration of a sleep, in seconds (same as vanilla)
        private static readonly float MAX_SLEEP_DURATION = 0.5f;
        //min duration of a sleep, in seconds (1f = 0.0167s; 0.02f in vanilla)
        private static readonly float MIN_SLEEP_DURATION = World.DELTA_TIME;
        private static readonly int ESTIMATE_SLEEP_CHECK_INTERVAL = 120;
        //controls how quickly runahead updates
        private static readonly float RUN_AHEAD_UPDATE_RATE = 0.1f;
        private static readonly float RUN_AHEAD_ACCUMULATOR_THRESHOLD = 1.5f;
        private static readonly float RECENT_SLEEP_BASE_FACTOR = 0.3f;
      
        private int nextRecommendedSleep = int.MaxValue;
        private float currentFrameEstimate = -1;
        private int noRunAheadUpdatesUntil = -1;
        //tracks how far ahead of this player we are. updates each frame with (remote frame estimate - local frame). thus its value is suggested sleep duration.
        //if threshold is reached, then we sleep for its value
        private readonly FrameAccumulator accumulator;

        public int NextRecommendedSleep { get => nextRecommendedSleep; }
        //current estimate of this player's frame. note that for the local player (host, ie p0) this is always just Sync.curFrame
        public float CurrentFrameEstimate { get => currentFrameEstimate; }
        public float RunAheadEstimate { get => accumulator.currentValue; }

        public TimeSyncSoloHostComponent(int playerIndex) : base(playerIndex)
        {
            accumulator = new FrameAccumulator(this.playerIndex, RUN_AHEAD_UPDATE_RATE, RUN_AHEAD_ACCUMULATOR_THRESHOLD);
            //values determined experimentally. could be improved further
            accumulator.upperBoundFunc = (frame, i) => LinearClamped(NetUtils.MaxPing, 0.03f, 0.3f, 0.55f, 1.08f) * (1 + GetRecentSleepFactor());
            accumulator.lowerBoundFunc = (frame, i) => LinearClamped(NetUtils.MaxPing, 0.03f, 0.3f, 0.25f, 0.5f);
            
        }

        public override void Reset()
        {
            nextRecommendedSleep = 60;
            currentFrameEstimate = -1;
            noRunAheadUpdatesUntil = -1;
            accumulator.Reset();
            base.Reset();
        }

        public override void FrameUpdate()
        {
            currentFrameEstimate = EstimateCurrentFrame();
            base.FrameUpdate();
            //also need to call UpdateRunAheadEstimate, but that needs to know minimum frame of all players, so we call it elsewhere after performing this frame estimate for all players.
            //kinda awkward. TODO refactor this
        }

        public float EstimateCurrentFrame()
        {
            if (playerIndex == 0) return Sync.curFrame;

            float estimate = NetUtils.GetTravelTimeEstimate(Player.GetPlayer(playerIndex).peer.ping);
            return Sync.statusInput.otherReceived[playerIndex] + estimate;
        }

        public void UpdateRunAheadEstimate(float minimumFrame)
        {
            if (Sync.curFrame < noRunAheadUpdatesUntil) return;
            else if (Sync.curFrame == noRunAheadUpdatesUntil) noRunAheadUpdatesUntil = -1;

            float estimate = CurrentFrameEstimate - minimumFrame;
            accumulator.FrameUpdate(Sync.curFrame, estimate);
        }

        public override bool ShouldEmergencySleep()
        {
            return accumulator.ThresholdVeryReached();
        }

        public override float GetSleepInterval()
        {
            if (!accumulator.ThresholdReached()) return 0;

            float sleep = Mathf.Clamp(RunAheadEstimate * World.DELTA_TIME * ALIGN_TIMES_FACTOR, MIN_SLEEP_DURATION, MAX_SLEEP_DURATION);
            return sleep;
        }

        public override void OnSleep(float frames)
        {
            noRunAheadUpdatesUntil = (int)(Sync.curFrame + System.Math.Ceiling(frames + NetUtils.MaxPing * World.FPS) + 2);
            nextRecommendedSleep = Sync.curFrame + ESTIMATE_SLEEP_CHECK_INTERVAL;
            accumulator.Reset();
            base.OnSleep(frames);
        }

        protected override float GetRecentSleepBaseFactor()
        {
            return RECENT_SLEEP_BASE_FACTOR;
        }

        protected override float GetRecentSleepWindow()
        {
            return playerIndex == 0 ? RECENT_SLEEP_WINDOW_HOST : RECENT_SLEEP_WINDOW_CLIENT;
        }
    }

}
