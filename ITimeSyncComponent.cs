using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SyncFix
{
    /// <summary>
    /// defines a time sync object, used for time alignment. mostly based on ggpo's timesync object. operates by  
    /// tracking some values per-frame, and calculates recommended sleep intervals based on those values. sleeping
    /// lets the local player wait for other players to catch up, in order to align times and even out rollback
    /// depth. this is how rollback fairness is maintained
    /// </summary>
    public interface ITimeSyncComponent
    {
        /// <summary>
        /// typical update method to be called every frame in order to keep timesync up-to-date
        /// </summary>
        public void FrameUpdate();

        /// <summary>
        /// gets the current recommended sleep interval
        /// </summary>
        /// <returns>a positive number representing the recommended sleep interval in order to maintain fairness. 
        /// if no sleep is recommended, then 0f is returned</returns>
        public float GetSleepInterval();

        /// <summary>
        /// performs some actions to be taken when a sleep is executed
        /// </summary>
        /// <param name="frames"></param>
        public void OnSleep(float frames);

        /// <summary>
        /// resets all state of the timesync component
        /// </summary>
        public void Reset();

        /// <summary>
        /// returns true if an emergency sleep is needed. after a sleep is recommended, typical behaviour is to not
        /// perform frame updates for a specified duration. this method should be checked outside of that typical 
        /// loop, in order to make sure nothing has gone horribly wrong in the meantime
        /// </summary>
        /// <returns></returns>
        public bool ShouldEmergencySleep();
    }
}
