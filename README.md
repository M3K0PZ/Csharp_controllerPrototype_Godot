# C# Controller Prototype for Godot

This project provides a reusable controller for prototyping in Godot using C#. The controller is designed to be easily integrated into any Godot C# project : just drag and drop the scene and script into your project to get started.

## Features

- Camera control in pov (will be improved to toggle between first-person and third-person views)
- Basic movement controls
- Jump
- Crouch
- Sprint
- Everything is customizable directly in the inspector, values, actions, and more.

## Usage

### You already have a Godot project set up with C#
1. Drag and drop the folder and use the scene.
2. Customize as needed for your usage.

### You want to create a new Godot project with C# and use the controller
1. Create a new Godot project with the .Net version
2. Make sure to go into project -> tools -> C# and Create a C# solution
3. Drag and drop the folder into your project
4. Customize as needed for your usage.

### I can look around but I can't move !?
Make sure to set the input actions in your project settings.
You can see what the controller expects in the editor, by opening the Input Actions tab after clicking on the prototype, you can also rename them if you want.

## Requirements
- Godot Engine (with C# enabled, that's the .Net version on their website)
- .NET SDK (compatible with your Godot version)

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
