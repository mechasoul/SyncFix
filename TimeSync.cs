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
        public static readonly float MAX_SLEEP_DURATION = 9 * World.DELTA_TIME;
        //minimum duration of a sleep. vanilla llb requires 0.02s
        protected static readonly float MIN_SLEEP_FRAMES = 1f;

        //the other (remote) player's playerIndex
        protected readonly int playerIndex;

        private readonly TimeSyncGroupComponent groupComponent;
        private readonly TimeSyncSoloHostEstimateComponent estimateComponent;
        private readonly TimeSyncSoloHostRollbackSizeComponent rollbackSizeComponent;
        private ITimeSyncComponent activeComponent;

        public TimeSync(int playerIndex)
        {
            this.playerIndex = playerIndex;
            this.groupComponent = new TimeSyncGroupComponent(playerIndex);
            this.estimateComponent = new TimeSyncSoloHostEstimateComponent(playerIndex);
            this.rollbackSizeComponent = new TimeSyncSoloHostRollbackSizeComponent(playerIndex);
        }

        public void Reset()
        {
            activeComponent.Reset();
        }

        public void SetInitialValues(float maxPing)
        {
            activeComponent.SetInitialValues(maxPing);
        }

        public void FrameUpdate()
        {
            activeComponent.FrameUpdate();
        }

        //calculates how long to sleep based on local vs remote frame advantage
        public float GetSleepInterval()
        {
            return activeComponent.GetSleepInterval();
        }

        public void OnSleep(float frames)
        {
            activeComponent.OnSleep(frames);

        }

        public void UpdateRemoteFrameAdvantage(float remoteAdvantage)
        {
            groupComponent.UpdateRemoteFrameAdvantage(remoteAdvantage);
        }

        public float GetCurrentLocalAdvantage()
        {
            return groupComponent.CurrentLocalAdvantage;
        }

        public void UpdateNextSmallSleepTime()
        {
            groupComponent.UpdateNextSmallSleepTime();
        }

        public float UpdateRunAheadEstimate(float minimumFrame)
        {
            return estimateComponent.UpdateRunAheadEstimate(minimumFrame);
        }

        public float GetCurrentFrameEstimate()
        {
            return estimateComponent.CurrentFrameEstimate;
        }

        public float GetNextRecommendedSleep()
        {
            return estimateComponent.NextRecommendedSleep;
        }

        public void SetActiveComponent()
        {
            if (StateManager.IsUsingGGPO())
            {
                activeComponent = groupComponent;
            }
            else
            {
                activeComponent = estimateComponent;
            }
        }
    }
}
