using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace NetDriver.AD
{
    public abstract partial class INetworker
    {
        public async Task ListeningSocket(Socket sock, CancellationToken t)
        {
            while (!t.IsCancellationRequested)
            {
                // header read
                var h = new byte[21];
                int read = 0;
                while (read < h.Length)
                {
                    read += await sock.ReceiveAsync(h.AsMemory(read, h.Length - read));
                }
                read = 0;

                var conf = Message.PartialParse(h);

                var lastFragment = new byte[conf.expectedSize];

                while (read < conf.expectedSize)
                {
                    read += await sock.ReceiveAsync(lastFragment.AsMemory(read, lastFragment.Length - read));
                }

                var msg = new Message(conf, lastFragment);
            }
        }
    }
}
