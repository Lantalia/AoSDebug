using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace AoSDebug
{

    /*
     * Store list of blockid to blockid remappings
     * Manage a queue of chunks that need scanning
     * Mannage a queue of coordinates that need to be cleaned
     * Background thread for scanning chunks
     * Foreground system to actually update individual blocks
     */
    internal class Cleaner : ModSystem
    {
        private ICoreServerAPI sapi;
        private ConcurrentQueue<BlockPos> performCleanQueue = new ConcurrentQueue<BlockPos>();
        private ConcurrentQueue<BlockPos> checkCleanQueue = new ConcurrentQueue<BlockPos>();
        private Dictionary<int, int> blockIdMap = new Dictionary<int, int>();

        private CheckCleanThread checkCleanThread;
        
        public static int processInterval = 50;

        private bool saveToWorld = false;
        private int blocksRemapped = 0;

        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }
        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            api.Event.SaveGameLoaded += onSaveGameLoaded;
            api.Event.GameWorldSave += onGameGettingSaved;
            api.Event.RegisterGameTickListener(processPerformCleanQueue, processInterval);

            var parsers = api.ChatCommands.Parsers;

            sapi.ChatCommands.GetOrCreate("aosdebug")            
                .BeginSubCommand("clean")
                .WithDescription("Show unknown block cleaner state")
                .HandleWith(_ => { return TextCommandResult.Success("Cleaner queues: chunks:" + checkCleanQueue.Count + " pending:" + performCleanQueue.Count + " remapped: " + blocksRemapped); })
                .BeginSubCommand("register")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithDescription("register unknown block id and the replacement block code to convert it to")
                    .WithArgs(parsers.Int("unknownBlockId"),parsers.OptionalWord("replacementBlockCode"))
                    .HandleWith(handleCleanRegister)
                .EndSubCommand()
                .BeginSubCommand("chunk")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithDescription("enqueue chunk at player position for cleaning")
                    .RequiresPlayer()
                    .WithArgs(parsers.OptionalInt("radius", 0))
                    .HandleWith(handleCleanChunkAtPlayer)
                .EndSubCommand()
                /* Not supported yet
                .BeginSubCommand("saveworld")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithDescription("Turn saving cleaner queues to world save on/off")
                    .WithArgs(parsers.Bool("saveToWorld"))
                    .HandleWith(handleSetSaveToWorld)
                .EndSubCommand()
                */
            .EndSubCommand();
        }

        private TextCommandResult handleSetSaveToWorld(TextCommandCallingArgs args)
        {
            saveToWorld = (bool)args[0];
            return TextCommandResult.Success("Set saveToWorld to " + saveToWorld);
        }

        private TextCommandResult handleCleanRegister(TextCommandCallingArgs args)
        {
            int unknownBlockId = (int)args[0];
            if (args.Parsers[1].IsMissing)
            { 
                if (blockIdMap.ContainsKey(unknownBlockId))
                {
                    blockIdMap.Remove(unknownBlockId);
                    return TextCommandResult.Success("Unregistered unknown block id " + unknownBlockId);
                }
                return TextCommandResult.Error("Unknown block id " + unknownBlockId + " is not currently registered");
            }
            string replacementBlockCode = (string)args[1];
            int newBlockId = 0;
            var block = sapi.World.GetBlock(new AssetLocation(replacementBlockCode));
            if (block is null)
            {
                return TextCommandResult.Error("Replacement block code " + replacementBlockCode + " not found");
            }
            newBlockId = block.BlockId;
            blockIdMap[unknownBlockId] = newBlockId;
            return TextCommandResult.Success("Registered unknown block id " + unknownBlockId + " to be remapped to " + replacementBlockCode);
        }

        private TextCommandResult handleCleanChunkAtPlayer(TextCommandCallingArgs args)
        {
            var player = args.Caller.Player;
            var pos = player.Entity.Pos.AsBlockPos;
            int radius = (int)args[0];
            int chunksize = GlobalConstants.ChunkSize;
            int cx = pos.X / chunksize;
            int cy = pos.Y / chunksize;
            int cz = pos.Z / chunksize;
            int ymax = sapi.WorldManager.MapSizeY / sapi.WorldManager.ChunkSize;

            int count = 0;

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        if (dx + cx < 0 || dy + cy < 0 || dz + cz < 0 || dy + cy >= ymax) continue;
                        count++;
                        BlockPos chunkPos = new BlockPos((cx + dx) * chunksize, (cy + dy) * chunksize, (cz + dz) * chunksize);
                        checkCleanQueue.Enqueue(chunkPos);
                    }
                }
            }
            return TextCommandResult.Success("Enqueued " + count + " chunks at " + pos + " for cleaning");
        }

        private void processPerformCleanQueue(float dt)
        {
            if (performCleanQueue.Count == 0) return;
            BlockPos pos;
            if (performCleanQueue.TryDequeue(out pos))
            {
                doClean(pos);
            }
        }

        private void doClean(BlockPos pos)
        {
            var block = sapi.World.BlockAccessor.GetBlock(pos);
            if (block?.Code is null)
            {
                if (blockIdMap.TryGetValue(block.BlockId, out int newBlockId))
                {
                    sapi.World.BlockAccessor.SetBlock(newBlockId, pos);
                    sapi.World.Logger.Notification("Cleaned unknown block at {0} to {1}", pos, sapi.World.Blocks[newBlockId]?.Code?.ToString());
                    blocksRemapped++;
                    return;
                }                
                sapi.World.Logger.Notification("Unknown block at {0} with id {1} which is not currently mapped for cleaning", pos, block.BlockId);
            }
            else
            {
                sapi.World.Logger.Notification("Block at {0} with id {1} queued for cleaning but is already known as {2}.", pos, block?.BlockId,block?.Code);
            }
        }

        private void onSaveGameLoaded()
        {
            checkCleanQueue = deserializeQueue("aosDebugCeckCleanQueue");
            performCleanQueue = deserializeQueue("aosDebugPerformCleanQueue");

            checkCleanThread = new CheckCleanThread(sapi);
            checkCleanThread.Start(checkCleanQueue, performCleanQueue);
        }
        private void onGameGettingSaved()
        {
            if (saveToWorld)
            {
                using FastMemoryStream ms = new();
                sapi.WorldManager.SaveGame.StoreData("aosDebugCheckCleanQueue", SerializerUtil.Serialize(checkCleanQueue, ms));
                sapi.WorldManager.SaveGame.StoreData("aosDebugPerformCleanQueue", SerializerUtil.Serialize(performCleanQueue, ms));
            }
        }

        private ConcurrentQueue<BlockPos> deserializeQueue(string name)
        {
            try
            {
                byte[] data = sapi.WorldManager.SaveGame.GetData(name);
                if (data != null)
                {
                    return SerializerUtil.Deserialize<ConcurrentQueue<BlockPos>>(data);
                }
            }
            catch (Exception e)
            {
                sapi.World.Logger.Error("Failed loading aosDebug.{0}. Resetting. Exception:", name);
                sapi.World.Logger.Error(e);
            }
            return new ConcurrentQueue<BlockPos>();
        }
        
        class CheckCleanThread : IAsyncServerSystem
        {
            const int chunksize = GlobalConstants.ChunkSize;

            public static int checkCleanInterval = 10;
            private ConcurrentQueue<BlockPos> checkCleanQueue;
            private ConcurrentQueue<BlockPos> performCleanQueue;
            private ICoreServerAPI sapi;
            public CheckCleanThread(ICoreServerAPI sapi)
            {
                this.sapi = sapi;
            }
            public void Start(ConcurrentQueue<BlockPos> checkCleanQueue, ConcurrentQueue<BlockPos> performCleanQueue)
            {
                this.checkCleanQueue = checkCleanQueue;
                this.performCleanQueue = performCleanQueue;
                sapi.Server.AddServerThread("checkClean", this);
            }
            public int OffThreadInterval()
            {
                return checkCleanInterval;
            }
            public void OnSeparateThreadTick()
            {
                List<int> reusableList = new List<int>();
                if (checkCleanQueue.TryDequeue(out BlockPos pos))
                {
                    var chunk = sapi.World.BlockAccessor.GetChunkAtBlockPos(pos);
                    chunk?.Unpack_ReadOnly();
                    var blocks = chunk?.MaybeBlocks;
                    if (blocks is null)
                    {
                        // Chunk not loaded, re-enqueue for later
                        checkCleanQueue.Enqueue(pos);
                        return;
                    }                        
                    blocks.FuzzyListBlockIds(reusableList);
                    var needToScan = false;
                    foreach (int blockId in reusableList)
                    {
                        var block = sapi.World.Blocks[blockId];
                        if (block?.Code is null)
                        {
                            needToScan = true;
                            break;
                        }
                    }
                    if (needToScan)
                    {
                        for (int index3d = 0; index3d < blocks.Length; index3d++)
                        {
                            int blockId = blocks[index3d];
                            var block = sapi.World.Blocks[blockId];
                            if (block?.Code is null)
                            {
                                // Unknown block, enqueue for cleaning
                                int x = index3d % chunksize;
                                int z = (index3d / chunksize) % chunksize;
                                int y = index3d / (chunksize * chunksize);
                                performCleanQueue.Enqueue(pos.AddCopy(x,y,z));
                            }
                        }
                    }
                }
            }
            public void ThreadDispose()
            {
                checkCleanQueue.Clear();
                performCleanQueue.Clear();
            }

        }
    }
}
