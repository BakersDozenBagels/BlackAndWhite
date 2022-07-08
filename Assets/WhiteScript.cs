using System.Collections;
using System.Linq;

public class WhiteScript : BlackWhiteScript
{
    public LightScript[] Lights;

    protected override string Name { get { return "White"; } }

    protected override void Activate()
    {
        if(Partner != null)
            return;
        BlackScript[] allw = transform.root.GetComponentsInChildren<BlackScript>();
        BlackScript w = allw.FirstOrDefault(ws => ws.Partner == null);
        if(w == null)
        {
            Error();
            throw new System.Exception("Not enough Blacks spawned!");
        }
        Partner = w;
        w.Partner = this;

        if(allw.Length > 1)
        {
            SetIdentifier(++Identifier);
            Partner.SetIdentifier(Identifier);
        }
    }

    protected override void BombOver()
    {
    }

    protected override void NeedyEnd()
    {
        //Allow Black to handle this
        Module.HandlePass();
    }

    protected override void NeedyStart()
    {
        //Allow Black to handle this
        if(Partner == null || !((BlackScript)Partner).IsActive)
            Module.HandlePass();
    }

    private readonly string TwitchHelpMessage = @"This module cannot be interacted with.";

    private IEnumerator ProcessTwitchCommand(string command)
    {
        yield return "sendtochaterror What are you trying to do here?";
    }

    private IEnumerator TwitchHandleForcedSolve()
    {
        yield break;
    }
}
