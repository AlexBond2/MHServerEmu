using MHServerEmu.Core.VectorMath;

namespace MHServerEmu.Games.Navi
{
    public enum NaviPointFlags
    {
        None,
        Attached
    }

    public class NaviPoint : NaviObject, IComparable<NaviPoint>
    {
        public Vector3 Pos { get; set; }
        public NaviPointFlags Flags { get; set; }
        public sbyte InfluenceRef { get; set; }
        public float InfluenceRadius { get; set; }
        public ulong Id { get; set; }

        public NaviPoint(NaviSystem navi) : base(navi) { }

        public static NaviPoint Create(NaviSystem navi, Vector3 pos)
        {
            var point = navi.NewPoint();
            point.Pos = pos;
            return point;
        }

        public NaviPoint Ref
        {
            get
            {
                AddRef();
                return this;
            }
        }

        public override void Reset()
        {
            base.Reset();
            Flags = NaviPointFlags.None;
            InfluenceRef = 0;
            InfluenceRadius = 0.0f;
        }

        public uint GetHash()
        {
            uint hash = 2166136261;
            hash = (hash ^ BitConverter.SingleToUInt32Bits(Pos.X)) * 16777619;
            hash = (hash ^ BitConverter.SingleToUInt32Bits(Pos.Y)) * 16777619;
            hash = (hash ^ BitConverter.SingleToUInt32Bits(Pos.Z)) * 16777619;
            hash = (hash ^ (byte)Flags) * 16777619;
            return hash;
        }

        public ulong GetHash64()
        {
            ulong hash = 14695981039346656037;
            hash = (hash ^ BitConverter.SingleToUInt32Bits(Pos.X)) * 1099511628211;
            hash = (hash ^ BitConverter.SingleToUInt32Bits(Pos.Y)) * 1099511628211;
            hash = (hash ^ BitConverter.SingleToUInt32Bits(Pos.Z)) * 1099511628211;
            // hash = (hash ^ (byte)Flags) * 1099511628211;
            return hash;
        }

        public string ToStringCoord2D()
        {
            return $"({Pos.X:F4} {Pos.Y:F4})";
        }

        public string ToHashString()
        {
            return $"{GetHash():X}";
        }

        public override string ToString()
        {
            return $"NaviPoint ({Pos.X:F4} {Pos.Y:F4} {Pos.Z:F4}) flg:{Flags} inf:{InfluenceRef}";
        }

        public int CompareTo(NaviPoint other)
        {
            return Id.CompareTo(other.Id);
        }

        public void SetFlag(NaviPointFlags flag)
        {
            Flags |= flag;
        }
        public void ClearFlag(NaviPointFlags flag)
        {
            Flags &= ~flag;
        }

        public bool TestFlag(NaviPointFlags flag)
        {
            return Flags.HasFlag(flag);
        }
    }
}
