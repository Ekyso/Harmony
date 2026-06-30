using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NUnit.Framework;

namespace HarmonyLibTests.Patching
{
    // Mono requires the stored value to match the local's exact type. Raw integer
    // operands can point at a different type after a target method changes.

    public struct SmallVec
    {
        public float X;
        public float Y;

        public SmallVec(float x, float y)
        {
            X = x;
            Y = y;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static SmallVec Multiply(SmallVec v, float s)
        {
            return new SmallVec(v.X * s, v.Y * s);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ToInt()
        {
            return (int)(X + Y);
        }
    }

    public struct SmallRect
    {
        public int X;
        public int Y;
        public int W;
        public int H;
    }

    public class StlocRawIndexTarget
    {
        // Local 20 intentionally has the wrong type for the transpiler below.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int BigMethodWithManyLocals(float input)
        {
            int a0 = (int)input;
            int a1 = a0 + 1;
            int a2 = a1 + 2;
            int a3 = a2 + 3;
            int a4 = a3 + 4;
            int a5 = a4 + 5;
            int a6 = a5 + 6;
            int a7 = a6 + 7;
            int a8 = a7 + 8;
            int a9 = a8 + 9;
            string a10 = a9.ToString();
            float a11 = input * 2f;
            SmallRect a12 = new SmallRect
            {
                X = a0,
                Y = a1,
                W = 10,
                H = 10,
            };
            double a13 = input * 3.0;
            long a14 = (long)input;
            int a15 = a0 + 15;
            int a16 = a0 + 16;
            int a17 = a0 + 17;
            int a18 = a0 + 18;
            int a19 = a0 + 19;
            SmallRect a20 = new SmallRect
            {
                X = a0,
                Y = a1,
                W = 20,
                H = 20,
            };
            int result =
                a0
                + a1
                + a2
                + a3
                + a4
                + a5
                + a6
                + a7
                + a8
                + a9
                + a10.Length
                + (int)a11
                + a12.X
                + (int)a13
                + (int)a14
                + a15
                + a16
                + a17
                + a18
                + a19
                + a20.X;
            return result;
        }
    }

    [TestFixture, NonParallelizable]
    public class StlocRawIndex : TestLogger
    {
        // Raw operands reproduce transpilers that cannot access the target's LocalBuilder values.
        public static IEnumerable<CodeInstruction> RawIndexTranspiler(
            IEnumerable<CodeInstruction> instructions
        )
        {
            var codes = new List<CodeInstruction>(instructions);

            for (int i = 0; i < codes.Count; i++)
            {
                if (
                    codes[i].opcode == OpCodes.Stloc_S
                    && codes[i].operand is LocalBuilder lb
                    && lb.LocalIndex == 12
                )
                {
                    var newInstructions = new List<CodeInstruction>
                    {
                        new CodeInstruction(OpCodes.Pop),
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(
                            OpCodes.Newobj,
                            typeof(SmallVec).GetConstructor(new[] { typeof(float), typeof(float) })
                        ),
                        new CodeInstruction(OpCodes.Ldc_R4, 2f),
                        new CodeInstruction(
                            OpCodes.Call,
                            typeof(SmallVec).GetMethod(nameof(SmallVec.Multiply))
                        ),
                        new CodeInstruction(OpCodes.Stloc_S, (object)(byte)20),
                    };

                    codes.RemoveAt(i);
                    codes.InsertRange(i, newInstructions);
                    break;
                }
            }

            return codes;
        }

        [Test]
        public void Test_TranspilerWithRawIntLocalIndex()
        {
            int baseline = StlocRawIndexTarget.BigMethodWithManyLocals(1f);
            Assert.Greater(baseline, 0, "Baseline method should return positive value");

            var original = AccessTools.Method(
                typeof(StlocRawIndexTarget),
                nameof(StlocRawIndexTarget.BigMethodWithManyLocals)
            );
            Assert.NotNull(original, "Original method not found");

            var transpiler = AccessTools.Method(typeof(StlocRawIndex), nameof(RawIndexTranspiler));
            Assert.NotNull(transpiler, "Transpiler method not found");

            var harmony = new Harmony("test-stloc-raw-index");

            Assert.DoesNotThrow(
                () => harmony.Patch(original, transpiler: new HarmonyMethod(transpiler)),
                "Patching with raw int local index should not throw"
            );

            int patched = StlocRawIndexTarget.BigMethodWithManyLocals(1f);
            Assert.Greater(patched, 0, "Patched method should return positive value");

            harmony.UnpatchAll("test-stloc-raw-index");
        }
    }
}
