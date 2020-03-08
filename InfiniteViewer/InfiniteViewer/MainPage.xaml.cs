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

namespace InfiniteViewer
{
    class FileErrorHelper
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
        public static async Task<List<StorageFile>> GetFilesAsync(StorageFolder folder)
        {
            var files = new List<StorageFile>();
            if (folder != null)
            {
                await FileErrorHelper.RunMethodAsync(async delegate (String path)
                {
                    List<string> fileTypeFilter = new List<string>();
                    fileTypeFilter.Add(".jpg");
                    fileTypeFilter.Add(".png");
                    fileTypeFilter.Add(".bmp");
                    fileTypeFilter.Add(".gif");
                    var queryOptions = new QueryOptions(CommonFileQuery.OrderByName, fileTypeFilter);
                    queryOptions.FolderDepth = FolderDepth.Deep;
                    var query = folder.CreateFileQueryWithOptions(queryOptions);
                    Debug.WriteLine("Querying files for " + path);
                    var stopwatch = new Stopwatch();
                    stopwatch.Start();
                    IReadOnlyList<StorageFile> unsorted = await query.GetFilesAsync();
                    stopwatch.Stop();
                    Debug.WriteLine("Query took " + stopwatch.ElapsedMilliseconds + "ms");
                    files = unsorted.OrderBy(f => f.Path).ToList();
                    Debug.WriteLine("Got " + files.Count() + " files");
                }, folder.Path);
            }
            return files;
        }

        public static async Task<List<StorageFileQueryResult>> GetFileQueries(StorageFolder parent)
        {
            var queries = new List<StorageFileQueryResult>();
            if (parent != null)
            {
                await FileErrorHelper.RunMethodAsync(async delegate (String path)
                {
                    var stopwatch = new Stopwatch();

                    List<string> fileTypeFilter = new List<string>();
                    fileTypeFilter.Add(".jpg");
                    fileTypeFilter.Add(".png");
                    fileTypeFilter.Add(".bmp");
                    fileTypeFilter.Add(".gif");

                    stopwatch.Restart();
                    var folderOptions = new QueryOptions(CommonFolderQuery.DefaultQuery);
                    folderOptions.FolderDepth = FolderDepth.Deep;
                    // TODO option
                    folderOptions.IndexerOption = IndexerOption.UseIndexerWhenAvailable;
                    var folderQuery = parent.CreateFolderQueryWithOptions(folderOptions);
                    var unsortedFolders = await folderQuery.GetFoldersAsync();
                    var folders = unsortedFolders.OrderBy(f => f.Path).ToList();
                    folders.Add(parent);
                    stopwatch.Stop();
                    Debug.WriteLine("Querying " + folders.Count() + " folders took " + stopwatch.ElapsedMilliseconds + "ms");

                    stopwatch.Restart();
                    var fileOptions = new QueryOptions(CommonFileQuery.OrderByName, fileTypeFilter);
                    fileOptions.IndexerOption = IndexerOption.UseIndexerWhenAvailable;
                    foreach (StorageFolder f in folders)
                    {
                        queries.Add(f.CreateFileQueryWithOptions(fileOptions));
                    }
                }, parent.Path);
            }
            return queries;
        }

        public static async Task<List<StorageFile>> GetFilesAsyncV2(StorageFolder folder)
        {
            var files = new List<StorageFile>();
            if (folder != null)
            {
                await FileErrorHelper.RunMethodAsync(async delegate (String path)
                {
                    var stopwatch = new Stopwatch();

                    List<string> fileTypeFilter = new List<string>();
                    fileTypeFilter.Add(".jpg");
                    fileTypeFilter.Add(".png");
                    fileTypeFilter.Add(".bmp");
                    fileTypeFilter.Add(".gif");

                    stopwatch.Restart();
                    var folderOptions = new QueryOptions(CommonFolderQuery.DefaultQuery);
                    folderOptions.FolderDepth = FolderDepth.Deep;
                    var folderQuery = folder.CreateFolderQueryWithOptions(folderOptions);
                    var unsortedFolders = await folderQuery.GetFoldersAsync();
                    var folders = unsortedFolders.OrderBy(f => f.Path).ToList();
                    folders.Add(folder);
                    stopwatch.Stop();
                    Debug.WriteLine("Querying " + folders.Count() + " folders took " + stopwatch.ElapsedMilliseconds + "ms");

                    stopwatch.Restart();
                    var fileOptions = new QueryOptions(CommonFileQuery.OrderByName, fileTypeFilter);
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
                    stopwatch.Stop();
                    Debug.WriteLine("Starting file queries took " + stopwatch.ElapsedMilliseconds + "ms");

                    stopwatch.Restart();
                    var fileLists = await Task.WhenAll(getFilesTasks.ToArray());
                    stopwatch.Stop();
                    Debug.WriteLine("Querying files took " + stopwatch.ElapsedMilliseconds + "ms");

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

    public class FileSource : IIncrementalSource<StorageFile>
    {
        public FileSource(IReadOnlyList<StorageFileQueryResult> queries)
        {
            _fileQueries = queries;
            int parallelQueries = Math.Min(queries.Count(), 10);
            for (int i = 0; i < parallelQueries; ++i)
            {
                _queryTasks.Add(_fileQueries[i].GetFilesAsync().AsTask());
            }
        }

        public async Task<IEnumerable<StorageFile>> GetPagedItemsAsync(int pageIndex, int pageSize, CancellationToken token)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            int startIndex = pageIndex * pageSize;
            int maxIndex = startIndex + pageSize;
            while (maxIndex > _files.Count() && _nextTaskToProcess < _fileQueries.Count())
            {
                int numTasks = _queryTasks.Count();
                if (numTasks < _fileQueries.Count())
                {
                    _queryTasks.Add(_fileQueries[numTasks].GetFilesAsync().AsTask());
                }
                var files = await _queryTasks[_nextTaskToProcess++];
                _files.AddRange(files);
                Debug.WriteLine("Loaded " + files.Count() + " files. Have " + _files.Count() + " of " + maxIndex + " after completing " +
                                _nextTaskToProcess + " of " + _fileQueries.Count() + " queries");
            }
            if (maxIndex > _files.Count())
            {
                Debug.WriteLine("Finished all queries.");
                maxIndex = _files.Count();
            }
            var retFiles = new List<StorageFile>();
            for (int i = startIndex; i < maxIndex; ++i)
            {
                retFiles.Add(_files[i]);
                // Debug.WriteLine("Returning " + _files[i].Path);
            }
            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds > 5)
            {
                Debug.WriteLine("Returning " + retFiles.Count() + " files took " + stopwatch.ElapsedMilliseconds + "ms");
            }
            return retFiles;
        }

        private IReadOnlyList<StorageFileQueryResult> _fileQueries;
        private List<Task<IReadOnlyList<StorageFile>>> _queryTasks = new List<Task<IReadOnlyList<StorageFile>>>();
        private int _nextTaskToProcess = 0;
        private List<StorageFile> _files = new List<StorageFile>();

    }

    public class ImageSourceV2 : IIncrementalSource<BitmapImage>
    {
        public ImageSourceV2(IReadOnlyList<StorageFileQueryResult> queries)
        {
            _fileSource = new FileSource(queries);
        }

        public async Task<IEnumerable<BitmapImage>> GetPagedItemsAsync(int pageIndex, int pageSize, CancellationToken token)
        {
            var tasks = new List<Task<BitmapImage>>();
            var files = await _fileSource.GetPagedItemsAsync(pageIndex, pageSize, token);
            foreach (var file in files)
            {
                tasks.Add(OpenImageFromFile(file));
            }
            return await Task.WhenAll(tasks.ToArray());
        }

        private async Task<BitmapImage> OpenImageFromFile(StorageFile file)
        {
            CoreDispatcher coreDispatcher = Window.Current.Dispatcher;
            var stream = await file.OpenReadAsync();
            BitmapImage bmp = new BitmapImage();
            await coreDispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                await bmp.SetSourceAsync(stream);
            });
            return bmp;
        }

        private FileSource _fileSource;
    }

    public class ImageSource : IIncrementalSource<BitmapImage>
    {
        public ImageSource(IReadOnlyList<StorageFile> files)
        {
            _files = files;
        }

        public async Task<IEnumerable<BitmapImage>> GetPagedItemsAsync(int pageIndex, int pageSize, CancellationToken token)
        {
            var tasks = new List<Task<BitmapImage>>();
            int start = pageIndex * pageSize;
            int end = Math.Min(_files.Count(), start + pageSize);
            for (int i = start; i < end; ++i)
            {
                var file = _files[i];
                Debug.WriteLine(i.ToString() + ":" + file.Path);
                tasks.Add(OpenImageFromFile(file));
            }
            var images = await Task.WhenAll(tasks.ToArray());
            return images;
        }

        private async Task<BitmapImage> OpenImageFromFile(StorageFile file)
        {
            CoreDispatcher coreDispatcher = Window.Current.Dispatcher;
            var stream = await file.OpenReadAsync();
            BitmapImage bmp = new BitmapImage();
            await coreDispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                await bmp.SetSourceAsync(stream);
            });
            return bmp;
        }

        private IReadOnlyList<StorageFile> _files;
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
                            Debug.WriteLine("Searching for " + curName + " among " + newFamily.Count() + " siblings");
                            for (int i = 0; i < newFamily.Count(); ++i)
                            {
                                if (newFamily[i].Name == curName)
                                {
                                    Debug.WriteLine("Found " + curName + " at position " + i);
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

        public StorageFolder Current()
        {
            lock (_mutex)
            {
                if (_currentIndex >= 0 && _currentIndex < _family.Count())
                {
                    return _family[_currentIndex];
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

    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
            _folderNavigator = new FolderNavigator();
            UpdateFromFolder(null);
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

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
        }

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
            await _navSemaphore.WaitAsync();
            await _folderNavigator.SetCurrentAsync(folder);
            await UpdateUi(folder);
            _navSemaphore.Release();
        }

        private async Task UpdateUi(StorageFolder folder)
        {
            String path = (folder != null) ? folder.Path : "";
            Debug.WriteLine("Setting folder " + path);

            var fetchQueries = FileFetcher.GetFileQueries(folder);

            CoreDispatcher coreDispatcher = Window.Current.Dispatcher;
            await coreDispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                NextFolderButton.IsEnabled = _folderNavigator.hasNext();
                PreviousFolderButton.IsEnabled = _folderNavigator.hasPrevious();

                var queries = await fetchQueries;
                Debug.WriteLine("Updated with " + queries.Count() + " folder queries");

                var appView = Windows.UI.ViewManagement.ApplicationView.GetForCurrentView();
                appView.Title = path;
                var imageSource = new ImageSourceV2(queries);
                ListViewMain.ItemsSource = new IncrementalLoadingCollection<ImageSourceV2, BitmapImage>(imageSource, 1);
            });
        }

        private async void MoveNext()
        {
            await _navSemaphore.WaitAsync();
            if (_folderNavigator.hasNext()) await UpdateUi(_folderNavigator.GoNext());
            _navSemaphore.Release();
        }
        private async void MovePrevious()
        {
            await _navSemaphore.WaitAsync();
            if (_folderNavigator.hasPrevious()) await UpdateUi(_folderNavigator.GoPrevious());
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
        private FolderNavigator _folderNavigator;
    }
}