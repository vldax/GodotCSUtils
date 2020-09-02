using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace GodotCSUtils.DllMod
{
    public class ModifyDllTask : Task
    {
        [Required]
        public string TargetAssemblyName { get; set; }
        
        [Required]
        public string ProjectDir { get; set; }
        
        [Required]
        public string Configuration { get; set; }

        public bool EnableChecks { get; set; } = true;
        public bool EnableChecksInRelease { get; set; } = false;
        
        
        public override bool Execute()
        {
            string buildType = Configuration.Contains("Release") ? "Release" : "Debug";

            string godotLinkedAssembliesDir = $"{ProjectDir}.mono/temp/bin/{Configuration}/";
            string targetDLLPath = $"{godotLinkedAssembliesDir}{TargetAssemblyName}.dll";
            string godotMainAssemblyDir = $"{ProjectDir}.mono/assemblies/{buildType}/";
            bool debugChecksEnabled = EnableChecks && (buildType == "Debug" || EnableChecksInRelease);

            using (GodotDllModifier dllModifier = new GodotDllModifier(targetDLLPath,
                godotMainAssemblyDir, 
                godotLinkedAssembliesDir, 
                debugChecksEnabled))
            {
                dllModifier.ModifyDll();
            }
            
            Log.LogMessage(MessageImportance.High, "And done");
            return true;
        }
    }
}