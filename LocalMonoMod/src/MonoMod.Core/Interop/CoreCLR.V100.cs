using System.Diagnostics.CodeAnalysis;

namespace MonoMod.Core.Interop
{
    internal static unsafe partial class CoreCLR
    {
        [SuppressMessage(
            "Performance",
            "CA1812: Avoid uninstantiated internal classes",
            Justification = "It must be non-static to be able to inherit others, as it does. This allows the Core*Runtime types "
                + "to each reference exactly the version they represent, and the compiler automatically resolves the correct one without "
                + "needing duplicates."
        )]
        [SuppressMessage(
            "Performance",
            "CA1852",
            Justification = "This type will be derived for .NET 11."
        )]
        public class V100 : V90
        {
            public static new class ICorJitInfoVtable
            {
                // .NET 10 ICorJitInfo vtable (src/coreclr/inc/icorjitinfoimpl_generated.h).
                // Net +1 method before allocMem vs .NET 9; total +2 (174 -> 176).
                // getSpecialCopyHelper is the LAST slot (0xAF), after allocMem.
                // 0x9F: void allocMem(AllocMemArgs *)
                public const int AllocMemIndex = 0x9F;
                public const int TotalVtableCount = 0xB0;
            }
        }
    }
}
