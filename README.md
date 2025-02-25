# Code Samples
Author: Ysidro H.</br>

This repo contains some code sample files from an ECS implementation of an economic simulation game with fantasy elements. I don't want to include the whole game's files, for security purposes. But hopefully these samples show some basic programming skills. I wrote all of the code. Please let me know if you have any questions.

It's not the most complicated game I've ever made, but the engine is the most advanced that I've written (It's written on top of Unity but uses basically no Unity features). I know it looks 2D (that's just the art style). It's fully 3D capable.
I designed a backend [SpreadSheet](https://github.com/scoat22/SampleCode/blob/main/Code%20Samples/SpreadSheet.cs) that holds all of the data in [simple arrays](https://github.com/scoat22/SampleCode/blob/main/Code%20Samples/FilledColumn.cs) and [sparse sets](https://github.com/scoat22/SampleCode/blob/main/Code%20Samples/SparseColumn.cs). Therefore, serialization of all data is trivial. This is a surprisingly nice feature to have, and a time saver. 

Click for a short video.<br />
[![Watch the video](https://i9.ytimg.com/vi/6lIUX4n4voQ/mqdefault.jpg?sqp=CLS29b0G&rs=AOn4CLBsV5d_cPUFJnKE2s0omqivsAMplw)](https://youtu.be/6lIUX4n4voQ)

I know it looks very simple, and it is (For instance it lacks a classic UI, but that was for design experimentation purposes. I have many UI design showcases on my website). The engine code is really what shines. It's extremely optimized and easy to work with for the developers and artists.

I set up the systems grouped by tick frequency:<br />
![alt text](https://github.com/scoat22/SampleCode/blob/main/Images/image1.png?raw=true)

Expanded list of systems:<br />
![alt text](https://github.com/scoat22/SampleCode/blob/main/Images/image2.png?raw=true)

I find that setting up systems visually like this helps a lot. If I had more time, I would build even more visualizations for dependency relationships. Where you could easily see which systems rely on which components. I think that would be very interesting for a next-generation game engine.

## Rendering 
The rendering systems are in the 1/30 Seconds group, meaning they'll get ticked 30 times a second. The way I setup the renderer is pretty interesting. If you want to dig into it, the code is here:<br />
[CharacterRenderingSystem.cs](https://github.com/scoat22/SampleCode/blob/main/Code%20Samples/CharacterRenderingSystem.cs)<br />
[GeneralSpriteRenderingSystem.cs](https://github.com/scoat22/SampleCode/blob/main/Code%20Samples/GeneralSpriteRendereringSystem.cs)<br />

The CharacterRenderingSystem will search for a sprite sheet that best matches the character definition (their list of components), such as "elf", "warrior", "male", etc. If the artist didn't make a defined sprite sheet for that set of components, it'll fall back to a more generally defined one, such as just "elf". Finally, if it can't render the character, the GeneralSpriteRenderingSystem will catch the work and just render a square at that position. This ensures a very stable rendering system. And it makes it easy for artists to just add spritesheets with whatever component definitions they want on them. I think this system is very flexible for both designers and artists, and very simple! Which is important.

By no means is this a full display of my rendering code abilities. I'm writing a Vulkan layer for my 3D modeling software that will interact directly with GPUs. You can read more about my render code [here](https://ysidrohartzell.wordpress.com/shaders-materials/) or my 3D modeler [here](https://ysidrohartzell.wordpress.com/).

## Data Backend
The Spreadsheet class is what drives the data backend. It's the "C" in ECS. It stores the component arrays. There are also helper functions in it for copying the spreadsheet and serializing it to disk:<br />
[SpreadSheet.cs](https://github.com/scoat22/SampleCode/blob/main/Code%20Samples/SpreadSheet.cs)<br />

And here are the implementations for the two main component array types, FilledColumn and SparseColumn. The FilledColumn is basically just an array and it means that every entity has that component (used for common components like position, sprite, size, etc). The SparseColumn is used for uncommon components, like velocity, race, health, or any specififc piece of data. You can read more about ECS online, but basicaly then whole point of ECS is aligning all the component data in tightly packed arrays so that system code can blaze through it in a single cache line (with no cache misses, which are stealth performance killers). The sparse column is basically an implementation of a sparse set.<br />
[FilledColumn.cs](https://github.com/scoat22/SampleCode/blob/main/Code%20Samples/FilledColumn.cs)<br />
[SparseColumn.cs](https://github.com/scoat22/SampleCode/blob/main/Code%20Samples/SparseColumn.cs)<br />

## Economy Game Code
You probably also want to see some implementations of the game code. 
The producer system is simple, every tick it will create an entity of the desired type:<br />
[ProducerSystem.cs](https://github.com/scoat22/SampleCode/blob/main/Code%20Samples/ProducerSystem.cs.cs)<br />

## Physics Code
Most games have physics systems so I'll provide some physics code as well.
The main velocity system will just apply each entity's total velocity (which is just a float3 component) to each entity's position.<br />
[VelocitySystem.cs](https://github.com/scoat22/SampleCode/blob/main/Code%20Samples/VelocitySystem.cs)<br />

The MoveSelf system will just add a velocity multiplied by speed, towards the entity's desired position:<br />
[MoveSelf.cs](https://github.com/scoat22/SampleCode/blob/main/Code%20Samples/MoveSelfSystem.cs)<br />

And finally, the RandomMovementSystem will choose a random desired position for each entity (that has a DesiredDestination component)<br />
[RandomMovementSystem.cs](https://github.com/scoat22/SampleCode/blob/main/Code%20Samples/RandomMovementSystem.cs)<br />

The implementation of the actual game is not finished. It's been more of an engine architecture exploration. I explored many topics in engines/games
For example:
1. Automatic Multithreaded job scheduler, where jobs are automically scheduled and completed based on their dependencies. Which in this case, are simple component arrays. So you end up with a tree of dependencies that neatly completes. 
2. Game model complexity exploration. Games are just models. And models, unlike reality, are constrained by complexity. The more rules you add, complexity increases exponentially. 

If I was to continue development, I would add:
1. Trading.
2. A simple supply and demand model where each agent stores the price they believe each good should cost.
3. An actual game loop, where the player can do something to affect the world state. Such as "blessing or cursing" characters or something. Or maybe they can add/remove different rules to the economy simulation to achieve desired quests. Haven't decided exactly what the interaction system looks like yet.
4. 

## Cross Platform Code
Unity and Unreal handles cross platform challenges pretty seamlessly. Although sometimes you need to pay attention because some features are supported by a platform or not. Usually the engine documentation will tell you. But sometimes they don't, or its out of date, so it's best to test! (Testing is always helpful no matter what you're doing). 
Additionally, I followed a guide pretty closesly, but I'm familiar with the Win32 API calls in C/C++. You can read the simple platform layer here: <br />
[Win32_Game.cpp](https://github.com/scoat22/SampleCode/blob/main/Code%20Samples/Win32_Game.cpp)

I think that having a single dedicated platform layer file is way better than random "#if Platform_Windows" statements scattered everywhere in the codebase. Those scattered statements make it way harder to port to more platforms, because you're dealing with random blocks of code all over the code base.
