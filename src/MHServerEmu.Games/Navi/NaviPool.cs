namespace MHServerEmu.Games.Navi
{
    public class NaviPool<T> where T : NaviObject
    {
        private readonly Stack<T> _pool;
        private readonly Func<T> _factory;

        public NaviPool(Func<T> factory, int capacity = 65536)
        {
            _pool = new(capacity);
            _factory = factory;
        }

        public T Get()
        {
            if (_pool.Count > 0)
                return _pool.Pop(); 

            return _factory();
        }

        public void Return(T obj)
        {
            obj.Reset();
            _pool.Push(obj);
        }
    }
}
