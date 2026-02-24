using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using BlazeDevNet.FrameRecorder;
using LLBML.Players;
using Multiplayer;
using UnityEngine;

namespace BlazeSyncFix
{
    public class TimeSyncSoloHostEstimateComponent(int playerIndex) : ITimeSyncComponent
    {
        protected static readonly int ESTIMATE_SLEEP_CHECK_INTERVAL = 240;
        protected static readonly float RUN_AHEAD_SMOOTHING_FACTOR = 0.1f;
        protected static readonly float RUN_AHEAD_ACCUMULATOR_THRESHOLD = 1.5f;
        protected static readonly float CURRENT_FRAME_ESTIMATE_PING_FACTOR = 0.5f;

        private readonly int playerIndex = playerIndex;

        //for estimating timesync when peers don't have the mod installed
        private int nextRecommendedSleep = int.MaxValue;
        private int lastSleep = -1;
        private float currentFrameEstimate = -1;
        private float runAheadEstimate;
        protected float runAheadAccumulator = 0f;
        protected int noRunAheadUpdatesUntil = -1;
        protected float runAheadEstimateFramesUpper = 0f;
        protected float runAheadEstimateFramesLower = 0f;

        public int NextRecommendedSleep { get => nextRecommendedSleep; }
        public int LastSleep { get => lastSleep; }
        public float CurrentFrameEstimate { get => currentFrameEstimate; }
        public float RunAheadEstimate { get => runAheadEstimate; }
        public float RunAheadEstimateUpperBound { get => runAheadEstimateFramesUpper; }
        public float RunAheadEstimateLowerBound { get => runAheadEstimateFramesLower; }

        public void Reset()
        {
            currentFrameEstimate = -1;
            runAheadEstimate = 0f;
            runAheadAccumulator = 0f;
            noRunAheadUpdatesUntil = -1;
            runAheadEstimateFramesUpper = 0f;
            runAheadEstimateFramesLower = 0f;
        }

        public void SetInitialValues(float ping)
        {
            runAheadEstimateFramesUpper = Mathf.Clamp(ping / 0.1f * 0.65f, 0.65f, 1.5f);
            runAheadEstimateFramesLower = Mathf.Clamp(ping / 0.1f * 0.25f, 0.25f, 0.75f);
            Plugin.Logger.LogInfo($"set lower bound: {runAheadEstimateFramesLower}, upper bound: {runAheadEstimateFramesUpper}");
        }

        public void FrameUpdate()
        {
            currentFrameEstimate = EstimateCurrentFrame();
            //also need to call UpdateRunAheadEstimate, but that needs to be called with minimum frame of all players. kinda awkward
        }

        public float EstimateCurrentFrame()
        {
            if (playerIndex == 0) return Sync.curFrame;

            float estimate = (Player.GetPlayer(playerIndex).peer.ping * CURRENT_FRAME_ESTIMATE_PING_FACTOR + (World.DELTA_TIME * 1.0f)) * World.FPS;
            Plugin.Logger.LogInfo($"current frame: {Sync.curFrame}, received: {Sync.statusInput.otherReceived[playerIndex]}, estimated travel time: {estimate}, est remote: {estimate + Sync.statusInput.otherReceived[playerIndex]}");
            return Sync.statusInput.otherReceived[playerIndex] + estimate;
        }

        public float UpdateRunAheadEstimate(float minimumFrame)
        {
            if (Sync.curFrame < noRunAheadUpdatesUntil) return 0f;
            else if (Sync.curFrame == noRunAheadUpdatesUntil) noRunAheadUpdatesUntil = -1;

            float estimate = CurrentFrameEstimate - minimumFrame;
            float prevEstimate = RunAheadEstimate;
            runAheadEstimate = Mathf.Lerp(RunAheadEstimate, estimate, RUN_AHEAD_SMOOTHING_FACTOR);
            UpdateRunAheadFrames();
            FrameRecorders.Record($"runahead_p{playerIndex + 1}", Sync.curFrame, RunAheadEstimate);
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

        public float GetSleepInterval()
        {
            if (runAheadAccumulator < RUN_AHEAD_ACCUMULATOR_THRESHOLD) return 0;

            float sleep = System.Math.Max(RunAheadEstimate * World.DELTA_TIME, World.DELTA_TIME);
            noRunAheadUpdatesUntil = (int)(Sync.curFrame + System.Math.Ceiling((sleep + (playerIndex == 0 ? 0 : Player.GetPlayer(playerIndex).peer.ping)) * World.FPS) + 2);
            runAheadEstimate = 0f;
            lastSleep = Sync.curFrame;
            nextRecommendedSleep = Sync.curFrame + ESTIMATE_SLEEP_CHECK_INTERVAL;
            runAheadAccumulator = 0f;
            return sleep;
        }

        public void OnSleep(float frames)
        {
            //?
        }

        

        
    }
}
