﻿using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Serialization;
using MHServerEmu.Core.System.Random;
using MHServerEmu.Games.Common;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Events;
using MHServerEmu.Games.Events.Templates;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.MetaGames.GameModes;
using MHServerEmu.Games.MetaGames.MetaStates;
using MHServerEmu.Games.Missions;
using MHServerEmu.Games.Network;
using MHServerEmu.Games.Populations;
using MHServerEmu.Games.Properties;
using MHServerEmu.Games.Properties.Evals;
using MHServerEmu.Games.Regions;
using MHServerEmu.Games.UI.Widgets;
using System.Text;

namespace MHServerEmu.Games.MetaGames
{
    public class MetaGame : Entity
    {
        public static readonly Logger Logger = LogManager.CreateLogger();

        protected RepString _name;
        protected ulong _regionId;

        public Region Region { get => GetRegion(); }
        public MetaGamePrototype MetaGamePrototype { get => Prototype as MetaGamePrototype; }
        public List<MetaState> MetaStates { get; }
        protected List<MetaGameTeam> Teams { get; }
        protected List<MetaGameMode> GameModes { get; }
        public GRandom Random { get; }
        public MetaGameMode CurrentMode => (_modeIndex > -1 && _modeIndex < GameModes.Count) ? GameModes[_modeIndex] : null;

        private readonly HashSet<ulong> _discoveredEntities = new();
        private readonly Queue<ApplyStateRecord> _applyStateStack = new();
        private readonly Queue<PrototypeId> _removeStateStack = new();
        private readonly EventPointer<ApplyStateEvent> _scheduledApplyState = new();
        private readonly EventPointer<ActivateGameModeEvent> _scheduledActivateGameMode = new();

        private Dictionary<PrototypeId, MetaStateSpawnEvent> _metaStateSpawnEvents;
        private Action<PlayerEnteredRegionGameEvent> _playerEnteredRegionAction;
        private Action<EntityEnteredWorldGameEvent> _entityEnteredWorldAction;
        private Action<EntityExitedWorldGameEvent> _entityExitedWorldAction;
        private Action<PlayerRegionChangeGameEvent> _playerRegionChangeAction;
        private Action<Entity> _destroyEntityAction;
        private int _modeIndex;

        public MetaGame(Game game) : base(game) 
        {
            MetaStates = new();
            GameModes = new();
            Teams = new();
            Random = new();
            _name = new();
            _modeIndex = -1;
            _playerEnteredRegionAction = OnPlayerEnteredRegion;
            _entityEnteredWorldAction = OnEntityEnteredWorld;
            _entityExitedWorldAction = OnEntityExitedWorld;
            _playerRegionChangeAction = OnPlayerRegionChange;
            _destroyEntityAction = OnDestroyEntity;
        }

        public override bool Initialize(EntitySettings settings)
        {
            base.Initialize(settings);

            var game = Game;
            if (game == null) return false;

            var region = Game.RegionManager?.GetRegion(settings.RegionId);
            if (region != null)
            {
                _metaStateSpawnEvents = new();
                _regionId = region.Id;
                region.RegisterMetaGame(this);
                region.PlayerEnteredRegionEvent.AddActionBack(_playerEnteredRegionAction);
                region.EntityEnteredWorldEvent.AddActionBack(_entityEnteredWorldAction);
                region.EntityExitedWorldEvent.AddActionBack(_entityExitedWorldAction);

                foreach (var kvp in region.Properties.IteratePropertyRange(PropertyEnum.MetaStateApplyOnInit))
                {
                    Property.FromParam(kvp.Key, 0, out PrototypeId stateRef);
                    if (stateRef != PrototypeId.Invalid)
                        ApplyMetaState(stateRef);
                }
            }
            else
            {
                Logger.Warn("Initialize(): region == null");
            }

            Game.EntityManager?.DestroyEntityEvent.AddActionBack(_destroyEntityAction);

            return true;
        }

        public override bool Serialize(Archive archive)
        {
            bool success = base.Serialize(archive);
            // if (archive.IsTransient)
            success &= Serializer.Transfer(archive, ref _name);
            return success;
        }

        public override void Destroy()
        {
            foreach (var mode in GameModes) mode.OnDestroy();
            foreach (var state in MetaStates) state.OnRemove();

            var region = Region;
            if (region != null)
            {
                region.PlayerRegionChangeEvent.RemoveAction(_playerRegionChangeAction);
                region.PlayerEnteredRegionEvent.RemoveAction(_playerEnteredRegionAction);
                region.EntityEnteredWorldEvent.RemoveAction(_entityEnteredWorldAction);
                region.EntityExitedWorldEvent.RemoveAction(_entityExitedWorldAction);
                region.UnRegisterMetaGame(this);
            }
            Game.EntityManager?.DestroyEntityEvent.RemoveAction(_destroyEntityAction);

            foreach(var team in Teams)
            {
                team.ClearPlayers();
                DestroyTeam(team);
            }
            Teams.Clear();

            base.Destroy();
        }

        public void CreateTeams(PrototypeId[] teams)
        {
            if (teams.HasValue())
            {
                foreach(var teamRef in teams)
                {
                    var team = CreateTeam(teamRef);
                    Teams.Add(team);
                }
            }
            else
            {
                var globalsProto = GameDatabase.GlobalsPrototype;
                if (globalsProto == null) return;
                var team = CreateTeam(globalsProto.MetaGameTeamDefault);
                Teams.Add(team);
            }
        }

        public MetaGameTeam CreateTeam(PrototypeId teamRef)
        {
            var teamProto = GameDatabase.GetPrototype<MetaGameTeamPrototype>(teamRef);
            if (teamProto == null) return null;
            return new MetaGameTeam(this, teamRef, teamProto.MaxPlayers);
        }

        private void DestroyTeam(MetaGameTeam team)
        {
            team.Destroy();
        }

        public Region GetRegion()
        {
            if (_regionId == 0) return null;
            return Game.RegionManager.GetRegion(_regionId);
        }

        protected override void BindReplicatedFields()
        {
            base.BindReplicatedFields();

            _name.Bind(this, AOINetworkPolicyValues.AOIChannelProximity);
        }

        protected override void UnbindReplicatedFields()
        {
            base.UnbindReplicatedFields();

            _name.Unbind();
        }

        protected override void BuildString(StringBuilder sb)
        {
            base.BuildString(sb);

            sb.AppendLine($"{nameof(_name)}: {_name}");
        }

        #region GameMode

        protected void CreateGameModes(PrototypeId[] gameModes)
        {
            if (gameModes.HasValue())
                foreach (var gameModeRef in gameModes)
                {
                    var gameMode = MetaGameMode.CreateGameMode(this, gameModeRef);
                    GameModes.Add(gameMode);
                }
        }

        public void ActivateGameMode(int index)
        {
            var proto = MetaGamePrototype;
            var region = Region;
            if (proto == null || region == null) return;

            if (index < 0 || index >= GameModes.Count) return;
            var mode = GameModes[index];
            var modeProto = mode?.Prototype;
            if (modeProto == null) return;

            // deactivate old mode
            CurrentMode?.OnDeactivate();

            // TODO modeProto.EventHandler
            // TODO lock for proto.SoftLockRegionMode

            _modeIndex = index;
            Random.Seed(region.RandomSeed + index);

            // activate new mode
            mode.OnActivate();

            foreach (var player in new PlayerIterator(region))
                player.Properties[PropertyEnum.PvPMode] = modeProto.DataRef;
        }

        public void ScheduleActivateGameMode(PrototypeId modeRef)
        {
            if (modeRef == PrototypeId.Invalid) return;
            var index = GameModes.FindIndex(mode => mode.PrototypeDataRef == modeRef);
            if (index != -1) ScheduleActivateGameMode(index);
        }

        public void ScheduleActivateGameMode(int index)
        {
            if (index < 0 || index >= GameModes.Count || _modeIndex == index) return;
            if (_scheduledActivateGameMode.IsValid == false)
                ScheduleEntityEvent(_scheduledActivateGameMode, TimeSpan.Zero, index);
        }

        private void OnActivateGameMode(int index)
        {
            if (_modeIndex != index) ActivateGameMode(index);
        }

        #endregion

        #region MetaState

        public void ApplyStates(PrototypeId[] states)
        {
            if (states.IsNullOrEmpty()) return;
            foreach(var stateRef in states)
                ApplyMetaState(stateRef);
        }

        private bool CanApplyState(PrototypeId stateRef, bool skipCooldown = false)
        {
            if (stateRef == PrototypeId.Invalid) return false;
            var stateProto = GameDatabase.GetPrototype<MetaStatePrototype>(stateRef);
            if (stateProto == null || stateProto.CanApplyState() == false) return false;

            if (skipCooldown == false && stateProto.CooldownMS > 0)
            {
                TimeSpan time = Game.CurrentTime - Properties[PropertyEnum.MetaGameTimeStateRemovedMS, stateRef];
                if (time < TimeSpan.FromMilliseconds(stateProto.CooldownMS)) return false;
            }

            bool hasPreventStates = stateProto.PreventStates.HasValue();
            bool hasPreventGroups = stateProto.PreventGroups.HasValue();
            foreach (var state in MetaStates)
            {
                var stateProtoRef = state.PrototypeDataRef;
                if (hasPreventStates) 
                    foreach(var preventState in stateProto.PreventStates)
                        if (preventState == stateProtoRef) return false;

                if (hasPreventGroups)
                    foreach(var group in stateProto.PreventGroups)
                        if (state.HasGroup(group)) return false;
            }

            if (stateProto.EvalCanActivate != null)
            {
                EvalContextData contextData = new(Game);
                contextData.SetReadOnlyVar_PropertyCollectionPtr(EvalContext.Other, Region.Properties);
                contextData.SetReadOnlyVar_EntityPtr(EvalContext.Default, this);
                if (Eval.RunBool(stateProto.EvalCanActivate, contextData) == false) return false;
            }

            return true;
        }

        public bool ApplyMetaState(PrototypeId stateRef, bool skipCooldown = false)
        {
            if (CanApplyState(stateRef, skipCooldown) == false) return false;
            var stateProto = GameDatabase.GetPrototype<MetaStatePrototype>(stateRef);
            if (MissionManager.Debug) Logger.Trace($"ApplyMetaState {GameDatabase.GetFormattedPrototypeName(stateProto.DataRef)} in {GameDatabase.GetFormattedPrototypeName(PrototypeDataRef)}");
            RemoveGroups(stateProto.RemoveGroups);
            RemoveStates(stateProto.RemoveStates);

            _applyStateStack.Enqueue(new(stateRef, skipCooldown));

            if (_scheduledApplyState.IsValid == false)
                ScheduleEntityEvent(_scheduledApplyState, TimeSpan.FromMilliseconds(0));

            ApplyStates(stateProto.SubStates);

            Properties[PropertyEnum.MetaGameTimeStateAddedMS, stateRef] = Game.CurrentTime;

            return true;
        }

        private void OnApplyState()
        {
            List<PrototypeId> removed = new();
            while (_removeStateStack.Count > 0 || _applyStateStack.Count > 0)
            {
                if (_removeStateStack.Count > 0)
                {
                    var removeState = _removeStateStack.Dequeue();
                    var state = MetaStates.FirstOrDefault(state => state.PrototypeDataRef == removeState);
                    if (state != null)
                    {
                        Properties[PropertyEnum.MetaGameTimeStateRemovedMS, removeState] = Game.CurrentTime;
                        state.OnRemove();
                        CurrentMode?.OnRemoveState(removeState);
                        removed.Add(removeState);
                        MetaStates.Remove(state);
                    }
                }
                else if (_applyStateStack.Count > 0)
                {
                    var applyState = _applyStateStack.Dequeue();
                    var stateRef = applyState.StateRef;
                    if (HasState(stateRef)) continue;
                    if (CanApplyState(stateRef, applyState.SkipCooldown) == false) continue;
                    MetaState state = MetaState.CreateMetaState(this, stateRef);
                    if (state == null) continue;
                    MetaStates.Add(state);
                    state.OnApply();
                }
            }

            foreach(var removedState in removed)
                foreach(var state in MetaStates)
                    state.OnRemovedState(removedState);
        }

        public bool HasState(PrototypeId stateRef)
        {
            return MetaStates.Any(state => state.PrototypeDataRef == stateRef);
        }

        public MetaState GetState(PrototypeId stateRef)
        {
            return MetaStates.FirstOrDefault(state => state.PrototypeDataRef == stateRef);
        }

        public void RemoveStates(PrototypeId[] removeStates)
        {
            if (removeStates.IsNullOrEmpty()) return;
            foreach (var removeState in removeStates)
                RemoveState(removeState);
        }

        public void RemoveGroups(AssetId[] removeGroups)
        {
            if (removeGroups.IsNullOrEmpty()) return;
            foreach (var removeGroup in removeGroups)
                RemoveGroup(removeGroup);
        }

        public void RemoveState(PrototypeId stateRef)
        {
            if (stateRef == PrototypeId.Invalid) return;
            var stateProto = GameDatabase.GetPrototype<MetaStatePrototype>(stateRef);
            if (stateProto == null) return;
            RemoveStates(stateProto.SubStates);
            _removeStateStack.Enqueue(stateRef);
            if (_scheduledApplyState.IsValid == false)
                ScheduleEntityEvent(_scheduledApplyState, TimeSpan.Zero);
            Properties[PropertyEnum.MetaGameTimeStateRemovedMS, stateRef] = Game.CurrentTime;
        }

        public void RemoveGroup(AssetId removeGroup)
        {
            if (removeGroup == AssetId.Invalid) return;
            foreach (var state in MetaStates)
                if (state.HasGroup(removeGroup))
                    RemoveState(state.PrototypeDataRef);
        }

        #endregion

        public bool RemoveSpawnEvent(PrototypeId contextRef)
        {
            return _metaStateSpawnEvents.Remove(contextRef);
        }

        public MetaStateSpawnEvent GetSpawnEvent(PrototypeId contextRef)
        {
            if (_metaStateSpawnEvents.TryGetValue(contextRef, out MetaStateSpawnEvent spawnEvent))
                return spawnEvent;
            else
            {
                spawnEvent = new(contextRef, Region);
                _metaStateSpawnEvents[contextRef] = spawnEvent;
                return spawnEvent;
            }
        }

        #region Player

        private void OnPlayerRegionChange(PlayerRegionChangeGameEvent evt)
        {
            var player = evt.Player;
            if (player == null) return;
            InitializePlayer(player);
        }

        public void OnRemovePlayer(Player player)
        {
            RemovePlayer(player);

            var manager = Game.EntityManager;
            if (manager == null) return;

            foreach (ulong entityId in _discoveredEntities)
            {
                var discoveredEntity = manager.GetEntity<WorldEntity>(entityId);
                if (discoveredEntity != null)
                    player.UndiscoverEntity(discoveredEntity, true);
            }

            foreach (MetaState state in MetaStates)
                state.OnRemovePlayer(player);
        }

        private void OnDestroyEntity(Entity entity)
        {
            if (entity is Player player)
                RemovePlayer(player);
        }

        private void OnPlayerEnteredRegion(PlayerEnteredRegionGameEvent evt)
        {
            var player = evt.Player;
            if (player == null) return;
            AddPlayer(player);
        }

        public bool InitializePlayer(Player player)
        {
            var team = GetTeamByPlayer(player);
            // TODO crate team?
            team?.AddPlayer(player);

            return true;
        }

        public bool UpdatePlayer(Player player, MetaGameTeam team)
        {
            if (player == null || team == null) return false;
            MetaGameTeam oldTeam = GetTeamByPlayer(player);
            if (oldTeam != team)
                return oldTeam.RemovePlayer(player) && team.AddPlayer(player);
            return false;
        }

        private MetaGameTeam GetTeamByPlayer(Player player)
        {
            foreach (var team in Teams)
                if (team.Contains(player)) return team;
            return null;
        }

        public virtual bool AddPlayer(Player player)
        {
            if (player == null) return false;

            // TODO add in chat

            return true;
        }

        public virtual bool RemovePlayer(Player player)
        {
            if (player == null) return false;

            // TODO remove from chat

            // remove from teams
            foreach (var team in Teams)
                if (team.RemovePlayer(player))
                    return true;

            return false;
        }

        #endregion

        #region Discover

        private void OnEntityEnteredWorld(EntityEnteredWorldGameEvent evt)
        {
            if (MetaGamePrototype?.DiscoverAvatarsForPlayers == true)
            {
                var entity = evt.Entity;
                if (entity is Avatar) DiscoverEntity(entity);
            }
        }

        private void DiscoverEntity(WorldEntity entity)
        {
            if (entity.IsDiscoverable && _discoveredEntities.Contains(entity.Id) == false)
            {
                _discoveredEntities.Add(entity.Id);
                DiscoverEntityForPlayers(entity);
            }
        }

        private void DiscoverEntityForPlayers(WorldEntity entity)
        {
            foreach (var player in new PlayerIterator(Game))
                player.DiscoverEntity(entity, true);
        }

        private void OnEntityExitedWorld(EntityExitedWorldGameEvent evt)
        {
            var entity = evt.Entity;
            if (entity != null) UniscoverEntity(entity);
        }

        public void UniscoverEntity(WorldEntity entity)
        {
            if (entity.IsDiscoverable && _discoveredEntities.Contains(entity.Id))
            {
                _discoveredEntities.Remove(entity.Id);
                UndiscoverEntityForPlayers(entity);
            }
        }

        private void UndiscoverEntityForPlayers(WorldEntity entity)
        {
            foreach (var player in new PlayerIterator(Game))
                player.UndiscoverEntity(entity, true);
        }

        public void ConsiderInAOI(AreaOfInterest aoi)
        {
            aoi.ConsiderEntity(this);
            foreach (var player in new PlayerIterator(Region))
                aoi.ConsiderEntity(player);
        }

        public void UpdatePlayerNotification(Player player)
        {
            var metaProto = MetaGamePrototype;

            if (metaProto.Teams.HasValue())
                foreach (var team in Teams)
                    team.SendTeamRoster(player);

            if (_modeIndex != -1 && _modeIndex < GameModes.Count)
                GameModes[_modeIndex].OnUpdatePlayerNotification(player);

            foreach (var state in MetaStates)
                state.OnUpdatePlayerNotification(player);
        }

        #endregion

        public void SetUIWidgetGenericFraction(PrototypeId widgetRef, PropertyId countPropId, TimeSpan timeOffset)
        {
            if (widgetRef == PrototypeId.Invalid) return;

            var widget = Region?.UIDataProvider?.GetWidget<UIWidgetGenericFraction>(widgetRef);
            if (widget != null)
            {
                int count = Properties[countPropId];
                widget.SetCount(count, count + 1);
                widget.SetTimeRemaining((long)timeOffset.TotalMilliseconds);
            }
        }

        public void ResetUIWidgetGenericFraction(PrototypeId widgetRef)
        {
            if (widgetRef == PrototypeId.Invalid) return;

            var uiDataProvider = Region?.UIDataProvider;
            var widget = uiDataProvider?.GetWidget<UIWidgetGenericFraction>(widgetRef);
            if (widget != null)
                uiDataProvider.DeleteWidget(widgetRef);
        }

        protected class ActivateGameModeEvent : CallMethodEventParam1<Entity, int>
        {
            protected override CallbackDelegate GetCallback() => (t, index) => (t as MetaGame)?.OnActivateGameMode(index);
        }

        protected class ApplyStateEvent : CallMethodEvent<Entity>
        {
            protected override CallbackDelegate GetCallback() => (t) => (t as MetaGame)?.OnApplyState();
        }

        public struct ApplyStateRecord
        {
            public PrototypeId StateRef;
            public bool SkipCooldown;

            public ApplyStateRecord(PrototypeId stateRef, bool skipCooldown)
            {
                StateRef = stateRef;
                SkipCooldown = skipCooldown;
            }
        }
    }
}
