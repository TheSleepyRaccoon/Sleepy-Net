using Sleepy.Streams;

namespace Sleepy.Net
{
    public interface INetworkable
    {
        bool Init { get; set; }
        ulong ID { get; set; }
        TCP.NetworkedObject ThisObject { get; set; }

        void GetData(ReusableStream writer);
        void RecvData(ReusableStream reader);
    }
}
