using System.IO.Compression;

namespace Infiniminer
{
    // A world in progress: the blocks, who built them, and how the match stands. Saved (compressed) so a server
    // can pick up where it left off after it is stopped, until the match ends.
    public sealed class WorldState
    {
        public int MapSize;
        public BlockType[,,] Blocks;
        public PlayerTeam[,,] Builders;
        public uint CashRed, CashBlue, OreRed, OreBlue;
        public int SecondsLeft = -1;        // -1: no time limit
        public bool Overtime;
    }

    public static class WorldSave
    {
        const string Magic = "INFINIMINER WORLD";
        const int FormatVersion = 1;

        public static void Save(string path, WorldState world)
        {
            // Written to a temporary file and moved into place, so a crash mid-save can't lose the old one.
            string temporary = path + ".tmp";
            using (FileStream file = File.Create(temporary))
            using (BinaryWriter writer = new BinaryWriter(file))
            {
                WriteSummary(writer, world);
                using DeflateStream deflate = new DeflateStream(file, CompressionLevel.Fastest, leaveOpen: true);
                int size = world.MapSize;
                byte[] layer = new byte[size * size];
                foreach (bool builders in new[] { false, true })
                    for (int x = 0; x < size; x++)
                    {
                        for (int y = 0; y < size; y++)
                            for (int z = 0; z < size; z++)
                                layer[y * size + z] = builders ? (byte)world.Builders[x, y, z] : (byte)world.Blocks[x, y, z];
                        deflate.Write(layer, 0, layer.Length);
                    }
            }
            File.Move(temporary, path, overwrite: true);
        }

        static void WriteSummary(BinaryWriter writer, WorldState world)
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(world.MapSize);
            writer.Write(world.CashRed);
            writer.Write(world.CashBlue);
            writer.Write(world.OreRed);
            writer.Write(world.OreBlue);
            writer.Write(world.SecondsLeft);
            writer.Write(world.Overtime);
        }

        static WorldState ReadSummary(BinaryReader reader)
        {
            if (reader.ReadString() != Magic || reader.ReadInt32() != FormatVersion)
                throw new InvalidDataException("not an Infiniminer world save");
            WorldState world = new WorldState { MapSize = reader.ReadInt32() };
            if (!GameConstants.MapSizes.Contains(world.MapSize))
                throw new InvalidDataException("unsupported world size " + world.MapSize);
            world.CashRed = reader.ReadUInt32();
            world.CashBlue = reader.ReadUInt32();
            world.OreRed = reader.ReadUInt32();
            world.OreBlue = reader.ReadUInt32();
            world.SecondsLeft = reader.ReadInt32();
            world.Overtime = reader.ReadBoolean();
            return world;
        }

        // Just the size and the match standing, without reading the blocks (for "continue" screens).
        public static WorldState Peek(string path)
        {
            try
            {
                using FileStream file = File.OpenRead(path);
                using BinaryReader reader = new BinaryReader(file);
                return ReadSummary(reader);
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        public static WorldState Load(string path)
        {
            using FileStream file = File.OpenRead(path);
            using BinaryReader reader = new BinaryReader(file);
            WorldState world = ReadSummary(reader);
            int size = world.MapSize;
            world.Blocks = new BlockType[size, size, size];
            world.Builders = new PlayerTeam[size, size, size];
            using DeflateStream deflate = new DeflateStream(file, CompressionMode.Decompress);
            byte[] layer = new byte[size * size];
            foreach (bool builders in new[] { false, true })
                for (int x = 0; x < size; x++)
                {
                    deflate.ReadExactly(layer, 0, layer.Length);
                    for (int y = 0; y < size; y++)
                        for (int z = 0; z < size; z++)
                        {
                            byte value = layer[y * size + z];
                            if (builders)
                                world.Builders[x, y, z] = value <= (byte)PlayerTeam.Blue ? (PlayerTeam)value : PlayerTeam.None;
                            else
                                world.Blocks[x, y, z] = value < (byte)BlockType.MAXIMUM ? (BlockType)value : BlockType.None;
                        }
                }
            return world;
        }
    }
}
