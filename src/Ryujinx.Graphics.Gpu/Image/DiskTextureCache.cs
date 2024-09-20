using Ryujinx.Common.Logging;
using Ryujinx.Common.Memory;
using System.Collections.Generic;
using System.IO;

namespace Ryujinx.Graphics.Gpu.Image
{
    public class DiskTextureCache 
    {
        private const long MaxTextureCacheCapacity = 1024 * 1024 * 1024; // 1 GB;

        private readonly Dictionary<string, byte[]> _diskTextureCache;

        private readonly LinkedList<string> _timeTextureCache;

        private long _totalSize;

        public DiskTextureCache() 
        {
            _diskTextureCache = new Dictionary<string, byte[]>();
            _timeTextureCache = new LinkedList<string>();
            _totalSize = 0;
        }

        public DiskTextureCache(string textureCacheFolder) 
        {
            _diskTextureCache = new Dictionary<string, byte[]>();
            _timeTextureCache = new LinkedList<string>();
            _totalSize = 0;

            InitCache(textureCacheFolder);
        }

        private void InitCache(string textureCacheFolder)
        {
			if(!Directory.Exists(textureCacheFolder)) return;

            Logger.Info?.Print(LogClass.Gpu, "Start of disk texture cache");
			
            foreach(string file in Directory.GetFiles(textureCacheFolder))
            {
                Add(Path.GetFileNameWithoutExtension(file), File.ReadAllBytes(file));
                if(_totalSize+(10 * 1024 * 1024) > MaxTextureCacheCapacity) return;
            }

            Logger.Info?.Print(LogClass.Gpu, "End of disk texture cache. Total Cache: " + _totalSize);
        } 

        public void Add(string textureId, byte[] texture) 
        {
            if(_diskTextureCache.ContainsKey(textureId) || texture==null || texture.Length==0) return;

            while(texture.Length+_totalSize > MaxTextureCacheCapacity)
            {
                string value = _timeTextureCache.First.Value;
                _totalSize -= _diskTextureCache[value].Length;
                _diskTextureCache.Remove(value);
                _timeTextureCache.RemoveFirst();
            }

            _diskTextureCache.Add(textureId, texture);
            _timeTextureCache.AddLast(textureId);
            _totalSize += texture.Length;
        }

        public void Lift(string textureId)
        {
            _timeTextureCache.Remove(textureId);
            _timeTextureCache.AddLast(textureId);
        }

        public bool IsTextureInCache(string textureId)
        {
            if(_diskTextureCache.ContainsKey(textureId)) return true;
            return false;
        }

        public MemoryOwner<byte> GetTexture(string textureId)
        {
            if(!_diskTextureCache.ContainsKey(textureId)) return null;
            return MemoryOwner<byte>.RentCopy(_diskTextureCache[textureId]);
        }

        public void Clear()
        {
            Logger.Info?.Print(LogClass.Gpu, "Clearing the disk texture cache");
            _diskTextureCache?.Clear();
            _timeTextureCache?.Clear();
            _totalSize = 0;
        }

    }
}