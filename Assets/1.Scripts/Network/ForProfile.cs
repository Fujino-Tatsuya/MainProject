using Unity.Netcode;
using UnityEngine;

public class ForProfile : MonoBehaviour
{

    void OnGUI()
    {
        if (NetworkManager.Singleton == null) return;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) return;

        if (GUI.Button(new Rect(20, 20, 200, 60), "Start Host"))
        {
            NetworkManager.Singleton.StartHost();
        }
    }
}
