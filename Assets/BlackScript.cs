using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RNG = UnityEngine.Random;

public class BlackScript : BlackWhiteScript
{
    [SerializeField]
    private KMSelectable[] _buttons;

    protected override string Name { get { return "Black"; } }

    public bool IsActive;

    private int[][] _quadrants = new int[][]
    {
        new int[] { 0, 1, 3, 4 },
        new int[] { 1, 2, 4, 5 },
        new int[] { 3, 4, 6, 7 },
        new int[] { 4, 5, 7, 8 }
    };
    private int[] _requiredPresses = new int[4];

    private new void Start()
    {
        base.Start();
        for(int i = 0; i < _buttons.Length; i++)
        {
            int j = i;
            _buttons[j].OnInteract += () => { Press(j); return false; };
        }
    }

    private void Press(int j)
    {
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, _buttons[j].transform);
        _buttons[j].AddInteractionPunch(0.1f);

        if(!IsActive)
            return;

        _requiredPresses[j]--;
        if(_requiredPresses[j] < 0)
        {
            Log("You pressed button {0} one too many times. Strike!", j + 1);
            Module.HandleStrike();
            IsActive = false;
            LightsOff();
            Module.HandlePass();
            Partner.Module.HandlePass();
            return;
        }
        if(_requiredPresses.All(i => i == 0))
        {
            Log("All buttons pressed correctly. Disarmed, for now...");
            IsActive = false;
            LightsOff();
            Module.HandlePass();
            Partner.Module.HandlePass();
        }
    }

    protected override void NeedyEnd()
    {
        Log("Time ran out. You needed to press the buttons this many more times: {0}", _requiredPresses.Join(" "));
        Module.HandleStrike();
        IsActive = false;
        LightsOff();
        Module.HandlePass();
        Partner.Module.HandlePass();
    }

    protected override void NeedyStart()
    {
        if(Partner == null)
        {
            Module.HandlePass();
            return;
        }

        IsActive = true;

        WhiteScript w = (WhiteScript)Partner;
        List<bool> lights = Enumerable.Repeat(0, 8).Select(i => RNG.Range(0, 2) == 1).ToList();
        lights.Insert(RNG.Range(0, 8), true);
        for(int i = 0; i < 9; i++)
        {
            if(lights[i])
                w.Lights[i].On();
            else
                w.Lights[i].Off();
        }

        for(int i = 0; i < _quadrants.Length; i++)
        {
            _requiredPresses[i] = 0;
            foreach(int j in _quadrants[i])
                if(lights[j])
                    _requiredPresses[i]++;
        }

        BWService.ActivateNeedy(Partner.NeedyComponent);

        Log("Module activated! Linked white displayed {0}. Correct press counts are {1}.", lights.Select(b => b ? "o" : "-").Join(""), _requiredPresses.Join(" "));

        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.NeedyActivated, transform);
    }

    private void Update()
    {
        if(!IsActive)
            return;
        Partner.Module.SetNeedyTimeRemaining(Module.GetNeedyTimeRemaining());
    }

    private void LightsOff()
    {
        WhiteScript w = (WhiteScript)Partner;
        foreach(LightScript l in w.Lights)
            l.Off();
    }

    protected override void Activate()
    {
        if(Partner != null)
            return;
        WhiteScript[] allw = transform.root.GetComponentsInChildren<WhiteScript>();
        WhiteScript w = allw.FirstOrDefault(ws => ws.Partner == null);
        if(w == null)
        {
            Error();
            throw new Exception("Not enough Whites spawned!");
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
        IsActive = false;
        LightsOff();
    }

    private readonly string TwitchHelpMessage = @"Use ""!{0} 1 123 34"" to press those buttons. Buttons are numbered in reading order.";

    private static readonly string validChars = "1234";
    private IEnumerator ProcessTwitchCommand(string command)
    {
        command = command.Trim();
        if(command.All(c => validChars.Contains(c) || char.IsWhiteSpace(c)) && command.Any(c => validChars.Contains(c)))
        {
            yield return null;
            foreach(char c in command)
            {
                switch(c)
                {
                    case '1':
                    case '2':
                    case '3':
                    case '4':
                        _buttons[int.Parse(c.ToString()) - 1].OnInteract();
                        yield return new WaitForSeconds(0.1f);
                        break;
                }
            }
        }
    }

    private void TwitchHandleForcedSolve()
    {
        StartCoroutine(AutoSolve());
    }

    private IEnumerator AutoSolve()
    {
        IEnumerable<int> buttons = Enumerable.Range(0, 4);
        while(true)
        {
            while(IsActive)
            {
                _buttons[buttons.First(i => _requiredPresses[i] != 0)].OnInteract();
                yield return new WaitForSeconds(0.1f);
            }
            yield return null;
        }
    }
}
