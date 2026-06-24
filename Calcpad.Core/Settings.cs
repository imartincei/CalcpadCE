using System;
using System.Collections.Generic;
using System.Text;

namespace Calcpad.Core
{
    [Serializable()]
    public class Settings
    {
        public MathSettings Math { get; set; } = new();
        public PlotSettings Plot { get; set; } = new();
        public string Units { get; set; } = "m";

        public ClientFileCache ClientFileCache { get; set; }

        public string SourceFilePath { get; set; }
    }

    [Serializable()]
    public class ClientFileCache
    {
        private sealed class Entry
        {
            public string Filename;
            public byte[] Content;
            public string Error;
            public string DiskGuid;
        }

        private readonly List<Entry> _entries = [];

        public string DiskCacheFolder { get; set; }

        [field: NonSerialized]
        public Func<string, byte[]> RefetchDelegate { get; set; }

        private Entry Find(string filename)
        {
            foreach (var e in _entries)
                if (string.Equals(e.Filename, filename, StringComparison.OrdinalIgnoreCase))
                    return e;
            return null;
        }

        public bool TryGetContent(string filename, out string content)
        {
            content = null;
            if (!TryGetBytes(filename, out var bytes))
                return false;
            content = Encoding.UTF8.GetString(bytes);
            return true;
        }

        public bool TryGetContentMultiKey(string primaryKey, string fallbackKey, out string content) =>
            TryGetContent(primaryKey, out content) ||
            (fallbackKey != null && TryGetContent(fallbackKey, out content));

        public bool TryGetBytes(string filename, out byte[] bytes)
        {
            bytes = null;
            var entry = Find(filename);
            if (entry == null)
                return false;

            if (entry.Content != null)
            {
                bytes = entry.Content;
                return true;
            }

            if (entry.DiskGuid != null && DiskCacheFolder != null)
            {
                if (ClientFileDiskCache.TryRead(DiskCacheFolder, entry.DiskGuid, out bytes))
                    return true;

                if (RefetchDelegate != null)
                {
                    try
                    {
                        bytes = RefetchDelegate(filename);
                        if (bytes != null)
                        {
                            entry.DiskGuid = ClientFileDiskCache.Write(DiskCacheFolder, bytes);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Boundary: the delegate is user-supplied (HTTP fetch, S3, etc.) and can throw anything.
                        // Record the message so TryGetError surfaces it and we stop retrying this entry.
                        entry.Error = ex.Message;
                        entry.DiskGuid = null;
                    }
                }
            }

            return false;
        }

        public bool TryGetError(string filename, out string error)
        {
            error = null;
            var entry = Find(filename);
            if (entry == null)
                return false;
            error = entry.Error;
            return error != null;
        }

        public bool TryGetErrorMultiKey(string primaryKey, string fallbackKey, out string error) =>
            TryGetError(primaryKey, out error) ||
            (fallbackKey != null && TryGetError(fallbackKey, out error));

        public void AddEntry(string filename, byte[] content, string error)
        {
            string diskGuid = null;
            byte[] inlineContent = content;

            if (content != null && content.Length > ClientFileDiskCache.DiskThresholdBytes && DiskCacheFolder != null)
            {
                diskGuid = ClientFileDiskCache.Write(DiskCacheFolder, content);
                inlineContent = null;
            }

            _entries.Add(new Entry
            {
                Filename = filename,
                Content = inlineContent,
                Error = error,
                DiskGuid = diskGuid,
            });
        }
    }

    [Serializable()]
    public class MathSettings
    {
        private int _decimals;
        private int _maxOutputCount;
        public int Decimals
        {
            get => _decimals;
            set
            {
                _decimals = value switch
                {
                    <= 0 => 0,
                    >= 15 => 15,
                    _ => value
                };
            }
        }
        public int Degrees { get; set; }
        public bool IsComplex { get; set; }
        public bool Substitute { get; set; }
        public bool FormatEquations { get; set; }
        public bool ZeroSmallMatrixElements { get; set; }
        public int MaxOutputCount
        {
            get => _maxOutputCount;
            set
            {
                _maxOutputCount = value switch
                {
                    <= 5 => 5,
                    >= 100 => 100,
                    _ => value
                };
            }
        }
        public string FormatString { get; set; }

        public MathSettings()
        {
            Decimals = 2;
            Degrees = 0;
            IsComplex = false;
            Substitute = true;
            FormatEquations = true;
            ZeroSmallMatrixElements = true;
            MaxOutputCount = 20;
        }
    }

    [Serializable()]
    public class PlotSettings
    {
        private bool _shadows;
        public bool IsAdaptive { get; set; }
        public double ScreenScaleFactor { get; set; } = 2.0;
        public string ImagePath { get; set; }
        public string ImageUri { get; set; }
        public bool VectorGraphics { get; set; }
        public ColorScales ColorScale { get; set; }
        public bool SmoothScale { get; set; }
        public bool Shadows
        {
            set => _shadows = value;
            get => _shadows && ColorScale != ColorScales.Gray || ColorScale == ColorScales.None;
        }
        public LightDirections LightDirection { get; set; }

        public enum LightDirections
        {
            North,
            NorthEast,
            East,
            SouthEast,
            South,
            SouthWest,
            West,
            NorthWest
        }

        public enum ColorScales
        {
            None,
            Gray,
            Rainbow,
            Terrain,
            VioletToYellow,
            GreenToYellow,
            Blues,
            BlueToYellow,
            BlueToRed,
            PurpleToYellow,
        }

        public PlotSettings()
        {
            IsAdaptive = true;
            ImagePath = string.Empty;
            ImageUri = string.Empty;
            VectorGraphics = false;
            ColorScale = ColorScales.Rainbow;
            SmoothScale = false;
            Shadows = true;
            LightDirection = LightDirections.NorthWest;
        }
    }
}