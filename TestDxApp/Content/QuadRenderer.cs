using SharpDX.Direct2D1;
using SharpDX.Direct3D11;
using SharpDX.DirectWrite;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using System;
using System.Numerics;
using TestDxApp.Common;
using Windows.UI.Input.Spatial;

namespace TestDxApp.Content
{
    /// <summary>
    /// This sample renderer instantiates a basic rendering pipeline.
    /// </summary>
    internal class QuadRenderer : Disposer
    {
        // Cached reference to device resources.
        private DeviceResources deviceResources;

        // Direct3D resources for cube geometry.
        private SharpDX.Direct3D11.InputLayout inputLayout;
        private SharpDX.Direct3D11.Buffer vertexBuffer;
        private SharpDX.Direct3D11.Buffer indexBuffer;
        private SharpDX.Direct3D11.VertexShader vertexShader;
        private SharpDX.Direct3D11.PixelShader pixelShader;
        private SharpDX.Direct3D11.Buffer modelConstantBuffer;

        // System resources for cube geometry.
        private ModelConstantBuffer modelConstantBufferData;
        private int indexCount = 0;
        private Vector3 position = new Vector3(0.0f, 0.0f, -2.0f);

        // Variables used with the rendering loop.
        private bool loadingComplete = false;

        private SamplerState samplerState;
        private Texture2D texture2D;
        private ShaderResourceView shaderResourceView;
        private RenderTargetView renderTargetView;
        private RenderTarget renderTarget;
        private Brush redBrush;
        private Brush whiteBrush;
        private TextFormat textFormat;
        private Ellipse ellipse;
        private RawRectangleF fillRectangle;
        private RawRectangleF textRectangle;
        private int totalTicks;

        /// <summary>
        /// Loads vertex and pixel shaders from files and instantiates the cube geometry.
        /// </summary>
        public QuadRenderer(DeviceResources deviceResources)
        {
            this.deviceResources = deviceResources;

            this.CreateDeviceDependentResourcesAsync();
        }

        // This function uses a SpatialPointerPose to position the world-locked hologram
        // two meters in front of the user's heading.
        public void PositionHologram(SpatialPointerPose pointerPose)
        {
            if (null != pointerPose)
            {
                // Get the gaze direction relative to the given coordinate system.
                Vector3 headPosition = pointerPose.Head.Position;
                Vector3 headDirection = pointerPose.Head.ForwardDirection;

                // The hologram is positioned two meters along the user's gaze direction.
                float distanceFromUser = 2.0f; // meters
                Vector3 gazeAtTwoMeters = headPosition + (distanceFromUser * headDirection);

                // This will be used as the translation component of the hologram's
                // model transform.
                this.position = gazeAtTwoMeters;
            }
        }

        /// <summary>
        /// Called once per frame, rotates the cube and calculates the model and view matrices.
        /// </summary>
        public void Update(StepTimer timer)
        {
            this.totalTicks = (int)timer.TotalTicks;

            // Position the cube.
            Matrix4x4 modelTranslation = Matrix4x4.CreateTranslation(position);

            // The view and projection matrices are provided by the system; they are associated
            // with holographic cameras, and updated on a per-camera basis.
            // Here, we provide the model transform for the sample hologram. The model transform
            // matrix is transposed to prepare it for the shader.
            this.modelConstantBufferData.model = Matrix4x4.Transpose(modelTranslation);

            // Loading is asynchronous. Resources must be created before they can be updated.
            if (!loadingComplete)
            {
                return;
            }

            // Use the D3D device context to update Direct3D device-based resources.
            var context = this.deviceResources.D3DDeviceContext;

            // Update the model transform buffer for the hologram.
            context.UpdateSubresource(ref this.modelConstantBufferData, this.modelConstantBuffer);
        }

        /// <summary>
        /// Renders one frame using the vertex and pixel shaders.
        /// On devices that do not support the D3D11_FEATURE_D3D11_OPTIONS3::
        /// VPAndRTArrayIndexFromAnyShaderFeedingRasterizer optional feature,
        /// a pass-through geometry shader is also used to set the render 
        /// target array index.
        /// </summary>
        public void Render()
        {
            // Loading is asynchronous. Resources must be created before drawing can occur.
            if (!this.loadingComplete)
            {
                return;
            }

            var context = this.deviceResources.D3DDeviceContext;

            context.ClearRenderTargetView(
                this.renderTargetView, new RawColor4(0, 0, 0, 255));

            // Each vertex is one instance of the VertexPositionColor struct.
            int stride = SharpDX.Utilities.SizeOf<VertexPositionTextureColor>();

            int offset = 0;

            var bufferBinding = new SharpDX.Direct3D11.VertexBufferBinding(
                this.vertexBuffer, stride, offset);

            context.InputAssembler.SetVertexBuffers(0, bufferBinding);

            context.InputAssembler.SetIndexBuffer(
                this.indexBuffer,
                SharpDX.DXGI.Format.R16_UInt, // Each index is one 16-bit unsigned integer (short).
                0);

            context.InputAssembler.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;

            context.InputAssembler.InputLayout = this.inputLayout;

            // Attach the vertex shader.
            context.VertexShader.SetShader(this.vertexShader, null, 0);

            // Apply the model constant buffer to the vertex shader.
            context.VertexShader.SetConstantBuffers(0, this.modelConstantBuffer);

            // Attach the pixel shader.
            context.PixelShader.SetShaderResource(0, this.shaderResourceView);
            context.PixelShader.SetSampler(0, this.samplerState);
            context.PixelShader.SetShader(this.pixelShader, null, 0);

            renderTarget.BeginDraw();

            renderTarget.DrawText($"{this.totalTicks} ticks", this.textFormat,
                this.textRectangle, this.whiteBrush, DrawTextOptions.Clip);

            renderTarget.FillEllipse(this.ellipse, this.redBrush);

            renderTarget.EndDraw();

            // Draw the objects.
            context.DrawIndexedInstanced(
                indexCount,     // Index count per instance.
                2,              // Instance count.
                0,              // Start index location.
                0,              // Base vertex location.
                0               // Start instance location.
                );
        }

        /// <summary>
        /// Creates device-based resources to store a constant buffer, cube
        /// geometry, and vertex and pixel shaders. In some cases this will also 
        /// store a geometry shader.
        /// </summary>
        public async void CreateDeviceDependentResourcesAsync()
        {
            ReleaseDeviceDependentResources();

            var folder = Windows.ApplicationModel.Package.Current.InstalledLocation;

            // On devices that do support the D3D11_FEATURE_D3D11_OPTIONS3::
            // VPAndRTArrayIndexFromAnyShaderFeedingRasterizer optional feature
            // we can avoid using a pass-through geometry shader to set the render
            // target array index, thus avoiding any overhead that would be 
            // incurred by setting the geometry shader stage.
            var vertexShaderFileName = "Content\\Shaders\\VPRTVertexShader.cso";

            // Load the compiled vertex shader.
            var vertexShaderByteCode = await DirectXHelper.ReadDataAsync(
                await folder.GetFileAsync(vertexShaderFileName));

            // After the vertex shader file is loaded, create the shader and input layout.
            vertexShader = this.ToDispose(
                new SharpDX.Direct3D11.VertexShader(
                    deviceResources.D3DDevice,
                    vertexShaderByteCode));

            SharpDX.Direct3D11.InputElement[] vertexDesc =
            {
                new SharpDX.Direct3D11.InputElement("POSITION", 0, SharpDX.DXGI.Format.R32G32B32_Float,  0, 0, SharpDX.Direct3D11.InputClassification.PerVertexData, 0),
                new SharpDX.Direct3D11.InputElement("TEXCOORD",    0, SharpDX.DXGI.Format.R32G32_Float, 12, 0, SharpDX.Direct3D11.InputClassification.PerVertexData, 0),
                new SharpDX.Direct3D11.InputElement("COLOR",    0, SharpDX.DXGI.Format.R32G32B32_Float, 20, 0, SharpDX.Direct3D11.InputClassification.PerVertexData, 0),
            };

            inputLayout = this.ToDispose(new SharpDX.Direct3D11.InputLayout(
                deviceResources.D3DDevice,
                vertexShaderByteCode,
                vertexDesc));

            // Load the compiled pixel shader.
            var pixelShaderByteCode = await DirectXHelper.ReadDataAsync(
                await folder.GetFileAsync("Content\\Shaders\\TexturePixelShader.cso"));

            // After the pixel shader file is loaded, create the shader.
            pixelShader = this.ToDispose(new SharpDX.Direct3D11.PixelShader(
                deviceResources.D3DDevice,
                pixelShaderByteCode));

            // Load mesh vertices. Each vertex has a position, a texture-coord and a colour.
            VertexPositionTextureColor[] quadVertices =
            {
                new VertexPositionTextureColor(new Vector3(-(QUAD_SIZE/2.0f), -(QUAD_SIZE/2.0f),  0f), new Vector2(0,1), Vector3.Zero),
                new VertexPositionTextureColor(new Vector3(-(QUAD_SIZE/2.0f),  (QUAD_SIZE/2.0f),  0f), new Vector2(0,0), Vector3.Zero),
                new VertexPositionTextureColor(new Vector3( (QUAD_SIZE/2.0f),  (QUAD_SIZE/2.0f),  0f), new Vector2(1,0), Vector3.Zero),
                new VertexPositionTextureColor(new Vector3( (QUAD_SIZE/2.0f), -(QUAD_SIZE/2.0f),  0f), new Vector2(1,1), Vector3.Zero)
            };

            vertexBuffer = this.ToDispose(SharpDX.Direct3D11.Buffer.Create(
                deviceResources.D3DDevice,
                SharpDX.Direct3D11.BindFlags.VertexBuffer,
                quadVertices));

            // Load mesh indices. Each trio of indices represents
            // a triangle to be rendered on the screen.
            // For example: 0,2,1 means that the vertices with indexes
            // 0, 2 and 1 from the vertex buffer compose the 
            // first triangle of this mesh.
            ushort[] cubeIndices =
            {
                0, 1, 2,
                0, 2, 3
            };

            indexCount = cubeIndices.Length;

            indexBuffer = this.ToDispose(SharpDX.Direct3D11.Buffer.Create(
                deviceResources.D3DDevice,
                SharpDX.Direct3D11.BindFlags.IndexBuffer,
                cubeIndices));

            // Create a constant buffer to store the model matrix.
            modelConstantBuffer = this.ToDispose(SharpDX.Direct3D11.Buffer.Create(
                deviceResources.D3DDevice,
                SharpDX.Direct3D11.BindFlags.ConstantBuffer,
                ref modelConstantBufferData));

            this.samplerState = this.ToDispose(
                new SamplerState(
                    this.deviceResources.D3DDevice,
                    new SamplerStateDescription()
                    {
                        AddressU = TextureAddressMode.Clamp,
                        AddressV = TextureAddressMode.Clamp,
                        AddressW = TextureAddressMode.Clamp,
                        ComparisonFunction = Comparison.Never,
                        MaximumAnisotropy = 16,
                        MipLodBias = 0,
                        MinimumLod = -float.MaxValue,
                        MaximumLod = float.MaxValue
                    }
                )
            );

            Texture2DDescription desc = new Texture2DDescription()
            {
                Width = TEXTURE_SIZE,
                Height = 640,
                ArraySize = 1,
                MipLevels = 1,
                Usage = ResourceUsage.Default,
                Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                CpuAccessFlags = CpuAccessFlags.Write | CpuAccessFlags.Read,
                SampleDescription = new SampleDescription(1, 0),
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource
            };
            this.texture2D = this.ToDispose(new Texture2D(this.deviceResources.D3DDevice, desc));

            this.shaderResourceView = this.ToDispose(new ShaderResourceView(
                this.deviceResources.D3DDevice, texture2D));

            this.renderTargetView = this.ToDispose(new RenderTargetView(
                this.deviceResources.D3DDevice, texture2D));

            using (var surface = texture2D.QueryInterface<Surface>())
            {
                this.renderTarget = this.ToDispose(
                    new RenderTarget(
                        this.deviceResources.D2DFactory,
                        surface,
                        new RenderTargetProperties()
                        {
                            DpiX = 96,
                            DpiY = 96,
                            Type = RenderTargetType.Default,
                            Usage = RenderTargetUsage.None,
                            MinLevel = FeatureLevel.Level_DEFAULT,
                            PixelFormat = new PixelFormat(Format.B8G8R8A8_UNorm, SharpDX.Direct2D1.AlphaMode.Premultiplied)
                        }
                    )
                );
            }
            this.redBrush = this.ToDispose(new SolidColorBrush(renderTarget, new RawColor4(255, 0, 0, 255)));

            this.whiteBrush = this.ToDispose(new SolidColorBrush(renderTarget, new RawColor4(255, 255, 255, 255)));

            this.textFormat = this.ToDispose(new TextFormat(this.deviceResources.DWriteFactory, "Consolas", 24));

            this.ellipse = new Ellipse(new RawVector2(TEXTURE_SIZE / 2, TEXTURE_SIZE / 2), TEXTURE_SIZE / 4, TEXTURE_SIZE / 4);

            this.fillRectangle = new RawRectangleF(0, 0, TEXTURE_SIZE, TEXTURE_SIZE);
            this.textRectangle = new RawRectangleF(24, 24, 240, 60); // magic numbers, hard-coded to fit.

            // Once the cube is loaded, the object is ready to be rendered.
            loadingComplete = true;
        }

        /// <summary>
        /// Releases device-based resources.
        /// </summary>
        public void ReleaseDeviceDependentResources()
        {
            loadingComplete = false;

            this.RemoveAndDispose(ref this.redBrush);
            this.RemoveAndDispose(ref this.whiteBrush);
            this.RemoveAndDispose(ref this.textFormat);
            this.RemoveAndDispose(ref this.samplerState);
            this.RemoveAndDispose(ref this.texture2D);
            this.RemoveAndDispose(ref this.shaderResourceView);
            this.RemoveAndDispose(ref this.renderTargetView);
            this.RemoveAndDispose(ref this.renderTarget);

            this.RemoveAndDispose(ref vertexShader);
            this.RemoveAndDispose(ref inputLayout);
            this.RemoveAndDispose(ref pixelShader);
            this.RemoveAndDispose(ref modelConstantBuffer);
            this.RemoveAndDispose(ref vertexBuffer);
            this.RemoveAndDispose(ref indexBuffer);
        }

        public Vector3 Position
        {
            get { return position; }
            set { position = value; }
        }
        static readonly int TEXTURE_SIZE = 720;
        static readonly float QUAD_SIZE = 1.0f;
    }
}
