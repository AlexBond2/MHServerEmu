﻿using Gazillion;
using MHServerEmu.Games.Network;

namespace MHServerEmu.Games.Common
{
    public class AdminCommandManager
    {
        private Game _game;
        private AdminFlags _flags;

        public AdminCommandManager(Game game) 
        { 
            _game = game;
            _flags = AdminFlags.LocomotionSync | AdminFlags.CurrencyItemsConvertToggle;
        }

        public bool TestAdminFlag(AdminFlags flag)
        {
            return _flags.HasFlag(flag);
        }

        public static void SendAdminCommandResponse(PlayerConnection playerConnection, string response)
        {
            playerConnection.SendMessage(NetMessageAdminCommandResponse.CreateBuilder()
                .SetResponse(response)
                .Build());
        }

        public static void SendAdminCommandResponseSplit(PlayerConnection playerConnection, string response)
        {
            foreach (string line in response.Split("\r\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                SendAdminCommandResponse(playerConnection, line);
        }

        public static void SendVerify(PlayerConnection playerConnection, string message)
        {
            playerConnection.SendMessage(NetMessageVerifyOnClient.CreateBuilder()
                .SetMessage($"(Server) {message}")
                .Build());
        }
    }

    [Flags]
    public enum AdminFlags : ulong              // Descriptions from 1.0.4932.0:
    {
        LocomotionSync              = 1 << 1,   // Toggles experimental locomotion sync mode
        CurrencyItemsConvertToggle  = 1 << 47   // Turns on/off conversion of Currency Items to Currency properties
    }
}
