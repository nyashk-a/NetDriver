using System;
using System.Net.Sockets;

namespace NetDriver.AE
{
    internal class OutcomingController(Socket sock)
    {
        public readonly Socket socket = sock;
    }
}