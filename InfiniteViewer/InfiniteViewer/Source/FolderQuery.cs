using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;

namespace InfiniteViewer
{
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
            Debug.WriteLine("Opening file stream for " + f.DisplayName);
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

        public override void Launch()
        {
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
}