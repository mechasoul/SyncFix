using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BlazeSyncFix
{
    public interface ITimeSyncComponent
    {
        public void FrameUpdate();

        public float GetSleepInterval();

        public void OnSleep(float frames);

        public void Reset();

        public void SetInitialValues(float ping);
    }
}
