using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;

namespace InfiniteViewer
{
    public class FileFetcher
    {
        public static List<string> FileFilter()
        {
            List<string> fileTypeFilter = new List<string>();
            fileTypeFilter.Add(".jpg");
            fileTypeFilter.Add(".jpeg");
            fileTypeFilter.Add(".png");
            fileTypeFilter.Add(".bmp");
            fileTypeFilter.Add(".gif");
            fileTypeFilter.Add(".webp");
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

}