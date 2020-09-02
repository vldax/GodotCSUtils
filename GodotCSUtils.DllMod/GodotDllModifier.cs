using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil.Rocks;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;

namespace GodotCSUtils.DllMod
{
    internal class GodotDllModifier : IDisposable
    {
        private readonly bool _debugChecksEnabled;
        private readonly string _targetModuleAssemblyPath;
        private readonly string _godotMainAssemblyDir;
        private readonly string _godotLinkedAssembliesDir;
        
        private ModuleDefinition _targetModule;
        private TypeDefinition _godotNodeTypeDef;
        private TypeDefinition _godotNodePathTypeDef;
        private TypeDefinition _godotExportAttrTypeDef;
        private TypeDefinition _godotPropertyHintTypeDef;

        private TypeDefinition _godotUtilsGetAttrTypeDef;
        private TypeDefinition _godotUtilsAutoloadAttrTypeDef;
        private TypeDefinition _godotUtilsExportRenameAttrTypeDef;
        
        private MethodReference _nodePathConversionMethodDef;
        private MethodReference _nodePathIsEmptyMethodDef;
        private MethodReference _nodeGetNodeMethodDef;
        private MethodReference _exportAttrCtorDef;
        private MethodReference _godotPushErrorMethodDef;
        
        public GodotDllModifier(string targetDLLPath, 
            string godotMainAssemblyDir, 
            string godotLinkedAssembliesDir,
            bool debugCheckEnabled)
        {
            _targetModuleAssemblyPath = targetDLLPath;
            _godotMainAssemblyDir = godotMainAssemblyDir;
            _godotLinkedAssembliesDir = godotLinkedAssembliesDir;
            _debugChecksEnabled = debugCheckEnabled;
        }

        private void ReadTargetAssembly()
        {
            DefaultAssemblyResolver assemblyResolver = new DefaultAssemblyResolver();
            assemblyResolver.AddSearchDirectory(_godotMainAssemblyDir);
            assemblyResolver.AddSearchDirectory(_godotLinkedAssembliesDir);
            
            ReaderParameters readerParameters = new ReaderParameters
            {
                AssemblyResolver = assemblyResolver,
                ReadSymbols = true,
                ReadWrite = true
            };
            
            _targetModule = ModuleDefinition.ReadModule(_targetModuleAssemblyPath, readerParameters);
            
            ModuleDefinition godotSharpModule = ModuleDefinition.ReadModule(_godotMainAssemblyDir + "GodotSharp.dll");
            
            _godotNodeTypeDef = _targetModule.ImportReference(godotSharpModule.GetType("Godot.Node")).Resolve();
            _nodeGetNodeMethodDef = _targetModule.ImportReference(
                FindMethodByFullName(_godotNodeTypeDef, "Godot.Node Godot.Node::GetNodeOrNull(Godot.NodePath)"));
            
            _godotNodePathTypeDef = _nodeGetNodeMethodDef.Parameters[0].ParameterType.Resolve();
            
            _nodePathConversionMethodDef = _targetModule.ImportReference(
                FindMethodByFullName(_godotNodePathTypeDef, "Godot.NodePath Godot.NodePath::op_Implicit(System.String)"));
            
            _nodePathIsEmptyMethodDef = _targetModule.ImportReference(
                FindMethodByFullName(_godotNodePathTypeDef, "System.Boolean Godot.NodePath::IsEmpty()"));
            
            _godotExportAttrTypeDef = GetResolvedType("Godot.ExportAttribute", true);
            if (_godotExportAttrTypeDef != null)
            {
                _godotPropertyHintTypeDef = GetResolvedType("Godot.PropertyHint", true);
                _exportAttrCtorDef = _targetModule.ImportReference(
                    FindMethodBySimpleName(_godotExportAttrTypeDef, ".ctor"));
            }

            _godotUtilsGetAttrTypeDef = GetResolvedType("GodotCSUtils.GetAttribute", true);
            _godotUtilsAutoloadAttrTypeDef = GetResolvedType("GodotCSUtils.AutoloadAttribute", true);
            _godotUtilsExportRenameAttrTypeDef = GetResolvedType("GodotCSUtils.ExportRenameAttribute", true);

            TypeDefinition gdClassTypeDef = _targetModule.ImportReference(godotSharpModule.GetType("Godot.GD")).Resolve();
            _godotPushErrorMethodDef = _targetModule.ImportReference(
                FindMethodByFullName(gdClassTypeDef, "System.Void Godot.GD::PushError(System.String)"));
        }
        
        private TypeDefinition GetResolvedType(string typeFullName, bool optional = false)
        {
            if (!_targetModule.TryGetTypeReference(typeFullName, out var typeReference))
            {
                if (optional)
                    return null;
                throw new Exception($"cannot locate {typeFullName} type ref");
            }

            return typeReference.Resolve();
        }
        
        public void Dispose()
        {
            _targetModule?.Dispose();
        }

        private bool HasBaseClass(TypeDefinition typeDef, TypeDefinition baseTypeDef)
        {
            if (typeDef == baseTypeDef)
                return true;
            if (typeDef.BaseType == null)
                return false;
            return HasBaseClass(typeDef.BaseType.Resolve(), baseTypeDef);
        }

        private TypeReference GetFieldOrPropertyType(IMemberDefinition member)
        {
            if (member is FieldDefinition field)
                return field.FieldType;
            if (member is PropertyDefinition property)
                return property.PropertyType;
            throw new Exception("must be field or property");
        }
        
        private IEnumerable<TypeDefinition> GetNodeClasses()
        {
            return _targetModule.Types.Where(type => HasBaseClass(type, _godotNodeTypeDef));
        }

        private MethodDefinition FindMethodByFullName(TypeDefinition typeDef, string fullName)
        {
            return typeDef.Methods.FirstOrDefault(m => m.FullName == fullName);
        }
        
        private MethodDefinition FindMethodBySimpleName(TypeDefinition typeDef, string simpleName)
        {
            return typeDef.Methods.FirstOrDefault(m => m.Name == simpleName);
        }

        private FieldDefinition FindFieldBySimpleName(TypeDefinition typeDef, string simpleName)
        {
            return typeDef.Fields.FirstOrDefault(m => m.Name == simpleName); 
        }
        
        private PropertyDefinition FindPropertyBySimpleName(TypeDefinition typeDef, string simpleName)
        {
            return typeDef.Properties.FirstOrDefault(m => m.Name == simpleName); 
        }

        private CustomAttribute GetFieldAttribute(IMemberDefinition memberDef, TypeDefinition attrTypeDef)
        {
            foreach (CustomAttribute attribute in memberDef.CustomAttributes)
            {
                if (attribute.AttributeType.Resolve() == attrTypeDef)
                    return attribute;
            }
            return null;
        }

        private MethodDefinition GetOrCreateReadyMethod(TypeDefinition nodeClass)
        {
            MethodDefinition methodDef = FindMethodBySimpleName(nodeClass, "_Ready");
            if (methodDef == null)
            {
                methodDef = new MethodDefinition("_Ready", 
                    MethodAttributes.Public 
                    | MethodAttributes.HideBySig
                    | MethodAttributes.Virtual, _targetModule.TypeSystem.Void);
                    
                ILProcessor il = methodDef.Body.GetILProcessor();
                il.Emit(OpCodes.Ret);
                    
                nodeClass.Methods.Add(methodDef);
            }

            return methodDef;
        }

        private void ProcessAutoloadLinks(TypeDefinition nodeClass)
        {
            List<IMemberDefinition> members = new List<IMemberDefinition>();
            members.AddRange(nodeClass.Fields);
            members.AddRange(nodeClass.Properties);

            MethodDefinition readyMethod = null;
            
            foreach (IMemberDefinition memberDef in members)
            {
                CustomAttribute autoloadAttr = GetFieldAttribute(memberDef, _godotUtilsAutoloadAttrTypeDef);
                if(autoloadAttr == null)
                    continue;

                string name = (string)autoloadAttr.ConstructorArguments[0].Value;
                if(name == null)
                    name = memberDef.Name;
                
                //Console.WriteLine($"found Autoload: class - {nodeClass}; member - {memberDef}; name - {name}");

                if (readyMethod == null)
                    readyMethod = GetOrCreateReadyMethod(nodeClass);

                ILProcessor il = readyMethod.Body.GetILProcessor();
                IEnumerable<Instruction> instructions = GenerateSetLinkFromPathInstructions(memberDef, $"/root/{name}", il);
                InsertInstructionsIntoExistingMethod(il, instructions);
            }
        }

        private void ProcessGetLinks(TypeDefinition nodeClass)
        {
            List<IMemberDefinition> members = new List<IMemberDefinition>();
            members.AddRange(nodeClass.Fields);
            members.AddRange(nodeClass.Properties);

            MethodDefinition readyMethod = null;
            
            foreach (IMemberDefinition memberDef in members)
            {
                CustomAttribute getAttr = GetFieldAttribute(memberDef, _godotUtilsGetAttrTypeDef);
                if(getAttr == null)
                    continue;

                string nodePath = (string)getAttr.ConstructorArguments[0].Value;
                if(nodePath == null)
                    nodePath = memberDef.Name;
                
                //Console.WriteLine($"found Get link: class - {nodeClass}; member - {memberDef}; nodePath - {nodePath}");

                if (readyMethod == null)
                    readyMethod = GetOrCreateReadyMethod(nodeClass);

                ILProcessor il = readyMethod.Body.GetILProcessor();
                IEnumerable<Instruction> instructions = GenerateSetLinkFromPathInstructions(memberDef, nodePath, il);
                InsertInstructionsIntoExistingMethod(il, instructions);
            }
        }

        private IEnumerable<Instruction> GenerateSetLinkFromPathInstructions(IMemberDefinition fieldOrPropDef,
            string nodePath, ILProcessor il)
        {
            ILHelper c = new ILHelper(il);

            TypeReference memberType;
            IEnumerable<Instruction> memberSetInstructions;
            IEnumerable<Instruction> memberGetInstructions;

            if (fieldOrPropDef is FieldDefinition field)
            {
                memberType = field.FieldType;
                memberSetInstructions = c.SetField(field);
                memberGetInstructions = c.LoadField(field);
            }
            else if (fieldOrPropDef is PropertyDefinition property)
            {
                memberType = property.PropertyType;
                memberSetInstructions = c.SetProperty(property);
                memberGetInstructions = c.LoadProperty(property);
            }
            else
                throw new Exception($"member is {fieldOrPropDef}, but only can be field or prop");

            IEnumerable<Instruction> checkInstructions;
            if (_debugChecksEnabled)
            {
                string messagePrefix = $"Linking {fieldOrPropDef.DeclaringType.Name}::{fieldOrPropDef.Name} " +
                                       $"to node path '{nodePath}': ";
                string nodeNotFoundMessage = messagePrefix + "Node not found";
                string incompatibleTypeMessage = messagePrefix + $"Type of node is incompatible with target";
                
                checkInstructions = c.Branch(true,
                    c.Compose(c.PushThis(), memberGetInstructions, c.IsNull()),
                    c.Branch(true, 
                        c.Compose(
                            c.PushThis(),
                            c.PushString(nodePath),
                            c.CallMethod(_nodePathConversionMethodDef),
                            c.CallMethod(_nodeGetNodeMethodDef),
                            c.IsNull()),
                        c.Compose(c.PushString(nodeNotFoundMessage), c.CallMethod(_godotPushErrorMethodDef)),
                        c.Compose(c.PushString(incompatibleTypeMessage), c.CallMethod(_godotPushErrorMethodDef))
                    ));
            }
            else
                checkInstructions = new List<Instruction>();
            
            
            return c.Compose(
                c.PushThis(),
                c.PushThis(),
                c.PushString(nodePath),
                c.CallMethod(_nodePathConversionMethodDef),
                c.CallMethod(_nodeGetNodeMethodDef),
                c.IsInstance(memberType),
                memberSetInstructions,
                checkInstructions
            );
        }

        private void ProcessEditorLinks(TypeDefinition nodeClass)
        {
            List<IMemberDefinition> members = new List<IMemberDefinition>();
            members.AddRange(nodeClass.Fields);
            members.AddRange(nodeClass.Properties);

            MethodDefinition readyMethod = null;
            
            foreach (IMemberDefinition memberDef in members)
            {
                if(!HasBaseClass(GetFieldOrPropertyType(memberDef).Resolve(), _godotNodeTypeDef))
                    continue;

                CustomAttribute exportAttr = GetFieldAttribute(memberDef, _godotExportAttrTypeDef);
                if(exportAttr == null)
                    continue;
                
                CustomAttribute exportRenameAttr = GetFieldAttribute(memberDef, _godotUtilsExportRenameAttrTypeDef);
                string exportedName = (string)exportRenameAttr?.ConstructorArguments[0].Value ?? memberDef.Name;

                //Console.WriteLine($"found Link: class - {nodeClass}; member - {memberDef}; exportedName - {exportedName}");

                exportedName = SelectNonUsedNameForEditorLinkField(nodeClass, exportedName);
                if (exportedName == null)
                {
                    Console.WriteLine($"cannot select non-used name for editor link: class - {nodeClass}; member - {memberDef}");
                    continue;
                }

                memberDef.CustomAttributes.Remove(exportAttr);
                
                string exportHint = (string)exportAttr.ConstructorArguments[1].Value;
                if (exportHint == null)
                {
                    string memberType = memberDef is FieldDefinition ? "field" : "property";
                    exportHint = $"Link to {memberType} '{memberDef.Name}'";
                }

                if (readyMethod == null)
                    readyMethod = GetOrCreateReadyMethod(nodeClass);

                FieldDefinition exportedField = InjectEditorLinkField(nodeClass, exportedName, exportHint);
                
                ILProcessor il = readyMethod.Body.GetILProcessor();
                IEnumerable<Instruction> instructions = GenerateEditorLinkInstructions(memberDef, exportedField, exportedName, il);
                InsertInstructionsIntoExistingMethod(il, instructions);
            }
        }

        private string SelectNonUsedNameForEditorLinkField(TypeDefinition nodeClass, string baseName)
        {
            string selectedName = baseName;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                if (FindMethodBySimpleName(nodeClass, selectedName) == null &&
                    FindFieldBySimpleName(nodeClass, selectedName) == null &&
                    FindPropertyBySimpleName(nodeClass, selectedName) == null)
                {
                    return selectedName;
                }

                selectedName += "_";
            }

            return null;
        }

        private IEnumerable<Instruction> GenerateEditorLinkInstructions(IMemberDefinition fieldOrPropDef, 
            FieldDefinition exportedFieldDef,
            string exportedName, ILProcessor il)
        {
            ILHelper c = new ILHelper(il);

            TypeReference memberType;
            IEnumerable<Instruction> memberSetInstructions;
            IEnumerable<Instruction> memberGetInstructions;

            if (fieldOrPropDef is FieldDefinition field)
            {
                memberType = field.FieldType;
                memberSetInstructions = c.SetField(field);
                memberGetInstructions = c.LoadField(field);
            }
            else if (fieldOrPropDef is PropertyDefinition property)
            {
                memberType = property.PropertyType;
                memberSetInstructions = c.SetProperty(property);
                memberGetInstructions = c.LoadProperty(property);
            }
            else
                throw new Exception($"member is {fieldOrPropDef}, but only can be field or prop");

            IEnumerable<Instruction> nodePathCheckInstructions;
            IEnumerable<Instruction> postCheckInstructions;
            if (_debugChecksEnabled)
            {
                string messagePrefix = $"Linking {fieldOrPropDef.DeclaringType.Name}::{fieldOrPropDef.Name} ";
                string nodePathNotSetMessage = messagePrefix + "Not set";

                IEnumerable<Instruction> printNotSetMessageInstructions =
                    c.Compose(c.PushString(nodePathNotSetMessage), c.CallMethod(_godotPushErrorMethodDef));

                nodePathCheckInstructions = c.Branch(false,
                    c.Compose(c.PushThis(), c.LoadField(exportedFieldDef), c.IsNull()),
                    c.Branch(true,
                            c.Compose(
                                c.PushThis(), 
                                c.LoadField(exportedFieldDef), 
                                c.CallMethod(_nodePathIsEmptyMethodDef)),
                            printNotSetMessageInstructions
                        ),
                    printNotSetMessageInstructions);
                
                
                
                string nodeNotFoundMessage = messagePrefix + "Node not found";
                string incompatibleTypeMessage = messagePrefix + $"Type of node is incompatible with target";
                
                postCheckInstructions = c.Branch(true,
                    c.Compose(c.PushThis(), memberGetInstructions, c.IsNull()),
                    c.Branch(true, 
                        c.Compose(
                            c.PushThis(),
                            c.PushThis(),
                            c.LoadField(exportedFieldDef),
                            c.CallMethod(_nodeGetNodeMethodDef),
                            c.IsNull()),
                        c.Compose(c.PushString(nodeNotFoundMessage), c.CallMethod(_godotPushErrorMethodDef)),
                        c.Compose(c.PushString(incompatibleTypeMessage), c.CallMethod(_godotPushErrorMethodDef))
                    ));
            }
            else
            {
                nodePathCheckInstructions = new List<Instruction>();
                postCheckInstructions = new List<Instruction>();
            }
            
            return c.Compose(
                nodePathCheckInstructions,
                c.PushThis(),
                c.PushThis(),
                c.PushThis(),
                c.LoadField(exportedFieldDef),
                c.CallMethod(_nodeGetNodeMethodDef),
                c.IsInstance(memberType),
                memberSetInstructions, 
                postCheckInstructions
            );
        }
        
        private FieldDefinition InjectEditorLinkField(TypeDefinition nodeClass, string exportedName, string exportHint)
        {
            CustomAttribute exportAttr = 
                new CustomAttribute(_targetModule.ImportReference(_exportAttrCtorDef))
                {
                    ConstructorArguments =
                    {
                        new CustomAttributeArgument(_targetModule.ImportReference(_godotPropertyHintTypeDef), 0),
                        new CustomAttributeArgument(_targetModule.TypeSystem.String, exportHint)
                    }
                };
                
            FieldDefinition exportedField = new FieldDefinition(exportedName, FieldAttributes.Public, 
                _targetModule.ImportReference(_godotNodePathTypeDef))
            {
                CustomAttributes = { exportAttr }
            };
                
            nodeClass.Fields.Add(exportedField);
            return exportedField;
        }

        private void InsertInstructionsIntoExistingMethod(ILProcessor il, IEnumerable<Instruction> instructions)
        {
            foreach (Instruction instruction in instructions.Reverse<Instruction>())
            {
                Instruction first = il.Body.Instructions[0];
                il.InsertBefore(first, instruction);
            }
        }
        
        public void ModifyDll()
        {
            ReadTargetAssembly();

            bool canProcessGetLinks = _godotUtilsGetAttrTypeDef != null;
            bool canProcessAutoloadLinks = _godotUtilsAutoloadAttrTypeDef != null;
            bool canProcessEditorLinks = _godotExportAttrTypeDef != null;
            
            IEnumerable<TypeDefinition> nodeClasses = GetNodeClasses();

            foreach (var nodeClass in nodeClasses)
            {
                if(canProcessGetLinks)
                    ProcessGetLinks(nodeClass);
                if(canProcessEditorLinks)
                    ProcessEditorLinks(nodeClass);
                if(canProcessAutoloadLinks)
                    ProcessAutoloadLinks(nodeClass);
            }
            
            WriterParameters writerParameters = new WriterParameters
            {
                WriteSymbols = true
            };
            
            _targetModule.Write(writerParameters);
        }
    }
}