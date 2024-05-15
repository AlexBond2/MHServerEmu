﻿using MHServerEmu.Core.Collections;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.System;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Events;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Generators.Population;
using MHServerEmu.Games.Powers;
using MHServerEmu.Games.Properties;

namespace MHServerEmu.Games.Behavior
{
    public class AIController
    {
        private static readonly Logger Logger = LogManager.CreateLogger();
        private EventGroup _pendingEvents = new();
        private AIThinkEvent _thinkEvent = new();
        public Agent Owner { get; private set; }
        public Game Game { get; private set; }
        public ProceduralAI.ProceduralAI Brain { get; private set; }
        public BehaviorSensorySystem Senses { get; private set; }
        public BehaviorBlackboard Blackboard { get; private set; }   
        public bool IsEnabled { get; private set; }
        public PrototypeId ActivePowerRef { get; internal set; }
        public WorldEntity TargetEntity => Senses.GetCurrentTarget();
        public WorldEntity InteractEntity => GetInteractEntityHelper();
        public WorldEntity AssistedEntity => GetAssistedEntityHelper();

        public AIController(Game game, Agent owner)
        {
            Game = game;
            Owner = owner;
            Senses = new ();
            Blackboard = new (owner);
            Brain = new (game, this); 
        }

        public bool Initialize(BehaviorProfilePrototype profile, SpawnSpec spec, PropertyCollection collection)
        {
            Senses.Initialize(this, profile, spec);
            Blackboard.Initialize(profile, spec, collection);
            Brain.Initialize(profile);
            return true;
        }

        public bool IsOwnerValid()
        {
            if (Owner != null && Owner.IsInWorld && Owner.IsSimulated 
                && Owner.TestStatus(EntityStatus.PendingDestroy) == false 
                && Owner.TestStatus(EntityStatus.Destroyed) == false)
                return true;
            
            return false;
        }

        public bool GetDesiredIsWalkingState(MovementSpeedOverride speedOverride)
        {
            var locomotor = Owner?.Locomotor;            
            if (locomotor != null && locomotor.SupportsWalking)
            {
                if (speedOverride == MovementSpeedOverride.Walk)               
                    return true;
                 else if (speedOverride == MovementSpeedOverride.Default) 
                    return Senses.GetCurrentTarget() == null;
            }
            else
            {
                if (speedOverride == MovementSpeedOverride.Walk)
                    Logger.Warn("An AI agent's behavior has a movement context with a MovementSpeed of Walk, " +
                        $"but the agent's Locomotor doesn't support Walking.\nAgent [{Owner}]");
            }            
            return false;
        }
        
        private WorldEntity GetInteractEntityHelper()
        {
            ulong interactEntityId = Blackboard.PropertyCollection[PropertyEnum.AIInteractEntityId];
            WorldEntity interactedEntity = Game.EntityManager.GetEntity<WorldEntity>(interactEntityId);
            return interactedEntity;
        }

        private WorldEntity GetAssistedEntityHelper()
        {
            if (Game == null) return null;
            ulong assistedEntityId = Blackboard.PropertyCollection[PropertyEnum.AIAssistedEntityID];
            if (assistedEntityId == 0) return null;

            Entity entity = Game.EntityManager.GetEntity<Entity>(assistedEntityId);
            if (entity == null) return null;
            if (entity is Player player) return player.CurrentAvatar;
            if (entity is not WorldEntity assistedEntity)
            {
                Logger.Warn($"Assisted entity [{entity}] is not a WorldEntity.");
                return null;
            }
            return assistedEntity;
        }

        public float AggroRangeAlly 
        { 
            get => 
                Blackboard.PropertyCollection.HasProperty(PropertyEnum.AIAggroRangeOverrideAlly) ?
                Blackboard.PropertyCollection[PropertyEnum.AIAggroRangeOverrideAlly] :
                Blackboard.PropertyCollection[PropertyEnum.AIAggroRangeAlly];
        }

        public float AggroRangeHostile 
        { 
            get => 
                Blackboard.PropertyCollection.HasProperty(PropertyEnum.AIAggroRangeOverrideHostile) ?
                Blackboard.PropertyCollection[PropertyEnum.AIAggroRangeOverrideHostile] : 
                Blackboard.PropertyCollection[PropertyEnum.AIAggroRangeHostile]; 
        }

        public void OnAIActivated()
        {
            OnAIEnabled();
        }

        public void OnAIEnteredWorld()
        {
            OnAIEnabled();
        }

        public void OnAIAllianceChange()
        {
            ResetCurrentTargetState();
        }

        private void ScheduleAIThinkEvent(TimeSpan timeOffset, bool useGlobalThinkVariance, bool ignoreActivePower = false)
        {
            if (Game == null || Owner == null) return;
            if (IsEnabled == false
                || Owner.TestStatus(EntityStatus.PendingDestroy) 
                || Owner.TestStatus(EntityStatus.Destroyed) 
                || !Owner.IsInWorld || Owner.IsDead) return;

            if (!ignoreActivePower && Owner.IsExecutingPower)
            {
                Power activePower = Owner.ActivePower;
                if (activePower == null || activePower.IsChannelingPower == false) return;
            }

            GameEventScheduler eventScheduler = Game.GameEventScheduler;
            if (eventScheduler == null) return;
            TimeSpan fixedTimeOffset = timeOffset;

            if (useGlobalThinkVariance && Blackboard.PropertyCollection[PropertyEnum.AIUseGlobalThinkVariance])
            {
                AIGlobalsPrototype aiGlobalsPrototype = GameDatabase.AIGlobalsPrototype;
                if (aiGlobalsPrototype == null) return;
                int thinkVariance = aiGlobalsPrototype.RandomThinkVarianceMS;
                fixedTimeOffset += TimeSpan.FromMilliseconds(Game.Random.Next(0, thinkVariance));
            }

            if (_thinkEvent.IsValid() && Game.GetCurrentTime() + timeOffset < _thinkEvent.FireTime)
            {
                if (HasNotExceededMaxThinksPerFrame(timeOffset))
                    eventScheduler.RescheduleEvent(_thinkEvent, fixedTimeOffset);
            }
            else if (!_thinkEvent.IsValid())
            {
                TimeSpan nextThinkTimeOffset = fixedTimeOffset;

                if (HasNotExceededMaxThinksPerFrame(timeOffset) == false)
                    nextThinkTimeOffset = TimeSpan.FromMilliseconds(Math.Max(100, (int)nextThinkTimeOffset.TotalMilliseconds));

                if (nextThinkTimeOffset != TimeSpan.Zero)
                    nextThinkTimeOffset += Clock.Max(Game.RealGameTime - Game.GetCurrentTime(), TimeSpan.Zero);

                if (nextThinkTimeOffset < TimeSpan.Zero)
                {
                    Logger.Warn($"Agent tried to schedule a negative think time of {nextThinkTimeOffset.TotalMilliseconds}MS!\n  Agent: {Owner}");
                    nextThinkTimeOffset = TimeSpan.Zero;
                }

                eventScheduler.ScheduleEvent(_thinkEvent, nextThinkTimeOffset, _pendingEvents);
                _thinkEvent.OwnerController = this;
            }
        }

        private void ClearScheduledThinkEvent()
        {
            if (Game == null) return;            
            GameEventScheduler eventScheduler = Game.GameEventScheduler;
            if (eventScheduler == null) return;
            eventScheduler.CancelEvent(_thinkEvent);
        }

        public void SetIsEnabled(bool enabled)
        {
            IsEnabled = enabled;
            if (enabled)
                OnAIEnabled();
            else
                OnAIDisabled();
        }

        private void OnAIEnabled()
        {
            ScheduleAIThinkEvent(TimeSpan.FromMilliseconds(0), true, false);
        }        

        private void OnAIDisabled()
        {
            ClearScheduledThinkEvent();
        }

        public void OnAIExitedWorld()
        {
            OnAIDisabled();
            Brain?.OnOwnerExitWorld();
        }

        public void ProcessInterrupts(BehaviorInterruptType interrupt)
        {
            Brain?.ProcessInterrupts(interrupt);
        }

        private bool HasNotExceededMaxThinksPerFrame(TimeSpan timeOffset)
        {
            if (Game == null || Brain == null) return false;

            if (timeOffset == TimeSpan.Zero && Brain.LastThinkQTime == Game.NumQuantumFixedTimeUpdates && Brain.ThinkCountPerFrame > 4)
            {
                Logger.Warn($"Tried to schedule too many thinks on the same frame. Frame: {Game.NumQuantumFixedTimeUpdates}, " +
                    $"Agent: {Owner}, Think count: {Brain?.ThinkCountPerFrame}");
                return false;
            }

            return true;
        }

        internal void AddPowersToPicker(Picker<ProceduralUsePowerContextPrototype> powerPicker, ProceduralUsePowerContextPrototype[] genericProceduralPowers)
        {
            throw new NotImplementedException();
        }

        internal void AddPowersToPicker(Picker<ProceduralUsePowerContextPrototype> powerPicker, ProceduralUsePowerContextPrototype primaryPower)
        {
            throw new NotImplementedException();
        }

        public void ResetCurrentTargetState()
        {
            SetTargetEntity(null);
            Senses.Interrupt = BehaviorInterruptType.NoTarget;
            var collection = Blackboard.PropertyCollection;
            collection.RemoveProperty(PropertyEnum.AINextSensoryUpdate);
            collection.RemoveProperty(PropertyEnum.AINextHostileSense);
            collection.RemoveProperty(PropertyEnum.AINextAllySense);
            collection.RemoveProperty(PropertyEnum.AINextItemSense);
        }

        public void SetTargetEntity(WorldEntity target)
        {
            throw new NotImplementedException();
        }

        internal void Think()
        {
            throw new NotImplementedException();
        }

        internal void OnAIKilled()
        {
            throw new NotImplementedException();
        }

        public void OnAIBehaviorChange()
        {
            if (IsEnabled)
                ScheduleAIThinkEvent(TimeSpan.FromMilliseconds(50), false);
            Blackboard.PropertyCollection.RemoveProperty(PropertyEnum.AINextSensoryUpdate);
        }
    }
}
