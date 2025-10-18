using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;


namespace AoSDebug
{
    [HarmonyPatch]
    public class AoSDebugModSystem : ModSystem
    {
        public static ICoreServerAPI sapi;
        public Harmony harmony;
        public static int message_delta = 30;
        public static int last_message_time_OnTransitionNow = 0;
        public static int message_count_OnTransitionNow = 0;

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
        class PatchOnTransitionNow
        {
            public static bool Prefix(CollectibleObject __instance, ref ItemStack __result, ItemSlot slot, TransitionableProperties props)
            {                
                if (props?.TransitionedStack?.ResolvedItemstack is null) 
                {
                    message_count_OnTransitionNow++;
                    int servertime = sapi.Server.ServerUptimeSeconds;
                    if (last_message_time_OnTransitionNow == 0 || last_message_time_OnTransitionNow+message_delta <= servertime)
                    {
                        sapi.Logger.Event("null ResolvedItemstack in OnTransitionNow (unable to resolve {0} {1}?) for transition {2}, returning dummy stack", props?.TransitionedStack?.Type, props?.TransitionedStack?.Code, props?.Type);
                        if (message_count_OnTransitionNow > 1)
                        {
                            sapi.Logger.Event("OnTransitionNow failed {0} times since prior logged instance", message_count_OnTransitionNow);
                        }
                        last_message_time_OnTransitionNow = servertime;
                        message_count_OnTransitionNow = 0;
                    }                    
                    __result = new ItemStack();
                    return false;
                }
                return true;
            }
        }
    }
}


/******
 * The issues
 * 
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



//TODO
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


//TODO
18.10.2025 08:20:08 [Server Error] At position 127648, 152, 128191 for block fruittree-branch a BlockEntityFruitTreeBranch threw an error when ticked:
18.10.2025 08:20:08 [Server Error] Exception: Object reference not set to an instance of an object.
   at Vintagestory.API.Common.Block.GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, Single dropQuantityMultiplier) in VintagestoryApi\Common\Collectible\Block\Block.cs:line 1228
   at Vintagestory.GameContent.BlockFruitTreeBranch.GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, Single dropQuantityMultiplier) in VSSurvivalMod\Systems\FruitTree\BlockFruitTreeBranch.cs:line 198
   at Vintagestory.API.Common.Block.OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, Single dropQuantityMultiplier) in VintagestoryApi\Common\Collectible\Block\Block.cs:line 1050
   at Vintagestory.Common.BlockAccessorBase.BreakBlock(BlockPos pos, IPlayer byPlayer, Single dropQuantityMultiplier) in VintagestoryLib\Common\API\BlockAccessorBase.cs:line 553
   at Vintagestory.GameContent.FruitTreeGrowingBranchBH.OnTick(Single dt) in VSSurvivalMod\Systems\FruitTree\FruitTreeGrowingBranchBH.cs:line 123
   at Vintagestory.Common.GameTickListener.OnTriggered(Int64 ellapsedMilliseconds) in VintagestoryLib\Common\Model\GameTickListener.cs:line 31


*/
