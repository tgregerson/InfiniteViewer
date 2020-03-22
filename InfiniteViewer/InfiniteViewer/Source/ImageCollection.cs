using Microsoft.Toolkit.Uwp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

namespace InfiniteViewer
{
    public class ImageCollection : IncrementalLoadingCollection<ImageSource, ImageElement>
    {
        public const int kPageSize = 1;

        public ImageCollection() : this(null, new List<StorageFolder>()) { }
        public ImageCollection(StorageFolder root, List<StorageFolder> folders) : this(new StreamSource(folders))
        {
            if (root != null)
                _name = root.Path;
            else
                _name = "";
            _root = root;
        }

        public ImageSource GetSource() { return _imageSource; }

        private ImageCollection(ImageSource isrc) : base(isrc, kPageSize)
        {
            _imageSource = isrc;
        }
        private ImageCollection(StreamSource ss) : this(new ImageSource(ss)) { }

        public String Name() { return _name; }
        public StorageFolder Root() { return _root; }

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
}