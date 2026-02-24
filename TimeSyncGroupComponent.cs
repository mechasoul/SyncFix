using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BlazeDevNet.FrameRecorder;
using Multiplayer;
using UnityEngine;

namespace BlazeSyncFix
{
    public class TimeSyncGroupComponent(int playerIndex) : ITimeSyncComponent
    {
        /*
         * note on this one:
         * i can reliably get difference in rollback size between players down to ~0.75f under good network conditions.
         * ideally we'd get it to 0 but i can't really do that with the options for frame timing that unity presents.
         * 0.75f is a small enough difference to probably be imperceptible to any player, even in a game like llb, so 
         * combined with the fact that the "advantaged" player is effectively random instead of always host as in 
         * vanilla, we're already doing very well
         * 
         * i mused for a bit anyway over whether there's a way to eliminate this advantage completely and the best i
         * can come up with is to forcibly oscillate advantage between players if it's in that ~0.5 - 0.75f range. 
         * this basically completely eliminates any advantage at the cost of pausing for 1f every (this duration * 2),
         * ie, 1f every 20s. so it's essentially one player potentially randomly having a ~0.75f rollback advantage 
         * vs both players pausing for 1f every 20s and increasing rollback variance slightly (eg, to use numbers from
         * a ~100 ping test, we go from say P1 having 50% 3f and 50% 4f rollbacks and P2 having 90% 4f and 10% 5f
         * rollbacks to both players having 25% 3f, 70% 4f, 5% 5f). i'm genuinely not sure if this is better. i don't 
         * think the pausing would be perceptible to anyone, but increasing rollback variance by a slight amount might
         * actually be noticeable and not worth eliminating such a tiny advantage. i'll try it for a bit and see i guess
         */
        public static readonly int MAX_SMALL_ADVANTAGE_DURATION = 300;
        protected static readonly float MIN_SMALL_SLEEP_ADVANTAGE = 0.5f;
        protected static readonly int SMALL_ADVANTAGE_MAINTAINED_DURATION = 10;
        protected static readonly float LOCAL_ADVANTAGE_SMOOTHING_FACTOR = 0.1f;

        private readonly int playerIndex = playerIndex;

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
        private int nextSmallAdvantageSleep = int.MaxValue;
        protected int smallSleepThresholdFrames;


        public float CurrentLocalAdvantage { get => currentLocalAdvantage; }
        public float CurrentRemoteAdvantage { get => currentRemoteAdvantage; }
        public float MinSleepThreshold { get => minSleepThreshold; }
        public int NextSmallAdvantageSleep { get => nextSmallAdvantageSleep; }
        public float SmallSleepThreshold { get => smallSleepThreshold; }

        public void Reset()
        {
            currentLocalAdvantage = 0f;
            currentRemoteAdvantage = 0f;
            lastRemoteAdvantage = 0f;
            minSleepThreshold = 0f;
            smallSleepThreshold = 0f;
            smallSleepThresholdFrames = 0;
            nextSmallAdvantageSleep = SMALL_ADVANTAGE_MAINTAINED_DURATION;
        }

        //takes max ping
        public void SetInitialValues(float ping)
        {
            minSleepThreshold = Mathf.Clamp(ping / 0.1f, 1f, 2f);
            smallSleepThreshold = Mathf.Clamp(ping / 0.15f * 0.5f, 0.5f, 0.75f);
            Plugin.Logger.LogInfo($"set min sleep threshold: {minSleepThreshold}, small sleep threshold: {smallSleepThreshold}");
        }

        public void FrameUpdate()
        {
            int localFrame = Sync.curFrame;
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

        public float GetSleepInterval()
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
                return System.Math.Min(sleepDuration, TimeSync.MAX_SLEEP_DURATION);
            }
            else if (Sync.curFrame > NextSmallAdvantageSleep && smallSleepThresholdFrames >= SMALL_ADVANTAGE_MAINTAINED_DURATION)
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
            UpdateNextSmallSleepTime();
        }

        public void UpdateNextSmallSleepTime()
        {
            nextSmallAdvantageSleep = Sync.curFrame + SMALL_ADVANTAGE_MAINTAINED_DURATION;
        }

        public void UpdateRemoteFrameAdvantage(float remoteAdvantage)
        {
            lastRemoteAdvantage = remoteAdvantage;
        }
    }
}
