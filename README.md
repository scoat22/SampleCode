An ECS implementation of an economic simulator with fantasy elements. 

As of Feb 2-24-25, it's my most complex game implementation. 
I designed a backend SpreadSheet that holds all of the data in simple arrays and sparse sets. Therefore, serialization of all data is trivial. This is a surprisingly nice feature to have, and a time saver. 

Click for a short video.<br />
[![Watch the video](https://i9.ytimg.com/vi/6lIUX4n4voQ/mqdefault.jpg?sqp=CLS29b0G&rs=AOn4CLBsV5d_cPUFJnKE2s0omqivsAMplw)](https://youtu.be/6lIUX4n4voQ)

I set up the systems grouped by tick frequency:<br />
![alt text](https://github.com/scoat22/SampleCode/blob/main/Images/image1.png?raw=true)

I know it looks very simple, and it is. But the engine code is what shines. It's extremely optimized and easy to work with for the developer and artists.

Expanded list of systems:<br />
![alt text](https://github.com/scoat22/SampleCode/blob/main/Images/image2.png?raw=true)

The rendering systems are in the 1/30 Seconds grouped, meaning they'll get ticked 30 times a second. The way I setup the renderer is pretty interesting. If you want to dig into it, the code is here:
CharacterRenderingSystem.cs: https://github.com/scoat22/FantasySimulator/blob/main/Assets/Sprite%20Rendering/CharacterRenderingSystem.cs
GeneralSpriteRenderingSystem.cs: https://github.com/scoat22/FantasySimulator/blob/main/Assets/Sprite%20Rendering/GeneralSpriteRendereringSystem.cs

The CharacterRenderingSystem will search for a sprite sheet that best matches the character definition (their list of components), such as "elf", "warrior", "male", etc. If the artist didn't make a defined sprite sheet for that set of components, it'll fall back to a more generally defined one, such as just "elf". Finally, if it can't render the character, the GeneralSpriteRenderingSystem will catch the work and just render a square at that position. This ensures a very stable rendering system. And it makes it easy for artists to just add spritesheets with whatever component definitions they want on them. I think this system is very flexible for both designers and artists, and very simple! Which is important.

The implementation of the actual game is not finished. It's been more of an engine architecture exploration. I explored many topics in engines/games
For example:
1. Automatic Multithreaded job scheduler, where jobs are automically scheduled and completed based on their dependencies. Which in this case, are simple component arrays. So you end up with a tree of dependencies that neatly completes. 
2. Game model complexity exploration. Games are just models. And models, unlike reality, are constrained by complexity. The more rules you add, complexity increases exponentially. In this case its more obvious because my game is intended as an economic simulation.



If I was to continue development, I would add:
1. Trading.
2. A simple supply and demand model where each agent stores the price they believe each good should cost.
3. 
