using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using static UnityEngine.InputSystem.PlayerInputManager;

namespace MalbersAnimations.InputSystem
{
    [AddComponentMenu("Malbers/Input/MInput Player Manager")]
    [RequireComponent(typeof(PlayerInputManager))]
    public class MInputPlayerManager : MonoBehaviour
    {
        public PlayerInputManager Manager;

        [SerializeField] private List<OutputChannels> playerOutputChannels;

        public List<PlayerInput> players;

        public List<Transform> SpawnPoints = new();

        private int NextPoint;

        public PlayerJoinedEvent OnPlayerJoined = new();
        public PlayerJoinedEvent OnPlayerLeft = new();

        private void OnEnable()
        {
            if (Manager == null)
                Manager = FindAnyObjectByType<PlayerInputManager>();

            if (Manager != null)
            {
                Manager.onPlayerJoined += PlayerJoined;
                Manager.onPlayerLeft += PlayerLeft;
            }
        }


        private void OnDisable()
        {
            if (Manager != null)
            {
                Manager.onPlayerJoined -= PlayerJoined;
                Manager.onPlayerLeft -= PlayerLeft;
            }
        }


        /// <summary> Check when the Player has Joined </summary>
        public void PlayerJoined(PlayerInput player)
        {
            Debug.Log($"Player Joined {player.name}", this);
            players.Add(player);
            var Player = player.transform;
            //Position the Player in a spawn point
            Player.position = SpawnPoints[NextPoint].position;
            CameraLayerSettings(player);
            NextPoint = (NextPoint + 1) % SpawnPoints.Count;
            OnPlayerJoined.Invoke(player);
        }

        private void CameraLayerSettings(PlayerInput player)
        {
            player.name += $"[{player.playerIndex}]";

            //It can have multiple Virtual Cameras
            var VirtualCams = player.transform.root.GetComponentsInChildren<CinemachineVirtualCameraBase>();

            foreach (var v in VirtualCams)
            {
                v.OutputChannel = playerOutputChannels[player.playerIndex];
            }

            var Camera = player.GetComponentInChildren<Camera>();

            Camera.name += $"[{player.playerIndex}]";

            var CMBrain = player.GetComponentInChildren<CinemachineBrain>();
            CMBrain.ChannelMask = playerOutputChannels[player.playerIndex];
        }


        //Check when the player has left
        public void PlayerLeft(PlayerInput input)
        {
            OnPlayerLeft.Invoke(input);
        }

        private void Reset()
        {
            playerOutputChannels = new List<OutputChannels>
            {
                OutputChannels.Channel01,
                OutputChannels.Channel02,
                OutputChannels.Channel03,
                OutputChannels.Channel04,
            };
        }
    }
}
