﻿// Copyright (c) Amer Koleci and contributors.
// Distributed under the MIT license. See the LICENSE file in the project root for more information.

using System;
using System.Runtime.CompilerServices;
using SharpGen.Runtime;
using SharpGen.Runtime.Win32;
using SharpDXGI;
using SharpDXGI.Direct3D;

namespace SharpDirect3D12
{
    public partial class ID3D12Device
    {
        private const int GENERIC_ALL = 0x10000000;

        public static bool IsSupported(IUnknown adapter, FeatureLevel minFeatureLevel = FeatureLevel.Level_11_0)
        {
            try
            {
                var result = D3D12.D3D12CreateDevice(
                   adapter,
                   minFeatureLevel,
                   typeof(ID3D12Device).GUID,
                   out var nativePtr);

                var device = new ID3D12Device(nativePtr);

                device.Dispose();

                return result.Success;
            }
            catch (DllNotFoundException)
            {
                // On pre Windows 10 d3d12.dll is not present and therefore not supported.
                return false;
            }
        }

        public unsafe T CheckFeatureSupport<T>(Feature feature) where T : struct
        {
            T featureSupport = default;
            CheckFeatureSupport(feature, new IntPtr(Unsafe.AsPointer(ref featureSupport)), Interop.SizeOf<T>());
            return featureSupport;
        }

        public unsafe bool CheckFeatureSupport<T>(Feature feature, ref T featureSupport) where T : struct
        {
            return CheckFeatureSupport(feature, new IntPtr(Unsafe.AsPointer(ref featureSupport)), Interop.SizeOf<T>()).Success;
        }

        public unsafe Result CheckMaxSupportedFeatureLevel(FeatureLevel[] featureLevels, out FeatureLevel maxSupportedFeatureLevel)
        {
            fixed (FeatureLevel* levelsPtr = &featureLevels[0])
            {
                var featureData = new FeatureDataFeatureLevels
                {
                    NumFeatureLevels = featureLevels.Length,
                    PFeatureLevelsRequested = new IntPtr(levelsPtr)
                };

                var result = CheckFeatureSupport(Feature.FeatureLevels, new IntPtr(&featureData), Interop.SizeOf<FeatureDataFeatureLevels>());
                maxSupportedFeatureLevel = featureData.MaxSupportedFeatureLevel;
                return result;
            }
        }

        public unsafe FeatureDataGpuVirtualAddressSupport GpuVirtualAddressSupport
        {
            get
            {
                var featureData = new FeatureDataGpuVirtualAddressSupport();
                if (CheckFeatureSupport(Feature.GpuVirtualAddressSupport, new IntPtr(&featureData), Interop.SizeOf<FeatureDataGpuVirtualAddressSupport>()).Success)
                {
                    return featureData;
                }

                return default;
            }
        }

        public unsafe ShaderModel CheckHighestShaderModel(ShaderModel highestShaderModel)
        {
            var featureData = new FeatureDataShaderModel
            {
                HighestShaderModel = highestShaderModel
            };

            if (CheckFeatureSupport(Feature.ShaderModel, new IntPtr(&featureData), Interop.SizeOf<FeatureDataShaderModel>()).Success)
            {
                return featureData.HighestShaderModel;
            }

            return ShaderModel.Model51;
        }

        public unsafe RootSignatureVersion CheckHighestRootSignatureVersion(RootSignatureVersion highestVersion)
        {
            var featureData = new FeatureDataRootSignature
            {
                HighestVersion = highestVersion
            };

            if (CheckFeatureSupport(Feature.RootSignature, new IntPtr(&featureData), Interop.SizeOf<FeatureDataRootSignature>()).Success)
            {
                return featureData.HighestVersion;
            }

            return RootSignatureVersion.Version10;
        }

        public ID3D12Resource CreateCommittedResource(HeapProperties heapProperties,
            HeapFlags heapFlags,
            ResourceDescription description,
            ResourceStates initialResourceState,
            ClearValue? optimizedClearValue = null)
        {
            return CreateCommittedResource(
                ref heapProperties,
                heapFlags,
                ref description,
                initialResourceState,
                optimizedClearValue,
                typeof(ID3D12Resource).GUID);
        }

        public ID3D12CommandQueue CreateCommandQueue(CommandListType type, int priority = 0, CommandQueueFlags flags = CommandQueueFlags.None, int nodeMask = 0)
        {
            return CreateCommandQueue(new CommandQueueDescription(type, priority, flags, nodeMask), typeof(ID3D12CommandQueue).GUID);
        }

        public ID3D12CommandQueue CreateCommandQueue(CommandListType type, CommandQueuePriority priority, CommandQueueFlags flags = CommandQueueFlags.None, int nodeMask = 0)
        {
            return CreateCommandQueue(new CommandQueueDescription(type, priority, flags, nodeMask), typeof(ID3D12CommandQueue).GUID);
        }

        public ID3D12CommandQueue CreateCommandQueue(CommandQueueDescription description)
        {
            return CreateCommandQueue(description, typeof(ID3D12CommandQueue).GUID);
        }

        public ID3D12DescriptorHeap CreateDescriptorHeap(DescriptorHeapDescription description)
        {
            return CreateDescriptorHeap(description, typeof(ID3D12DescriptorHeap).GUID);
        }

        public ID3D12CommandAllocator CreateCommandAllocator(CommandListType type)
        {
            return CreateCommandAllocator(type, typeof(ID3D12CommandAllocator).GUID);
        }

        public ID3D12GraphicsCommandList CreateCommandList(CommandListType type, ID3D12CommandAllocator commandAllocator, ID3D12PipelineState initialState = null)
        {
            return CreateCommandList(0, type, commandAllocator, initialState);
        }

        public ID3D12GraphicsCommandList CreateCommandList(int nodeMask, CommandListType type, ID3D12CommandAllocator commandAllocator, ID3D12PipelineState initialState = null)
        {
            Guard.NotNull(commandAllocator, nameof(commandAllocator));

            var nativePtr = CreateCommandList(nodeMask, type, commandAllocator, initialState, typeof(ID3D12GraphicsCommandList).GUID);
            return new ID3D12GraphicsCommandList(nativePtr);
        }

        public ID3D12Fence CreateFence(ulong initialValue, FenceFlags flags = FenceFlags.None)
        {
            return CreateFence(initialValue, flags, typeof(ID3D12Fence).GUID);
        }

        public ID3D12Heap CreateHeap(HeapDescription description)
        {
            return CreateHeap(ref description, typeof(ID3D12Heap).GUID);
        }

        public ID3D12RootSignature CreateRootSignature(int nodeMask, VersionedRootSignatureDescription rootSignatureDescription)
        {
            Guard.NotNull(rootSignatureDescription, nameof(rootSignatureDescription));

            var result = D3D12.D3D12SerializeVersionedRootSignature(rootSignatureDescription, out var blob, out var errorBlob);
            if (result.Failure)
            {
                if (errorBlob != null)
                {
                    throw new SharpGenException(result, errorBlob.ConvertToString());
                }

                throw new SharpGenException(result);
            }

            try
            {
                return CreateRootSignature(nodeMask, blob.BufferPointer, blob.BufferSize, typeof(ID3D12RootSignature).GUID);
            }
            finally
            {
                blob.Dispose();
            }
        }

        public ID3D12RootSignature CreateRootSignature(VersionedRootSignatureDescription rootSignatureDescription)
        {
            return CreateRootSignature(0, rootSignatureDescription);
        }

        public ID3D12CommandSignature CreateCommandSignature(CommandSignatureDescription description, ID3D12RootSignature rootSignature)
        {
            Guard.NotNull(rootSignature, nameof(rootSignature));

            return CreateCommandSignature(ref description, rootSignature, typeof(ID3D12CommandSignature).GUID);
        }

        public ID3D12PipelineState CreateComputePipelineState(ComputePipelineStateDescription description)
        {
            Guard.NotNull(description, nameof(description));

            return CreateComputePipelineState(description, typeof(ID3D12PipelineState).GUID);
        }

        public ID3D12PipelineState CreateGraphicsPipelineState(GraphicsPipelineStateDescription description)
        {
            Guard.NotNull(description, nameof(description));

            return CreateGraphicsPipelineState(description, typeof(ID3D12PipelineState).GUID);
        }

        public ID3D12QueryHeap CreateQueryHeap(QueryHeapDescription description)
        {
            return CreateQueryHeap(description, typeof(ID3D12QueryHeap).GUID);
        }

        public ID3D12Resource CreatePlacedResource(
            ID3D12Heap heap,
            ulong heapOffset,
            ResourceDescription resourceDescription,
            ResourceStates initialState,
            ClearValue? clearValue = null)
        {
            Guard.NotNull(heap, nameof(heap));

            return CreatePlacedResource(heap, heapOffset, ref resourceDescription, initialState, clearValue, typeof(ID3D12Resource).GUID);
        }

        public ID3D12Resource CreateReservedResource(ResourceDescription resourceDescription, ResourceStates initialState, ClearValue? clearValue = null)
        {
            return CreateReservedResource(ref resourceDescription, initialState, clearValue, typeof(ID3D12Resource).GUID);
        }

        public IntPtr CreateSharedHandle(ID3D12DeviceChild deviceChild, SecurityAttributes? attributes, string name)
        {
            Guard.NotNull(deviceChild, nameof(deviceChild));
            Guard.NotNullOrEmpty(name, nameof(name));

            return CreateSharedHandlePrivate(deviceChild, attributes, GENERIC_ALL, name);
        }

        /// <summary>
        /// Opens a handle for shared resources, shared heaps, and shared fences.
        /// </summary>
        /// <typeparam name="T">The handle that was output by the call to <see cref="CreateSharedHandle(ID3D12DeviceChild, SecurityAttributes?, string)"/> </typeparam>
        /// <param name="handle"></param>
        /// <returns>Instance of <see cref="ID3D12Heap"/>, <see cref="ID3D12Resource"/> or <see cref="ID3D12Fence"/>.</returns>
        public T OpenSharedHandle<T>(IntPtr handle) where T : ComObject
        {
            Guard.IsTrue(handle != IntPtr.Zero, nameof(handle), "Invalid handle");
            var result = OpenSharedHandle(handle, typeof(T).GUID, out var nativePtr);
            if (result.Failure)
            {
                return default;
            }

            return FromPointer<T>(nativePtr);
        }

        /// <summary>
        /// Opens a handle for shared resources, shared heaps, and shared fences, by using Name and Access.
        /// </summary>
        /// <param name="name">The name that was optionally passed as the name parameter in the call to <see cref="CreateSharedHandle(ID3D12DeviceChild, SecurityAttributes?, string)"/> </param>
        /// <returns>The shared handle.</returns>
        public IntPtr OpenSharedHandleByName(string name)
        {
            var result = OpenSharedHandleByName(name, GENERIC_ALL, out var handleRef);
            if (result.Failure)
            {
                return IntPtr.Zero;
            }

            return handleRef;
        }

        public HeapProperties GetCustomHeapProperties(HeapType heapType)
        {
            return GetCustomHeapProperties(0, heapType);
        }

        public void Evict(params ID3D12Pageable[] objects)
        {
            Evict(objects.Length, objects);
        }

        public ResourceAllocationInfo GetResourceAllocationInfo(int visibleMask, params ResourceDescription[] resourceDescriptions)
        {
            return GetResourceAllocationInfo(visibleMask, resourceDescriptions.Length, resourceDescriptions);
        }

        public ResourceAllocationInfo GetResourceAllocationInfo(params ResourceDescription[] resourceDescriptions)
        {
            return GetResourceAllocationInfo(0, resourceDescriptions.Length, resourceDescriptions);
        }
    }
}
