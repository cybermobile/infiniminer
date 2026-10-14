using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Infiniminer
{
    public class Camera
    {
        public float Pitch, Yaw;
        public Vector3 Position;
        public Matrix ViewMatrix = Matrix.Identity;
        public Matrix ProjectionMatrix = Matrix.Identity;
        public float FieldOfView = 70;
        readonly GraphicsDevice device;

        public Camera(GraphicsDevice device)
        {
            Pitch = 0;
            Yaw = 0;
            Position = Vector3.Zero;
            this.device = device;
            UpdateProjection();
        }

        // Recomputed every update, so window resizes and field of view changes apply immediately.
        void UpdateProjection()
        {
            this.ProjectionMatrix = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(FieldOfView), device.Viewport.AspectRatio, 0.01f, 1000.0f);
        }

        // Returns a unit vector pointing in the direction that we're looking.
        public Vector3 GetLookVector()
        {
            Matrix rotation = Matrix.CreateRotationX(Pitch) * Matrix.CreateRotationY(Yaw);
            return Vector3.Transform(Vector3.Forward, rotation);
        }

        public Vector3 GetRightVector()
        {
            Matrix rotation = Matrix.CreateRotationX(Pitch) * Matrix.CreateRotationY(Yaw);
            return Vector3.Transform(Vector3.Right, rotation);
        }

        public void Update()
        {
            UpdateProjection();
            Vector3 target = Position + GetLookVector();
            this.ViewMatrix = Matrix.CreateLookAt(Position, target, Vector3.Up);
        }
    }
}
