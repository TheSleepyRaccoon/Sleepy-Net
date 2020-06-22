namespace Sleepy.Net
{
    public class Stats
    {
        // Number Of Bytes
        public ulong RecvBytesTotal;
        public ulong SentBytesTotal;
        public ulong RecvBytesLastSecond;
        public ulong SentBytesLastSecond;
        
        // Number Of Packets
        public ulong RecvTotal; 
        public ulong SentTotal;
        public ulong RecvLastSecond;
        public ulong SentLastSecond;

        ulong totalRecvBytesLast;
        ulong totalSentBytesLast;        
        ulong totalRecvLast;
        ulong totalSentLast;

        public void Flush()
        {
            RecvBytesLastSecond = RecvBytesTotal - totalRecvBytesLast;
            SentBytesLastSecond = SentBytesTotal - totalSentBytesLast;
            totalRecvBytesLast = RecvBytesTotal;
            totalSentBytesLast = SentBytesTotal;

            RecvLastSecond = RecvTotal - totalRecvLast;
            SentLastSecond = SentTotal - totalSentLast;
            totalRecvLast = RecvTotal;
            totalSentLast = SentTotal;
        }

        public static string BytesToString(ulong bytes)
        {
            if (bytes > 1048576f)
            {
                return string.Format("{0:0.00}Mb", bytes / 1048576f);
            }
            if (bytes > 1024f)
            {
                return string.Format("{0:0.00}Kb", bytes / 1024f);
            }
            else
            {
                return string.Format("{0}b", bytes);
            }
        }
    }
}
