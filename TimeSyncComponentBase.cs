using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Multiplayer;
using UnityEngine;

namespace BlazeSyncFix
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

        protected readonly int playerIndex;
        protected readonly Queue<int> recentSleeps = new Queue<int>();

        public TimeSyncComponentBase(int playerIndex)
        {
            this.playerIndex = playerIndex;
        }

        public abstract float GetSleepInterval();
        public abstract bool ShouldEmergencySleep();

        //returns a base value to be used by GetRecentSleepFactor
        protected abstract float GetRecentSleepUpperBoundFactor();
        protected abstract float GetRecentSleepWindow();

        public virtual void Reset()
        {
            recentSleeps.Clear();
        }

        public virtual void FrameUpdate()
        {
            if (recentSleeps.Count != 0)
            {
                while (Sync.curFrame > recentSleeps.Peek() + GetRecentSleepWindow())
                {
                    recentSleeps.Dequeue();
                }
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
        /// this is summed over all recent sleeps and multiplied by the defined recent sleep upper bound factor. this resulting number
        /// is multiplied by the frame accumulator's base upper bound to produce the final upper bound.
        /// </summary>
        /// <returns></returns>
        protected float GetRecentSleepFactor()
        {
            float factor = 0f;
            foreach (int sleepFrame in recentSleeps)
            {
                factor += (GetRecentSleepWindow() - (Sync.curFrame - sleepFrame)) / GetRecentSleepWindow() * GetRecentSleepUpperBoundFactor();
            }
            return factor;
        }

        protected static float LinearClamped(float x, float xStart, float xEnd, float yMin, float yMax)
        {
            return Mathf.Clamp((yMax - yMin) / (xEnd - xStart) * (x - xStart) + yMin, yMin, yMax);
        }
    }
}
