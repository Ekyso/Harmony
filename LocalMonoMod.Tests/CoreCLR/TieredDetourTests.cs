// Release builds exercise tiered recompilation. The detour must survive every tier change.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Xunit;

namespace LocalMonoMod.Tests.CoreCLR
{
    [Trait("Category", "Release")]
    public class TieredDetourTests
    {
        static class TieredTarget
        {
            // NoInlining prevents the call site from being optimized away, but the
            // method body itself is still eligible for tier-1 recompilation.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int HotMethod(int x) => x * 2 + 1;
        }

        static class TieredPrefix
        {
            public static volatile int CallCount;

            public static void Run() => Interlocked.Increment(ref CallCount);
        }

        [Fact]
        public void Prefix_Fires_Through_Tier1_Recompilation()
        {
            const string id = "test.coreclr.tiered-recompilation";
            const int TotalCalls = 120_000;

            var h = new Harmony(id);
            var original = typeof(TieredTarget).GetMethod(nameof(TieredTarget.HotMethod))!;
            var prefix = new HarmonyMethod(
                typeof(TieredPrefix).GetMethod(nameof(TieredPrefix.Run))!
            );

            TieredPrefix.CallCount = 0;
            h.Patch(original, prefix: prefix);

            // Brief pauses let the background JIT publish the tier-1 version.
            for (int i = 0; i < TotalCalls; i++)
            {
                TieredTarget.HotMethod(i);
                if ((i & 0x27FF) == 0x27FF) // ~every 10 240 calls
                    Thread.Sleep(5);
            }

            h.UnpatchAll(id);

            Assert.Equal(TotalCalls, TieredPrefix.CallCount);
        }
    }
}
