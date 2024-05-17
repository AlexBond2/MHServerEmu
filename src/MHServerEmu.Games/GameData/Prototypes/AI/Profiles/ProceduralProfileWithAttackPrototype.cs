﻿using MHServerEmu.Core.Collections;
using MHServerEmu.Core.System.Random;
using MHServerEmu.Games.Behavior.ProceduralAI;
using MHServerEmu.Games.Behavior.StaticAI;
using MHServerEmu.Games.Behavior;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Properties;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Games.Powers;
using MHServerEmu.Core.Extensions;

namespace MHServerEmu.Games.GameData.Prototypes
{

    public class ProceduralProfileWithAttackPrototype : ProceduralProfileWithTargetPrototype
    {
        public int AttackRateMaxMS { get; protected set; }
        public int AttackRateMinMS { get; protected set; }
        public ProceduralUsePowerContextPrototype[] GenericProceduralPowers { get; protected set; }
        public ProceduralUseAffixPowerContextPrototype AffixSettings { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            Game game = agent.Game;
            if (game == null) return;
            AIController ownerController = agent.AIController;
            if (ownerController == null) return;

            long nextAttackThinkTime = (long)game.GetCurrentTime().TotalMilliseconds + game.Random.Next(AttackRateMinMS, AttackRateMaxMS);
            ownerController.Blackboard.PropertyCollection[PropertyEnum.AIProceduralNextAttackTime] = nextAttackThinkTime;
            InitPowers(agent, GenericProceduralPowers);
        }

        public virtual void PopulatePowerPicker(AIController ownerController, Picker<ProceduralUsePowerContextPrototype> powerPicker)
        {
            ownerController.AddPowersToPicker(powerPicker, GenericProceduralPowers);
        }

        protected static bool AddPowerToPickerIfStartedPowerIsContextPower(AIController ownerController,
            ProceduralUsePowerContextPrototype powerToAdd, PrototypeId startedPowerRef, Picker<ProceduralUsePowerContextPrototype> powerPicker)
        {
            var powerContext = powerToAdd?.PowerContext;
            if (powerContext == null
                || powerContext.Power == PrototypeId.Invalid
                || startedPowerRef != powerContext.Power) return false;

            ownerController.AddPowersToPicker(powerPicker, powerToAdd);
            return true;
        }

        protected StaticBehaviorReturnType HandleProceduralPower(AIController ownerController, ProceduralAI proceduralAI, GRandom random,
            long currentTime, Picker<ProceduralUsePowerContextPrototype> powerPicker, bool affixPower)
        {
            Agent agent = ownerController.Owner;
            if (agent == null)
            {
                ProceduralAI.Logger.Warn($"[{agent}]");
                return StaticBehaviorReturnType.None;
            }

            BehaviorBlackboard blackboard = ownerController.Blackboard;
            StaticBehaviorReturnType contextResult = StaticBehaviorReturnType.None;

            if (proceduralAI.GetState(0) == UsePower.Instance)
            {
                PrototypeId powerStartedRef = ownerController.ActivePowerRef;
                if (powerStartedRef == PrototypeId.Invalid)
                {
                    ProceduralAI.Logger.Warn($"In UsePower state, but no power was recorded as started! agent=[{agent}]");
                    return StaticBehaviorReturnType.Failed;
                }

                ProceduralUsePowerContextPrototype proceduralUsePowerProto = null;
                UsePowerContextPrototype powerContextProtoToRun = null;
                int numPowers = powerPicker.GetNumElements();
                for (int i = 0; i < numPowers; ++i)
                {
                    if (powerPicker.GetElementAt(i, out proceduralUsePowerProto) == false)
                    {
                        ProceduralAI.Logger.Warn($"failed to GetElementAt i=[{i}] agent=[{agent}]");
                        return StaticBehaviorReturnType.Failed;
                    }
                    if (proceduralUsePowerProto == null)
                    {
                        ProceduralAI.Logger.Warn($"proceduralUsePowerProto is NULL! agent=[{agent}]");
                        return StaticBehaviorReturnType.Failed;
                    }
                    UsePowerContextPrototype powerContextProto = proceduralUsePowerProto.PowerContext;
                    if (powerContextProto == null)
                    {
                        ProceduralAI.Logger.Warn($"powerContextProto is NULL! agent=[{agent}]");
                        return StaticBehaviorReturnType.Failed;
                    }
                    if (powerContextProto.Power != PrototypeId.Invalid && powerStartedRef == powerContextProto.Power)
                    {
                        powerContextProtoToRun = powerContextProto;
                        break;
                    }
                }

                if (powerContextProtoToRun == null)
                {
                    PrototypeId syncPowerRef = blackboard.PropertyCollection[PropertyEnum.AISyncAttackTargetPower];
                    if (syncPowerRef != PrototypeId.Invalid)
                    {
                        proceduralUsePowerProto = GameDatabase.GetPrototype<ProceduralUsePowerContextPrototype>(syncPowerRef);
                        if (proceduralUsePowerProto == null)
                        {
                            ProceduralAI.Logger.Warn($"proceduralUsePowerProto is NULL! agent=[{agent}]");
                            return StaticBehaviorReturnType.Failed;
                        }
                        powerContextProtoToRun = proceduralUsePowerProto.PowerContext;
                        if (powerContextProtoToRun == null || powerContextProtoToRun.Power == PrototypeId.Invalid)
                        {
                            ProceduralAI.Logger.Warn($"powerContextProtoToRun or Power is NULL! agent=[{agent}]");
                            return StaticBehaviorReturnType.Failed;
                        }
                        if (powerContextProtoToRun.Power != powerStartedRef)
                        {
                            ProceduralAI.Logger.Warn($"SyncPower doesn't match power running!\n AI: {agent}\n Power Running: {GameDatabase.GetFormattedPrototypeName(powerStartedRef)}");
                            return StaticBehaviorReturnType.Failed;
                        }
                    }
                }

                if (proceduralUsePowerProto == null || powerContextProtoToRun == null)
                {
                    ProceduralAI.Logger.Warn($"proceduralUsePowerProto or powerContextProtoToRun is NULL! powerStartedRef=[{powerStartedRef}] numPowers=[{numPowers}] agent=[{agent}]");
                    return StaticBehaviorReturnType.Failed;
                }

                contextResult = HandleUsePowerContext(ownerController, proceduralAI, random, currentTime, powerContextProtoToRun, proceduralUsePowerProto);
            }
            else if (proceduralAI.GetState(0) == UseAffixPower.Instance)
            {
                contextResult = HandleUseAffixPowerContext(ownerController, proceduralAI, random, currentTime);
            }
            else if (currentTime >= blackboard.PropertyCollection[PropertyEnum.AIProceduralNextAttackTime])
            {
                if (affixPower && agent.Properties.HasProperty(PropertyEnum.EnemyBoost))
                {
                    if (AffixSettings == null)
                    {
                        ProceduralAI.Logger.Warn($"Agent [{agent}] has enemy affix(es), but no AffixSettings data in its procedural profile!");
                        return StaticBehaviorReturnType.Failed;
                    }
                    powerPicker.Add(null, AffixSettings.PickWeight);
                }

                while (powerPicker.Empty() == false)
                {
                    powerPicker.PickRemove(out var randomProceduralPowerProto);
                    if (affixPower && randomProceduralPowerProto == null)
                    {
                        contextResult = HandleUseAffixPowerContext(ownerController, proceduralAI, random, currentTime);
                    }
                    else
                    {
                        UsePowerContextPrototype randomPowerContextProto = randomProceduralPowerProto.PowerContext;
                        if (randomPowerContextProto == null || randomPowerContextProto.Power == PrototypeId.Invalid)
                        {
                            ProceduralAI.Logger.Warn($"Agent [{agent}] has a NULL PowerContext or PowerContext.Power");
                            return StaticBehaviorReturnType.Failed;
                        }

                        if (randomPowerContextProto.HasDifficultyTierRestriction((PrototypeId)agent.Properties[PropertyEnum.DifficultyTier]))
                            continue;

                        contextResult = HandleUsePowerCheckCooldown(ownerController, proceduralAI, random, currentTime, randomPowerContextProto, randomProceduralPowerProto);
                        if (contextResult == StaticBehaviorReturnType.Completed)
                            break;
                    }

                    if (contextResult == StaticBehaviorReturnType.Running || contextResult == StaticBehaviorReturnType.Completed)
                        break;
                }
            }

            proceduralAI.LastPowerResult = contextResult;
            return contextResult;
        }

        public StaticBehaviorReturnType HandleUsePowerCheckCooldown(AIController ownerController, ProceduralAI proceduralAI, GRandom random,
            long currentTime, UsePowerContextPrototype powerContext, ProceduralUsePowerContextPrototype proceduralPowerContext)
        {
            var collection = ownerController.Blackboard.PropertyCollection;
            int agroTime = collection[PropertyEnum.AIAggroTime] + collection[PropertyEnum.AIInitialCooldownMSForPower, powerContext.Power];
            if (currentTime >= agroTime)
            {
                if (currentTime >= collection[PropertyEnum.AIProceduralPowerSpecificCDTime, powerContext.Power])
                {
                    if (OnPowerPicked(ownerController, proceduralPowerContext))
                    {
                        StaticBehaviorReturnType contextResult = HandleUsePowerContext(ownerController, proceduralAI, random, currentTime, powerContext, proceduralPowerContext);
                        OnPowerAttempted(ownerController, proceduralPowerContext, contextResult);
                        return contextResult;
                    }
                }
            }
            return StaticBehaviorReturnType.Failed;
        }

        public virtual void OnPowerAttempted(AIController ownerController, ProceduralUsePowerContextPrototype proceduralPowerContext,
            StaticBehaviorReturnType contextResult)
        { }

        public StaticBehaviorReturnType HandleUseAffixPowerContext(AIController ownerController, ProceduralAI proceduralAI, GRandom random, long currentTime)
        {
            BehaviorBlackboard blackboard = ownerController.Blackboard;
            IStateContext useAffixPowerContext = new UseAffixPowerContext(ownerController, null);
            var contextResult = proceduralAI.HandleContext(UseAffixPower.Instance, ref useAffixPowerContext, AffixSettings);
            UpdateNextAttackThinkTime(blackboard, random, currentTime, contextResult);
            return contextResult;
        }

        protected override StaticBehaviorReturnType HandleUsePowerContext(AIController ownerController, ProceduralAI proceduralAI, GRandom random,
            long currentTime, UsePowerContextPrototype powerContext, ProceduralContextPrototype proceduralContext = null)
        {
            var contextResult = base.HandleUsePowerContext(ownerController, proceduralAI, random, currentTime, powerContext, proceduralContext);
            UpdateNextAttackThinkTime(ownerController.Blackboard, random, currentTime, contextResult);
            return contextResult;
        }

        private void UpdateNextAttackThinkTime(BehaviorBlackboard blackboard, GRandom random, long currentTime, StaticBehaviorReturnType contextResult)
        {
            if (contextResult == StaticBehaviorReturnType.Completed)
                blackboard.PropertyCollection[PropertyEnum.AIProceduralNextAttackTime] = currentTime + random.Next(AttackRateMinMS, AttackRateMaxMS);
        }

        protected static bool IsProceduralPowerContextOnCooldown(BehaviorBlackboard blackboard, ProceduralUsePowerContextPrototype powerContext, long currentTime)
        {
            if (powerContext.PowerContext == null
                || powerContext.PowerContext.Power == PrototypeId.Invalid) return false;

            var specificTimeProp = new PropertyId(PropertyEnum.AIProceduralPowerSpecificCDTime, powerContext.PowerContext.Power);
            var collection = blackboard.PropertyCollection;
            if (collection.HasProperty(specificTimeProp))
                return currentTime < collection[specificTimeProp];
            else
            {
                int agroTime = collection[PropertyEnum.AIAggroTime] + collection[PropertyEnum.AIInitialCooldownMSForPower, powerContext.PowerContext.Power];
                return currentTime < agroTime;
            }
        }

        public virtual bool OnPowerPicked(AIController ownerController, ProceduralUsePowerContextPrototype powerContext)
        {
            if (powerContext.TargetSwitch != null)
            {
                var selectionContext = new SelectEntity.SelectEntityContext(ownerController, powerContext.TargetSwitch.SelectTarget);
                WorldEntity selectedEntity = SelectEntity.DoSelectEntity(ref selectionContext);
                if (selectedEntity == null)
                {
                    if (powerContext.TargetSwitch.UsePowerOnCurTargetIfSwitchFails) return true;
                    return false;
                }

                if (powerContext.TargetSwitch.SwitchPermanently == false)
                {
                    WorldEntity targetEntity = ownerController.TargetEntity;
                    if (targetEntity != null)
                        ownerController.Blackboard.PropertyCollection[PropertyEnum.AIProceduralPowerPrevTargetId] = targetEntity.Id;
                }

                if (SelectEntity.RegisterSelectedEntity(ownerController, selectedEntity, selectionContext.SelectEntityType) == false)
                    return false;
            }

            return true;
        }

    }

    public class ProceduralProfileStationaryTurretPrototype : ProceduralProfileWithAttackPrototype
    {
        public override void Think(AIController ownerController)
        {
            ProceduralAI proceduralAI = ownerController.Brain;
            if (proceduralAI == null) return;
            Agent agent = ownerController.Owner;
            if (agent == null) return;
            Game game = agent.Game;
            if (game == null) return;
            long currentTime = (long)game.GetCurrentTime().TotalMilliseconds;

            if (HandleOverrideBehavior(ownerController)) return;

            WorldEntity target = ownerController.TargetEntity;
            if (DefaultSensory(ref target, ownerController, proceduralAI, SelectTarget, CombatTargetType.Hostile) == false
                && proceduralAI.PartialOverrideBehavior == null) return;

            GRandom random = game.Random;
            Picker<ProceduralUsePowerContextPrototype> powerPicker = new(random);
            PopulatePowerPicker(ownerController, powerPicker);
            HandleProceduralPower(ownerController, proceduralAI, random, currentTime, powerPicker, true);

            HandleRotateToTarget(agent, target);
        }
    }

    public class ProceduralProfileRotatingTurretWithTargetPrototype : ProceduralProfileWithAttackPrototype
    {
        public RotateContextPrototype Rotate { get; protected set; }

        public override void Think(AIController ownerController)
        {
            ProceduralAI proceduralAI = ownerController.Brain;
            if (proceduralAI == null) return;
            Agent agent = ownerController.Owner;
            if (agent == null) return;
            Game game = agent.Game;
            if (game == null) return;
            long currentTime = (long)game.GetCurrentTime().TotalMilliseconds;

            if (HandleOverrideBehavior(ownerController)) return;

            WorldEntity target = ownerController.TargetEntity;
            if (DefaultSensory(ref target, ownerController, proceduralAI, SelectTarget, CombatTargetType.Hostile) == false
                && proceduralAI.PartialOverrideBehavior == null) return;

            GRandom random = game.Random;
            Picker<ProceduralUsePowerContextPrototype> powerPicker = new(random);
            PopulatePowerPicker(ownerController, powerPicker);
            if (HandleProceduralPower(ownerController, proceduralAI, random, currentTime, powerPicker, true) == StaticBehaviorReturnType.Running)
            {
                proceduralAI.PushSubstate();
                HandleContext(proceduralAI, ownerController, Rotate);
                proceduralAI.PopSubstate();
            }
        }
    }

    public class ProceduralProfileBasicMeleePrototype : ProceduralProfileWithAttackPrototype
    {
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public OrbitContextPrototype OrbitTarget { get; protected set; }
        public ProceduralUsePowerContextPrototype PrimaryPower { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            InitPower(agent, PrimaryPower);
        }

        public override void Think(AIController ownerController)
        {
            ProceduralAI proceduralAI = ownerController.Brain;
            if (proceduralAI == null) return;
            Agent agent = ownerController.Owner;
            if (agent == null) return;
            Game game = agent.Game;
            if (game == null) return;
            long currentTime = (long)game.GetCurrentTime().TotalMilliseconds;

            if (HandleOverrideBehavior(ownerController)) return;

            WorldEntity target = ownerController.TargetEntity;
            if (DefaultSensory(ref target, ownerController, proceduralAI, SelectTarget, CombatTargetType.Hostile) == false
                && proceduralAI.PartialOverrideBehavior == null) return;

            GRandom random = game.Random;
            Picker<ProceduralUsePowerContextPrototype> powerPicker = new(random);
            PopulatePowerPicker(ownerController, powerPicker);
            if (HandleProceduralPower(ownerController, proceduralAI, random, currentTime, powerPicker, true) == StaticBehaviorReturnType.Running) return;

            proceduralAI.PartialOverrideBehavior?.Think(ownerController);

            DefaultMeleeMovement(proceduralAI, ownerController, agent.Locomotor, target, MoveToTarget, OrbitTarget);
        }

        public override void PopulatePowerPicker(AIController ownerController, Picker<ProceduralUsePowerContextPrototype> powerPicker)
        {
            base.PopulatePowerPicker(ownerController, powerPicker);
            ownerController.AddPowersToPicker(powerPicker, PrimaryPower);
        }
    }

    public class ProceduralProfileBasicMelee2PowerPrototype : ProceduralProfileWithAttackPrototype
    {
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public OrbitContextPrototype OrbitTarget { get; protected set; }
        public ProceduralUsePowerContextPrototype Power1 { get; protected set; }
        public ProceduralUsePowerContextPrototype Power2 { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            InitPower(agent, Power1);
            InitPower(agent, Power2);
        }

        public override void Think(AIController ownerController)
        {
            ProceduralAI proceduralAI = ownerController.Brain;
            if (proceduralAI == null) return;
            Agent agent = ownerController.Owner;
            if (agent == null) return;
            Game game = agent.Game;
            if (game == null) return;
            long currentTime = (long)game.GetCurrentTime().TotalMilliseconds;

            if (HandleOverrideBehavior(ownerController)) return;

            WorldEntity target = ownerController.TargetEntity;
            if (DefaultSensory(ref target, ownerController, proceduralAI, SelectTarget, CombatTargetType.Hostile) == false
                && proceduralAI.PartialOverrideBehavior == null) return;

            GRandom random = game.Random;
            Picker<ProceduralUsePowerContextPrototype> powerPicker = new(random);
            PopulatePowerPicker(ownerController, powerPicker);
            if (HandleProceduralPower(ownerController, proceduralAI, random, currentTime, powerPicker, true) == StaticBehaviorReturnType.Running) return;

            proceduralAI.PartialOverrideBehavior?.Think(ownerController);

            DefaultMeleeMovement(proceduralAI, ownerController, agent.Locomotor, target, MoveToTarget, OrbitTarget);
        }

        public override void PopulatePowerPicker(AIController ownerController, Picker<ProceduralUsePowerContextPrototype> powerPicker)
        {
            base.PopulatePowerPicker(ownerController, powerPicker);
            ownerController.AddPowersToPicker(powerPicker, Power1);
            ownerController.AddPowersToPicker(powerPicker, Power2);
        }
    }

    public class ProceduralProfileBasicRangePrototype : ProceduralProfileWithAttackPrototype
    {
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public OrbitContextPrototype OrbitTarget { get; protected set; }

        public override void Think(AIController ownerController)
        {
            ProceduralAI proceduralAI = ownerController.Brain;
            if (proceduralAI == null) return;
            Agent agent = ownerController.Owner;
            if (agent == null) return;
            Game game = agent.Game;
            if (game == null) return;
            long currentTime = (long)game.GetCurrentTime().TotalMilliseconds;

            if (HandleOverrideBehavior(ownerController)) return;

            WorldEntity target = ownerController.TargetEntity;
            if (DefaultSensory(ref target, ownerController, proceduralAI, SelectTarget, CombatTargetType.Hostile) == false
                && proceduralAI.PartialOverrideBehavior == null) return;

            GRandom random = game.Random;
            Picker<ProceduralUsePowerContextPrototype> powerPicker = new(random);
            PopulatePowerPicker(ownerController, powerPicker);
            if (HandleProceduralPower(ownerController, proceduralAI, random, currentTime, powerPicker, true) == StaticBehaviorReturnType.Running) return;

            proceduralAI.PartialOverrideBehavior?.Think(ownerController);

            DefaultRangedMovement(proceduralAI, ownerController, agent, target, MoveToTarget, OrbitTarget);
        }
    }

    public class ProceduralProfileAlternateRange2Prototype : ProceduralProfileWithAttackPrototype
    {
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public ProceduralFlankContextPrototype FlankTarget { get; protected set; }
        public ProceduralUsePowerContextPrototype Power1 { get; protected set; }
        public ProceduralUsePowerContextPrototype Power2 { get; protected set; }
        public ProceduralUsePowerContextPrototype PowerSwap { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            InitPower(agent, Power1);
            InitPower(agent, Power2);
            InitPower(agent, PowerSwap);
        }

        public override void PopulatePowerPicker(AIController ownerController, Picker<ProceduralUsePowerContextPrototype> powerPicker)
        {
            int stateVal = ownerController.Blackboard.PropertyCollection[PropertyEnum.AICustomStateVal1];
            if (stateVal == 0)
                ownerController.AddPowersToPicker(powerPicker, Power1);
            else if (stateVal == 1)
                ownerController.AddPowersToPicker(powerPicker, Power2);
            ownerController.AddPowersToPicker(powerPicker, PowerSwap);
        }

        public override void Think(AIController ownerController)
        {
            ProceduralAI proceduralAI = ownerController.Brain;
            if (proceduralAI == null) return;
            Agent agent = ownerController.Owner;
            if (agent == null) return;
            Game game = agent.Game;
            if (game == null) return;
            long currentTime = (long)game.GetCurrentTime().TotalMilliseconds;

            if (HandleOverrideBehavior(ownerController)) return;

            WorldEntity target = ownerController.TargetEntity;
            if (DefaultSensory(ref target, ownerController, proceduralAI, SelectTarget, CombatTargetType.Hostile) == false
                && proceduralAI.PartialOverrideBehavior == null) return;

            GRandom random = game.Random;
            Picker<ProceduralUsePowerContextPrototype> powerPicker = new(random);
            PopulatePowerPicker(ownerController, powerPicker);
            if (HandleProceduralPower(ownerController, proceduralAI, random, currentTime, powerPicker, true) == StaticBehaviorReturnType.Running) return;

            proceduralAI.PartialOverrideBehavior?.Think(ownerController);

            DefaultRangedFlankerMovement(proceduralAI, ownerController, agent, target, currentTime, MoveToTarget, FlankTarget);
        }

    }

    public class ProceduralProfileMultishotRangePrototype : ProceduralProfileWithAttackPrototype
    {
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public OrbitContextPrototype OrbitTarget { get; protected set; }
        public ProceduralUsePowerContextPrototype MultishotPower { get; protected set; }
        public int NumShots { get; protected set; }
        public bool RetargetPerShot { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            InitPower(agent, MultishotPower);
        }

        public override void PopulatePowerPicker(AIController ownerController, Picker<ProceduralUsePowerContextPrototype> powerPicker)
        {
            base.PopulatePowerPicker(ownerController, powerPicker);
            ownerController.AddPowersToPicker(powerPicker, MultishotPower);
        }

        public override void Think(AIController ownerController)
        {
            ProceduralAI proceduralAI = ownerController.Brain;
            if (proceduralAI == null) return;
            Agent agent = ownerController.Owner;
            if (agent == null) return;
            Game game = agent.Game;
            if (game == null) return;
            long currentTime = (long)game.GetCurrentTime().TotalMilliseconds;

            if (HandleOverrideBehavior(ownerController)) return;

            WorldEntity target = ownerController.TargetEntity;
            if (DefaultSensory(ref target, ownerController, proceduralAI, SelectTarget, CombatTargetType.Hostile) == false
                && proceduralAI.PartialOverrideBehavior == null) return;

            BehaviorBlackboard blackboard = ownerController.Blackboard;
            int numShotsProp = blackboard.PropertyCollection[PropertyEnum.AICustomStateVal1];
            if (numShotsProp > 0)
            {
                if (MultishotLooper(ownerController, proceduralAI, agent, game.Random, currentTime, numShotsProp) == StaticBehaviorReturnType.Running)
                    return;
            }
            else
            {
                GRandom random = game.Random;
                Picker<ProceduralUsePowerContextPrototype> powerPicker = new(random);
                PopulatePowerPicker(ownerController, powerPicker);
                StaticBehaviorReturnType powerResult = HandleProceduralPower(ownerController, proceduralAI, random, currentTime, powerPicker, true);
                if (powerResult == StaticBehaviorReturnType.Running) return;
                if (powerResult == StaticBehaviorReturnType.Completed)
                {
                    numShotsProp = 1;
                    blackboard.PropertyCollection[PropertyEnum.AICustomStateVal1] = numShotsProp;

                    if (MultishotLooper(ownerController, proceduralAI, agent, game.Random, currentTime, numShotsProp) == StaticBehaviorReturnType.Running)
                        return;
                }
            }

            proceduralAI.PartialOverrideBehavior?.Think(ownerController);

            DefaultRangedMovement(proceduralAI, ownerController, agent, target, MoveToTarget, OrbitTarget);
        }

        protected StaticBehaviorReturnType MultishotLooper(AIController ownerController, ProceduralAI proceduralAI, Agent agent, GRandom random, long currentTime, int numShotsProp)
        {
            var collection = ownerController.Blackboard.PropertyCollection;

            if (numShotsProp >= NumShots)
            {
                collection.RemoveProperty(PropertyEnum.AICustomStateVal1);
                return StaticBehaviorReturnType.Completed;
            }

            while (numShotsProp < NumShots)
            {
                var powerResult = HandleUsePowerContext(ownerController, proceduralAI, random, currentTime, MultishotPower.PowerContext);
                if (powerResult == StaticBehaviorReturnType.Running)
                    return powerResult;
                else if (powerResult == StaticBehaviorReturnType.Completed)
                {
                    ++numShotsProp;
                    if (numShotsProp >= NumShots)
                        collection.RemoveProperty(PropertyEnum.AICustomStateVal1);
                    else
                    {
                        collection.AdjustProperty(1, PropertyEnum.AICustomStateVal1);
                        if (RetargetPerShot)
                        {
                            var selectionContext = new SelectEntity.SelectEntityContext(ownerController, SelectTarget);
                            WorldEntity selectedEntity = SelectEntity.DoSelectEntity(ref selectionContext);
                            if (selectedEntity != null && selectedEntity != agent)
                                SelectEntity.RegisterSelectedEntity(ownerController, selectedEntity, selectionContext.SelectEntityType);
                        }
                    }
                }
                else if (powerResult == StaticBehaviorReturnType.Failed)
                {
                    collection.RemoveProperty(PropertyEnum.AICustomStateVal1);
                    return powerResult;
                }
            }

            return StaticBehaviorReturnType.Completed;
        }

    }

    public class ProceduralProfileMultishotFlankerPrototype : ProceduralProfileWithAttackPrototype
    {
        public ProceduralFlankContextPrototype FlankTarget { get; protected set; }
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public ProceduralUsePowerContextPrototype MultishotPower { get; protected set; }
        public int NumShots { get; protected set; }
        public bool RetargetPerShot { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            InitPower(agent, MultishotPower);
        }

        public override void PopulatePowerPicker(AIController ownerController, Picker<ProceduralUsePowerContextPrototype> powerPicker)
        {
            base.PopulatePowerPicker(ownerController, powerPicker);
            ownerController.AddPowersToPicker(powerPicker, MultishotPower);
        }

        public override void Think(AIController ownerController)
        {
            ProceduralAI proceduralAI = ownerController.Brain;
            if (proceduralAI == null) return;
            Agent agent = ownerController.Owner;
            if (agent == null) return;
            Game game = agent.Game;
            if (game == null) return;
            long currentTime = (long)game.GetCurrentTime().TotalMilliseconds;

            if (HandleOverrideBehavior(ownerController)) return;

            WorldEntity target = ownerController.TargetEntity;
            if (DefaultSensory(ref target, ownerController, proceduralAI, SelectTarget, CombatTargetType.Hostile) == false
                && proceduralAI.PartialOverrideBehavior == null) return;

            BehaviorBlackboard blackboard = ownerController.Blackboard;
            int numShotsProp = blackboard.PropertyCollection[PropertyEnum.AICustomStateVal1];
            if (numShotsProp > 0)
            {
                if (MultishotLooper(ownerController, proceduralAI, agent, game.Random, currentTime, numShotsProp) == StaticBehaviorReturnType.Running)
                    return;
            }
            else
            {
                GRandom random = game.Random;
                Picker<ProceduralUsePowerContextPrototype> powerPicker = new(random);
                PopulatePowerPicker(ownerController, powerPicker);
                StaticBehaviorReturnType powerResult = HandleProceduralPower(ownerController, proceduralAI, random, currentTime, powerPicker, true);
                if (powerResult == StaticBehaviorReturnType.Running) return;
                if (powerResult == StaticBehaviorReturnType.Completed)
                {
                    numShotsProp = 1;
                    blackboard.PropertyCollection[PropertyEnum.AICustomStateVal1] = numShotsProp;

                    if (MultishotLooper(ownerController, proceduralAI, agent, game.Random, currentTime, numShotsProp) == StaticBehaviorReturnType.Running)
                        return;
                }
            }

            proceduralAI.PartialOverrideBehavior?.Think(ownerController);

            DefaultRangedFlankerMovement(proceduralAI, ownerController, agent, target, currentTime, MoveToTarget, FlankTarget);
        }

        protected StaticBehaviorReturnType MultishotLooper(AIController ownerController, ProceduralAI proceduralAI, Agent agent, GRandom random, long currentTime, int numShotsProp)
        {
            var collection = ownerController.Blackboard.PropertyCollection;

            while (numShotsProp < NumShots)
            {
                var powerResult = HandleUsePowerContext(ownerController, proceduralAI, random, currentTime, MultishotPower.PowerContext);
                if (powerResult == StaticBehaviorReturnType.Running)
                    return powerResult;
                else if (powerResult == StaticBehaviorReturnType.Completed)
                {
                    ++numShotsProp;
                    if (numShotsProp >= NumShots)
                        collection.RemoveProperty(PropertyEnum.AICustomStateVal1);
                    else
                    {
                        collection.AdjustProperty(1, PropertyEnum.AICustomStateVal1);
                        if (RetargetPerShot)
                        {
                            var selectionContext = new SelectEntity.SelectEntityContext(ownerController, SelectTarget);
                            WorldEntity selectedEntity = SelectEntity.DoSelectEntity(ref selectionContext);
                            if (selectedEntity != null && selectedEntity != agent)
                                SelectEntity.RegisterSelectedEntity(ownerController, selectedEntity, selectionContext.SelectEntityType);
                        }
                    }
                }
                else if (powerResult == StaticBehaviorReturnType.Failed)
                {
                    collection.RemoveProperty(PropertyEnum.AICustomStateVal1);
                    return powerResult;
                }
            }

            return StaticBehaviorReturnType.Completed;
        }
    }

    public class ProceduralProfileMultishotHiderPrototype : ProceduralProfileWithAttackPrototype
    {
        public ProceduralUsePowerContextPrototype HidePower { get; protected set; }
        public ProceduralUsePowerContextPrototype MultishotPower { get; protected set; }
        public int NumShots { get; protected set; }
        public bool RetargetPerShot { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            InitPower(agent, HidePower);
            InitPower(agent, MultishotPower);
        }

        private enum State
        {
            Hide,
            Multishot,
            Unhide
        }

        public override void PopulatePowerPicker(AIController ownerController, Picker<ProceduralUsePowerContextPrototype> powerPicker)
        {
            int stateVal = ownerController.Blackboard.PropertyCollection[PropertyEnum.AICustomStateVal2];
            if ((State)stateVal == State.Multishot)
                ownerController.AddPowersToPicker(powerPicker, MultishotPower);
            else
                ownerController.AddPowersToPicker(powerPicker, HidePower);
            base.PopulatePowerPicker(ownerController, powerPicker);
        }

        public override void Think(AIController ownerController)
        {
            ProceduralAI proceduralAI = ownerController.Brain;
            if (proceduralAI == null) return;
            Agent agent = ownerController.Owner;
            if (agent == null) return;
            Game game = agent.Game;
            if (game == null) return;
            long currentTime = (long)game.GetCurrentTime().TotalMilliseconds;

            if (HandleOverrideBehavior(ownerController)) return;

            WorldEntity target = ownerController.TargetEntity;
            if (DefaultSensory(ref target, ownerController, proceduralAI, SelectTarget, CombatTargetType.Hostile) == false
                && proceduralAI.PartialOverrideBehavior == null) return;

            var powerContext = HidePower?.PowerContext;
            if (powerContext == null || powerContext.Power == PrototypeId.Invalid) return;
            Power hidePower = agent.GetPower(powerContext.Power);
            if (hidePower == null) return;

            BehaviorBlackboard blackboard = ownerController.Blackboard;
            int state = blackboard.PropertyCollection[PropertyEnum.AICustomStateVal2];
            switch ((State)state)
            {
                case State.Hide:
                    GRandom random = game.Random;
                    Picker<ProceduralUsePowerContextPrototype> powerPicker = new(random);
                    PopulatePowerPicker(ownerController, powerPicker);
                    if (HandleProceduralPower(ownerController, proceduralAI, random, currentTime, powerPicker, true) == StaticBehaviorReturnType.Completed)
                        blackboard.PropertyCollection[PropertyEnum.AICustomStateVal2] = (int)State.Multishot;
                    break;

                case State.Multishot:
                    int numShotsProp = blackboard.PropertyCollection[PropertyEnum.AICustomStateVal1];
                    random = game.Random;
                    if (numShotsProp > 0)
                        MultishotLooper(ownerController, proceduralAI, agent, random, currentTime, numShotsProp);
                    else
                    {
                        powerPicker = new(random);
                        PopulatePowerPicker(ownerController, powerPicker);
                        if (HandleProceduralPower(ownerController, proceduralAI, random, currentTime, powerPicker, true) == StaticBehaviorReturnType.Completed)
                        {
                            numShotsProp = 1;
                            blackboard.PropertyCollection[PropertyEnum.AICustomStateVal1] = numShotsProp;
                            MultishotLooper(ownerController, proceduralAI, agent, game.Random, currentTime, numShotsProp);
                        }
                    }
                    break;

                case State.Unhide:
                    random = game.Random;
                    if (HandleUsePowerContext(ownerController, proceduralAI, random, currentTime, HidePower.PowerContext) == StaticBehaviorReturnType.Completed)
                        blackboard.PropertyCollection[PropertyEnum.AICustomStateVal2] = (int)State.Hide;
                    break;
            }
        }

        protected StaticBehaviorReturnType MultishotLooper(AIController ownerController, ProceduralAI proceduralAI, Agent agent, GRandom random, long currentTime, int numShotsProp)
        {
            var collection = ownerController.Blackboard.PropertyCollection;

            while (numShotsProp < NumShots)
            {
                var powerResult = HandleUsePowerContext(ownerController, proceduralAI, random, currentTime, MultishotPower.PowerContext);
                if (powerResult == StaticBehaviorReturnType.Running)
                    return powerResult;
                else if (powerResult == StaticBehaviorReturnType.Completed)
                {
                    ++numShotsProp;
                    if (numShotsProp >= NumShots)
                    {
                        collection.RemoveProperty(PropertyEnum.AICustomStateVal1);
                        collection[PropertyEnum.AICustomStateVal2] = (int)State.Unhide;
                    }
                    else
                    {
                        collection.AdjustProperty(1, PropertyEnum.AICustomStateVal1);
                        if (RetargetPerShot)
                        {
                            var selectionContext = new SelectEntity.SelectEntityContext(ownerController, SelectTarget);
                            WorldEntity selectedEntity = SelectEntity.DoSelectEntity(ref selectionContext);
                            if (selectedEntity != null && selectedEntity != agent)
                                SelectEntity.RegisterSelectedEntity(ownerController, selectedEntity, selectionContext.SelectEntityType);
                        }
                    }
                }
                else if (powerResult == StaticBehaviorReturnType.Failed)
                {
                    collection.RemoveProperty(PropertyEnum.AICustomStateVal1);
                    collection[PropertyEnum.AICustomStateVal2] = (int)State.Unhide;
                    return powerResult;
                }
            }

            return StaticBehaviorReturnType.Completed;
        }
    }

    public class ProceduralProfileMeleeSpeedByDistancePrototype : ProceduralProfileWithAttackPrototype
    {
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public OrbitContextPrototype OrbitTarget { get; protected set; }
        public ProceduralUsePowerContextPrototype PrimaryPower { get; protected set; }
        public UsePowerContextPrototype ExtraSpeedPower { get; protected set; }
        public UsePowerContextPrototype SpeedRemovalPower { get; protected set; }
        public float DistanceFromTargetForSpeedBonus { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            InitPower(agent, PrimaryPower);
            InitPower(agent, ExtraSpeedPower);
            InitPower(agent, SpeedRemovalPower);
        }

        public override void PopulatePowerPicker(AIController ownerController, Picker<ProceduralUsePowerContextPrototype> powerPicker)
        {
            base.PopulatePowerPicker(ownerController, powerPicker);
            ownerController.AddPowersToPicker(powerPicker, PrimaryPower);
        }
    }

    public class ProceduralProfileRangeFlankerPrototype : ProceduralProfileWithAttackPrototype
    {
        public ProceduralFlankContextPrototype FlankTarget { get; protected set; }
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public ProceduralUsePowerContextPrototype PrimaryPower { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            InitPower(agent, PrimaryPower);
        }

        public override void PopulatePowerPicker(AIController ownerController, Picker<ProceduralUsePowerContextPrototype> powerPicker)
        {
            base.PopulatePowerPicker(ownerController, powerPicker);
            ownerController.AddPowersToPicker(powerPicker, PrimaryPower);
        }

        public override void Think(AIController ownerController)
        {
            ProceduralAI proceduralAI = ownerController.Brain;
            if (proceduralAI == null) return;
            Agent agent = ownerController.Owner;
            if (agent == null) return;
            Game game = agent.Game;
            if (game == null) return;
            long currentTime = (long)game.GetCurrentTime().TotalMilliseconds;

            if (HandleOverrideBehavior(ownerController)) return;

            WorldEntity target = ownerController.TargetEntity;
            if (DefaultSensory(ref target, ownerController, proceduralAI, SelectTarget, CombatTargetType.Hostile) == false
                && proceduralAI.PartialOverrideBehavior == null) return;

            GRandom random = game.Random;
            Picker<ProceduralUsePowerContextPrototype> powerPicker = new(random);
            PopulatePowerPicker(ownerController, powerPicker);
            if (HandleProceduralPower(ownerController, proceduralAI, random, currentTime, powerPicker, true) == StaticBehaviorReturnType.Running) return;

            proceduralAI.PartialOverrideBehavior?.Think(ownerController);

            DefaultRangedFlankerMovement(proceduralAI, ownerController, agent, target, currentTime, MoveToTarget, FlankTarget);
        }
    }

    public class ProceduralProfileSkirmisherPrototype : ProceduralProfileWithAttackPrototype
    {
        public WanderContextPrototype SkirmishMovement { get; protected set; }
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public ProceduralUsePowerContextPrototype PrimaryPower { get; protected set; }
        public float MoveToSpeedBonus { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            InitPower(agent, PrimaryPower);
        }

        public override void Think(AIController ownerController)
        {
            ProceduralAI proceduralAI = ownerController.Brain;
            if (proceduralAI == null) return;
            Agent agent = ownerController.Owner;
            if (agent == null) return;
            Game game = agent.Game;
            if (game == null) return;
            long currentTime = (long)game.GetCurrentTime().TotalMilliseconds;

            if (HandleOverrideBehavior(ownerController)) return;

            WorldEntity target = ownerController.TargetEntity;
            if (DefaultSensory(ref target, ownerController, proceduralAI, SelectTarget, CombatTargetType.Hostile) == false
                && proceduralAI.PartialOverrideBehavior == null) return;

            GRandom random = game.Random;
            Picker<ProceduralUsePowerContextPrototype> powerPicker = new(random);
            PopulatePowerPicker(ownerController, powerPicker);
            if (HandleProceduralPower(ownerController, proceduralAI, random, currentTime, powerPicker, true) == StaticBehaviorReturnType.Running) return;

            proceduralAI.PartialOverrideBehavior?.Think(ownerController);

            IAIState state = proceduralAI.GetState(0);
            bool toMove = state == MoveTo.Instance;
            if (toMove == false && state != Wander.Instance)
                toMove = !IsProceduralPowerContextOnCooldown(ownerController.Blackboard, PrimaryPower, currentTime);

            if (toMove)
            {
                if (proceduralAI.GetState(0) != MoveTo.Instance)
                    agent.Properties.AdjustProperty(MoveToSpeedBonus, PropertyEnum.MovementSpeedIncrPct);
                if (HandleMovementContext(proceduralAI, ownerController, agent.Locomotor, MoveToTarget, false, out var moveToResult) == false) return;
                if (moveToResult == StaticBehaviorReturnType.Running || moveToResult == StaticBehaviorReturnType.Completed)
                {
                    if (moveToResult == StaticBehaviorReturnType.Completed)
                        agent.Properties.AdjustProperty(-MoveToSpeedBonus, PropertyEnum.MovementSpeedIncrPct);
                    return;
                }
            }

            HandleMovementContext(proceduralAI, ownerController, agent.Locomotor, SkirmishMovement, false, out _);
        }

        public override void PopulatePowerPicker(AIController ownerController, Picker<ProceduralUsePowerContextPrototype> powerPicker)
        {
            base.PopulatePowerPicker(ownerController, powerPicker);
            ownerController.AddPowersToPicker(powerPicker, PrimaryPower);
        }
    }

    public class ProceduralProfileRangedWithMeleePriority2PowerPrototype : ProceduralProfileWithAttackPrototype
    {
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public OrbitContextPrototype OrbitTarget { get; protected set; }
        public ProceduralUsePowerContextPrototype MeleePower { get; protected set; }
        public ProceduralUsePowerContextPrototype RangedPower { get; protected set; }
        public float MaxDistToMoveIntoMelee { get; protected set; }
        public MoveToContextPrototype MoveIntoMeleeRange { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            InitPower(agent, MeleePower);
            InitPower(agent, RangedPower);
        }
    }

    public class ProfMeleePwrSpecialAtHealthPctPrototype : ProceduralProfileWithAttackPrototype
    {
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public OrbitContextPrototype OrbitTarget { get; protected set; }
        public float SpecialAtHealthChunkPct { get; protected set; }
        public UsePowerContextPrototype SpecialPowerAtHealthChunkPct { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            InitPower(agent, SpecialPowerAtHealthChunkPct);
        }

        public override void Think(AIController ownerController)
        {
            ProceduralAI proceduralAI = ownerController.Brain;
            if (proceduralAI == null) return;
            Agent agent = ownerController.Owner;
            if (agent == null) return;
            Game game = agent.Game;
            if (game == null) return;
            long currentTime = (long)game.GetCurrentTime().TotalMilliseconds;

            if (HandleOverrideBehavior(ownerController)) return;

            WorldEntity target = ownerController.TargetEntity;
            if (DefaultSensory(ref target, ownerController, proceduralAI, SelectTarget, CombatTargetType.Hostile) == false
                && proceduralAI.PartialOverrideBehavior == null) return;

            if (CheckAgentHealthAndUsePower(ownerController, proceduralAI, currentTime, agent)) return;

            GRandom random = game.Random;
            Picker<ProceduralUsePowerContextPrototype> powerPicker = new(random);
            PopulatePowerPicker(ownerController, powerPicker);
            if (HandleProceduralPower(ownerController, proceduralAI, random, currentTime, powerPicker, true) == StaticBehaviorReturnType.Running) return;

            proceduralAI.PartialOverrideBehavior?.Think(ownerController);

            DefaultMeleeMovement(proceduralAI, ownerController, agent.Locomotor, target, MoveToTarget, OrbitTarget);
        }

        protected bool CheckAgentHealthAndUsePower(AIController ownerController, ProceduralAI proceduralAI, long currentTime, Agent agent)
        {
            Game game = agent.Game;
            if (game == null) return false;
            BehaviorBlackboard blackboard = ownerController.Blackboard;

            if (blackboard.PropertyCollection[PropertyEnum.AICustomStateVal2] == 1)
            {
                if (HandleUsePowerContext(ownerController, proceduralAI, game.Random, currentTime, SpecialPowerAtHealthChunkPct) == StaticBehaviorReturnType.Running)
                    return true;
                blackboard.PropertyCollection[PropertyEnum.AICustomStateVal2] = 2;
            }
            else
            {
                if (proceduralAI.GetState(0) != UsePower.Instance)
                {
                    long health = agent.Properties[PropertyEnum.Health];
                    long maxHealth = agent.Properties[PropertyEnum.HealthMax];
                    long healthChunk = MathHelper.RoundToInt64(SpecialAtHealthChunkPct * maxHealth);
                    int lastHealth = blackboard.PropertyCollection[PropertyEnum.AICustomStateVal1];
                    int nextHealth = lastHealth + 1;

                    if (health <= (maxHealth - healthChunk * nextHealth))
                    {
                        StaticBehaviorReturnType powerResult = HandleUsePowerContext(ownerController, proceduralAI, game.Random, currentTime, SpecialPowerAtHealthChunkPct);
                        if (powerResult == StaticBehaviorReturnType.Running || powerResult == StaticBehaviorReturnType.Completed)
                        {
                            blackboard.PropertyCollection[PropertyEnum.AICustomStateVal1] = nextHealth;
                            if (powerResult == StaticBehaviorReturnType.Running)
                            {
                                blackboard.PropertyCollection[PropertyEnum.AICustomStateVal2] = 1;
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

    }

    public class ProceduralProfileNoMoveDefaultSensoryPrototype : ProceduralProfileWithAttackPrototype
    {
        public override void Think(AIController ownerController)
        {
            ProceduralAI proceduralAI = ownerController.Brain;
            if (proceduralAI == null) return;
            Agent agent = ownerController.Owner;
            if (agent == null) return;
            Game game = agent.Game;
            if (game == null) return;
            long currentTime = (long)game.GetCurrentTime().TotalMilliseconds;

            WorldEntity target = ownerController.TargetEntity;
            if (DefaultSensory(ref target, ownerController, proceduralAI, SelectTarget, CombatTargetType.Hostile) == false
                && proceduralAI.PartialOverrideBehavior == null) return;

            GRandom random = game.Random;
            Picker<ProceduralUsePowerContextPrototype> powerPicker = new(random);
            PopulatePowerPicker(ownerController, powerPicker);
            HandleProceduralPower(ownerController, proceduralAI, random, currentTime, powerPicker, true);
        }
    }

    public class ProceduralProfileNoMoveSimplifiedSensoryPrototype : ProceduralProfileWithAttackPrototype
    {
    }

    public class ProceduralProfileNoMoveSimplifiedAllySensoryPrototype : ProceduralProfileWithAttackPrototype
    {
    }

    public class ProfKillSelfAfterOnePowerNoMovePrototype : ProceduralProfileWithAttackPrototype
    {
    }

    public class ProceduralProfileNoMoveNoSensePrototype : ProceduralProfileWithAttackPrototype
    {
        public override void Think(AIController ownerController)
        {
            ProceduralAI proceduralAI = ownerController.Brain;
            if (proceduralAI == null) return;
            Agent agent = ownerController.Owner;
            if (agent == null) return;
            Game game = agent.Game;
            if (game == null) return;
            long currentTime = (long)game.GetCurrentTime().TotalMilliseconds;

            if (ownerController.TargetEntity == null)
                SelectEntity.RegisterSelectedEntity(ownerController, agent, SelectEntityType.SelectTarget);

            GRandom random = game.Random;
            Picker<ProceduralUsePowerContextPrototype> powerPicker = new(random);
            PopulatePowerPicker(ownerController, powerPicker);
            HandleProceduralPower(ownerController, proceduralAI, random, currentTime, powerPicker, true);
        }
    }

    public class ProceduralProfileBasicWanderPrototype : ProceduralProfileWithAttackPrototype
    {
        public WanderContextPrototype WanderMovement { get; protected set; }

        public override void Think(AIController ownerController)
        {
            ProceduralAI proceduralAI = ownerController.Brain;
            if (proceduralAI == null) return;
            Agent agent = ownerController.Owner;
            if (agent == null) return;
            Game game = agent.Game;
            if (game == null) return;
            long currentTime = (long)game.GetCurrentTime().TotalMilliseconds;

            if (HandleOverrideBehavior(ownerController)) return;

            WorldEntity target = ownerController.TargetEntity;
            if (DefaultSensory(ref target, ownerController, proceduralAI, SelectTarget, CombatTargetType.Hostile) == false
                && proceduralAI.PartialOverrideBehavior == null) return;

            GRandom random = game.Random;
            Picker<ProceduralUsePowerContextPrototype> powerPicker = new(random);
            PopulatePowerPicker(ownerController, powerPicker);
            if (HandleProceduralPower(ownerController, proceduralAI, random, currentTime, powerPicker, true) == StaticBehaviorReturnType.Running) return;

            proceduralAI.PartialOverrideBehavior?.Think(ownerController);

            HandleMovementContext(proceduralAI, ownerController, agent.Locomotor, WanderMovement, false, out _);
        }
    }

    public class ProceduralProfilePvPMeleePrototype : ProceduralProfileWithAttackPrototype
    {
        public float AggroRadius { get; protected set; }
        public float AggroDropRadius { get; protected set; }
        public float AggroDropByLOSChance { get; protected set; }
        public long AttentionSpanMS { get; protected set; }
        public PrototypeId PrimaryPower { get; protected set; }
        public int PathGroup { get; protected set; }
        public PathMethod PathMethod { get; protected set; }
        public float PathThreshold { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            BehaviorBlackboard blackboard = agent?.AIController?.Blackboard;
            if (blackboard == null) return;
            blackboard.PropertyCollection[PropertyEnum.AIAggroDropRange] = AggroDropRadius;
            blackboard.PropertyCollection[PropertyEnum.AIAggroDropRange] = AggroDropByLOSChance;
            blackboard.PropertyCollection[PropertyEnum.AIAggroRangeHostile] = AggroRadius;

            InitPower(agent, PrimaryPower);
        }
    }

    public class ProceduralProfilePvPTowerPrototype : ProceduralProfileWithAttackPrototype
    {
        public SelectEntityContextPrototype SelectTarget2 { get; protected set; }
        public SelectEntityContextPrototype SelectTarget3 { get; protected set; }
        public SelectEntityContextPrototype SelectTarget4 { get; protected set; }
    }

    public class ProceduralProfileMeleeDropWeaponPrototype : ProceduralProfileWithAttackPrototype
    {
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public OrbitContextPrototype OrbitTarget { get; protected set; }
        public ProceduralUsePowerContextPrototype PowerMeleeWithWeapon { get; protected set; }
        public ProceduralUsePowerContextPrototype PowerMeleeNoWeapon { get; protected set; }
        public ProceduralUsePowerContextPrototype PowerDropWeapon { get; protected set; }
        public ProceduralUsePowerContextPrototype PowerPickupWeapon { get; protected set; }
        public SelectEntityContextPrototype SelectWeaponAsTarget { get; protected set; }
        public int DropPickupTimeMax { get; protected set; }
        public int DropPickupTimeMin { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            InitPower(agent, PowerMeleeWithWeapon);
            InitPower(agent, PowerMeleeNoWeapon);
            InitPower(agent, PowerDropWeapon);
            InitPower(agent, PowerPickupWeapon);
        }
    }

    public class ProceduralProfileMeleeAllyDeathFleePrototype : ProceduralProfileWithAttackPrototype
    {
        public FleeContextPrototype FleeFromTargetOnAllyDeath { get; protected set; }
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public OrbitContextPrototype OrbitTarget { get; protected set; }
    }

    public class ProceduralProfileRangedFlankerAllyDeathFleePrototype : ProceduralProfileWithAttackPrototype
    {
        public FleeContextPrototype FleeFromTargetOnAllyDeath { get; protected set; }
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public ProceduralFlankContextPrototype FlankTarget { get; protected set; }
    }

    public class ProceduralProfileRangedHotspotDropperPrototype : ProceduralProfileWithAttackPrototype
    {
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public OrbitContextPrototype OrbitTarget { get; protected set; }
        public ProceduralUsePowerContextPrototype RangedPower { get; protected set; }
        public ProceduralUsePowerContextPrototype HotspotPower { get; protected set; }
        public WanderContextPrototype HotspotDroppingMovement { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            InitPower(agent, RangedPower);
            InitPower(agent, HotspotPower);
        }

        public override void PopulatePowerPicker(AIController ownerController, Picker<ProceduralUsePowerContextPrototype> powerPicker)
        {
            ownerController.AddPowersToPicker(powerPicker, RangedPower);
            ownerController.AddPowersToPicker(powerPicker, HotspotPower);
        }
    }

    public class ProceduralProfileTeamUpPrototype : ProceduralProfileWithAttackPrototype
    {
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public OrbitContextPrototype OrbitTarget { get; protected set; }
        public ProceduralFlankContextPrototype FlankTarget { get; protected set; }
        public bool IsRanged { get; protected set; }
        public MoveToContextPrototype MoveToMaster { get; protected set; }
        public TeleportContextPrototype TeleportToMasterIfTooFarAway { get; protected set; }
        public int MaxDistToMasterBeforeTeleport { get; protected set; }
        public ProceduralUsePowerContextPrototype[] TeamUpPowerProgressionPowers { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            InitPowers(agent, TeamUpPowerProgressionPowers);
        }

        public override void PopulatePowerPicker(AIController ownerController, Picker<ProceduralUsePowerContextPrototype> powerPicker)
        {
            base.PopulatePowerPicker(ownerController, powerPicker);
            Agent agent = ownerController.Owner;
            if (agent == null) return;

            if (TeamUpPowerProgressionPowers.HasValue())
            {
                PrototypeId activePowerRef = ownerController.ActivePowerRef;
                foreach (var proceduralPower in TeamUpPowerProgressionPowers)
                {
                    var powerContext = proceduralPower?.PowerContext;
                    if (powerContext == null || powerContext.Power == PrototypeId.Invalid) continue;

                    PrototypeId powerRef = proceduralPower.PowerContext.Power;
                    var rank = agent.GetPowerRank(powerRef);
                    bool isActivePower = activePowerRef != PrototypeId.Invalid && powerRef == activePowerRef;
                    if (rank > 0 || isActivePower)
                        ownerController.AddPowersToPicker(powerPicker, proceduralPower);
                }
            }
        }

    }

    public class ProceduralProfilePetPrototype : ProceduralProfileWithAttackPrototype
    {
        public MoveToContextPrototype PetFollow { get; protected set; }
        public TeleportContextPrototype TeleportToMasterIfTooFarAway { get; protected set; }
        public int MaxDistToMasterBeforeTeleport { get; protected set; }
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public OrbitContextPrototype OrbitTarget { get; protected set; }
        public ProceduralFlankContextPrototype FlankTarget { get; protected set; }
        public bool IsRanged { get; protected set; }
    }

    public class ProceduralProfileMeleePowerOnHitPrototype : ProceduralProfileWithAttackPrototype
    {
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public OrbitContextPrototype OrbitTarget { get; protected set; }
        public ProceduralUsePowerContextPrototype PowerOnHit { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            InitPower(agent, PowerOnHit);
        }

        public override void Think(AIController ownerController)
        {
            ProceduralAI proceduralAI = ownerController.Brain;
            if (proceduralAI == null) return;
            Agent agent = ownerController.Owner;
            if (agent == null) return;
            Game game = agent.Game;
            if (game == null) return;
            long currentTime = (long)game.GetCurrentTime().TotalMilliseconds;

            if (HandleOverrideBehavior(ownerController)) return;

            WorldEntity target = ownerController.TargetEntity;
            if (DefaultSensory(ref target, ownerController, proceduralAI, SelectTarget, CombatTargetType.Hostile) == false
                && proceduralAI.PartialOverrideBehavior == null) return;

            GRandom random = game.Random;
            Picker<ProceduralUsePowerContextPrototype> powerPicker = new(random);
            PopulatePowerPicker(ownerController, powerPicker);
            if (HandleProceduralPower(ownerController, proceduralAI, random, currentTime, powerPicker, true) == StaticBehaviorReturnType.Running) return;

            proceduralAI.PartialOverrideBehavior?.Think(ownerController);

            DefaultMeleeMovement(proceduralAI, ownerController, agent.Locomotor, target, MoveToTarget, OrbitTarget);
        }

        public override void PopulatePowerPicker(AIController ownerController, Picker<ProceduralUsePowerContextPrototype> powerPicker)
        {
            if (PowerOnHit == null) return;
            PrototypeId startedPowerRef = ownerController.ActivePowerRef;
            if (startedPowerRef != PrototypeId.Invalid)
            {
                if (AddPowerToPickerIfStartedPowerIsContextPower(ownerController, PowerOnHit, startedPowerRef, powerPicker))
                    return;
            }
            else
            {
                int stateVal = ownerController.Blackboard.PropertyCollection[PropertyEnum.AICustomStateVal1];
                if (stateVal != 0)
                    ownerController.AddPowersToPicker(powerPicker, PowerOnHit);
            }
            base.PopulatePowerPicker(ownerController, powerPicker);
        }
    }

    public class ProceduralProfileBotAIPrototype : ProceduralProfileWithAttackPrototype
    {
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public OrbitContextPrototype OrbitTarget { get; protected set; }
        public SelectEntityContextPrototype SelectTargetItem { get; protected set; }
        public WanderContextPrototype WanderMovement { get; protected set; }
        public ProceduralUsePowerContextPrototype[] SlottedAbilities { get; protected set; }

        public override void PopulatePowerPicker(AIController ownerController, Picker<ProceduralUsePowerContextPrototype> powerPicker)
        {
            base.PopulatePowerPicker(ownerController, powerPicker);
        }
    }

    public class ProceduralProfileMeleeFlockerPrototype : ProceduralProfileWithAttackPrototype
    {
        public FlockContextPrototype FlockContext { get; protected set; }
        public PrototypeId FleeOnAllyDeathOverride { get; protected set; }
    }

    public class ProceduralProfileWithEnragePrototype : ProceduralProfileWithAttackPrototype
    {
        public int EnrageTimerInMinutes { get; protected set; }
        public ProceduralUsePowerContextPrototype EnragePower { get; protected set; }
        public float EnrageTimerAvatarSearchRadius { get; protected set; }
        public ProceduralUsePowerContextPrototype[] PostEnragePowers { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            InitPower(agent, EnragePower);
            InitPowers(agent, PostEnragePowers);
        }

        public override void PopulatePowerPicker(AIController ownerController, Picker<ProceduralUsePowerContextPrototype> powerPicker)
        {
            base.PopulatePowerPicker(ownerController, powerPicker);
            int stateVal = ownerController.Blackboard.PropertyCollection[PropertyEnum.AIEnrageState];
            if (stateVal == 3)
                ownerController.AddPowersToPicker(powerPicker, PostEnragePowers);
        }
    }

    public class ProceduralProfileMissionAllyPrototype : ProceduralProfileWithAttackPrototype
    {
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public OrbitContextPrototype OrbitTarget { get; protected set; }
        public ProceduralFlankContextPrototype FlankTarget { get; protected set; }
        public MoveToContextPrototype MoveToAvatarAlly { get; protected set; }
        public TeleportContextPrototype TeleportToAvatarAllyIfTooFarAway { get; protected set; }
        public int MaxDistToAvatarAllyBeforeTele { get; protected set; }
        public bool IsRanged { get; protected set; }
        public float AvatarAllySearchRadius { get; protected set; }
    }

    public class ProceduralProfileSpikeDanceMobPrototype : ProceduralProfileWithAttackPrototype
    {
        public ProceduralUsePowerContextPrototype SpikeDanceMissile { get; protected set; }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            InitPower(agent, SpikeDanceMissile);
        }
    }

    public class ProceduralProfilePowerRestrictedPrototype : ProceduralProfileWithAttackPrototype
    {
        public MoveToContextPrototype MoveToTarget { get; protected set; }
        public OrbitContextPrototype OrbitTarget { get; protected set; }
        public ProceduralFlankContextPrototype FlankTarget { get; protected set; }
        public bool IsRanged { get; protected set; }
        public ProceduralUsePowerContextPrototype RestrictedModeStartPower { get; protected set; }
        public ProceduralUsePowerContextPrototype RestrictedModeEndPower { get; protected set; }
        public ProceduralUsePowerContextPrototype[] RestrictedModeProceduralPowers { get; protected set; }
        public int RestrictedModeMinCooldownMS { get; protected set; }
        public int RestrictedModeMaxCooldownMS { get; protected set; }
        public int RestrictedModeTimerMS { get; protected set; }
        public bool NoMoveInRestrictedMode { get; protected set; }

        private enum State
        {
            Default,
            StartPower = 1,
            ProceduralPowers = 2,
            EndPower = 3,
        }

        public override void Init(Agent agent)
        {
            base.Init(agent);
            InitPower(agent, RestrictedModeStartPower);
            InitPower(agent, RestrictedModeEndPower);
            InitPowers(agent, RestrictedModeProceduralPowers);

            Game game = agent.Game;
            var blackboard = agent?.AIController?.Blackboard;
            if (game == null || blackboard == null) return;

            long restrictedCooldown = (long)game.GetCurrentTime().TotalMilliseconds + game.Random.Next(RestrictedModeMinCooldownMS, RestrictedModeMaxCooldownMS);
            blackboard.PropertyCollection[PropertyEnum.AICustomTimeVal1] = restrictedCooldown;
        }

        public override void PopulatePowerPicker(AIController ownerController, Picker<ProceduralUsePowerContextPrototype> powerPicker)
        {
            int stateVal = ownerController.Blackboard.PropertyCollection[PropertyEnum.AICustomStateVal1];
            if ((State)stateVal == State.ProceduralPowers)
                ownerController.AddPowersToPicker(powerPicker, RestrictedModeProceduralPowers);
            else
                base.PopulatePowerPicker(ownerController, powerPicker);
        }
    }

}