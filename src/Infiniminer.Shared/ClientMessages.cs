using System.Numerics;
using LiteNetLib.Utils;

namespace Infiniminer
{
    // A message ready to send, with the channel it belongs on.
    public readonly struct OutgoingMessage
    {
        public readonly NetDataWriter Data;
        public readonly NetChannel Channel;

        public OutgoingMessage(NetDataWriter data, NetChannel channel)
        {
            Data = data;
            Channel = channel;
        }

        public InfiniminerMessage Type
        {
            get { return (InfiniminerMessage)Data.Data[0]; }
        }
    }

    // Everything a client sends to the server, encoded in one place. The game client and the tests' bot
    // both use these, so the tests exercise the real wire format.
    public static class ClientMessages
    {
        // The connection request payload (not a message: it travels with LiteNetLib's connect request).
        public static NetDataWriter ConnectRequest(string handle, string version = GameConstants.Version)
        {
            NetDataWriter writer = new NetDataWriter();
            writer.Put(GameConstants.AppIdentifier);
            writer.Put(handle);
            writer.Put(version);
            return writer;
        }

        static OutgoingMessage Message(InfiniminerMessage type, NetChannel channel, Action<NetDataWriter> write = null)
        {
            NetDataWriter writer = NetExtensions.CreateMessage(type);
            write?.Invoke(writer);
            return new OutgoingMessage(writer, channel);
        }

        // Our position and what we're doing, sent 20 times a second. The epoch is the last one the
        // server gave us, so it can ignore updates sent before it moved us.
        public static OutgoingMessage PlayerUpdate(Vector3 position, Vector3 heading, PlayerTools tool, bool usingTool, byte epoch)
        {
            return Message(InfiniminerMessage.PlayerUpdate, NetChannel.UnreliableInOrder1, w =>
            {
                w.Put(position);
                w.Put(heading);
                w.Put((byte)tool);
                w.Put(usingTool);
                w.Put(epoch);
            });
        }

        public static OutgoingMessage UseTool(Vector3 position, Vector3 heading, PlayerTools tool, BlockType blockType = BlockType.None)
        {
            return Message(InfiniminerMessage.UseTool, NetChannel.ReliableUnordered, w =>
            {
                w.Put(position);
                w.Put(heading);
                w.Put((byte)tool);
                w.Put((byte)blockType);
            });
        }

        public static OutgoingMessage SetTeam(PlayerTeam team)
        {
            return Message(InfiniminerMessage.PlayerSetTeam, NetChannel.ReliableInOrder2, w => w.Put((byte)team));
        }

        public static OutgoingMessage SelectClass(PlayerClass playerClass)
        {
            return Message(InfiniminerMessage.SelectClass, NetChannel.ReliableInOrder2, w => w.Put((byte)playerClass));
        }

        // Asks the server to respawn us.
        public static OutgoingMessage Respawn()
        {
            return Message(InfiniminerMessage.PlayerAlive, NetChannel.ReliableInOrder2);
        }

        // Reports a death we noticed ourselves (falling, lava, the suicide key).
        public static OutgoingMessage Died(string deathMessage)
        {
            return Message(InfiniminerMessage.PlayerDead, NetChannel.ReliableInOrder2, w => w.Put(deathMessage));
        }

        public static OutgoingMessage DepositCash()
        {
            return Message(InfiniminerMessage.DepositCash, NetChannel.ReliableInOrder2);
        }

        public static OutgoingMessage DepositOre()
        {
            return Message(InfiniminerMessage.DepositOre, NetChannel.ReliableInOrder2);
        }

        public static OutgoingMessage WithdrawOre()
        {
            return Message(InfiniminerMessage.WithdrawOre, NetChannel.ReliableInOrder2);
        }

        public static OutgoingMessage Chat(ChatMessageType type, string text)
        {
            return Message(InfiniminerMessage.ChatMessage, NetChannel.ReliableInOrder3, w =>
            {
                w.Put((byte)type);
                w.Put(text);
            });
        }

        public static OutgoingMessage Ping(uint myId)
        {
            return Message(InfiniminerMessage.PlayerPing, NetChannel.ReliableUnordered, w => w.Put(myId));
        }

        // Asks the server to play a sound for everyone (only landings and jump blocks are honoured).
        public static OutgoingMessage PlaySound(InfiniminerSound sound, Vector3 position)
        {
            return Message(InfiniminerMessage.PlaySound, NetChannel.ReliableUnordered, w =>
            {
                w.Put((byte)sound);
                w.Put(position);
            });
        }
    }
}
