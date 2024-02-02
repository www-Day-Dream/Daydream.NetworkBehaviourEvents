using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodAttributes = Mono.Cecil.MethodAttributes;

namespace Daydream.NetworkBehaviourEvents;

public static class Preloader
{
    private const string NetworkBehaviourFullName = "Unity.Netcode.NetworkBehaviour";
    private static ManualLogSource Logger { get; set; }
    private static TypeReference NetworkBehaviourRef { get; set; }
    private static MethodReference[] NetworkBehaviourEvents { get; set; }

    public static IEnumerable<string> TargetDLLs { get; } = new[]
    {
        "Assembly-CSharp.dll"
    };
    
    public static void Patch(AssemblyDefinition assemblyDefinition)
    {
        Logger.LogInfo("Patching NetworkBehaviour's sub-classes to always contain implementations of virtual methods..");
        NetworkBehaviourRef = assemblyDefinition.MainModule.GetTypeReferences()
            .FirstOrDefault(typeRef => typeRef.FullName == NetworkBehaviourFullName);
        if (NetworkBehaviourRef == default)
        {
            Logger.LogError("The game doesn't contain a reference to " + NetworkBehaviourFullName);
            return;
        }

        NetworkBehaviourEvents = NetworkBehaviourRef.Resolve().Methods
            .Where(methodDef => methodDef.IsVirtual && !methodDef.HasGenericParameters && methodDef.Name.StartsWith("On"))
            .Select(methodDef => assemblyDefinition.MainModule.ImportReference(methodDef))
            .ToArray();
        
        assemblyDefinition.MainModule.GetTypes()
            .Where(typeDef => typeDef.IsSubclassOf(NetworkBehaviourFullName))
            .Do(typeDef =>
            {
                foreach (var methodReference in NetworkBehaviourEvents
                             .Where(methodRef => 
                                 typeDef.Methods.All(methodDef => methodDef.Name != methodRef.Name)))
                {
                    CreateEventMethod(assemblyDefinition, methodReference, typeDef);
                }
            });
        assemblyDefinition.Write(Path.Combine(Paths.CachePath, "Assembly-CSharp.dll"));
    }

    private static void CreateEventMethod(AssemblyDefinition assemblyDefinition, MethodReference methodReference,
        TypeDefinition typeDef)
    {
        var newMethod = new MethodDefinition(
            methodReference.Name,
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            assemblyDefinition.MainModule.TypeSystem.Void);
        typeDef.Methods.Add(newMethod);
        newMethod.Body.InitLocals = true;
                    
        foreach (var paramRef in methodReference.Parameters)
            newMethod.Parameters.Add(paramRef.Resolve());
                    
        var processor = newMethod.Body.GetILProcessor();
                    
        processor.Emit(OpCodes.Ldarg_0);
        for (var i = 0; i < methodReference.Parameters.Count; i++)
            processor.Emit(OpCodes.Ldarg, i + 1);
        processor.Emit(OpCodes.Call, methodReference);
        processor.Emit(OpCodes.Ret);
    }

    private static void Initialize()
    {
        Logger = BepInEx.Logging.Logger.CreateLogSource("DayDream.NetworkBehaviourEvents");
    }

    public static void Finish()
    {
        Logger.LogInfo($"Patching complete! Verifying types contain " +
                       $"{{{string.Join(", ", NetworkBehaviourEvents.Select(evt => evt.Name).ToArray())}}}...");
        var assemblyCSharp = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.FullName.StartsWith("Assembly-CSharp"));
        if (assemblyCSharp == default)
        {
            Logger.LogError("Couldn't find Assembly-CSharp within the AppDomain! Was it even patched?!");
            return;
        }

        if (NetworkBehaviourRef == null || NetworkBehaviourEvents == null) return;

        var allTypes = assemblyCSharp.GetTypes()
            .Where(type => type.IsSubclassOf(NetworkBehaviourFullName)).ToArray();
        var typesInCompliance = 0;
        foreach (var allType in allTypes)
        {
            var onEventsTotal = NetworkBehaviourEvents
                .Aggregate(0, (i, reference) => 
                    i + (allType
                        .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(methodInfo => methodInfo.Name == reference.Name) == null ? 0 : 1));
            if (onEventsTotal == NetworkBehaviourEvents.Length)
                typesInCompliance++;
        }
        Logger.LogInfo(typesInCompliance + "/" + allTypes.Length + " NetworkBehaviour Sub-Classes are now in compliance! Useless reference dll can be found in /BepInEx/cache/.");
    }

    private static bool IsSubclassOf(this Type type, string fullClassName)
    {
        if (type.FullName == fullClassName)
            return false;
        for (; type != null; type = type.BaseType)
        {
            if (type.FullName == fullClassName)
                return true;
        }
        return false;
    }
    private static bool IsSubclassOf(this TypeDefinition typeDefinition, string fullClassName)
    {
        if (typeDefinition.FullName == fullClassName)
            return false;
        for (; typeDefinition != null; typeDefinition = typeDefinition.BaseType?.Resolve())
        {
            if (typeDefinition.FullName == fullClassName)
                return true;
        }
        return false;
    }
    
}