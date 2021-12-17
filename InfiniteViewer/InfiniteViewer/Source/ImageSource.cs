using Microsoft.Toolkit.Collections;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml.Media.Imaging;

namespace InfiniteViewer
{
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
            Interlocked.Add(ref _loadCount, items.Length);
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
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception e)
            {
                Debug.WriteLine("Open image exception for " + stream.File.Path + ": " + e.ToString());
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
}