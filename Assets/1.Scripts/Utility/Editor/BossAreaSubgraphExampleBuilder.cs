using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class BossAreaSubgraphExampleBuilder
{
    private const string GraphPath = "Assets/8.BehaviorTreeGraph/Boss/Wells&No.23/No.23 Boss Area Subgraph Example.asset";
    private const string RequestPath = "Temp/CreateBossAreaSubgraphExample.request";

    [InitializeOnLoadMethod]
    private static void CreateFromRequestFile()
    {
        if (!File.Exists(RequestPath))
        {
            return;
        }

        EditorApplication.delayCall += () =>
        {
            if (!File.Exists(RequestPath))
            {
                return;
            }

            File.Delete(RequestPath);
            Create();
        };
    }

    [MenuItem("Tools/Behavior/Create No.23 Boss Area Subgraph Example")]
    public static void Create()
    {
        DeleteExistingAsset();

        Type graphType = FindType("Unity.Behavior.BehaviorAuthoringGraph");
        UnityEngine.Object graph = ScriptableObject.CreateInstance(graphType);
        graph.name = Path.GetFileNameWithoutExtension(GraphPath);

        Directory.CreateDirectory(Path.GetDirectoryName(GraphPath).Replace('\\', '/'));
        AssetDatabase.CreateAsset(graph, GraphPath);
        Invoke(graph, "EnsureAssetHasBlackboard");

        object blackboard = GetMember(graph, "Blackboard");
        var bossArea = AddVariable(blackboard, "BossArea", typeof(GameObject), null, true);
        var isOpen = AddVariable(blackboard, "IsOpen", typeof(bool), false, true);
        var currentPlayerNumber = AddVariable(blackboard, "CurrentPlayerNumber", typeof(int), 0, true);
        var totalPlayerNumber = AddVariable(blackboard, "TotalPlayerNumber", typeof(int), 0, true);
        Invoke(blackboard, "SetAssetDirty");

        object start = CreateRuntimeNode(graph, "Unity.Behavior.Start", new Vector2(0f, 0f));
        object topBranch = CreateRuntimeNode(graph, "Unity.Behavior.BranchingConditionComposite", new Vector2(0f, 140f), OutputPort(start));
        AddComparisonCondition(topBranch, isOpen, Equal(), false, null);

        object topFalseFailure = CreateRuntimeNode(graph, "ReturnFailAction", new Vector2(220f, 360f), OutputPort(topBranch, "False"));
        object parallel = CreateRuntimeNode(graph, "Unity.Behavior.ParallelAnySuccess", new Vector2(0f, 360f), OutputPort(topBranch, "True"));

        object enterSequence = CreateRuntimeNode(graph, "Unity.Behavior.SequenceComposite", new Vector2(-320f, 520f), OutputPort(parallel));
        object waitEnter = CreateWaitForTrigger(graph, new Vector2(-520f, 690f), OutputPort(enterSequence), bossArea, 0);
        object plusEnter = CreatePlusInt(graph, new Vector2(-300f, 780f), OutputPort(enterSequence), currentPlayerNumber, 1);
        object allPlayersBranch = CreateRuntimeNode(graph, "Unity.Behavior.BranchingConditionComposite", new Vector2(-300f, 920f), OutputPort(plusEnter));
        AddComparisonCondition(allPlayersBranch, currentPlayerNumber, Equal(), null, totalPlayerNumber);

        object openSequence = CreateSequenceGroup(graph, new Vector2(-420f, 1120f), OutputPort(allPlayersBranch, "True"));
        object setOpen = CreateSetVariable(graph, new Vector2(-510f, 1280f), isOpen, true);
        object disableBossArea = CreateSetEnableBoxCollider(graph, new Vector2(-260f, 1280f), bossArea, false);
        AddToSequence(graph, setOpen, openSequence, 0);
        AddToSequence(graph, disableBossArea, openSequence, 1);

        object allPlayersFalseFailure = CreateRuntimeNode(graph, "ReturnFailAction", new Vector2(-90f, 1120f), OutputPort(allPlayersBranch, "False"));

        object exitSequence = CreateRuntimeNode(graph, "Unity.Behavior.SequenceComposite", new Vector2(340f, 520f), OutputPort(parallel));
        object waitExit = CreateWaitForTrigger(graph, new Vector2(140f, 690f), OutputPort(exitSequence), bossArea, 1);
        object plusExit = CreatePlusInt(graph, new Vector2(360f, 780f), OutputPort(exitSequence), currentPlayerNumber, -1);
        object zeroBranch = CreateRuntimeNode(graph, "Unity.Behavior.BranchingConditionComposite", new Vector2(700f, 780f), OutputPort(exitSequence));
        AddComparisonCondition(zeroBranch, currentPlayerNumber, LowerOrEqual(), 0, null);

        object setZero = CreateSetVariable(graph, new Vector2(560f, 1000f), currentPlayerNumber, 0, OutputPort(zeroBranch, "True"));
        object zeroFalseFailure = CreateRuntimeNode(graph, "ReturnFailAction", new Vector2(840f, 1000f), OutputPort(zeroBranch, "False"));

        _ = waitEnter;
        _ = waitExit;
        _ = plusExit;
        _ = topFalseFailure;
        _ = allPlayersFalseFailure;
        _ = setZero;
        _ = zeroFalseFailure;

        Invoke(graph, "SetAssetDirty", true);
        InvokeIfExists(blackboard, "BuildRuntimeBlackboard");
        Invoke(graph, "BuildRuntimeGraph", true);

        EditorUtility.SetDirty(graph);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(GraphPath, ImportAssetOptions.ForceUpdate);
        Selection.activeObject = graph;

        Debug.Log($"Created Behavior Graph example: {GraphPath}", graph);
    }

    private static void DeleteExistingAsset()
    {
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(GraphPath) != null)
        {
            AssetDatabase.DeleteAsset(GraphPath);
        }
    }

    private static object CreateWaitForTrigger(object graph, Vector2 position, object connectedPort, object bossArea, int messageType)
    {
        object node = CreateRuntimeNode(graph, "Unity.Behavior.WaitForTriggerAction", position, connectedPort);
        LinkField(node, "Agent", bossArea, typeof(GameObject));
        SetLocalField(node, "MessageType", EnumValue("Unity.Behavior.WaitForPhysicsMessageAction+EMessageType", messageType));
        SetLocalField(node, "Tag", "Player");
        return node;
    }

    private static object CreatePlusInt(object graph, Vector2 position, object connectedPort, object currentPlayerNumber, int amount)
    {
        object node = CreateRuntimeNode(graph, "PlusIntAction", position, connectedPort);
        LinkField(node, "A", currentPlayerNumber, typeof(int));
        SetLocalField(node, "B", amount);
        LinkField(node, "C", currentPlayerNumber, typeof(int));
        return node;
    }

    private static object CreateSetVariable(object graph, Vector2 position, object variable, object value, object connectedPort = null)
    {
        Type valueType = GetVariableType(variable);
        object node = CreateRuntimeNode(graph, "Unity.Behavior.SetVariableValueAction", position, connectedPort);
        LinkField(node, "Variable", variable, valueType);
        SetLocalField(node, "Value", value, valueType);
        return node;
    }

    private static object CreateSetEnableBoxCollider(object graph, Vector2 position, object bossArea, bool enabled)
    {
        object node = CreateRuntimeNode(graph, "SetEnableBoxColliderAction", position);
        LinkField(node, "gameObject", bossArea, typeof(GameObject));
        SetLocalField(node, "Enable", enabled);
        return node;
    }

    private static object CreateSequenceGroup(object graph, Vector2 position, object connectedPort)
    {
        return CreateNode(graph, FindType("Unity.Behavior.GraphFramework.SequenceNodeModel"), position, connectedPort, null);
    }

    private static object CreateRuntimeNode(object graph, string runtimeTypeName, Vector2 position, object connectedPort = null)
    {
        Type runtimeType = FindType(runtimeTypeName);
        object nodeInfo = InvokeStatic(FindType("Unity.Behavior.NodeRegistry"), "GetInfo", runtimeType);
        if (nodeInfo == null)
        {
            throw new InvalidOperationException($"NodeInfo not found for runtime node type '{runtimeTypeName}'.");
        }

        Type modelType = SerializableTypeToType(GetMember(nodeInfo, "ModelType"));
        return CreateNode(graph, modelType, position, connectedPort, nodeInfo);
    }

    private static object CreateNode(object graph, Type modelType, Vector2 position, object connectedPort, object nodeInfo)
    {
        MethodInfo createNode = FindMethod(graph.GetType(), "CreateNode", typeof(Type), typeof(Vector2), FindType("Unity.Behavior.GraphFramework.PortModel"), typeof(object[]));
        object[] args = nodeInfo == null ? null : new[] { nodeInfo };
        return createNode.Invoke(graph, new object[] { modelType, position, connectedPort, args });
    }

    private static void AddComparisonCondition(object branchNode, object variable, object op, object comparisonValue, object comparisonVariable)
    {
        Type conditionModelType = FindType("Unity.Behavior.ConditionModel");
        Type behaviorGraphNodeModelType = FindType("Unity.Behavior.BehaviorGraphNodeModel");
        Type conditionBaseType = FindType("Unity.Behavior.Condition");
        object conditionInfo = InvokeStatic(FindType("Unity.Behavior.ConditionUtility"), "GetInfoForConditionType", FindType("Unity.Behavior.VariableComparisonCondition"));

        ConstructorInfo constructor = conditionModelType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { behaviorGraphNodeModelType, conditionBaseType, FindType("Unity.Behavior.ConditionInfo") },
            null);
        object condition = constructor.Invoke(new[] { branchNode, null, conditionInfo });

        LinkField(condition, "Variable", variable, typeof(object));
        SetLocalField(condition, "Operator", op, FindType("Unity.Behavior.ConditionOperator"));

        if (comparisonVariable != null)
        {
            LinkField(condition, "ComparisonValue", comparisonVariable, GetVariableType(comparisonVariable));
        }
        else
        {
            Type valueType = comparisonValue?.GetType() ?? GetVariableType(variable);
            SetLocalField(condition, "ComparisonValue", comparisonValue, valueType);
        }

        IList conditions = (IList)GetMember(branchNode, "ConditionModels");
        conditions.Add(condition);
    }

    private static void AddToSequence(object graph, object node, object sequence, int index)
    {
        MethodInfo addNodeToSequence = FindMethod(graph.GetType(), "AddNodeToSequence", FindType("Unity.Behavior.GraphFramework.NodeModel"), FindType("Unity.Behavior.GraphFramework.SequenceNodeModel"), typeof(int));
        addNodeToSequence.Invoke(graph, new[] { node, sequence, index });
    }

    private static object AddVariable(object blackboard, string name, Type valueType, object value, bool exposed)
    {
        Type variableType = FindType("Unity.Behavior.GraphFramework.TypedVariableModel`1").MakeGenericType(valueType);
        object variable = Activator.CreateInstance(variableType);
        SetMember(variable, "Name", name);
        SetMember(variable, "IsExposed", exposed);
        SetMember(variable, "ObjectValue", value);

        IList variables = (IList)GetMember(blackboard, "Variables");
        variables.Add(variable);
        return variable;
    }

    private static void LinkField(object model, string fieldName, object variable, Type variableType)
    {
        MethodInfo setField = FindMethods(model.GetType(), "SetField")
            .First(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return !method.IsGenericMethodDefinition && parameters.Length == 3;
            });
        setField.Invoke(model, new[] { fieldName, variable, variableType });
    }

    private static void SetLocalField(object model, string fieldName, object value)
    {
        Type valueType = value?.GetType() ?? typeof(object);
        SetLocalField(model, fieldName, value, valueType);
    }

    private static void SetLocalField(object model, string fieldName, object value, Type valueType)
    {
        MethodInfo setField = FindMethods(model.GetType(), "SetField")
            .First(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return method.IsGenericMethodDefinition && parameters.Length == 2;
            })
            .MakeGenericMethod(valueType);
        setField.Invoke(model, new[] { fieldName, value });
    }

    private static object OutputPort(object node, string name = "OutputPort")
    {
        object port = Invoke(node, "FindPortModelByName", name);
        if (port == null)
        {
            throw new InvalidOperationException($"Port '{name}' not found on node '{node}'.");
        }

        bool isFloating = (bool)GetMember(port, "IsFloating");
        if (!isFloating)
        {
            return port;
        }

        IList connections = (IList)GetMember(port, "Connections");
        if (connections.Count == 0)
        {
            return port;
        }

        object floatingInputPort = connections[0];
        object floatingNode = GetMember(floatingInputPort, "NodeModel");
        return Invoke(floatingNode, "FindPortModelByName", "OutputPort");
    }

    private static Type GetVariableType(object variable)
    {
        object type = GetMember(variable, "Type");
        return (Type)type;
    }

    private static object Equal() => EnumValue("Unity.Behavior.ConditionOperator", 0);

    private static object LowerOrEqual() => EnumValue("Unity.Behavior.ConditionOperator", 5);

    private static object EnumValue(string typeName, int value)
    {
        Type enumType = FindType(typeName);
        return Enum.ToObject(enumType, value);
    }

    private static Type SerializableTypeToType(object serializableType)
    {
        return (Type)GetMember(serializableType, "Type");
    }

    private static Type FindType(string fullName)
    {
        Type type = Type.GetType(fullName);
        if (type != null)
        {
            return type;
        }

        type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(fullName))
            .FirstOrDefault(found => found != null);

        if (type == null)
        {
            throw new TypeLoadException($"Type not found: {fullName}");
        }

        return type;
    }

    private static object Invoke(object target, string name, params object[] args)
    {
        Type[] argumentTypes = args.Select(arg => arg?.GetType() ?? typeof(object)).ToArray();
        MethodInfo method = FindMethod(target.GetType(), name, argumentTypes);
        return method.Invoke(target, args);
    }

    private static void InvokeIfExists(object target, string name, params object[] args)
    {
        try
        {
            Invoke(target, name, args);
        }
        catch (MissingMethodException)
        {
        }
    }

    private static object InvokeStatic(Type type, string name, params object[] args)
    {
        Type[] argumentTypes = args.Select(arg => arg?.GetType() ?? typeof(object)).ToArray();
        MethodInfo method = FindMethod(type, name, argumentTypes);
        return method.Invoke(null, args);
    }

    private static MethodInfo FindMethod(Type type, string name, params Type[] argumentTypes)
    {
        while (type != null)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, argumentTypes, null);
            if (method != null)
            {
                return method;
            }

            foreach (MethodInfo candidate in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (candidate.Name != name)
                {
                    continue;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length != argumentTypes.Length)
                {
                    continue;
                }

                bool matches = true;
                for (int i = 0; i < parameters.Length; i++)
                {
                    Type parameterType = parameters[i].ParameterType;
                    Type argumentType = argumentTypes[i];
                    if (argumentType == typeof(object) || parameterType.IsAssignableFrom(argumentType))
                    {
                        continue;
                    }

                    matches = false;
                    break;
                }

                if (matches)
                {
                    return candidate;
                }
            }

            type = type.BaseType;
        }

        throw new MissingMethodException(name);
    }

    private static MethodInfo[] FindMethods(Type type, string name)
    {
        return EnumerateTypeHierarchy(type)
            .SelectMany(current => current.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method => method.Name == name)
            .ToArray();
    }

    private static IEnumerable<Type> EnumerateTypeHierarchy(Type type)
    {
        while (type != null)
        {
            yield return type;
            type = type.BaseType;
        }
    }

    private static object GetMember(object target, string name)
    {
        Type type = target.GetType();
        while (type != null)
        {
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                return property.GetValue(target);
            }

            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return field.GetValue(target);
            }

            type = type.BaseType;
        }

        throw new MissingMemberException(target.GetType().FullName, name);
    }

    private static void SetMember(object target, string name, object value)
    {
        Type type = target.GetType();
        while (type != null)
        {
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value);
                return;
            }

            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            type = type.BaseType;
        }

        throw new MissingMemberException(target.GetType().FullName, name);
    }
}
