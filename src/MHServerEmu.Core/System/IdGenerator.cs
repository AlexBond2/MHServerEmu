﻿using MHServerEmu.Core.System.Time;

namespace MHServerEmu.Core.System
{
    public enum IdType
    {
        Generic = 0,
        Region  = 1,        // [Runtime] Region instance id
        Player  = 2,        // [Database] Accounts and persistent player entities representing those accounts in the game
        Session = 3,        // [Runtime] Client connection id
        Game    = 4,        // [Runtime] Game instance id
        Entity  = 5,        // [Database] Persistent entities (avatars, items, etc.)
        Limit   = 1 << 4    // 16
    }

    /// <summary>
    /// Generates snowflake-like 64-bit ids for various purposes.
    /// </summary>
    public class IdGenerator
    {
        // Based on snowflake ids (see here: https://en.wikipedia.org/wiki/Snowflake_ID)
        // Current structure:
        //  4 bits - type
        // 12 bits - machine id (for generating ids of the same type in parallel, up to 4096 instances at the same time)
        // 32 bits - unix timestamp in seconds
        // 16 bits - machine sequence number (to avoid collisions if multiple ids are generated in the same second)

        private readonly object _lock = new();

        private readonly IdType _type;
        private readonly ushort _machineId;
        private ushort _machineSequenceNumber = 0;

        /// <summary>
        /// Constructs a new <see cref="IdGenerator"/> instance. Machine Id must be < 4096.
        /// </summary>
        public IdGenerator(IdType type, ushort machineId = 0)
        {
            if (type >= IdType.Limit) throw new OverflowException("Type exceeds 4 bits.");
            if (machineId >= 1 << 12) throw new OverflowException("MachineId exceeds 12 bits.");

            _type = type;
            _machineId = machineId;
        }

        /// <summary>
        /// Generates a new 64-bit id.
        /// </summary>
        public ulong Generate()
        {
            // NOTE: Generation needs to be thread-safe because it can be
            // called by multiple game instances running on the same server. 
            lock (_lock)
            {
                ulong id = 0;
                id |= (ulong)_type << 60;
                id |= (ulong)_machineId << 48;
                id |= (((ulong)Clock.UnixTime.TotalSeconds) & 0xFFFFFFFF) << 16;
                id |= _machineSequenceNumber++;
                return id;
            }
        }

        /// <summary>
        /// Parses metadata from a generated id.
        /// </summary>
        public static ParsedId Parse(ulong id) => new(id);

        /// <summary>
        /// Parsed id generated by <see cref="IdGenerator"/>.
        /// </summary>
        public readonly struct ParsedId
        {
            public IdType Type { get; }
            public ushort MachineId { get; }
            public DateTime Timestamp { get; }
            public ushort MachineSequenceNumber { get; }

            public ParsedId(ulong id)
            {
                Type = (IdType)(id >> 60);
                MachineId = (ushort)(id >> 48 & 0xFFF);
                Timestamp = Clock.UnixTimeToDateTime(TimeSpan.FromSeconds(id >> 16 & 0xFFFFFFFF));
                MachineSequenceNumber = (ushort)(id & 0xFFFF);
            }

            public override string ToString()
            {
                return $"{Type} | 0x{MachineId:X} | {Timestamp} | {MachineSequenceNumber}";
            }
        }
    }
}
