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
                
            }
        }
    }
}
