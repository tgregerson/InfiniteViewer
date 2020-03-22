using Microsoft.Toolkit.Collections;
using Microsoft.Toolkit.Uwp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.Storage.Search;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media;
using System.Collections.Concurrent;

using FileElement = Windows.Storage.StorageFile;

namespace InfiniteViewer
{
    public enum SortOrder
    {
        NameAscending,
        NameDescending,
        DateModifiedAscending,
        DateModifiedDescending,
        Random,
    }

    public class SortOptions : INotifyPropertyChanged
    {
        public void SetOrder(SortOrder o)
        {
            _order = o;
            IsNameAscending = (o == SortOrder.NameAscending);
            IsNameDescending = (o == SortOrder.NameDescending);
            IsDateModifiedAscending = (o == SortOrder.DateModifiedAscending);
            IsDateModifiedDescending = (o == SortOrder.DateModifiedDescending);
            IsRandom = (o == SortOrder.Random);
        }
        public SortOrder Order() { return _order; }

        public bool IsNameAscending
        {
            get { return _order == SortOrder.NameAscending; }
            set { this.OnPropertyChanged(); }
        }
        public bool IsNameDescending
        {
            get { return _order == SortOrder.NameDescending; }
            set { this.OnPropertyChanged(); }
        }
        public bool IsDateModifiedAscending
        {
            get { return _order == SortOrder.DateModifiedAscending; }
            set { this.OnPropertyChanged(); }
        }
        public bool IsDateModifiedDescending
        {
            get { return _order == SortOrder.DateModifiedDescending; }
            set { this.OnPropertyChanged(); }
        }
        public bool IsRandom
        {
            get { return _order == SortOrder.Random; }
            set { this.OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged = delegate { };

        public void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private SortOrder _order = SortOrder.NameAscending;
    }

    public class Options
    {
        public static Options Instance { get; } = new Options();

        public SortOptions FileSortOptions { get; set; } = new SortOptions();
        public SortOptions FolderSortOptions { get; set; } = new SortOptions();
    }

    public class ImageProperties : INotifyPropertyChanged
    {
        public double Width {
            get { return this._width; }
            set { _width = value; this.OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged = delegate { };

        public void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            // Raise the PropertyChanged event, passing the name of the property whose value has changed.
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private double _width = double.NaN;
    }

    public class WallClockMeasurement
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

        public void Flush()
        {
            lock(_mutex)
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

        public static async Task RaiseImageErrorDialog(String path)
        {
            ContentDialog imageErrorDialog = new ContentDialog
            {
                Title = "Error opening image",
                Content = "InfiniteViewer could not render " + path + ". The file may be corrupt.\n\n" +
                          "Select Silence to suppress further errors of this type for the remainder of the session.",
                PrimaryButtonText = "Silence",
                CloseButtonText = "Dismiss"
            };
            var result = await imageErrorDialog.ShowAsync();
            SuppressImageErrors = result == ContentDialogResult.Primary;
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

        public static bool SuppressImageErrors { get; set; } = false;
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

        public static SortEntry MakeSortEntry(SortOptions opts)
        {
            var sort = new SortEntry();
            if (opts.IsNameDescending || opts.IsDateModifiedDescending)
                sort.AscendingOrder = false;
            else
                sort.AscendingOrder = true;
            if (opts.IsDateModifiedAscending || opts.IsDateModifiedDescending)
                sort.PropertyName = "System.DateModified";
            else
                sort.PropertyName = "System.FileName";
            return sort;
        }

        public static QueryOptions GetFileQueryOptions()
        {
            var opts = Options.Instance.FileSortOptions;
            var fileOptions = new QueryOptions(CommonFileQuery.DefaultQuery, FileFilter());
            fileOptions.SortOrder.Clear();
            fileOptions.SortOrder.Add(MakeSortEntry(opts));
            fileOptions.FolderDepth = FolderDepth.Shallow;
            return fileOptions;
        }

        public static QueryOptions GetFolderQueryOptions()
        {
            var opts = Options.Instance.FolderSortOptions;
            var folderOptions = new QueryOptions(CommonFolderQuery.DefaultQuery);
            folderOptions.FolderDepth = FolderDepth.Deep;
            folderOptions.SortOrder.Add(MakeSortEntry(opts));
            return folderOptions;
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
                var folderQuery = parent.CreateFolderQueryWithOptions(folderOptions);
                var unsortedFolders = await folderQuery.GetFoldersAsync();
                subfolders = unsortedFolders.OrderBy(f => f.Path).ToList();
                clock.Report(0, "Querying " + subfolders.Count() + " subfolders of " + parent.Path);
            }, parent.Path);
            return subfolders;
        }
    }

    public class StreamElement
    {
        public int Index { get; set; } = 0;
        public FileElement File { get; set; }
        public IRandomAccessStreamWithContentType Stream { get; set; }
    }


    public abstract class FolderQuery
    {
        protected FolderQuery(StorageFileQueryResult q)
        {
            _query = q;
        }

        public abstract void Launch();
        public abstract Task<bool> HasAtLeast(int n);
        public abstract Task<int> Count();
        public abstract Task<IReadOnlyList<StreamElement>> GetFiles(int startOffset, int endOffset);

        protected async Task<IReadOnlyList<StreamElement>> OpenFileStreams(uint startIndex, uint count)
        {
            var files = await _query.GetFilesAsync(startIndex, count);
            var openTasks = new List<Task<StreamElement>>();
            for (int i = 0; i < files.Count; ++i)
            {
                var type = files[i].FileType;
                if (type != ".url" && type != ".lnk")
                    openTasks.Add(OpenFileStream(files[i]));
            }
            return await Task.WhenAll(openTasks);
        }

        protected async Task<StreamElement> OpenFileStream(StorageFile f)
        {
            StreamElement s = new StreamElement();
            s.File = f;
            s.Stream = await f.OpenReadAsync();
            return s;
        }

        private StorageFileQueryResult _query;
    }

    public class TwoPhaseFolderQuery : FolderQuery
    {
        public TwoPhaseFolderQuery(StorageFileQueryResult query, uint numInitial) : base(query)
        {
            _numInitial = numInitial;
        }

        public override void Launch() { LaunchInitial(); }

        public override async Task<bool> HasAtLeast(int n)
        {
            var i = await InitialCount();
            if (i >= n) return true;
            var r = await RemainingCount();
            return (i + r) >= n;
        }

        public override async Task<int> Count()
        {
            var i = await InitialCount();
            var r = await RemainingCount();
            return i + r;
        }

        public override async Task<IReadOnlyList<StreamElement>> GetFiles(int startOffset, int endOffset)
        {
            var ret = new List<StreamElement>();
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

        private void LaunchInitial()
        {
            lock (_mutex)
            {
                if (_initial == null)
                    _initial = OpenFileStreams(0, _numInitial);
            }
        }

        private void LaunchRemaining()
        {
            lock (_mutex)
            {
                if (_remaining == null)
                    _remaining = OpenFileStreams(_numInitial, uint.MaxValue);
            }
        }

        private async Task<IReadOnlyList<StreamElement>> GetInitial()
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

        private async Task<int> InitialCount()
        {
            var i = await GetInitial();
            return i.Count;
        }

        private async Task<IReadOnlyList<StreamElement>> GetRemaining()
        {
            if (RemainingUnnecessary) return new List<StreamElement>();
            if (RemainingDone) return _remaining.Result;
            LaunchRemaining();
            var ret = await _remaining;
            RemainingDone = true;
            return ret;
        }

        private async Task<int> RemainingCount()
        {
            if (RemainingUnnecessary) return 0;
            var r = await GetRemaining();
            return r.Count;
        }

        private bool Finished() { return InitialDone && RemainingDone; }

        private bool InitialDone { get; set; } = false;
        private bool RemainingDone { get; set; } = false;
        private bool RemainingUnnecessary { get; set; } = false;

        private uint _numInitial;

        private object _mutex = new object();
        private Task<IReadOnlyList<StreamElement>> _initial = null;
        private Task<IReadOnlyList<StreamElement>> _remaining = null;
    }

    public class ShuffledFolderQuery : FolderQuery
    {
        public ShuffledFolderQuery(StorageFileQueryResult query) : base(query)
        {
        }

        public override void Launch() {
            lock (_mutex)
            {
                if (_open == null)
                    _open = GetShuffledStreams();
            }
        }

        public override async Task<bool> HasAtLeast(int n)
        {
            var i = await Count();
            return i >= n;
        }

        public override async Task<int> Count()
        {
            var i = await GetAll();
            return i.Count;
        }

        public override async Task<IReadOnlyList<StreamElement>> GetFiles(int startOffset, int endOffset)
        {
            var ret = new List<StreamElement>();
            var initial = await GetAll();
            int offset = startOffset;
            for (; offset < endOffset && offset < initial.Count; ++offset)
                ret.Add(initial[offset]);
            return ret;
        }

        private async Task<IReadOnlyList<StreamElement>> GetAll()
        {
            if (Done) return _open.Result;
            Launch();
            var ret = await _open;
            Done = true;
            return ret;
        }

        private async Task<IReadOnlyList<StreamElement>> GetShuffledStreams()
        {
            var streams = new List<StreamElement>();
            streams.AddRange(await OpenFileStreams(0, uint.MaxValue));
            var rnd = new Random();
            for (int i = streams.Count - 1; i > 1; i--)
            {
                int r = rnd.Next(i + 1);
                var v = streams[r];
                streams[r] = streams[i];
                streams[i] = v;
            }
            return streams;
        }

        private bool Done { get; set; } = false;

        private object _mutex = new object();
        private Task<IReadOnlyList<StreamElement>> _open = null;
    }

    public class StreamSource : IIncrementalSource<StreamElement>
    {
        public StreamSource(List<StorageFolder> folders)
        {
            var options = FileFetcher.GetFileQueryOptions();
            _queries = new FolderQuery[folders.Count];
            const int kMaxInitialQueries = 1;
            for (int i = 0; i < folders.Count; ++i)
            {
                var query = folders[i].CreateFileQueryWithOptions(options);
                if (Options.Instance.FileSortOptions.IsRandom)
                    _queries[i] = new ShuffledFolderQuery(query);
                else
                    _queries[i] = new TwoPhaseFolderQuery(query, kNumPrimaryImages);
                if (i < kMaxInitialQueries)
                {
                    _queries[i].Launch();
                }
            }
        }

        public async Task<IEnumerable<StreamElement>> GetPagedItemsAsync(int pageIndex, int pageSize, CancellationToken token)
        {
            int startIndex = pageIndex * pageSize;
            int maxIndex = startIndex + pageSize;
            return await GetFilesAsync(startIndex, maxIndex);
        }

        public Int32 LoadCount() { return _loadCount; }

        private async Task<IEnumerable<StreamElement>> GetFilesAsync(int startFileIndex, int maxFileIndex)
        {
            var files = new List<StreamElement>();
            int queriesStartFileIndex = 0;
            int startQuery = 0;
            for (; startQuery < _queries.Count(); ++startQuery)
            {
                int offset = startFileIndex - queriesStartFileIndex;
                var haveItems = await _queries[startQuery].HasAtLeast(offset).ConfigureAwait(false);
                if (haveItems) break;
                queriesStartFileIndex += await _queries[startQuery].Count();
            }

            int frontExtra = startFileIndex - queriesStartFileIndex;
            int desired = maxFileIndex - startFileIndex;         
            for (int i = startQuery; i < _queries.Count(); ++i)
            {
                int deficit = desired - (files.Count() - frontExtra);
                if (deficit <= 0) break;
                var q = _queries[i];
                files.AddRange(await q.GetFiles(0, deficit));
            }
            int numToTake = Math.Min(desired, files.Count - frontExtra);
            Interlocked.Add(ref _loadCount, numToTake);
            var trimmed = files.GetRange(frontExtra, numToTake);
            for (int i = 0; i < trimmed.Count; ++i)
                trimmed[i].Index = i + startFileIndex;
            return trimmed;
        }


        private Int32 _loadCount = 0;

        private const int kNumPrimaryImages = 25;
        private FolderQuery[] _queries;
    }

    public class ImageElement
    {
        public BitmapImage Bitmap { get; set; }
        public int Index { get; set; } = 0;
        public StorageFile File { get; set; }
    }

    public class ImageSource : IIncrementalSource<ImageElement>
    {
        public ImageSource(StreamSource ss)
        {
            _streamSource = ss;
        }

        public async Task<IEnumerable<ImageElement>> GetPagedItemsAsync(int pageIndex, int pageSize, CancellationToken token)
        {
            var tasks = new List<Task<ImageElement>>();
            var files = await _streamSource.GetPagedItemsAsync(pageIndex, pageSize, token);
            foreach (var file in files)
            {
                if (pageIndex < 10)
                {
                    Debug.WriteLine("Opening " + file.File.Path + " : " + pageIndex + " " + pageSize);
                }
                tasks.Add(OpenImageFromStream(file));
            }
            var items = await Task.WhenAll(tasks.ToArray());
            Interlocked.Add(ref _loadCount, files.Count());
            return items;
        }

        public Int32 ImageLoadCount() { return _loadCount; }
        public Int32 FileLoadCount() { return _streamSource.LoadCount(); }

        public string PathForIndex(int index)
        {
            string s;
            if (_indexToFile.TryGetValue(index, out s))
                return s;
            return "";
        }

        // Must run on UI thread.
        private async Task<ImageElement> OpenImageFromStream(StreamElement stream)
        {
            ImageElement element = new ImageElement();
            element.Bitmap = new BitmapImage();
            element.Index = stream.Index;
            element.File = stream.File;
            _indexToFile.TryAdd(element.Index, element.File.Path);
            try
            {
                await element.Bitmap.SetSourceAsync(stream.Stream);
            } catch (TaskCanceledException) { 
            } catch (Exception e)
            {
                if (!FileErrorHelper.SuppressImageErrors)
                {
                    await FileErrorHelper.RaiseImageErrorDialog(stream.File.Path);
                }
            }
            return element;
        }

        private Int32 _loadCount = 0;
        private StreamSource _streamSource;
        private ConcurrentDictionary<int, string> _indexToFile = new ConcurrentDictionary<int, string>();
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

    public class ImageCollection : IncrementalLoadingCollection<ImageSource, ImageElement>
    {
        public const int kPageSize = 1;

        public ImageCollection() : this(null, new List<StorageFolder>()) { }
        public ImageCollection(StorageFolder root, List<StorageFolder> folders) : this(new StreamSource(folders)) {
            if (root != null)
                _name = root.Path;
            else
                _name = "";
            _root = root;
        }

        public ImageSource GetSource() { return _imageSource; }

        private ImageCollection(ImageSource isrc) : base(isrc, kPageSize) {
            _imageSource = isrc;
        }
        private ImageCollection(StreamSource ss) : this(new ImageSource(ss)) {}

        public String Name() { return _name; }
        public StorageFolder Root() { return _root;  }

        private String _name;
        private StorageFolder _root;
        private ImageSource _imageSource { get; set; }
    }

    public class ImageCollectionFactory
    {
        public static async Task<ImageCollection> MakeCollectionAsync(StorageFolder root)
        {
            var folders = await FileFetcher.GetSubfolders(root);
            folders = folders.OrderBy(f => f.Path).ToList();
            if (root != null)
            {
                folders.Add(root);
            }
            var collection = new ImageCollection(root, folders);
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
            _cachedCollections.InsertIfNotPresent(c.Name(), c);
        }

        public void Flush() { _cachedCollections.Flush(); }

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
            Debug.WriteLine("Set CURRENT to " + Current().Name());
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
                Debug.WriteLine("Set CURRENT to " + _current.Name());
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
                Debug.WriteLine("Set CURRENT to " + Current().Name());
                return true;
            }
            return false;
        }

        public async Task Reset()
        {
            var currentFolder = _folderNavigator.Current();
            _cache.Flush();
            await SetCurrentCollection(currentFolder);
        }

        private async Task<ImageCollection> PreloadCollection(StorageFolder folder)
        {
            var c = await _cache.GetOrCreateCollectionAsync(folder).ConfigureAwait(false);
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
            UpdateViewToCurrentCollection();
        }

        public static ImageProperties GlobalImageProperties = new ImageProperties();

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
                var args = e.Parameter as IActivatedEventArgs;
                if (args != null)
                {
                    if (args.Kind == ActivationKind.CommandLineLaunch)
                    {
                        string folderPath = "";
                        var clArgs = args as CommandLineActivatedEventArgs;
                        var op = clArgs.Operation;
                        if (op != null) folderPath = op.Arguments;
                        if (folderPath.Length > 0)
                        await OpenFolderPathAndReloadView(folderPath);
                    } else if (args.Kind == ActivationKind.File)
                    {
                        var fileArgs = args as FileActivatedEventArgs;
                        var file = fileArgs.Files[0] as StorageFile;
                        await FileErrorHelper.RunMethodAsync(async delegate (String p)
                        {
                            var parent = await file.GetParentAsync();
                            await UpdateFromFolder(parent);
                        }, file.Path);
                    }
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

        private void UpdateViewToCurrentCollection()
        {
            var current = _collectionNavigator.Current();
            NextFolderButton.IsEnabled = _collectionNavigator.CanGoNext();
            PreviousFolderButton.IsEnabled = _collectionNavigator.CanGoPrevious();
            var appView = ApplicationView.GetForCurrentView();
            ListViewMain.ItemsSource = current;
            UpdateTitle();
            Debug.WriteLine("Updated UI to " + current.Name());
        }

        private void UpdateTitle()
        {
            var current = _collectionNavigator.Current();
            var appView = ApplicationView.GetForCurrentView();
            string indexString = "";
            string nameString = current.Name();
            var panel = GetChild<ItemsStackPanel>(ListViewMain);
            if (panel != null)
            {
                var visibleItems = GetChildren<ListViewItem>(panel);
                int first = panel.FirstVisibleIndex;
                nameString = current.GetSource().PathForIndex(first);
                indexString = (first + 1).ToString();
            }
            appView.Title = nameString + " - " + indexString + "/" + current.GetSource().FileLoadCount();
        }

        private async Task UpdateUiFromNonUiThread()
        {
            CoreDispatcher coreDispatcher = Window.Current.Dispatcher;
            await coreDispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                UpdateViewToCurrentCollection();
            });
        }

        private async void MoveNext()
        {
            if (!_navSemaphore.Wait(0)) { return; }
            var clock = new WallClockMeasurement();
            if (await _collectionNavigator.MoveNext())
            {
                clock.Report(200, "MOVE NEXT: Navigation");
                UpdateViewToCurrentCollection();
                clock.Report(200, "MOVE NEXT: Update UI");
            }
            _navSemaphore.Release();
        }

        private async void MovePrevious()
        {
            if (!_navSemaphore.Wait(0)) return;
            if (await _collectionNavigator.MovePrevious())
            {
                UpdateViewToCurrentCollection();
            }
            _navSemaphore.Release();
        }

        private bool IsFullScreen()
        {
            return ApplicationView.GetForCurrentView().IsFullScreenMode;
        }
        private bool TryEnterFullScreen()
        {
            var actualWidth = ListViewMain.ActualWidth;
            if (ApplicationView.GetForCurrentView().TryEnterFullScreenMode())
            {
                MainPage.GlobalImageProperties.Width = actualWidth;
                return true;
            }
            return false;
        }
        private void ExitFullScreen()
        {
            MainPage.GlobalImageProperties.Width = double.NaN;
            ApplicationView.GetForCurrentView().ExitFullScreenMode();
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

        private async Task SetFileSortOrder(SortOrder order)
        {
            await _navSemaphore.WaitAsync();
            Options.Instance.FileSortOptions.SetOrder(order);
            await _collectionNavigator.Reset();
            UpdateViewToCurrentCollection();
            _navSemaphore.Release();
        }
        private async void FileSortNameAscending_Click(object sender, RoutedEventArgs e)
        {
            await SetFileSortOrder(SortOrder.NameAscending);
        }
        private async void FileSortNameDescending_Click(object sender, RoutedEventArgs e)
        {
            await SetFileSortOrder(SortOrder.NameDescending);
        }
        private async void FileSortDateModifiedAscending_Click(object sender, RoutedEventArgs e)
        {
            await SetFileSortOrder(SortOrder.DateModifiedAscending);
        }
        private async void FileSortDateModifiedDescending_Click(object sender, RoutedEventArgs e)
        {
            await SetFileSortOrder(SortOrder.DateModifiedDescending);
        }
        private async void FileSortRandom_Click(object sender, RoutedEventArgs e)
        {
            await SetFileSortOrder(SortOrder.Random);
        }

        private void Keyboard_Right(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            MoveNext();
        }
        private void Keyboard_Left(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            MovePrevious();
        }
        private void Keyboard_Enter(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (IsFullScreen())
                ExitFullScreen();
            else
                TryEnterFullScreen();
        }
        private void Keyboard_Escape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            ExitFullScreen();
        }

        private void ListViewMain_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            UpdateTitle();
        }

        private static T GetChild<T>(DependencyObject o) where T : DependencyObject
        {
            if (o != null && o is T) return o as T;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
            {
                var child = VisualTreeHelper.GetChild(o, i);
                var result = GetChild<T>(child);
                if (result != null) return result;
            }
            return default(T);
        }
        private static List<T> GetChildren<T>(DependencyObject o) where T : DependencyObject
        {
            var ret = new List<T>();
            if (o != null && o is T) ret.Add(o as T);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
            {
                var child = VisualTreeHelper.GetChild(o, i);
                var result = GetChild<T>(child);
                if (result != null && result is T) ret.Add(result as T);
            }
            return ret;
        }


        private SemaphoreSlim _navSemaphore = new SemaphoreSlim(1, 1);
        private CollectionNavigator _collectionNavigator = new CollectionNavigator(4, 2);
    }
}