using System.Numerics;

namespace TestDxApp.Content
{
    /// <summary>
    /// Constant buffer used to send hologram position transform to the shader pipeline.
    /// </summary>
    internal struct ModelConstantBuffer
    {
        public Matrix4x4 model;
    }

    /// <summary>
    /// Used to send per-vertex data to the vertex shader.
    /// </summary>
    internal struct VertexPositionTextureColor
    {
        public VertexPositionTextureColor(Vector3 pos, Vector2 textCoord, Vector3 color)
        {
            this.pos   = pos;
            this.textCoord = textCoord;
            this.color = color;
        }

        public Vector3 pos;
        public Vector2 textCoord;
        public Vector3 color;
    };
}
