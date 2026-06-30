using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MonoMod.Core.Platforms;
using Xunit;

namespace LocalMonoMod.Tests.CoreCLR
{
    public class RuntimeInitTests
    {
        static class Target
        {
            public static bool PrefixRan;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Echo(int x) => x;
        }

        static class Prefix
        {
            public static void Run() => Target.PrefixRan = true;
        }

        [Fact]
        public void PlatformTriple_Selects_Core100() =>
            Assert.Contains("Core100Runtime", PlatformTriple.Current.Runtime.GetType().Name);

        [Fact]
        public void Harmony_Prefix_Applies_On_Net10()
        {
            const string id = "test.coreclr.net10-prefix";
            var h = new Harmony(id);
            var original = typeof(Target).GetMethod(nameof(Target.Echo))!;
            var prefix = new HarmonyMethod(typeof(Prefix).GetMethod(nameof(Prefix.Run))!);

            Target.PrefixRan = false;
            h.Patch(original, prefix: prefix);
            Target.Echo(1);

            Assert.True(
                Target.PrefixRan,
                "The .NET 10 detour did not apply."
            );
            h.UnpatchAll(id);
        }
    }
}
