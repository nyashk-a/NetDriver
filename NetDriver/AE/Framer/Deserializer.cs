using System;
using AVcontrol;

namespace NetDriver.AE
{
    internal static class FrameParser
    {
        public static netframe.Header UnpackHeader(byte[] header)
        {
            var output = new netframe.Header(
                FromBinary.LittleEndian<UInt32>(header.AsSpan(0, 4)),
                (netframe.Type)header[4]
            );

            return output;
        }

        public static netframe.Content UnpackContent(byte[] content)
        {
            var output = new netframe.Content(
                new Guid(content.AsSpan(0, 16)),
                content.AsSpan(16).ToArray()
            );

            return output;
        }

        public static netframe UnpackFrame(byte[] frame)
        {
            var h = UnpackHeader(frame.AsSpan(0, 5).ToArray());
            var c = UnpackContent(frame.AsSpan(5).ToArray());

            var output = new netframe(h, c);

            return output;
        }

        public static netframe BuildFrame(netframe.Type t, Guid frameuid, byte[] content)
        {
            var h = new netframe.Header((UInt32)(content.Length + 16), t);

            var c = new netframe.Content(frameuid, content);

            return new netframe(h, c);
        }
    }
}