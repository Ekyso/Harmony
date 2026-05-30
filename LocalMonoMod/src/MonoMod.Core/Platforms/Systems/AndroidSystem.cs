using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using MonoMod.Core.Interop;
using MonoMod.Core.Platforms.Memory;
using MonoMod.Core.Utils;
using MonoMod.Utils;

namespace MonoMod.Core.Platforms.Systems
{
    internal sealed class AndroidSystem : ISystem, IInitialize<IArchitecture>
    {
        public OSKind Target => OSKind.Android;

        public SystemFeature Features => SystemFeature.RWXPages | SystemFeature.RXPages;

        private readonly Abi defaultAbi;
        public Abi? DefaultAbi => defaultAbi;

        private readonly nint PageSize;

        private readonly MmapPagedMemoryAllocator allocator;
        public IMemoryAllocator MemoryAllocator => allocator;

        private IArchitecture? architecture;

        public INativeExceptionHelper? NativeExceptionHelper
        {
            get
            {
                // No native exception helper available for Android
                throw new NotImplementedException();
            }
        }

        public AndroidSystem()
        {
            PageSize = (nint)Unix.Sysconf((Unix.SysconfName)39);
            allocator = new MmapPagedMemoryAllocator(PageSize);

            switch (PlatformDetection.Architecture)
            {
                case ArchitectureKind.Arm64:
                    defaultAbi = new Abi(
                        new[]
                        {
                            SpecialArgumentKind.ThisPointer,
                            SpecialArgumentKind.UserArguments
                        },
                        SystemVABI.ClassifyARM64,
                        true
                    );
                    break;
                case ArchitectureKind.x86_64:
                    defaultAbi = new Abi(
                        new[] { SpecialArgumentKind.ReturnBuffer, SpecialArgumentKind.ThisPointer, SpecialArgumentKind.UserArguments },
                        SystemVABI.ClassifyAMD64,
                        true
                    );
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        public void Initialize(IArchitecture arch)
        {
            architecture = arch;
        }

        public IEnumerable<string?> EnumerateLoadedModuleFiles()
        {
            return Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Select(m => m.FileName)!;
        }

        public nint GetSizeOfReadableMemory(IntPtr start, nint guess)
        {
            var currentPage = allocator.RoundDownToPageBoundary(start);
            if (!MmapPagedMemoryAllocator.PageReadable(currentPage))
            {
                return 0;
            }
            currentPage += PageSize;

            var known = currentPage - start;

            while (known < guess)
            {
                if (!MmapPagedMemoryAllocator.PageReadable(currentPage))
                {
                    return known;
                }
                known += PageSize;
                currentPage += PageSize;
            }

            return known;
        }

        public unsafe void PatchData(PatchTargetKind patchKind, IntPtr patchTarget, ReadOnlySpan<byte> data, Span<byte> backup)
        {
            var target = new Span<byte>((void*)patchTarget, data.Length);
            _ = target.TryCopyTo(backup);
            data.CopyTo(target);
        }

        private sealed class MmapPagedMemoryAllocator : PagedMemoryAllocator
        {
            public MmapPagedMemoryAllocator(nint pageSize)
                : base(pageSize)
            {
            }

            [SuppressMessage("Design", "CA1032:Implement standard exception constructors")]
            [SuppressMessage("Design", "CA1064:Exceptions should be public",
                Justification = "This is used exclusively internally as jank control flow because I'm lazy")]
            private sealed class SyscallNotImplementedException : Exception { }

            private static int PageProbePipeReadFD, PageProbePipeWriteFD;

            [SuppressMessage("Design", "CA1065:Do not raise exceptions in unexpected locations",
                Justification = "If the exception is thrown, the application is in an unrecoverable state.")]
            [SuppressMessage("Performance", "CA1810:Initialize reference type static fields inline",
                Justification = "There is no good way to inline the initialization here.")]
            static unsafe MmapPagedMemoryAllocator()
            {
                var pipefd = stackalloc int[2];
                if (Unix.Pipe2(pipefd, Unix.PipeFlags.CloseOnExec) == -1)
                {
                    throw new Win32Exception(Unix.Errno, "Failed to create pipe for page probes");
                }

                PageProbePipeReadFD = pipefd[0];
                PageProbePipeWriteFD = pipefd[1];
            }

            public static unsafe bool PageAllocated(nint page)
            {
                byte garbage;
                if (Unix.Mincore(page, 1, &garbage) == -1)
                {
                    var lastError = Unix.Errno;
                    return lastError switch
                    {
                        12 => false, // ENOMEM, page is unallocated
                        38 => throw new SyscallNotImplementedException(), // ENOSYS
                        _ => throw new NotImplementedException($"Got unimplemented errno for mincore(2); errno = {lastError}"),
                    };
                }
                return true;
            }

            public static unsafe bool PageReadable(nint page)
            {
                if (Unix.Write(PageProbePipeWriteFD, page, 1) == -1)
                {
                    var lastError = Unix.Errno;
                    if (lastError == 14) // EFAULT
                    {
                        return false;
                    }
                    throw new NotImplementedException($"Got unimplemented errno for write(2); errno = {lastError}");
                }

                byte garbage;
                if (Unix.Read(PageProbePipeReadFD, new IntPtr(&garbage), 1) == -1)
                {
                    throw new Win32Exception("Failed to clean up page probe pipe after successful page probe");
                }

                return true;
            }

            private bool canTestPageAllocation = true;

            protected override bool TryAllocateNewPage(AllocationRequest request, [MaybeNullWhen(false)] out IAllocatedMemory allocated)
            {
                var prot = request.Executable ? Unix.Protection.Execute : Unix.Protection.None;
                prot |= Unix.Protection.Read | Unix.Protection.Write;

                var mmapPtr = Unix.Mmap(IntPtr.Zero, (nuint)PageSize, prot, Unix.MmapFlags.Private | Unix.MmapFlags.Anonymous, -1, 0);
                if (mmapPtr is 0 or -1)
                {
                    var errno = Unix.Errno;
                    MMDbgLog.Error($"Error creating allocation: {errno} {new Win32Exception(errno).Message}");
                    allocated = null;
                    return false;
                }

                var page = new Page(this, mmapPtr, (uint)PageSize, request.Executable);
                InsertAllocatedPage(page);

                if (!page.TryAllocate((uint)request.Size, (uint)request.Alignment, out var pageAlloc))
                {
                    RegisterForCleanup(page);
                    allocated = null;
                    return false;
                }

                allocated = pageAlloc;
                return true;
            }

            protected override bool TryAllocateNewPage(
                PositionedAllocationRequest request,
                nint targetPage, nint lowPageBound, nint highPageBound,
                [MaybeNullWhen(false)] out IAllocatedMemory allocated
            )
            {
                if (!canTestPageAllocation)
                {
                    allocated = null;
                    return false;
                }

                var prot = request.Base.Executable ? Unix.Protection.Execute : Unix.Protection.None;
                prot |= Unix.Protection.Read | Unix.Protection.Write;

                var numPages = request.Base.Size / PageSize + 1;

                var low = targetPage - PageSize;
                var high = targetPage;
                nint ptr = -1;

                try
                {
                    while (low >= lowPageBound || high <= highPageBound)
                    {
                        if (high <= highPageBound)
                        {
                            for (nint i = 0; i < numPages; i++)
                            {
                                if (PageAllocated(high + PageSize * i))
                                {
                                    high += PageSize;
                                    goto FailHigh;
                                }
                            }
                            ptr = high;
                            break;
                        }
                        FailHigh:
                        if (low >= lowPageBound)
                        {
                            for (nint i = 0; i < numPages; i++)
                            {
                                if (PageAllocated(low + PageSize * i))
                                {
                                    low -= PageSize;
                                    goto FailLow;
                                }
                            }
                            ptr = low;
                            break;
                        }
                        FailLow:
                        { }
                    }
                }
                catch (SyscallNotImplementedException)
                {
                    canTestPageAllocation = false;
                    allocated = null;
                    return false;
                }

                if (ptr == -1)
                {
                    allocated = null;
                    return false;
                }

                var mmapPtr = Unix.Mmap(ptr, (nuint)PageSize, prot, Unix.MmapFlags.Private | Unix.MmapFlags.Anonymous | Unix.MmapFlags.FixedNoReplace, -1, 0);
                if (mmapPtr is 0 or -1)
                {
                    allocated = null;
                    return false;
                }

                var page = new Page(this, mmapPtr, (uint)PageSize, request.Base.Executable);
                InsertAllocatedPage(page);

                if (!page.TryAllocate((uint)request.Base.Size, (uint)request.Base.Alignment, out var pageAlloc))
                {
                    RegisterForCleanup(page);
                    allocated = null;
                    return false;
                }

                if ((nint)pageAlloc.BaseAddress < request.LowBound || (nint)pageAlloc.BaseAddress + pageAlloc.Size >= request.HighBound)
                {
                    pageAlloc.Dispose();
                    allocated = null;
                    return false;
                }

                allocated = pageAlloc;
                return true;
            }

            protected override bool TryFreePage(Page page, [NotNullWhen(false)] out string? errorMsg)
            {
                var res = Unix.Munmap(page.BaseAddr, page.Size);
                if (res != 0)
                {
                    errorMsg = new Win32Exception(Unix.Errno).Message;
                    return false;
                }
                errorMsg = null;
                return true;
            }
        }

        public unsafe IntPtr GetNativeJitHookConfig(int runtimeMajMin)
        {
            throw new NotImplementedException();
        }
    }
}
