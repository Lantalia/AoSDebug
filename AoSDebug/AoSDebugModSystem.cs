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
            UpdatePerms();
            base.StartServerSide(api);
        }

        // Lock down server chat commands that normal players should not have
        private void UpdatePerms()
        {
            var itemizer = sapi.ChatCommands.Get("itemizer");
            if (itemizer is not null)
            {
                itemizer.RequiresPrivilege(Privilege.setwelcome);
            }
            var role = sapi.ChatCommands.Get("role");
            var player = sapi.ChatCommands.Get("player");

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
