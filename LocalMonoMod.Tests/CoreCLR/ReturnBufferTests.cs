// A wrong vtable constant for AllocMem or a botched sret convention would cause
// the postfix's ref __result rewrite to scribble into the wrong memory or simply
// never reach the caller's buffer. These tests catch that class of breakage.

using System.Runtime.CompilerServices;
using HarmonyLib;
using Xunit;

namespace LocalMonoMod.Tests.CoreCLR
{
    public class ReturnBufferTests
    {
        // More than eight bytes forces the return-buffer convention on x64 and ARM64.
        public struct Big
        {
            public ulong A,
                B,
                C;
        }

        class InstanceTarget
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public Big GetBig() =>
                new Big
                {
                    A = 1,
                    B = 2,
                    C = 3,
                };
        }

        static class InstancePostfix
        {
            public static void Run(ref Big __result)
            {
                __result.A += 10;
                __result.B += 20;
                __result.C += 30;
            }
        }

        [Fact]
        public void Postfix_Rewrites_StructReturnBuffer_InstanceMethod()
        {
            const string id = "test.coreclr.instance-return-buffer";
            var h = new Harmony(id);
            var original = typeof(InstanceTarget).GetMethod(nameof(InstanceTarget.GetBig))!;
            var postfix = new HarmonyMethod(
                typeof(InstancePostfix).GetMethod(nameof(InstancePostfix.Run))!
            );

            h.Patch(original, postfix: postfix);

            var target = new InstanceTarget();
            var result = target.GetBig();

            h.UnpatchAll(id);

            Assert.Equal(11UL, result.A);
            Assert.Equal(22UL, result.B);
            Assert.Equal(33UL, result.C);
        }

        static class StaticTarget
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static Big GetBigStatic() =>
                new Big
                {
                    A = 100,
                    B = 200,
                    C = 300,
                };
        }

        static class StaticPostfix
        {
            public static void Run(ref Big __result)
            {
                __result.A += 1;
                __result.B += 2;
                __result.C += 3;
            }
        }

        [Fact]
        public void Postfix_Rewrites_StructReturnBuffer_StaticMethod()
        {
            const string id = "test.coreclr.static-return-buffer";
            var h = new Harmony(id);
            var original = typeof(StaticTarget).GetMethod(nameof(StaticTarget.GetBigStatic))!;
            var postfix = new HarmonyMethod(
                typeof(StaticPostfix).GetMethod(nameof(StaticPostfix.Run))!
            );

            h.Patch(original, postfix: postfix);

            var result = StaticTarget.GetBigStatic();

            h.UnpatchAll(id);

            Assert.Equal(101UL, result.A);
            Assert.Equal(202UL, result.B);
            Assert.Equal(303UL, result.C);
        }
    }
}
