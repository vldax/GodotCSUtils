using Godot;
using System;
using Demo;
using GodotCSUtils;

public class Main : Spatial
{
	[Get] private Spatial Child;
	[Get("123/GrandChild")] private UserType _grandChild;
	[Export] private Node _selectMe;
	[Autoload] public ManagerAutoload Manager;
	
	public override void _Ready()
	{
		GD.Print($"Name of node in Child field: {Child.Name}");
		GD.Print($"Name of node in _grandChild field: {_grandChild.Name}");
		GD.Print($"Name of editor selectable node: {_selectMe.Name}");
		Manager.SayHello();
	}
}
