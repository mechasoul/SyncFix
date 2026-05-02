# Sync Fix

This is a BepInEx plugin that fixes host advantage in Lethal League Blaze. It can be installed with a mod manager of your choice, such as Thunderstore Mod Manager or r2modman. Just install the mod, and it'll work automatically.

## FAQ

### how do I use this mod

You don't have to do anything other than install it. It will work regardless of whether the people you're playing with have the mod (though it works best if everyone has it).

### I don't notice anything different when I use this mod

Host advantage is extremely minor. Most players would never notice it. Even among players that claim to notice it, I'd guess that a lot of them are tricking themselves. Here's a visual demo of host advantage (details in the video description): https://www.youtube.com/watch?v=3YgEoF-yWAo If you can't see a difference between the red and green Raptors, then congratulations, you are immune to host advantage

This mod doesn't require anything to run though, so there's not much reason not to use it

### I still can't play with my friend who has 300 ping / I still get more rollbacks than my friend / this didn't make the lag go away

This mod is: a fix for host advantage

This mod is not: a networking magic bullet

It won't fix any of those things. It fixes an extremely minor asymmetry between the host and everyone else. That's it. It won't have any effect on how many rollbacks you experience, as that's unrelated to host advantage.

Below is a brief explanation of host advantage and how this mod fixes it (if you're curious), as well as a detailed explanation of host advantage and how this mod fixes it (if you're a nerd).

## A brief explanation of host advantage

### what is host advantage?

Host advantage in the context of Lethal League Blaze refers to a very slight advantage in average rollback size for the host player (ie, player 1). On average, the host experiences rollbacks that are 1.5 to 2 frames smaller than what other players experience. This means that rollbacks experienced by the host will be marginally less disorienting than rollbacks experienced by other players. 

In most games, ~1.5 - 2f would be a trivial difference (in fact, the default implementation of GGPO - the de facto standard for rollback netcode - ignores differences of less than 3 frames). Lethal League Blaze, however, is not most games. Moves have almost exclusively 1 frame startup, and movement is fast and twitchy. It would be difficult to design a game more sensitive to rollbacks. Consequently, while this difference is _very_ small, it could affect things at a high level of play.

### why does this happen?

The main reason is that Lethal League Blaze's time sync code has a slight host bias. "Time sync" here refers to a part of rollback code that is not widely talked about, but is pretty important for ensuring fairness. It basically boils down to making sure that all players are operating at close to the same point in time ingame - that no one is running ahead or behind.

### how does this mod fix it

The best solution is to adopt the model used by GGPO: all players periodically communicate with all other players in order to sync their times. If all players have the mod installed, this is what it does. This provides the best results.

If not all players have the mod installed, then this isn't possible. In this case, the mod does the best it can - if only host has the mod, then they use more generous estimates for other players to try to offset the innate host bias. If only client has the mod, then they run their own version of the time sync code, essentially forcing symmetry between them and the host. From my testing, these changes remove like 80% of host advantage in the worst case (typically much more), making it basically totally negligible.

## A detailed explanation of host advantage

### determining the effects of host advantage

Host advantage has been discussed with respect to Lethal League Blaze since alpha testing, but due to the uncertainty of online multiplayer and the nonspecific nature of the problem (usually something like "it feels better as host" or "there's less lag as host"), it was never properly diagnosed and fixed. Host advantage is also generally a term reserved for client-server networking, and it's unusual to hear it in the context of a game with rollback networking; together with the complexity of rollback netcode and misunderstanding of what a problem with rollback actually looks like, this probably caused the developers to write it off as a non-issue. And again, the effects _are_ very minor. All of these factors together caused it to go undiagnosed for a long time.

In order to determine what host advantage is, we have to control all other possible sources of asymmetry. A naive idea is to have two players record their gameplay and compare rollbacks between the two; this is flawed. Players will press different buttons at different times, unavoidably causing rollbacks to occur at different times between the two players. Players who press buttons more frequently will cause rollbacks for their opponents more frequently. This is all by design and working as intended.

So to actually demonstrate the effects, I rigged up a BepInEx plugin to allow two instances of the game running on the same machine to connect via online multiplayer. Next, I added a lag simulation layer that could artificially produce latency and packet loss between the two clients. Finally, I recorded a sequence of inputs and set things up so either player could play back that sequence at game start, from the same starting position. This meant that inputs and network conditions were controlled, and any measurable long-term differences in the rollbacks experienced by each player must be due to player port differences, since everything else was equal.

The results were as described above. On average, rollbacks experienced by player 1 were smaller than those experienced by other players, regardless of the lag I tested with. This is host advantage (note that the number of rollbacks experienced by each player was roughly equal, as expected)

### determining the cause of host advantage

The game never seems to handle host and client networking differently, so this was difficult to track down. The major difference is the game's time sync logic (Multiplayer.Sync.AlignTimes). To understand why this produces host advantage, we need to briefly talk about why time sync is important. Basically, the goal of time sync is to sync up the times of all players' games - that is, each player's game wants to avoid running ahead of other players' games. This is because running ahead is disadvantageous in rollback. 

As an example, suppose it takes 5 frames for data sent by player 1 to reach player 2, and vice versa. If both players' times are aligned, then when player 1 sends their data for frame 100 to player 2, player 2 is sending their data for frame 100 to player 1 at the same time. Both players receive this data on frame 105, and if either player needs to roll back based on this data, then they roll back a total of 5 frames. This is fair.

Now imagine if player 2 is running 5 frames ahead - ie, when player 1 is on frame 100, player 2 is on frame 105. Both players send their data for their current frame to the other. In 5 frames, player 1 is on frame 105 and receives player 2's data for frame 105, so they never need to roll back. Meanwhile, player 2 is on frame 110 and receives player 1's data for frame 100, potentially causing a 10-frame rollback. This is unfair. This is why time sync is important.

Lethal League Blaze's time sync logic is very close to fair - the host estimates each remote player's current frame as (remote player's last known frame + ping / 2), where ping / 2 is a best-guess approximation for the travel time required for that player's data to reach the host. It calculates this for each player (using its known current frame for itself), compares each player's estimated current frame to the minimal estimated current frame, and if anyone is running too far ahead, it sends them a message telling them to briefly pause to let other players catch up.

At a glance, there's nothing unfair about this. The issue stems from the fact that the host always has perfect knowledge of themselves and imperfect knowledge of everyone else. Suppose a tiny spike in latency causes traffic from a remote player to be delayed by a couple of frames - if this spike happens around the same time as time alignment, it will cause the host to believe that player is a couple of frames behind where they really are, and so the host will slow down to match (note that this spike is unlikely to be reflected in the ping / 2 estimate, since ping only updates once per second). These incorrect guesses can happen for remote players, but never for the host. This causes problems long-term.

There's also a slight problem with the game's initial time sync - due to an oversight, it uses ping rather than ping / 2. This causes the host to generally run behind at match start, giving them a slight advantage depending on latency. This will usually be detected and fixed in the first few seconds of the match, but it's worth mentioning.

It's important to emphasize that the nature of networking makes this all pretty unpredictable in the short term. I've measured games where the client experienced an advantage, rather than the host. And, once again, the effects are very minor.

## Thanks

thanks to Glomzubuk and avg_duck for help with getting started with BepInEx mods. thanks to MrGentle, whose exploration of this stuff years ago served as a great starting point. thanks to Tenshi, Bad Joe, and Antero for helping test