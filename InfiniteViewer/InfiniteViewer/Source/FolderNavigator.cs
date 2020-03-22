using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;

namespace InfiniteViewer
{
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
                            for (int i = 0; i < newFamily.Count; ++i)
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
        public StorageFolder Next() { return CurrentOffset(1); }
        public StorageFolder Previous() { return CurrentOffset(-1); }
        public StorageFolder CurrentOffset(int offset)
        {
            lock (_mutex)
            {
                var index = _currentIndex + offset;
                if (_currentIndex >= 0 && index >= 0 && index < _family.Count)
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
            lock (_mutex) return _currentIndex >= 0 && _currentIndex < (_family.Count - 1);
        }
        public bool hasPrevious()
        {
            lock (_mutex) return _currentIndex > 0 && _currentIndex <= _family.Count;
        }

        private object _mutex = new object();
        private int _currentIndex = -1;
        private IReadOnlyList<StorageFolder> _family = new List<StorageFolder>();
    }
}