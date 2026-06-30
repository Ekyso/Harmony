using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Xunit;

namespace LocalMonoMod.Tests.CoreCLR
{
    public class MethodShapeTests
    {
        static class ListAddPrefix
        {
            public static bool PrefixRan;

            public static void Run() => PrefixRan = true;
        }

        [Fact]
        public void Prefix_On_ListStringAdd_Fires()
        {
            const string id = "test.coreclr.shared-generic";
            var h = new Harmony(id);

            var addMethod = typeof(List<string>).GetMethod(
                nameof(List<string>.Add),
                new[] { typeof(string) }
            )!;
            ListAddPrefix.PrefixRan = false;
            h.Patch(
                addMethod,
                prefix: new HarmonyMethod(
                    typeof(ListAddPrefix).GetMethod(nameof(ListAddPrefix.Run))!
                )
            );

            var list = new List<string>();
            list.Add("x");

            h.UnpatchAll(id);

            Assert.True(
                ListAddPrefix.PrefixRan,
                "The shared-generic detour did not apply."
            );
        }

        struct NamedValue
        {
            public int Value;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public override string ToString() => $"original:{Value}";
        }

        static class ToStringPostfix
        {
            public static void Run(ref string __result)
            {
                __result = "patched:" + __result;
            }
        }

        [Fact]
        public void Postfix_On_Struct_ToString_Rewrites_Result()
        {
            const string id = "test.coreclr.struct-postfix";
            var h = new Harmony(id);

            var toStringMethod = typeof(NamedValue).GetMethod(
                nameof(NamedValue.ToString),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )!;
            h.Patch(
                toStringMethod,
                postfix: new HarmonyMethod(
                    typeof(ToStringPostfix).GetMethod(nameof(ToStringPostfix.Run))!
                )
            );

            var nv = new NamedValue { Value = 42 };
            var result = nv.ToString();

            h.UnpatchAll(id);

            Assert.Equal("patched:original:42", result);
        }

        static class LifecycleTarget
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Echo(int x) => x;
        }

        static class LifecyclePrefix
        {
            public static bool PrefixRan;

            public static void Run() => PrefixRan = true;
        }

        [Fact]
        public void Prefix_Fires_Only_While_Patched_And_Repatch_Restores()
        {
            const string id = "test.coreclr.patch-lifecycle";
            var original = typeof(LifecycleTarget).GetMethod(nameof(LifecycleTarget.Echo))!;
            var prefixInfo = new HarmonyMethod(
                typeof(LifecyclePrefix).GetMethod(nameof(LifecyclePrefix.Run))!
            );

            var h = new Harmony(id);
            LifecyclePrefix.PrefixRan = false;
            h.Patch(original, prefix: prefixInfo);
            LifecycleTarget.Echo(1);
            Assert.True(LifecyclePrefix.PrefixRan, "Prefix should fire while patched.");

            h.UnpatchAll(id);
            LifecyclePrefix.PrefixRan = false;
            LifecycleTarget.Echo(2);
            Assert.False(
                LifecyclePrefix.PrefixRan,
                "Prefix should not fire after UnpatchAll."
            );

            var h2 = new Harmony(id);
            LifecyclePrefix.PrefixRan = false;
            h2.Patch(original, prefix: prefixInfo);
            LifecycleTarget.Echo(3);
            Assert.True(
                LifecyclePrefix.PrefixRan,
                "Prefix should fire after patching again."
            );
            h2.UnpatchAll(id);
        }
    }
}
