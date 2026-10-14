using Microsoft.Xna.Framework;

namespace Infiniminer
{
    // The prospector's radar: a narrow cone of rays along the view direction, out to 30 metres.
    public static class Radar
    {
        public const float Range = 30;

        // The distance to the nearest gold or diamond in view and the value of the best one
        // (200 for gold, 1000 for diamonds; 0 and Range when there is nothing).
        public static (float distance, float value) Scan(Func<Vector3, BlockType> blockAt, Vector3 position, Vector3 lookVector)
        {
            float distance = Range, value = 0;
            for (int i = -3; i <= 3; i++)
                for (int j = -3; j <= 3; j++)
                {
                    Matrix rotation = Matrix.CreateRotationX((float)(i * Math.PI / 128)) * Matrix.CreateRotationY((float)(j * Math.PI / 128));
                    Vector3 scanPoint = position;
                    Vector3 direction = Vector3.Transform(lookVector, rotation);
                    for (int k = 0; k < 60; k++)
                    {
                        BlockType blockType = blockAt(scanPoint);
                        if (blockType == BlockType.Gold)
                        {
                            distance = Math.Min(distance, 0.5f * k);
                            value = Math.Max(value, 200);
                        }
                        else if (blockType == BlockType.Diamond)
                        {
                            distance = Math.Min(distance, 0.5f * k);
                            value = Math.Max(value, 1000);
                        }
                        scanPoint += 0.5f * direction;
                    }
                }
            return (distance, value);
        }
    }
}
