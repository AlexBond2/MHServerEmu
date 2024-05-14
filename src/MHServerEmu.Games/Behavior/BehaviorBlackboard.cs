﻿
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Generators.Population;
using MHServerEmu.Games.Properties;

namespace MHServerEmu.Games.Behavior
{
    public class BehaviorBlackboard
    {
        private Agent _owner; 
        public PropertyCollection PropertyCollection { get; private set; }
        public Vector3 SpawnPoint { get; private set; }
        public Vector3 SpawnOffset { get; private set; }

        public BehaviorBlackboard(Agent owner)
        {
            _owner = owner;
            PropertyCollection = new ();
            SpawnPoint = Vector3.Zero;
            SpawnOffset = Vector3.Zero;
        }

        public void Initialize(BehaviorProfilePrototype profile, SpawnSpec spec, PropertyCollection collection)
        {
            SpawnPoint = _owner.RegionLocation.Position;
            if (spec != null)
                SpawnOffset = spec.Transform.Translation;
       
            PropertyCollection[PropertyEnum.AIAggroDropRange] = profile.AggroDropDistance;
            PropertyCollection[PropertyEnum.AIAggroDropByLOSChance] = profile.AggroDropChanceLOS;
            PropertyCollection[PropertyEnum.AIAggroRangeHostile] = profile.AggroRangeHostile;
            PropertyCollection[PropertyEnum.AIAggroRangeAlly] = profile.AggroRangeAlly;

            if (profile.AlwaysAggroed)
            {
                PropertyCollection[PropertyEnum.AIAlwaysAggroed] = true;
                PropertyCollection[PropertyEnum.AIAggroState] = true;
                PropertyCollection[PropertyEnum.AIAggroTime] = (long)_owner.Game.GetCurrentTime().TotalMilliseconds;
            }

            if (profile.Properties != null)
                PropertyCollection.FlattenCopyFrom(profile.Properties, false);
            if (collection != null)
                PropertyCollection.FlattenCopyFrom(collection, false);
        }
    }
}