namespace MHServerEmu.Games.Navi
{
    public class NaviPathSearchState : NaviObject, IComparable<NaviPathSearchState>
    {
        public NaviPathSearchState ParentState;
        public NaviTriangle Triangle;
        public NaviEdge Edge;
        public float DistDone;
        public float DistLeft;
        public float Distance;

        public NaviPathSearchState(NaviSystem navi) : base(navi) { }

        public static NaviPathSearchState Create(NaviSystem navi)
        {
            return navi.NewPathSearchState();
        }

        public override void Reset()
        {
            base.Reset();
            ParentState = null;
            Triangle = null;
            Edge = null;
            DistDone = 0.0f;
            DistLeft = 0.0f;
            Distance = 0.0f;
        }

        public int CompareTo(NaviPathSearchState other)
        {
            return other.Distance.CompareTo(Distance);
        }

        public bool IsAncestor(NaviTriangle triangle)
        {
            NaviPathSearchState parent = ParentState;
            while (parent != null)
            {
                if (parent.Triangle == triangle)
                    return true;
                parent = parent.ParentState;
            }
            return false;
        }

    }
}
