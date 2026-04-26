using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SyncFix
{
    public enum SyncFixMessages
    {
        //initial message to ask if a peer has syncfix installed
        LOBBY_MOD_CHECK = 5040,

        //reply to LOBBY_MOD_CHECK. tells host that the peer has syncfix installed
        LOBBY_MOD_REPLY,

        //message sent at game start to tell all peers to run in group mode. to be used when all players have syncfix
        GAME_USE_GROUP,

        //group syncfix message periodically sent to other players containing the player's local advantage
        GAME_LOCAL_ADVANTAGE,
    }
}
