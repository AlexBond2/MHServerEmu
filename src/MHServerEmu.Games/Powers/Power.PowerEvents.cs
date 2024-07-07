﻿using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.System.Random;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Properties;

namespace MHServerEmu.Games.Powers
{
    public partial class Power
    {
        #region Event Handlers

        // Please keep these sorted by PowerEventType enum value

        public void HandleTriggerPowerEventOnContactTime()              // 1
        {
            PowerActivationSettings settings = _lastActivationSettings;
            settings.TriggeringPowerPrototypeRef = PrototypeDataRef;
            HandleTriggerPowerEvent(PowerEventType.OnContactTime, in settings);
        }

        public void HandleTriggerPowerEventOnCriticalHit()              // 2
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnHitKeyword()               // 3
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnPowerApply()               // 4
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnPowerEnd()                 // 5
        {
            PowerActivationSettings settings = _lastActivationSettings;
            settings.TriggeringPowerPrototypeRef = PrototypeDataRef;
            HandleTriggerPowerEvent(PowerEventType.OnPowerEnd, in settings);
        }

        public void HandleTriggerPowerEventOnPowerHit()                 // 6
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnPowerStart()               // 7
        {
            PowerActivationSettings settings = _lastActivationSettings;
            settings.TriggeringPowerPrototypeRef = PrototypeDataRef;
            HandleTriggerPowerEvent(PowerEventType.OnPowerStart, in settings);
        }

        public void HandleTriggerPowerEventOnProjectileHit()            // 8
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnStackCount()               // 9
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnTargetKill()               // 10
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnSummonEntity()             // 11
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnHoldBegin()                // 12
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnMissileHit()               // 13
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnMissileKilled()            // 14
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnHotspotNegated()           // 15
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnHotspotNegatedByOther()    // 16
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnHotspotOverlapBegin()      // 17
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnHotspotOverlapEnd()        // 18
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnRemoveCondition()          // 19
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnRemoveNegStatusEffect()    // 20
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnPowerPivot()               // 21
        {
            // Client-only?
            Logger.Debug("HandleTriggerPowerEventOnPowerPivot()");
            PowerActivationSettings settings = _lastActivationSettings;
            settings.TriggeringPowerPrototypeRef = PrototypeDataRef;
            settings.Flags |= PowerActivationSettingsFlags.ClientCombo;
            HandleTriggerPowerEvent(PowerEventType.OnPowerPivot, in settings);
        }

        public void HandleTriggerPowerEventOnPowerToggleOn()            // 22
        {
            PowerActivationSettings settings = _lastActivationSettings;
            settings.TriggeringPowerPrototypeRef = PrototypeDataRef;
            HandleTriggerPowerEvent(PowerEventType.OnPowerToggleOn, in settings);
        }

        public void HandleTriggerPowerEventOnPowerToggleOff()           // 23
        {
            PowerActivationSettings settings = _lastActivationSettings;
            settings.TriggeringPowerPrototypeRef = PrototypeDataRef;
            HandleTriggerPowerEvent(PowerEventType.OnPowerToggleOff, in settings);
        }

        public bool HandleTriggerPowerEventOnPowerStopped(EndPowerFlags flags)             // 24
        {
            // This event's handling does its own thing
            PowerPrototype powerProto = Prototype;
            if (powerProto == null) return Logger.WarnReturn(false, "HandleTriggerPowerEventOnPowerStopped(): powerProto == null");
            if (Owner == null) return Logger.WarnReturn(false, "HandleTriggerPowerEventOnPowerStopped(): Owner == null");
            if (Game == null) return Logger.WarnReturn(false, "HandleTriggerPowerEventOnPowerStopped(): Game == null");

            // Nothing to trigger
            if (powerProto.ActionsTriggeredOnPowerEvent.IsNullOrEmpty())
                return true;

            PowerActivationSettings settings = _lastActivationSettings;
            settings.TriggeringPowerPrototypeRef = PrototypeDataRef;

            foreach (PowerEventActionPrototype triggeredPowerEvent in powerProto.ActionsTriggeredOnPowerEvent)
            {
                // Check event type / action combination
                if (triggeredPowerEvent.PowerEvent == PowerEventType.None)
                {
                    Logger.Warn($"HandleTriggerPowerEventOnPowerStopped(): This power contains a triggered power event action with a null event type \n[{this}]");
                    continue;
                }

                PowerEventActionType actionType = triggeredPowerEvent.EventAction;

                if (actionType == PowerEventActionType.None)
                {
                    Logger.Warn($"HandleTriggerPowerEventOnPowerStopped(): This power contains a triggered power event action with a null action type\n[{this}]");
                    continue;
                }

                if (triggeredPowerEvent.PowerEvent != PowerEventType.OnPowerStopped)
                    continue;

                switch (actionType)
                {
                    case PowerEventActionType.CancelScheduledActivationOnTriggeredPower:    DoPowerEventActionCancelScheduledActivation(triggeredPowerEvent, in settings); break;
                    case PowerEventActionType.EndPower:                                     DoPowerEventActionEndPower(triggeredPowerEvent.Power, flags); break;
                    case PowerEventActionType.CooldownStart:                                DoPowerEventActionCooldownStart(triggeredPowerEvent, in settings); break;
                    case PowerEventActionType.CooldownEnd:                                  DoPowerEventActionCooldownEnd(triggeredPowerEvent, in settings); break;
                    case PowerEventActionType.CooldownModifySecs:                           DoPowerEventActionCooldownModifySecs(triggeredPowerEvent, in settings); break;
                    case PowerEventActionType.CooldownModifyPct:                            DoPowerEventActionCooldownModifyPct(triggeredPowerEvent, in settings); break;

                    default: Logger.Warn($"HandleTriggerPowerEventOnPowerStopped(): Power [{this}] contains a triggered event with an unsupported action"); break;
                }
            }

            return true;
        }

        public void HandleTriggerPowerEventOnExtraActivationCooldown()  // 25
        {
            PowerActivationSettings settings = _lastActivationSettings;
            settings.TriggeringPowerPrototypeRef = PrototypeDataRef;
            HandleTriggerPowerEvent(PowerEventType.OnExtraActivationCooldown, in settings);
        }

        public void HandleTriggerPowerEventOnPowerLoopEnd()             // 26
        {
            PowerActivationSettings settings = _lastActivationSettings;
            settings.TriggeringPowerPrototypeRef = PrototypeDataRef;
            HandleTriggerPowerEvent(PowerEventType.OnPowerLoopEnd, in settings);
        }

        public void HandleTriggerPowerEventOnSpecializationPowerAssigned()      // 27
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnSpecializationPowerUnassigned()    // 28
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnEntityControlled()                 // 29
        {
            // not present in the client
        }

        public void HandleTriggerPowerEventOnOutOfRangeActivateMovementPower()  // 30
        {
            PowerActivationSettings settings = _lastActivationSettings;
            settings.TriggeringPowerPrototypeRef = PrototypeDataRef;
            HandleTriggerPowerEvent(PowerEventType.OnOutOfRangeActivateMovementPower, in settings);
        }

        #endregion

        private bool HandleTriggerPowerEvent(PowerEventType eventType, in PowerActivationSettings initialSettings,
            int comparisonParam = 0, MathComparisonType comparisonType = MathComparisonType.Invalid)
        {
            if (CanTriggerPowerEventType(eventType, in initialSettings) == false)
                return false;

            PowerPrototype powerProto = Prototype;
            if (powerProto == null) return Logger.WarnReturn(false, "HandleTriggerPowerEvent(): powerProto == null");
            if (Owner == null) return Logger.WarnReturn(false, "HandleTriggerPowerEvent(): Owner == null");
            if (Game == null) return Logger.WarnReturn(false, "HandleTriggerPowerEvent(): Game == null");

            // Early return for powers that don't have any triggered actions
            if (powerProto.ActionsTriggeredOnPowerEvent.IsNullOrEmpty())
                return true;

            WorldEntity target = Game.EntityManager.GetEntity<WorldEntity>(initialSettings.TargetEntityId);
            GRandom random = new((int)initialSettings.PowerRandomSeed);

            // Check all actions defined for this event type
            foreach (PowerEventActionPrototype triggeredPowerEvent in powerProto.ActionsTriggeredOnPowerEvent)
            {
                // Check event type / action combination
                if (triggeredPowerEvent.PowerEvent == PowerEventType.None)
                {
                    Logger.Warn($"HandleTriggerPowerEvent(): This power contains a triggered power event action with a null event type \n[{this}]");
                    continue;
                }

                PowerEventActionType actionType = triggeredPowerEvent.EventAction;

                if (actionType == PowerEventActionType.None)
                {
                    Logger.Warn($"HandleTriggerPowerEvent(): This power contains a triggered power event action with a null action type\n[{this}]");
                    continue;
                }

                if (eventType != triggeredPowerEvent.PowerEvent)
                    continue;

                if (CanTriggerPowerEventAction(eventType, actionType) == false)
                    continue;

                // Copy settings and generate a new seed
                PowerActivationSettings newSettings = initialSettings;
                newSettings.PowerRandomSeed = (uint)random.Next(1, 10000);

                // Run trigger chance check
                float eventTriggerChance = triggeredPowerEvent.GetEventTriggerChance(Properties, Owner, target);
                if (random.NextFloat() >= eventTriggerChance)
                    continue;

                // Run param comparison if needed
                if (comparisonType != MathComparisonType.Invalid)
                {
                    float eventParam = triggeredPowerEvent.GetEventParam(Properties, Owner);
                    switch (comparisonType)
                    {
                        case MathComparisonType.Equals:
                            if (comparisonParam != eventParam)
                                continue;
                            break;

                        case MathComparisonType.GreaterThan:
                            if (comparisonParam <= eventParam)
                                continue;
                            break;

                        case MathComparisonType.LessThan:
                            if (comparisonParam >= eventParam)
                                continue;
                            break;
                    }
                }

                // Do the action for this event
                switch (actionType)
                {
                    case PowerEventActionType.BodySlide:                                    DoPowerEventActionBodyslide(); break;
                    case PowerEventActionType.CancelScheduledActivation:
                    case PowerEventActionType.CancelScheduledActivationOnTriggeredPower:    DoPowerEventActionCancelScheduledActivation(triggeredPowerEvent, in newSettings); break;
                    case PowerEventActionType.ContextCallback:                              DoPowerEventActionContextCallback(triggeredPowerEvent, in newSettings); break;
                    case PowerEventActionType.DespawnTarget:                                DoPowerEventActionDespawnTarget(triggeredPowerEvent, in newSettings); break;
                    case PowerEventActionType.ChargesIncrement:                             DoPowerEventActionChargesIncrement(triggeredPowerEvent, in newSettings); break;
                    case PowerEventActionType.InteractFinish:                               DoPowerEventActionInteractFinish(); break;
                    case PowerEventActionType.RestoreThrowable:                             DoPowerEventActionRestoreThrowable(in newSettings); break;
                    case PowerEventActionType.RescheduleActivationInSeconds:
                    case PowerEventActionType.ScheduleActivationAtPercent:
                    case PowerEventActionType.ScheduleActivationInSeconds:                  DoPowerEventActionScheduleActivation(triggeredPowerEvent, in newSettings, actionType); break;
                    case PowerEventActionType.ShowBannerMessage:                            DoPowerEventActionShowBannerMessage(triggeredPowerEvent, in newSettings); break;
                    case PowerEventActionType.SpawnLootTable:                               DoPowerEventActionSpawnLootTable(triggeredPowerEvent, in newSettings); break;
                    case PowerEventActionType.SwitchAvatar:                                 DoPowerEventActionSwitchAvatar(); break;
                    case PowerEventActionType.ToggleOnPower:
                    case PowerEventActionType.ToggleOffPower:                               DoPowerEventActionTogglePower(triggeredPowerEvent, in newSettings, actionType); break;
                    case PowerEventActionType.TransformModeChange:                          DoPowerEventActionTransformModeChange(triggeredPowerEvent); break;
                    case PowerEventActionType.TransformModeStart:                           DoPowerEventActionTransformModeStart(triggeredPowerEvent, in newSettings); break;
                    case PowerEventActionType.UsePower:                                     DoPowerEventActionUsePower(triggeredPowerEvent, in newSettings); break;
                    case PowerEventActionType.TeleportToPartyMember:                        DoPowerEventActionTeleportToPartyMember(); break;
                    case PowerEventActionType.ControlAgentAI:                               DoPowerEventActionControlAgentAI(newSettings.TargetEntityId); break;
                    case PowerEventActionType.RemoveAndKillControlledAgentsFromInv:         DoPowerEventActionRemoveAndKillControlledAgentsFromInv(); break;
                    case PowerEventActionType.EndPower:                                     DoPowerEventActionEndPower(triggeredPowerEvent.Power, EndPowerFlags.ExplicitCancel | EndPowerFlags.PowerEventAction); break;
                    case PowerEventActionType.CooldownStart:                                DoPowerEventActionCooldownStart(triggeredPowerEvent, in newSettings); break;
                    case PowerEventActionType.CooldownEnd:                                  DoPowerEventActionCooldownEnd(triggeredPowerEvent, in newSettings); break;
                    case PowerEventActionType.CooldownModifySecs:                           DoPowerEventActionCooldownModifySecs(triggeredPowerEvent, in newSettings); break;
                    case PowerEventActionType.CooldownModifyPct:                            DoPowerEventActionCooldownModifyPct(triggeredPowerEvent, in newSettings); break;
                    case PowerEventActionType.TeamUpAgentSummon:                            DoPowerEventActionTeamUpAgentSummon(triggeredPowerEvent); break;
                    case PowerEventActionType.TeleportToRegion:                             DoPowerEventActionTeleportRegion(triggeredPowerEvent, in newSettings); break;
                    case PowerEventActionType.StealPower:                                   DoPowerEventActionStealPower(newSettings.TargetEntityId); break;
                    case PowerEventActionType.PetItemDonate:                                DoPowerEventActionPetItemDonate(triggeredPowerEvent); break;
                    case PowerEventActionType.MapPowers:                                    DoPowerEventActionMapPowers(triggeredPowerEvent); break;
                    case PowerEventActionType.UnassignMappedPowers:                         DoPowerEventActionUnassignMappedPowers(triggeredPowerEvent); break;
                    case PowerEventActionType.RemoveSummonedAgentsWithKeywords:             DoPowerEventActionRemoveSummonedAgentsWithKeywords(triggeredPowerEvent, in newSettings); break;
                    case PowerEventActionType.SpawnControlledAgentWithSummonDuration:       DoPowerEventActionSummonControlledAgentWithDuration(); break;
                    case PowerEventActionType.LocalCoopEnd:                                 DoPowerEventActionLocalCoopEnd(); break;

                    default: Logger.Warn($"HandleTriggerPowerEvent(): Power [{this}] contains a triggered event with an unsupported action"); break;
                }
            }

            return true;
        }

        private bool CanTriggerPowerEventType(PowerEventType eventType, in PowerActivationSettings settings)
        {
            // TODO: Recheck this when we have a proper PowerEffectsPacket / PowerResults implementation
            if (settings.PowerResults != null && settings.PowerResults.TargetEntityId != Entity.InvalidId)
            {
                WorldEntity target = Game.EntityManager.GetEntity<WorldEntity>(settings.PowerResults.TargetEntityId);
                if (target != null && target.Properties[PropertyEnum.DontTriggerOtherPowerEvents, (int)eventType])
                    return false;
            }

            return true;
        }

        private bool CanTriggerPowerEventAction(PowerEventType eventType, PowerEventActionType actionType)
        {
            if (actionType == PowerEventActionType.EndPower)
            {
                if (eventType != PowerEventType.OnPowerEnd && eventType != PowerEventType.OnPowerLoopEnd)
                {
                    return Logger.WarnReturn(false,
                        $"CanTriggerPowerEventAction(): Power [{this}] contains an unsupported triggered event/action combination: event=[{eventType}] action=[{actionType}]");
                }
            }

            return true;
        }

        private bool DoActivateComboPower(Power triggeredPower, PowerEventActionPrototype triggeredPowerEvent, in PowerActivationSettings settings)
        {
            // Activate combo power - a power triggered by a power event action
            Logger.Debug($"DoActivateComboPower(): {triggeredPower.Prototype}");
            return true;
        }

        #region Event Actions

        // Please keep these ordered by PowerEventActionType enum value

        // 1
        private void DoPowerEventActionBodyslide()
        {
            Logger.Warn($"DoPowerEventActionBodyslide(): Not implemented");
        }

        // 2, 3
        private void DoPowerEventActionCancelScheduledActivation(PowerEventActionPrototype triggeredPowerEvent, in PowerActivationSettings settings)
        {
            Logger.Warn($"DoPowerEventActionCancelScheduledActivation(): Not implemented");
        }

        // 4
        private void DoPowerEventActionContextCallback(PowerEventActionPrototype triggeredPowerEvent, in PowerActivationSettings settings)
        {
            Logger.Warn($"DoPowerEventActionContextCallback(): Not implemented");
        }

        // 5
        private void DoPowerEventActionDespawnTarget(PowerEventActionPrototype triggeredPowerEvent, in PowerActivationSettings settings)
        {
            Logger.Warn($"DoPowerEventActionDespawnTarget(): Not implemented");
        }

        // 6
        private void DoPowerEventActionChargesIncrement(PowerEventActionPrototype triggeredPowerEvent, in PowerActivationSettings settings)
        {
            Logger.Warn($"DoPowerEventActionChargesIncrement(): Not implemented");
        }

        // 7
        private void DoPowerEventActionInteractFinish()             
        {
            Logger.Warn($"DoPowerEventActionInteractFinish(): Not implemented");
        }

        // 9
        private void DoPowerEventActionRestoreThrowable(in PowerActivationSettings settings)
        {
            Logger.Warn($"DoPowerEventActionRestoreThrowable(): Not implemented");
        }

        // 8, 10, 11
        private void DoPowerEventActionScheduleActivation(PowerEventActionPrototype triggeredPowerEvent, in PowerActivationSettings settings, PowerEventActionType actionType)
        {
            Logger.Warn($"DoPowerEventActionScheduleActivation(): Not implemented");
        }

        // 12
        private void DoPowerEventActionShowBannerMessage(PowerEventActionPrototype triggeredPowerEvent, in PowerActivationSettings settings)
        {
            Logger.Warn($"DoPowerEventActionShowBannerMessage(): Not implemented");
        }

        // 13
        private void DoPowerEventActionSpawnLootTable(PowerEventActionPrototype triggeredPowerEvent, in PowerActivationSettings settings)
        {
            Logger.Warn($"DoPowerEventActionSpawnLootTable(): Not implemented");
        }

        // 14
        private void DoPowerEventActionSwitchAvatar()
        {
            Logger.Warn($"DoPowerEventActionSwitchAvatar(): Not implemented");
        }

        // 15, 16
        private void DoPowerEventActionTogglePower(PowerEventActionPrototype triggeredPowerEvent, in PowerActivationSettings settings, PowerEventActionType actionType)
        {
            Logger.Warn($"DoPowerEventActionTogglePower(): Not implemented");
        }

        // 17
        private void DoPowerEventActionTransformModeChange(PowerEventActionPrototype triggeredPowerEvent)
        {
            Logger.Warn($"DoPowerEventActionTransformModeChange(): Not implemented");
        }

        // 18
        private void DoPowerEventActionTransformModeStart(PowerEventActionPrototype triggeredPowerEvent, in PowerActivationSettings settings)
        {
            Logger.Warn($"DoPowerEventActionTransformModeStart(): Not implemented");
        }

        // 19
        private void DoPowerEventActionUsePower(PowerEventActionPrototype triggeredPowerEvent, in PowerActivationSettings settings)
        {
            Logger.Warn($"DoPowerEventActionUsePower(): Not implemented ({triggeredPowerEvent.Power.GetName()})");
        }

        // 20
        private void DoPowerEventActionTeleportToPartyMember()
        {
            Logger.Warn($"DoPowerEventActionTeleportToPartyMember(): Not implemented");
        }

        // 21
        private void DoPowerEventActionControlAgentAI(ulong targetId)
        {
            Logger.Warn($"DoPowerEventActionControlAgentAI(): Not implemented");
        }

        // 22
        private void DoPowerEventActionRemoveAndKillControlledAgentsFromInv()
        {
            Logger.Warn($"DoPowerEventActionRemoveAndKillControlledAgentsFromInv(): Not implemented");
        }

        // 23
        private void DoPowerEventActionEndPower(PrototypeId powerProtoRef, EndPowerFlags flags)
        {
            Logger.Warn($"DoPowerEventActionEndPower(): Not implemented (powerProtoRef={powerProtoRef.GetName()}, flags={flags})");
        }

        // 24
        private void DoPowerEventActionCooldownStart(PowerEventActionPrototype triggeredPowerEvent, in PowerActivationSettings settings)
        {
            Logger.Warn($"DoPowerEventActionCooldownStart(): Not implemented");
        }

        // 25
        private void DoPowerEventActionCooldownEnd(PowerEventActionPrototype triggeredPowerEvent, in PowerActivationSettings settings)
        {
            Logger.Warn($"DoPowerEventActionCooldownEnd(): Not implemented");
        }

        // 26
        private void DoPowerEventActionCooldownModifySecs(PowerEventActionPrototype triggeredPowerEvent, in PowerActivationSettings settings)
        {
            Logger.Warn($"DoPowerEventActionCooldownModifySecs(): Not implemented");
        }

        // 27
        private void DoPowerEventActionCooldownModifyPct(PowerEventActionPrototype triggeredPowerEvent, in PowerActivationSettings settings)
        {
            Logger.Warn($"DoPowerEventActionCooldownModifyPct(): Not implemented");
        }

        // 28
        private void DoPowerEventActionTeamUpAgentSummon(PowerEventActionPrototype triggeredPowerEvent)
        {
            Logger.Warn($"DoPowerEventActionTeamUpAgentSummon(): Not implemented");
        }

        // 29
        private void DoPowerEventActionTeleportRegion(PowerEventActionPrototype triggeredPowerEvent, in PowerActivationSettings settings)
        {
            Logger.Warn($"DoPowerEventActionTeleportRegion(): Not implemented");
        }

        // 30
        private void DoPowerEventActionStealPower(ulong targetId)
        {
            Logger.Warn($"DoPowerEventActionStealPower(): Not implemented");
        }

        // 31
        private void DoPowerEventActionPetItemDonate(PowerEventActionPrototype triggeredPowerEvent)
        {
            Logger.Warn($"DoPowerEventActionPetItemDonate(): Not implemented");
        }

        // 32
        private void DoPowerEventActionMapPowers(PowerEventActionPrototype triggeredPowerEvent)
        {
            Logger.Warn($"DoPowerEventActionMapPowers(): Not implemented");
        }

        // 33
        private void DoPowerEventActionUnassignMappedPowers(PowerEventActionPrototype triggeredPowerEvent)
        {
            Logger.Warn($"DoPowerEventActionUnassignMappedPowers(): Not implemented");
        }

        // 34
        private void DoPowerEventActionRemoveSummonedAgentsWithKeywords(PowerEventActionPrototype triggeredPowerEvent, in PowerActivationSettings settings)
        {
            Logger.Warn($"DoPowerEventActionRemoveSummonedAgentsWithKeywords(): Not implemented");
        }

        // 35
        private void DoPowerEventActionSummonControlledAgentWithDuration()
        {
            Logger.Warn($"DoPowerEventActionSummonControlledAgentWithDuration(): Not implemented");
        }

        // 36
        private void DoPowerEventActionLocalCoopEnd()
        {
            Logger.Warn($"DoPowerEventActionLocalCoopEnd(): Not implemented");
        }

        #endregion
    }
}