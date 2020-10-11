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


namespace InfiniteViewer
{

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
                sort.PropertyName = "System.ItemNameDisplay";
            return sort;
        }

        public static QueryOptions GetFileQueryOptions()
        {
            var opts = Options.FileSortOptions;
            var fileOptions = new QueryOptions(CommonFileQuery.DefaultQuery, FileFilter());
            fileOptions.SortOrder.Clear();
            fileOptions.SortOrder.Add(MakeSortEntry(opts));
            fileOptions.FolderDepth = FolderDepth.Shallow;
            return fileOptions;
        }

        public static QueryOptions GetFolderQueryOptions()
        {
            var opts = Options.FolderSortOptions;
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

    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
            UpdateViewToCurrentCollection();
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
                Options.ImageOptions.Width = actualWidth;
                return true;
            }
            return false;
        }
        private void ExitFullScreen()
        {
            Options.ImageOptions.Width = double.NaN;
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
            Options.FileSortOptions.SetOrder(order);
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
        private CollectionNavigator _collectionNavigator = new CollectionNavigator();
    }
}