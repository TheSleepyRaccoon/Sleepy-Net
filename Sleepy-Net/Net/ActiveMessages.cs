using System;
using System.Collections.Generic;

namespace Sleepy.Net
{
    public class OngoingMessage
    {
        public ushort Channel;
        public ushort ID;
        public ushort totalParts;
        public byte[] Data;

        public bool[] partsCollected;
        public int partsCollectedNum;
        public bool IsComplete => partsCollectedNum >= totalParts;
        public float Progress => (float)partsCollectedNum / totalParts;

        public OngoingMessage(ushort c, ushort total, int len, ushort id)
        {
            Channel = c;
            ID = id;
            totalParts = total;
            Data = new byte[len];
            partsCollected = new bool[total];
        }

        public bool MessageRecieved(MessagePart message, int maxPacketSize)
        {
            if (!partsCollected[message.Part])
            {
                message.Data.CopyTo(Data, message.Part * maxPacketSize);
                partsCollected[message.Part] = true;
                ++partsCollectedNum;
            }

            return IsComplete;
        }
    }
}