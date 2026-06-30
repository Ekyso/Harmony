// Handle resolution must support static, instance, throwing, and shared generic methods.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MonoMod.Core.Platforms;
using Xunit;

namespace LocalMonoMod.Tests.CoreCLR
{
    public class JitHookRobustnessTests
    {
        static class StaticShape
        {
            public static bool PrefixRan;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Add(int a, int b) => a + b;
        }

        static class StaticShapePrefix
        {
            public static void Run() => StaticShape.PrefixRan = true;
        }

        class InstanceShape
        {
            public static bool PrefixRan;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public int Multiply(int a, int b) => a * b;
        }

        static class InstanceShapePrefix
        {
            public static void Run() => InstanceShape.PrefixRan = true;
        }

        static class ThrowingShape
        {
            public static bool PrefixRan;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void MayThrow(bool doThrow)
            {
                if (doThrow)
                    throw new InvalidOperationException("expected");
            }
        }

        static class ThrowingShapePrefix
        {
            public static void Run() => ThrowingShape.PrefixRan = true;
        }

        // Dictionary<string,int>.ContainsKey is a shared-generic method.
        // The JIT hook's GetDeclaringTypeOfMethodHandle throws for shared generics
        // and the hook deliberately ignores that tracking failure.
        static class GenericBclPrefix
        {
            public static bool PrefixRan;

            public static void Run() => PrefixRan = true;
        }

        static class GuardTarget
        {
            public static bool PrefixRan;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int GuardMethod(int x) => x + 1;
        }

        static class GuardPrefix
        {
            public static void Run() => GuardTarget.PrefixRan = true;
        }

        [Fact]
        public void JitHook_Resolves_Various_Method_Shapes_Without_Crash()
        {
            const string id = "test.coreclr.handle-shapes";
            var h = new Harmony(id);

            StaticShape.PrefixRan = false;
            h.Patch(
                typeof(StaticShape).GetMethod(nameof(StaticShape.Add))!,
                prefix: new HarmonyMethod(
                    typeof(StaticShapePrefix).GetMethod(nameof(StaticShapePrefix.Run))!
                )
            );

            InstanceShape.PrefixRan = false;
            h.Patch(
                typeof(InstanceShape).GetMethod(nameof(InstanceShape.Multiply))!,
                prefix: new HarmonyMethod(
                    typeof(InstanceShapePrefix).GetMethod(nameof(InstanceShapePrefix.Run))!
                )
            );

            ThrowingShape.PrefixRan = false;
            h.Patch(
                typeof(ThrowingShape).GetMethod(nameof(ThrowingShape.MayThrow))!,
                prefix: new HarmonyMethod(
                    typeof(ThrowingShapePrefix).GetMethod(nameof(ThrowingShapePrefix.Run))!
                )
            );

            GenericBclPrefix.PrefixRan = false;
            h.Patch(
                typeof(Dictionary<string, int>).GetMethod(
                    nameof(Dictionary<string, int>.ContainsKey)
                )!,
                prefix: new HarmonyMethod(
                    typeof(GenericBclPrefix).GetMethod(nameof(GenericBclPrefix.Run))!
                )
            );

            _ = StaticShape.Add(3, 4);
            _ = new InstanceShape().Multiply(2, 5);

            try
            {
                ThrowingShape.MayThrow(doThrow: true);
            }
            catch (InvalidOperationException)
            {
                // The prefix should already have run.
            }

            var dict = new Dictionary<string, int> { ["hello"] = 1 };
            _ = dict.ContainsKey("hello");

            h.UnpatchAll(id);

            Assert.True(StaticShape.PrefixRan, "Static method prefix did not fire.");
            Assert.True(InstanceShape.PrefixRan, "Instance method prefix did not fire.");
            Assert.True(ThrowingShape.PrefixRan, "Throwing method prefix did not fire.");
            Assert.True(
                GenericBclPrefix.PrefixRan,
                "Generic BCL method (Dictionary.ContainsKey) prefix did not fire."
            );
        }

        [Fact]
        public void MakeAssemblySystemAssembly_Is_Never_Called_On_Net10()
        {
            // The legacy path fails before applying the prefix on .NET 10.
            const string id = "test.coreclr.modern-handle-path";
            var h = new Harmony(id);

            GuardTarget.PrefixRan = false;
            h.Patch(
                typeof(GuardTarget).GetMethod(nameof(GuardTarget.GuardMethod))!,
                prefix: new HarmonyMethod(typeof(GuardPrefix).GetMethod(nameof(GuardPrefix.Run))!)
            );

            GuardTarget.GuardMethod(42);
            h.UnpatchAll(id);

            Assert.True(
                GuardTarget.PrefixRan,
                "The detour failed during handle resolution."
            );
        }

        [Fact]
        public void MakeAssemblySystemAssembly_Guard_Throws_On_Modern_Runtime()
        {
            // The legacy object layout is unsafe on modern runtimes.
            var runtime = PlatformTriple.Current.Runtime;

            MethodInfo? mi = null;
            for (var t = runtime.GetType(); t is not null && mi is null; t = t.BaseType)
                mi = t.GetMethod(
                    "MakeAssemblySystemAssembly",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                );
            Assert.NotNull(mi);

            var ex = Assert.Throws<TargetInvocationException>(() =>
                mi!.Invoke(runtime, new object[] { typeof(object).Assembly })
            );
            Assert.IsType<InvalidOperationException>(ex.InnerException);
        }
    }
}
