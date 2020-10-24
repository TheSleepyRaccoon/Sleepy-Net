using System.IO;
using System.Net.Sockets;

namespace Sleepy.Net
{
    public static class NetworkStreamExtensions
    {
        // .Read returns '0' if remote closed the connection but throws an
        // IOException if we voluntarily closed our own connection.
        //
        // let's add a ReadSafely method that returns '0' in both cases so we don't
        // have to worry about exceptions, since a disconnect is a disconnect...
        public static int ReadSafely(this NetworkStream stream, byte[] buffer, int offset, int size)
        {
            try
            {
                return stream.Read(buffer, offset, size);
            }
            catch (IOException)
            {
                return 0;
            }
        }

        // helper function to read EXACTLY 'n' bytes
        // -> default .Read reads up to 'n' bytes. this function reads exactly 'n'
        //    bytes
        // -> this is blocking until 'n' bytes were received
        // -> immediately returns false in case of disconnects
        public static bool ReadExactly(this NetworkStream stream, byte[] buffer, int amount)
        {
            int bytesRead = 0;
            while (bytesRead < amount)
            {
                int result = stream.ReadSafely(buffer, bytesRead, amount - bytesRead);

                if (result == 0) return false; // Disconnected
                bytesRead += result;
            }
            return true;
        }
    }
}