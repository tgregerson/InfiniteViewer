using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace InfiniteViewer
{
    public class CollectionCache
    {
        public CollectionCache(int capacity)
        {
            _cachedCollections = new LRUCache<String, ImageCollection>(capacity);
        }

        public ImageCollection GetCollection(StorageFolder folder)
        {
            ImageCollection c = null;
            if (folder != null)
                if (!_cachedCollections.TryGet(folder.Path, out c))
                    Debug.WriteLine("Cache miss for " + folder.Path);
            return c;
        }

        public async Task<ImageCollection> GetOrCreateCollectionAsync(StorageFolder folder)
        {
            var res = GetCollection(folder);
            if (res == null)
            {
                res = await ImageCollectionFactory.MakeCollectionAsync(folder).ConfigureAwait(false);
                Add(res);
            }
            return res;
        }

        public void Add(ImageCollection c)
        {
            _cachedCollections.InsertIfNotPresent(c.Name(), c);
        }

        public void Flush() { _cachedCollections.Flush(); }

        public bool Contains(String path) { return _cachedCollections.Contains(path); }

        private LRUCache<String, ImageCollection> _cachedCollections;
    }

    public class CachePopulator
    {
        public CachePopulator(CollectionCache cache)
        {
            _cache = cache;
        }

        public void Add(StorageFolder folder)
        {
            if (folder == null) return;
            if (!_cache.Contains(folder.Path))
            {
                WorkItem item = new WorkItem();
                item.folder = folder;
                if (_work.PushIfNotPresent(item, e => e.folder.Path == folder.Path))
                {
                    Debug.WriteLine("Queued background loading for " + folder.Path);
                    ThreadPool.QueueUserWorkItem(ServiceQueue);
                }
            }
        }

        private async void ServiceQueue(Object stateInfo)
        {
            WorkItem next = _work.Pull();
            while (next != null)
            {
                next.clock.Report(200, "Serviced queue for " + next.folder.Path);
                var collection = await ImageCollectionFactory.MakeCollectionAsync(next.folder).ConfigureAwait(false);
                next.clock.Report(1000, "Background creation for " + next.folder.Path);
                _cache.Add(collection);
                next = _work.Pull();
            }
        }

        private class WorkItem
        {
            public StorageFolder folder;
            public WallClockMeasurement clock = new WallClockMeasurement();
        }

        private CollectionCache _cache;
        private WorkQueue<WorkItem> _work = new WorkQueue<WorkItem>();
    }
}