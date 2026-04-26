using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SyncFix
{
    /*
     * vanilla llb checks advantage by performing a check every 60f. ggpo checks advantage by waiting for some predefined interval (default
     * 240f), then checking advantage every frame until something out of threshold is found, at which point it goes back to sleep for the
     * same interval. both check instant advantage on a single frame, which produces very noisy results when trying to maintain an
     * extremely small advantage window as is desirable in llb (we really want to shoot for like 0.5f; at most 1f. llb vanilla uses 1.2f
     * and ggpo uses default 3f, for context. llb is a very rollback-sensitive game). thus, we want a solution that's a bit more resilient.
     * i choose to use an accumulator object to track long-term advantage problems. 
     * 
     * the accumulator works as follows: the accumulator has a current value. on every frame we have a 
     * frame value. we update the current value towards the new frame value by lerping with some update rate, creating an exponential 
     * moving average. the accumulator also defines an upper and lower bound. if the new current value exceeds the
     * upper bound, then the accumulator increases by (value - upper bound). if the value is below the lower bound, then it decreases by 
     * (lower bound - value). if the accumulator reaches some threshold, then we act based on that. proper tweaking of these values (bounds,
     * threshold, update rate) provides an algorithm that is resistant to noise but still responds quickly to real issues
     * 
     * it's a bit overkill for sure. just calculating the average of the most recent x frames or using an exponential moving average would
     * get you like 90% of the way there
     */
    /// <summary>
    /// an accumulator that adjusts per-frame based on some value. used to determine things like local frame advantage in a way that's
    /// less susceptible to noise than instantaneous checks
    /// </summary>
    public class FrameAccumulator
    {
        public readonly int playerIndex;
        public readonly float updateRate;
        public readonly float threshold;
        public float currentValue = -1f;
        public float accumulator = 0f;
        public float upperBound = 0f;
        public float lowerBound = 0f;

        //frame, playerIndex -> upper bound/lower bound
        //allows us to update bounds dynamically per-frame. useful to automatically change based on eg ping or recent performance
        public Func<int, int, float> upperBoundFunc;
        public Func<int, int, float> lowerBoundFunc;

        public FrameAccumulator(int playerIndex, float updateRate, float threshold)
        {
            this.playerIndex = playerIndex;
            this.updateRate = updateRate;
            this.threshold = threshold;
        }

        public void Reset()
        {
            currentValue = -1f;
            accumulator = 0f;
        }

        private void UpdateValue(float newValue)
        {
            if (currentValue == -1f)
            {
                currentValue = newValue;
            }
            else
            {
                currentValue = Mathf.Lerp(currentValue, newValue, updateRate);
            }
        }

        public void FrameUpdate(int frame, float frameValue)
        {
            upperBound = upperBoundFunc(frame, playerIndex);
            lowerBound = lowerBoundFunc(frame, playerIndex);

            float prevValue = currentValue;
            UpdateValue(frameValue);
            //Plugin.Logger.LogInfo($"frame value: {frameValue}, old: {prevValue}, new: {currentValue}, upper: {upperBound}");

            if (currentValue > upperBound)
            {
                float dif = currentValue - upperBound;
                accumulator += dif;
                //Plugin.Logger.LogInfo($"added {dif}; new: {accumulator}");
            }
            else if (currentValue < lowerBound)
            {
                float dif = currentValue - lowerBound;
                accumulator = System.Math.Max(accumulator + dif, 0f);
                //Plugin.Logger.LogInfo($"added {dif}; new: {accumulator}");
            }
        }

        public bool ThresholdReached()
        {
            return accumulator >= threshold;
        }

        /// <summary>
        /// used for emergency problem detection. typical behaviour is to sleep for some amount of time after the threshold has been reached.
        /// this allows us to still react in case something has very obviously gone wrong
        /// </summary>
        /// <returns></returns>
        public bool ThresholdVeryReached()
        {
            return accumulator >= threshold * 10f;
        }
    }
}
