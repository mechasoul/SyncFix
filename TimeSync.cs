using Multiplayer;

namespace SyncFix
{
    //this top-level object holds both a group and solo component, so we can swap between them as needed.
    //this is goofy and i should just be using a single TimeSyncComponentBase, but i ended up with some methods 
    //unique to group/solo that made it difficult to use just the base class. still there's surely a better
    //way to do this so todo refactor or something?
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

        //TODO move SyncFixManager's NextRecommendedSleep to groupcomponent and incorporate it into this
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
