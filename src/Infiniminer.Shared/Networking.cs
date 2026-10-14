using System.Numerics;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Infiniminer
{
    // The original game used Lidgren's numbered channels. Each one maps onto a LiteNetLib channel number
    // plus delivery method so that the same ordering guarantees hold.
    public readonly struct NetChannel
    {
        public readonly byte Number;
        public readonly DeliveryMethod Method;

        public NetChannel(byte number, DeliveryMethod method)
        {
            Number = number;
            Method = method;
        }

        public const int ChannelCount = 4;

        // Map chunks and block changes share one ordered channel, so a block change can never be
        // overwritten by a map chunk that arrives late.
        public static readonly NetChannel World = new NetChannel(0, DeliveryMethod.ReliableOrdered);
        public static readonly NetChannel ReliableUnordered = new NetChannel(0, DeliveryMethod.ReliableUnordered);
        public static readonly NetChannel ReliableInOrder1 = new NetChannel(1, DeliveryMethod.ReliableOrdered);
        public static readonly NetChannel ReliableInOrder2 = new NetChannel(2, DeliveryMethod.ReliableOrdered);
        public static readonly NetChannel ReliableInOrder3 = new NetChannel(3, DeliveryMethod.ReliableOrdered);
        public static readonly NetChannel UnreliableInOrder1 = new NetChannel(1, DeliveryMethod.Sequenced);
    }

    public static class NetExtensions
    {
        public static NetDataWriter CreateMessage(InfiniminerMessage type)
        {
            NetDataWriter writer = new NetDataWriter();
            writer.Put((byte)type);
            return writer;
        }

        public static void Send(this NetPeer peer, NetDataWriter writer, NetChannel channel)
        {
            if (peer != null && peer.ConnectionState == ConnectionState.Connected)
                peer.Send(writer, channel.Number, channel.Method);
        }

        public static void Put(this NetDataWriter writer, Vector3 value)
        {
            writer.Put(value.X);
            writer.Put(value.Y);
            writer.Put(value.Z);
        }

        public static Vector3 GetVector3(this NetDataReader reader)
        {
            float x = reader.GetFloat();
            float y = reader.GetFloat();
            float z = reader.GetFloat();
            return new Vector3(x, y, z);
        }

        public static NetDataWriter StringPayload(string text)
        {
            NetDataWriter writer = new NetDataWriter();
            writer.Put(text);
            return writer;
        }
    }

    public static class MapChunk
    {
        // Map chunks are mostly runs of dirt and air, so they compress very well.
        public static byte[] Compress(byte[] data)
        {
            using MemoryStream output = new MemoryStream();
            using (System.IO.Compression.DeflateStream deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Fastest))
                deflate.Write(data, 0, data.Length);
            return output.ToArray();
        }

        public static byte[] Decompress(byte[] data, int expectedLength)
        {
            using System.IO.Compression.DeflateStream inflate = new System.IO.Compression.DeflateStream(new MemoryStream(data), System.IO.Compression.CompressionMode.Decompress);
            byte[] result = new byte[expectedLength];
            int read = 0, n;
            while (read < expectedLength && (n = inflate.Read(result, read, expectedLength - read)) > 0)
                read += n;
            if (read != expectedLength)
                throw new InvalidDataException("truncated map chunk");
            return result;
        }
    }

    // Information a server returns in reply to a LAN discovery broadcast.
    public class DiscoveryInfo
    {
        public string InstanceId;
        public string ServerName;
        public int PlayerCount;
        public int MaxPlayers;
        public string Extra;

        public void Write(NetDataWriter writer)
        {
            writer.Put(GameConstants.DiscoveryResponse);
            writer.Put(InstanceId);
            writer.Put(ServerName);
            writer.Put(PlayerCount);
            writer.Put(MaxPlayers);
            writer.Put(Extra);
        }

        public static DiscoveryInfo TryRead(NetDataReader reader)
        {
            try
            {
                if (reader.GetString() != GameConstants.DiscoveryResponse)
                    return null;
                DiscoveryInfo info = new DiscoveryInfo();
                info.InstanceId = reader.GetString();
                info.ServerName = reader.GetString();
                info.PlayerCount = reader.GetInt();
                info.MaxPlayers = reader.GetInt();
                info.Extra = reader.GetString();
                return info;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
