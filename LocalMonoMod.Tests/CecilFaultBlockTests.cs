using System;
using System.Reflection;
using System.Reflection.Emit;
using MonoMod.Utils;
using Xunit;

namespace LocalMonoMod.Tests
{
    public class CecilFaultBlockTests
    {
        [Fact]
        public void CecilBackendIsActive()
        {
            Assert.Contains(
                "Cecil",
                new DynamicMethodDefinition("BackendProbe", typeof(void), System.Type.EmptyTypes)
                    .GetILGenerator()
                    .GetType()
                    .ToString()
            );
        }

        // Emitted IL needs a public field it can update.
        public static bool FaultRan;

        static MethodInfo BuildFaultMethod()
        {
            var dmd = new DynamicMethodDefinition(
                "FaultProbe",
                typeof(void),
                new[] { typeof(bool) }
            );
            var il = dmd.GetILGenerator();
            var faultRan = typeof(CecilFaultBlockTests).GetField(
                nameof(FaultRan),
                BindingFlags.Public | BindingFlags.Static
            )!;
            var ioeCtor = typeof(InvalidOperationException).GetConstructor(
                new[] { typeof(string) }
            )!;

            il.BeginExceptionBlock();
            var skip = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Ldstr, "boom");
            il.Emit(OpCodes.Newobj, ioeCtor);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(skip);
            il.BeginFaultBlock();
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stsfld, faultRan);
            il.EndExceptionBlock();
            il.Emit(OpCodes.Ret);

            return dmd.Generate();
        }

        [Fact]
        public void FaultBlock_DoesNotRun_OnNormalCompletion()
        {
            var act = (Action<bool>)BuildFaultMethod().CreateDelegate(typeof(Action<bool>));
            FaultRan = false;
            act(false);
            Assert.False(FaultRan);
        }

        [Fact]
        public void FaultBlock_Runs_AndExceptionPropagates_OnThrow()
        {
            var act = (Action<bool>)BuildFaultMethod().CreateDelegate(typeof(Action<bool>));
            FaultRan = false;
            Assert.Throws<InvalidOperationException>(() => act(true));
            Assert.True(FaultRan);
        }
    }
}
