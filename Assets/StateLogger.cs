using UnityEngine;

public class StateLogger : MonoBehaviour
{
    private void OnDestroy()
    {
        Debug.LogFormat("[State Logger] Object \"{0}\" has been destroyed.", gameObject.name);
    }
}
