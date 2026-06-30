using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Xunit;

namespace LocalMonoMod.Tests.CoreCLR
{
    public class PatchNamingTests
    {
        static class NamingTarget
        {
            public static int Value;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Method(int x) => x + Value;
        }

        static class NamingPrefix
        {
            public static void Prefix() { }
        }

        [Fact]
        public void PatchWrapperName_IncludesOwnerId()
        {
            const string ownerId = "test.cinderbox.owner";
            var h = new Harmony(ownerId);
            var original = typeof(NamingTarget).GetMethod(nameof(NamingTarget.Method))!;
            var prefix = new HarmonyMethod(
                typeof(NamingPrefix).GetMethod(nameof(NamingPrefix.Prefix))!
            );

            MethodInfo replacement = h.Patch(original, prefix: prefix);

            Assert.Contains($"_PatchedBy<{ownerId}>", replacement.Name);
            Assert.DoesNotContain("_Patch1", replacement.Name);

            h.UnpatchAll(ownerId);
        }
    }
}
