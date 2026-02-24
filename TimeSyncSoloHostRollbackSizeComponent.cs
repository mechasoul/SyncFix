using LLBML.Players;
using UnityEngine;

namespace BlazeSyncFix
{
    public class TimeSyncSoloHostRollbackSizeComponent(int playerIndex)
    {
        private static readonly float RECENT_ROLLBACK_UPDATE_RATE = 0.2f;
        private static readonly float ROLLBACK_UPDATE_RATE = 0.05f;
        private static readonly float BOUNDS_ERROR = 0.25f;

        private float rollbackCeiling = -1;
        private float rollbackFloor = -1;
        private float rollbackBoundsError = -1;
        private float recentRollbackSize = -1;
        private float rollbackSize = -1;

        public readonly int playerIndex = playerIndex;

        public void Reset()
        {
            rollbackCeiling = -1;
            rollbackFloor = -1;
            rollbackBoundsError = -1;
            recentRollbackSize = -1;
            rollbackSize = -1;
        }

        /*
         * these bounds were determined empirically.
         * noting that ping is in seconds, theoretically target rollback size in frames for both players should be around
         * (the time it takes opponent input to arrive) + (extra processing time). the first term should be around 
         * ping / 2 * FPS, and the second term is generally slightly below 1 from experience (network stuff, time it takes unity to poll 
         * received messages, etc).
         * these are pretty close, so that's good. i think maybe the difference is because ping time is very slightly inflated
         * compared to inputs due to pings having to wait for unity update twice? not totally sure
         */
        public void UpdateRollbackBounds()
        {
            float ping = Player.GetPlayer(playerIndex).peer.ping;
            rollbackCeiling = ping * 29.4f + 1.05f;
            rollbackFloor = ping * 28.2f + 0.6f;
            rollbackBoundsError = Mathf.Clamp(BOUNDS_ERROR * ping / 0.1f, BOUNDS_ERROR, BOUNDS_ERROR * 2);
        }

        public void RecordRollback(int size)
        {
            if (recentRollbackSize == -1)
            {
                recentRollbackSize = size;
            }
            else
            {
                //TODO detect spikes? if outside of expected bounds, start a timer (10-30f), and ignore for that time
                recentRollbackSize = Mathf.Lerp(recentRollbackSize, size, RECENT_ROLLBACK_UPDATE_RATE);
            }
            if (rollbackSize == -1)
            {
                rollbackSize = size;
            }
            else
            {
                rollbackSize = Mathf.Lerp(rollbackSize, size, ROLLBACK_UPDATE_RATE);
            }
        }

        public float GetSleepInterval()
        {
            float sleep = recentRollbackSize - (rollbackCeiling + 0.75f);
            if (sleep <= 0f) return 0f;

            recentRollbackSize = -1;
            return sleep;
        }
    }
}
