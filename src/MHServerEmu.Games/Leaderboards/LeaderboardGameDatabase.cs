﻿using Gazillion;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using System.Diagnostics;

namespace MHServerEmu.Games.Leaderboards
{   
    /// <summary>
    /// A singleton that contains leaderboard infomation.
    /// </summary>
    public class LeaderboardGameDatabase
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private Dictionary<PrototypeGuid, LeaderboardInfo> _leaderboardInfoMap = new();
        private Queue<LeaderboardQueue> _updateQueue = new(); 
        private readonly object _lock = new object();
        public static LeaderboardGameDatabase Instance { get; } = new();

        private LeaderboardGameDatabase() { }

        /// <summary>
        /// Initializes the <see cref="LeaderboardGameDatabase"/> instance.
        /// </summary>
        public bool Initialize()
        {
            var stopwatch = Stopwatch.StartNew();

            int count = 0;

            // Load leaderboard prototypes
            foreach (var dataRef in GameDatabase.DataDirectory.IteratePrototypesInHierarchy<LeaderboardPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
            {
                var proto = GameDatabase.GetPrototype<LeaderboardPrototype>(dataRef);
                if (proto != null)
                {
                    var guid = GameDatabase.GetPrototypeGuid(dataRef);
                    _leaderboardInfoMap[guid] = new(proto);
                    count++;
                }
                else
                    Logger.Warn($"Prototype {dataRef} == null");
            }

            Logger.Info($"Initialized {count} leaderboards in {stopwatch.ElapsedMilliseconds} ms");

            return true;
        }

        public IEnumerable<LeaderboardPrototype> GetActiveLeaderboardPrototypes()
        {
            lock (_lock)
            {
                foreach (var leaderboard in _leaderboardInfoMap.Values)
                    foreach (var instance in leaderboard.Instances)
                        if (instance.State == LeaderboardState.eLBS_Active)
                            yield return leaderboard.Prototype;
            }
        }

        public void UpdateLeaderboards(List<LeaderboardInstanceInfo> instances)
        {
            lock (_lock)
            {
                foreach (var instance in instances)
                    UpdateLeaderboardInstance(instance, false);
            }
        }

        public void UpdateLeaderboardInstance(LeaderboardInstanceInfo instanceInfo, bool rewarded)
        {
            lock (_lock)
            {
                if (rewarded)
                {
                    var activePlayers = new PlayerIterator(Game.Current).ToArray();
                    foreach (var player in activePlayers)
                        player.LeaderboardManager.CheckRewards = true;
                }

                if (_leaderboardInfoMap.TryGetValue(instanceInfo.LeaderboardId, out var leaderboardInfo))
                {
                    var updateInstance = leaderboardInfo.Instances.Find(instance => instance.InstanceId == instanceInfo.InstanceId);

                    if (updateInstance != null)
                        updateInstance.Update(instanceInfo);
                    else
                        leaderboardInfo.Instances.Add(instanceInfo);
                }
                else
                {
                    var dataRef = GameDatabase.GetDataRefByPrototypeGuid(instanceInfo.LeaderboardId);
                    var proto = GameDatabase.GetPrototype<LeaderboardPrototype>(dataRef);

                    if (proto != null)
                    {
                        leaderboardInfo = new(proto);
                        leaderboardInfo.Instances.Add(instanceInfo);
                        _leaderboardInfoMap[instanceInfo.LeaderboardId] = leaderboardInfo;
                    }
                }
            }
        }

        public void AddUpdateQueue(LeaderboardQueue queue)
        {
            lock (_lock)
            {
                _updateQueue.Enqueue(queue);
            }
        }

        public Queue<LeaderboardQueue> GetUpdateQueue()
        {
            lock (_lock)
            {
                Queue<LeaderboardQueue> queue = new(_updateQueue);
                _updateQueue.Clear();
                return queue;
            }
        }
    }
}
