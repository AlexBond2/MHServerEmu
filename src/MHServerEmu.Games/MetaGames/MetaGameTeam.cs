﻿using MHServerEmu.Core.Logging;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.GameData;

namespace MHServerEmu.Games.MetaGames
{
    public class MetaGameTeam
    {
        private static readonly Logger Logger = LogManager.CreateLogger();
        public MetaGame MetaGame { get; }
        public PrototypeId ProtoRef {  get; }
        public int MaxPlayers { get; }
        private List<Player> _players {  get; }

        public MetaGameTeam(MetaGame metaGame, PrototypeId protoRef, int maxPlayers)
        {
            MetaGame = metaGame;
            ProtoRef = protoRef;
            _players = new();
            MaxPlayers = maxPlayers;
        }

        public virtual bool AddPlayer(Player player)
        {
            if (IndexOf(player) >= 0) return Logger.WarnReturn(false, "Attempt to add a player to a team twice");
            _players.Add(player); 
            return true;
        }

        public virtual void ClearPlayers()
        {
            while (_players.Count > 0) 
                RemovePlayer(_players[0]);
        }

        public virtual bool RemovePlayer(Player player)
        {
            if (_players.Contains(player) == false) return false;
            _players.Remove(player);
            return true;
        }

        public bool Contains(Player player) => IndexOf(player) > -1;

        public int IndexOf(Player player)
        {
            for (int i = 0; i < _players.Count; i++)
                if (_players[i] == player) return i;
            return -1;
        }

        internal void Destroy()
        {
            throw new NotImplementedException();
        }
    }
}