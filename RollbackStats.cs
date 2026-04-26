using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SyncFix
{
    /// <summary>
    /// static live stat tracker for rollbacks in current game
    /// </summary>
    public class RollbackStats
    {
        private static int numRollbacks = 0;
        private static float total = 0;
        private static float recentSize = -1;
        private static int numSleeps = 0;

        public static int NumRollbacks { get => numRollbacks; }
        public static float Average {  get => total / NumRollbacks; }
        public static float RecentSize { get => recentSize; }
        public static int NumSleeps { get => numSleeps; }

        public static void Reset()
        {
            numRollbacks = 0;
            total = 0;
            recentSize = -1;
            numSleeps = 0;
        }

        public static void AddRollback(int size)
        {
            numRollbacks++;
            total += size;
            if (recentSize == -1)
            {
                recentSize = size;
            }
            else
            {
                recentSize = Mathf.Lerp(recentSize, size, 0.1f);
            }
        }

        public static void AddSleep(float size)
        {
            numSleeps++;
        }

        public static string GetStats()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("rollbacks: ");
            sb.Append(NumRollbacks);
            sb.AppendLine();
            sb.Append("average: ");
            sb.Append(Average);
            sb.AppendLine();
            sb.Append("recent: ");
            sb.Append(RecentSize);
            sb.AppendLine();
            sb.Append("sleeps: ");
            sb.Append(NumSleeps);
            sb.AppendLine();
            return sb.ToString();
        }
    }
}
