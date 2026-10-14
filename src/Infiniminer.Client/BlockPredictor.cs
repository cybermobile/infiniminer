namespace Infiniminer
{
    // Digging and building used to wait a full round trip for the server before anything happened.
    // The client now applies the change it expects immediately and remembers it; the server's block
    // updates are still the truth. A prediction the server refuses (or never confirms) is rolled back.
    public sealed class BlockPredictor
    {
        public const double Timeout = 1.5;

        sealed class Pending
        {
            public int X, Y, Z;
            public BlockType Before, After;
            public double Time;
        }

        readonly Func<int, int, int, BlockType> getBlock;
        readonly Action<int, int, int, BlockType> setBlock;
        readonly List<Pending> pending = new List<Pending>();

        public BlockPredictor(Func<int, int, int, BlockType> getBlock, Action<int, int, int, BlockType> setBlock)
        {
            this.getBlock = getBlock;
            this.setBlock = setBlock;
        }

        public int PendingCount
        {
            get { return pending.Count; }
        }

        public void Predict(int x, int y, int z, BlockType after, double now)
        {
            BlockType before = getBlock(x, y, z);
            if (before == after)
                return;
            setBlock(x, y, z, after);
            pending.Add(new Pending { X = x, Y = y, Z = z, Before = before, After = after, Time = now });
        }

        // The server said what this block is: any prediction about it is settled.
        public void OnServerBlockSet(int x, int y, int z)
        {
            pending.RemoveAll(p => p.X == x && p.Y == y && p.Z == z);
        }

        // The server refused our last build.
        public void OnBuildFailed()
        {
            Pending lastBuild = pending.LastOrDefault(p => p.After != BlockType.None);
            if (lastBuild != null)
                Rollback(lastBuild);
        }

        // Predictions the server never confirmed are undone.
        public void Update(double now)
        {
            foreach (Pending expired in pending.Where(p => now - p.Time > Timeout).ToList())
                Rollback(expired);
        }

        void Rollback(Pending prediction)
        {
            pending.Remove(prediction);
            if (getBlock(prediction.X, prediction.Y, prediction.Z) == prediction.After)
                setBlock(prediction.X, prediction.Y, prediction.Z, prediction.Before);
        }
    }
}
