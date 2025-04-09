namespace MHServerEmu.Games.Navi
{
    public abstract class NaviObject : IDisposable
    {
        public int RefCount { get; private set; }
        public NaviSystem NaviSystem { get; set; }

        protected NaviObject(NaviSystem navi)
        {
            NaviSystem = navi;
            RefCount = 0;
        }

        public void AddRef() => RefCount++;

        public void Release()
        {
            if (--RefCount <= 0)
                NaviSystem.Delete(this);
        }

        public virtual void Dispose() => Release();

        public virtual void Reset()
        {
            RefCount = 0;
        }

        public static void Release(NaviObject[] objects)
        {
            for (int i = 0; i < objects.Length; i++)
            {
                objects[i]?.Release();
                objects[i] = null;
            }
        }

        public T RefObj<T>(T obj) where T : NaviObject
        {
            if (obj != null)
            {
                if (this == obj) return (T)this;
                obj.Release();
                AddRef();
            }
            return (T)this;
        }

        public static void SafeRelease<T>(ref T obj) where T: NaviObject
        {
            obj?.Release();
            obj = null;
        }
    }
}
