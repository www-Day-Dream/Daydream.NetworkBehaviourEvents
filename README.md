## Network Behaviour Event Preloader
A Preload Patcher that adds the methods OnNetworkSpawn, OnNetworkDespawn, OnGainedOwnership, OnLostOwnership, OnNetworkObjectParentChanged, and OnDestroy to all NetworkBehaviours in 'Assembly-CSharp.dll'.

### How It's Implemented
Besides sanity checks, all methods are implemented with the following code snippet (TLDR it calls base.OnXYZ() and returns):
```csharp
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
```
References to NetworkBehaviour are resolved from the 'Assembly-CSharp'.