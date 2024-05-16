using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BotManager : MonoBehaviour
{
    const float NAMETAG_DELAY = 0.3f;
    const int HOLDING_LAYER_INDEX = 1;
    const int WALL_LAYER_INDEX = 2;
    const int WALL_HELD_LAYER_INDEX = 3;
    const int DIVE_LAYER_INDEX = 5;
    const int CATCH_DIVE_LAYER_INDEX = 6;
    const int HOLDING_DIVE_LAYER_INDEX = 7;
    const int NO_POSESSION = 0;
    const int TEAM_1_POSSESSION = 1;
    const int TEAM_2_POSSESSION = 2;

    [SerializeField]
    private State state = null;
    private List<PlayerScript> botsTeamA = new List<PlayerScript>();
    private List<PlayerScript> botsTeamB = new List<PlayerScript>();

    [SerializeField]
    public List<Transform> goalieSpawns;

    public int possessionState = 0;
    public int currentPossessionState = 0;

    private GameObject gameball;

    void Awake()
    {
        gameball = State.gameManager.gameball;
    }
    void Update()
    {
        AssignBotJob();
    }
    //! - called by player -
    public void SwitchWithBotGoalie(PlayerScript ps)
    {
        //! - return if team mate is already goalie -
        foreach (GameObject player in State.humanPlayers)
        {
            //! - not this player, same team, team mate is goalie already -
            if (player != ps.gameObject &&
                player.GetComponent<PlayerScript>().team == ps.team
                && player.GetComponent<PlayerScript>().isGoalie)
            {
                return;
            }
        }

        if (ps.team == 0)
        {
            for (int i = 0; i < botsTeamA.Count; i++)
            {
                if (botsTeamA[i].isGoalieBot)
                {
                    SwitchingCode(botsTeamA[i], ps, goalieSpawns[0]);
                    ps.UpdateGoalieSwitchUI();
                }
            }
        }
        else
        {
            for (int i = 0; i < botsTeamB.Count; i++)
            {
                if (botsTeamB[i].isGoalieBot)
                {
                    SwitchingCode(botsTeamB[i], ps, goalieSpawns[1]);
                    ps.UpdateGoalieSwitchUI();
                }
            }
        }
    }
    void SwitchingCode(PlayerScript bot, PlayerScript player, Transform goalieSpawn)
    {
        //! - save bot details -
        Transform oldBotTransform = bot.transform;
        Vector3 oldBotPosition = bot.transform.position;
        Vector3 oldBotRotation = bot.transform.rotation.eulerAngles;
        //Vector3 oldBotVelocity = bot.rbPlayer.velocity;

        //! - update bot details -
        bot.transform.position = player.transform.position;
        bot.transform.eulerAngles = player.transform.eulerAngles;
        //if (bot.rbPlayer) bot.rbPlayer.velocity = player.rbPlayer.velocity;

        //! - update player details -
        player.transform.position = oldBotPosition;
        player.sceneVCamScript.timeSwitched = Time.time;
        //if (bot.rbPlayer) player.rbPlayer.velocity = oldBotVelocity;

        //! - Turn off nametag for a short time to not show on playercam -
        StartCoroutine(NameTagDelay(bot, player));

        if (player.isGoalie)
        {
            player.gameObject.layer = LayerMask.NameToLayer("Player");
            bot.gameObject.layer = LayerMask.NameToLayer("Goalie");
            bot.isGoalie = true;
            bot.particleSystems.SetActive(false);
            if (player.isOnGround) player.particleSystems.SetActive(true);
            player.isGoalie = false;
            //player.transform.eulerAngles = oldBotRotation;
            player.sceneVCamScript.UpdateVCamRotation(oldBotRotation.x, oldBotRotation.y);
        }
        else
        {
            player.gameObject.layer = LayerMask.NameToLayer("Goalie");
            bot.gameObject.layer = LayerMask.NameToLayer("Player");
            bot.isGoalie = false;
            player.particleSystems.SetActive(false);
            player.isGoalie = true;
            //player.transform.eulerAngles = goalieSpawn.rotation.eulerAngles;
            player.sceneVCamScript.UpdateVCamRotation(goalieSpawn.rotation.eulerAngles.x, goalieSpawn.eulerAngles.y);
        }

        SwitchJumpStates(bot, player);
        SwitchKnockedStates(bot, player);
        SwitchRecoveryStates(bot, player);
        SwitchLerpStates(bot, player);
        SwitchRigidbodyStates(bot, player);
        SwitchDodgeballStates(bot, player);
        SwitchHeldEntityStates(bot, player);
        SwitchWallStates(bot, player);
        SwitchDiveStates(bot, player);
    }
    void SwitchJumpStates(PlayerScript bot, PlayerScript player)
    {
        bool boolHolder = false;

        //! - Jumping States -
        boolHolder = bot.isJumping;
        bot.isJumping = player.isJumping;
        player.isJumping = boolHolder;

        boolHolder = bot.canDoubleJump;
        bot.canDoubleJump = player.canDoubleJump;
        player.canDoubleJump = boolHolder;

        boolHolder = bot.hasDoveThisJump;
        bot.hasDoveThisJump = player.hasDoveThisJump;
        player.hasDoveThisJump = boolHolder;

        boolHolder = bot.canDive;
        bot.canDive = player.canDive;
        player.canDive = boolHolder;
    }
    void SwitchKnockedStates(PlayerScript bot, PlayerScript player)
    {
        //! - Knocked States -
        if (player.startNoControlTime != 0f || bot.startNoControlTime != 0f)
        {
            bool playerHasHitGround = player.hasHitGround;
            bool botHasHitGround = bot.hasHitGround;

            bool playerHasBeenDamaged = player.hasBeenDamaged;
            bool botHasBeenDamaged = bot.hasBeenDamaged;

            float playerHasHitGroundTime = player.hasHitGroundTime;
            float botHasHitGroundTime = bot.hasHitGroundTime;

            float playerStartNoControlTime = player.startNoControlTime;
            float botStartNoControlTime = bot.startNoControlTime;

            //! - Both knocked -
            if (player.startNoControlTime != 0f && bot.startNoControlTime != 0f)
            {
                //! Do nothing
            }
            //! - Only Player knocked -
            else if (player.startNoControlTime != 0f)
            {
                bot.SetHasNoControl(true, false);
                player.SetHasNoControl(false, false);
            }
            //! - Only bot knocked -
            else
            {
                player.SetHasNoControl(true, false);
                bot.SetHasNoControl(false, false);
            }
            bot.hasHitGround = playerHasHitGround;
            player.hasHitGround = botHasHitGround;

            bot.hasBeenDamaged = playerHasBeenDamaged;
            player.hasBeenDamaged = botHasBeenDamaged;

            bot.hasHitGroundTime = playerHasHitGroundTime;
            player.hasHitGroundTime = botHasHitGroundTime;

            bot.startNoControlTime = playerStartNoControlTime;
            player.startNoControlTime = botStartNoControlTime;
        }
    }
    void SwitchRecoveryStates(PlayerScript bot, PlayerScript player)
    {
        //! - Recovery States -
        if (player.playerRecovering || bot.playerRecovering)
        {
            bool playerRecovering = player.playerRecovering;
            bool botRecovering = bot.playerRecovering;

            float playerRecoverTime = player.recoverTime;
            float botRecoverTime = bot.recoverTime;

            player.shieldGlow.SetActive(bot.playerRecovering);
            bot.shieldGlow.SetActive(player.playerRecovering);
            player.playerRecovering = botRecovering;
            bot.playerRecovering = playerRecovering;
            player.recoverTime = botRecoverTime;
            bot.recoverTime = playerRecoverTime;
        }
    }
    void SwitchLerpStates(PlayerScript bot, PlayerScript player)
    {
        float floatHolder = 0f;

        //! - Lerp States -
        floatHolder = bot.timeStandingUp;
        bot.timeStandingUp = player.timeStandingUp;
        player.timeStandingUp = floatHolder;
    }
    void SwitchRigidbodyStates(PlayerScript bot, PlayerScript player)
    {
        //! - Player Rigidbody States -
        if (player.rbPlayer || bot.rbPlayer)
        {
            bool playerGravity;
            bool botGravity;

            bool playerFreeze;
            bool botFreeze;

            Vector3 playerVelocity;
            Vector3 botVelocity;

            //todo does the code need to setup entities without rigids, as they will be setup on release anyway?
            //! - Both have rigidbidy -
            if (player.rbPlayer && bot.rbPlayer)
            {
                playerGravity = player.rbPlayer.useGravity;
                botGravity = bot.rbPlayer.useGravity;

                playerFreeze = player.rbPlayer.freezeRotation;
                botFreeze = bot.rbPlayer.freezeRotation;

                playerVelocity = player.rbPlayer.velocity;
                botVelocity = bot.rbPlayer.velocity;

                player.rbPlayer.velocity = botVelocity;
                bot.rbPlayer.velocity = playerVelocity;

                player.rbPlayer.useGravity = botGravity;
                bot.rbPlayer.useGravity = playerGravity;

                player.rbPlayer.freezeRotation = botFreeze;
                bot.rbPlayer.freezeRotation = playerFreeze;
            }
            //! - Bot is being held -
            else if (player.rbPlayer)
            {
                playerGravity = player.rbPlayer.useGravity;
                playerFreeze = player.rbPlayer.freezeRotation;
                playerVelocity = player.rbPlayer.velocity;

                PlayerScript previousParent = bot.transform.parent.GetComponent<PlayerScript>();
                float heldTimer = previousParent.timeStartedHoldingPlayer;
                previousParent.ReleaseOrPickupHeldEntity(true, previousParent.heldEntity);
                bot.isAboutToBeSwapped = false;
                bot.startNoControlTime = Time.time - 2f;
                bot.recoverTime = Time.time - 2f;
                bot.timeStandingUp = Time.time - 0.3f;
                bot.rbPlayer.velocity = playerVelocity;
                bot.rbPlayer.useGravity = playerGravity;
                bot.rbPlayer.freezeRotation = playerFreeze;

                previousParent.ReleaseOrPickupHeldEntity(false, player.gameObject);
                previousParent.timeStartedHoldingPlayer = heldTimer;
            }
            //! - Player is being held -
            else
            {
                botGravity = bot.rbPlayer.useGravity;
                botFreeze = bot.rbPlayer.freezeRotation;
                botVelocity = bot.rbPlayer.velocity;

                PlayerScript previousParent = player.transform.parent.GetComponent<PlayerScript>();
                float heldTimer = previousParent.timeStartedHoldingPlayer;
                previousParent.ReleaseOrPickupHeldEntity(true, previousParent.heldEntity);
                player.isAboutToBeSwapped = true;
                player.startNoControlTime = Time.time - 2f;
                player.recoverTime = Time.time - 2f;
                player.timeStandingUp = Time.time - 0.3f;
                player.rbPlayer.velocity = botVelocity;
                player.rbPlayer.useGravity = botGravity;
                player.rbPlayer.freezeRotation = botFreeze;

                previousParent.ReleaseOrPickupHeldEntity(false, bot.gameObject);
                previousParent.timeStartedHoldingPlayer = heldTimer;
            }
        }
        else
        {
            //! - Both player and bot are being held -
            PlayerScript playerParent = bot.transform.parent.GetComponent<PlayerScript>();
            PlayerScript botParent = bot.transform.parent.GetComponent<PlayerScript>();
            float playerHeldTimer = botParent.timeStartedHoldingPlayer;
            float botHeldTimer = botParent.timeStartedHoldingPlayer;

            playerParent.timeStartedHoldingPlayer = botHeldTimer;
            botParent.timeStartedHoldingPlayer = playerHeldTimer;

        }
    }
    void SwitchDodgeballStates(PlayerScript bot, PlayerScript player)
    {
        //! - Dodgeball States -
        GameObject playerDodgeball = player.myDodgeball;
        GameObject botDodgeball = bot.myDodgeball;

        PlayerScript dodgeballOwningPlayer = player.myDodgeball.GetComponent<Dodgeball>().owningPlayer;
        PlayerScript dodgeballOwningBot = bot.myDodgeball.GetComponent<Dodgeball>().owningPlayer;

        bool playerDodgeballSpawned = player.isDodgeballSpawned;
        bool botDodgeballSpawned = bot.isDodgeballSpawned;

        player.myDodgeball.GetComponent<Dodgeball>().owningPlayer = dodgeballOwningBot;
        bot.myDodgeball.GetComponent<Dodgeball>().owningPlayer = dodgeballOwningPlayer;
        player.myDodgeball = botDodgeball;
        bot.myDodgeball = playerDodgeball;
        State.gameManager.dodgeballs[player.playerIndex] = botDodgeball;
        State.gameManager.dodgeballs[bot.playerIndex] = playerDodgeball;
        player.isDodgeballSpawned = botDodgeballSpawned;
        bot.isDodgeballSpawned = playerDodgeballSpawned;

    }
    void SwitchHeldEntityStates(PlayerScript bot, PlayerScript player)
    {
        //! - Held Object states -
        if (player.heldEntity || bot.heldEntity)
        {
            GameObject playerHeldEntity = player.heldEntity;
            GameObject botHeldEntity = bot.heldEntity;
            //! - Both have heldEntity -
            if (player.heldEntity && bot.heldEntity)
            {
                //! - release first -
                bot.ReleaseOrPickupHeldEntity(isRelease: true, botHeldEntity);
                player.ReleaseOrPickupHeldEntity(isRelease: true, playerHeldEntity);

                //! - then pickup -
                bot.isAboutToSwapHeldEntity = true;
                player.isAboutToSwapHeldEntity = true;
                bot.ReleaseOrPickupHeldEntity(isRelease: false, playerHeldEntity);
                player.ReleaseOrPickupHeldEntity(isRelease: false, botHeldEntity);
            }
            //! - Only player holding something -
            else if (player.heldEntity)
            {
                player.ReleaseOrPickupHeldEntity(isRelease: true, playerHeldEntity);
                bot.isAboutToSwapHeldEntity = true;
                bot.ReleaseOrPickupHeldEntity(isRelease: false, playerHeldEntity);
            }
            //! - Only bot holding something -
            else if (bot.heldEntity)
            {
                bot.ReleaseOrPickupHeldEntity(isRelease: true, botHeldEntity);
                player.isAboutToSwapHeldEntity = true;
                player.ReleaseOrPickupHeldEntity(isRelease: false, botHeldEntity);
            }


        }
    }
    void SwitchWallStates(PlayerScript bot, PlayerScript player)
    {
        //! - Wall States -
        if (player.isOnWall || bot.isOnWall)
        {
            bool playerOnWall = player.isOnWall;
            bool botOnWall = bot.isOnWall;

            bool playerOnCorner = player.isOnCorner;
            bool botOnCorner = bot.isOnCorner;

            bool playerWasPositiveAngle = player.wasPositiveAngle;
            bool botWasPositiveAngle = bot.wasPositiveAngle;

            float playerTimeWallGrabbed = player.timeWallGrabbed;
            float botTimeWallGrabbed = bot.timeWallGrabbed;

            float playerTimeStartedGrab = player.timeStartedGrab;
            float botTimeStartedGrab = bot.timeStartedGrab;

            float playerTimeChangingHands = player.timeChangingHands;
            float botTimeChangingHands = bot.timeChangingHands;

            Vector3 playerContactNormal = player.contactNormal;
            Vector3 botContactNormal = bot.contactNormal;

            player.isOnWall = botOnWall;
            bot.isOnWall = playerOnWall;

            player.isOnCorner = botOnCorner;
            bot.isOnCorner = playerOnCorner;

            player.wasPositiveAngle = botWasPositiveAngle;
            bot.wasPositiveAngle = playerWasPositiveAngle;

            player.timeWallGrabbed = botTimeWallGrabbed;
            bot.timeWallGrabbed = playerTimeWallGrabbed;

            player.timeStartedGrab = botTimeStartedGrab;
            bot.timeStartedGrab = playerTimeStartedGrab;

            player.timeChangingHands = botTimeChangingHands;
            bot.timeChangingHands = playerTimeChangingHands;

            player.contactNormal = botContactNormal;
            bot.contactNormal = playerContactNormal;

            player.SetWallGravityForSwitching(player.isOnWall);
            bot.SetWallGravityForSwitching(bot.isOnWall);
        }
    }
    void SwitchDiveStates(PlayerScript bot, PlayerScript player)
    {
        //! - Diving States -
        //diveShield.SetActive(true);
        if (player.isDiving || bot.isDiving)
        {
            bool playerIsDiving = player.isDiving;
            bool botIsDiving = bot.isDiving;

            bool playerIsDiveRecovering = player.isDiveRecovering;
            bool botIsDiveRecovering = bot.isDiveRecovering;

            bool playerCanDive = player.canDive;
            bool botCanDive = bot.canDive;

            bool playerDivedFromGoalBox = player.divedFromGoalBox;
            bool botDivedFromGoalBox = bot.divedFromGoalBox;

            float playerDiveStartTime = player.diveStartTime;
            float botDiveStartTime = bot.diveStartTime;

            float playerTimeHitGroundInDive = player.timeHitGroundInDive;
            float botTimeHitGroundInDive = bot.timeHitGroundInDive;

            float playerTimeGotKnockedInDive = player.timeGotKnockedInDive;
            float botTimeGotKnockedInDive = bot.timeGotKnockedInDive;

            float playerHitBallTime = player.timeHitBall;
            float botHitBallTime = bot.timeHitBall;

            float playerTimeEndedDive = player.timeEndedDive;
            float botTimeEndedDive = bot.timeEndedDive;

            float playerDiveRecoveryTime = player.diveRecoveryTime;
            float botDiveRecoveryTime = bot.diveRecoveryTime;

            float playerTimeStartedCatch = player.timeStartedCatch;
            float botTimeStartedCatch = bot.timeStartedCatch;


            player.isDiving = botIsDiving;
            bot.isDiving = playerIsDiving;

            player.isDiveRecovering = botIsDiveRecovering;
            bot.isDiveRecovering = playerIsDiveRecovering;

            bot.canDive = playerCanDive;
            player.canDive = botCanDive;

            player.divedFromGoalBox = botDivedFromGoalBox;
            bot.divedFromGoalBox = playerDivedFromGoalBox;

            player.diveStartTime = botDiveStartTime;
            bot.diveStartTime = playerDiveStartTime;

            player.timeHitGroundInDive = botTimeHitGroundInDive;
            bot.timeHitGroundInDive = playerTimeHitGroundInDive;

            player.timeGotKnockedInDive = botTimeGotKnockedInDive;
            bot.timeGotKnockedInDive = playerTimeGotKnockedInDive;

            player.timeHitBall = botHitBallTime;
            bot.timeHitBall = playerHitBallTime;

            player.timeEndedDive = botTimeEndedDive;
            bot.timeEndedDive = playerTimeEndedDive;

            player.diveRecoveryTime = botDiveRecoveryTime;
            bot.diveRecoveryTime = playerDiveRecoveryTime;

            player.timeStartedCatch = botTimeStartedCatch;
            bot.timeStartedCatch = playerTimeStartedCatch;

            player.ResetAnimations(false);
            bot.ResetAnimations(false);

            HandleDiveAnimationSwitch(player);
            HandleDiveAnimationSwitch(bot);
        }
    }
    void HandleDiveAnimationSwitch(PlayerScript targetPlayer)
    {
        if (targetPlayer.animator != null)
        {
            if (targetPlayer.isDiving)
            {
                //! - Set the correct dive layer for the situation -
                if (targetPlayer.heldEntity)
                {
                    targetPlayer.animator.SetLayerWeight(DIVE_LAYER_INDEX, 0);
                    targetPlayer.animator.SetLayerWeight(HOLDING_DIVE_LAYER_INDEX, 1);
                    targetPlayer.animator.SetLayerWeight(CATCH_DIVE_LAYER_INDEX, 0);
                }
                else
                {
                    targetPlayer.animator.SetLayerWeight(DIVE_LAYER_INDEX, 1);
                    targetPlayer.animator.SetLayerWeight(HOLDING_DIVE_LAYER_INDEX, 0);
                    if (targetPlayer.timeStartedCatch != 0f)
                    {
                        targetPlayer.animator.Play("Catch Dive");
                        targetPlayer.animator.SetLayerWeight(CATCH_DIVE_LAYER_INDEX, 1);
                    }
                    else
                    {
                        targetPlayer.animator.SetLayerWeight(CATCH_DIVE_LAYER_INDEX, 0);
                    }
                }
                //! - Begin Animation set -
                if (targetPlayer.divedFromGoalBox)
                {
                    if (targetPlayer.isOnGround)
                    {
                        targetPlayer.animator.Play("Save Ground");
                    }
                    else
                    {
                        if (targetPlayer.animator.GetBool("isFalling"))
                        {
                            targetPlayer.animator.Play("Save Falling");
                        }
                        else
                        {
                            targetPlayer.animator.Play("Save Jumping");
                        }
                    }
                }
                else
                {
                    if (targetPlayer.isOnGround)
                    {
                        targetPlayer.animator.Play("Header Ground");
                    }
                    else
                    {
                        if (targetPlayer.animator.GetBool("isFalling"))
                        {
                            targetPlayer.animator.Play("Header Falling");
                        }
                        else
                        {
                            targetPlayer.animator.Play("Header Jumping");
                        }
                    }
                }
            }
            else
            {
                targetPlayer.animator.SetLayerWeight(DIVE_LAYER_INDEX, 0);
                targetPlayer.animator.SetLayerWeight(CATCH_DIVE_LAYER_INDEX, 0);
                targetPlayer.animator.SetLayerWeight(HOLDING_DIVE_LAYER_INDEX, 0);
            }
        }
    }
    void AssignBotJob()
    {
        if (botsTeamA.Count > 0)
        {
            //! - Set bool state in bot brain whther they are chasing the ball or not -
            //! - codeReview: will a bot with a player team mate always be isOnBall, since it is closest bot on team to ball
            foreach (PlayerScript bot in botsTeamA)
            {
                if (bot != GetBotClosestToGameball(botsTeamA))
                {
                    bot.GetComponent<BotBrain>().isOnBall = false;
                }
                else
                {
                    bot.GetComponent<BotBrain>().isOnBall = true;
                }

                //! - just hard setting goalie bot to on ball atm -
                if (bot.isGoalie)
                {
                    bot.GetComponent<BotBrain>().isOnBall = true;
                }
            }
        }
        if (botsTeamB.Count > 0)
        {
            //! - Set bool state in bot brain whther they are chasing the ball or not -
            foreach (PlayerScript bot in botsTeamB)
            {
                if (bot != GetBotClosestToGameball(botsTeamB))
                {
                    bot.GetComponent<BotBrain>().isOnBall = false;
                }
                else
                {
                    bot.GetComponent<BotBrain>().isOnBall = true;
                }

                //! - just hard setting goalie bot to on ball atm -
                if (bot.isGoalie)
                {
                    bot.GetComponent<BotBrain>().isOnBall = true;
                }
            }
        }
    }
    void AssignTeamPosession()
    {
        if (botsTeamA.Count > 0)
        {
            //! - Set bool state in bot brain whther they are chasing the ball or not -
            foreach (PlayerScript bot in botsTeamA)
            {
                if (bot != GetBotClosestToGameball(botsTeamA))
                {
                    bot.GetComponent<BotBrain>().isOnBall = false;
                }
                else
                {
                    bot.GetComponent<BotBrain>().isOnBall = true;
                }

                //! - just hard setting goalie bot to on ball atm -
                if (bot.isGoalie)
                {
                    bot.GetComponent<BotBrain>().isOnBall = true;
                }
            }
        }
        if (botsTeamB.Count > 0)
        {
            //! - Set bool state in bot brain whther they are chasing the ball or not -
            foreach (PlayerScript bot in botsTeamB)
            {
                if (bot != GetBotClosestToGameball(botsTeamB))
                {
                    bot.GetComponent<BotBrain>().isOnBall = false;
                }
                else
                {
                    bot.GetComponent<BotBrain>().isOnBall = true;
                }

                //! - just hard setting goalie bot to on ball atm -
                if (bot.isGoalie)
                {
                    bot.GetComponent<BotBrain>().isOnBall = true;
                }
            }
        }
    }
    PlayerScript GetBotClosestToGameball(List<PlayerScript> bots)
    {
        //! - Calculate closest bot to the gameball -
        float shortestDistance = 160f;
        float newDistance;
        PlayerScript closetsBot = null;
        foreach (PlayerScript bot in bots)
        {
            newDistance = (gameball.transform.position - bot.transform.position).magnitude;
            if (newDistance < shortestDistance && !bot.isGoalie)
            {
                shortestDistance = newDistance;
                closetsBot = bot;
            }
        }
        return closetsBot;
    }
    public void GetBotList()
    {
        botsTeamA.Clear();
        botsTeamB.Clear();
        foreach (GameObject player in State.players)
        {
            if (player)
            {
                PlayerScript playerScript = player.GetComponent<PlayerScript>();
                if (playerScript.isBot)
                {
                    player.GetComponent<BotBrain>().GetPlayers();
                    if (playerScript.team == 0)
                    {
                        botsTeamA.Add(playerScript);
                    }
                    else
                    {
                        botsTeamB.Add(playerScript);
                    }
                }
            }
        }
    }
    public void AddToBotList(int team, PlayerScript botPlayerScript)
    {
        if (botPlayerScript.team == 0)
        {
            botsTeamA.Add(botPlayerScript);
        }
        else
        {
            botsTeamB.Add(botPlayerScript);
        }
    }
    public void HandleChangingPossession(int possession)
    {
        //! - Only run if possession changing to different team or state -
        //if (possession != possessionState)
        {
            //! - Used to check in coroutine if the state possession it is trying to change to matches the latest possession state input -
            //! - Used for multiple coroutines running and to ensure that the latest coroutine is prioritised over previous state change coroutines -
            currentPossessionState += 1;

            //! - Start possession timer for possession team -
            StartCoroutine(ChangePossession(possession, currentPossessionState));
        }
    }
    public IEnumerator ChangePossession(int possession, int stateIndex)
    {
        if (possession == NO_POSESSION)
        {
            yield return new WaitForSeconds(1.5f);
        }
        else
        {
            yield return new WaitForSeconds(1f);
        }
        if (stateIndex == currentPossessionState)
        {
            possessionState = possession;
            print($"CHANGED POSSESSION STATE: {possessionState}");
        }
    }
    IEnumerator NameTagDelay(PlayerScript bot, PlayerScript player)
    {
        bot.nameTags[player.playerIndex].gameObject.SetActive(false);
        yield return new WaitForSeconds(NAMETAG_DELAY);
        bot.nameTags[player.playerIndex].gameObject.SetActive(true);
    }
}
