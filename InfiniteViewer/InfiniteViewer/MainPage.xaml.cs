using Microsoft.Toolkit.Collections;
using Microsoft.Toolkit.Uwp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.Storage.Search;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Input;
using Windows.Foundation;

namespace InfiniteViewer
{
    class WallClockMeasurement
    {
        public WallClockMeasurement()
        {
            _stopwatch = new Stopwatch();
            _stopwatch.Start();
        }

        public void Report(int threshold_ms, String id)
        {
            _stopwatch.Stop();
            if (_stopwatch.ElapsedMilliseconds >= threshold_ms)
            {
                Debug.WriteLine(id + " took " + _stopwatch.ElapsedMilliseconds + "ms");
            }
            _stopwatch.Restart();
        }

        private Stopwatch _stopwatch;
    }

    public class WorkQueue<T>
    {
        public T Pull()
        {
            lock(_mutex)
            {
                if (_queue.Count > 0)
                    return _queue.Dequeue();
                return default(T);
            }
        }

        public bool PushIfNotPresent(T t, Func<T, bool> eq)
        {
            lock(_mutex)
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

    public class LRUCache<K, V>
    {
        public LRUCache(int capacity)
        {
            _capacity = capacity;
        }

        public bool TryGet(K key, out V value)
        {
            lock(_mutex)
            {
                Entry entry;
                bool exists = _map.TryGetValue(key, out entry);
                if (exists)
                {
                    value = entry.value;
                    _list.Remove(entry.node);
                    _list.AddFirst(entry.node);
                } else
                {
                    value = default(V);
                }
                return exists;
            }
        }

        public bool InsertIfNotPresent(K key, V value)
        {
            lock(_mutex)
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
            lock(_mutex) { return _map.ContainsKey(key); }
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

    public class FileErrorHelper
    {
        public static async Task RaisePermissionsDialog(String path)
        {
            ContentDialog permissionsDialog = new ContentDialog
            {
                Title = "File system permissions required",
                Content = "Infinite Viewer must be granted file system access permissions and "
                   + "reloaded in order to open " + path,
                PrimaryButtonText = "Open App Settings",
                CloseButtonText = "Cancel"
            };
            var result = await permissionsDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:appsfeatures-app"));
            }
        }

        public static async Task RaiseNotFoundDialog(String path)
        {
            ContentDialog notFoundDialog = new ContentDialog
            {
                Title = "Folder not found",
                Content = "Folder '" + path + " not found.",
                CloseButtonText = "OK"
            };
            await notFoundDialog.ShowAsync();
        }

        public static async Task RunMethodAsync(Func<String, Task> fn, String path)
        {
            try
            {
                await fn(path);
            }
            catch (System.IO.FileNotFoundException)
            {
                await FileErrorHelper.RaiseNotFoundDialog(path);
            }
            catch (UnauthorizedAccessException)
            {
                await FileErrorHelper.RaisePermissionsDialog(path);
            }
        }
    }

    public class FileFetcher
    {
        public static List<string> FileFilter()
        {
            List<string> fileTypeFilter = new List<string>();
            fileTypeFilter.Add(".jpg");
            fileTypeFilter.Add(".png");
            fileTypeFilter.Add(".bmp");
            fileTypeFilter.Add(".gif");
            return fileTypeFilter;
        }

        public static QueryOptions GetFileQueryOptions()
        {
            var fileOptions = new QueryOptions(CommonFileQuery.OrderByName, FileFilter());
            fileOptions.IndexerOption = IndexerOption.UseIndexerWhenAvailable;
            return fileOptions;
        }

        public static async Task<List<StorageFile>> GetFilesAsync(StorageFolder folder)
        {
            var files = new List<StorageFile>();
            if (folder != null)
            {
                await FileErrorHelper.RunMethodAsync(async delegate (String path)
                {
                    var queryOptions = new QueryOptions(CommonFileQuery.OrderByName, FileFilter());
                    queryOptions.FolderDepth = FolderDepth.Deep;
                    var query = folder.CreateFileQueryWithOptions(queryOptions);
                    var clock = new WallClockMeasurement();
                    IReadOnlyList<StorageFile> unsorted = await query.GetFilesAsync();
                    clock.Report(0, "File query");
                    files = unsorted.OrderBy(f => f.Path).ToList();
                    Debug.WriteLine("Got " + files.Count() + " files");
                }, folder.Path);
            }
            return files;
        }

        public static async Task<List<StorageFolder>> GetSubfolders(StorageFolder parent)
        {
            List<StorageFolder> subfolders = new List<StorageFolder>();
            if (parent == null) return subfolders;
            await FileErrorHelper.RunMethodAsync(async delegate (String path)
            {
                var clock = new WallClockMeasurement();
                var folderOptions = new QueryOptions(CommonFolderQuery.DefaultQuery);
                folderOptions.FolderDepth = FolderDepth.Deep;
                // TODO option
                folderOptions.IndexerOption = IndexerOption.UseIndexerWhenAvailable;
                var folderQuery = parent.CreateFolderQueryWithOptions(folderOptions);
                var unsortedFolders = await folderQuery.GetFoldersAsync();
                subfolders = unsortedFolders.OrderBy(f => f.Path).ToList();
                clock.Report(0, "Querying " + subfolders.Count() + " subfolders of " + parent.Path);
            }, parent.Path);
            return subfolders;
        }

        public static List<StorageFileQueryResult> GetFileQueries(List<StorageFolder> folders)
        {
            var queries = new List<StorageFileQueryResult>();
            var clock = new WallClockMeasurement();
            var fileOptions = GetFileQueryOptions();
            foreach (StorageFolder f in folders)
                queries.Add(f.CreateFileQueryWithOptions(fileOptions));
            clock.Report(1, "Creating file queries");
            return queries;
        }

        public static async Task<List<StorageFile>> GetFilesAsyncV2(StorageFolder folder)
        {
            var files = new List<StorageFile>();
            if (folder != null)
            {
                await FileErrorHelper.RunMethodAsync(async delegate (String path)
                {
                    var clock = new WallClockMeasurement();
                    var folderOptions = new QueryOptions(CommonFolderQuery.DefaultQuery);
                    folderOptions.FolderDepth = FolderDepth.Deep;
                    var folderQuery = folder.CreateFolderQueryWithOptions(folderOptions);
                    var unsortedFolders = await folderQuery.GetFoldersAsync();
                    var folders = unsortedFolders.OrderBy(f => f.Path).ToList();
                    folders.Add(folder);
                    clock.Report(0, "Querying " + folders.Count() + " folders");

                    var fileOptions = new QueryOptions(CommonFileQuery.OrderByName, FileFilter());
                    var folderFileQueries = new List<StorageFileQueryResult>();
                    foreach (StorageFolder f in folders)
                    {
                        folderFileQueries.Add(f.CreateFileQueryWithOptions(fileOptions));
                    }
                    var getFilesTasks = new List<Task<IReadOnlyList<StorageFile>>>();
                    foreach (var q in folderFileQueries)
                    {
                        getFilesTasks.Add(q.GetFilesAsync().AsTask());
                    }
                    clock.Report(1, "Starting file queries");

                    var fileLists = await Task.WhenAll(getFilesTasks.ToArray());
                    clock.Report(0, "Querying files");

                    foreach (var fl in fileLists)
                    {
                        files.AddRange(fl);
                    }
                    Debug.WriteLine("Got " + files.Count() + " files");
                }, folder.Path);
            }
            return files;
        }
    }

    public class FolderNode
    {
        public FolderNode(StorageFolder me, SemaphoreSlim sem)
        {
            _me = me;
            _parallelismSemaphore = sem;
        }

        public async Task LaunchNext(CancellationToken ct)
        {
            var clock = new WallClockMeasurement();
            if (_me == null) return;
            try
            {
                if (_subfolderNodes == null)
                {
                    await LoadSubfolderNodes();
                }

                if (_nextSubfolderToLaunch < _subfolderNodes.Count)
                {
                    await _subfolderNodes[_nextSubfolderToLaunch++].LaunchNext(ct);
                    return;
                }
                else
                {
                    await _parallelismSemaphore.WaitAsync(ct);
                    var queryOptions = new QueryOptions(CommonFileQuery.OrderByName, FileFetcher.FileFilter());
                    queryOptions.IndexerOption = IndexerOption.UseIndexerWhenAvailable;
                    var query = _me.CreateFileQueryWithOptions(queryOptions);
                    _myFilesTask = query.GetFilesAsync();
                    _myFileTaskLoaded.SetResult(true);
                    _parallelismSemaphore.Release();
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine("Cancelled " + _me.Path + " load: " + e.ToString());
            }
            clock.Report(0, "Launching queries for " + _me.Path);
        }

        public async Task<List<StorageFile>> GetFilesDepthFirstAsync(int requested, CancellationToken ct)
        {            
            List<StorageFile> files = new List<StorageFile>();
            if (_me == null || _finished || (ct != null && ct.IsCancellationRequested)) return files;
            var clock = new WallClockMeasurement();
            await _subfolderNodesLoaded.Task;
            for (int i = _nextSubfolderWithItems; i < _subfolderNodes.Count(); ++i)
            {
                var sub = _subfolderNodes[i];
                if (i == _nextSubfolderToLaunch)
                {
                    await sub.LaunchNext(ct);
                    _nextSubfolderToLaunch++;
                }
                if (!sub.Finished())
                {
                    files.AddRange(await sub.GetFilesDepthFirstAsync(requested - files.Count(), ct));
                    if (files.Count >= requested)
                        return files;
                }
                if (sub.Finished())
                {
                    _nextSubfolderWithItems++;
                }
            }
            await _myFileTaskLoaded.Task;
            files.AddRange(await _myFilesTask);
            _finished = true;
            clock.Report(1, "Returning " + files.Count() + " files from " + _me.Path);
            return files;
        }

        private async Task LoadSubfolderNodes()
        {
            IReadOnlyList<StorageFolder> subfolders = new List<StorageFolder>();
            await FileErrorHelper.RunMethodAsync(
                async delegate (String p)
                {
                    subfolders = await _me.GetFoldersAsync();
                }, _me.Path);
            _subfolderNodes = new List<FolderNode>();
            foreach (var sf in subfolders)
                _subfolderNodes.Add(new FolderNode(sf, _parallelismSemaphore));
            _subfolderNodesLoaded.SetResult(true);
        }

        public bool Finished() { return _finished;  }

        private StorageFolder _me;
        private SemaphoreSlim _parallelismSemaphore;
        private bool _finished = false;

        private TaskCompletionSource<bool> _subfolderNodesLoaded = new TaskCompletionSource<bool>();
        private List<FolderNode> _subfolderNodes = null;
        private int _nextSubfolderWithItems = 0;
        private int _nextSubfolderToLaunch = 0;

        private TaskCompletionSource<bool> _myFileTaskLoaded = new TaskCompletionSource<bool>();
        private Windows.Foundation.IAsyncOperation<IReadOnlyList<StorageFile>> _myFilesTask = null;
    }

    public class FileSourceV2 : IIncrementalSource<StorageFile>
    {
        public FileSourceV2(FolderNode root)
        {
            _rootFolder = root;
        }

        public async Task<IEnumerable<StorageFile>> GetPagedItemsAsync(int pageIndex, int pageSize, CancellationToken token)
        {
            int startIndex = pageIndex * pageSize;
            int maxIndex = startIndex + pageSize;
            await LoadFilesAsync(maxIndex, token);
            if (maxIndex > _files.Count())
                maxIndex = _files.Count();
            var retFiles = new List<StorageFile>();
            for (int i = startIndex; i < maxIndex; ++i)
                retFiles.Add(_files[i]);
            return retFiles;
        }

        public async Task LoadFilesAsync(int items, CancellationToken ct)
        {
            var clock = new WallClockMeasurement();
            if (!_rootFolder.Finished())
            {
                var files = await _rootFolder.GetFilesDepthFirstAsync(items, ct);
                _files.AddRange(files);
                clock.Report(5, "Loaded " + files.Count() + " files. Have " + _files.Count() + " of " + items + " requested.");
            }          
        }

        private CancellationTokenSource _tokenSource = new CancellationTokenSource();
        private FolderNode _rootFolder;
        private List<StorageFile> _files = new List<StorageFile>();
    }

    public class FileSource : IIncrementalSource<StorageFile>
    {
        public FileSource(List<StorageFolder> folders)
        {
            var options = FileFetcher.GetFileQueryOptions();
            _queries = new FolderQuery[folders.Count];
            const int kMaxInitialQueries = 1;
            for (int i = 0; i < folders.Count; ++i)
            {
                var query = folders[i].CreateFileQueryWithOptions(options);
                _queries[i] = new FolderQuery(query, kNumPrimaryImages);
                if (i < kMaxInitialQueries)
                {
                    _queries[i].LaunchInitial();
                }
            }
        }

        public async Task<IEnumerable<StorageFile>> GetPagedItemsAsync(int pageIndex, int pageSize, CancellationToken token)
        {
            int startIndex = pageIndex * pageSize;
            int maxIndex = startIndex + pageSize;
            return await GetFilesAsync(startIndex, maxIndex);
        }

        private async Task<IEnumerable<StorageFile>> GetFilesAsync(int startFileIndex, int maxFileIndex)
        {
            var files = new List<StorageFile>();
            int queriesStartFileIndex = 0;
            int startQuery = 0;
            for (; startQuery < _queries.Count(); ++startQuery)
            {
                int offset = startFileIndex - queriesStartFileIndex;
                if (await _queries[startQuery].HasAtLeast(offset)) break;
                queriesStartFileIndex += await _queries[startQuery].Count();
            }

            int frontExtra = startFileIndex - queriesStartFileIndex;
            int desired = maxFileIndex - startFileIndex;
            for (int i = startQuery; i < _queries.Count(); ++i)
            {
                var q = _queries[i];
                files.AddRange(await q.GetInitial());
                if ((files.Count - frontExtra) >= desired) break;  // Done
                files.AddRange(await q.GetRemaining());
                if ((files.Count - frontExtra) >= desired) break;  // Done
            }
            int numToTake = Math.Min(desired, files.Count - frontExtra);
            return files.GetRange(frontExtra, numToTake);
        }

        private class FolderQuery
        {
            public FolderQuery(StorageFileQueryResult query, uint numInitial)
            {
                _numInitial = numInitial;
                _query = query;
            }

            public void LaunchInitial()
            {
                lock (_mutex)
                {
                    if (_initial == null)
                        _initial = _query.GetFilesAsync(0, _numInitial).AsTask();
                }
            }

            private void LaunchRemaining()
            {
                lock (_mutex)
                {
                    if (_remaining == null)
                        _remaining = _query.GetFilesAsync(_numInitial, 20000).AsTask();
                }
            }

            public async Task<IReadOnlyList<StorageFile>> GetFiles(int startOffset, int endOffset)
            {
                var ret = new List<StorageFile>();
                var initial = await GetInitial();
                int offset = startOffset;
                for (; offset < endOffset && offset < initial.Count; ++offset)
                    ret.Add(initial[offset]);
                if (offset >= endOffset) return ret;
                var remaining = await GetRemaining();
                offset = Math.Max(offset, startOffset);
                for (int i = offset - initial.Count; (i < remaining.Count) && ((offset + i) < endOffset); ++i)
                    ret.Add(remaining[i]);
                return ret;
          }

            public async Task<IReadOnlyList<StorageFile>> GetInitial()
            {
                if (InitialDone) return _initial.Result;
                LaunchInitial();
                var ret = await _initial;
                InitialDone = true;
                if (ret.Count < _numInitial)
                    RemainingUnnecessary = true;
                else if (_remaining == null)
                    LaunchRemaining();
                return ret;
            }

            public async Task<int> InitialCount() { 
                var i = await GetInitial();
                return i.Count;
            }

            public async Task<IReadOnlyList<StorageFile>> GetRemaining()
            {
                if (RemainingUnnecessary) return new List<StorageFile>();
                if (RemainingDone) return _remaining.Result;
                LaunchRemaining();
                var ret = await _remaining;
                RemainingDone = true;
                return ret;
            }

            public async Task<int> RemainingCount()
            {
                if (RemainingUnnecessary) return 0;
                var r = await GetRemaining();
                return r.Count;
            }

            public bool Finished() { return InitialDone && RemainingDone; }
            public async Task<int> Count()
            {
                var i = await InitialCount();
                var r = await RemainingCount();
                return i + r;
            }

            public async Task<bool> HasAtLeast(int n)
            {
                var i = await InitialCount();
                if (i >= n) return true;
                var r = await RemainingCount();
                return (i + r) >= n;
            }

            public bool InitialDone { get; set; } = false;
            public bool RemainingDone { get; set; } = false;
            public bool RemainingUnnecessary { get; set; } = false;

            private uint _numInitial;
            private StorageFileQueryResult _query;

            private object _mutex = new object();
            private Task<IReadOnlyList<StorageFile>> _initial = null;
            private Task<IReadOnlyList<StorageFile>> _remaining = null;
        }

        private const int kNumPrimaryImages = 25;
        private FolderQuery[] _queries;
    }

    public class ImageSource : IIncrementalSource<BitmapImage>
    {
        public ImageSource(FileSource fs)
        {
            _fileSource = fs;
        }

        public async Task<IEnumerable<BitmapImage>> GetPagedItemsAsync(int pageIndex, int pageSize, CancellationToken token)
        {
            var clock = new WallClockMeasurement();
            lock(_mutex)
            {
                if (pageIndex < _preloadedPages.Count && _preloadedPages[pageIndex] != null)
                {
                    var ret = _preloadedPages[pageIndex];
                    _preloadedPages[pageIndex] = null;
                    return ret;
                }
            }
            var tasks = new List<Task<BitmapImage>>();
            var files = await _fileSource.GetPagedItemsAsync(pageIndex, pageSize, token);
            if (pageIndex == 0)
                clock.Report(0, "Opening initial files");
            Debug.Assert(files != null);
            foreach (var file in files)
            {
                tasks.Add(OpenImageFromFile(file));
            }
            var ret2 = await Task.WhenAll(tasks.ToArray());
            if (pageIndex == 0)
                clock.Report(0, "Opening initial images");
            return ret2;
        }

        public async Task PreloadPage(int pageIndex, int pageSize)
        {
            var page = await GetPagedItemsAsync(pageIndex, pageSize, new CancellationToken());
            Debug.Assert(page != null);
            lock (_mutex)
            {
                while (pageIndex >= _preloadedPages.Count)
                    _preloadedPages.Add(null);
                _preloadedPages[pageIndex] = page;
            }
        }

        private async Task<BitmapImage> OpenImageFromFile(StorageFile file)
        {
            Debug.Assert(file != null);
            CoreDispatcher coreDispatcher = Window.Current.Dispatcher;
            var stream = await file.OpenReadAsync();
            Debug.Assert(stream != null);
            BitmapImage bmp = new BitmapImage();
            await bmp.SetSourceAsync(stream);
            return bmp;
        }

        private object _mutex = new object();
        // Check memory use
        private List<IEnumerable<BitmapImage>> _preloadedPages = new List<IEnumerable<BitmapImage>>();
        private FileSource _fileSource;
    }

    public class FolderNavigator
    {
        public async Task SetCurrentAsync(StorageFolder newCurrent)
        {
            IReadOnlyList<StorageFolder> newFamily = new List<StorageFolder>();
            int newIndex = -1;

            if (newCurrent != null)
            {
                await FileErrorHelper.RunMethodAsync(async delegate (String path)
                {
                    var parent = await newCurrent.GetParentAsync();
                    if (parent != null)
                    {
                        await FileErrorHelper.RunMethodAsync(async delegate (String parent_path)
                        {
                            var curName = newCurrent.Name;
                            newFamily = await parent.GetFoldersAsync();
                            for (int i = 0; i < newFamily.Count(); ++i)
                            {
                                if (newFamily[i].Name == curName)
                                {
                                    newIndex = i;
                                    break;
                                }
                            }
                        }, parent.Path);
                    }
                }, newCurrent.Path);
            }

            lock (_mutex)
            {
                _currentIndex = newIndex;
                _family = newFamily;
            }
        }

        public StorageFolder Current() { return CurrentOffset(0); }
        public StorageFolder Next() { return CurrentOffset(1);  }
        public StorageFolder Previous() { return CurrentOffset(-1); }
        public StorageFolder CurrentOffset(int offset)
        {
            lock (_mutex)
            {
                var index = _currentIndex + offset;
                if (_currentIndex >= 0 && index >= 0 && index < _family.Count())
                {
                    return _family[index];
                }
                return null;
            }
        }

        public StorageFolder GoNext()
        {
            lock (_mutex)
            {
                if (hasNext()) return _family[++_currentIndex];
                return null;
            }
        }
        public StorageFolder GoPrevious()
        {
            lock (_mutex)
            {
                if (hasPrevious()) return _family[--_currentIndex];
                return null;
            }
        }

        public bool hasNext()
        {
            lock (_mutex) return _currentIndex >= 0 && _currentIndex < (_family.Count() - 1);
        }
        public bool hasPrevious()
        {
            lock (_mutex) return _currentIndex > 0 && _currentIndex <= _family.Count();
        }

        private object _mutex = new object();
        private int _currentIndex = -1;
        private IReadOnlyList<StorageFolder> _family = new List<StorageFolder>();
    }

    public class ImageCollection : IncrementalLoadingCollection<ImageSource, BitmapImage>
    {
        public const int kPageSize = 1;

        public ImageCollection() : this("", new List<StorageFolder>()) { }
        public ImageCollection(String name, List<StorageFolder> folders) : this(new FileSource(folders)) {
            Name = name;
        }

        public async Task PreloadPages(int numPages)
        {
            if (numPages < 1) return;
            Task[] tasks = new Task[numPages];
            for (int i = 0; i < numPages; ++i)
            {
                tasks[i] = _imageSource.PreloadPage(i, kPageSize);
                Debug.Assert(tasks[i] != null);
            }
            await Task.WhenAll(tasks);
        }

        private ImageCollection(ImageSource isrc) : base(isrc, kPageSize) {
            _imageSource = isrc;
        }
        private ImageCollection(FileSource fs) : this(new ImageSource(fs)) {}

        public String Name { get; set; }

        private ImageSource _imageSource { get; set; }
    }

    public class ImageCollectionFactory
    {
        public static async Task<ImageCollection> MakeCollectionAsync(StorageFolder folder)
        {
            var folders = await FileFetcher.GetSubfolders(folder);
            String name = "";
            if (folder != null)
            {
                folders.Add(folder);
                name = folder.Path;
            }
            var collection = new ImageCollection(name, folders);
            return collection;
        }
    }

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
            _cachedCollections.InsertIfNotPresent(c.Name, c);
        }

        public bool Contains(String path) { return _cachedCollections.Contains(path);  }

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
                next.clock.Report(200, "Background creation for " + next.folder.Path);
                // await collection.PreloadPages(1).ConfigureAwait(false);
                next.clock.Report(200, "Background preload for " + next.folder.Path);
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

    public class CollectionNavigator
    {
        public CollectionNavigator(uint numLookAhead, uint numLookBehind)
        {
            _numAhead = numLookAhead;
            _numBehind = numLookBehind;
            _current = new ImageCollection();
            _cache = new CollectionCache(2 * (int)(numLookAhead + numLookBehind));
            _populator = new CachePopulator(_cache);
        }

        public async Task SetCurrentCollection(StorageFolder folder)
        {
            Debug.WriteLine("------ SET NEW FOLDER -------");
            var nav = _folderNavigator.SetCurrentAsync(folder);
            _current = await PreloadCollection(folder).ConfigureAwait(false);
            for (int i = 1; i <= _numAhead; ++i)
                _populator.Add(_folderNavigator.CurrentOffset(i));
            for (int i = 1; i <= _numBehind; ++i)
                _populator.Add(_folderNavigator.CurrentOffset(-i));
            Debug.WriteLine("Set CURRENT to " + Current().Name);
            await nav;
        }

        public ImageCollection Current()
        {
            return _current;
        }

        public bool CanGoNext() { return _folderNavigator.hasNext(); }
        public bool CanGoPrevious() { return _folderNavigator.hasPrevious(); }

        public async Task<bool> MoveNext()
        {
            if (_folderNavigator.hasNext())
            {
                Debug.WriteLine("------ NAVIGATE NEXT -------");
                _folderNavigator.GoNext();
                _populator.Add(_folderNavigator.CurrentOffset((int)_numAhead));
                _current = await _cache.GetOrCreateCollectionAsync(_folderNavigator.Current()).ConfigureAwait(false);
                Debug.WriteLine("Set CURRENT to " + _current.Name);
                return true;
            }
            return false;
        }

        public async Task<bool> MovePrevious()
        {
            if (_folderNavigator.hasPrevious())
            {
                Debug.WriteLine("------ NAVIGATE PREV -------");
                _folderNavigator.GoPrevious();
                _populator.Add(_folderNavigator.CurrentOffset(-(int)_numBehind));
                _current = await _cache.GetOrCreateCollectionAsync(_folderNavigator.Current()).ConfigureAwait(false);
                Debug.WriteLine("Set CURRENT to " + Current().Name);
                return true;
            }
            return false;
        }

        private async Task<ImageCollection> PreloadCollection(StorageFolder folder)
        {
            var c = await _cache.GetOrCreateCollectionAsync(folder).ConfigureAwait(false);
            // await c.PreloadPages(1);
            return c;
        }

        uint _numAhead;
        uint _numBehind;
        private ImageCollection _current = new ImageCollection();
        private FolderNavigator _folderNavigator = new FolderNavigator();
        private CollectionCache _cache;
        private CachePopulator _populator;
    }

    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
            UpdateUi();
        }

        private async Task debugDialog(String message)
        {
            ContentDialog debugDialog = new ContentDialog
            {
                Title = "DEBUG: " + message,
                Content = message,
                CloseButtonText = "OK"
            };
            await debugDialog.ShowAsync();
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e != null)
            {
                var folderPath = e.Parameter as String;
                if (folderPath != null && folderPath.Length > 0)
                {
                    await OpenFolderPathAndReloadView(folderPath);
                }
            }
            base.OnNavigatedTo(e);
        }

        private void Page_Loaded(object sender, RoutedEventArgs e) { }

        private async Task OpenFolderPathAndReloadView(String path)
        {
            await FileErrorHelper.RunMethodAsync(async delegate (String p) {
                var folder = await StorageFolder.GetFolderFromPathAsync(p);
                await UpdateFromFolder(folder);
            }, path);
        }

        private async Task SelectFolderAndReloadView()
        {
            var folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.Desktop;
            folderPicker.FileTypeFilter.Add("*");
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                StorageApplicationPermissions.FutureAccessList.AddOrReplace("PickedFolderToken", folder);
                await UpdateFromFolder(folder);
            }
        }

        private async Task UpdateFromFolder(StorageFolder folder)
        {
            await _navSemaphore.WaitAsync().ConfigureAwait(false);
            await _collectionNavigator.SetCurrentCollection(folder);
            await UpdateUiFromNonUiThread();
            _navSemaphore.Release();
        }

        private void UpdateUi()
        {
            var current = _collectionNavigator.Current();
            NextFolderButton.IsEnabled = _collectionNavigator.CanGoNext();
            PreviousFolderButton.IsEnabled = _collectionNavigator.CanGoPrevious();
            var appView = Windows.UI.ViewManagement.ApplicationView.GetForCurrentView();              
            appView.Title = current.Name;
            ListViewMain.ItemsSource = current;
            Debug.WriteLine("Updated UI to " + current.Name);
        }

        private async Task UpdateUiFromNonUiThread()
        {
            CoreDispatcher coreDispatcher = Window.Current.Dispatcher;
            await coreDispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                UpdateUi();
            });
        }

        private async void MoveNext()
        {
            if (!_navSemaphore.Wait(0)) return;
            var clock = new WallClockMeasurement();
            if (await _collectionNavigator.MoveNext())
            {
                clock.Report(0, "MOVE NEXT: Navigation");
                UpdateUi();
                clock.Report(0, "MOVE NEXT: Update UI");
            }
            _navSemaphore.Release();
            clock.Report(0, "----- Finished Move Next ------");
        }

        private async void MovePrevious()
        {
            if (!_navSemaphore.Wait(0)) return;
            if (await _collectionNavigator.MovePrevious())
            {
                UpdateUi();
            }
            _navSemaphore.Release();
        }


        private async void SelectFolder_Click(object sender, RoutedEventArgs e)
        {
            await SelectFolderAndReloadView();
        }
        private void NextFolder_Click(object sender, RoutedEventArgs e)
        {
            MoveNext();
        }
        private void PreviousFolder_Click(object sender, RoutedEventArgs e)
        {
            MovePrevious();
        }

        private void Keyboard_Right(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            MoveNext();
        }
        private void Keyboard_Left(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            MovePrevious();
        }

        private SemaphoreSlim _navSemaphore = new SemaphoreSlim(1, 1);
        private CollectionNavigator _collectionNavigator = new CollectionNavigator(4, 2);
    }
}