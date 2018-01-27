﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PacManBot.Modules.PacManModule
{
    public class Game
    {
        static public List<Game> gameInstances = new List<Game>();

        //Constants
        public const string LeftEmoji = "⬅", UpEmoji = "⬆", DownEmoji = "⬇", RightEmoji = "➡", WaitEmoji = "⏸", RefreshEmoji = "🔃"; //Controls
        private const char CharPlayer = 'O', CharFruit = '$', CharGhost = 'G', CharDoor = '-', CharPellet = '·', CharPowerPellet = '●', CharSoftWall = '_', CharSoftWallPellet = '~'; //Read from map
        private const char CharPlayerDead = 'X', CharGhostFrightened = 'E'; //Displayed
        private const int PowerTime = 20, ScatterCycle = 100, ScatterTime1 = 30, ScatterTime2 = 20; //Mechanics constants
        
        private readonly static char[] GhostAppearance = { 'B', 'P', 'I', 'C' };
        private readonly static int[] GhostSpawnPauseTime = { 0, 3, 15, 35 };
        private readonly static Dir[] AllDirs = { Dir.up, Dir.left, Dir.down, Dir.right }; //Order of preference when deciding direction

        //Fields
        public readonly ulong channelId; //Which channel this game is located in
        public readonly bool custom = false;
        public ulong messageId = 1; //The focus message of the game, for controls to work. Even if not set, it must be a number above 0
        public State state = State.Active;
        public bool mobileDisplay = false;
        public int score = 0;
        public int time = 0; //How many turns have passed

        private readonly Random random;
        private readonly Fruit[] fruitTypes; //Stores the fruits that will be available in this game
        private readonly int maxPellets;
        private int pellets; //Pellets remaining
        private int oldScore = 0; //Score obtained last turn
        private char[,] map;
        private Player player;
        private List<Ghost> ghosts;
        private Fruit fruit;

        //Fruit
        private readonly Pos fruitSpawnPos; //Where all fruit will spawn
        private Pos FruitSecondPos => fruitSpawnPos + Dir.right; //Second tile which fruit will also occupy
        private int FruitTrigger1 => maxPellets - 70; //Amount of pellets remaining needed to spawn fruit
        private int FruitTrigger2 => maxPellets - 170;


        //Game data types
        public enum State { Active, Lose, Win }

        public enum Dir { none, up, down, left, right }

        public enum AiType { Blinky, Pinky, Inky, Clyde }

        public enum AiMode { Chase, Scatter, Frightened }


        //Game objects
        public class Pos //Coordinate in the map
        {
            public int x, y;
            public Pos(int x, int y)
            {
                this.x = x;
                this.y = y;
            }

            public static bool operator !=(Pos pos1, Pos pos2) => !(pos1 == pos2);
            public static bool operator ==(Pos pos1, Pos pos2)
            {
                if (pos1 is null || pos2 is null) return ReferenceEquals(pos1, pos2);
                return pos1.x == pos2.x && pos1.y == pos2.y;
            }

            public static Pos operator +(Pos pos1, Pos pos2) => new Pos(pos1.x + pos2.x, pos1.y + pos2.y);
            public static Pos operator -(Pos pos1, Pos pos2) => new Pos(pos1.x - pos2.x, pos1.y - pos2.y);

            public static Pos operator +(Pos pos, Dir dir) //Moves position in the given direction
            {
                switch (dir)
                {
                    case Dir.up:    return new Pos(pos.x, pos.y - 1);
                    case Dir.down:  return new Pos(pos.x, pos.y + 1);
                    case Dir.left:  return new Pos(pos.x - 1, pos.y);
                    case Dir.right: return new Pos(pos.x + 1, pos.y);
                    default: return pos;
                }
            }

            public static float Distance(Pos pos1, Pos pos2) => (float)Math.Sqrt(Math.Pow(pos2.x - pos1.x, 2) + Math.Pow(pos2.y - pos1.y, 2));
        }

        private class Player
        {
            public readonly Pos origin; //Position it started at
            public Pos pos; //Position on the map
            public Dir dir = Dir.none; //Direction it's facing
            public int power = 0; //Time left of power mode
            public int ghostStreak = 0; //Ghosts eaten during the current power mode

            public Player(Pos pos)
            {
                this.pos = pos ?? new Pos(0, 0);
                origin = this.pos;
            }
        }

        private class Fruit
        {
            public int timer = 0;
            public readonly char char1, char2;
            public readonly int points;

            public Fruit(char char1, char char2, int points)
            {
                this.char1 = char1;
                this.char2 = char2;
                this.points = points;
            }
        }

        private class Ghost
        {
            public readonly Pos origin; //Tile it spawns in
            public readonly Pos corner; //Preferred corner
            public Pos pos; //Position on the map
            public Pos target; //Tile it's trying to reach
            public Dir dir = Dir.none; //Direction it's facing
            public AiType type; //Ghost behavior type
            public AiMode mode; //Ghost behavior mode
            public int pauseTime; //Time remaining until it can move

            public Ghost(Pos pos, AiType type, Pos corner)
            {
                this.pos = pos;
                this.type = type;
                this.corner = corner ?? pos;
                origin = pos;
                pauseTime = GhostSpawnPauseTime[(int)type];
            }

            public void AI(Game game)
            {
                //Decide mode
                if (game.player.power <= 1) DecideMode(game.time); //Doesn't change mode while the player is in power mode

                if (pauseTime > 0)
                {
                    pauseTime--;
                    return;
                }

                //Decide target
                switch (mode)
                {
                    case AiMode.Chase: //Normal
                        switch(type)
                        {
                            case AiType.Blinky:
                                target = game.player.pos;
                                break;

                            case AiType.Pinky:
                                target = game.player.pos;
                                target += game.player.dir.OfLength(4); //4 squares ahead
                                if (game.player.dir == Dir.up) target += Dir.left.OfLength(4); //Intentional bug from the original arcade
                                break;

                            case AiType.Inky:
                                target = game.player.pos;
                                target += game.player.dir.OfLength(2); //2 squares ahead
                                if (game.player.dir == Dir.up) target += Dir.left.OfLength(2); //Intentional bug from the original arcade
                                target += target - game.ghosts[(int)AiType.Blinky].pos; //Opposite position relative to Blinky
                                break;

                            case AiType.Clyde:
                                if (Pos.Distance(pos, game.player.pos) > 8) target = game.player.pos;
                                else target = corner; //When close, gets scared
                                break;
                        }
                        break;

                    case AiMode.Scatter:
                        target = corner;
                        break;

                    case AiMode.Frightened:
                        for (int i = 0; i < 20; i++)
                        {
                            target = pos + (Dir)(game.random.Next(1, 5)); //Random adjacent empty space, 20 attempts
                            if (game.NonSolid(target)) break;
                        }
                        break;
                }

                //Decide movement
                Dir newDir = Dir.none;
                
                if (game.Map(pos) == CharDoor || game.Map(pos + Dir.up) == CharDoor)
                {
                    newDir = Dir.up; //If it's inside the cage
                }
                else if (type == AiType.Blinky && !game.custom && game.time < 4)
                {
                    newDir = Dir.left; //Blinky starts already facing left
                }
                else //Track target
                {
                    float distance = 1000f;
                    foreach (Dir testDir in AllDirs) //Decides the direction that will get it closest to its target
                    {
                        Pos testPos = pos + testDir;
                        
                        if (testDir == Dir.up && (game.Map(testPos) == CharSoftWall || game.Map(testPos) == CharSoftWallPellet)) continue; //Can't go up these places
                        if (testDir == dir.Opposite() && mode != AiMode.Frightened && !JustChangedMode(game.time)) continue; //Can't turn 180 degrees except on special situations

                        if (game.NonSolid(testPos) && Pos.Distance(testPos, target) < distance) //Check if it can move to the tile and if this direction is better than the previous
                        {
                            newDir = testDir;
                            distance = Pos.Distance(testPos, target);
                        }
                        //Console.WriteLine($"Target: {target.x},{target.y} / Ghost: {pos.x},{pos.y} / Test Dir: {(pos + testDir).x},{(pos + testDir).y} / Test Dist: {Pos.Distance(pos + testDir, target)}"); //For debugging AI
                    }
                }

                dir = newDir;
                pos += newDir;
                game.WrapAround(ref pos);
            }

            public void DecideMode(int time)
            {
                if (time < 4 * ScatterCycle  //In set cycles, a set number of turns is spent in scatter mode, up to 4 times
                    && (time < 2 * ScatterCycle && time % ScatterCycle < ScatterTime1
                    || time >= 2 * ScatterCycle && time % ScatterCycle < ScatterTime2)
                ) { mode = AiMode.Scatter; }
                else { mode = AiMode.Chase; }
            }

            private bool JustChangedMode(int time)
            {
                for (int i = 0; i < 2; i++) if (time == i * ScatterCycle || time == i * ScatterCycle + ScatterTime1) return true;
                for (int i = 2; i < 4; i++) if (time == i * ScatterCycle || time == i * ScatterCycle + ScatterTime2) return true;
                return false;
            }
        }


        //Game methods

        public Game(ulong channelId, string customMap = null)
        {
            this.channelId = channelId;
            random = new Random();
            
            string[] newMap;
            if (customMap == null) newMap = File.ReadAllLines(Program.File_GameMap);
            else
            {
                newMap = customMap.Trim(new char[] { '\n', ' ' }).Trim(new char[] { '\n', '`' }).Split('\n');
                custom = true;
            }
            LoadMap(newMap);
            
            maxPellets = pellets;

            Pos playerPos = FindChar(CharPlayer); //Set player
            player = new Player(playerPos);
            if (playerPos != null) map[playerPos.x, playerPos.y] = ' ';

            Pos fruitPos = FindChar(CharFruit); //Set fruit defaults
            fruitSpawnPos = fruitPos;
            if (fruitPos != null) map[fruitPos.x, fruitPos.y] = ' ';
            fruitTypes = new Fruit[]{ new Fruit('x', 'x', 1000), new Fruit('w', 'w', 2000) };
            fruit = fruitTypes[0];

            ghosts = new List<Ghost>(); //Set ghosts
            Pos[] ghostCorners = new Pos[] { new Pos(2, -3), new Pos(map.LengthX() - 3, -3), new Pos(0, map.LengthY()), new Pos(map.LengthX() - 1, map.LengthY()) }; //Matches original game
            for (int i = 0; i < 4; i++)
            {
                Pos ghostPos = FindChar(CharGhost);
                if (ghostPos == null) break;
                Pos cornerPos = ghostCorners[i % 2 == 0 ? i + 1 : i - 1]; //Goes in order: Top-Right Top-Left Bottom-Right Bottom-Left
                ghosts.Add(new Ghost(ghostPos, (AiType)i, cornerPos));
                map[ghostPos.x, ghostPos.y] = ' ';
            }
        }

        public void DoTick(Dir direction)
        {
            if (state != State.Active) return; //Failsafe

            time++;
            oldScore = score;

            //Player
            if (direction != Dir.none) player.dir = direction;
            if (NonSolid(player.pos + direction)) player.pos += direction;
            WrapAround(ref player.pos);

            //Fruit
            if (fruit.timer > 0)
            {
                fruit.timer--;
                if (fruitSpawnPos == player.pos || FruitSecondPos == player.pos)
                {
                    score += fruit.points;
                    fruit.timer = 0;
                }
            }

            //Pellet collision
            char tile = Map(player.pos);
            if (tile == CharPellet || tile == CharPowerPellet || tile == CharSoftWallPellet)
            {
                pellets--;
                if ((pellets == FruitTrigger1 || pellets == FruitTrigger2) && fruitSpawnPos != null)
                {
                    fruit = fruitTypes[(pellets >= FruitTrigger1) ? 0 : 1];
                    fruit.timer = random.Next(25, 30 + 1);
                }

                score += (tile == CharPowerPellet) ? 50 : 10;
                map[player.pos.x, player.pos.y] = (tile == CharSoftWallPellet) ? CharSoftWall : ' ';
                if (tile == CharPowerPellet)
                {
                    player.power += PowerTime;
                    foreach (Ghost ghost in ghosts) ghost.mode = AiMode.Frightened;
                }

                if (pellets == 0)
                {
                    state = State.Win;
                    return;
                }
            }

            //Ghosts
            foreach (Ghost ghost in ghosts)
            {
                bool didAI = false;
                while (true) //Checks player collision before and after AI
                {
                    if (player.pos == ghost.pos) //Collision
                    {
                        if (ghost.mode == AiMode.Frightened)
                        {
                            ghost.pos = ghost.origin;
                            ghost.dir = Dir.none;
                            ghost.pauseTime = 6;
                            ghost.DecideMode(time); //Removes frightened state
                            score += 200 * (int)Math.Pow(2, player.ghostStreak); //Each ghost gives double the points of the last
                            player.ghostStreak++;
                        }
                        else state = State.Lose;

                        didAI = true; //Skips AI after collision
                    }

                    if (didAI) break; //Doesn't run AI twice

                    ghost.AI(this); //Full ghost behavior
                    didAI = true;
                }
            }

            if (player.power > 0) player.power--;
            if (player.power == 0) player.ghostStreak = 0;
        }

        public string GetDisplay()
        {
            try
            {
                StringBuilder display = new StringBuilder(); //The final display in string form
                char[,] displayMap = (char[,])map.Clone(); //The display array to modify
                
                //Scan replacements
                for (int y = 0; y < map.LengthY(); y++)
                {
                    for (int x = 0; x < map.LengthX(); x++)
                    {
                        if (displayMap[x, y] == CharSoftWall) displayMap[x, y] = ' ';
                        else if (displayMap[x, y] == CharSoftWallPellet) displayMap[x, y] = CharPellet;
                        
                        if (mobileDisplay) //Mode with simplified characters
                        {
                            if (!NonSolid(x, y) && displayMap[x, y] != CharDoor) displayMap[x, y] = '#'; //Walls
                            else if (displayMap[x, y] == CharPellet) displayMap[x, y] = '.'; //Pellets
                            else if (displayMap[x, y] == CharPowerPellet) displayMap[x, y] = 'o'; //Power pellets
                        }
                    }
                }

                //Adds fruit, ghosts and player
                if (fruit.timer > 0)
                {
                    displayMap[fruitSpawnPos.x, fruitSpawnPos.y] = fruit.char1;
                    displayMap[FruitSecondPos.x, FruitSecondPos.y] = fruit.char2;
                }
                foreach (Ghost ghost in ghosts)
                {
                    displayMap[ghost.pos.x, ghost.pos.y] = (ghost.mode == AiMode.Frightened) ? CharGhostFrightened : GhostAppearance[(int)ghost.type];
                }
                displayMap[player.pos.x, player.pos.y] = (state == State.Lose) ? CharPlayerDead : CharPlayer;

                //Converts 2d array to string
                for (int y = 0; y < displayMap.LengthY(); y++)
                {
                    for (int x = 0; x < displayMap.LengthX(); x++)
                    {
                        display.Append(displayMap[x, y]);
                    }
                    display.Append('\n');
                }

                //Add text to the side
                string[] info = //Info panel
                {
                    $" ┌{"< MOBILE MODE >".If(mobileDisplay)}",
                    $" │ {"#".If(!mobileDisplay)}Time: {time}",
                    $" │ {"#".If(!mobileDisplay)}Score: {score}{$" +{score - oldScore}".If(!mobileDisplay && score - oldScore != 0)}",
                    $" │ {$"{"#".If(!mobileDisplay)}Power: {player.power}".If(player.power > 0)}",
                    $" │ ",
                    $" │ {CharPlayer}{" - Pac-Man".If(!mobileDisplay)}{$": {player.dir}".If(player.dir != Dir.none)}",
                    $" │ ",
                    $" │ ", " │ ", " │ ", " │ ", //7-10: ghosts
                    $" │ ",
                    $" │ {($"{fruit.char1}{fruit.char2}{" - Fruit".If(!mobileDisplay)}: {fruit.timer}").If(fruit.timer > 0)}",
                    $" └ {$"+{score - oldScore}".If(mobileDisplay && score - oldScore != 0)}"
                };

                for (int i = 0; i < 4; i++) //Ghost info
                {
                    if (i + 1 > ghosts.Count) continue;
                    char appearance = (ghosts[i].mode == AiMode.Frightened) ? CharGhostFrightened : GhostAppearance[i];
                    info[i + 7] = $" │ {appearance}{$" - {(AiType)i}".If(!mobileDisplay)}{$": {ghosts[i].dir}".If(ghosts[i].dir != Dir.none)}";
                }

                for (int i = 0; i < info.Length && i < map.LengthY(); i++) //Insert info
                {
                    int insertIndex = (i + 1) * displayMap.LengthX(); //Skips ahead a certain amount of lines
                    for (int j = i - 1; j >= 0; j--) insertIndex += info[j].Length + 1; //Takes into account the added line length of previous info
                    display.Insert(insertIndex, info[i]);
                }

                //Code tags
                switch (state)
                {
                    case State.Active:
                        display.Insert(0, mobileDisplay ? "```\n" : "```css\n");
                        break;

                    case State.Lose:
                        display.Insert(0, "```diff\n");
                        display.Replace("\n", "\n-", 0, display.Length - 1); //All red
                        break;

                    case State.Win:
                        display.Insert(0, "```diff\n");
                        display.Replace("\n", "\n+", 0, display.Length - 1); //All green
                        break;
                }
                display.Append("```");

                if (state != State.Active || custom) //Secondary info box
                {
                    display.Append("```diff");
                    if (state == State.Win) display.Append("\n+You won!");
                    else if (state == State.Lose) display.Append("\n-You lost!");
                    if (custom) display.Append("\n*** Custom game: Score won't be registered. ***");
                    display.Append("```");
                }

                return display.ToString();
            }
            catch
            {
                return "```There was an error displaying the game. If you're using a custom map, make sure it's valid. If this problem persists, please contact the author of the bot.```";
            }
        }

        private Pos FindChar(char c, int index = 0) //Finds the specified character instance in the map
        {
            for (int y = 0; y < map.LengthY(); y++)
            {
                for (int x = 0; x < map.LengthX(); x++)
                {
                    if (map[x, y] == c)
                    {
                        if (index > 0) index--;
                        else
                        {
                            return new Pos(x, y);
                        }
                    }
                }
            }

            return null;
        }

        private bool NonSolid(int x, int y) => NonSolid(new Pos(x, y));
        private bool NonSolid(Pos pos) //Defines which tiles in the map entities can move through
        {
            WrapAround(ref pos);
            return (Map(pos) == ' ' || Map(pos) == CharPellet || Map(pos) == CharPowerPellet || Map(pos) == CharSoftWall || Map(pos) == CharSoftWallPellet);
        }
        
        private char Map(Pos pos) //Returns the character at the specified pos
        {
            WrapAround(ref pos);
            return map[pos.x, pos.y];
        }

        private void WrapAround(ref Pos pos) //Wraps the position from one side of the map to the other if it's out of bounds
        {
            if (pos.x < 0) pos.x = map.LengthX() + pos.x;
            else if (pos.x > map.LengthX() - 1) pos.x -= map.LengthX();
            if (pos.y < 0) pos.y = map.LengthY() + pos.y;
            else if (pos.y > map.LengthY() - 1) pos.y -= map.LengthY();
        }

        private void LoadMap(string[] lines)
        {
            int width = lines[0].Length;
            int height = lines.Length;

            char[,] newMap = new char[width, height];
            try
            {
                bool hasEmptySpace = false;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        newMap[x, y] = lines[y].ToCharArray()[x];
                        if (newMap[x, y] == ' ') hasEmptySpace = true;
                        if (newMap[x, y] == CharPellet || newMap[x, y] == CharPowerPellet || newMap[x, y] == CharSoftWallPellet)
                        {
                            pellets++;
                            hasEmptySpace = true;
                        }
                    }
                }

                if (!hasEmptySpace) throw new Exception("Map is completely solid");

            }
            catch { throw new Exception("Invalid map"); }

            map = newMap;

            if (custom) File.AppendAllLines("logs/custom.txt", lines);
        }
    }
}
