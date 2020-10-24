using System.ComponentModel.Design.Serialization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

// =============== Messages ==================

namespace Sleepy.Net
{
    public static partial class MessageTypes
    {
        public const ushort Internal = 0;
        public const ushort Ping = 1;
        public const ushort MessagePartConfirmation = 2;
        public const ushort RSARegistration = 3;
        public const ushort AESMessage = 4;
        public const ushort RSAMessage = 5;
    }

    public static partial class NetworkingVars
    {
        public const int MaxPacketSize = 64000;
        public const int MaxBufferSize = 64512;
        public const double MaxTimeout = 120;
        public const float ConnectAttemptTimeout = 1.5f;
        public const bool NoDelay = true;
        public const int SendTimeout = 5000;
    }

    public static class Utilities
    {
        public static string GetFastestMacAddress(bool verbose = false)
        {
            string macAddress = string.Empty;
            long maxSpeed = -1;

            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (verbose) Log.WriteNow("Found MAC Address: " + nic.GetPhysicalAddress() +
                                          " Type: " + nic.NetworkInterfaceType +
                                          " Status: " + nic.OperationalStatus +
                                          " Description: " + nic.Description);

                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.Speed <= maxSpeed) continue;


                string tempMac = nic.GetPhysicalAddress().ToString();

                if (string.IsNullOrEmpty(tempMac)) continue;

                if (verbose) Log.WriteNow("New Max Speed = " + nic.Speed + ", MAC: " + tempMac);
                maxSpeed = nic.Speed;
                macAddress = tempMac;
            }

            return macAddress;
        }

        public static string GetMacAddress()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                                   .Where(nic => nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback && !string.IsNullOrEmpty(nic.GetPhysicalAddress().ToString()))
                                   .OrderBy(nic => nic.Speed)
                                   .LastOrDefault()
                                   .GetPhysicalAddress().ToString();
        }

        public static string GetIPAddress()
        {
            return new System.Net.WebClient().DownloadString("https://canihazip.com/s");
        }

        public static bool IsValidIP(string ip)
        {
            return IPAddress.TryParse(ip, out _);

            //string[] splits = ip.Split('.');
            //if (splits.Length != 4) return false;
            //foreach(string split in splits)
            //{
            //    if (int.TryParse(split, out int value))
            //    {
            //        if (value > byte.MaxValue || value < 0) return false;
            //    }
            //    else return false;
            //}
            //return true;
        }

        // ============= Byte Util ==================

        // fast int to byte[] conversion and vice versa
        // -> test with 100k conversions:
        //    BitConverter.GetBytes(ushort): 144ms
        //    bit shifting: 11ms
        // -> 10x speed improvement makes this optimization actually worth it
        // -> this way we don't need to allocate BinaryWriter/Reader either
        // -> 4 bytes because some people may want to send messages larger than
        //    64K bytes
        // => big endian is standard for network transmissions, and necessary
        //    for compatibility with erlang
        public static byte[] IntToBytesBigEndian(int value)
        {
            return new byte[] {
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value
            };
        }

        // IntToBytes version that doesn't allocate a new byte[4] each time.
        // -> important for MMO scale networking performance.
        public static void IntToBytesBigEndianNonAlloc(int value, byte[] bytes)
        {
            bytes[0] = (byte)(value >> 24);
            bytes[1] = (byte)(value >> 16);
            bytes[2] = (byte)(value >> 8);
            bytes[3] = (byte)value;
        }

        public static int BytesToIntBigEndian(byte[] bytes)
        {
            return (bytes[0] << 24) |
                   (bytes[1] << 16) |
                   (bytes[2] << 8) |
                   bytes[3];

        }
    }
}
