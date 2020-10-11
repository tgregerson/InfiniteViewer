using Microsoft.Toolkit.Collections;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;

namespace InfiniteViewer
{
    public class StreamElement
    {
        public int Index { get; set; } = 0;
        public StorageFile File { get; set; }
        public IRandomAccessStreamWithContentType Stream { get; set; }
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
                if (Options.FileSortOptions.IsRandom)
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
            for (; startQuery < _queries.Length; ++startQuery)
            {
                int offset = startFileIndex - queriesStartFileIndex;
                var haveItems = await _queries[startQuery].HasAtLeast(offset).ConfigureAwait(false);
                if (haveItems) break;
                queriesStartFileIndex += await _queries[startQuery].Count();
            }

            int frontExtra = startFileIndex - queriesStartFileIndex;
            int desired = maxFileIndex - startFileIndex;
            for (int i = startQuery; i < _queries.Length; ++i)
            {
                int deficit = desired - (files.Count - frontExtra);
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
}