using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Multiplayer;

namespace BlazeSyncFix
{
    public class TimeSyncHost(int playerIndex) : TimeSync(playerIndex)
    {
        public override float EstimateRemoteFrame()
        {
            return Sync.curFrame;
        }
    }
}
