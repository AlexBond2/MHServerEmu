using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Regions;
using MHServerEmu.Games.GameData;

namespace MHServerEmu.Games.Missions.Conditions
{
    public class MissionConditionHotspotEnter : MissionPlayerCondition
    {
        private MissionConditionHotspotEnterPrototype _proto;
        private Action<EntityEnteredMissionHotspotGameEvent> _entityEnteredMissionHotspotAction;

        public MissionConditionHotspotEnter(Mission mission, IMissionConditionOwner owner, MissionConditionPrototype prototype) 
            : base(mission, owner, prototype)
        {
            // CH00NPETrainingRoom
            _proto = prototype as MissionConditionHotspotEnterPrototype;
            _entityEnteredMissionHotspotAction = OnEntityEnteredMissionHotspot;
        }

        public override bool OnReset()
        {
            var missionRef = Mission.PrototypeDataRef;
            bool entered = false;
            if (_proto.TargetFilter != null)
            {
                foreach (var hotspot in Mission.GetMissionHotspots())
                    if (EvaluateEntityFilter(_proto.EntityFilter, hotspot)
                        && hotspot.GetMissionConditionCount(missionRef, _proto) > 0)
                    {
                        entered = true;
                        break;
                    }
            }
            else
            {
                foreach (var player in Mission.GetParticipants())
                {
                    var avatar = player.CurrentAvatar;
                    if (avatar != null)
                        if (Mission.FilterHotspots(avatar, PrototypeId.Invalid, _proto.EntityFilter))
                        {
                            entered = true;
                            break;
                        }
                }
            }

            SetCompletion(entered);
            return true;
        }

        private bool EvaluateEntity(WorldEntity entity, Hotspot hotspot)
        {
            if (hotspot == null) return false;
            if (EvaluateEntityFilter(_proto.EntityFilter, hotspot) == false) return false;

            if (_proto.TargetFilter != null)
            {
                if (entity == null) return false;
                if (EvaluateEntityFilter(_proto.TargetFilter, entity) == false) return false;
            }
            else
            {
                if (entity is not Avatar avatar) return false;
                var player = avatar.GetOwnerOfType<Player>();
                if (player == null || IsMissionPlayer(player) == false) return false;
            }

            return true;
        }

        private void OnEntityEnteredMissionHotspot(EntityEnteredMissionHotspotGameEvent evt)
        {
            var entity = evt.Target;
            var hotspot = evt.Hotspot;

            if (EvaluateEntity(entity, hotspot) == false) return;
            
            if (entity is Avatar avatar)
            {
                var player = avatar.GetOwnerOfType<Player>();
                if (player != null)
                {
                    Mission.AddParticipant(player);
                    UpdatePlayerContribution(player);
                }
            }
            SetCompleted();            
        }

        public override void RegisterEvents(Region region)
        {
            EventsRegistered = true;
            region.EntityEnteredMissionHotspotEvent.AddActionBack(_entityEnteredMissionHotspotAction);
        }

        public override void UnRegisterEvents(Region region)
        {
            EventsRegistered = false;
            region.EntityEnteredMissionHotspotEvent.RemoveAction(_entityEnteredMissionHotspotAction);
        }
    }
}
