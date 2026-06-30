using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NUnit.Framework;

namespace HarmonyLibTests.Patching
{
    // Type.GetType uses the DynamicMethod owner when the name is not assembly-qualified.
    // Replacement methods must retain the original assembly context.

    public class TypeGetTypeTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Type ResolveTypeByName(string typeName)
        {
            return Type.GetType(typeName);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string CreateObjectiveByTypeName(string ns, string name)
        {
            var type = Type.GetType(ns + "." + name);
            if (type == null)
                return null;
            return type.FullName;
        }
    }

    public static class TypeGetTypeTarget_Prefix
    {
        public static bool prefixCalled = false;

        public static void Prefix()
        {
            prefixCalled = true;
        }
    }

    public class SameAssemblyMarker { }

    [TestFixture, NonParallelizable]
    public class TypeGetTypeFromDMD : TestLogger
    {
        [SetUp]
        public void ResetState()
        {
            TypeGetTypeTarget_Prefix.prefixCalled = false;
        }

        [Test]
        public void Test_TypeGetType_AQN_After_Prefix_Patch()
        {
            const string id = "test.dynamic-owner.aqn";
            var harmony = new Harmony(id);
            var original = AccessTools.Method(
                typeof(TypeGetTypeTarget),
                nameof(TypeGetTypeTarget.ResolveTypeByName)
            );
            var prefix = AccessTools.Method(
                typeof(TypeGetTypeTarget_Prefix),
                nameof(TypeGetTypeTarget_Prefix.Prefix)
            );

            _ = harmony.Patch(original, prefix: new HarmonyMethod(prefix));

            var aqn = typeof(SameAssemblyMarker).AssemblyQualifiedName;
            var result = TypeGetTypeTarget.ResolveTypeByName(aqn);

            Assert.True(TypeGetTypeTarget_Prefix.prefixCalled, "Prefix was not called");
            Assert.NotNull(result, "Type.GetType with AQN must resolve from within DMD");
            Assert.AreEqual(typeof(SameAssemblyMarker), result);

            harmony.UnpatchAll(id);
        }

        [Test]
        public void Test_TypeGetType_NamespaceOnly_After_Prefix_Patch()
        {
            const string id = "test.dynamic-owner.namespace";
            var harmony = new Harmony(id);
            var original = AccessTools.Method(
                typeof(TypeGetTypeTarget),
                nameof(TypeGetTypeTarget.CreateObjectiveByTypeName)
            );
            var prefix = AccessTools.Method(
                typeof(TypeGetTypeTarget_Prefix),
                nameof(TypeGetTypeTarget_Prefix.Prefix)
            );

            _ = harmony.Patch(original, prefix: new HarmonyMethod(prefix));

            var result = TypeGetTypeTarget.CreateObjectiveByTypeName(
                typeof(SameAssemblyMarker).Namespace,
                nameof(SameAssemblyMarker)
            );

            Assert.True(TypeGetTypeTarget_Prefix.prefixCalled, "Prefix was not called");
            Assert.NotNull(
                result,
                "Type.GetType(namespace.name) must resolve from the original method's assembly "
                    + "inside the Harmony replacement method"
            );
            Assert.AreEqual(typeof(SameAssemblyMarker).FullName, result);

            harmony.UnpatchAll(id);
        }

        [Test]
        public void Test_DMD_Uses_DynamicMethod_Not_Cecil()
        {
            Assert.True(
                MonoMod.Utils.DynamicMethodDefinition.IsDynamicILAvailable,
                "The DynamicMethod path must preserve the owner module"
            );
        }

        [Test]
        public void Test_Raw_DynamicMethod_Owner_Module_Resolution()
        {
            var dm = new DynamicMethod(
                "RawDM_TypeGetType",
                typeof(Type),
                new[] { typeof(string) },
                typeof(TypeGetTypeTarget),
                true
            );
            var il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(
                OpCodes.Call,
                typeof(Type).GetMethod(
                    nameof(Type.GetType),
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null
                )!
            );
            il.Emit(OpCodes.Ret);

            var fn = (Func<string, Type>)dm.CreateDelegate(typeof(Func<string, Type>));
            var fullName = typeof(SameAssemblyMarker).FullName;
            var result = fn(fullName);

            Assert.NotNull(
                result,
                "DynamicMethod with owner type must use owner's module for Type.GetType resolution"
            );
            Assert.AreEqual(typeof(SameAssemblyMarker), result);
        }
    }
}
