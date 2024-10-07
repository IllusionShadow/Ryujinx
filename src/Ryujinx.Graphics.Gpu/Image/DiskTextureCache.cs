using K4os.Compression.LZ4;
using Ryujinx.Common.Logging;
using Ryujinx.Common.Memory;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Graphics.Gpu.Image
{
    public class DiskTextureCache 
    {
        private const long MaxTextureCacheCapacity = 1024 * 1024 * 1024; // 1 GB;

        private readonly Dictionary<string, byte[]> _diskTextureCache;

        private readonly LinkedList<string> _timeTextureCache;

        private long _totalSize;

        private bool _loadingCache;

        public DiskTextureCache() 
        {
            _diskTextureCache = new Dictionary<string, byte[]>();
            _timeTextureCache = new LinkedList<string>();
            _totalSize = 0;
            _loadingCache = false;
        }

        public DiskTextureCache(string textureCacheFolder) 
        {
            _diskTextureCache = new Dictionary<string, byte[]>();
            _timeTextureCache = new LinkedList<string>();
            _totalSize = 0;

            new Task(() => { 
                InitCache(textureCacheFolder);
            }).Start();
        }

        private void InitCache(string textureCacheFolder)
        {
			if(!Directory.Exists(textureCacheFolder)) return;

            Logger.Warning?.Print(LogClass.Gpu, "Start of disk texture cache");
            _loadingCache = true;
			
            foreach(string file in Directory.GetFiles(textureCacheFolder))
            {
                if(Path.GetFileName(file).EndsWith(".lz4")) {
                    Add(Path.GetFileNameWithoutExtension(file), LZ4Pickler.Unpickle(File.ReadAllBytes(file)), true);
                    if(_totalSize+(10 * 1024 * 1024) > MaxTextureCacheCapacity) break;
                }
            }

            Logger.Warning?.Print(LogClass.Gpu, "End of disk texture cache. Total Cache: " + (_totalSize / 1000000f) + " MiB");
            _loadingCache = false;
        } 

        public void Add(string textureId, byte[] texture, bool fromLoading = false) 
        {
            if(_loadingCache && !fromLoading) return;
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
            if(_loadingCache) return;
            _timeTextureCache.Remove(textureId);
            _timeTextureCache.AddLast(textureId);
        }

        public bool IsTextureInCache(string textureId)
        {
            if(_loadingCache) return false;
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