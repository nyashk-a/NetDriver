using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;

namespace NetDriver.AD
{
    internal class ContentBuilder
    {
        public static readonly string swapDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads");
        private readonly FileStream _fs;
        private readonly int _chunkSize;
        private readonly int _chunkCount;

        private readonly ConcurrentDictionary<int, byte?> hash = new();
        private int writeNow = 0;

        private readonly Channel<ChunkWriteRQ> writeQueue = Channel.CreateUnbounded<ChunkWriteRQ>();

        public ContentBuilder(string fileName, int chunkSize, int chunkCount, Action<ContentBuilder> disposeBuilder)
        {
            var pathToFile = Path.Combine(swapDir, fileName);
            _chunkSize = chunkSize;
            _chunkCount = chunkCount;

            Directory.CreateDirectory(swapDir);
            _fs = new FileStream(pathToFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);

            var ts = new CancellationTokenSource();
            if (ResourceControl.AnnounceTask(WriterTask(ts.Token).ContinueWith(_ => { disposeBuilder(this) }), ts))
            {
                ts.Cancel();
                ts.Dispose();
            }
        }

        private async Task<bool> AddChunk(byte[] content, int chunkNumb)
        {
            if (!hash.TryAdd(chunkNumb, null)) return false;

            long position = (long)chunkNumb * _chunkSize;

            _fs.Position = position;

            await _fs.WriteAsync(content.AsMemory(16, content.Length - 16));

            return Interlocked.Increment(ref writeNow) == _chunkCount;
        }

        private async Task WriterTask(CancellationToken ct)
        {
            var reader = writeQueue.Reader;

            await foreach (var chunk in reader.ReadAllAsync(ct))
            {
                if (await AddChunk(chunk.Content, chunk.ChunkNumb)) break;
            }
        }

        private struct ChunkWriteRQ(byte[] cnt, int numb)
        {
            public readonly byte[] Content = cnt;
            public readonly int ChunkNumb = numb;
        }
    }
}
