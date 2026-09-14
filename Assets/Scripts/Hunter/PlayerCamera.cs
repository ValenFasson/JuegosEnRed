using Photon.Pun;
using UnityEngine;

public class PlayerCamera : MonoBehaviourPun
{
    [SerializeField] private Camera _playerCamera;
    [SerializeField] private AudioListener _audioListener;

    private void Start()
    {
        if (photonView.IsMine)
        {
            if (_playerCamera != null) _playerCamera.enabled = true;
            if (_audioListener != null) _audioListener.enabled = true;
        }
        else
        {
            if (_playerCamera != null) _playerCamera.enabled = false;
            if (_audioListener != null) _audioListener.enabled = false;
        }
    }
}