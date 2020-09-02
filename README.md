# GodotCSUtils
## Features:

* Link node to field\property in C# script without `GetNode("...")` (analog to onready keyword in gdscript)
* Link autoload node to field\property
* Export node from C# script to set it in editor
* Show errors if node not found or of incorrect type
* No runtime overhead (no reflection)

## Usage:
* Download binaries from [Release page](https://github.com/crym0nster/GodotCSUtils/releases).
* Place binaries somewhere in project directory (e.g. bin/ directory inside project root).
* Add following fragment to .csproj of your project at the end (right before `</Project>`) assuming that binaries located in bin/ directory inside project root:
	
```
<UsingTask AssemblyFile="$(ProjectDir)bin/GodotCSUtils.DllMod.dll" 
             TaskName="GodotCSUtils.DllMod.ModifyDllTask" />
  <Target Name="ModifyDllTask" AfterTargets="Build">
    <GodotCSUtils.DllMod.ModifyDllTask  ProjectDir="$(ProjectDir)" 
                                        Configuration="$(Configuration)" 
                                        TargetAssemblyName="$(AssemblyName)" 
                                        EnableChecks="True" 
                                        EnableChecksInRelease="False" />
  </Target>
  ```
  
  * Reference assembly `GodotCSUtils.Runtime.dll` from your game's C# project
  * You can enable or disable debug checks by manipulating parameters in above fragment. Debug checks will print error if some node not found by specified path or if node found is incompatible by type with field in code.
  

## Examples:
Get node named "Target" from immediate children of this node:

`[Get] private Spatial Target;`

Get node by path "Player/Camera" relative to this node:

`[Get("Player/Camera")] private Camera _camera;`

Get autoload named "Manager":

`[Autoload] public ManagerAutoload Manager;`

Override autoload name to "GameManager":

`[Autoload("GameManager")] public GameManager Manager;`

Export node to be selectable from editor:

`[Export] private Node _selectMe;`

Export node and override in-editor name to be "Player":

`[Export] [ExportRename("Player")] private Node _selectMe;`
