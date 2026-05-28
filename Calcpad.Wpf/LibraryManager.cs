using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace Calcpad.Wpf
{
    internal abstract class LibraryNode : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public string Name { get; protected set; }
        public string FullPath { get; protected set; }
        public ObservableCollection<LibraryNode> Children { get; } = new();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnExpanded();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }

        protected virtual void OnExpanded() { }
    }

    internal sealed class LibraryFolderNode : LibraryNode
    {
        public bool Exists { get; private set; }

        public LibraryFolderNode(string path)
        {
            FullPath = path;
            Refresh();
        }

        public void Refresh()
        {
            Children.Clear();
            Exists = Directory.Exists(FullPath);
            Name = Exists ? new DirectoryInfo(FullPath).Name : Path.GetFileName(FullPath);
            if (!Exists) return;

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(FullPath, "*.cpd", SearchOption.AllDirectories); }
            catch { return; }

            foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                Children.Add(new LibraryFileNode(file));
        }
    }

    internal sealed class LibraryFileNode : LibraryNode
    {
        private bool _scanned;

        public LibraryFileNode(string path)
        {
            FullPath = path;
            Name = Path.GetFileName(path);
            // Add a placeholder so the TreeView shows an expansion arrow.
            Children.Add(new LibraryPlaceholderNode());
        }

        protected override void OnExpanded()
        {
            if (_scanned || !IsExpanded) return;
            _scanned = true;
            Children.Clear();
            foreach (var fn in LibraryScanner.ScanFile(FullPath))
                Children.Add(new LibraryFunctionNode(FullPath, fn));
            if (Children.Count == 0)
                Children.Add(new LibraryPlaceholderNode(MainWindowResources.Library_NoFunctionsFound));
        }

        public void Rescan()
        {
            _scanned = false;
            Children.Clear();
            Children.Add(new LibraryPlaceholderNode());
            if (IsExpanded) OnExpanded();
        }
    }

    internal sealed class LibraryFunctionNode : LibraryNode
    {
        public LibraryFunction Function { get; }
        public string FilePath { get; }

        public LibraryFunctionNode(string filePath, LibraryFunction fn)
        {
            FilePath = filePath;
            Function = fn;
            Name = string.IsNullOrEmpty(fn.Signature) ? fn.Name : $"{fn.Name}({fn.Signature})";
            FullPath = filePath;
        }
    }

    internal sealed class LibraryPlaceholderNode : LibraryNode
    {
        public LibraryPlaceholderNode(string label = null)
        {
            Name = label ?? "…";
            FullPath = string.Empty;
        }
    }

    internal sealed class LibraryManager
    {
        public ObservableCollection<LibraryFolderNode> Folders { get; } = new();

        public void LoadFromSettings(StringCollection persisted)
        {
            Folders.Clear();
            if (persisted is null) return;
            foreach (var path in persisted)
                if (!string.IsNullOrWhiteSpace(path))
                    Folders.Add(new LibraryFolderNode(path));
        }

        public StringCollection ToSettings()
        {
            var sc = new StringCollection();
            foreach (var f in Folders)
                sc.Add(f.FullPath);
            return sc;
        }

        public bool AddFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var full = Path.GetFullPath(path);
            if (Folders.Any(f => string.Equals(f.FullPath, full, StringComparison.OrdinalIgnoreCase)))
                return false;
            Folders.Add(new LibraryFolderNode(full));
            return true;
        }

        public void RemoveFolder(LibraryFolderNode node)
        {
            Folders.Remove(node);
        }

        public void Refresh()
        {
            foreach (var f in Folders)
                f.Refresh();
        }
    }
}
