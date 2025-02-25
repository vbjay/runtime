﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// This type is for the COM source generator and implements part of the COM-specific interactions.
// This API need to be exposed to implement the COM source generator in one form or another.

using System.Diagnostics;

namespace System.Runtime.InteropServices.Marshalling
{
    /// <summary>
    /// Base class for all COM source generated Runtime Callable Wrapper (RCWs).
    /// </summary>
    public sealed unsafe class ComObject : IDynamicInterfaceCastable, IUnmanagedVirtualMethodTableProvider
    {
        private readonly void* _instancePointer;

        /// <summary>
        /// Initialize ComObject instance.
        /// </summary>
        /// <param name="interfaceDetailsStrategy">Strategy for getting details</param>
        /// <param name="iunknownStrategy">Interaction strategy for IUnknown</param>
        /// <param name="cacheStrategy">Caching strategy</param>
        /// <param name="thisPointer">Pointer to the identity IUnknown interface for the object.</param>
        internal ComObject(IIUnknownInterfaceDetailsStrategy interfaceDetailsStrategy, IIUnknownStrategy iunknownStrategy, IIUnknownCacheStrategy cacheStrategy, void* thisPointer)
        {
            InterfaceDetailsStrategy = interfaceDetailsStrategy;
            IUnknownStrategy = iunknownStrategy;
            CacheStrategy = cacheStrategy;
            _instancePointer = IUnknownStrategy.CreateInstancePointer(thisPointer);
        }

        ~ComObject()
        {
            CacheStrategy.Clear(IUnknownStrategy);
            IUnknownStrategy.Release(_instancePointer);
        }

        /// <summary>
        /// Interface details strategy.
        /// </summary>
        private IIUnknownInterfaceDetailsStrategy InterfaceDetailsStrategy { get; }

        /// <summary>
        /// IUnknown interaction strategy.
        /// </summary>
        private IIUnknownStrategy IUnknownStrategy { get; }

        /// <summary>
        /// Caching strategy.
        /// </summary>
        private IIUnknownCacheStrategy CacheStrategy { get; }

        /// <summary>
        /// Returns an IDisposable that can be used to perform a final release
        /// on this COM object wrapper.
        /// </summary>
        /// <remarks>
        /// This property will only be non-null if the ComObject was created using
        /// CreateObjectFlags.UniqueInstance.
        /// </remarks>
        public IDisposable? FinalRelease { get; internal init; }

        /// <inheritdoc />
        RuntimeTypeHandle IDynamicInterfaceCastable.GetInterfaceImplementation(RuntimeTypeHandle interfaceType)
        {
            if (!LookUpVTableInfo(interfaceType, out IIUnknownCacheStrategy.TableInfo info, out int qiResult))
            {
                Marshal.ThrowExceptionForHR(qiResult);
            }
            return info.ManagedType;
        }

        /// <inheritdoc />
        bool IDynamicInterfaceCastable.IsInterfaceImplemented(RuntimeTypeHandle interfaceType, bool throwIfNotImplemented)
        {
            if (!LookUpVTableInfo(interfaceType, out _, out int qiResult))
            {
                if (throwIfNotImplemented)
                {
                    Marshal.ThrowExceptionForHR(qiResult);
                }
                return false;
            }
            return true;
        }

        private bool LookUpVTableInfo(RuntimeTypeHandle handle, out IIUnknownCacheStrategy.TableInfo result, out int qiHResult)
        {
            qiHResult = 0;
            if (!CacheStrategy.TryGetTableInfo(handle, out result))
            {
                IIUnknownDerivedDetails? details = InterfaceDetailsStrategy.GetIUnknownDerivedDetails(handle);
                if (details is null)
                {
                    return false;
                }
                int hr = IUnknownStrategy.QueryInterface(_instancePointer, details.Iid, out void* ppv);
                if (hr < 0)
                {
                    qiHResult = hr;
                    return false;
                }

                result = CacheStrategy.ConstructTableInfo(handle, details, ppv);

                // Update some local cache. If the update fails, we lost the race and
                // then are responsible for calling Release().
                if (!CacheStrategy.TrySetTableInfo(handle, result))
                {
                    bool found = CacheStrategy.TryGetTableInfo(handle, out result);
                    Debug.Assert(found);
                    _ = IUnknownStrategy.Release(ppv);
                }
            }

            return true;
        }

        /// <inheritdoc />
        VirtualMethodTableInfo IUnmanagedVirtualMethodTableProvider.GetVirtualMethodTableInfoForKey(Type type)
        {
            if (!LookUpVTableInfo(type.TypeHandle, out IIUnknownCacheStrategy.TableInfo result, out int qiHResult))
            {
                Marshal.ThrowExceptionForHR(qiHResult);
            }

            return new(result.ThisPtr, result.Table);
        }
    }
}
