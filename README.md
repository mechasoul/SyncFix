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

todo

## Thanks

thanks to Glomzubuk and avg_duck for help with getting started with BepInEx mods. thanks to MrGentle, whose exploration of this stuff years ago served as a great starting point. thanks to Tenshi, Bad Joe, and Antero for helping test