using UnityEngine;
using Photon.Pun;

public class LobbyInstantiator : MonoBehaviour
{
    void Start()
    {
        // Solo el Master Client crea el controlador del lobby para todos.
        // Photon se encarga de sincronizarlo.
        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.Instantiate("LobbyController", Vector3.zero, Quaternion.identity);
        }
    }
}
