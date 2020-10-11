using System.ComponentModel;
using System.Runtime.CompilerServices;

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

    public class ImageOptions : INotifyPropertyChanged
    {
        public double Width
        {
            get { return this._width; }
            set { _width = value; this.OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged = delegate { };

        public void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private double _width = double.NaN;
    }

    public class CollectionPrefetchOptions
    {
        public uint NumLookAhead { get; set; } = 4;
        public uint NumLookBehind { get; set; } = 2;
    }

    public class Options
    {
        public static SortOptions FileSortOptions { get; set; } = new SortOptions();
        public static SortOptions FolderSortOptions { get; set; } = new SortOptions();
        public static ImageOptions ImageOptions { get; set; } = new ImageOptions();
        public static CollectionPrefetchOptions CollectionPrefetchOptions { get; set; } = new CollectionPrefetchOptions();
    }
}