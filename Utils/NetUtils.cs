using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LLBML.Players;
using Multiplayer;
using UnityEngine;

namespace BlazeSyncFix.Utils
{
    public class NetUtils
    {
        private static float maxPing = 0f;
        private static int maxPingPlayer = -1;

        //tracking max ping is useful for a lot of solo syncfix stuff. just update it here whenever a ping is resolved
        public static float MaxPing { get => maxPing; }

        //returns an estimate for one-way travel time for the given ping, in frames.
        //empirically calculated. for some reason this is measurably better than the expected estimate of half ping?
        //could definitely be improved upon further, but given that it's only used for solo syncfix (a backup algorithm) and it's also
        //already much better than vanilla to the point that i think there's no more observable host advantage, i'm sticking with this
        public static float GetTravelTimeEstimate(float ping)
        {
            return Mathf.Pow(ping, 2f) * -17.3f + ping * 36.2f + 0.23f;
        }

        /*
         * called whenever a ping is resolved (ie, the provided peer's ping changes). keeps max ping updated and accurate at all times.
         * whenever we resolve a ping, do the following:
         * 1. if the peer IS NOT the player who has the current max ping, check to see if their new ping is the new max.
         *      if so, update; if not, then max ping can't have changed
         * 2. if the peer IS the player who has the current max ping, check to see if their new ping is the new max.
         *      if so, update; if not, compare with all players to find new max (since their ping could have dropped below a different player's)
         */
        public static void UpdateMaxPing(Peer peer)
        {
            if (peer.playerNr == maxPingPlayer)
            {
                if (peer.ping > maxPing)
                {
                    maxPing = peer.ping;
                }
                else
                {
                    maxPing = GetMaxPing(out maxPingPlayer);
                }
            }
            else if (peer.ping > maxPing)
            {
                maxPing = peer.ping;
                maxPingPlayer = peer.playerNr;
            }
        }

        public static float GetMaxPing(out int maxPlayerIndex)
        {
            float maxPing = -1f;
            int index = -1;
            foreach (var player in Player.EPlayers())
            {
                //player.inMatch && (!Sync.isActive || Sync.IsValidOther(player.nr)) && player.peer.ping > maxPing
                if (player.NGLDMOLLPLK && (!Sync.isActive || Sync.IsValidOther(player.CJFLMDNNMIE)) && player.KLEEADMGHNE.ping > maxPing) 
                {
                    maxPing = player.KLEEADMGHNE.ping; //player.peer.ping
                    index = player.CJFLMDNNMIE; //player.nr
                }
            }
            maxPlayerIndex = index;
            return maxPing;
        }

        public static void ResetMaxPing()
        {
            maxPing = 0f;
            maxPingPlayer = -1;
        }
    }
}
