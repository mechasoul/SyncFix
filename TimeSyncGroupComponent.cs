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

namespace SyncFix
{
    /// <summary>
    /// timesync used to align times when all players have syncfix
    /// </summary>
    public class TimeSyncGroupComponent : TimeSyncComponentBase
    {
        //max duration of a sleep, in seconds (same as vanilla)
        private static readonly float MAX_SLEEP_DURATION = 0.5f;
        //min duration of a sleep, in seconds (1f = 0.0167s; 0.02s vanilla)
        private static readonly float MIN_SLEEP_DURATION = World.DELTA_TIME;
        //controls how quickly local advantage values can change
        private static readonly float LOCAL_ADVANTAGE_UPDATE_RATE = 0.1f;
        private static readonly float RECENT_SLEEP_BASE_FACTOR = 0.25f;


        /*
         * holds the current local frame advantage vs this player. this represents how many frames behind the other player we are (further behind->more advantaged. 
         * being behind is advantaged because receiving an incorrect frame doesn't force as deep of a rollback). this updates every frame, at the same timing as 
         * Sync.AlignTimes. note that despite being behind->being advantaged, these values are positive and more positive is more advantaged. slightly confusing...
         */
        private float currentLocalAdvantage;
        //same as above, but represents the remote player's advantage vs us. this is sent by the remote player at regular intervals.
        //each player keeps track of their own advantage vs each other player in this way
        private float currentRemoteAdvantage;
        private float lastRemoteAdvantage;
        //frame accumulator for tracking how far ahead of this player we are. its value is effectively the same as needed sleep duration, so when 
        //threshold is reached, we sleep according to its current value
        private readonly FrameAccumulator accumulator;


        public float CurrentLocalAdvantage { get => currentLocalAdvantage; }
        public float CurrentRemoteAdvantage { get => currentRemoteAdvantage; }

        public TimeSyncGroupComponent(int playerIndex) : base(playerIndex)
        {
            this.accumulator = new FrameAccumulator(this.playerIndex, 0.25f, 1.5f);
            //these values were determined experimentally. could be improved further i'm sure
            accumulator.upperBoundFunc = (frame, i) => LinearClamped(NetUtils.MaxPing, 0.03f, 0.3f, 0.5f, 0.7f) * (1 + GetRecentSleepFactor());
            accumulator.lowerBoundFunc = (frame, i) => LinearClamped(NetUtils.MaxPing, 0.03f, 0.3f, 0.1f, 0.4f);
        }

        public override void Reset()
        {
            currentLocalAdvantage = 0f;
            currentRemoteAdvantage = 0f;
            lastRemoteAdvantage = 0f;
            accumulator.Reset();
            base.Reset();
        }

        public override void FrameUpdate()
        {
            int localFrame = Sync.curFrame;
            float receivedFrame = Sync.statusInput.otherReceived[playerIndex];
            if (receivedFrame < 0) return; //prevents weird values at game start, where otherReceived is initialized to -1

            float estimatedTravelFrames = NetUtils.GetTravelTimeEstimate(Sync.othersInfo[playerIndex].peer.ping);
            float remoteFrame = receivedFrame + estimatedTravelFrames;
            float localAdvantage = remoteFrame - localFrame;
            currentLocalAdvantage = Mathf.Lerp(CurrentLocalAdvantage, localAdvantage, LOCAL_ADVANTAGE_UPDATE_RATE);
            //update remote. kinda weird to lerp this, should maybe just update it immediately?
            currentRemoteAdvantage = Mathf.Lerp(CurrentRemoteAdvantage, lastRemoteAdvantage, 0.2f);
            //as in ggpo's algorithm, divide by two here to account for double-counting the difference, since both local advantage and other player's local advantage
            //are calculated by remote - local. looks weird but is correct. do the math
            accumulator.FrameUpdate(Sync.curFrame, (CurrentRemoteAdvantage - CurrentLocalAdvantage) / 2);
            base.FrameUpdate();
        }

        public override float GetSleepInterval()
        {
            if (!accumulator.ThresholdReached()) return 0;

            float sleepDuration = accumulator.currentValue * World.DELTA_TIME * ALIGN_TIMES_FACTOR;
            return Mathf.Clamp(sleepDuration, MIN_SLEEP_DURATION, MAX_SLEEP_DURATION);
        }

        public override bool ShouldEmergencySleep()
        {
            return accumulator.ThresholdVeryReached();
        }

        //immediately updating local/remote advantage by sleep duration seems correct,
        //but maybe instant change like this is weird, given how we approach most other things?
        public override void OnSleep(float frames)
        {
            currentLocalAdvantage += frames;
            currentRemoteAdvantage -= frames;
            lastRemoteAdvantage -= frames;
            accumulator.Reset();
            base.OnSleep(frames);
        }

        public void UpdateRemoteFrameAdvantage(float remoteAdvantage)
        {
            lastRemoteAdvantage = remoteAdvantage;
        }

        protected override float GetRecentSleepBaseFactor()
        {
            return RECENT_SLEEP_BASE_FACTOR;
        }

        protected override float GetRecentSleepWindow()
        {
            return RECENT_SLEEP_WINDOW_CLIENT;
        }
    }
}
