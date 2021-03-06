﻿// Copyright (c) Amer Koleci and contributors.
// Distributed under the MIT license. See the LICENSE file in the project root for more information.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using Vortice.DXGI;
using Vortice.Direct3D;
using SharpGen.Runtime;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;
using Vortice.Mathematics;
using Vortice.Dxc;
using Vortice.Direct3D12.Shader;
using Vortice;
using System.IO;
using System.Drawing;
using Vortice.DXGI.Debug;

namespace HelloDirect3D12
{
    public sealed partial class D3D12GraphicsDevice : IGraphicsDevice
    {
        private const int RenderLatency = 2;

        public readonly Window Window;
        public readonly IDXGIFactory4 DXGIFactory;
        private readonly ID3D12Device2 _d3d12Device;
        private readonly ID3D12DescriptorHeap _rtvHeap;
        private readonly int _rtvDescriptorSize;
        private readonly ID3D12Resource[] _renderTargets;
        private readonly ID3D12CommandAllocator[] _commandAllocators;

        private readonly ID3D12RootSignature _rootSignature;
        private readonly ID3D12PipelineState _pipelineState;

        private readonly ID3D12GraphicsCommandList4 _commandList;

        private readonly ID3D12Resource _vertexBuffer;

        private readonly ID3D12Fence _frameFence;
        private readonly AutoResetEvent _frameFenceEvent;
        private ulong _frameCount;
        private ulong _frameIndex;
        private int _backbufferIndex;

        public static bool IsSupported() => D3D12.IsSupported(FeatureLevel.Level_12_0);

        public D3D12GraphicsDevice(bool validation, Window window)
        {
            if (!IsSupported())
            {
                throw new InvalidOperationException("Direct3D12 is not supported on current OS");
            }

            Window = window;

            if (validation
                && D3D12GetDebugInterface(out ID3D12Debug? debug).Success)
            {
                debug!.EnableDebugLayer();
                debug!.Dispose();
            }
            else
            {
                validation = false;
            }

            DXGIFactory = CreateDXGIFactory2<IDXGIFactory4>(validation);

            for (int adapterIndex = 0; DXGIFactory.EnumAdapters1(adapterIndex, out IDXGIAdapter1 adapter).Success; adapterIndex++)
            {
                AdapterDescription1 desc = adapter.Description1;

                // Don't select the Basic Render Driver adapter.
                if ((desc.Flags & AdapterFlags.Software) != AdapterFlags.None)
                {
                    adapter.Dispose();

                    continue;
                }

                if (D3D12CreateDevice(adapter, FeatureLevel.Level_11_0, out _d3d12Device).Success)
                {
                    adapter.Dispose();

                    break;
                }
            }

            using (IDXGIFactory5? factory5 = DXGIFactory.QueryInterfaceOrNull<IDXGIFactory5>())
            {
                if (factory5 != null)
                {
                    IsTearingSupported = factory5.PresentAllowTearing;
                }
            }

            // Create Command queue.
            GraphicsQueue = _d3d12Device!.CreateCommandQueue<ID3D12CommandQueue>(CommandListType.Direct);
            GraphicsQueue.Name = "Graphics Queue";

            SwapChainDescription1 swapChainDesc = new()
            {
                BufferCount = RenderLatency,
                Width = window.ClientSize.Width,
                Height = window.ClientSize.Height,
                Format = Format.R8G8B8A8_UNorm,
                Usage = Usage.RenderTargetOutput,
                SwapEffect = SwapEffect.FlipDiscard,
                SampleDescription = new SampleDescription(1, 0)
            };

            using (IDXGISwapChain1 swapChain = DXGIFactory.CreateSwapChainForHwnd(GraphicsQueue, window.Handle, swapChainDesc))
            {
                DXGIFactory.MakeWindowAssociation(window.Handle, WindowAssociationFlags.IgnoreAltEnter);

                SwapChain = swapChain.QueryInterface<IDXGISwapChain3>();
                _backbufferIndex = SwapChain.GetCurrentBackBufferIndex();
            }

            _rtvHeap = _d3d12Device.CreateDescriptorHeap<ID3D12DescriptorHeap>(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, RenderLatency));
            _rtvDescriptorSize = _d3d12Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);

            // Create frame resources.
            {
                CpuDescriptorHandle rtvHandle = _rtvHeap.GetCPUDescriptorHandleForHeapStart();

                // Create a RTV for each frame.
                _renderTargets = new ID3D12Resource[RenderLatency];
                for (int i = 0; i < RenderLatency; i++)
                {
                    _renderTargets[i] = SwapChain.GetBuffer<ID3D12Resource>(i);
                    _d3d12Device.CreateRenderTargetView(_renderTargets[i], null, rtvHandle);
                    rtvHandle += _rtvDescriptorSize;
                }
            }

            _commandAllocators = new ID3D12CommandAllocator[RenderLatency];
            for (int i = 0; i < RenderLatency; i++)
            {
                _commandAllocators[i] = _d3d12Device.CreateCommandAllocator<ID3D12CommandAllocator>(CommandListType.Direct);
            }

            RootSignatureDescription1 rootSignatureDesc = new(RootSignatureFlags.AllowInputAssemblerInputLayout);

            _rootSignature = _d3d12Device.CreateRootSignature<ID3D12RootSignature>(rootSignatureDesc);
            InputElementDescription[] inputElementDescs = new[]
            {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 12, 0)
            };

            byte[] vertexShaderByteCode = CompileBytecode(DxcShaderStage.Vertex, "Triangle.hlsl", "VSMain");

            byte[] pixelShaderByteCode = CompileBytecode(DxcShaderStage.Pixel, "Triangle.hlsl", "PSMain");

            PipelineStateStream pipelineStateStream = new PipelineStateStream
            {
                RootSignature = _rootSignature,
                VertexShader = new ShaderBytecode(vertexShaderByteCode),
                PixelShader = new ShaderBytecode(pixelShaderByteCode),
                InputLayout = new InputLayoutDescription(inputElementDescs),
                SampleMask = uint.MaxValue,
                PrimitiveTopology = PrimitiveTopologyType.Triangle,
                RasterizerState = RasterizerDescription.CullCounterClockwise,
                BlendState = BlendDescription.Opaque,
                DepthStencilState = DepthStencilDescription.None,
                RenderTargetFormats = new[] { Format.R8G8B8A8_UNorm },
                DepthStencilFormat = Format.Unknown,
                SampleDescription = new SampleDescription(1, 0)
            };

            _pipelineState = _d3d12Device.CreatePipelineState(pipelineStateStream);
            _commandList = _d3d12Device.CreateCommandList<ID3D12GraphicsCommandList4>(CommandListType.Direct, _commandAllocators[0], _pipelineState);
            _commandList.Close();

            int vertexBufferSize = 3 * Unsafe.SizeOf<VertexPositionColor>();

            _vertexBuffer = _d3d12Device.CreateCommittedResource<ID3D12Resource>(
                new HeapProperties(HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer((ulong)vertexBufferSize),
                ResourceStates.GenericRead);

            VertexPositionColor[] triangleVertices = new VertexPositionColor[]
            {
                  new VertexPositionColor(new Vector3(0f, 0.5f, 0.0f), new Color4(1.0f, 0.0f, 0.0f, 1.0f)),
                  new VertexPositionColor(new Vector3(0.5f, -0.5f, 0.0f), new Color4(0.0f, 1.0f, 0.0f, 1.0f)),
                  new VertexPositionColor(new Vector3(-0.5f, -0.5f, 0.0f), new Color4(0.0f, 0.0f, 1.0f, 1.0f))
            };

            unsafe
            {
                IntPtr bufferData = _vertexBuffer.Map(0);
                ReadOnlySpan<VertexPositionColor> src = new(triangleVertices);
                MemoryHelpers.CopyMemory(bufferData, src);
                _vertexBuffer.Unmap(0);
            }

            // Create synchronization objects.
            _frameFence = _d3d12Device.CreateFence<ID3D12Fence>(0);
            _frameFenceEvent = new AutoResetEvent(false);
        }

        public bool IsTearingSupported { get; }

        public ID3D12Device2 D3D12Device => _d3d12Device;
        public ID3D12CommandQueue GraphicsQueue { get; }

        public IDXGISwapChain3 SwapChain { get; }

        public void Dispose()
        {
            WaitIdle();

            _vertexBuffer.Dispose();

            for (int i = 0; i < RenderLatency; i++)
            {
                _commandAllocators[i].Dispose();
                _renderTargets[i].Dispose();
            }
            _commandList.Dispose();

            _rtvHeap.Dispose();
            _pipelineState.Dispose();
            _rootSignature.Dispose();
            SwapChain.Dispose();
            _frameFence.Dispose();
            GraphicsQueue.Dispose();
            _d3d12Device.Dispose();
            DXGIFactory.Dispose();

            if (DXGIGetDebugInterface1(out IDXGIDebug1? dxgiDebug).Success)
            {
                dxgiDebug!.ReportLiveObjects(DebugAll, ReportLiveObjectFlags.Summary | ReportLiveObjectFlags.IgnoreInternal);
                dxgiDebug!.Dispose();
            }
        }

        public void WaitIdle()
        {
            GraphicsQueue.Signal(_frameFence, ++_frameCount);
            _frameFence.SetEventOnCompletion(_frameCount, _frameFenceEvent);
            _frameFenceEvent.WaitOne();
        }

        public bool DrawFrame(Action<int, int> draw, [CallerMemberName] string? frameName = null)
        {
            _commandAllocators[_frameIndex].Reset();
            _commandList.Reset(_commandAllocators[_frameIndex], _pipelineState);
            _commandList.BeginEvent("Frame");

            // Set necessary state.
            _commandList.SetGraphicsRootSignature(_rootSignature);
            _commandList.RSSetViewport(new Viewport(Window.ClientSize.Width, Window.ClientSize.Height));
            _commandList.RSSetScissorRect(Window.ClientSize.Width, Window.ClientSize.Height);

            // Indicate that the back buffer will be used as a render target.
            _commandList.ResourceBarrierTransition(_renderTargets[_backbufferIndex], ResourceStates.Present, ResourceStates.RenderTarget);

            CpuDescriptorHandle rtv = _rtvHeap.GetCPUDescriptorHandleForHeapStart();
            rtv += _backbufferIndex * _rtvDescriptorSize;

            Color4 clearColor = new Color4(0.0f, 0.2f, 0.4f, 1.0f);

            var renderPassDesc = new RenderPassRenderTargetDescription(rtv,
                new RenderPassBeginningAccess(new ClearValue(Format.R8G8B8A8_UNorm, clearColor)),
                new RenderPassEndingAccess(RenderPassEndingAccessType.Preserve)
                );

            _commandList.BeginRenderPass(renderPassDesc);

            // Call callback.
            draw(Window.ClientSize.Width, Window.ClientSize.Height);

            _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            int stride = Unsafe.SizeOf<VertexPositionColor>();
            int vertexBufferSize = 3 * stride;
            _commandList.IASetVertexBuffers(0, new VertexBufferView(_vertexBuffer.GPUVirtualAddress, vertexBufferSize, stride));
            _commandList.DrawInstanced(3, 1, 0, 0);

            _commandList.EndRenderPass();

            // Indicate that the back buffer will now be used to present.
            _commandList.ResourceBarrierTransition(_renderTargets[_backbufferIndex], ResourceStates.RenderTarget, ResourceStates.Present);
            _commandList.EndEvent();
            _commandList.Close();

            // Execute the command list.
            GraphicsQueue.ExecuteCommandList(_commandList);

            Result result = SwapChain.Present(1, PresentFlags.None);
            if (result.Failure
                && result.Code == Vortice.DXGI.ResultCode.DeviceRemoved.Code)
            {
                return false;
            }

            GraphicsQueue.Signal(_frameFence, ++_frameCount);

            ulong GPUFrameCount = _frameFence.CompletedValue;

            if ((_frameCount - GPUFrameCount) >= RenderLatency)
            {
                _frameFence.SetEventOnCompletion(GPUFrameCount + 1, _frameFenceEvent);
                _frameFenceEvent.WaitOne();
            }

            _frameIndex = _frameCount % RenderLatency;
            _backbufferIndex = SwapChain.GetCurrentBackBufferIndex();

            return true;
        }

        private static byte[] CompileBytecode(DxcShaderStage stage, string shaderName, string entryPoint)
        {
            string assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
            string shaderSource = File.ReadAllText(Path.Combine(assetsPath, shaderName));

            using (var includeHandler = new ShaderIncludeHandler(assetsPath))
            {
                using IDxcResult? results = DxcCompiler.Compile(stage, shaderSource, entryPoint, includeHandler: includeHandler);
                if (results!.GetStatus().Failure)
                {
                    throw new Exception(results!.GetErrors());
                }

                return results.GetObjectBytecodeArray();
            }
        }
    }
}
