using MHServerEmu.Core.Collisions;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.Regions;
using System.Text;

namespace MHServerEmu.Games.Navi
{
    public class NaviSystem
    {
        public bool Log = true;
        public static readonly Logger Logger = LogManager.CreateLogger();
        public Region Region { get; private set; }
        public List<NaviErrorReport> ErrorLog { get; private set; } = [];

        private readonly NaviPool<NaviPoint> _pointPool;
        private readonly NaviPool<NaviEdge> _edgePool;
        private readonly NaviPool<NaviTriangle> _trianglePool;
        private readonly NaviPool<NaviPathSearchState> _pathSearchStatePool;

        private ulong _nextId = 0;

        public NaviSystem()
        {
            _pointPool = new(() => new NaviPoint(this));
            _edgePool = new(() => new NaviEdge(this));
            _trianglePool = new(() => new NaviTriangle(this));
            _pathSearchStatePool = new(() => new NaviPathSearchState(this), 32768);
        }

        public bool Initialize(Region region)
        {
            Region = region;
            return true;
        }

        public NaviPoint NewPoint()
        {
            var point = _pointPool.Get();
            point.Id = _nextId++;
            return point;
        }

        public NaviEdge NewEdge() => _edgePool.Get();
        public NaviTriangle NewTriangle() => _trianglePool.Get();
        public NaviPathSearchState NewPathSearchState() => _pathSearchStatePool.Get();

        internal void Delete(NaviObject obj)
        {
            switch (obj)
            {
                case NaviPoint point: _pointPool.Return(point); break;
                case NaviEdge edge: _edgePool.Return(edge); break;
                case NaviTriangle triangle: _trianglePool.Return(triangle); break;
                case NaviPathSearchState state: _pathSearchStatePool.Return(state); break;
            }
        }

        public void LogError(string msg)
        {
            using NaviErrorReport errorReport = new()
            {
                Msg = msg
            };
            ErrorLog.Add(errorReport);
        }

        public void LogError(string msg, NaviEdge edge)
        {
            using NaviErrorReport errorReport = new()
            {
                Msg = msg,
                Edge = edge.Ref
            };
            ErrorLog.Add(errorReport);
        }

        public void LogError(string msg, NaviPoint point)
        {
            using NaviErrorReport errorReport = new()
            {
                Msg = msg,
                Point = point.Ref
            };
            ErrorLog.Add(errorReport);
        }

        public void ClearErrorLog()
        {
            ErrorLog.Clear();
        }

        public bool CheckErrorLog(bool clearErrorLog, string info = null)
        {
            bool hasErrors = HasErrors();
            if (Log && hasErrors)
            {
                var error = ErrorLog[0];

                Cell cell = null;
                if (Region != null)
                {
                    if (error.Point != null)
                        cell = Region.GetCellAtPosition(error.Point.Pos);
                    else if (error.Edge != null)
                        cell = Region.GetCellAtPosition(error.Edge.Midpoint());
                }
                StringBuilder sb = new();
                sb.AppendLine($"Navigation Error: {error.Msg}");
                sb.AppendLine($"Cell: {(cell != null ? cell.ToString() : "Unknown")}");
                if (error.Point != null)
                    sb.AppendLine($"Point: {error.Point}");
                if (error.Edge != null)
                    sb.AppendLine($"Edge: {error.Edge}");
                if (string.IsNullOrEmpty(info) == false)
                    sb.AppendLine($"Extra Info: {info}");
                Logger.Error(sb.ToString());
            }

            if (clearErrorLog) ClearErrorLog();
            return hasErrors;
        }

        public bool HasErrors()
        {
            return ErrorLog.Count > 0;
        }

    }

    public struct NaviErrorReport : IDisposable
    { 
        public string Msg;
        public NaviPoint Point;
        public NaviEdge Edge; 

        public void Dispose()
        {
            Point?.Dispose();
            Point = null;
            Edge?.Dispose();
            Edge = null;
        }
    }

    public class NaviSerialCheck
    {
        public NaviSerialCheck(NaviCdt naviCdt)
        {
            NaviCdt = naviCdt;
            Serial = ++NaviCdt.Serial;
        }

        public uint Serial { get; private set; }
        protected NaviCdt NaviCdt { get; private set; }

    }

    public class NavigationInfluence : IDisposable
    {
        public NaviPoint Point;
        public NaviTriangle Triangle;

        public void Dispose()
        {
            Triangle?.Dispose();
            Triangle = null;
            Point?.Dispose();
            Point = null;
        }
    }
}
