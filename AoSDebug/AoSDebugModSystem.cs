using System.Runtime.CompilerServices;
using HarmonyLib;
using ProperVersion;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using System.Collections.Generic;
using System;

namespace AoSDebug
{
    [HarmonyPatch]
    public class AoSDebugModSystem : ModSystem
    {
        public static ICoreServerAPI sapi;
        public Harmony harmony;
        public static int message_delta = 30;
        private static Dictionary<int, (int count, int last_message_time)> throttledMessages = new Dictionary<int, (int count, int last_message_time)>();
        private static void RunIfNotThrottled(Action<int> message, [CallerLineNumber] int lineNumber = 0)
        {
            if (!throttledMessages.TryGetValue(lineNumber, out var entry))
            {
                entry = (0, -message_delta);
            }

            int serverTime = sapi.Server.ServerUptimeSeconds;

            if (entry.last_message_time + message_delta > serverTime)
            {
                entry.count++;
                throttledMessages[lineNumber] = entry;
                return;
            }
            message(entry.count);
            entry.count = 0;
            entry.last_message_time = serverTime;
            throttledMessages[lineNumber] = entry;
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
            base.StartServerSide(api);
        }

        private void RegisterCommands()
        {
            var parsers = sapi.ChatCommands.Parsers;
            sapi.ChatCommands.Create("aosdebug")
                .WithDescription("aos debug controls")
                .RequiresPrivilege(Privilege.controlserver)
                .HandleWith(_ => { return TextCommandResult.Success("Status"); });
        }


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


/******
 * The issues
 * 
 * 
 * 
// Priority: High (done)
22.10.2025 19:23:15 [Error] Exception: Object reference not set to an instance of an object.
   at Vintagestory.GameContent.CollectibleBehaviorHeldBag.OnEntityDespawn(ItemSlot itemslot, Int32 slotIndex, Entity onEntity, EntityDespawnData despawn) in VSEssentials\CollectibleBehavior\CollectibleBehaviorHeldBag.cs:line 228
   at Vintagestory.GameContent.EntityBehaviorAttachable.OnEntityDespawn(EntityDespawnData despawn) in VSSurvivalMod\Entity\Behavior\BehaviorAttachable.cs:line 404
   at Vintagestory.API.Common.Entities.Entity.OnEntityDespawn_Patch1(Entity this, EntityDespawnData despawn)
   at Vintagestory.API.Common.EntityAgent.OnEntityDespawn(EntityDespawnData despawn) in VintagestoryApi\Common\Entity\EntityAgent.cs:line 296
   at Vintagestory.Server.ServerMain.DespawnEntity(Entity entity, EntityDespawnData despawnData) in VintagestoryLib\Server\ServerMain.cs:line 2515
   at Vintagestory.Server.ServerSystemEntitySimulation.TickEntities(Single dt) in VintagestoryLib\Server\Systems\World\EntitySimulation.cs:line 258
   at Vintagestory.Server.ServerSystemEntitySimulation.OnServerTick(Single dt) in VintagestoryLib\Server\Systems\World\EntitySimulation.cs:line 159
   at Vintagestory.Server.ServerMain.Process() in VintagestoryLib\Server\ServerMain.cs:line 932


//Researching
17.10.2025 22:29:19 [Server Error] Exception: Object reference not set to an instance of an object.
at Vintagestory.Common.InventoryPlayerCreative.get_Count() in VintagestoryLib\Common\GameContent\Inventory\InventoryPlayerCreative.cs:line 222
at Vintagestory.API.Common.InventoryBase.GetEnumerator()+MoveNext()
at Vintagestory.API.Common.InventoryBase.Clear() in VintagestoryApi\Common\Inventory\InventoryBase.cs:line 789
at Vintagestory.Server.CmdPlayer.WipePlayerInventory(PlayerUidName targetPlayer, TextCommandCallingArgs args) in VintagestoryLib\Server\Systems\Player\CmdPlayer.cs:line 436
at Vintagestory.Server.CmdPlayer.Each(TextCommandCallingArgs args, PlayerEachDelegate onPlayer) in VintagestoryLib\Server\Systems\Player\CmdPlayer.cs:line 1115
at Vintagestory.Server.CmdPlayer.<>c__DisplayClass5_0.<.ctor>b__15(TextCommandCallingArgs args) in VintagestoryLib\Server\Systems\Player\CmdPlayer.cs:line 164
at Vintagestory.Common.ChatCommandImpl.CallHandler(TextCommandCallingArgs callargs, Action`1 onCommandComplete, Dictionary`2 asyncParseResults) in VintagestoryLib\Common\API\Command\ChatCommandImpl.cs:line 311
at Vintagestory.Common.ChatCommandImpl.Execute(TextCommandCallingArgs callargs, Action`1 onCommandComplete) in VintagestoryLib\Common\API\Command\ChatCommandImpl.cs:line 236
at Vintagestory.Common.ChatCommandImpl.CallHandler(TextCommandCallingArgs callargs, Action`1 onCommandComplete, Dictionary`2 asyncParseResults) in VintagestoryLib\Common\API\Command\ChatCommandImpl.cs:line 263
at Vintagestory.Common.ChatCommandImpl.Execute(TextCommandCallingArgs callargs, Action`1 onCommandComplete) in VintagestoryLib\Common\API\Command\ChatCommandImpl.cs:line 236
at Vintagestory.Common.ChatCommandApi.Execute(String commandName, TextCommandCallingArgs args, Action`1 onCommandComplete) in VintagestoryLib\Common\API\Command\ChatCommandApi.cs:line 99


18.10.2025 08:20:08 [Server Error] At position 127648, 152, 128191 for block fruittree-branch a BlockEntityFruitTreeBranch threw an error when ticked:
18.10.2025 08:20:08 [Server Error] Exception: Object reference not set to an instance of an object.
   at Vintagestory.API.Common.Block.GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, Single dropQuantityMultiplier) in VintagestoryApi\Common\Collectible\Block\Block.cs:line 1228
   at Vintagestory.GameContent.BlockFruitTreeBranch.GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, Single dropQuantityMultiplier) in VSSurvivalMod\Systems\FruitTree\BlockFruitTreeBranch.cs:line 198
   at Vintagestory.API.Common.Block.OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, Single dropQuantityMultiplier) in VintagestoryApi\Common\Collectible\Block\Block.cs:line 1050
   at Vintagestory.Common.BlockAccessorBase.BreakBlock(BlockPos pos, IPlayer byPlayer, Single dropQuantityMultiplier) in VintagestoryLib\Common\API\BlockAccessorBase.cs:line 553
   at Vintagestory.GameContent.FruitTreeGrowingBranchBH.OnTick(Single dt) in VSSurvivalMod\Systems\FruitTree\FruitTreeGrowingBranchBH.cs:line 123
   at Vintagestory.Common.GameTickListener.OnTriggered(Int64 ellapsedMilliseconds) in VintagestoryLib\Common\Model\GameTickListener.cs:line 31


// Done
 17.10.2025 22:24:07 [Error] Exception: Object reference not set to an instance of an object.
   at Vintagestory.API.Common.CollectibleObject.OnTransitionNow(ItemSlot slot, TransitionableProperties props) in VintagestoryApi\Common\Collectible\Collectible.cs:line 2628
   at Vintagestory.API.Common.CollectibleObject.UpdateAndGetTransitionStatesNative(IWorldAccessor world, ItemSlot inslot) in VintagestoryApi\Common\Collectible\Collectible.cs:line 2462
   at Vintagestory.GameContent.BlockContainer.UpdateAndGetTransitionStates(IWorldAccessor world, ItemSlot inslot) in VSSurvivalMod\Block\BlockContainer.cs:line 202
   at Vintagestory.Server.ServerSystemInventory.UpdateTransitionStates(Single dt) in VintagestoryLib\Server\Systems\Inventory.cs:line 144
   at Vintagestory.Common.GameTickListener.OnTriggered(Int64 ellapsedMilliseconds) in VintagestoryLib\Common\Model\GameTickListener.cs:line 31
   at Vintagestory.Common.EventManager.TriggerGameTick(Int64 ellapsedMilliseconds, IWorldAccessor world) in VintagestoryLib\Common\EventManager.cs:line 174



17.10.2025 22:08:28 [Server Error] At position 122879, 139, 136186 for block barrel a BlockEntityBarrel threw an error when ticked:
17.10.2025 22:08:28 [Server Error] Exception: Object reference not set to an instance of an object.
at Vintagestory.API.Common.CollectibleObject.OnTransitionNow(ItemSlot slot, TransitionableProperties props) in VintagestoryApi\Common\Collectible\Collectible.cs:line 2628
at Vintagestory.API.Common.CollectibleObject.UpdateAndGetTransitionStatesNative(IWorldAccessor world, ItemSlot inslot) in VintagestoryApi\Common\Collectible\Collectible.cs:line 2462
at Vintagestory.GameContent.InWorldContainer.OnTick(Single dt) in VSEssentials\Inventory\InWorldContainer.cs:line 129
at Vintagestory.Common.GameTickListener.OnTriggered(Int64 ellapsedMilliseconds) in VintagestoryLib\Common\Model\GameTickListener.cs:line 31



17.10.2025 22:17:23 [Server Error] Exception: Object reference not set to an instance of an object.
at Vintagestory.API.Common.CollectibleObject.OnTransitionNow(ItemSlot slot, TransitionableProperties props) in VintagestoryApi\Common\Collectible\Collectible.cs:line 2628
at Vintagestory.API.Common.CollectibleObject.UpdateAndGetTransitionStatesNative(IWorldAccessor world, ItemSlot inslot) in VintagestoryApi\Common\Collectible\Collectible.cs:line 2462
at Vintagestory.GameContent.BlockContainer.UpdateAndGetTransitionStates(IWorldAccessor world, ItemSlot inslot) in VSSurvivalMod\Block\BlockContainer.cs:line 190
at Vintagestory.API.Common.ItemSlot.OnItemSlotModified(ItemStack sinkStack) in VintagestoryApi\Common\Inventory\ItemSlot.cs:line 425
at Vintagestory.API.Common.ItemSlot.TryPutInto(ItemSlot sinkSlot, ItemStackMoveOperation& op) in VintagestoryApi\Common\Inventory\ItemSlot.cs:line 188
at Vintagestory.Common.PlayerInventoryManager.TryTransferAway(ItemSlot sourceSlot, ItemStackMoveOperation& op, Boolean onlyPlayerInventory, StringBuilder shiftClickDebugText, Boolean slotNotifyEffect) in VintagestoryLib\Common\GameContent\Inventory\PlayerInventoryManager.cs:line 232
at Vintagestory.Common.PlayerInventoryManager.TryGiveItemstack(ItemStack itemstack, Boolean slotNotifyEffect) in VintagestoryLib\Common\GameContent\Inventory\PlayerInventoryManager.cs:line 213
at PlayerCorpse.Entities.EntityPlayerCorpse.Collect(IPlayer byPlayer)
at PlayerCorpse.Entities.EntityPlayerCorpse.OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode)
at Vintagestory.Server.ServerSystemEntitySimulation.HandleEntityInteraction(Packet_Client packet, ConnectedClient client)
at Vintagestory.Server.ServerMain.HandleClientPacket_mainthread(ReceivedClientPacket cpk) in VintagestoryLib\Server\ServerMainNetworking.cs:line 238
at Vintagestory.Server.ServerMain.ProcessMain() in VintagestoryLib\Server\ServerMain.cs:line 977

*/
