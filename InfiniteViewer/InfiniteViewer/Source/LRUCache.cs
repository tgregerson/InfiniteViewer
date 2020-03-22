using System.Collections.Generic;

namespace InfiniteViewer
{

    public class LRUCache<K, V>
    {
        public LRUCache(int capacity)
        {
            _capacity = capacity;
        }

        public bool TryGet(K key, out V value)
        {
            lock (_mutex)
            {
                Entry entry;
                bool exists = _map.TryGetValue(key, out entry);
                if (exists)
                {
                    value = entry.value;
                    _list.Remove(entry.node);
                    _list.AddFirst(entry.node);
                }
                else
                {
                    value = default(V);
                }
                return exists;
            }
        }

        public bool InsertIfNotPresent(K key, V value)
        {
            lock (_mutex)
            {
                if (_map.ContainsKey(key))
                    return false;
                Entry entry = new Entry();
                entry.value = value;
                entry.node = _list.AddFirst(key);
                _map[key] = entry;

                if (_list.Count > _capacity)
                {
                    _map.Remove(_list.Last.Value);
                    _list.RemoveLast();
                }
                return true;
            }
        }

        public bool Contains(K key)
        {
            lock (_mutex) { return _map.ContainsKey(key); }
        }

        public void Flush()
        {
            lock (_mutex)
            {
                _list.Clear();
                _map.Clear();
            }
        }

        private class Entry
        {
            public V value;
            public LinkedListNode<K> node;
        }

        private object _mutex = new object();
        private int _capacity;
        private LinkedList<K> _list = new LinkedList<K>();
        private Dictionary<K, Entry> _map = new Dictionary<K, Entry>();
    }
}