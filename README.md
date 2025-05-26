# JSG xNode

<img align="right" width="100" height="100" src="https://user-images.githubusercontent.com/37786733/41541140-71602302-731a-11e8-9434-79b3a57292b6.png">
<br>

Node based graph toolkit for Unity 6. Enhanced fork of [Siccity/xNode](https://github.com/Siccity/xNode) with additional editor features and stability improvements.
<br>

## Core Features

- **Runtime Performance**: Minimal footprint, cached reflection for editor operations only
- **Zero Dependencies**: No external libraries required
- **Unity Integration**: Compatible with Unity 2018.1+ through Unity 6
- **Assembly Definitions**: Full support included
- **Editor Tools**: Custom inspectors, visual debugging, preference management

<div align="center">
    <br>
    <img src="./Samples~/Example.png?raw=true" width="45%">
    <img src="./Samples~/Example2.png?raw=true" width="45%">
    <p align="center">
    <img src="https://user-images.githubusercontent.com/6402525/53689100-3821e680-3d4e-11e9-8440-e68bd802bfd9.png" width="30%">
    </p>
</div>

## Enhancements

### Editor Improvements
- **Node Minimization**: Toggle node body visibility with eye icon in bottom-left corner for significant performance improvements
- **Toolbar**: Zoom in/out, change noodle type and more
- **Noodle Customization**: Per-graph noodle type selection via toolbar dropdown, plus global default settings
- **Parent Graph Navigation**: Hierarchical graph toolbar with breadcrumb navigation
- **Node Dragging**: Drag selected nodes using the drag handle below the toolbar
- **Keep Open Setting**: Auto-open last graph functionality

### Technical Enhancements
- **JSON Settings Storage** (v1.8.7): Settings stored in `Assets/Plugins/Unity Editor/xNode/` as JSON files instead of EditorPrefs
- **Asset Processing** (v1.9.0): Robust missing script handling with automatic cleanup
- **Toolbar Positioning** (v1.9.3): Fixed toolbar layout issues
- **Port Attributes** (v1.8.5, v1.8.8-v1.8.9): Extended port customization with hide, field, and tooltip overrides

## Installation

**Unity Package Manager**
```json
"com.jahnstargames.xnode": "https://github.com/JahnStar/JSG-xNode.git"
```

**Manual Installation**
Download and extract to your Assets folder.

## Sample Projects

Includes comprehensive implementations:

- **DialogueSystem**: Branching conversation system
- **LogicToy**: Boolean logic gates and circuit simulation  
- **MathGraph**: Mathematical operations and formula evaluation
- **RuntimeMathGraph**: Dynamic graph creation and runtime evaluation
- **StateMachine**: Finite state machine for AI and game flow
- **xNodeDialogueSystem**: Advanced dialogue with character management

Import via Package Manager → JSG xNode → Samples.

## Quick Start

### Creating Graphs
```csharp
[CreateAssetMenu(fileName = "New Graph", menuName = "Your Framework/Graph")]
public class YourGraph : NodeGraph { }
```

### Creating Nodes
```csharp
[NodeWidth(200), NodeTint(40, 95, 65)]
[CreateNodeMenu("New/Calculator")]
public class CalculatorNode : Node {
    [Input] public float valueA;
    [Input] public float valueB;
    [Output] public float result;
    
    public Operation operation = Operation.Add;
    public enum Operation { Add, Subtract, Multiply, Divide }
    
    public override object GetValue(NodePort port) {
        float a = GetInputValue<float>("valueA", valueA);
        float b = GetInputValue<float>("valueB", valueB);
        
        return operation switch {
            Operation.Add => a + b,
            Operation.Subtract => a - b,
            Operation.Multiply => a * b,
            Operation.Divide => b != 0 ? a / b : 0,
            _ => 0
        };
    }
}
```

### Custom Node Editor
```csharp
[CustomNodeEditor(typeof(CalculatorNode))]
public class CalculatorNodeEditor : NodeEditor {
    public override void OnBodyGUI() {
        serializedObject.Update();
        NodeEditorGUILayout.PropertyField(serializedObject.FindProperty("valueA"));
        NodeEditorGUILayout.PropertyField(serializedObject.FindProperty("valueB"));
        NodeEditorGUILayout.PropertyField(serializedObject.FindProperty("operation"));
        NodeEditorGUILayout.PropertyField(serializedObject.FindProperty("result"));
        serializedObject.ApplyModifiedProperties();
    }
}
```

## Port System

### Port Attributes
```csharp
[Input(backingValue = ShowBackingValue.Never, connectionType = ConnectionType.Multiple)]
public List<float> multipleInputs;

[Output(dynamicPortList = true)] 
public float[] dynamicOutputs;

[Input(typeConstraint = TypeConstraint.Strict)]
public Transform strictTransform;

// Hide field in node but show in inspector
[NodeField(hideInNode: true), SerializeField, Header("Node")] 
protected internal string ID;

[NodeField(hideInNode: true), SerializeField, TextArea(1, 9)] 
protected internal string message;

// Custom tooltip and label override
[OverrideTooltip("GameObjectNode")]
[Input(ShowBackingValue.Always, ConnectionType.Override, overrideLabel = "Game Object")] 
public GameObjectNode gameObjectNode;

// Empty port display
[Output(ShowBackingValue.Never, ConnectionType.Override, overrideLabel = " "), HeyHideInInspector] 
public Node empty;
```

### Dynamic Ports
```csharp
public class DynamicNode : Node {
    protected override void Init() {
        base.Init();
        AddDynamicInput(typeof(float), fieldName: "dynamicInput");
        AddDynamicOutput(typeof(float), fieldName: "dynamicOutput");
    }
}
```

## Graph Editors

### Custom Graph Editor with Node Filtering
```csharp
[CustomNodeGraphEditor(typeof(YourGraph), "YourFramework.Settings")]
public class YourGraphEditor : NodeGraphEditor {
    public override string GetNodeMenuName(Type type) {
        if (type.Namespace.Contains("YourFrameworkName")) return base.GetNodeMenuName(type);
        else return null; // Hide other system nodes
    }
    
    public override Gradient GetNoodleGradient(NodePort output, NodePort input) {
        var gradient = base.GetNoodleGradient(output, input);
        var highlightColor = Color.yellow;
        
        GradientColorKey[] colorKeys = gradient.colorKeys;
        for (int i = 0; i < colorKeys.Length; i++) {
            colorKeys[i].color = Color.Lerp(colorKeys[i].color, highlightColor, 0.5f);
        }
        gradient.SetKeys(colorKeys, gradient.alphaKeys);
        return gradient;
    }
}
```

### Advanced Node Editor with Conditional Ports
```csharp
[NodeWidth(200), NodeTint(40, 95, 65)]
[CreateNodeMenu("New/Example/Behavior")]
public class BehaviorNode : Node {
    [Input(hidePort = true)] private BaseWeightNode choice;
    private List<BaseWeightNode> weights;
    protected internal List<Node> states;
    
    public enum Type { Utility, Choice }
    [NodeEnum] public Type type;
}
[CustomNodeEditor(typeof(BehaviorNode))]
public class BehaviorNodeEditor : NodeEditor {
    private SerializedObject serializedBehaviorNode;
    private int type = -1;
    
    public override void OnBodyGUI() {
        serializedBehaviorNode ??= new SerializedObject(target);
        serializedBehaviorNode.Update();
        
        NodeEditorGUILayout.PropertyField(serializedObject.FindProperty("type"));
        
        BehaviorNode behaviorNode = target as BehaviorNode;
        
        if (type >= 0) {
            if (type == (int)BehaviorNode.Type.Choice) {
                NodeEditorGUILayout.PortField(behaviorNode.GetPort("choice"));
                EditorGUILayout.Space();
            }
            else if (behaviorNode.HasPort("choice")) {
                var choicePort = behaviorNode.GetInputPort("choice");
                if (choicePort.IsConnected) choicePort.Disconnect(choicePort.Connection);
            }
            
            if (type == (int)BehaviorNode.Type.Utility) {
                NodeEditorGUILayout.DynamicPortList("weights", typeof(BaseWeightNode), 
                    serializedBehaviorNode, NodePort.IO.Input);
                EditorGUILayout.Space();
            }
            else {
                int i = 0;
                while (behaviorNode.HasPort($"weights {i}")) {
                    behaviorNode.RemoveDynamicPort($"weights {i}");
                    i++;
                }
            }
        }
        
        NodeEditorGUILayout.DynamicPortList("states", typeof(Node), serializedBehaviorNode, NodePort.IO.Output);
        serializedBehaviorNode.ApplyModifiedProperties();
        
        type = (int)behaviorNode.type;
    }
}
```

## Scene Graphs

For scene-specific graphs that reference GameObjects:
```csharp
public class MySceneGraph : SceneGraph<CalculatorGraph> { }
```

## Configuration

### Editor Preferences
Access via `Edit → Preferences → Node Editor`:

- **Grid Settings**: Snap behavior, zoom limits, visual appearance
- **System Settings**: Auto-save, auto-open, keep-open functionality  
- **Node Settings**: Colors, connection styles, port tooltips
- **Type Colors**: Custom color coding for data types

### JSON Storage
Settings are stored in `Assets/Plugins/Unity Editor/xNode/[GraphType].Settings.json` with project-specific configurations including custom color schemes and editor behavior preferences.

## Architecture

### Node System
- **BaseNode**: Foundation class with serialization support
- **Dynamic Ports**: Runtime port creation and management
- **Type Safety**: Compile-time type checking for connections
- **Custom Editors**: Unity-style inspector integration

### Performance Characteristics
- **Editor Only**: All visual scripting overhead removed from builds
- **Cached Reflection**: Editor reflection cached for performance
- **Minimal Runtime**: Core functionality designed for production use
- **Scalable**: Optimized for complex node networks

## License

MIT License

Copyright (c) 2017 Thor Brigsted and contributors  
Copyright (c) 2024 Halil Emre Yıldız (Jahn Star Games)

## Links

- **Original Repository**: [Siccity/xNode](https://github.com/Siccity/xNode)
- **Documentation**: [xNode Wiki](https://github.com/Siccity/xNode/wiki)
- **Package Repository**: [JahnStar/JSG-xNode](https://github.com/JahnStar/JSG-xNode)