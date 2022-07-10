using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(KMNeedyModule), typeof(KMAudio))]
public abstract class BlackWhiteScript : MonoBehaviour
{
    [SerializeField]
    private TextMesh _identifier;

    protected virtual string Name { get { return "Default"; } }
    public object NeedyComponent { get; private set; }

    private int _id;
    private static readonly Dictionary<string, int> _idc = new Dictionary<string, int>();
    public BlackWhiteScript Partner;

    public KMNeedyModule Module;
    protected KMAudio Audio;
    protected bool Errored;

    protected static int Identifier;

    private void Awake()
    {
        if(!_idc.ContainsKey(Name))
            _idc[Name] = 0;
        _id = ++_idc[Name];
    }

    protected void Start()
    {
        Audio = GetComponent<KMAudio>();
        Module = GetComponent<KMNeedyModule>();
        Module.OnActivate += Activate;
        Module.OnNeedyActivation += NeedyStart;
        Module.OnTimerExpired += NeedyEnd;
        Module.OnNeedyDeactivation += BombOver;
        NeedyComponent = gameObject.GetComponent(ReflectionHelper.FindTypeInGame("ModNeedyComponent"));
    }

    protected abstract void NeedyStart();
    protected abstract void NeedyEnd();
    protected abstract void BombOver();
    protected abstract void Activate();

    protected void Log(string message, params object[] args)
    {
        Debug.LogFormat("[" + Name + " #" + _id + "] " + message, args);
    }

    public void SetIdentifier(int id)
    {
        _identifier.text = id.ToString();
    }

    protected void Error()
    {
        _identifier.text = "!!!";
        _identifier.color = Color.red;
        Errored = true;
    }
}