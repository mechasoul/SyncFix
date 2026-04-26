using System.Collections.Generic;
using Multiplayer;
using UnityEngine;

namespace SyncFix
{
    //note that implementation details in this base clase are mostly concerned with managing recent sleeps, which are used 
    //to automatically scale frame accumulator bounds. this makes each successive sleep harder to trigger than the last, 
    //decaying over time.
    /// <summary>
    /// shared base for group and solo TimeSync objects
    /// </summary>
    public abstract class TimeSyncComponentBase : ITimeSyncComponent
    {
        //these recent sleep window values are used to automatically scale sleep thresholds relative to recent sleeps.
        //the more we've slept recently and the more recent those sleeps were, the higher the threshold becomes.
        //see GetRecentSleepFactor for details. note this is in frames (30s)
        protected static readonly float RECENT_SLEEP_WINDOW_HOST = 1800;
        protected static readonly float RECENT_SLEEP_WINDOW_CLIENT = 1800;
        /*
         * in vanilla Sync.AlignTimes, the game estimates current time for all players, and then calculates 
         * (current time - furthest back player's current time) for all players. this is the amount that each player
         * is running ahead and is used to tell them how long to pause for, in order to try to align their time with the
         * slowest player. 
         * when determining pause time, this resulting number is multiplied by a constant. in vanilla it's 0.75; ie, the
         * time that players pause for is 0.75 * how far ahead they are. this is an "err on the side of caution"
         * thing and helps to avoid oscillating pauses and stuff since the current frame numbers are all estimates anyway.
         * this serves the same purpose for our logic. i tried a few different values and ended up at basically the same.
         * note that when this factor causes sleep durations to dip below 1f, exact timing is difficult to predict. i think
         * unity checks to see if the sleep duration has elapsed every time it runs the update loop, the timing of which
         * will vary from player to player since this is in update and not fixedupdate
         */
        protected static readonly float ALIGN_TIMES_FACTOR = 0.8f;

        protected readonly int playerIndex;
        protected readonly Queue<int> recentSleeps = new Queue<int>();

        public TimeSyncComponentBase(int playerIndex)
        {
            this.playerIndex = playerIndex;
        }

        public abstract float GetSleepInterval();
        public abstract bool ShouldEmergencySleep();

        //returns a base value to be used by GetRecentSleepFactor
        protected abstract float GetRecentSleepBaseFactor();
        protected abstract float GetRecentSleepWindow();

        public virtual void Reset()
        {
            recentSleeps.Clear();
        }

        public virtual void FrameUpdate()
        {
            while (recentSleeps.Count > 0 && Sync.curFrame > recentSleeps.Peek() + GetRecentSleepWindow())
            {
                recentSleeps.Dequeue();
            }
        }

        public virtual void OnSleep(float frames)
        {
            recentSleeps.Enqueue(Sync.curFrame);
        }

        /// <summary>
        /// calculates a multiplier for frame accumulator upper bound based on recent sleeps. each recent sleep contributes:
        /// (sleep window - (current frame - sleep frame)) / sleep window
        /// where sleep frame is the recorded frame on which a sleep occurred.
        /// eg if the sleep window is 1800 and 900 frames have passed since the sleep occurred, then that sleep contributes 0.5.
        /// this is summed over all recent sleeps and multiplied by the defined recent sleep upper bound factor. this resulting 
        /// number + 1 is multiplied by the frame accumulator's base upper bound to produce the final upper bound.
        /// eg in the above example if our base factor was 0.3, then our upper bound multiplier would be 1 + 0.5 * 0.3 = 1.15
        /// </summary>
        /// <returns></returns>
        protected float GetRecentSleepFactor()
        {
            float factor = 0f;
            foreach (int sleepFrame in recentSleeps)
            {
                factor += (GetRecentSleepWindow() - (Sync.curFrame - sleepFrame)) / GetRecentSleepWindow() * GetRecentSleepBaseFactor();
            }
            return factor;
        }

        protected static float LinearClamped(float x, float xStart, float xEnd, float yMin, float yMax)
        {
            return Mathf.Clamp((yMax - yMin) / (xEnd - xStart) * (x - xStart) + yMin, yMin, yMax);
        }
    }
}
