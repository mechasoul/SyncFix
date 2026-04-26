using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LLBML.Players;
using Multiplayer;
using UnityEngine;

namespace SyncFix
{
    //this top-level object holds both a group and solo component, so we can swap between them as needed. resulting structure is a bit odd, consider refactoring somehow
    /// <summary>
    /// based on ggpo's time sync. represents a sync state with another player. mostly a combination of ggpo's TimeSync and UdpProtocol
    /// </summary>
    public class TimeSync
    {
        //the other (remote) player's playerIndex
        protected readonly int playerIndex;

        private readonly TimeSyncGroupComponent groupComponent;
        private readonly TimeSyncSoloHostComponent soloComponent;
        private ITimeSyncComponent activeComponent;

        public TimeSync(int playerIndex)
        {
            this.playerIndex = playerIndex;
            this.groupComponent = new TimeSyncGroupComponent(playerIndex);
            this.soloComponent = new TimeSyncSoloHostComponent(playerIndex);
            SetActiveComponent();
        }

        public void Reset()
        {
            SetActiveComponent();
            activeComponent.Reset();
        }

        public void ResetActiveComponent()
        {
            activeComponent.Reset();
        }

        public void FrameUpdate()
        {
            activeComponent.FrameUpdate();
        }

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

        public void UpdateRunAheadEstimate(float minimumFrame)
        {
            soloComponent.UpdateRunAheadEstimate(minimumFrame);
        }

        public float GetCurrentFrameEstimate()
        {
            return soloComponent.CurrentFrameEstimate;
        }

        //should move SyncFixManager's NextRecommendedSleep to groupcomponent and incorporate it into this
        public bool CanSleep()
        {
            return Sync.curFrame > soloComponent.NextRecommendedSleep || soloComponent.ShouldEmergencySleep();
        }

        public bool ShouldEmergencySleep()
        {
            return activeComponent.ShouldEmergencySleep();
        }

        public void SetActiveComponent()
        {
            if (StateManager.IsUsingGroup())
            {
                activeComponent = groupComponent;
            }
            else
            {
                activeComponent = soloComponent;
            }
        }
    }
}
