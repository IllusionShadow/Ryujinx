using K4os.Compression.LZ4;
using Ryujinx.Common.Logging;
using Ryujinx.Common.Memory;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Graphics.Gpu.Image
{
    public class DiskTextureCache 
    {
        private const long MaxTextureCacheCapacity = 1024 * 1024 * 1024; // 1 GB;

        private readonly ConcurrentDictionary<string, byte[]> _diskTextureCache;

        private readonly ConcurrentStack<string> _usedTextures;

        private readonly CancellationTokenSource _cancellationToken;

        private readonly string _textureCacheFolder;
        private LinkedList<string> _files;

        private long _totalSize;
        private bool _wentOver;

        public DiskTextureCache(string textureCacheFolder) 
        {
            _diskTextureCache = new ConcurrentDictionary<string, byte[]>();
            _usedTextures = new ConcurrentStack<string>();
            _totalSize = 0;
            _wentOver = false;
            _cancellationToken = new CancellationTokenSource();
            _textureCacheFolder = textureCacheFolder;
			if(Directory.Exists(textureCacheFolder))
			{
				_files = new LinkedList<string>(Directory.GetFiles(textureCacheFolder));
                new Task(() => { 
                    InitCache();
                }).Start();
			}
			else
			{
				_files = new LinkedList<string>();
			}   
        }

        private void InitCache()
        {
			if(!Directory.Exists(_textureCacheFolder)) return;

            Logger.Warning?.Print(LogClass.Gpu, "Start of disk texture cache");
			
            while(_files.Count!=0){
                var file = _files.First();
                _files.RemoveFirst();
                if(Path.GetFileName(file).EndsWith(".lz4")) {
                    Add(Path.GetFileNameWithoutExtension(file), LZ4Pickler.Unpickle(File.ReadAllBytes(file)));
                    if(_wentOver) break;
                }
            }

            Logger.Warning?.Print(LogClass.Gpu, "End of disk texture cache. Total Cache: " + (_totalSize / 1000000f) + " MiB");
        } 

        public void Add(string textureId, byte[] texture) 
        {
            if(textureId==null || textureId.Length==0 || _diskTextureCache.ContainsKey(textureId) || texture==null || texture.Length==0) return;

            if(texture.Length+_totalSize > MaxTextureCacheCapacity)
            {
                if(_wentOver) return;
                _wentOver = true;
                StartCleanTask();
            }
            else
            {
                _diskTextureCache.TryAdd(textureId, texture);
                _totalSize += texture.Length;
            }
        }

        public bool IsTextureInCache(string textureId)
        {
            if(textureId!=null && _diskTextureCache.ContainsKey(textureId)) return true;
            return false;
        }

        public MemoryOwner<byte> GetTexture(string textureId)
        {
            if(!IsTextureInCache(textureId)) return null;
            if(_wentOver) _usedTextures.Push(textureId);
            _diskTextureCache.TryGetValue(textureId, out byte[] value);
            return MemoryOwner<byte>.RentCopy(value);
        }

        private void StartCleanTask()
        {
            new Task(() => {
				Logger.Warning?.Print(LogClass.Gpu, "Starting disk texture cache cleaning. Total size: " + (_totalSize / 1000000f) + " MiB");
                while(true){
                    //These textures have been used, remove them and get new textures
                    while(!_usedTextures.IsEmpty)
                    {
                        _usedTextures.TryPop(out string textureId);
						if(_diskTextureCache.ContainsKey(textureId))
						{
							_diskTextureCache.TryRemove(textureId, out byte[] tex);
							_totalSize -= tex.Length;
						}
                    }

                    while(_totalSize < MaxTextureCacheCapacity && _files.Count!=0)
                    {
                        var file = _files.First();
                        _files.RemoveFirst();
                        if(Path.GetFileName(file).EndsWith(".lz4")) {
                            var textureId = Path.GetFileNameWithoutExtension(file);
                            if(IsTextureInCache(textureId)) continue;
                            byte[] texture = LZ4Pickler.Unpickle(File.ReadAllBytes(file));
                            _diskTextureCache.TryAdd(textureId, texture);
                            _totalSize += texture.Length;
                        }
                    }
                    if(_files.Count==0) _files = new LinkedList<string>(Directory.GetFiles(_textureCacheFolder));
                    Thread.Sleep(10000); // 10 seconds
                }
            }, _cancellationToken.Token).Start();
        }

        public void Clear()
        {
            Logger.Info?.Print(LogClass.Gpu, "Clearing the disk texture cache");
			_cancellationToken.Cancel();
            _diskTextureCache?.Clear();
            _usedTextures?.Clear();
            _totalSize = 0;
        }

    }
}