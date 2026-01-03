using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using System.Collections.Concurrent;
using System;

namespace AoSDebug
{
    [HarmonyPatch]
    public class AoSDebugModSystem : ModSystem
    {
        public static ICoreServerAPI sapi;
        public Harmony harmony;
        public static int message_delta = 30;
        private static ConcurrentDictionary<int, (int count, int last_message_time)> throttledMessages = new ConcurrentDictionary<int, (int count, int last_message_time)>();

        private static void RunIfNotThrottled(Action<int> message, [CallerLineNumber] int lineNumber = 0)
        {
            var entry = throttledMessages.GetOrAdd(lineNumber, (0, -message_delta));

            int serverTime = sapi.Server.ServerUptimeSeconds;

            if (entry.last_message_time + message_delta > serverTime)
            {
                throttledMessages.TryUpdate(lineNumber, (entry.count+1,entry.last_message_time),entry);
                return;
            }
            message(entry.count);
            throttledMessages[lineNumber] = (0, serverTime); // More important to update last message time than to be perfectly accurate on count
        }
        public override void Start(ICoreAPI api)
        {

        }
        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            AoSDebugModSystem.sapi = api;
            if (!Harmony.HasAnyPatches(Mod.Info.ModID))
            {
                harmony = new Harmony(Mod.Info.ModID);
                harmony.PatchAll(typeof(AoSDebugModSystem).Assembly);
            }
            RegisterCommands();
            UpdatePerms();
            base.StartServerSide(api);
        }

        // Lock down server chat commands that normal players should not have
        private void UpdatePerms()
        {
            var itemizer = sapi.ChatCommands.Get("itemizer");
            if (itemizer is not null)
            {
                itemizer.RequiresPrivilege("itemizer");
            }

        }

        // Placeholder untill we need more live feedback
        private void RegisterCommands()
        {
            var parsers = sapi.ChatCommands.Parsers;
            sapi.ChatCommands.Create("aosdebug")
                .WithDescription("aos debug controls")
                .RequiresPrivilege(Privilege.controlserver)
                .HandleWith(_ => { return TextCommandResult.Success("Status"); });

        }

        // Defuse ?chisel? bombs
        [HarmonyPatch(typeof(BlockShapeFromAttributes), "getCollisionBoxes")]
        class PatchBlockShapeFromAttributesGetCollisionBoxes
        {
            private static readonly Cuboidf[] fullCube = new Cuboidf[] { new(0, 0, 0, 1, 1, 1) };
            /*
            // Retained for debugging purposes
            public static bool Prefix(BlockShapeFromAttributes __instance, ref Cuboidf[] __result, IBlockAccessor blockAccessor, BlockPos pos, BEBehaviorShapeFromAttributes bect, IShapeTypeProps cprops)
            {
                return true;
            }

            public static void Postfix(Cuboidf[] __result)
            {
                if (__result is not null)
                {
                    RunIfNotThrottled(
                        count =>
                        {
                            sapi.Logger.Event("BlockShapeFromAttributes.getCollisionBoxes returning {0} boxes; {1} ; elided {2} other calls", __result?.Length, string.Join("; ", Array.ConvertAll(__result, p => p.ToString())), count);
                        });
                }
            } */
            public static Exception Finalizer(Exception __exception, ref Cuboidf[] __result)
            {
                if (__exception is not null)
                {
                    RunIfNotThrottled(count =>
                    {
                        sapi.Logger.Event("BlockShapeFromAttributes.getCollisionBoxes threw exception {0}, returning full cube; elided {1} other calls", __exception?.ToString(), count);
                    });
                    __result = fullCube;
                }
                return null;
            }
        }

        // Defuse another transition bomb. As we don't actually know what is blowing up here, check a few things

        [HarmonyPatch(typeof(CollectibleObject), "UpdateAndGetTransitionStatesNative")]
        class PatchCollectibleObjectUpdateAndGetTransitionStatesNative
        {
            /* // Can't tell what is blowing up yet so don't include this untill we have a valid test
            public static bool Prefix(CollectibleObject __instance, ref TransitionState[] __result, IWorldAccessor world, ItemSlot inslot )
            {
                
                if (inslot is ItemSlotCreative or inslot?.Itemstack is null)
                {
                    return true;
                }
                
                if (inslot?.Itemstack?.Collectible?.TransitionableProps is null) {
                    RunIfNotThrottled(
                        count => {
                            sapi.Logger.Event("null ??? in UpdateAndGetTransitionStateNative (unable to resolve {0} {1} {2}?) null; elided {3} other calls",
                                __instance?.Code,
                                inslot?.Itemstack?.Collectible?.Code,
                                inslot?.Itemstack?.Id,
                                count);
                        });
                    __result = null;
                    return false;
                }
                return true;
            }
            */

            // Treat it as if it doesn't have transitions, try and work out what is blowing up
            public static Exception Finalizer(CollectibleObject __instance, Exception __exception, ref TransitionState[] __result)
            {
                if (__exception is not null)
                {
                    RunIfNotThrottled(count =>
                    {
                        sapi.Logger.Event("CollectibleObject.UpdateAndGetTransitionStatesNative ({1}) threw exception  {0} , returning null; elided {1} other calls", __exception?.ToString(), __instance?.Code, count);
                    });
                    __result = null;
                }
                return null;
            }
        }
        // Defuse rot bombs
        [HarmonyPatch(typeof(CollectibleObject), "OnTransitionNow")]
        class PatchCollectibleObjectOnTransitionNow
        {
            public static bool Prefix(CollectibleObject __instance, ref ItemStack __result, ItemSlot slot, TransitionableProperties props)
            {
                if (props?.TransitionedStack?.ResolvedItemstack is null)
                {
                    RunIfNotThrottled(
                        count => { 
                            sapi.Logger.Event("null ResolvedItemstack in OnTransitionNow (unable to resolve {0} {1}?) for transition {2}, returning dummy stack; elided {3} other calls", 
                                props?.TransitionedStack?.Type, 
                                props?.TransitionedStack?.Code, 
                                props?.Type, 
                                count); 
                        });
                    __result = new ItemStack();
                    return false;
                }
                return true;
            }
        }

        // Defuse Elk bombs
        [HarmonyPatch(typeof(CollectibleBehaviorHeldBag), "OnEntityDespawn")]
        class PatchCollectibleBehaviorHeldBagOnEntityDespawn
        {
            public static bool Prefix(CollectibleBehaviorHeldBag __instance, ItemSlot itemslot, int slotIndex, Entity onEntity, ref EntityDespawnData despawn)
            {
                if (despawn is null)
                {
                    RunIfNotThrottled(
                        count =>
                        {
                            sapi.Logger.Event("CollectibleBehaviorHeldBag.OnEntityDespawn called with null despawn data for entity {0} id {1} at {2}, saying it Died instead; elided {3} other calls",
                                onEntity?.Code,
                                onEntity?.EntityId,
                                onEntity?.ServerPos?.OnlyPosToString(),
                                count);
                        });
                    despawn = new EntityDespawnData()
                    {
                        Reason = EnumDespawnReason.Death,
                    };
                }
                return true;
            }
        }

        // Log tempts to despawn elks
        [HarmonyPatch(typeof(Entity), "OnEntityDespawn")]
        class PatchEntityOnEntityDespawn
        {
            public static bool Prefix(Entity __instance)
            {
                if (__instance?.Code?.Path?.Contains("tameddeer") ?? false)
                {
                    RunIfNotThrottled(count =>
                    {
                        sapi.Logger.Event("Entity.OnEntityDespawn called for entity {0} id {1} at {2}; elided {3} other calls", 
                            __instance?.Code, 
                            __instance?.EntityId, 
                            __instance?.ServerPos?.OnlyPosToString(), count);
                    });
                }
                return true;
            }
        }
    }
}


/*
 * 
 * 8.12.2025 19:15:39 [Error] Exception: Object reference not set to an instance of an object.
   at FromGoldenCombs.BlockEntities.BELangstrothStack.manageCropBoost(BlockPos cropPos, Double distance, EnumHandling& handling)
   at FromGoldenCombs.FromGoldenCombs.HandlePollinationEvents(String eventName, EnumHandling& handled, IAttribute data)
   at Vintagestory.Server.ServerEventAPI.PushEvent(String eventName, IAttribute data) in VintagestoryLib\Server\API\ServerEventAPI.cs:line 307
   at FromGoldenCombs.BlockBehaviors.PushEventOnCropBreakBehavior.OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, EnumHandling& handling)
   at Vintagestory.API.Common.Block.OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, Single dropQuantityMultiplier) in VintagestoryApi\Common\Collectible\Block\Block.cs:line 1041
   at Vintagestory.GameContent.BlockCrop.OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, Single dropQuantityMultiplier) in VSSurvivalMod\Block\BlockCrop.cs:line 201
   at Vintagestory.GameContent.ItemScythe.breakMultiBlock(BlockPos pos, IPlayer plr) in VSSurvivalMod\Item\ItemScythe.cs:line 197
   at Vintagestory.GameContent.ItemShears.OnBlockBrokenWith(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, BlockSelection blockSel, Single dropQuantityMultiplier) in VSSurvivalMod\Item\ItemShears.cs:line 68
   at Vintagestory.GameContent.ItemScythe.OnHeldAttackStep(Single secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSelection, EntitySelection entitySel) in VSSurvivalMod\Item\ItemScythe.cs:line 130
   at Vintagestory.Server.ServerSystemInventory.callOnUsing(ItemSlot slot, ServerPlayer player, BlockSelection blockSel, EntitySelection entitySel, Single& secondsPassed, Boolean callStop) in VintagestoryLib\Server\Systems\Inventory.cs:line 506
   at Vintagestory.Server.ServerSystemInventory.OnUsingTick(Single dt) in VintagestoryLib\Server\Systems\Inventory.cs:line 112
   at Vintagestory.Common.GameTickListener.OnTriggered(Int64 ellapsedMilliseconds) in VintagestoryLib\Common\Model\GameTickListener.cs:line 25
   at Vintagestory.Common.EventManager.TriggerGameTick_Patch0(EventManager this, Int64 ellapsedMilliseconds, IWorldAccessor world)
   at Vintagestory.Server.ServerMain.Process() in VintagestoryLib\Server\ServerMain.cs:line 859


8.12.2025 19:02:13 [Error] At position 124306, 172, 123802 for block fromgoldencombs:langstrothstack-two-south a BELangstrothStack threw an error when ticked:
8.12.2025 19:02:13 [Error] Exception: Object reference not set to an instance of an object.
   at FromGoldenCombs.BlockEntities.BELangstrothStack.<>c__DisplayClass77_0.<OnScanForFlowers>b__0(Block block, Int32 posx, Int32 posy, Int32 posz)
   at Vintagestory.Common.BlockAccessorBase.WalkBlocks(BlockPos minPos, BlockPos maxPos, Action`4 onBlock, Boolean centerOrder) in VintagestoryLib\Common\API\BlockAccessorBase.cs:line 281
   at FromGoldenCombs.BlockEntities.BELangstrothStack.OnScanForFlowers(Single dt)
   at Vintagestory.Common.GameTickListener.OnTriggered(Int64 ellapsedMilliseconds) in VintagestoryLib\Common\Model\GameTickListener.cs:line 25

*/