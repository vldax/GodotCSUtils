using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GodotCSUtils.DllMod
{
    public class ILHelper
    {
        private ILProcessor _il;

        public ILHelper(ILProcessor il)
        {
            _il = il;
        }

        public IEnumerable<Instruction> Compose(params IEnumerable<Instruction>[] instructionsArray)
        {
            List<Instruction> composed = new List<Instruction>();
            foreach (IEnumerable<Instruction> instructions in instructionsArray)
            {
                composed.AddRange(instructions);
            }

            return composed;
        }

        public IEnumerable<Instruction> Nop()
        {
            return new[] {_il.Create(OpCodes.Nop)};
        }
        
        public IEnumerable<Instruction> PushThis()
        {
            return new[] {_il.Create(OpCodes.Ldarg_0)};
        }
        
        public IEnumerable<Instruction> PushString(string arg)
        {
            return new[] {_il.Create(OpCodes.Ldstr, arg)};
        }
        
        public IEnumerable<Instruction> CallMethod(MethodReference method)
        {
            return new[] {_il.Create(OpCodes.Call, method)};
        }
        
        public IEnumerable<Instruction> CallMethodVirtual(MethodReference method)
        {
            return new[] {_il.Create(OpCodes.Callvirt, method)};
        }
        
        public IEnumerable<Instruction> LoadField(FieldDefinition field)
        {
            return new[] {_il.Create(OpCodes.Ldfld, field)};
        }

        public IEnumerable<Instruction> SetField(FieldDefinition field)
        {
            return new[] {_il.Create(OpCodes.Stfld, field)};
        }
        
        public IEnumerable<Instruction> LoadProperty(PropertyDefinition property)
        {
            return new[] {_il.Create(OpCodes.Call, property.GetMethod)};
        }

        public IEnumerable<Instruction> SetProperty(PropertyDefinition property)
        {
            return new[] {_il.Create(OpCodes.Call, property.SetMethod)};
        }

        public IEnumerable<Instruction> IsInstance(TypeReference type)
        {
            return new[] {_il.Create(OpCodes.Isinst, type)};
        }
        
        public IEnumerable<Instruction> IsNull()
        {
            return new[]
            {
                _il.Create(OpCodes.Ldnull),
                _il.Create(OpCodes.Ceq)
            };
        }
        
        public IEnumerable<Instruction> Branch(bool condition, 
            IEnumerable<Instruction> conditionInstructions,
            IEnumerable<Instruction> bodyInstructions,
            IEnumerable<Instruction> elseInstructions = null)
        {
            List<Instruction> instructions = new List<Instruction>();
            
            instructions.AddRange(conditionInstructions);
            
            if (elseInstructions != null)
            {
                Instruction elseBlockBegin = _il.Create(OpCodes.Nop);
                Instruction branchEnd = _il.Create(OpCodes.Nop);
                instructions.Add(_il.Create(condition ? OpCodes.Brfalse_S : OpCodes.Brtrue_S, elseBlockBegin));
                instructions.AddRange(bodyInstructions);
                instructions.Add(_il.Create(OpCodes.Br_S, branchEnd));
                instructions.Add(elseBlockBegin);
                instructions.AddRange(elseInstructions);
                instructions.Add(branchEnd);
            }
            else
            {
                Instruction branchEnd = _il.Create(OpCodes.Nop);
                instructions.Add(_il.Create(condition ? OpCodes.Brfalse_S : OpCodes.Brtrue_S, branchEnd));
                instructions.AddRange(bodyInstructions);
                instructions.Add(branchEnd);
            }
            
            return instructions;
        }
    }
}