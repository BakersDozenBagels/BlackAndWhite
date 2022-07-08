using UnityEngine;

public class LightScript : MonoBehaviour
{
    [SerializeField]
    private Light _light;
    [SerializeField]
    private Renderer _bulb;
    [SerializeField]
    private Transform _filament;
    [SerializeField]
    private Texture _onTex, _offTex;

    private void Awake()
    {
        _bulb.sharedMaterial = new Material(_bulb.sharedMaterial);

        transform.localEulerAngles = new Vector3(0f, Random.Range(0f, 360f), 0f);
        _filament.transform.localEulerAngles = new Vector3(0f, Random.Range(0f, 360f), 0f);
        Off();
    }

    public void On()
    {
        _light.enabled = true;
        _bulb.material.mainTexture = _onTex;
    }

    public void Off()
    {
        _light.enabled = false;
        _bulb.material.mainTexture = _offTex;
    }
}
