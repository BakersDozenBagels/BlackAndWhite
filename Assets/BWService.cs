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
    private GameObject _fullWhitePrefab, _fullBlackPrefab;

    private static bool _hasPatched;
    private static Component _whitePrefab, _blackPrefab;
    private static int _blackCount; //Accurate for the current bomb. This is reset when we spwan Whites.
    private static Action<object> _activateNeedy;

    private static object _propertyKey;
    private static readonly List<AddOnConditionData> _propertyAdded = new List<AddOnConditionData>();

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
        _whitePrefab = Instantiate(_fullWhitePrefab.gameObject, transform.parent).GetComponent(ReflectionHelper.FindTypeInGame("BombComponent"));
        _blackPrefab = Instantiate(_fullBlackPrefab.gameObject, transform.parent).GetComponent(ReflectionHelper.FindTypeInGame("BombComponent"));

        Type ms = ReflectionHelper.FindTypeInGame("ModSource");

        ms.SetField("ModName", _whitePrefab.gameObject.AddComponent(ms), "blackWhiteModule");
        ms.SetField("ModName", _blackPrefab.gameObject.AddComponent(ms), "blackWhiteModule");

        Type mnc = ReflectionHelper.FindTypeInGame("ModNeedyComponent");
        FieldInfo fi = mnc.GetField("module", ReflectionHelper.Flags);

        fi.SetValue(_blackPrefab.GetComponent(mnc), _blackPrefab.GetComponent<KMNeedyModule>());
        fi.SetValue(_whitePrefab.GetComponent(mnc), _whitePrefab.GetComponent<KMNeedyModule>());

        if(_whitePrefab == null)
            Debug.LogFormat("[Black and White] White not found!");
        if(_blackPrefab == null)
            Debug.LogFormat("[Black and White] Black not found!");
        if(_whitePrefab == null || _blackPrefab == null)
            throw new Exception("A module was not found.");
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
        Debug.Log("[Black and White] Beginning hook process.");

        //Create a method to start a needy component.
        CreateStartNeedy();
        Debug.Log("[Black and White] Start Needy okay");

        Harmony _harmony = new Harmony("BlackAndWhiteKTANE");

        //We want to know when Black has been picked (to add White)
        Type t = ReflectionHelper.FindTypeInGame("BombGenerator");
        MethodBase m = t.Method("SelectWeightedRandomComponentType");
        HarmonyMethod p = new HarmonyMethod(GetType().Method("SelectRandomPostfix"));
        _harmony.Patch(m, postfix: p);
        Debug.Log("[Black and White] Select Random okay");

        //We want to be able to instantiate Whites before modules are placed on any face
        m = t.Method("CreateBomb");
        p = new HarmonyMethod(GetType().Method("CreateBombTranspiler"));
        _harmony.Patch(m, transpiler: p);
        Debug.Log("[Black and White] Create Bomb okay");

        //We want to set the prefab to be active, but if we do that before it's instantiated some components are duplicated
        m = t.Method("InstantiateComponent");
        p = new HarmonyMethod(GetType().Method("InstantiateComponentTranspiler"));
        _harmony.Patch(m, transpiler: p);
        Debug.Log("[Black and White] Instantiate Component okay");

        //Any pool that can spawn a Black must be counted as twice as big, to accommodate a White
        m = t.Method("GetBombPrefab");
        p = new HarmonyMethod(GetType().Method("CountModsPrefix"));
        HarmonyMethod p2 = new HarmonyMethod(GetType().Method("CountModsPostfix"));
        _harmony.Patch(m, prefix: p, postfix: p2);
        Debug.Log("[Black and White] Count Mods okay");

        //Tweaks doesn't use the method above, and counts modules itself
        t = ReflectionHelper.FindType("BetterCasePicker");
        if(t != null)
        {
            //We technically modify a nested type auto-generated for a lambda expression.
            t = t.GetNestedType("\u003C\u003Ec", ReflectionHelper.Flags);
            if(t != null)
            {
                m = t.Method("\u003CHandleGeneratorSetting\u003Eb__5_1");
                p = new HarmonyMethod(GetType().Method("BCPCountPostfix"));
                _harmony.Patch(m, postfix: p);
                Debug.Log("[Black and White] Better Case Picker okay");

            }
            else
                Debug.LogFormat("[Black and White] Better Case Picker is enabled, but modification failed!");
        }
        else
            Debug.Log("[Black and White] Better Case Picker skipped");

        //White should not make noise upon activation
        t = ReflectionHelper.FindTypeInGame("NeedyComponent");
        m = t.Method("ResetAndStart");
        p = new HarmonyMethod(GetType().Method("NeedyActivateTranspiler"));
        _harmony.Patch(m, transpiler: p);
        Debug.Log("[Black and White] Needy Activate okay");

        //DBML should never make a fake module for Black or White
        t = ReflectionHelper.FindType("Repository");
        if(t != null)
        {
            t = t.GetNestedType("\u003CLoadData\u003Ed__3", ReflectionHelper.Flags);
            if(t != null)
            {
                m = t.Method("MoveNext");
                p = new HarmonyMethod(GetType().Method("DBMLNoFakeTranspiler"));
                _harmony.Patch(m, transpiler: p);
                Debug.Log("[Black and White] DBML Nofake okay");
            }
            else
                Debug.Log("[Black and White] DBML is enabled, but modifications failed!");
        }
        else
            Debug.Log("[Black and White] DBML Nofake not okay");


        //Tweaks needs to only know about Black's module id
        t = ReflectionHelper.FindType("Repository");
        if(t != null)
        {
            StartCoroutine(TweaksRepoFix());
            Debug.Log("[Black and White] Mod Available started");
        }
        else
            Debug.Log("[Black and White] Mod Available not started");

        //Deal with destroyed prefabs
        t = ReflectionHelper.FindTypeInGame("ModManager");
        m = t.Method("GetSolvableBombModules");
        p = new HarmonyMethod(GetType().Method("GetSolvablesPrefix"));
        _harmony.Patch(m, prefix: p);
        Debug.Log("[Black and White] Mod Manger Fix okay");

        ////Deal with destroyed prefabs pt 2
        //t = t.GetNestedType("\u003CCheckAndLoadMods\u003Ec__Iterator0", ReflectionHelper.Flags);
        //m = t.Method("MoveNext");
        //p = new HarmonyMethod(GetType().Method("CheckAndLoadTranspiler"));
        //_harmony.Patch(m, transpiler: p);
        //Debug.Log("[Black and White] Mod Manger Fix 2 okay");

        //Tweaks generates bombs itself, so we need to generate Whites on the back here as well.
        t = ReflectionHelper.FindType("DemandBasedLoading");
        if(t != null)
        {
            t = t.GetNestedType("\u003CInstantiateComponents\u003Ed__25", ReflectionHelper.Flags);
            if(t != null)
            {
                m = t.Method("MoveNext");
                p = new HarmonyMethod(GetType().Method("DBMLGenTranspiler"));
                _harmony.Patch(m, transpiler: p);
                Debug.Log("[Black and White] DBML Generator okay");
            }
            else
            {
                Debug.Log("[Black and White] DBML Generator not okay");
                throw new Exception();
            }
        }

        Debug.Log("[Black and White] Registering API hook...");
        ModdedAPI.AddProperty("LoadOnCondition", null, new Action<object>(SetLoadOnCondition));

        Debug.Log("[Black and White] End hook process.");

        _hasPatched = true;
    }

    private void SetLoadOnCondition(object d)
    {
        if(!(d is IDictionary))
            throw new Exception("SetLoadOnCondition expected an IDictionary, got " + d.GetType().Name);
        IDictionary data = (IDictionary)d;

        object prefab = data["Prefab"];
        Func<object, int> condition = (Func<object, int>)data["Condition"];
        string id;
        Component fab;

        KMBombModule mod = ((GameObject)prefab).GetComponentInChildren<KMBombModule>();
        if(mod == null)
        {
            KMNeedyModule needymod = ((GameObject)prefab).GetComponentInChildren<KMNeedyModule>();
            if(needymod == null)
                throw new Exception("SetLoadOnCondition That's not a module.");
            else
            {
                fab = Instantiate(needymod.gameObject, transform.parent).GetComponent(ReflectionHelper.FindTypeInGame("BombComponent"));
                Type mnc = ReflectionHelper.FindTypeInGame("ModNeedyComponent");
                FieldInfo fi = mnc.GetField("module", ReflectionHelper.Flags);

                fi.SetValue(fab, fab.GetComponent<KMBombModule>());

                id = needymod.ModuleType;
            }
        }
        else
        {
            fab = Instantiate(mod.gameObject, transform.parent).GetComponent(ReflectionHelper.FindTypeInGame("BombComponent"));
            Type mnc = ReflectionHelper.FindTypeInGame("ModBombComponent");
            FieldInfo fi = mnc.GetField("module", ReflectionHelper.Flags);

            fi.SetValue(fab, fab.GetComponent<KMBombModule>());

            id = mod.ModuleType;
        }

        AddOnConditionData aocd = new AddOnConditionData()
        {
            Id = id,
            Condition = condition,
            Prefab = fab
        };

        _propertyAdded.Add(aocd);
    }

    private IEnumerator TweaksRepoFix()
    {
        Type t = ReflectionHelper.FindType("Repository");
        Type t2 = t.GetNestedType("KtaneModule", ReflectionHelper.Flags);
        Type t3 = typeof(List<>).MakeGenericType(t2);
        FieldInfo fi = t2.GetField("SteamID", ReflectionHelper.Flags);
        FieldInfo fi2 = t2.GetField("ModuleID", ReflectionHelper.Flags);
        FieldInfo mfi = t.GetField("Modules", ReflectionHelper.Flags);
        FieldInfo mfl = t.GetField("Loaded", ReflectionHelper.Flags);
        IList l = null;
        yield return new WaitUntil(() => (bool)mfl.GetValue(null));
        yield return null;
        l = (IList)mfi.GetValue(null);
        Debug.Log("[Black and White] Tweaks Repo loaded");

        ClearRepository(); //In case this loads before it's patched

        //DBML needs to not see a module in the bundle
        t = ReflectionHelper.FindTypeInGame("ModManager");
        MonoBehaviour inst = t.Field<MonoBehaviour>("Instance", null);
        if(inst != null)
        {
            IDictionary d = t.Field<IDictionary>("loadedBombComponents", inst);
            if(d == null)
            {
                Debug.Log("[Black and White] Failed to make modules available. Code 2");
                throw new Exception();
            }
            d.Remove("whiteModule");
            //d["blackModule"] = d["ttProtogen"];
            //d.Remove("ttProtogen"); // For testing
            d["blackModule"] = _blackPrefab;
            t.SetField("loadedBombComponents", inst, d);

            //object[] arr = new object[d.Count];
            //d.Keys.CopyTo(arr, 0);
            //Debug.Log(arr.Join(", "));
        }
        else
        {
            Debug.Log("[Black and White] Failed to make modules available. Code 1");
            throw new Exception();
        }
        Debug.Log("[Black and White] Mod Available okay");

        //In case this loads before it's patched
        t = ReflectionHelper.FindType("DemandBasedLoading");
        if(t != null)
        {
            List<string> fm = t.Field<List<string>>("fakedModules", null);
            fm.RemoveAll(s => s.EqualsAny("whiteModule", "blackModule"/*, "ttProtogen"*/));
            Debug.Log("[Black and White] Fake Modules Initial Remove okay");
        }

        t = ReflectionHelper.FindType("BombGenerator");
        object instance = FindObjectOfType(t);
        if(instance != null)
        {
            Debug.Log("[Black and White] Fixing BombGenerator...");
            IList ls = t.Field<IList>("componentPrefabs", instance);
            if(ls != null) //I don't think this is assigned anywhere?
            {
                Type mbct = ReflectionHelper.FindTypeInGame("ModNeedyComponent");
                MethodInfo mi = mbct.Method("GetModComponentType");
                for(int i = ls.Count - 1; i >= 0; i--)
                    if(mbct.IsAssignableFrom(ls[i].GetType()) && ((string)mi.Invoke(ls[i], new object[0])).EqualsAny("whiteModule", "blackModule"/*, "ttProtogen"*/))
                        ls.RemoveAt(i);
                Debug.Log("[Black and White] ComponentPrefabs okay");
            }
            IDictionary d = t.Field<IDictionary>("componentPrefabDictionary", instance);
            if(d != null)
            {
                d.Remove("whiteModule");
                d.Remove("blackModule");
                //if(d.Contains("ttProtogen"))
                //    d.Remove("ttProtogen");
                Debug.Log("[Black and White] ComponentPrefabDictionary okay");
            }
        }
        else
            Debug.Log("[Black and White] No BombGenerator instances found");


        t = ReflectionHelper.FindType("ModSelectorService");
        if(t != null)
        {
            Debug.Log("[Black and White] Mod selector located, updating...");
            object minst = t.GetField("_instance", ReflectionHelper.Flags).GetValue(null);
            Type nt = t.GetNestedType("NeedyModule", ReflectionHelper.Flags);
            object n = Activator.CreateInstance(nt, _blackPrefab.GetComponent<KMNeedyModule>(), _blackPrefab);

            IDictionary nd = t.Field<IDictionary>("_allNeedyModules", minst);
            nd.Add("blackModule", n);

            //t.MethodCall("ClearModInfo", minst, new object[0]);
            //t.MethodCall("SetupModInfo", minst, new object[0]);
        }

        t = ReflectionHelper.FindType("DynamicMissionGeneratorAssembly.DynamicMissionGenerator");
        if(t != null)
        {
            object minst = t.Field<object>("Instance", null);
            if(minst != null)
            {
                object p = t.Field<object>("InputPage", minst);
                if(p != null)
                {
                    Debug.Log("[Black and White] Dynamic mission generator located, updating...");
                    p.GetType().MethodCall("InitModules", p, new object[0]);
                }
            }
        }
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

    private static IEnumerable<CodeInstruction> DBMLNoFakeTranspiler(IEnumerable<CodeInstruction> instr)
    {
        List<CodeInstruction> instructions = instr.ToList();

        int i = 0;

        Type t = ReflectionHelper.FindType("Repository");
        FieldInfo f = t.GetField("Loaded", ReflectionHelper.Flags);

        for(; i < instructions.Count; i++)
        {
            //We only insert a call to our method just before it finishes loading.
            //The rest of the method is unchanged.
            if(instructions[i].StoresField(f))
                yield return CodeInstruction.Call(typeof(BWService), "ClearRepository");

            yield return instructions[i];
        }
    }

    private static IEnumerable<CodeInstruction> DBMLGenTranspiler(IEnumerable<CodeInstruction> instr, MethodBase orig)
    {
        List<CodeInstruction> instructions = instr.ToList();

        int i = 0;
        bool flag = false;

        for(; i < instructions.Count; i++)
        {
            //We only insert a call to our method to ensure the correct BombFace is chosen.
            //The rest of the method is unchanged.

            //Null is only stored once, but we only do the first instance to be safe.
            if(!flag && instructions[i].opcode == OpCodes.Ldnull && instructions[i + 1].IsStloc())
            {
                yield return instructions[i + 2]; //Loads the BombComponent so we can check if it is White
                yield return new CodeInstruction(OpCodes.Ldloc_S, 6); //timerFace
                yield return new CodeInstruction(OpCodes.Ldloc_3, 6); //bombFaceList
                yield return new CodeInstruction(OpCodes.Ldloc_S, 5); //bombInfo (for rand)
                yield return CodeInstruction.Call(typeof(BWService), "ChooseFaceDBML");
                flag = true;
                i++; //Don't load null again, as we've already returned a value.
            }

            yield return instructions[i];
        }
    }

    private static object ChooseFaceDBML(MonoBehaviour comp, object frontFace, IList bombFaceList, object bombInfo)
    {
        //We only want to affect geberation for our own module.
        KMNeedyModule n = comp.GetComponent<KMNeedyModule>();
        if(!(n && n.ModuleType == "whiteModule"))
            return null;

        //We simulate Tweaks' calculations for which face to spawn modules on
        List<object> l = bombFaceList.Cast<object>().ToList();

        if(l.Count == 0)
            Debug.LogFormat("[Black and White] There's no room to spawn White.");
        else
        {
            if(l.Contains(frontFace))
                l.Remove(frontFace);
            if(l.Count == 0)
            {
                Debug.LogFormat("[Black and White] There's no room on the back face. Using the front instead.");
                return frontFace;
            }
            else
            {
                return l[bombInfo.GetType().Field<System.Random>("Rand", bombInfo).Next(0, l.Count)];
            }
        }

        return null;
    }

    //private static IEnumerable<CodeInstruction> CheckAndLoadTranspiler(IEnumerable<CodeInstruction> allinstr)
    //{
    //    List<CodeInstruction> instructions = allinstr.ToList();

    //    int i = 0;
    //    int m3 = 1;

    //    Type t = ReflectionHelper.FindType("ModManager");
    //    FieldInfo f = t.GetField("loadedBombComponents", ReflectionHelper.Flags);
    //    int lf = 1;

    //    for(; i < instructions.Count; i++)
    //    {
    //        yield return instructions[i];
    //        if(instructions[i].LoadsConstant(-3))
    //        {
    //            yield return new CodeInstruction(OpCodes.Ldc_I4_S, m3++);
    //            yield return CodeInstruction.Call(typeof(BWService), "LogMinus");
    //        }
    //        if(instructions[i].LoadsField(f))
    //        {
    //            yield return new CodeInstruction(OpCodes.Ldc_I4_S, lf++);
    //            yield return CodeInstruction.Call(typeof(BWService), "LogLoad");
    //        }
    //    }

    //    yield break;

    //    //Type t = ReflectionHelper.FindType("ModManager");
    //    //FieldInfo f = t.GetField("loadedBombComponents", ReflectionHelper.Flags);

    //    for(; i < instructions.Count; i++)
    //    {
    //        //We only insert a call to our method just before this is used.
    //        //The rest of the method is unchanged.
    //        if(instructions[i].LoadsField(f))
    //        {
    //            yield return instructions[i];
    //            //We re-use this method because it does exactly what we need.
    //            yield return CodeInstruction.Call(typeof(BWService), "GetSolvablesPrefix");

    //            //Our method consumes the value, so we make another copy.
    //            yield return new CodeInstruction(OpCodes.Ldarg_0); //this
    //            yield return instructions[i - 1]; //.$this
    //            yield return instructions[i]; //.loadedBombComponents

    //            i++;
    //            break;
    //        }

    //        yield return instructions[i];
    //    }
    //    for(; i < instructions.Count; i++)
    //        yield return instructions[i];
    //}

    //private static void LogMinus(int i)
    //{
    //    Debug.Log("KWMinusThree " + i);
    //}

    //private static void LogLoad(int i)
    //{
    //    Debug.Log("KWLoad " + i);
    //}

    private static void ClearRepository()
    {
        Type t = ReflectionHelper.FindType("Repository");
        IList m = t.Field<IList>("Modules", null);
        t = t.GetNestedType("KtaneModule", ReflectionHelper.Flags);
        FieldInfo fi = t.GetField("ModuleID", ReflectionHelper.Flags);
        List<string> removed = new List<string>();
        for(int i = m.Count - 1; i >= 0; i--)
        {
            string id = (string)fi.GetValue(m[i]);
            if(id.EqualsAny("whiteModule", "blackModule"/*, "ttProtogen"*/))
            {
                m.RemoveAt(i);
                removed.Add(id);
            }
        }

        if(removed.Count > 0)
            Debug.LogFormat("[Black and White] IDs removed from Tweaks: {0}", removed.Join(", "));
        else
            Debug.LogFormat("[Black and White] No IDs removed from Tweaks.");
    }

    private static IEnumerable<CodeInstruction> CreateBombTranspiler(IEnumerable<CodeInstruction> instr)
    {
        List<CodeInstruction> instructions = instr.ToList();

        int i = 0;

        for(; i < instructions.Count; i++)
        {
            //Keep the original method until just before this log message
            if(!instructions[i].Is(OpCodes.Ldstr, "Instantiating RequiresTimerVisibility components on {0}"))
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
                "LoadOnCondition",
                parameters: new Type[] { typeof(object), typeof(object), typeof(object) },
                generics: new Type[0]
            );
            break;
        }
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
            //Notably, if DBML is enabled, this simply adds them to its pool of modules, and we will have to do this again.
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

    private static void LoadOnCondition(object instance, object settings, object frontFace)
    {
        Type t = instance.GetType();

        Debug.LogFormat("[Black and White] Spawning per condition...");

        foreach(AddOnConditionData d in _propertyAdded)
        {
            int count = d.Condition(settings);
            for(int i = 0; i < count; ++i)
            {
                //We simulate the Game's calculations for which face to spawn modules on
                //Notably, if DBML is enabled, this simply adds them to its pool of modules, and we will have to do this again.
                IList vbf = t.Field<IList>("validBombFaces", instance);
                List<object> l = vbf.Cast<object>().ToList();

                if(l.Count == 0)
                    Debug.LogFormat("[Black and White] There's no room to spawn {0}.", d.Id);
                else
                {
                    t.MethodCall("InstantiateComponent", instance, new object[] {
                        l[t.Field<System.Random>("rand", instance).Next(0, l.Count)],
                        d.Prefab,
                        settings }
                    );
                }
            }
        }
    }

    private static void SelectRandomPostfix(string __result)
    {
        if(__result != "blackModule")
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

    private static void GetSolvablesPrefix(IDictionary ___loadedBombComponents)
    {
        //Type mnc = ReflectionHelper.FindTypeInGame("ModNeedyComponent");
        //FieldInfo fi = mnc.GetField("module", ReflectionHelper.Flags);
        List<string> removed = new List<string>();
        IEnumerable<string> keys = ___loadedBombComponents.Keys.Cast<string>();
        foreach(string key in keys)
        {
            //Debug.LogFormat("[Black and White] Id: {0}", key);
            //Debug.LogFormat("Component: {1}", key, ___loadedBombComponents[key]);

            //if(key == "blackModule")
            //{
            //    Debug.Log(___loadedBombComponents[key] == null);
            //    Debug.Log(fi.GetValue(___loadedBombComponents[key]));
            //    Debug.Log(fi.GetValue(___loadedBombComponents[key]) == null);
            //    Debug.Log(fi.GetValue(___loadedBombComponents[key]) is KMNeedyModule);
            //    Debug.Log((fi.GetValue(___loadedBombComponents[key]) as KMNeedyModule).ModuleDisplayName);
            //    continue;
            //}

            if(___loadedBombComponents[key] == null)
            {
                ___loadedBombComponents.Remove(key);
                removed.Add(key);
            }
            //else if(mnc.Equals(___loadedBombComponents[key].GetType()) && fi.GetValue(___loadedBombComponents[key]) == null)
            //{
            //    ___loadedBombComponents.Remove(key);
            //    removed.Add(key);
            //}
        }

        if(removed.Count > 0)
            Debug.LogFormat("[Black and White] IDs removed from Mod Manager: {0}", removed.Join(", "));
        else
            Debug.LogFormat("[Black and White] No IDs removed from Mod Manager.");
    }

    private sealed class AddOnConditionData
    {
        public object Prefab;
        public Func<object, int> Condition = _ => 0;
        public string Id = null;
    }
}
