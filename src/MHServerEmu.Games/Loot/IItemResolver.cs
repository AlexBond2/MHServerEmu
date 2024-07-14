﻿using MHServerEmu.Core.System.Random;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;

namespace MHServerEmu.Games.Loot
{
    /// <summary>
    /// An interface for a class that does the rolling ("resolves") on loot tables.
    /// </summary>
    public interface IItemResolver
    {
        public GRandom Random { get; }
        public LootContext LootContext { get; }
        public Player Player { get; }

        public LootRollResult PushItem(DropFilterArguments dropFilterArgs, RestrictionTestFlags restrictionTestFlags,
            int stackCount, IEnumerable<LootMutationPrototype> mutations);
        public LootRollResult PushCurrency(WorldEntityPrototype worldEntityProto, DropFilterArguments dropFilterArgs,
            RestrictionTestFlags restrictionTestFlags, LootDropChanceModifiers dropChanceModifiers, int stackCount);

        public void PushLootNodeCallback();
        public void PushCraftingCallback();

        public int ResolveLevel(int level, bool useLevelVerbatim);
        public AvatarPrototype ResolveAvatarPrototype(AvatarPrototype usableAvatarProto, bool hasUsableOverride, float usableOverrideValue);
        public AgentPrototype ResolveTeamUpPrototype(AgentPrototype usableTeamUpProto, float usableOverrideValue);
        public PrototypeId ResolveRarity(HashSet<PrototypeId> rarities, int level, ItemPrototype itemProto);

        public void Fail();
        public bool Resolve(LootRollSettings settings);
    }
}
