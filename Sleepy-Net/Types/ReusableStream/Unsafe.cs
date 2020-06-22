namespace Sleepy
{
    /// <summary>
    /// Unsafe memory utilities.
    /// </summary>
    public static class Unsafe
    {
        /// <summary>
        /// This method is not a general-purpose replacement for memcpy or Marshal.Copy, however, it will generally out-perform those methods for byte lengths
        /// of around 400 or less.
        /// </summary>
        /// <param name="src">Buffer to copy from.</param>
        /// <param name="dest">Buffer to copy to.</param>
        /// <param name="count">Number of bytes to copy.</param>
        public static unsafe void MemoryCopy(byte* src, byte* dest, int count)
        {
            int remainder = count;

            if (count > 8)
            {
                int longs = count / 8;
                remainder = count % 8;

                long* srcLong = (long*)src;
                long* destLong = (long*)dest;

                while (longs > 0)
                {
                    *destLong = *srcLong;
                    srcLong++;
                    destLong++;
                    longs--;
                }

                src = (byte*)srcLong;
                dest = (byte*)destLong;
            }

            switch (remainder)
            {
                case 1:
                    *dest = *src;
                    return;
                case 2:
                    *(short*)dest = *(short*)src;
                    return;
                case 3:
                    *(short*)dest = *(short*)src;
                    *(dest + 2) = *(src + 2);
                    return;
                case 4:
                    *(int*)dest = *(int*)src;
                    return;
                case 5:
                    *(int*)dest = *(int*)src;
                    dest[4] = src[4];
                    return;
                case 6:
                    *(int*)dest = *(int*)src;
                    *(short*)(dest + 4) = *(short*)(src + 4);
                    return;
                case 7:
                    *(int*)dest = *(int*)src;
                    *(short*)(dest + 4) = *(short*)(src + 4);
                    dest[6] = src[6];
                    return;
                case 8:
                    *(long*)dest = *(long*)src;
                    return;
            }
        }

        /// <summary>
        /// Returns a hexadecimal string representation of the buffer.
        /// </summary>
        /// <param name="buffer">The bytes to convert to a hex string.</param>
        /// <param name="length">Length of the buffer (in bytes).</param>
        public static unsafe string ToHexString(byte* buffer, int length)
        {
            const int numeric = '0';
            const int alpha = 'a' - 10;

            string str = new string('0', length * 2);
            fixed (char* strPtr = str)
            {
                char* c = strPtr;

                for (int i = 0; i < length; i++)
                {
                    int high = buffer[i] >> 4;
                    *c = (char)(high + (high < 10 ? numeric : alpha));
                    c++;

                    int low = buffer[i] & 0xf;
                    *c = (char)(low + (low < 10 ? numeric : alpha));
                    c++;
                }
            }

            return str;
        }
    }
}