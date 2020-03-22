using System;
using System.Collections.Generic;

namespace InfiniteViewer
{
    public class WorkQueue<T>
    {
        public T Pull()
        {
            lock (_mutex)
            {
                if (_queue.Count > 0)
                    return _queue.Dequeue();
                return default(T);
            }
        }

        public bool PushIfNotPresent(T t, Func<T, bool> eq)
        {
            lock (_mutex)
            {
                foreach (var other in _queue)
                    if (eq(other))
                        return false;
                _queue.Enqueue(t);
                return true;
            }
        }

        public int Count() { lock (_mutex) { return _queue.Count; } }

        private object _mutex = new object();
        private Queue<T> _queue = new Queue<T>();
    }
}