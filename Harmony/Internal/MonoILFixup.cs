using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace HarmonyLib
{
    // Mono rejects raw local operands when the slot type changed in the target method.
    // This pass remaps them after transpilers run but before IL emission.
    internal static class MonoILFixup
    {
        private const string Tag = "[MonoILFixup]";

        // Mono exposes RuntimeStructs in System.Private.CoreLib.
        private static readonly bool IsMono =
            typeof(object).Assembly.GetType("Mono.RuntimeStructs") != null;

        internal static List<CodeInstruction> Apply(
            List<CodeInstruction> instructions,
            ILGenerator il = null
        )
        {
            if (!IsMono)
                return instructions;

            if (il != null)
                FixRawLocalIndices(instructions, il);

            return instructions;
        }

        private static void FixRawLocalIndices(List<CodeInstruction> instructions, ILGenerator il)
        {
            var rawIndexUsages = new Dictionary<int, List<int>>();

            for (int i = 0; i < instructions.Count; i++)
            {
                int rawIndex = GetRawLocalIndex(instructions[i]);
                if (rawIndex < 0)
                    continue;

                if (!rawIndexUsages.TryGetValue(rawIndex, out var list))
                {
                    list = new List<int>();
                    rawIndexUsages[rawIndex] = list;
                }
                list.Add(i);
            }

            if (rawIndexUsages.Count == 0)
                return;

            foreach (var kvp in rawIndexUsages)
            {
                int rawIndex = kvp.Key;
                var instrIndices = kvp.Value;

                Type localType = null;
                foreach (int idx in instrIndices)
                {
                    if (IsStlocOpcode(instructions[idx].opcode) && idx > 0)
                    {
                        localType = InferStackType(instructions[idx - 1]);
                        if (localType != null)
                            break;
                    }
                }

                if (localType == null)
                {
                    foreach (int idx in instrIndices)
                    {
                        if (
                            IsLdlocaOpcode(instructions[idx].opcode)
                            && idx + 1 < instructions.Count
                        )
                        {
                            localType = InferLdlocaType(instructions[idx + 1]);
                            if (localType != null)
                                break;
                        }
                    }
                }

                if (localType == null)
                {
                    var existingLocal = FindLocalBuilderForIndex(instructions, rawIndex);
                    if (existingLocal != null)
                    {
                        Type consumerExpectedType = null;
                        foreach (int idx in instrIndices)
                        {
                            if (
                                !IsStlocOpcode(instructions[idx].opcode)
                                && idx + 1 < instructions.Count
                            )
                            {
                                var expected = InferConsumerThisType(instructions[idx + 1]);
                                if (
                                    expected != null
                                    && !expected.IsAssignableFrom(existingLocal.LocalType)
                                )
                                {
                                    consumerExpectedType = expected;
                                    break;
                                }
                            }
                        }

                        if (consumerExpectedType != null)
                        {
                            var betterLocal = FindCompatibleLocalNearIndex(
                                instructions,
                                rawIndex,
                                consumerExpectedType
                            );
                            if (betterLocal != null)
                            {
                                Console.WriteLine(
                                    $"{Tag} FIXED: Raw local index {rawIndex} type mismatch "
                                        + $"(existing: {existingLocal.LocalType.Name}, consumer expects: {consumerExpectedType.Name}). "
                                        + $"Redirected to nearby local {betterLocal.LocalIndex} (type: {betterLocal.LocalType.Name})"
                                );
                                foreach (int idx in instrIndices)
                                    instructions[idx].operand = betterLocal;
                            }
                            else
                            {
                                Console.WriteLine(
                                    $"{Tag} WARNING: Raw local index {rawIndex} type mismatch "
                                        + $"(existing: {existingLocal.LocalType.Name}, consumer expects: {consumerExpectedType.Name}). "
                                        + $"No compatible local found nearby, using existing local"
                                );
                                foreach (int idx in instrIndices)
                                    instructions[idx].operand = existingLocal;
                            }
                        }
                        else
                        {
                            Console.WriteLine(
                                $"{Tag} FIXED: Resolved raw local index {rawIndex} -> existing LocalBuilder {existingLocal.LocalIndex} (type: {existingLocal.LocalType.Name})"
                            );
                            foreach (int idx in instrIndices)
                                instructions[idx].operand = existingLocal;
                        }
                    }
                    else
                    {
                        Console.WriteLine(
                            $"{Tag} WARNING: Cannot infer type or find LocalBuilder for raw local index {rawIndex}, skipping"
                        );
                    }
                    continue;
                }

                // A new slot would split declarations from raw references in async methods.
                Console.WriteLine(
                    $"{Tag} Raw local index {rawIndex} has no LocalBuilder operand; "
                        + $"leaving untouched for native resolution (type: {localType.Name})"
                );
            }
        }

        private static int GetRawLocalIndex(CodeInstruction instr)
        {
            var op = instr.opcode;
            if (
                op != OpCodes.Stloc
                && op != OpCodes.Stloc_S
                && op != OpCodes.Ldloc
                && op != OpCodes.Ldloc_S
                && op != OpCodes.Ldloca
                && op != OpCodes.Ldloca_S
            )
                return -1;

            if (instr.operand is LocalBuilder || instr.operand is LocalVariableInfo)
                return -1;

            if (instr.operand is int i)
                return i;
            if (instr.operand is byte b)
                return b;
            if (instr.operand is sbyte sb)
                return sb;
            if (instr.operand is short s)
                return s;

            return -1;
        }

        private static LocalBuilder FindLocalBuilderForIndex(
            List<CodeInstruction> instructions,
            int targetIndex
        )
        {
            foreach (var instr in instructions)
            {
                var op = instr.opcode;
                if (
                    op != OpCodes.Stloc
                    && op != OpCodes.Stloc_S
                    && op != OpCodes.Ldloc
                    && op != OpCodes.Ldloc_S
                    && op != OpCodes.Ldloca
                    && op != OpCodes.Ldloca_S
                )
                    continue;

                if (instr.operand is LocalBuilder lb && lb.LocalIndex == targetIndex)
                    return lb;
            }
            return null;
        }

        private static Type InferConsumerThisType(CodeInstruction consumer)
        {
            if (
                (consumer.opcode == OpCodes.Call || consumer.opcode == OpCodes.Callvirt)
                && consumer.operand is MethodInfo mi
                && !mi.IsStatic
            )
                return mi.DeclaringType;
            return null;
        }

        private static LocalBuilder FindCompatibleLocalNearIndex(
            List<CodeInstruction> instructions,
            int targetIndex,
            Type expectedType
        )
        {
            int[] offsets = { 1, -1, 2, -2, 3 };
            foreach (int offset in offsets)
            {
                int candidateIndex = targetIndex + offset;
                if (candidateIndex < 0)
                    continue;

                var candidate = FindLocalBuilderForIndex(instructions, candidateIndex);
                if (candidate != null && expectedType.IsAssignableFrom(candidate.LocalType))
                    return candidate;
            }
            return null;
        }

        private static bool IsStlocOpcode(OpCode opcode) =>
            opcode == OpCodes.Stloc || opcode == OpCodes.Stloc_S;

        private static bool IsLdlocaOpcode(OpCode opcode) =>
            opcode == OpCodes.Ldloca || opcode == OpCodes.Ldloca_S;

        private static Type InferStackType(CodeInstruction instr)
        {
            var op = instr.opcode;

            if ((op == OpCodes.Call || op == OpCodes.Callvirt) && instr.operand is MethodInfo mi)
                return mi.ReturnType == typeof(void) ? null : mi.ReturnType;

            if (op == OpCodes.Newobj && instr.operand is ConstructorInfo ci)
                return ci.DeclaringType;

            if ((op == OpCodes.Ldfld || op == OpCodes.Ldsfld) && instr.operand is FieldInfo fi)
                return fi.FieldType;

            if ((op == OpCodes.Ldloc || op == OpCodes.Ldloc_S) && instr.operand is LocalBuilder lb)
                return lb.LocalType;

            if (op == OpCodes.Conv_I4 || op == OpCodes.Conv_Ovf_I4 || op == OpCodes.Conv_Ovf_I4_Un)
                return typeof(int);
            if (op == OpCodes.Conv_I8 || op == OpCodes.Conv_Ovf_I8 || op == OpCodes.Conv_Ovf_I8_Un)
                return typeof(long);
            if (op == OpCodes.Conv_R4)
                return typeof(float);
            if (op == OpCodes.Conv_R8)
                return typeof(double);

            if (op == OpCodes.Unbox_Any && instr.operand is Type ut)
                return ut;

            if ((op == OpCodes.Castclass || op == OpCodes.Isinst) && instr.operand is Type ct)
                return ct;

            if (op == OpCodes.Box)
                return typeof(object);

            if (op == OpCodes.Ldstr)
                return typeof(string);

            if (
                op == OpCodes.Ldc_I4
                || op == OpCodes.Ldc_I4_S
                || op == OpCodes.Ldc_I4_0
                || op == OpCodes.Ldc_I4_1
                || op == OpCodes.Ldc_I4_2
                || op == OpCodes.Ldc_I4_3
                || op == OpCodes.Ldc_I4_4
                || op == OpCodes.Ldc_I4_5
                || op == OpCodes.Ldc_I4_6
                || op == OpCodes.Ldc_I4_7
                || op == OpCodes.Ldc_I4_8
                || op == OpCodes.Ldc_I4_M1
            )
                return typeof(int);
            if (op == OpCodes.Ldc_R4)
                return typeof(float);
            if (op == OpCodes.Ldc_R8)
                return typeof(double);
            if (op == OpCodes.Ldc_I8)
                return typeof(long);

            if (op == OpCodes.Ldnull)
                return typeof(object);

            return null;
        }

        private static Type InferLdlocaType(CodeInstruction consumer)
        {
            if (
                (consumer.opcode == OpCodes.Call || consumer.opcode == OpCodes.Callvirt)
                && consumer.operand is MethodInfo mi
            )
            {
                if (!mi.IsStatic)
                    return mi.DeclaringType;

                var parameters = mi.GetParameters();
                if (parameters.Length > 0 && parameters[0].ParameterType.IsByRef)
                    return parameters[0].ParameterType.GetElementType();
            }

            if (consumer.opcode == OpCodes.Initobj && consumer.operand is Type initType)
                return initType;

            return null;
        }
    }
}
