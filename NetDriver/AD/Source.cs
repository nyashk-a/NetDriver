using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace NetDriver.AD
{
    internal class Request(TaskCompletionSource<Message>? rh)
    {
        public readonly TaskCompletionSource<Message>? rHook = rh;
        public void Activate(Message msg)
        {
            if (rHook != null)
            {
                rHook.TrySetResult(msg);
            }
        }
    }

    internal struct IncomingRequest(Socket sock, Message msg)
    {
        public readonly Socket socket = sock;
        public readonly Message message = msg;
    }

    public enum MassiveSendParametr
    {
        Straight,
        Random,
        Reverse,
    }
}
