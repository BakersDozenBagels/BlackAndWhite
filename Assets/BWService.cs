using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

/* Note to anyone who wants to make modules like these:
 * 
 * Please don't copy this code into your module. It would be better to make this
 * an API if multiple mods are going to do this.
 * */

public class BWService : MonoBehaviour
{
    [SerializeField]
    private GameObject _fullWhitePrefab;

    private static bool _hasPatched;
    private static Component _whitePrefab;
    private static int _blackCount; //Accurate for the current bomb. This is reset when we spwan Whites.
    private static Action<object> _activateNeedy;

    public static void ActivateNeedy(object NeedyComponent)
    {
        if(_activateNeedy != null)
            _activateNeedy(NeedyComponent);
        else
        {
            Debug.LogFormat("[Black and White] No expression found, using Reflection instead.");
            NeedyComponent.GetType().MethodCall("ResetAndStart", NeedyComponent, new object[0]);
        }
    }

    private void Start()
    {
        transform.localPosition = Vector3.zero;
        _whitePrefab = _fullWhitePrefab.GetComponent(ReflectionHelper.FindTypeInGame("BombComponent"));
        SceneManager.sceneLoaded += PatchAll; //Ensure all relevant code is loaded before we modify it
    }

    private void CreateStartNeedy()
    {
        Type t = ReflectionHelper.FindTypeInGame("NeedyComponent");
        ParameterExpression param = Expression.Parameter(typeof(object), "module");
        UnaryExpression castParam = Expression.Convert(param, t);
        MethodCallExpression call = Expression.Call(castParam, t.Method("ResetAndStart"));

        Expression<Action<object>> func = Expression.Lambda<Action<object>>(call, param);
        _activateNeedy = func.Compile();
    }

    private void PatchAll(Scene _, LoadSceneMode __)
    {
        if(_hasPatched)
            return;

        //Create a method to start a needy component.
        CreateStartNeedy();

        Harmony _harmony = new Harmony("BlackAndWhiteKTANE");

        //We want to know when Black has been picked (to add White)
        Type t = ReflectionHelper.FindTypeInGame("BombGenerator");
        MethodBase m = t.Method("SelectWeightedRandomComponentType");
        HarmonyMethod p = new HarmonyMethod(GetType().Method("SelectRandomPostfix"));
        _harmony.Patch(m, postfix: p);

        //We want to be able to instantiate Whites before modules are placed on any face
        m = t.Method("CreateBomb");
        p = new HarmonyMethod(GetType().Method("CreateBombTranspiler"));
        _harmony.Patch(m, transpiler: p);

        //We want to set the prefab to be active, but if we do that before it's instantiated some components are duplicated
        m = t.Method("InstantiateComponent");
        p = new HarmonyMethod(GetType().Method("InstantiateComponentTranspiler"));
        _harmony.Patch(m, transpiler: p);

        //Any pool that can spawn a Black must be counted as twice as big, to accommodate a White
        m = t.Method("GetBombPrefab");
        p = new HarmonyMethod(GetType().Method("CountModsPrefix"));
        HarmonyMethod p2 = new HarmonyMethod(GetType().Method("CountModsPostfix"));
        _harmony.Patch(m, prefix: p, postfix: p2);

        //White should not make noise upon activation
        t = ReflectionHelper.FindTypeInGame("NeedyComponent");
        m = t.Method("ResetAndStart");
        p = new HarmonyMethod(GetType().Method("NeedyActivateTranspiler"));
        _harmony.Patch(m, transpiler: p);

        //Tweaks doesn't use the method above, and counts modules itself
        t = ReflectionHelper.FindType("BetterCasePicker");
        if(t != null)
        {
            //We technically modify a nested type auto-generated for a lambda expression.
            t = t.GetNestedType("\u003C\u003Ec", ReflectionHelper.Flags);
            m = t.Method("\u003CHandleGeneratorSetting\u003Eb__5_1");
            p = new HarmonyMethod(GetType().Method("BCPCountPostfix"));
            _harmony.Patch(m, postfix: p);
        }

        _hasPatched = true;
    }

    private static void CountModsPrefix(object settings)
    {
        //Blacks need to count double for sizing
        IList cps = settings.GetType().Field<IList>("ComponentPools", settings);
        foreach(object cp in cps)
            if(cp.GetType().Field<List<string>>("ModTypes", cp).Contains("blackModule") || cp.GetType().Field<int>("SpecialComponentType", cp) == 2)
                cp.GetType().SetField("Count", cp, cp.GetType().Field<int>("Count", cp) * 2);
    }

    private static void CountModsPostfix(object settings)
    {
        //Undo the doubling so the correct number of modules are spawned
        IList cps = settings.GetType().Field<IList>("ComponentPools", settings);
        foreach(object cp in cps)
            if(cp.GetType().Field<List<string>>("ModTypes", cp).Contains("blackModule") || cp.GetType().Field<int>("SpecialComponentType", cp) == 2)
                cp.GetType().SetField("Count", cp, cp.GetType().Field<int>("Count", cp) / 2);
    }

    private static IEnumerable<CodeInstruction> CreateBombTranspiler(IEnumerable<CodeInstruction> instr)
    {
        List<CodeInstruction> instructions = instr.ToList();

        int i = 0;

        for(; i < instructions.Count; i++)
        {
            //Keep the original method until just before this log message
            if(!instructions[i].Is(OpCodes.Ldstr, "Instantiating remaining components on any valid face."))
            {
                yield return instructions[i];
                continue;
            }

            //Load needed data onto the stack (The methoid call will consume these)
            CodeInstruction ld1 = new CodeInstruction(instructions[i])
            {
                opcode = OpCodes.Ldarg_0,
                operand = null
            };
            CodeInstruction ld2 = new CodeInstruction(instructions[i])
            {
                opcode = OpCodes.Ldarg_1,
                operand = null
            };
            CodeInstruction ld3 = new CodeInstruction(instructions[i])
            {
                opcode = OpCodes.Ldloc_S,
                operand = 8
            };

            yield return ld1;
            yield return ld2;
            yield return ld3;
            //Call our method to spawn Whites
            yield return CodeInstruction.Call(
                typeof(BWService),
                "SpawnWhites",
                parameters: new Type[] { typeof(object), typeof(object), typeof(object) },
                generics: new Type[0]
            );
            break;
        }
        //After we've spawned Whites, the rest of the method is unchanged.
        for(; i < instructions.Count; i++)
            yield return instructions[i];

        yield break;
    }

    private static IEnumerable<CodeInstruction> InstantiateComponentTranspiler(IEnumerable<CodeInstruction> instr)
    {
        List<CodeInstruction> instructions = instr.ToList();

        int i = 0;
        MethodInfo mi = typeof(UnityEngine.Object)
            .GetMethods(ReflectionHelper.Flags)
            .First(m => m.IsGenericMethodDefinition && m.GetParameters().Skip(1).Select(pi => pi.ParameterType).SequenceEqual(new Type[] { typeof(Vector3), typeof(Quaternion) }))
            .MakeGenericMethod(typeof(GameObject));

        for(; i < instructions.Count; i++)
        {
            //Keep the original method until just before this method is called
            if(!instructions[i].Calls(mi))
            {
                yield return instructions[i];
                continue;
            }

            //Allow the method to be called, and for the local variable to be set
            yield return instructions[i];
            yield return instructions[i + 1];

            //Load that local variable onto the stack again, then call our method
            //Conveniently, the next instruction is what we want, so we can copy it
            yield return instructions[i + 2];
            yield return CodeInstruction.Call(typeof(BWService), "ActiveTrue", new Type[] { typeof(GameObject) }, new Type[0]);

            //Skip code we've already executed
            i += 2;
            break;
        }
        //Leave the rest of the method unchanged
        for(; i < instructions.Count; i++)
            yield return instructions[i];

        yield break;
    }

    private static IEnumerable<CodeInstruction> NeedyActivateTranspiler(IEnumerable<CodeInstruction> instr, ILGenerator generator)
    {
        List<CodeInstruction> instructions = instr.ToList();

        int i = 0;

        for(; i < instructions.Count; i++)
        {
            //Keep the original method until just before playing this sound
            if(!instructions[i].Is(OpCodes.Ldstr, "needy_activated"))
            {
                yield return instructions[i];
                continue;
            }

            //We have to move a label to have the previous if go to the right place.
            yield return new CodeInstruction(instructions[i + 1]).MoveLabelsFrom(instructions[i]); //this (Copied from later)
            yield return CodeInstruction.Call(typeof(BWService), "CheckNeedySound");

            //If false, play no sound.
            Label lbl = generator.DefineLabel();
            yield return new CodeInstruction(OpCodes.Brfalse, lbl);

            for(; i < instructions.Count; i++)
            {
                yield return instructions[i];
                if(instructions[i].opcode == OpCodes.Pop)
                    break;
            }

            yield return instructions[i + 1].WithLabels(lbl);
            i += 2;
            break;
        }
        //After that, the rest of the method is unchanged.
        for(; i < instructions.Count; i++)
            yield return instructions[i];

        yield break;
    }

    private static bool CheckNeedySound(object inst)
    {
        KMNeedyModule n = ((MonoBehaviour)inst).GetComponent<KMNeedyModule>();
        if(n && n.ModuleDisplayName.EqualsAny("Black", "White"))
            return false; //White shouldn't make activation sounds and neither should Black. Black will play the sound itself.
        return true;
    }

    private static void ActiveTrue(GameObject o)
    {
        o.SetActive(true);
    }

    private static void SpawnWhites(object instance, object settings, object frontFace)
    {
        if(_blackCount == 0)
            return;

        Debug.LogFormat("[Black and White] Instantiating Whites on the back face.");

        Type t = instance.GetType();

        for(int i = 0; i < _blackCount; i++)
        {
            //We simulate the Game's calculations for which face to spawn modules on
            IList vbf = t.Field<IList>("validBombFaces", instance);
            List<object> l = vbf.Cast<object>().ToList();

            if(l.Count == 0)
                Debug.LogFormat("[Black and White] There's no room to spawn White.");
            else
            {
                if(l.Contains(frontFace))
                    l.Remove(frontFace);
                if(l.Count == 0)
                {
                    Debug.LogFormat("[Black and White] There's no room on the back face. Using the front instead.");
                    t.MethodCall("InstantiateComponent", instance, new object[] {
                        frontFace,
                        _whitePrefab,
                        settings }
                    );
                }
                else
                {
                    t.MethodCall("InstantiateComponent", instance, new object[] {
                        l[t.Field<System.Random>("rand", instance).Next(0, l.Count)],
                        _whitePrefab,
                        settings }
                    );
                }
            }
        }

        //At this point, we can forget about this bomb as our job is done.
        _blackCount = 0;
    }

    private static void SelectRandomPostfix(object __instance, string __result)
    {
        if(__result != "blackModule")
            return;
        //If DBML is enabled, the random object will be Tweaks' on the first pass.
        //We don't want to count these then to avoid having too many Whites.
        if(__instance.GetType().Field<object>("rand", __instance).GetType().Name.Contains("Fake"))
            return;

        Debug.LogFormat("[Black and White] Black selected! Adding White.");

        _blackCount++;
    }

    private static void BCPCountPostfix(object pool, ref int __result)
    {
        List<string> types = pool.GetType().Field<List<string>>("ModTypes", pool);
        if(types == null || types.Count == 0)
            return;
        if(types.Contains("blackModule"))
            __result *= 2;
    }
}
