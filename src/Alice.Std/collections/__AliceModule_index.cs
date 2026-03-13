namespace Alice.Std.collections;

public static class __AliceModule_index
{
    public sealed class List<T>
    {
        private readonly System.Collections.Generic.List<T> _list;

        public List()
        {
            _list = new System.Collections.Generic.List<T>();
        }

        public void Add(T x) => _list.Add(x);

        public T Get(int i) => _list[i];

        public void Set(int i, T v) => _list[i] = v;

        public int Len() => _list.Count;

        public T[] ToArray() => _list.ToArray();
    }

    public sealed class Map<K, V> where K : notnull
    {
        private readonly System.Collections.Generic.Dictionary<K, V> _dict;

        public Map()
        {
            _dict = new System.Collections.Generic.Dictionary<K, V>();
        }

        public void Set(K k, V v) => _dict[k] = v;

        public V Get(K k) => _dict[k];

        public bool Has(K k) => _dict.ContainsKey(k);

        public int Len() => _dict.Count;
    }
}
