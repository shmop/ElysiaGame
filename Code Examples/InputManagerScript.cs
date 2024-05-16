using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManagerScript : MonoBehaviour
{
    [SerializeField]
    State state = null;
    PlayerInputManager playerInputManager;

    [SerializeField]
    CustomizeMenuNew customizeMenuNew;

    [SerializeField]
    PlayerInput mouse;

    bool mainPlayerJoined = false;

    private void Awake()
    {
        playerInputManager = GetComponent<PlayerInputManager>();
        playerInputManager.DisableJoining();
    }
    void OnPlayerJoined(PlayerInput newPlayer)
    {
        if (newPlayer.gameObject.tag == "Player")
        {
            //! index = 0 used by mouse and keys
            int additionalCount = 1;
            PlayerScript player = newPlayer.gameObject.GetComponent<PlayerScript>();
            if (customizeMenuNew.HandleNewPlayer(player))
            {
                /*
                if (state.isObserverMode)
                {
                    additionalCount = 1;
                }*/
                //newPlayer.gameObject.transform.position = State.gameManager.spawnPositions[newPlayer.playerIndex].position;

                player.gameObject.name = $"Player {newPlayer.playerIndex + 1 - additionalCount}";

                //todo: player index set after this in assign customnize  menu positions, so probs not needed here
                player.playerIndex = newPlayer.playerIndex - additionalCount + State.gameManager.botCount;

                customizeMenuNew.AssignCusomizeMenuPosition(player);
                print("inputmanager calling setup player");
                player.SetupPlayer();
                state.AddPlayer(player);

                State.humanPlayers.Add(newPlayer.gameObject);

                state.UpdatePlayerCount();

                customizeMenuNew.InitializeScreen();

                print("in OnPlayerJoined");

                //newPlayer.actions.Disable();
            }
        }

    }
    public PlayerInput JoinMainPlayer(Gamepad mainGamepad)
    {
        print(mainGamepad);
        //! - Join player as index = 1 as mouse and keys are taking index = 0 -
        PlayerInput mainPlayerInput = playerInputManager.JoinPlayer(1, -1, null, mainGamepad);
        playerInputManager.EnableJoining();
        return mainPlayerInput;
    }
}
