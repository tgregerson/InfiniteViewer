using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;

namespace InfiniteViewer
{
    public class CollectionNavigator
    {
        public CollectionNavigator()
        {
            Recreate();
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
            Recreate();
            await SetCurrentCollection(currentFolder);
        }

        private void Recreate()
        {
            _numAhead = Options.CollectionPrefetchOptions.NumLookAhead;
            _numBehind = Options.CollectionPrefetchOptions.NumLookBehind;
            _cache = new CollectionCache(2 * (int)(_numAhead + _numBehind));
            _populator = new CachePopulator(_cache);
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
}