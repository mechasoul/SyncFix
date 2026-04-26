using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;

namespace SyncFix.Utils
{
    //brought this in for the huge AlignTimes transpiler, which we now don't use
    //TODO delete
    public class InstructionBuilder
    {
        private List<CodeInstruction> instructions = [];
        private Queue<OpCode> previousOpCodes = new();
        private bool expectingOpCode = true;

        public InstructionBuilder OpCode(OpCode opcode)
        {
            if (!expectingOpCode) throw new InvalidOperationException($"tried to add opcode {opcode.Name} when expecting operand (previous instruction: {instructions.LastOrDefault()})");

            previousOpCodes.Enqueue(opcode);
            expectingOpCode = false;
            if (opcode.OperandType == OperandType.InlineNone)
            {
                instructions.Add(new CodeInstruction(previousOpCodes.Dequeue(), null));
                expectingOpCode = true;
            }
            return this;
        }

        public InstructionBuilder Operand(object operand)
        {
            if (expectingOpCode)
            {
                if (operand == null && instructions.LastOrDefault()?.opcode.OperandType == OperandType.InlineNone)
                {
                    //explicitly adding a null even though we already added one. unnecessary but not a problem. continue operation as normal
                    return this;
                }
                throw new InvalidOperationException($"tried to add operand {operand} when expecting opcode (previous instruction: {instructions.LastOrDefault()}, opcode: {previousOpCodes.LastOrDefault()}");
            }

            if (previousOpCodes.Count == 0) throw new InvalidOperationException($"tried to add operand {operand} without previously adding an opcode (also this shouldnt happen?)");

            instructions.Add(new CodeInstruction(previousOpCodes.Dequeue(), operand));
            expectingOpCode = true;
            return this;
        }

        public CodeInstruction[] Build()
        {
            return instructions.ToArray();
        }

        public CodeMatch[] BuildAsMatch()
        {
            return instructions.Select(instruction => new CodeMatch(instruction)).ToArray();
        }
    }
}
