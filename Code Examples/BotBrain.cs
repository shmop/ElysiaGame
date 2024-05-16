using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class BotBrain : MonoBehaviour
{
    const float SLAP_COOLDOWN = 1.5f;
    const float SLAP_DISTANCE = 1.5f;
    const float SLAP_RADIUS = 0.5f;
    const float GRAB_DISTANCE = 10f;
    const float DIVE_DISTANCE = 20f;
    const float MIN_CHARGE_TIME = 2f;
    const float MAX_CHARGE_TIME = 1.5f; //? was 5f - sped up to avoid long time jumping at goal without throwing
    const float INPUT_DELAY_TIME = 0.5f;
    const float DASH_DELAY_TIME = 0.5f;
    const float ONWALL_DELAY_TIME = 0.8f;
    const float INITIAL_SPEED = 66.67f;
    const int NO_POSSESSION = 0;
    const int TEAM_POSSESSION = 1;
    const int ENEMY_POSSESSION = 2;
    const int GAMEBALL_TARGET = 0;
    const int AWAY_FROM_GAMEBALL_TARGET = 1;
    const int TEAM_A_GOAL_TARGET = 2;
    const int TEAM_B_GOAL_TARGET = 3;
    const int TEAMMATE_TARGET = 4;
    const int CLOSEST_ENEMY_TARGET = 5;
    const int CLOSEST_ENEMY_TO_GAMEBALL_TARGET = 6;
    const int AWAY_FROM_CLOSEST_ENEMY_TARGET_TO_GAMEBALL = 7;
    const int FORWARD_TARGET = 8;
    const int ZERO_TARGET = 9;
    Vector3 GRAB_BOX = new Vector3(2.5f, 2.5f, 0.5f);
    Vector3 DIVE_BOX = new Vector3(12f, 11f, 5f);
    Vector3 GAMEBALL_CHECK_BOX_TEAM_A = new Vector3(0f, 25f, -141f);
    Vector3 GAMEBALL_CHECK_BOX_TEAM_B = new Vector3(0f, 25f, 141f);
    Vector3 GAMEBALL_CHECK_BOX = new Vector3(13f, 11f, 13f);
    Vector3 SLAP_BOX = new Vector3(5f, 5f, 5f);
    const float JUMP_ONE_PEAK_TIME = 0.8f;
    const float JUMP_TWO_PEAK_TIME = 1f;
    const float DIVE_REACTION_TIME = 0.1f;
    const float FIELD_DISTANCE = 256f;
    const float PLAYER_SPEED = 30f;
    const float PLAYER_AIR_SPEED = 40f;
    const float GAMEBALL_SPEED = 65f;
    const float DODGEBALL_SPEED = 120f;

    [SerializeField]
    private State state;
    private GameManager gameManager;
    public PlayerScript botPlayerScript;
    private PlayerScript closestEnemyPlayerScript;
    private GameObject gameball;
    private List<PlayerScript> players = new List<PlayerScript>();
    private List<PlayerScript> botTeammates = new List<PlayerScript>();
    private List<PlayerScript> teammates = new List<PlayerScript>();
    private List<PlayerScript> enemyPlayers = new List<PlayerScript>();

    Collider[] slapColliders;
    RaycastHit[] grabColliders;
    Collider[] dodgeballColliders;
    Collider[] saveColliders;
    public bool isOnBall = false;
    int possessionState = 0;
    int movingTargetState = 0;
    int rotationTargetState = 0;
    float timePickedupEntity = 0f;
    float timeStartedCharge = 0f;
    float timeDelayForThrow = 0f;
    float timeOnWall = 0f;
    float timeJumped = 0f;
    float timeDetectedShotBall = 0f;
    float timeOfAction = 0f;
    float timeThrownDodgeball = 0f;
    float angleOffset = 0f;
    float sideAngleOffset = 0f;
    float distanceToBall;
    bool isGoalieDiving = false;
    bool teamGoalieHasBall = false;
    bool enemyGoalieHasBall = false;
    bool canSearchForSlap = true;
    bool lookToThrowDodgeball = true;
    public bool walkingToGameball = false;
    Vector3 dirToBall;
    bool isJumpingAtGoal = false;
    bool jumpingInGoal = false;

    [SerializeField]
    float reactionTime = 0f;

    private void Awake()
    {
        gameManager = State.gameManager;
        botPlayerScript = GetComponent<PlayerScript>();
        gameball = State.gameManager.gameball;
    }
    void Update()
    {
        if (gameManager.gameStarted)
        {
            //! - Goalie will only catch and throw for now -
            if (botPlayerScript.isGoalie)
            {
                //! - only move when jumping at goal coroutine is not active -
                CheckEnemyPlayer();
                MoveAsGoalie();
                RotateAsGoalie();
                JumpInGoals();
                if (timeOfAction + INPUT_DELAY_TIME < Time.time)
                {
                    CheckGrab();
                    if (botPlayerScript.heldEntity)
                    {
                        CheckThrow();
                    }
                }

            }
            //! - do majority of things when bot is not goalie for now -
            else
            {
                CheckEnemyPlayer();
                CheckIfTeamGoalieHasBall();
                CheckIfEnemyGoalieHasBall();
                if (gameManager.testingNewBotStates)
                {
                    UpdatePossessionState();
                    if (OnlyFielderOnTeam())
                    {
                        HandleBotTargetOnlyThisBotOnTeam();
                    }
                    else
                    {
                        HandleBotTarget();
                    }
                    HandleMoving();
                    HandleRotation();
                }
                else
                {
                    MoveAsFielder();
                    RotateAsFielder();
                }
                JumpOffWall();
                JumpAsFielder();
                DiveAsFielder();
                //! - Testing bots with dodgeball changed 16/05 - 
                if (gameManager.testingBotDodgeball)
                {
                    SpawnOrDespawnDodgeball();
                    RecallDodgeball();
                }
                if (timeOfAction + INPUT_DELAY_TIME < Time.time)
                {
                    if (!botPlayerScript.heldEntity)
                    {
                        CheckGrab();
                        Slap();
                    }
                    else
                    {
                        CheckThrow();
                    }
                }
            }
        }
        else
        {
            botPlayerScript.SimulateInputLS(Vector2.zero);
            botPlayerScript.SimulateInputRS(Vector2.zero);
        }
    }

    private void UpdatePossessionState()
    {
        if (possessionState != gameManager.botManager.possessionState)
        {
            //! - No clear possession -
            if (gameManager.botManager.possessionState == 0)
            {
                possessionState = NO_POSSESSION;
            }
            //! - No clear possession -
            else if (gameManager.botManager.possessionState == botPlayerScript.team + 1)
            {
                possessionState = TEAM_POSSESSION;
            }
            //! - Enemy has the ball -
            else
            {
                possessionState = ENEMY_POSSESSION;
            }
        }
    }
    void HandleBotTarget()
    {
        //! - Team goalie has the ball -
        if (teamGoalieHasBall)
        {
            //! - Attack enemy player closest to this bot only if they are close -
            if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                !closestEnemyPlayerScript.playerRecovering &&
                Vector3.Distance(transform.position, closestEnemyPlayerScript.transform.position) < 20f)
            {
                movingTargetState = CLOSEST_ENEMY_TARGET;
                rotationTargetState = CLOSEST_ENEMY_TARGET;
                lookToThrowDodgeball = true;
            }
            //! - Move towards goal anticipating pass -
            else
            {
                if (botPlayerScript.team == 0)
                {
                    movingTargetState = TEAM_B_GOAL_TARGET;
                }
                else
                {
                    movingTargetState = TEAM_A_GOAL_TARGET;
                }
                rotationTargetState = GAMEBALL_TARGET;
                lookToThrowDodgeball = false;
            }
            if (botPlayerScript.heldEntity)
            {
                if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                {
                    //! - If holding team player -
                    if (botPlayerScript.team == botPlayerScript.heldEntity.GetComponent<PlayerScript>().team)
                    {
                        rotationTargetState = GAMEBALL_TARGET;
                    }
                    //! - If holding enemy player -
                    else
                    {
                        if (botPlayerScript.team == 0)
                        {
                            rotationTargetState = TEAM_A_GOAL_TARGET;
                        }
                        else
                        {
                            rotationTargetState = TEAM_B_GOAL_TARGET;
                        }
                    }
                }
                else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                {
                    //! - Attack enemy player closest to gameball -
                    if (FindEnemyClosestToGameball())
                    {
                        rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                    }
                    //! - Attack enemy player closest to this bot -
                    else if (closestEnemyPlayerScript)
                    {
                        rotationTargetState = CLOSEST_ENEMY_TARGET;
                    }
                    //! - Otherwise aim for enemy goal
                    else
                    {
                        if (botPlayerScript.team == 0)
                        {
                            rotationTargetState = TEAM_B_GOAL_TARGET;
                        }
                        else
                        {
                            rotationTargetState = TEAM_A_GOAL_TARGET;
                        }
                    }
                }
            }
        }
        //! - Enemy goalie has the ball -
        else if (enemyGoalieHasBall)
        {
            //! - Attack enemy player closest to this bot only if they are close -
            if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                !closestEnemyPlayerScript.playerRecovering &&
                Vector3.Distance(transform.position, closestEnemyPlayerScript.transform.position) < 20f)
            {
                movingTargetState = CLOSEST_ENEMY_TARGET;
                rotationTargetState = CLOSEST_ENEMY_TARGET;
                lookToThrowDodgeball = true;
            }
            //! - Move towards goal -
            else
            {
                if (FindTeammateClosestToGameball() && FindTeammateClosestToGameball() == botPlayerScript)
                {
                    //! - Try to intercept pass -
                    if (botPlayerScript.team == 0)
                    {
                        movingTargetState = TEAM_B_GOAL_TARGET;
                    }
                    else
                    {
                        movingTargetState = TEAM_A_GOAL_TARGET;
                    }
                    rotationTargetState = GAMEBALL_TARGET;
                    lookToThrowDodgeball = false;
                }
                else
                {
                    //! - Fall back -
                    if (botPlayerScript.team == 0)
                    {
                        movingTargetState = TEAM_A_GOAL_TARGET;
                    }
                    else
                    {
                        movingTargetState = TEAM_B_GOAL_TARGET;
                    }
                    rotationTargetState = GAMEBALL_TARGET;
                    lookToThrowDodgeball = true;
                }
            }
            if (botPlayerScript.heldEntity)
            {
                if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                {
                    //! - If holding team player -
                    if (botPlayerScript.team == botPlayerScript.heldEntity.GetComponent<PlayerScript>().team)
                    {
                        if (botPlayerScript.team == 0)
                        {
                            rotationTargetState = TEAM_A_GOAL_TARGET;
                        }
                        else
                        {
                            rotationTargetState = TEAM_B_GOAL_TARGET;
                        }
                    }
                    //! - If holding enemy player -
                    else
                    {
                        if (botPlayerScript.team == 0)
                        {
                            rotationTargetState = TEAM_B_GOAL_TARGET;
                        }
                        else
                        {
                            rotationTargetState = TEAM_A_GOAL_TARGET;
                        }
                    }
                }
                else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                {
                    //! - Attack enemy player closest to gameball -
                    if (FindEnemyClosestToGameball())
                    {
                        rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                    }
                    //! - Attack enemy player closest to this bot -
                    else if (closestEnemyPlayerScript)
                    {
                        rotationTargetState = CLOSEST_ENEMY_TARGET;
                    }
                    //! - Otherwise aim for enemy goal
                    else
                    {
                        if (botPlayerScript.team == 0)
                        {
                            rotationTargetState = TEAM_B_GOAL_TARGET;
                        }
                        else
                        {
                            rotationTargetState = TEAM_A_GOAL_TARGET;
                        }
                    }
                }
            }
        }
        //! - No clear possession -
        else if (possessionState == NO_POSSESSION)
        {
            //! - If no teammates always go for ball during no possession -
            //! - This bot is the bot closest on team to the gameball -
            if (FindTeammateClosestToGameball() && FindTeammateClosestToGameball() == botPlayerScript)
            {
                lookToThrowDodgeball = false;
                //! - If no possession of the gameball go for the gameball -
                if (!botPlayerScript.heldEntity)
                {
                    //! - Move towards gameball -
                    movingTargetState = GAMEBALL_TARGET;
                    rotationTargetState = GAMEBALL_TARGET;
                }
                //! - Holding an object -
                else
                {
                    //! - Has the gameball -
                    if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                    {
                        //! - Enemy nearby -
                        if (FindEnemyClosestToGameball() && (FindEnemyClosestToGameball().
                            transform.position - transform.position).magnitude < 20f)
                        {
                            //! - Move away from closest player -
                            movingTargetState = AWAY_FROM_CLOSEST_ENEMY_TARGET_TO_GAMEBALL;
                        }
                        //! - No enemies nearby -
                        else
                        {
                            //! - Move towards goal -
                            if (botPlayerScript.team == 0)
                            {
                                movingTargetState = TEAM_B_GOAL_TARGET;
                            }
                            else
                            {
                                movingTargetState = TEAM_A_GOAL_TARGET;
                            }
                        }
                        //! - Aim for enemy goal -
                        if (botPlayerScript.team == 0)
                        {
                            rotationTargetState = TEAM_B_GOAL_TARGET;
                        }
                        else
                        {
                            rotationTargetState = TEAM_A_GOAL_TARGET;
                        }
                    }
                    //! - Holding another Player -
                    else if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                    {
                        movingTargetState = GAMEBALL_TARGET;
                        //! - Holding teammate -
                        if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                        {
                            //! - Move towards gameball and aim teammate towards gameball -
                            rotationTargetState = GAMEBALL_TARGET;
                        }
                        else
                        {
                            //! - Move towards gameball and aim enemy away from gameball to keep them away -
                            rotationTargetState = AWAY_FROM_GAMEBALL_TARGET;
                        }
                    }
                    //! - Holding Dodgeball -
                    else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                    {
                        movingTargetState = GAMEBALL_TARGET;
                        //! - Attack enemy player closest to gameball -
                        if (FindEnemyClosestToGameball())
                        {
                            rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                        }
                        //! - Attack enemy player closest to this bot -
                        else if (closestEnemyPlayerScript)
                        {
                            rotationTargetState = CLOSEST_ENEMY_TARGET;
                        }
                        //! - Otherwise aim for enemy goal
                        else
                        {
                            if (botPlayerScript.team == 0)
                            {
                                rotationTargetState = TEAM_B_GOAL_TARGET;
                            }
                            else
                            {
                                rotationTargetState = TEAM_A_GOAL_TARGET;
                            }
                        }
                    }
                }
            }
            //! - This bot is not the closest to the gameball on team -
            else
            {
                lookToThrowDodgeball = true;
                //! - If no possession of the gameball go for the gameball -
                if (!botPlayerScript.heldEntity)
                {
                    //! - Attack enemy player closest to ball -
                    if (FindEnemyClosestToGameball())
                    {
                        movingTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                        rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                    }
                    else if (closestEnemyPlayerScript)
                    {
                        movingTargetState = CLOSEST_ENEMY_TARGET;
                        rotationTargetState = CLOSEST_ENEMY_TARGET;
                    }
                    //! - No available enemy to target -
                    else
                    {
                        if (botPlayerScript.team == 0)
                        {
                            movingTargetState = TEAM_B_GOAL_TARGET;
                            rotationTargetState = TEAM_B_GOAL_TARGET;
                        }
                        else
                        {
                            movingTargetState = TEAM_A_GOAL_TARGET;
                            rotationTargetState = TEAM_A_GOAL_TARGET;
                        }
                    }
                }
                //! - Holding an object -
                else
                {
                    //! - Has the gameball -
                    if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                    {
                        //! - Enemy nearby -
                        if (FindEnemyClosestToGameball() && (FindEnemyClosestToGameball().
                            transform.position - transform.position).magnitude < 20f)
                        {
                            //! - Move away from closest player -
                            movingTargetState = AWAY_FROM_CLOSEST_ENEMY_TARGET_TO_GAMEBALL;
                        }
                        //! - No enemies nearby -
                        else
                        {
                            //! - Move towards goal -
                            if (botPlayerScript.team == 0)
                            {
                                movingTargetState = TEAM_B_GOAL_TARGET;
                            }
                            else
                            {
                                movingTargetState = TEAM_A_GOAL_TARGET;
                            }
                        }
                        //! - Aim for enemy goal -
                        if (botPlayerScript.team == 0)
                        {
                            rotationTargetState = TEAM_B_GOAL_TARGET;
                        }
                        else
                        {
                            rotationTargetState = TEAM_A_GOAL_TARGET;
                        }
                    }
                    //! - Holding another Player -
                    else if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                    {
                        movingTargetState = GAMEBALL_TARGET;
                        //! - Holding teammate -
                        if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                        {
                            //! - Move towards gameball and aim teammate towards gameball -
                            rotationTargetState = GAMEBALL_TARGET;
                        }
                        else
                        {
                            //! - Move towards gameball and aim enemy away from gameball to keep them away -
                            rotationTargetState = AWAY_FROM_GAMEBALL_TARGET;
                        }
                    }
                    //! - Holding Dodgeball -
                    else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                    {
                        movingTargetState = GAMEBALL_TARGET;
                        //! - Attack enemy player closest to gameball -
                        if (FindEnemyClosestToGameball())
                        {
                            rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                        }
                        //! - Attack enemy player closest to this bot -
                        else if (closestEnemyPlayerScript)
                        {
                            rotationTargetState = CLOSEST_ENEMY_TARGET;
                        }
                        //! - Otherwise aim for enemy goal
                        else
                        {
                            if (botPlayerScript.team == 0)
                            {
                                rotationTargetState = TEAM_B_GOAL_TARGET;
                            }
                            else
                            {
                                rotationTargetState = TEAM_A_GOAL_TARGET;
                            }
                        }
                    }
                }
            }
        }
        //! - Team has the ball -
        else if (possessionState == TEAM_POSSESSION)
        {
            //! - This bot is the bot closest on team to the gameball -
            if (FindTeammateClosestToGameball() && FindTeammateClosestToGameball() == botPlayerScript)
            {
                lookToThrowDodgeball = false;
                //! - Holding nothing, try to get the gameball back -
                if (!botPlayerScript.heldEntity)
                {
                    //! - Move towards gameball -
                    movingTargetState = GAMEBALL_TARGET;
                    rotationTargetState = GAMEBALL_TARGET;
                }
                else
                {
                    //! - Has the gameball -
                    if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                    {
                        //! - Enemy nearby -
                        if (FindEnemyClosestToGameball() && (FindEnemyClosestToGameball().
                            transform.position - transform.position).magnitude < 10f)
                        {
                            //! - Move away from closest player -
                            movingTargetState = AWAY_FROM_CLOSEST_ENEMY_TARGET_TO_GAMEBALL;
                        }
                        //! - No enemies nearby -
                        else
                        {
                            if (timeStartedCharge != 0f && timeStartedCharge + MAX_CHARGE_TIME - 0.3f > Time.time)
                            {
                                //! - Move towards goal -
                                if (botPlayerScript.team == 0)
                                {
                                    movingTargetState = TEAM_B_GOAL_TARGET;
                                }
                                else
                                {
                                    movingTargetState = TEAM_A_GOAL_TARGET;
                                }
                            }
                            else
                            {
                                movingTargetState = ZERO_TARGET;
                            }
                        }
                        if (teammates.Count > 2 && FindTeammateToPassAsFielder())
                        {
                            //! - Look for a teammate to pass to -
                            rotationTargetState = TEAMMATE_TARGET;
                        }
                        else
                        {
                            //! - Rotate to look at opposition goal -
                            if (botPlayerScript.team == 0)
                            {
                                rotationTargetState = TEAM_B_GOAL_TARGET;
                            }
                            else
                            {
                                rotationTargetState = TEAM_A_GOAL_TARGET;
                            }
                        }
                    }
                    //! - Holding another Player -
                    else if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                    {
                        movingTargetState = GAMEBALL_TARGET;
                        //! - Holding teammate -
                        if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                        {
                            //! - Aim teammate towards gameball -
                            rotationTargetState = GAMEBALL_TARGET;
                        }
                        else
                        {
                            //! - Aim enemy away to oppsite goal to keep them away -
                            if (botPlayerScript.team == 0)
                            {
                                rotationTargetState = TEAM_A_GOAL_TARGET;
                            }
                            else
                            {
                                rotationTargetState = TEAM_B_GOAL_TARGET;
                            }
                        }
                    }
                    //! - Holding Dodgeball -
                    else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                    {
                        movingTargetState = GAMEBALL_TARGET;
                        //! - Attack enemy player closest to gameball -
                        if (FindEnemyClosestToGameball())
                        {
                            rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                        }
                        //! - Attack enemy player closest to this bot -
                        else if (closestEnemyPlayerScript)
                        {
                            rotationTargetState = CLOSEST_ENEMY_TARGET;
                        }
                        //! - Otherwise aim for enemy goal
                        else
                        {
                            if (botPlayerScript.team == 0)
                            {
                                rotationTargetState = TEAM_B_GOAL_TARGET;
                            }
                            else
                            {
                                rotationTargetState = TEAM_A_GOAL_TARGET;
                            }
                        }
                    }
                }
            }
            //! - Not the closest bot on team to gameball, try to find better position or attack enemy -
            else
            {
                //! - Bot teammate has the gameball -
                if (FindTeammateClosestToGameball().GetComponent<BotBrain>())
                {
                    //! - Bot teammate trying to pass 
                    if (FindTeammateClosestToGameball().GetComponent<BotBrain>().FindTeammateToPassAsFielder())
                    {
                        //! - If this bot is the intended pass target move towards goal -
                        if (FindTeammateClosestToGameball().GetComponent<BotBrain>().FindTeammateToPassAsFielder() == botPlayerScript)
                        {
                            lookToThrowDodgeball = false;
                            //! - Move towards goal
                            if (botPlayerScript.team == 0)
                            {
                                movingTargetState = TEAM_B_GOAL_TARGET;
                            }
                            else
                            {
                                movingTargetState = TEAM_A_GOAL_TARGET;
                            }
                        }
                        //! - If this bot is not the intended pass target (still push up for now)-
                        else
                        {
                            lookToThrowDodgeball = true;
                            //! - Move towards goal
                            if (botPlayerScript.team == 0)
                            {
                                movingTargetState = TEAM_B_GOAL_TARGET;
                            }
                            else
                            {
                                movingTargetState = TEAM_A_GOAL_TARGET;
                            }
                        }
                        rotationTargetState = GAMEBALL_TARGET;
                        if (botPlayerScript.heldEntity)
                        {
                            if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                            {
                                if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                                {
                                    if (botPlayerScript.team == 0)
                                    {
                                        rotationTargetState = TEAM_B_GOAL_TARGET;
                                    }
                                    else
                                    {
                                        rotationTargetState = TEAM_A_GOAL_TARGET;
                                    }
                                }
                                else
                                {
                                    //! - Move towards gameball and aim enemy away from gameball to keep them away -
                                    rotationTargetState = AWAY_FROM_GAMEBALL_TARGET;
                                }
                            }
                            else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                            {
                                //! - Attack enemy player closest to gameball -
                                if (FindEnemyClosestToGameball())
                                {
                                    rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                                }
                                //! - Attack enemy player closest to this bot -
                                else if (closestEnemyPlayerScript)
                                {
                                    rotationTargetState = CLOSEST_ENEMY_TARGET;
                                }
                                //! - Otherwise aim for enemy goal
                                else
                                {
                                    if (botPlayerScript.team == 0)
                                    {
                                        rotationTargetState = TEAM_B_GOAL_TARGET;
                                    }
                                    else
                                    {
                                        rotationTargetState = TEAM_A_GOAL_TARGET;
                                    }
                                }
                            }
                        }
                    }
                    //! - Bot teammate trying to shoot -
                    else
                    {
                        lookToThrowDodgeball = true;
                        //! - Attack enemy player closest to ball -
                        if (FindEnemyClosestToGameball())
                        {
                            movingTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                            rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                        }
                        else if (closestEnemyPlayerScript)
                        {
                            movingTargetState = CLOSEST_ENEMY_TARGET;
                            rotationTargetState = CLOSEST_ENEMY_TARGET;
                        }
                        //! - No available enemy to target -
                        else
                        {
                            //! - Move towards goal
                            if (botPlayerScript.team == 0)
                            {
                                movingTargetState = TEAM_B_GOAL_TARGET;
                            }
                            else
                            {
                                movingTargetState = TEAM_A_GOAL_TARGET;
                            }
                            rotationTargetState = GAMEBALL_TARGET;
                        }
                        if (botPlayerScript.heldEntity)
                        {
                            if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                            {
                                if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                                {
                                    if (botPlayerScript.team == 0)
                                    {
                                        rotationTargetState = TEAM_B_GOAL_TARGET;
                                    }
                                    else
                                    {
                                        rotationTargetState = TEAM_A_GOAL_TARGET;
                                    }
                                }
                                else
                                {
                                    //! - Move towards gameball and aim enemy away from gameball to keep them away -
                                    rotationTargetState = AWAY_FROM_GAMEBALL_TARGET;
                                }
                            }
                        }
                    }
                }
                //! - Player teammate has the ball -
                else
                {
                    //! - Player is close to the goal -
                    if (FindTeammateDistanceToGoal(FindTeammateClosestToGameball(), ownGoal: false) < 100f)
                    {
                        //! - Player is closer to goal than this bot -
                        if (FindTeammateDistanceToGoal(FindTeammateClosestToGameball(), ownGoal: false) <
                            FindTeammateDistanceToGoal(botPlayerScript, ownGoal: false))
                        {
                            lookToThrowDodgeball = true;
                            //! - Attack enemy player closest to ball -
                            if (FindEnemyClosestToGameball())
                            {
                                movingTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                                rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                            }
                            else if (closestEnemyPlayerScript)
                            {
                                movingTargetState = CLOSEST_ENEMY_TARGET;
                                rotationTargetState = CLOSEST_ENEMY_TARGET;
                            }
                            //! - No available enemy to target -
                            else
                            {
                                //! - Move towards goal
                                if (botPlayerScript.team == 0)
                                {
                                    movingTargetState = TEAM_B_GOAL_TARGET;
                                }
                                else
                                {
                                    movingTargetState = TEAM_A_GOAL_TARGET;
                                }
                                rotationTargetState = GAMEBALL_TARGET;
                            }
                            if (botPlayerScript.heldEntity)
                            {
                                if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                                {
                                    if (botPlayerScript.team == 0)
                                    {
                                        rotationTargetState = TEAM_B_GOAL_TARGET;
                                    }
                                    else
                                    {
                                        rotationTargetState = TEAM_A_GOAL_TARGET;
                                    }
                                }
                                else if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                                {
                                    if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                                    {
                                        if (botPlayerScript.team == 0)
                                        {
                                            rotationTargetState = TEAM_B_GOAL_TARGET;
                                        }
                                        else
                                        {
                                            rotationTargetState = TEAM_A_GOAL_TARGET;
                                        }
                                    }
                                    else
                                    {
                                        //! - Move towards gameball and aim enemy away from gameball to keep them away -
                                        rotationTargetState = AWAY_FROM_GAMEBALL_TARGET;
                                    }
                                }
                                else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                                {
                                    //! - Attack enemy player closest to gameball -
                                    if (FindEnemyClosestToGameball())
                                    {
                                        rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                                    }
                                    //! - Attack enemy player closest to this bot -
                                    else if (closestEnemyPlayerScript)
                                    {
                                        rotationTargetState = CLOSEST_ENEMY_TARGET;
                                    }
                                    //! - Otherwise aim for enemy goal
                                    else
                                    {
                                        if (botPlayerScript.team == 0)
                                        {
                                            rotationTargetState = TEAM_B_GOAL_TARGET;
                                        }
                                        else
                                        {
                                            rotationTargetState = TEAM_A_GOAL_TARGET;
                                        }
                                    }
                                }
                            }
                        }
                        //! - Bot is closer to goal than player with ball -
                        else
                        {
                            lookToThrowDodgeball = true;
                            //! - Attack enemy player closest to this bot -
                            if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                                !closestEnemyPlayerScript.playerRecovering)

                            {
                                movingTargetState = CLOSEST_ENEMY_TARGET;
                                rotationTargetState = CLOSEST_ENEMY_TARGET;
                            }
                            //! - No available enemy to target -
                            else
                            {
                                //! - Move towards goal
                                if (botPlayerScript.team == 0)
                                {
                                    movingTargetState = TEAM_B_GOAL_TARGET;
                                }
                                else
                                {
                                    movingTargetState = TEAM_A_GOAL_TARGET;
                                }
                                rotationTargetState = GAMEBALL_TARGET;
                            }
                            if (botPlayerScript.heldEntity)
                            {
                                if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                                {
                                    if (botPlayerScript.team == 0)
                                    {
                                        rotationTargetState = TEAM_B_GOAL_TARGET;
                                    }
                                    else
                                    {
                                        rotationTargetState = TEAM_A_GOAL_TARGET;
                                    }
                                }
                                else if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                                {
                                    if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                                    {
                                        if (botPlayerScript.team == 0)
                                        {
                                            rotationTargetState = TEAM_B_GOAL_TARGET;
                                        }
                                        else
                                        {
                                            rotationTargetState = TEAM_A_GOAL_TARGET;
                                        }
                                    }
                                    else
                                    {
                                        //! - Move towards gameball and aim enemy away from gameball to keep them away -
                                        rotationTargetState = AWAY_FROM_GAMEBALL_TARGET;
                                    }
                                }
                                else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                                {
                                    //! - Attack enemy player closest to this bot -
                                    if (closestEnemyPlayerScript)
                                    {
                                        rotationTargetState = CLOSEST_ENEMY_TARGET;
                                    }
                                    //! - Otherwise aim for enemy goal
                                    else
                                    {
                                        if (botPlayerScript.team == 0)
                                        {
                                            rotationTargetState = TEAM_B_GOAL_TARGET;
                                        }
                                        else
                                        {
                                            rotationTargetState = TEAM_A_GOAL_TARGET;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    //! - Player is far from the goal -
                    else
                    {
                        //! - Player is closer to goal than this bot -
                        if (FindTeammateDistanceToGoal(FindTeammateClosestToGameball(), ownGoal: false) < FindTeammateDistanceToGoal(botPlayerScript, ownGoal: false))
                        {
                            lookToThrowDodgeball = true;
                            //! - Attack enemy player closest to ball -
                            if (FindEnemyClosestToGameball())
                            {
                                movingTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                                rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                            }
                            else if (closestEnemyPlayerScript)
                            {
                                movingTargetState = CLOSEST_ENEMY_TARGET;
                                rotationTargetState = CLOSEST_ENEMY_TARGET;
                            }
                            //! - No available enemy to target -
                            else
                            {
                                //! - Move towards goal
                                if (botPlayerScript.team == 0)
                                {
                                    movingTargetState = TEAM_B_GOAL_TARGET;
                                }
                                else
                                {
                                    movingTargetState = TEAM_A_GOAL_TARGET;
                                }
                                rotationTargetState = GAMEBALL_TARGET;
                            }
                            if (botPlayerScript.heldEntity)
                            {
                                if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                                {
                                    if (botPlayerScript.team == 0)
                                    {
                                        rotationTargetState = TEAM_B_GOAL_TARGET;
                                    }
                                    else
                                    {
                                        rotationTargetState = TEAM_A_GOAL_TARGET;
                                    }
                                }
                                else if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                                {
                                    if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                                    {
                                        if (botPlayerScript.team == 0)
                                        {
                                            rotationTargetState = TEAM_B_GOAL_TARGET;
                                        }
                                        else
                                        {
                                            rotationTargetState = TEAM_A_GOAL_TARGET;
                                        }
                                    }
                                    else
                                    {
                                        //! - Move towards gameball and aim enemy away from gameball to keep them away -
                                        rotationTargetState = AWAY_FROM_GAMEBALL_TARGET;
                                    }
                                }
                                else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                                {
                                    //! - Attack enemy player closest to gameball -
                                    if (FindEnemyClosestToGameball())
                                    {
                                        rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                                    }
                                    //! - Attack enemy player closest to this bot -
                                    else if (closestEnemyPlayerScript)
                                    {
                                        rotationTargetState = CLOSEST_ENEMY_TARGET;
                                    }
                                    //! - Otherwise aim for enemy goal
                                    else
                                    {
                                        if (botPlayerScript.team == 0)
                                        {
                                            rotationTargetState = TEAM_B_GOAL_TARGET;
                                        }
                                        else
                                        {
                                            rotationTargetState = TEAM_A_GOAL_TARGET;
                                        }
                                    }
                                }
                            }
                        }
                        //! - Bot is closer to goal than player with ball -
                        else
                        {
                            lookToThrowDodgeball = true;
                            //! - Attack enemy player closest to this bot only if they are close -
                            if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                                !closestEnemyPlayerScript.playerRecovering &&
                                Vector3.Distance(transform.position, closestEnemyPlayerScript.transform.position) < 20f)
                            {
                                movingTargetState = CLOSEST_ENEMY_TARGET;
                                rotationTargetState = CLOSEST_ENEMY_TARGET;
                            }
                            //! - Otherwise push up to goal -
                            else
                            {
                                //! - Move towards goal
                                if (botPlayerScript.team == 0)
                                {
                                    movingTargetState = TEAM_B_GOAL_TARGET;
                                }
                                else
                                {
                                    movingTargetState = TEAM_A_GOAL_TARGET;
                                }
                                rotationTargetState = GAMEBALL_TARGET;
                            }
                            if (botPlayerScript.heldEntity)
                            {
                                if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                                {
                                    if (botPlayerScript.team == 0)
                                    {
                                        rotationTargetState = TEAM_B_GOAL_TARGET;
                                    }
                                    else
                                    {
                                        rotationTargetState = TEAM_A_GOAL_TARGET;
                                    }
                                }
                                else if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                                {
                                    if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                                    {
                                        if (botPlayerScript.team == 0)
                                        {
                                            rotationTargetState = TEAM_B_GOAL_TARGET;
                                        }
                                        else
                                        {
                                            rotationTargetState = TEAM_A_GOAL_TARGET;
                                        }
                                    }
                                    else
                                    {
                                        //! - Move towards gameball and aim enemy away from gameball to keep them away -
                                        rotationTargetState = AWAY_FROM_GAMEBALL_TARGET;
                                    }
                                }
                                else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                                {
                                    //! - Attack enemy player closest to gameball -
                                    if (FindEnemyClosestToGameball())
                                    {
                                        rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                                    }
                                    //! - Attack enemy player closest to this bot -
                                    else if (closestEnemyPlayerScript)
                                    {
                                        rotationTargetState = CLOSEST_ENEMY_TARGET;
                                    }
                                    //! - Otherwise aim for enemy goal
                                    else
                                    {
                                        if (botPlayerScript.team == 0)
                                        {
                                            rotationTargetState = TEAM_B_GOAL_TARGET;
                                        }
                                        else
                                        {
                                            rotationTargetState = TEAM_A_GOAL_TARGET;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        //! - Enemy has the ball -
        else
        {
            //todo movement
            if (FindTeammateClosestToGameball() && FindTeammateClosestToGameball() == botPlayerScript)
            {
                //! - If the closest team player to gamball, go for the gameball
                movingTargetState = GAMEBALL_TARGET;
                lookToThrowDodgeball = false;
                if (!botPlayerScript.heldEntity)
                {
                    rotationTargetState = GAMEBALL_TARGET;
                }
                else
                {
                    //! - Holding Gameball - //codeReview: can you hold ball if in "enemy has ball" section
                    if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                    {
                        //! - Enemy nearby -
                        if (FindEnemyClosestToGameball() && (FindEnemyClosestToGameball().
                            transform.position - transform.position).magnitude < 10f)
                        {
                            //! - Move away from closest player -
                            movingTargetState = AWAY_FROM_CLOSEST_ENEMY_TARGET_TO_GAMEBALL;
                        }
                        //! - No enemies nearby -
                        else
                        {
                            if (timeStartedCharge != 0f && timeStartedCharge + MAX_CHARGE_TIME - 0.3f > Time.time)
                            {
                                //! - Move towards goal -
                                if (botPlayerScript.team == 0)
                                {
                                    movingTargetState = TEAM_B_GOAL_TARGET;
                                }
                                else
                                {
                                    movingTargetState = TEAM_A_GOAL_TARGET;
                                }
                            }
                            else
                            {
                                movingTargetState = ZERO_TARGET;
                            }
                        }
                        if (teammates.Count > 2 && FindTeammateToPassAsFielder())
                        {
                            //! - Look for a teammate to pass to -
                            rotationTargetState = TEAMMATE_TARGET;
                        }
                        else
                        {
                            //! - Rotate to look at opposition goal -
                            if (botPlayerScript.team == 0)
                            {
                                rotationTargetState = TEAM_B_GOAL_TARGET;
                            }
                            else
                            {
                                rotationTargetState = TEAM_A_GOAL_TARGET;
                            }
                        }
                    }
                    //! - Holding another Player -
                    else if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                    {
                        movingTargetState = GAMEBALL_TARGET;
                        //! - Holding teammate -
                        if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                        {
                            //! - Aim teammate towards gameball -
                            rotationTargetState = GAMEBALL_TARGET;
                        }
                        else
                        {
                            rotationTargetState = AWAY_FROM_GAMEBALL_TARGET;
                        }
                    }
                    //! - Holding Dodgeball - //codeReview: holding dodgeball basically identical, but multiple copies 
                    else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                    {
                        movingTargetState = GAMEBALL_TARGET;
                        //! - Attack enemy player closest to gameball -
                        if (FindEnemyClosestToGameball())
                        {
                            rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                        }
                        //! - Attack enemy player closest to this bot -
                        else if (closestEnemyPlayerScript)
                        {
                            rotationTargetState = CLOSEST_ENEMY_TARGET;
                        }
                        //! - Otherwise aim for enemy goal
                        else
                        {
                            if (botPlayerScript.team == 0)
                            {
                                rotationTargetState = TEAM_B_GOAL_TARGET;
                            }
                            else
                            {
                                rotationTargetState = TEAM_A_GOAL_TARGET;
                            }
                        }
                    }
                }
            }
            //! - Otherwise push back to own goal
            else
            {
                //! - gameball is in offensive half -
                if ((botPlayerScript.team == 0 && gameball.transform.position.z > 0)
                    || (botPlayerScript.team == 1 && gameball.transform.position.z < 0))
                {
                    lookToThrowDodgeball = true;
                    if (FindEnemyClosestToGameball())
                    {
                        movingTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                        rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                    }
                    else
                    {
                        movingTargetState = GAMEBALL_TARGET;
                        rotationTargetState = GAMEBALL_TARGET;
                    }
                    if (botPlayerScript.heldEntity)
                    {
                        //! - Holding Gameball - //codeReview: gameball cant be held if in enemy has ball section
                        if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                        {
                            //! - Enemy nearby -
                            if (FindEnemyClosestToGameball() && (FindEnemyClosestToGameball().
                                transform.position - transform.position).magnitude < 10f)
                            {
                                //! - Move away from closest player -
                                movingTargetState = AWAY_FROM_CLOSEST_ENEMY_TARGET_TO_GAMEBALL;
                            }
                            //! - No enemies nearby -
                            else
                            {
                                if (timeStartedCharge != 0f && timeStartedCharge + MAX_CHARGE_TIME - 0.3f > Time.time)
                                {
                                    //! - Move towards goal -
                                    if (botPlayerScript.team == 0)
                                    {
                                        movingTargetState = TEAM_B_GOAL_TARGET;
                                    }
                                    else
                                    {
                                        movingTargetState = TEAM_A_GOAL_TARGET;
                                    }
                                }
                                else
                                {
                                    movingTargetState = ZERO_TARGET;
                                }
                            }
                            if (teammates.Count > 2 && FindTeammateToPassAsFielder())
                            {
                                //! - Look for a teammate to pass to -
                                rotationTargetState = TEAMMATE_TARGET;
                            }
                            else
                            {
                                //! - Rotate to look at opposition goal -
                                if (botPlayerScript.team == 0)
                                {
                                    rotationTargetState = TEAM_B_GOAL_TARGET;
                                }
                                else
                                {
                                    rotationTargetState = TEAM_A_GOAL_TARGET;
                                }
                            }
                        }
                        //! - Holding another Player -
                        else if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                        {
                            movingTargetState = GAMEBALL_TARGET;
                            //! - Holding teammate -
                            if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                            {
                                //! - Aim teammate towards gameball -
                                rotationTargetState = GAMEBALL_TARGET;
                            }
                            else
                            {
                                rotationTargetState = AWAY_FROM_GAMEBALL_TARGET;
                            }
                        }
                        //! - Holding Dodgeball -
                        else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                        {
                            movingTargetState = GAMEBALL_TARGET;
                            //! - Attack enemy player closest to gameball -
                            if (FindEnemyClosestToGameball())
                            {
                                rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                            }
                            //! - Attack enemy player closest to this bot -
                            else if (closestEnemyPlayerScript)
                            {
                                rotationTargetState = CLOSEST_ENEMY_TARGET;
                            }
                            //! - Otherwise aim for enemy goal
                            else
                            {
                                if (botPlayerScript.team == 0)
                                {
                                    rotationTargetState = TEAM_B_GOAL_TARGET;
                                }
                                else
                                {
                                    rotationTargetState = TEAM_A_GOAL_TARGET;
                                }
                            }
                        }
                    }
                }
                //! - gameball is in defensive half -
                else
                {
                    lookToThrowDodgeball = true;
                    if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                        !closestEnemyPlayerScript.playerRecovering &&
                        Vector3.Distance(transform.position, closestEnemyPlayerScript.transform.position) < 20f)
                    {
                        movingTargetState = CLOSEST_ENEMY_TARGET;
                        rotationTargetState = CLOSEST_ENEMY_TARGET;
                    }
                    else
                    {
                        movingTargetState = GAMEBALL_TARGET;
                        rotationTargetState = GAMEBALL_TARGET;
                    }
                    if (botPlayerScript.heldEntity)
                    {
                        //! - Holding Gameball - //codeReview: enemy has ball
                        if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                        {
                            //! - Enemy nearby -
                            if (FindEnemyClosestToGameball() && (FindEnemyClosestToGameball().
                                transform.position - transform.position).magnitude < 10f)
                            {
                                //! - Move away from closest player -
                                movingTargetState = AWAY_FROM_CLOSEST_ENEMY_TARGET_TO_GAMEBALL;
                            }
                            //! - No enemies nearby -
                            else
                            {
                                if (timeStartedCharge != 0f && timeStartedCharge + MAX_CHARGE_TIME - 0.3f > Time.time)
                                {
                                    //! - Move towards goal -
                                    if (botPlayerScript.team == 0)
                                    {
                                        movingTargetState = TEAM_B_GOAL_TARGET;
                                    }
                                    else
                                    {
                                        movingTargetState = TEAM_A_GOAL_TARGET;
                                    }
                                }
                                else
                                {
                                    movingTargetState = ZERO_TARGET;
                                }
                            }
                            if (teammates.Count > 2 && FindTeammateToPassAsFielder())
                            {
                                //! - Look for a teammate to pass to -
                                rotationTargetState = TEAMMATE_TARGET;
                            }
                            else
                            {
                                //! - Rotate to look at opposition goal -
                                if (botPlayerScript.team == 0)
                                {
                                    rotationTargetState = TEAM_B_GOAL_TARGET;
                                }
                                else
                                {
                                    rotationTargetState = TEAM_A_GOAL_TARGET;
                                }
                            }
                        }
                        //! - Holding another Player -
                        else if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                        {
                            movingTargetState = GAMEBALL_TARGET;
                            //! - Holding teammate -
                            if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                            {
                                //! - Aim teammate towards gameball -
                                rotationTargetState = GAMEBALL_TARGET;
                            }
                            else
                            {
                                rotationTargetState = AWAY_FROM_GAMEBALL_TARGET;
                            }
                        }
                        //! - Holding Dodgeball -
                        else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                        {
                            movingTargetState = GAMEBALL_TARGET;
                            //! - Attack enemy player closest to gameball -
                            if (FindEnemyClosestToGameball())
                            {
                                rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                            }
                            //! - Attack enemy player closest to this bot -
                            else if (closestEnemyPlayerScript)
                            {
                                rotationTargetState = CLOSEST_ENEMY_TARGET;
                            }
                            //! - Otherwise aim for enemy goal
                            else
                            {
                                if (botPlayerScript.team == 0)
                                {
                                    rotationTargetState = TEAM_B_GOAL_TARGET;
                                }
                                else
                                {
                                    rotationTargetState = TEAM_A_GOAL_TARGET;
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    void HandleBotTargetOnlyThisBotOnTeam()
    {
        //! - Team goalie has the ball -
        if (teamGoalieHasBall)
        {
            //! - Push up and attack enemy player if they are close -
            if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                !closestEnemyPlayerScript.playerRecovering &&
                Vector3.Distance(transform.position, closestEnemyPlayerScript.transform.position) < 50f)
            {
                if (botPlayerScript.team == 0)
                {
                    movingTargetState = TEAM_B_GOAL_TARGET;
                }
                else
                {
                    movingTargetState = TEAM_A_GOAL_TARGET;
                }
                rotationTargetState = CLOSEST_ENEMY_TARGET;
                lookToThrowDodgeball = true;
            }
            //! - Move towards goal anticipating pass -
            else
            {
                if (botPlayerScript.team == 0)
                {
                    movingTargetState = TEAM_B_GOAL_TARGET;
                }
                else
                {
                    movingTargetState = TEAM_A_GOAL_TARGET;
                }
                rotationTargetState = GAMEBALL_TARGET;
                lookToThrowDodgeball = false;
            }
            if (botPlayerScript.heldEntity)
            {
                if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                {
                    //! - If holding team player -
                    if (botPlayerScript.team == botPlayerScript.heldEntity.GetComponent<PlayerScript>().team)
                    {
                        rotationTargetState = GAMEBALL_TARGET;
                    }
                    //! - If holding enemy player -
                    else
                    {
                        if (botPlayerScript.team == 0)
                        {
                            rotationTargetState = TEAM_A_GOAL_TARGET;
                        }
                        else
                        {
                            rotationTargetState = TEAM_B_GOAL_TARGET;
                        }
                    }
                }
                else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                {
                    //! - Attack enemy player closest to gameball -
                    if (FindEnemyClosestToGameball())
                    {
                        rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                    }
                    //! - Attack enemy player closest to this bot -
                    else if (closestEnemyPlayerScript)
                    {
                        rotationTargetState = CLOSEST_ENEMY_TARGET;
                    }
                    //! - Otherwise aim for enemy goal
                    else
                    {
                        if (botPlayerScript.team == 0)
                        {
                            rotationTargetState = TEAM_B_GOAL_TARGET;
                        }
                        else
                        {
                            rotationTargetState = TEAM_A_GOAL_TARGET;
                        }
                    }
                }
            }
        }
        //! - Enemy goalie has the ball -
        else if (enemyGoalieHasBall)
        {
            //! - Attack enemy player closest to this bot only if they are close -
            if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                !closestEnemyPlayerScript.playerRecovering &&
                Vector3.Distance(transform.position, closestEnemyPlayerScript.transform.position) < 50f)
            {
                movingTargetState = CLOSEST_ENEMY_TARGET;
                rotationTargetState = CLOSEST_ENEMY_TARGET;
                lookToThrowDodgeball = true;
            }
            //! - Move towards goal -
            else
            {
                //! - Fall back -
                if (botPlayerScript.team == 0)
                {
                    movingTargetState = TEAM_A_GOAL_TARGET;
                }
                else
                {
                    movingTargetState = TEAM_B_GOAL_TARGET;
                }
                rotationTargetState = closestEnemyPlayerScript ? CLOSEST_ENEMY_TARGET : GAMEBALL_TARGET;
                lookToThrowDodgeball = true;
            }
            if (botPlayerScript.heldEntity)
            {
                if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                {
                    //! - If holding team player -
                    if (botPlayerScript.team == botPlayerScript.heldEntity.GetComponent<PlayerScript>().team)
                    {
                        if (botPlayerScript.team == 0)
                        {
                            rotationTargetState = TEAM_A_GOAL_TARGET;
                        }
                        else
                        {
                            rotationTargetState = TEAM_B_GOAL_TARGET;
                        }
                    }
                    //! - If holding enemy player -
                    else
                    {
                        if (botPlayerScript.team == 0)
                        {
                            rotationTargetState = TEAM_B_GOAL_TARGET;
                        }
                        else
                        {
                            rotationTargetState = TEAM_A_GOAL_TARGET;
                        }
                    }
                }
                else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                {
                    //! - Attack enemy player closest to gameball -
                    if (FindEnemyClosestToGameball())
                    {
                        rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                    }
                    //! - Attack enemy player closest to this bot -
                    else if (closestEnemyPlayerScript)
                    {
                        rotationTargetState = CLOSEST_ENEMY_TARGET;
                    }
                    //! - Otherwise aim for enemy goal
                    else
                    {
                        if (botPlayerScript.team == 0)
                        {
                            rotationTargetState = TEAM_B_GOAL_TARGET;
                        }
                        else
                        {
                            rotationTargetState = TEAM_A_GOAL_TARGET;
                        }
                    }
                }
            }
        }
        //! - No clear possession -
        else if (possessionState == NO_POSSESSION)
        {
            //! - If no teammates always go for ball during no possession -
            if (!botPlayerScript.heldEntity)
            {
                //! - Move towards gameball -
                movingTargetState = GAMEBALL_TARGET;
                rotationTargetState = GAMEBALL_TARGET;

                //! - Find enemy position to compare distance to ball with this bot -
                Vector3 targetEnemyPosition = Vector3.zero;
                if (FindEnemyClosestToGameball())
                {
                    targetEnemyPosition = FindEnemyClosestToGameball().transform.position;
                }
                else if (closestEnemyPlayerScript)
                {
                    targetEnemyPosition = closestEnemyPlayerScript.transform.position;
                }
                else
                {
                    targetEnemyPosition = 1000f * Vector3.forward; //! So that it's always further than this bot if no enemy exists
                }

                //! - Enemy is closer to gameball than this bot -
                if (Vector3.Distance(transform.position, gameball.transform.position) >
                    Vector3.Distance(targetEnemyPosition, gameball.transform.position))
                {
                    lookToThrowDodgeball = true;
                }
                //! - This bot is closer to gameball than enemy -
                else
                {
                    //! - If still far from gameball attack enemy -
                    if (Vector3.Distance(transform.position, gameball.transform.position) > 10f)
                    {
                        lookToThrowDodgeball = true;
                    }
                }
            }
            else
            {
                //! - Has the gameball -
                if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                {
                    //! - Enemy nearby -
                    if (FindEnemyClosestToGameball() && (FindEnemyClosestToGameball().
                        transform.position - transform.position).magnitude < 10f)
                    {
                        //! - Move away from closest player -
                        movingTargetState = AWAY_FROM_CLOSEST_ENEMY_TARGET_TO_GAMEBALL;
                    }
                    //! - No enemies nearby -
                    else
                    {
                        //! - Move towards goal -
                        if (botPlayerScript.team == 0)
                        {
                            movingTargetState = TEAM_B_GOAL_TARGET;
                        }
                        else
                        {
                            movingTargetState = TEAM_A_GOAL_TARGET;
                        }
                    }
                    //! - Aim for enemy goal -
                    if (botPlayerScript.team == 0)
                    {
                        rotationTargetState = TEAM_B_GOAL_TARGET;
                    }
                    else
                    {
                        rotationTargetState = TEAM_A_GOAL_TARGET;
                    }
                }
                //! - Holding another Player -
                else if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                {
                    movingTargetState = GAMEBALL_TARGET;
                    //! - Holding teammate -
                    if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                    {
                        //! - Move towards gameball and aim teammate towards gameball -
                        rotationTargetState = GAMEBALL_TARGET;
                    }
                    else
                    {
                        //! - Move towards gameball and aim enemy away from gameball to keep them away -
                        rotationTargetState = AWAY_FROM_GAMEBALL_TARGET;
                    }
                }
                //! - Holding Dodgeball -
                else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                {
                    movingTargetState = GAMEBALL_TARGET;
                    //! - Attack enemy player closest to gameball -
                    if (FindEnemyClosestToGameball())
                    {
                        rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                    }
                    //! - Attack enemy player closest to this bot -
                    else if (closestEnemyPlayerScript)
                    {
                        rotationTargetState = CLOSEST_ENEMY_TARGET;
                    }
                    //! - Otherwise aim for enemy goal
                    else
                    {
                        if (botPlayerScript.team == 0)
                        {
                            rotationTargetState = TEAM_B_GOAL_TARGET;
                        }
                        else
                        {
                            rotationTargetState = TEAM_A_GOAL_TARGET;
                        }
                    }
                }
            }
        }
        //! - Team has the ball -
        else if (possessionState == TEAM_POSSESSION)
        {
            //! - If no teammates always go for ball during no possession -
            if (!botPlayerScript.heldEntity)
            {
                //! - Move towards gameball -
                movingTargetState = GAMEBALL_TARGET;
                rotationTargetState = GAMEBALL_TARGET;

                //! - Find enemy position to compare distance to ball with this bot -
                Vector3 targetEnemyPosition = Vector3.zero;
                if (FindEnemyClosestToGameball())
                {
                    targetEnemyPosition = FindEnemyClosestToGameball().transform.position;
                }
                else if (closestEnemyPlayerScript)
                {
                    targetEnemyPosition = closestEnemyPlayerScript.transform.position;
                }
                else
                {
                    targetEnemyPosition = 1000f * Vector3.forward; //! So that it's always further than this bot if no enemy exists
                }

                //! - Enemy is closer to gameball than this bot -
                if (Vector3.Distance(transform.position, gameball.transform.position) >
                    Vector3.Distance(targetEnemyPosition, gameball.transform.position))
                {
                    lookToThrowDodgeball = true;
                }
                //! - This bot is closer to gameball than enemy -
                else
                {
                    //! - If still far from gameball attack enemy -
                    if (Vector3.Distance(transform.position, gameball.transform.position) > 10f)
                    {
                        lookToThrowDodgeball = true;
                    }
                }
            }
            else
            {
                //! - Has the gameball -
                if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                {
                    //! - Enemy nearby -
                    if (FindEnemyClosestToGameball() && (FindEnemyClosestToGameball().
                        transform.position - transform.position).magnitude < 10f)
                    {
                        //! - Move away from closest player -
                        movingTargetState = AWAY_FROM_CLOSEST_ENEMY_TARGET_TO_GAMEBALL;
                    }
                    //! - No enemies nearby -
                    else
                    {
                        if (timeStartedCharge != 0f && timeStartedCharge + MAX_CHARGE_TIME - 0.3f > Time.time)
                        {
                            //! - Move towards goal -
                            if (botPlayerScript.team == 0)
                            {
                                movingTargetState = TEAM_B_GOAL_TARGET;
                            }
                            else
                            {
                                movingTargetState = TEAM_A_GOAL_TARGET;
                            }
                        }
                        else
                        {
                            movingTargetState = ZERO_TARGET;
                        }
                    }
                    //! - Rotate to look at opposition goal -
                    if (botPlayerScript.team == 0)
                    {
                        rotationTargetState = TEAM_B_GOAL_TARGET;
                    }
                    else
                    {
                        rotationTargetState = TEAM_A_GOAL_TARGET;
                    }
                }
                //! - Holding another Player -
                else if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                {
                    movingTargetState = GAMEBALL_TARGET;
                    //! - Holding teammate -
                    if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                    {
                        //! - Aim teammate towards gameball -
                        rotationTargetState = GAMEBALL_TARGET;
                    }
                    else
                    {
                        //! - Aim enemy away to oppsite goal to keep them away -
                        if (botPlayerScript.team == 0)
                        {
                            rotationTargetState = TEAM_A_GOAL_TARGET;
                        }
                        else
                        {
                            rotationTargetState = TEAM_B_GOAL_TARGET;
                        }
                    }
                }
                //! - Holding Dodgeball -
                else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                {
                    movingTargetState = GAMEBALL_TARGET;
                    //! - Attack enemy player closest to gameball -
                    if (FindEnemyClosestToGameball())
                    {
                        rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                    }
                    //! - Attack enemy player closest to this bot -
                    else if (closestEnemyPlayerScript)
                    {
                        rotationTargetState = CLOSEST_ENEMY_TARGET;
                    }
                    //! - Otherwise aim for enemy goal
                    else
                    {
                        if (botPlayerScript.team == 0)
                        {
                            rotationTargetState = TEAM_B_GOAL_TARGET;
                        }
                        else
                        {
                            rotationTargetState = TEAM_A_GOAL_TARGET;
                        }
                    }
                }
            }
        }
        //! - Enemy has the ball -
        else
        {
            if (botPlayerScript.heldEntity)
            {
                //! - Holding Gameball -
                if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                {
                    //! - Enemy nearby -
                    if (FindEnemyClosestToGameball() && (FindEnemyClosestToGameball().
                        transform.position - transform.position).magnitude < 10f)
                    {
                        //! - Move away from closest player -
                        movingTargetState = AWAY_FROM_CLOSEST_ENEMY_TARGET_TO_GAMEBALL;
                    }
                    //! - No enemies nearby -
                    else
                    {
                        if (timeStartedCharge != 0f && timeStartedCharge + MAX_CHARGE_TIME - 0.3f > Time.time)
                        {
                            //! - Move towards goal -
                            if (botPlayerScript.team == 0)
                            {
                                movingTargetState = TEAM_B_GOAL_TARGET;
                            }
                            else
                            {
                                movingTargetState = TEAM_A_GOAL_TARGET;
                            }
                        }
                        else
                        {
                            movingTargetState = ZERO_TARGET;
                        }
                    }
                    if (teammates.Count > 2 && FindTeammateToPassAsFielder())
                    {
                        //! - Look for a teammate to pass to -
                        rotationTargetState = TEAMMATE_TARGET;
                    }
                    else
                    {
                        //! - Rotate to look at opposition goal -
                        if (botPlayerScript.team == 0)
                        {
                            rotationTargetState = TEAM_B_GOAL_TARGET;
                        }
                        else
                        {
                            rotationTargetState = TEAM_A_GOAL_TARGET;
                        }
                    }
                }
                //! - Holding another Player -
                else if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                {
                    movingTargetState = GAMEBALL_TARGET;
                    //! - Holding teammate -
                    if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                    {
                        //! - Aim teammate towards gameball -
                        rotationTargetState = GAMEBALL_TARGET;
                    }
                    else
                    {
                        rotationTargetState = AWAY_FROM_GAMEBALL_TARGET;
                    }
                }
                //! - Holding Dodgeball -
                else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                {
                    movingTargetState = GAMEBALL_TARGET;
                    //! - Attack enemy player closest to gameball -
                    if (FindEnemyClosestToGameball())
                    {
                        rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                    }
                    //! - Attack enemy player closest to this bot -
                    else if (closestEnemyPlayerScript)
                    {
                        rotationTargetState = CLOSEST_ENEMY_TARGET;
                    }
                    //! - Otherwise aim for enemy goal
                    else
                    {
                        if (botPlayerScript.team == 0)
                        {
                            rotationTargetState = TEAM_B_GOAL_TARGET;
                        }
                        else
                        {
                            rotationTargetState = TEAM_A_GOAL_TARGET;
                        }
                    }
                }
            }
            else
            {
                lookToThrowDodgeball = true;
                if (Vector3.Distance(transform.position, gameball.transform.position) < 20f)
                {
                    //! - If the closest team player to gamball, go for the gameball
                    movingTargetState = GAMEBALL_TARGET;
                    rotationTargetState = GAMEBALL_TARGET;
                }
                else
                {
                    //! - gameball is in offensive half -
                    if ((botPlayerScript.team == 0 && gameball.transform.position.z > 0)
                        || (botPlayerScript.team == 1 && gameball.transform.position.z < 0))
                    {
                        if (FindEnemyClosestToGameball())
                        {
                            movingTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                            rotationTargetState = CLOSEST_ENEMY_TO_GAMEBALL_TARGET;
                        }
                        else
                        {
                            movingTargetState = GAMEBALL_TARGET;
                            rotationTargetState = GAMEBALL_TARGET;
                        }
                    }
                    //! - gameball is in defensive half -
                    else
                    {
                        if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                            !closestEnemyPlayerScript.playerRecovering &&
                            Vector3.Distance(transform.position, closestEnemyPlayerScript.transform.position) < 20f)
                        {
                            movingTargetState = CLOSEST_ENEMY_TARGET;
                            rotationTargetState = CLOSEST_ENEMY_TARGET;
                        }
                        else
                        {
                            movingTargetState = GAMEBALL_TARGET;
                            rotationTargetState = GAMEBALL_TARGET;
                        }
                    }
                }
            }
        }
    }
    void HandleMoving()
    {
        Vector3 dirToTarget = Vector3.zero;
        Vector3 targetPosition = Vector3.zero;
        Vector2 dirToTargetInput;
        float distance;
        switch (movingTargetState)
        {
            case GAMEBALL_TARGET:
                //! - Move towards gameball -
                distance = Vector3.Distance(gameball.transform.position, transform.position);
                targetPosition = PredictPosition(gameball, distance, botPlayerScript.isOnGround ? PLAYER_SPEED : PLAYER_AIR_SPEED);
                dirToTarget = transform.InverseTransformDirection(targetPosition - transform.position);
                break;
            case AWAY_FROM_GAMEBALL_TARGET:
                //! - Move away from gameball -
                targetPosition = gameball.transform.position;
                dirToTarget = transform.InverseTransformDirection(targetPosition - transform.position);
                break;
            case TEAM_A_GOAL_TARGET:
                //! - Move to Team A Goal -
                targetPosition = gameManager.goalHoleA.transform.position + Vector3.forward * 80f;
                dirToTarget = transform. InverseTransformDirection(targetPosition - transform.position);
                break;
            case TEAM_B_GOAL_TARGET:
                //! - Move to Team B Goal -
                targetPosition = gameManager.goalHoleB.transform.position - Vector3.forward * 80f;
                dirToTarget = transform.InverseTransformDirection(targetPosition - transform.position);
                break;
            case TEAMMATE_TARGET:
                //! - Move to Teammate (not used) -
                targetPosition = FindTeammateToPassAsFielder().transform.position;
                dirToTarget = transform.InverseTransformDirection(targetPosition - transform.position);
                break;
            case CLOSEST_ENEMY_TARGET:
                //! - Move to closest enemy -
                distance = Vector3.Distance(closestEnemyPlayerScript.transform.position, transform.position);
                targetPosition = PredictPosition(closestEnemyPlayerScript.gameObject, distance,
                    botPlayerScript.isOnGround ? PLAYER_SPEED : PLAYER_AIR_SPEED);
                dirToTarget = transform.InverseTransformDirection(targetPosition - transform.position);
                break;
            case CLOSEST_ENEMY_TO_GAMEBALL_TARGET:
                //! - Move to closest enemy to gameball -
                distance = Vector3.Distance(FindEnemyClosestToGameball().transform.position, transform.position);
                targetPosition = PredictPosition(FindEnemyClosestToGameball().gameObject, distance,
                    botPlayerScript.isOnGround ? PLAYER_SPEED : PLAYER_AIR_SPEED);
                dirToTarget = transform.InverseTransformDirection(targetPosition - transform.position);
                break;
            //! - Move away from closest enemy target -
            case AWAY_FROM_CLOSEST_ENEMY_TARGET_TO_GAMEBALL:
                targetPosition = Vector3.zero;
                dirToTarget = transform.InverseTransformDirection(transform.position - FindEnemyClosestToGameball().transform.position);
                break;
            case FORWARD_TARGET:
                //! - Move player forward (not used) - 
                targetPosition = Vector3.zero;
                dirToTarget = transform.forward;
                break;
            case ZERO_TARGET:
                //! - Dont move -
                targetPosition = Vector3.zero;
                dirToTarget = Vector3.zero;
                break;
        }
        if (targetPosition != Vector3.zero && Vector3.Distance(transform.position, targetPosition) < 10f)
        {
            //! - Slow down the movement input if bot is lose to moving target -
            dirToTarget = (Vector3.Distance(transform.position, dirToTarget) / 10f) * dirToTarget;
        }
        dirToTargetInput = new Vector2(dirToTarget.x, dirToTarget.z).normalized;
        botPlayerScript.SimulateInputLS(dirToTargetInput);
    }
    void HandleRotation()
    {
        Vector3 dirToTarget = transform.forward;
        float yDistFromGoal;
        float distance;
        switch (rotationTargetState)
        {
            case GAMEBALL_TARGET:
                //! - Rotate towards gameball -
                dirToTarget = (gameball.transform.position - transform.position).normalized;
                break;

            case AWAY_FROM_GAMEBALL_TARGET:
                //! - Rotate away from gameball -
                dirToTarget = (gameball.transform.position - transform.position).normalized;
                break;

            case TEAM_A_GOAL_TARGET:
                //! - Rotate towards Team A Goal -
                angleOffset = 20f;
                dirToTarget = gameManager.goalHoleA.transform.position - transform.position;
                yDistFromGoal = transform.position.y - gameManager.goalHoleA.transform.position.y;
                if (yDistFromGoal > 5)
                {
                    angleOffset = 0f;
                }
                //! - Vertical angle offset -
                if (dirToTarget.magnitude < 20f)
                {
                    angleOffset = 70f;
                }
                else if (dirToTarget.magnitude < 10f)
                {
                    angleOffset = 90f;
                }
                //! - Horizontal angle offset -
                if (sideAngleOffset == 0f)
                {
                    int multiplier = Random.value >= 0.5f ? -1 : 1;
                    sideAngleOffset = 10f * (1 - dirToTarget.magnitude / 250f) * multiplier;
                }
                dirToTarget = dirToTarget.normalized;
                dirToTarget = transform.InverseTransformDirection(dirToTarget);
                dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, sideAngleOffset, 0f))
                    * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                dirToTarget = transform.TransformDirection(dirToTarget);
                break;

            case TEAM_B_GOAL_TARGET:
                //! - Rotate towards Team B Goal -
                angleOffset = -20f;
                dirToTarget = gameManager.goalHoleB.transform.position - transform.position;
                yDistFromGoal = transform.position.y - gameManager.goalHoleB.transform.position.y;
                if (yDistFromGoal > 5)
                {
                    angleOffset = 0f;
                }
                //! - Vertical angle offset -
                if (dirToTarget.magnitude < 20f)
                {
                    angleOffset = -70f;
                }
                else if (dirToTarget.magnitude < 10f)
                {
                    angleOffset = -90f;
                }
                //! - Horizontal angle offset -
                if (sideAngleOffset == 0f)
                {
                    int multiplier = Random.value >= 0.5f ? -1 : 1;
                    sideAngleOffset = 10f * (1 - dirToTarget.magnitude / 250f) * multiplier;
                }
                dirToTarget = dirToTarget.normalized;
                dirToTarget = transform.InverseTransformDirection(dirToTarget);
                dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, sideAngleOffset, 0f))
                    * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                dirToTarget = transform.TransformDirection(dirToTarget);
                break;

            case TEAMMATE_TARGET:
                //! - Rotate towards Teammate -
                distance = Vector3.Distance(transform.position, FindTeammateToPassAsFielder().transform.position);
                Vector3 angleAdjust = Vector3.zero;
                if (distance > 50f)
                {
                    angleAdjust = Vector3.up * (Mathf.Pow(distance - 50f, 1.1f) / 4f);
                }
                dirToTarget = (PredictPosition(FindTeammateToPassAsFielder().gameObject, distance, GAMEBALL_SPEED)
                    + angleAdjust - transform.position).normalized;
                break;

            case CLOSEST_ENEMY_TARGET:
                //! - Rotate towards closest enemy -
                if (EnemyWithGameballWithinRange(100f))
                {
                    distance = Vector3.Distance(EnemyWithGameballWithinRange(100f).transform.position, transform.position);
                    dirToTarget = PredictPosition(EnemyWithGameballWithinRange(100f).gameObject, distance, DODGEBALL_SPEED)
                        - transform.position;
                    //dirToTarget = EnemyWithGameballWithinRange(100f).transform.position + transform.forward * 5f - transform.position;
                }
                else if (closestEnemyPlayerScript)
                {
                    distance = Vector3.Distance(closestEnemyPlayerScript.transform.position, transform.position);
                    dirToTarget = PredictPosition(closestEnemyPlayerScript.gameObject, distance, DODGEBALL_SPEED)
                        - transform.position;
                    //dirToTarget = closestEnemyPlayerScript.transform.position * 5f - transform.position;
                }
                dirToTarget = dirToTarget.normalized;
                break;

            case CLOSEST_ENEMY_TO_GAMEBALL_TARGET:
                //! - Rotate towards closest enemy to gameball -
                distance = Vector3.Distance(FindEnemyClosestToGameball().transform.position, transform.position);
                dirToTarget = PredictPosition(FindEnemyClosestToGameball().gameObject, distance, DODGEBALL_SPEED)
                    - transform.position;
                //dirToTarget = FindEnemyClosestToGameball().transform.position - transform.right * 10f - transform.position;
                dirToTarget = dirToTarget.normalized;
                break;

            case AWAY_FROM_CLOSEST_ENEMY_TARGET_TO_GAMEBALL:
                //! - Rotate away from closest enemy target (not used) -
                dirToTarget = (transform.position - FindEnemyClosestToGameball().transform.position).normalized;
                break;

            case FORWARD_TARGET:
                //! - Rotate to player forward - 
                dirToTarget = transform.forward;
                break;

            case ZERO_TARGET:
                //! - Dont rotate -
                dirToTarget = Vector3.zero;
                break;
        }

        Vector3 rotationDir = transform.InverseTransformDirection(dirToTarget - botPlayerScript.sceneVCamScript.ReturnForward());
        //! - Helps rotation when target is behind character -
        if (Vector3.Angle(botPlayerScript.sceneVCamScript.ReturnForward(), dirToTarget) > 135f)
        {
            rotationDir.x = 1;
        }
        Vector2 dirToGameballInput = new Vector2(rotationDir.x, rotationDir.y);
        botPlayerScript.SimulateInputRS(dirToGameballInput);
        //! - Throw ball if rotation completed -
        if (botPlayerScript.heldEntity && timeStartedCharge != 0f &&
            (timeStartedCharge + MIN_CHARGE_TIME * 2f) < Time.time && rotationDir.magnitude < 0.01f)
        {
            FinishCharge();
        }
    }
    void MoveAsFielder()
    {
        Vector3 dirToTarget;
        Vector3 targetPosition;
        Vector2 dirToTargetInput;
        //! - Regular movement -
        if (!teamGoalieHasBall)
        {
            //! - Testing new bot movement changed 03/05 -
            if (gameManager.newBotMovement)
            {
                //! - Neither team has clear possession -
                if (gameManager.botManager.possessionState == 0)
                {
                    //! - If no teammates always go for ball during no possession -
                    //! - This bot is the bot closest on team to the gameball -
                    if (teammates.Count == 1 || (FindTeammateClosestToGameball() && FindTeammateClosestToGameball() == botPlayerScript))
                    {
                        //! - If no possession of the gameball go for the gameball -
                        if (!botPlayerScript.heldEntity)
                        {
                            //! - Move towards gameball -
                            targetPosition = gameball.transform.position;
                            dirToTarget = transform.InverseTransformDirection(targetPosition - transform.position);
                        }
                        //! - Holding an object -
                        else
                        {
                            //! - Has the gameball -
                            if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                            {
                                //! - Run from the closest non-knocked or non-recovering enemy player if they are close -
                                if (FindEnemyClosestToGameball() &&
                                    (FindEnemyClosestToGameball().transform.position - transform.position).magnitude < 20f)
                                {
                                    //! - Move away from closest player -
                                    targetPosition = FindEnemyClosestToGameball().transform.position;
                                    dirToTarget = transform.InverseTransformDirection(transform.position - targetPosition);
                                }
                                //! - Otherwise run towards enemy goal -
                                else
                                {
                                    //! - Move towards goal
                                    if (timeStartedCharge != 0f && timeStartedCharge + MAX_CHARGE_TIME - 0.3f > Time.time)
                                    {
                                        if (botPlayerScript.team == 0)
                                        {
                                            targetPosition = gameManager.goalHoleB.transform.position - Vector3.forward * 30f;
                                        }
                                        else
                                        {
                                            targetPosition = gameManager.goalHoleA.transform.position + Vector3.forward * 30f;
                                        }
                                        dirToTarget = transform.InverseTransformDirection(targetPosition - transform.position);
                                    }
                                    else
                                    {
                                        targetPosition = Vector3.zero;
                                        dirToTarget = Vector3.zero;
                                    }
                                }
                            }
                            //! - Holding something not gameball, run towards gameball -
                            else
                            {
                                //! - Move towards gameball -
                                targetPosition = gameball.transform.position;
                                dirToTarget = transform.InverseTransformDirection(targetPosition - transform.position);
                            }
                        }
                    }
                    //! - This bot is not the closest to the gameball on team -
                    else
                    {
                        //! - Attack enemy player closest to ball -
                        if (FindEnemyClosestToGameball())
                        {
                            targetPosition = FindEnemyClosestToGameball().transform.position;
                        }
                        //! - No enemy fielders -
                        else if (enemyPlayers.Count == 1) //! - only goallie on opposite team
                        {
                            //! - Move towards goal -
                            if (botPlayerScript.team == 0)
                            {
                                targetPosition = gameManager.goalHoleB.transform.position - Vector3.forward * 30f;
                            }
                            else
                            {
                                targetPosition = gameManager.goalHoleA.transform.position + Vector3.forward * 30f;
                            }
                        }
                        //! - No available enemy to target -
                        else
                        {
                            //! - Move towards gameball -
                            targetPosition = gameball.transform.position;
                        }
                        dirToTarget = transform.InverseTransformDirection(targetPosition - transform.position);
                    }
                }
                //! - Bot team has possession -
                else if (gameManager.botManager.possessionState == botPlayerScript.team + 1)
                {
                    //! - If no teammates always go for ball during no possession -
                    //! - This bot is the bot closest on team to the gameball -
                    if (teammates.Count == 1 || (FindTeammateClosestToGameball() && FindTeammateClosestToGameball() == botPlayerScript))
                    {
                        //! - Holding nothing, try to get the gameball back -
                        if (!botPlayerScript.heldEntity)
                        {
                            //! - Move towards gameball -
                            targetPosition = gameball.transform.position;
                            dirToTarget = transform.InverseTransformDirection(targetPosition - transform.position);
                        }
                        else
                        {
                            //! - Currently has gameball, run for goals -
                            if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                            {
                                if (timeStartedCharge != 0f && timeStartedCharge + MAX_CHARGE_TIME - 0.3f > Time.time)
                                {
                                    //! - Move towards goal -
                                    if (botPlayerScript.team == 0)
                                    {
                                        targetPosition = gameManager.goalHoleB.transform.position - Vector3.forward * 20f;
                                    }
                                    else
                                    {
                                        targetPosition = gameManager.goalHoleA.transform.position + Vector3.forward * 20f;
                                    }
                                    dirToTarget = transform.InverseTransformDirection(targetPosition - transform.position);
                                }
                                else
                                {
                                    targetPosition = Vector3.zero;
                                    dirToTarget = Vector3.zero;
                                }
                            }
                            else
                            {
                                //! - Holding something else, try to get to the gameball -
                                targetPosition = gameball.transform.position;
                                dirToTarget = transform.InverseTransformDirection(targetPosition - transform.position);
                            }
                        }
                    }
                    //! - Not the closest bot on team to gameball, try to find better position or attack enemy -
                    else
                    {
                        //! - Bot teammate has the gameball -
                        if (FindTeammateClosestToGameball().GetComponent<BotBrain>())
                        {
                            //! - Bot teammate trying to pass 
                            if (FindTeammateClosestToGameball().GetComponent<BotBrain>().FindTeammateToPassAsFielder())
                            {
                                //! - If this bot is the intended pass target move towards goal -
                                if (FindTeammateClosestToGameball().GetComponent<BotBrain>().FindTeammateToPassAsFielder() == botPlayerScript)
                                {
                                    //! - Move towards goal
                                    if (botPlayerScript.team == 0)
                                    {
                                        targetPosition = gameManager.goalHoleB.transform.position - Vector3.forward * 30f;
                                    }
                                    else
                                    {
                                        targetPosition = gameManager.goalHoleA.transform.position + Vector3.forward * 30f;
                                    }
                                }
                                //! - If this bot is not the intended pass target (still push up for now)-
                                else
                                {
                                    //! - Move towards goal
                                    if (botPlayerScript.team == 0)
                                    {
                                        targetPosition = gameManager.goalHoleB.transform.position - Vector3.forward * 30f;
                                    }
                                    else
                                    {
                                        targetPosition = gameManager.goalHoleA.transform.position + Vector3.forward * 30f;
                                    }
                                }
                            }
                            //! - Bot teammate trying to shoot -
                            else
                            {
                                //! - Attack enemy player closest to ball -
                                if (FindEnemyClosestToGameball())
                                {
                                    targetPosition = FindEnemyClosestToGameball().transform.position;
                                }
                                //! - No available enemy to target -
                                else
                                {
                                    //! - Move towards gameball -
                                    targetPosition = gameball.transform.position;
                                }
                            }
                        }
                        //! - Player teammate has the ball -
                        else
                        {
                            //! - Player is close to the goal -
                            if (FindTeammateDistanceToGoal(FindTeammateClosestToGameball(), ownGoal: false) < 100f)
                            {
                                //! - Player is closer to goal than this bot -
                                if (FindTeammateDistanceToGoal(FindTeammateClosestToGameball(), ownGoal: false) <
                                    FindTeammateDistanceToGoal(botPlayerScript, ownGoal: false))
                                {
                                    //! - Attack enemy player closest to ball -
                                    if (FindEnemyClosestToGameball())
                                    {
                                        targetPosition = FindEnemyClosestToGameball().transform.position;
                                    }
                                    //! - No available enemy to target -
                                    else
                                    {
                                        //! - Move towards goal -
                                        if (botPlayerScript.team == 0)
                                        {
                                            targetPosition = gameManager.goalHoleB.transform.position - Vector3.forward * 30f;
                                        }
                                        else
                                        {
                                            targetPosition = gameManager.goalHoleA.transform.position + Vector3.forward * 30f;
                                        }
                                    }
                                }
                                //! - Bot is closer to goal than player with ball -
                                else
                                {
                                    //! - Attack enemy player closest to this bot -
                                    if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                                        !closestEnemyPlayerScript.playerRecovering)
                                    {
                                        targetPosition = closestEnemyPlayerScript.transform.position;
                                    }
                                    //! - No available enemy to target -
                                    else
                                    {
                                        //! - Move towards goal -
                                        if (botPlayerScript.team == 0)
                                        {
                                            targetPosition = gameManager.goalHoleB.transform.position - Vector3.forward * 30f;
                                        }
                                        else
                                        {
                                            targetPosition = gameManager.goalHoleA.transform.position + Vector3.forward * 30f;
                                        }
                                    }
                                }
                            }
                            //! - Player is far from the goal -
                            else
                            {
                                //! - Player is closer to goal than this bot -
                                if (FindTeammateDistanceToGoal(FindTeammateClosestToGameball(), ownGoal: false) < FindTeammateDistanceToGoal(botPlayerScript, ownGoal: false))
                                {
                                    //! - Attack enemy player closest to ball -
                                    if (FindEnemyClosestToGameball())
                                    {
                                        targetPosition = FindEnemyClosestToGameball().transform.position;
                                    }
                                    //! - No available enemy to target -
                                    else
                                    {
                                        //! - Move towards goal -
                                        if (botPlayerScript.team == 0)
                                        {
                                            targetPosition = gameManager.goalHoleB.transform.position - Vector3.forward * 30f;
                                        }
                                        else
                                        {
                                            targetPosition = gameManager.goalHoleA.transform.position + Vector3.forward * 30f;
                                        }
                                    }
                                }
                                //! - Bot is closer to goal than player with ball -
                                else
                                {
                                    //! - Attack enemy player closest to this bot only if they are close -
                                    if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                                        !closestEnemyPlayerScript.playerRecovering &&
                                        Vector3.Distance(transform.position, closestEnemyPlayerScript.transform.position) < 20f)
                                    {
                                        targetPosition = closestEnemyPlayerScript.transform.position;
                                    }
                                    //! - Otherwise push up to goal -
                                    else
                                    {
                                        //! - Move towards goal -
                                        if (botPlayerScript.team == 0)
                                        {
                                            targetPosition = gameManager.goalHoleB.transform.position - Vector3.forward * 30f;
                                        }
                                        else
                                        {
                                            targetPosition = gameManager.goalHoleA.transform.position + Vector3.forward * 30f;
                                        }
                                    }
                                }
                            }
                        }
                        dirToTarget = transform.InverseTransformDirection(targetPosition - transform.position);
                    }
                }
                //! - Enemy team has possession -
                else
                {
                    if (FindTeammateClosestToGameball() == botPlayerScript)
                    {
                        //! - If the closest team player to gamball, go for the gameball
                        targetPosition = gameball.transform.position;
                        dirToTarget = transform.InverseTransformDirection(targetPosition - transform.position);
                    }
                    //! - Otherwise push back to own goal
                    else
                    {
                        //! - Attack enemy player closest to this bot only if they are close and this bot is close to the defending goal-
                        if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                            !closestEnemyPlayerScript.playerRecovering &&
                            Vector3.Distance(transform.position, closestEnemyPlayerScript.transform.position) < 20f &&
                            FindTeammateDistanceToGoal(botPlayerScript, ownGoal: true) < 100f)
                        {
                            targetPosition = closestEnemyPlayerScript.transform.position;
                            dirToTarget = transform.InverseTransformDirection(targetPosition - transform.position);
                        }
                        //! - Otherwise go back to goal -
                        else
                        {
                            //! - Move towards goal -
                            if (botPlayerScript.team == 0)
                            {
                                targetPosition = gameManager.goalHoleA.transform.position - Vector3.forward * 30f;
                                dirToTarget = transform.InverseTransformDirection(targetPosition - transform.position);
                            }
                            else
                            {
                                targetPosition = gameManager.goalHoleB.transform.position + Vector3.forward * 30f;
                                dirToTarget = transform.InverseTransformDirection(targetPosition - transform.position);
                            }
                        }
                    }
                }
            }
            //! - Old bot movement changed 03/05 -
            else
            {

                #region old stuff
                ////! - Force chasing ball if no opposition team for whatever reason -
                //if (enemyPlayers.Count == 0)
                //{
                //    isOnBall = true;
                //}
                ////! - Chasing the ball -
                //if (isOnBall)
                //{
                //    if (!botPlayerScript.heldEntity)
                //    {
                //        //! - Move towards main ball -
                //        dirToTarget = transform.InverseTransformDirection(gameball.transform.position - transform.position);
                //    }
                //    else
                //    {
                //        if (timeStartedCharge != 0f && timeStartedCharge + MAX_CHARGE_TIME - 0.3f > Time.time)
                //        {
                //            //! - Move towards goal -
                //            if (botPlayerScript.team == 0)
                //            {
                //                dirToTarget = transform.InverseTransformDirection(
                //                    gameManager.goalHoleB.transform.position - Vector3.forward * 30f - transform.position);
                //            }
                //            else
                //            {
                //                dirToTarget = transform.InverseTransformDirection(
                //                    gameManager.goalHoleA.transform.position + Vector3.forward * 30f - transform.position);
                //            }
                //        }
                //        else
                //        {
                //            dirToTarget = Vector3.zero;
                //        }
                //    }
                //}
                ////! - Chasing Enemy Players -
                //else
                //{
                //    if (closestEnemyPlayerScript != null)
                //    {
                //        //! - Move towards main ball -
                //        dirToTarget = transform.InverseTransformDirection(closestEnemyPlayerScript.transform.position - transform.position);
                //    }
                //    else
                //    {
                //        dirToTarget = Vector3.zero;
                //    }
                //}
                #endregion
                //! - Neither team has clear possession -
                if (gameManager.botManager.possessionState == 0)
                {
                    //! - If no teammates always go for ball during no possession -
                    //! - This bot is the bot closest on team to the gameball -
                    if (teammates.Count == 1 || (FindTeammateClosestToGameball() && FindTeammateClosestToGameball() == botPlayerScript))
                    {
                        //! - If no possession of the gameball go for the gameball -
                        if (!botPlayerScript.heldEntity)
                        {
                            //! - Move towards gameball -
                            dirToTarget = transform.InverseTransformDirection(gameball.transform.position - transform.position);
                        }
                        //! - Holding an object -
                        else
                        {
                            //! - Has the gameball -
                            if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                            {
                                //! - Run from the closest non-knocked or non-recovering enemy player if they are close -
                                if (FindEnemyClosestToGameball() &&
                                    (FindEnemyClosestToGameball().transform.position - transform.position).magnitude < 20f)
                                {
                                    //! - Move away from closest player -
                                    dirToTarget = transform.InverseTransformDirection(transform.position - FindEnemyClosestToGameball().transform.position);
                                }
                                //! - Otherwise run towards enemy goal -
                                else
                                {
                                    //! - Move towards goal
                                    if (timeStartedCharge != 0f && timeStartedCharge + MAX_CHARGE_TIME - 0.3f > Time.time)
                                    {
                                        if (botPlayerScript.team == 0)
                                        {
                                            dirToTarget = transform.InverseTransformDirection(
                                                gameManager.goalHoleB.transform.position - Vector3.forward * 30f - transform.position);
                                        }
                                        else
                                        {
                                            dirToTarget = transform.InverseTransformDirection(
                                                gameManager.goalHoleA.transform.position + Vector3.forward * 30f - transform.position);
                                        }
                                    }
                                    else
                                    {
                                        dirToTarget = Vector3.zero;
                                    }
                                }
                            }
                            //! - Holding something not gameball, run towards gameball -
                            else
                            {
                                //! - Move towards gameball -
                                dirToTarget = transform.InverseTransformDirection(gameball.transform.position - transform.position);
                            }
                        }
                    }
                    //! - This bot is not the closest to the gameball on team -
                    else
                    {
                        //! - Attack enemy player closest to ball -
                        if (FindEnemyClosestToGameball())
                        {
                            dirToTarget = transform.InverseTransformDirection(FindEnemyClosestToGameball().transform.position - transform.position);
                        }
                        //! - No enemy fielders -
                        else if (enemyPlayers.Count == 1) //! - only goallie on opposite team
                        {
                            //! - Move towards goal -
                            if (botPlayerScript.team == 0)
                            {
                                dirToTarget = transform.InverseTransformDirection(
                                    gameManager.goalHoleB.transform.position - Vector3.forward * 30f - transform.position);
                            }
                            else
                            {
                                dirToTarget = transform.InverseTransformDirection(
                                    gameManager.goalHoleA.transform.position + Vector3.forward * 30f - transform.position);
                            }
                        }
                        //! - No available enemy to target -
                        else
                        {
                            //! - Move towards gameball -
                            dirToTarget = transform.InverseTransformDirection(gameball.transform.position - transform.position);
                        }
                    }
                }
                //! - Bot team has possession -
                else if (gameManager.botManager.possessionState == botPlayerScript.team + 1)
                {
                    //! - If no teammates always go for ball during no possession -
                    //! - This bot is the bot closest on team to the gameball -
                    if (teammates.Count == 1 || (FindTeammateClosestToGameball() && FindTeammateClosestToGameball() == botPlayerScript))
                    {
                        //! - Holding nothing, try to get the gameball back -
                        if (!botPlayerScript.heldEntity)
                        {
                            //! - Move towards gameball -
                            dirToTarget = transform.InverseTransformDirection(gameball.transform.position - transform.position);
                        }
                        else
                        {
                            //! - Currently has gameball, run for goals -
                            if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                            {
                                if (timeStartedCharge != 0f && timeStartedCharge + MAX_CHARGE_TIME - 0.3f > Time.time)
                                {
                                    //! - Move towards goal -
                                    if (botPlayerScript.team == 0)
                                    {
                                        dirToTarget = transform.InverseTransformDirection(
                                            gameManager.goalHoleB.transform.position - Vector3.forward * 20f - transform.position);
                                    }
                                    else
                                    {
                                        dirToTarget = transform.InverseTransformDirection(
                                            gameManager.goalHoleA.transform.position + Vector3.forward * 20f - transform.position);
                                    }
                                }
                                else
                                {
                                    dirToTarget = Vector3.zero;
                                }
                            }
                            else
                            {
                                //! - Holding something else, try to get to the gameball -
                                dirToTarget = transform.InverseTransformDirection(gameball.transform.position - transform.position);
                            }
                        }
                    }
                    //! - Not the closest bot on team to gameball, try to find better position or attack enemy -
                    else
                    {
                        //! - Bot teammate has the gameball -
                        if (FindTeammateClosestToGameball().GetComponent<BotBrain>())
                        {
                            //! - Bot teammate trying to pass 
                            if (FindTeammateClosestToGameball().GetComponent<BotBrain>().FindTeammateToPassAsFielder())
                            {
                                //! - If this bot is the intended pass target move towards goal -
                                if (FindTeammateClosestToGameball().GetComponent<BotBrain>().FindTeammateToPassAsFielder() == botPlayerScript)
                                {
                                    //! - Move towards goal
                                    if (botPlayerScript.team == 0)
                                    {
                                        dirToTarget = transform.InverseTransformDirection(
                                            gameManager.goalHoleB.transform.position - Vector3.forward * 30f - transform.position);
                                    }
                                    else
                                    {
                                        dirToTarget = transform.InverseTransformDirection(
                                            gameManager.goalHoleA.transform.position + Vector3.forward * 30f - transform.position);
                                    }
                                }
                                //! - If this bot is not the intended pass target (still push up for now)-
                                else
                                {
                                    //! - Move towards goal
                                    if (botPlayerScript.team == 0)
                                    {
                                        dirToTarget = transform.InverseTransformDirection(
                                            gameManager.goalHoleB.transform.position - Vector3.forward * 30f - transform.position);
                                    }
                                    else
                                    {
                                        dirToTarget = transform.InverseTransformDirection(
                                            gameManager.goalHoleA.transform.position + Vector3.forward * 30f - transform.position);
                                    }
                                }
                            }
                            //! - Bot teammate trying to shoot -
                            else
                            {
                                //! - Attack enemy player closest to ball -
                                if (FindEnemyClosestToGameball())
                                {
                                    dirToTarget = transform.InverseTransformDirection(FindEnemyClosestToGameball().transform.position - transform.position);
                                }
                                //! - No available enemy to target -
                                else
                                {
                                    //! - Move towards gameball -
                                    dirToTarget = transform.InverseTransformDirection(gameball.transform.position - transform.position);
                                }
                            }
                        }
                        //! - Player teammate has the ball -
                        else
                        {
                            //! - Player is close to the goal -
                            if (FindTeammateDistanceToGoal(FindTeammateClosestToGameball(), ownGoal: false) < 100f)
                            {
                                //! - Player is closer to goal than this bot -
                                if (FindTeammateDistanceToGoal(FindTeammateClosestToGameball(), ownGoal: false) <
                                    FindTeammateDistanceToGoal(botPlayerScript, ownGoal: false))
                                {
                                    //! - Attack enemy player closest to ball -
                                    if (FindEnemyClosestToGameball())
                                    {
                                        dirToTarget = transform.InverseTransformDirection(FindEnemyClosestToGameball().transform.position - transform.position);
                                    }
                                    //! - No available enemy to target -
                                    else
                                    {
                                        //! - Move towards goal -
                                        if (botPlayerScript.team == 0)
                                        {
                                            dirToTarget = transform.InverseTransformDirection(
                                                gameManager.goalHoleB.transform.position - Vector3.forward * 30f - transform.position);
                                        }
                                        else
                                        {
                                            dirToTarget = transform.InverseTransformDirection(
                                                gameManager.goalHoleA.transform.position + Vector3.forward * 30f - transform.position);
                                        }
                                    }
                                }
                                //! - Bot is closer to goal than player with ball -
                                else
                                {
                                    //! - Attack enemy player closest to this bot -
                                    if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                                        !closestEnemyPlayerScript.playerRecovering)
                                    {
                                        dirToTarget = transform.InverseTransformDirection(closestEnemyPlayerScript.transform.position - transform.position);
                                    }
                                    //! - No available enemy to target -
                                    else
                                    {
                                        //! - Move towards goal -
                                        if (botPlayerScript.team == 0)
                                        {
                                            dirToTarget = transform.InverseTransformDirection(
                                                gameManager.goalHoleB.transform.position - Vector3.forward * 30f - transform.position);
                                        }
                                        else
                                        {
                                            dirToTarget = transform.InverseTransformDirection(
                                                gameManager.goalHoleA.transform.position + Vector3.forward * 30f - transform.position);
                                        }
                                    }
                                }
                            }
                            //! - Player is far from the goal -
                            else
                            {
                                //! - Player is closer to goal than this bot -
                                if (FindTeammateDistanceToGoal(FindTeammateClosestToGameball(), ownGoal: false) < FindTeammateDistanceToGoal(botPlayerScript, ownGoal: false))
                                {
                                    //! - Attack enemy player closest to ball -
                                    if (FindEnemyClosestToGameball())
                                    {
                                        dirToTarget = transform.InverseTransformDirection(FindEnemyClosestToGameball().transform.position - transform.position);
                                    }
                                    //! - No available enemy to target -
                                    else
                                    {
                                        //! - Move towards goal -
                                        if (botPlayerScript.team == 0)
                                        {
                                            dirToTarget = transform.InverseTransformDirection(
                                                gameManager.goalHoleB.transform.position - Vector3.forward * 30f - transform.position);
                                        }
                                        else
                                        {
                                            dirToTarget = transform.InverseTransformDirection(
                                                gameManager.goalHoleA.transform.position + Vector3.forward * 30f - transform.position);
                                        }
                                    }
                                }
                                //! - Bot is closer to goal than player with ball -
                                else
                                {
                                    //! - Attack enemy player closest to this bot only if they are close -
                                    if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                                        !closestEnemyPlayerScript.playerRecovering &&
                                        Vector3.Distance(transform.position, closestEnemyPlayerScript.transform.position) < 20f)
                                    {
                                        dirToTarget = transform.InverseTransformDirection(closestEnemyPlayerScript.transform.position - transform.position);
                                    }
                                    //! - Otherwise push up to goal -
                                    else
                                    {
                                        //! - Move towards goal -
                                        if (botPlayerScript.team == 0)
                                        {
                                            dirToTarget = transform.InverseTransformDirection(
                                                gameManager.goalHoleB.transform.position - Vector3.forward * 30f - transform.position);
                                        }
                                        else
                                        {
                                            dirToTarget = transform.InverseTransformDirection(
                                                gameManager.goalHoleA.transform.position + Vector3.forward * 30f - transform.position);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                //! - Enemy team has possession -
                else
                {
                    if (FindTeammateClosestToGameball() == botPlayerScript)
                    {
                        //! - If the closest team player to gamball, go for the gameball
                        dirToTarget = transform.InverseTransformDirection(gameball.transform.position - transform.position);
                    }
                    //! - Otherwise push back to own goal
                    else
                    {
                        //! - Attack enemy player closest to this bot only if they are close and this bot is close to the defending goal-
                        if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                            !closestEnemyPlayerScript.playerRecovering &&
                            Vector3.Distance(transform.position, closestEnemyPlayerScript.transform.position) < 20f &&
                            FindTeammateDistanceToGoal(botPlayerScript, ownGoal: true) < 100f)
                        {
                            dirToTarget = transform.InverseTransformDirection(closestEnemyPlayerScript.transform.position - transform.position);
                        }
                        //! - Otherwise go back to goal -
                        else
                        {
                            //! - Move towards goal -
                            if (botPlayerScript.team == 0)
                            {
                                dirToTarget = transform.InverseTransformDirection(
                                    gameManager.goalHoleA.transform.position - Vector3.forward * 30f - transform.position);
                            }
                            else
                            {
                                dirToTarget = transform.InverseTransformDirection(
                                    gameManager.goalHoleB.transform.position + Vector3.forward * 30f - transform.position);
                            }
                        }
                    }
                }
            }
        }
        //! - If goalie has ball move towards opponent goal -
        else
        {
            //! - Attack enemy player closest to this bot only if they are close -
            if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                !closestEnemyPlayerScript.playerRecovering &&
                Vector3.Distance(transform.position, closestEnemyPlayerScript.transform.position) < 20f)
            {
                dirToTarget = transform.InverseTransformDirection(closestEnemyPlayerScript.transform.position - transform.position);
            }
            //! - Move towards goal -
            else
            {
                if (botPlayerScript.team == 0)
                {
                    dirToTarget = transform.InverseTransformDirection(
                        gameManager.goalHoleB.transform.position - Vector3.forward * 15f - transform.position);
                }
                else
                {
                    dirToTarget = transform.InverseTransformDirection(
                        gameManager.goalHoleA.transform.position + Vector3.forward * 15f - transform.position);
                }
            }
        }
        dirToTargetInput = new Vector2(dirToTarget.x, dirToTarget.z).normalized;
        botPlayerScript.SimulateInputLS(dirToTargetInput);
    }
    void MoveAsGoalie()
    {
        if (!botPlayerScript.isDiving)
        {
            Vector3 dirToTarget;
            Vector2 dirToTargetInput;
            //! - If holding gameball -
            if (botPlayerScript.heldEntity)
            {
                //! - Move to throwing position -
                //! Team A
                if (botPlayerScript.team == 0)
                {
                    dirToTarget = transform.InverseTransformDirection
                        (gameManager.goaliePassingPositions[0].position - transform.position);
                }
                //! Team B
                else
                {
                    dirToTarget = transform.InverseTransformDirection
                        (gameManager.goaliePassingPositions[1].position - transform.position);
                }
                //! - Move towards goal position -
                dirToTargetInput = new Vector2(dirToTarget.x, dirToTarget.z).normalized;
            }
            else
            {
                //! - Move to goal position -
                if (!walkingToGameball || !botPlayerScript.IsInGoalBox())
                {
                    //! Team A
                    if (botPlayerScript.team == 0)
                    {
                        dirToTarget = transform.InverseTransformDirection(gameManager.goaliePositions[0].position - transform.position);
                    }
                    //! Team B
                    else
                    {
                        dirToTarget = transform.InverseTransformDirection(gameManager.goaliePositions[1].position - transform.position);
                    }
                    //! - Move towards goal position -
                    dirToTargetInput = new Vector2(dirToTarget.x, dirToTarget.z).normalized;
                }
                //! - Move towards ball -
                else
                {
                    dirToTarget = transform.InverseTransformDirection(gameball.transform.position - transform.position);
                    //! - Move towards goal position -
                    dirToTargetInput = new Vector2(dirToTarget.x, dirToTarget.z).normalized;
                }
            }
            if (dirToTarget.y > 1f)
            {
                CheckIfReturnToGoalJumpNeeded();
            }
            botPlayerScript.SimulateInputLS(dirToTargetInput);
        }
    }
    void RotateAsFielder()
    {
        Vector3 dirToTarget;
        //! - Testing new bot movement changed 03/05 -
        if (gameManager.newBotMovement)
        {
            //! - Regular Rotation -
            if (!teamGoalieHasBall)
            {
                //! - Neither team has clear possession -
                if (gameManager.botManager.possessionState == 0)
                {
                    //! - If no teammates always go for ball during no possession -
                    //! - This bot is the bot closest on team to the gameball -
                    if (teammates.Count == 1 || (FindTeammateClosestToGameball() && FindTeammateClosestToGameball() == botPlayerScript))
                    {
                        //! - If no possession of the gameball go for the gameball -
                        if (!botPlayerScript.heldEntity)
                        {
                            //! - Rotate towards gameball -
                            dirToTarget = (gameball.transform.position - transform.position).normalized;
                        }
                        //! - Holding an object -
                        else
                        {
                            //! - Holding Gameball -
                            if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                            {
                                if (teammates.Count > 2 && FindTeammateToPassAsFielder())
                                {
                                    //! - Look for a teammate to pass to -
                                    Vector3 dirToTeammate = FindTeammateToPassAsFielder().transform.position - transform.position;
                                    float distance = dirToTeammate.magnitude;
                                    Vector3 angleAdjust = Vector3.zero;
                                    if (distance > 50f)
                                    {
                                        angleAdjust = Vector3.up * (Mathf.Pow(distance - 50f, 1.1f) / 4f);
                                    }
                                    dirToTarget = (TeammatePredictedPosition(FindTeammateToPassAsFielder()) + angleAdjust - transform.position).normalized;
                                }
                                else
                                {
                                    //! - Rotate to look at opposition goal -
                                    if (gameManager.newBotShooting)
                                    {
                                        //! - New shooting aiming 11/05 -
                                        //? Testing not changing angle on throw -
                                        //angleOffset = CalculateAngle();
                                        angleOffset = 20f;
                                        float yDistFromGoal;

                                        if (botPlayerScript.team == 0)
                                        {
                                            dirToTarget = gameManager.goalHoleB.transform.position - transform.position;
                                            yDistFromGoal = transform.position.y - gameManager.goalHoleB.transform.position.y;
                                        }
                                        else
                                        {
                                            dirToTarget = gameManager.goalHoleA.transform.position - transform.position;
                                            yDistFromGoal = transform.position.y - gameManager.goalHoleA.transform.position.y;
                                        }
                                        if (yDistFromGoal > 5)
                                        {
                                            angleOffset = 0f;
                                        }
                                        //! - Vertical angle offset -
                                        if (dirToTarget.magnitude < 20f)
                                        {
                                            angleOffset = 60f;
                                        }
                                        else
                                        {
                                            angleOffset = 20f;
                                        }
                                        //! - Horizontal angle offset -
                                        if (sideAngleOffset == 0f)
                                        {
                                            int multiplier = Random.value >= 0.5f ? -1 : 1;
                                            sideAngleOffset = 16f * (1 - dirToTarget.magnitude / 250f) * multiplier;
                                        }
                                        angleOffset = botPlayerScript.team == 0 ? -angleOffset : angleOffset;
                                        dirToTarget = dirToTarget.normalized;
                                        dirToTarget = transform.InverseTransformDirection(dirToTarget);
                                        dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, sideAngleOffset, 0f))
                                            * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                                        dirToTarget = transform.TransformDirection(dirToTarget);
                                    }
                                    else
                                    {
                                        //! - Old shooting aiming -
                                        //? Testing not changing angle on throw -
                                        //angleOffset = CalculateAngle();
                                        angleOffset = 20f;

                                        if (botPlayerScript.team == 0)
                                        {
                                            dirToTarget = gameManager.goalHoleB.transform.position - transform.position;
                                        }
                                        else
                                        {
                                            dirToTarget = gameManager.goalHoleA.transform.position - transform.position;
                                        }
                                        if (dirToTarget.magnitude < 20f)
                                        {
                                            angleOffset = 50f;
                                        }
                                        else
                                        {
                                            angleOffset = 20f;
                                        }
                                        angleOffset = botPlayerScript.team == 0 ? -angleOffset : angleOffset;
                                        dirToTarget = dirToTarget.normalized;
                                        dirToTarget = transform.InverseTransformDirection(dirToTarget);
                                        dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                                            * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                                        dirToTarget = transform.TransformDirection(dirToTarget);
                                    }
                                }
                            }
                            //! - Holding Player -
                            else if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                            {
                                angleOffset = 20f;
                                if (botPlayerScript.team == 0)
                                {
                                    //! - Throw teammate towards your enemy goal -
                                    if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                                    {
                                        dirToTarget = gameManager.goalHoleA.transform.position - transform.position;
                                    }
                                    //! - Throw enemy at your own goal to keep them away -
                                    else
                                    {
                                        dirToTarget = gameManager.goalHoleB.transform.position - transform.position;
                                        angleOffset = -angleOffset;
                                    }
                                }
                                else
                                {
                                    //! - Throw teammate towards your enemy goal -
                                    if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                                    {
                                        dirToTarget = gameManager.goalHoleB.transform.position - transform.position;
                                        angleOffset = -angleOffset;
                                    }
                                    //! - Throw enemy at your own goal to keep them away -
                                    else
                                    {
                                        dirToTarget = gameManager.goalHoleA.transform.position - transform.position;
                                    }
                                }

                                dirToTarget = dirToTarget.normalized;
                                dirToTarget = transform.InverseTransformDirection(dirToTarget);
                                dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                                    * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                                dirToTarget = transform.TransformDirection(dirToTarget);
                            }
                            //! - Holding Dodgeball -
                            else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                            {
                                angleOffset = 0f;
                                if (EnemyWithGameballWithinRange(100f))
                                {
                                    dirToTarget = EnemyWithGameballWithinRange(100f).transform.position - transform.position;
                                }
                                else
                                {
                                    dirToTarget = closestEnemyPlayerScript.transform.position - transform.position;
                                }
                                dirToTarget = dirToTarget.normalized;
                                dirToTarget = transform.InverseTransformDirection(dirToTarget);
                                dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                                    * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                                dirToTarget = transform.TransformDirection(dirToTarget);
                            }
                            else
                            {
                                dirToTarget = transform.forward;
                            }
                        }
                    }
                    //! - This bot is not the closest to the gameball on team -
                    else
                    {
                        //! - Attack enemy player closest to ball -
                        if (FindEnemyClosestToGameball())
                        {
                            dirToTarget = FindEnemyClosestToGameball().transform.position - transform.position;
                            dirToTarget = dirToTarget.normalized;
                            dirToTarget = transform.InverseTransformDirection(dirToTarget);
                            dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                                * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                            dirToTarget = transform.TransformDirection(dirToTarget);
                        }
                        //! - No enemy fielders -
                        else if (enemyPlayers.Count == 1) //! - only goallie on opposite team
                        {
                            //! - Rotate to look at opposition goal -
                            //? Testing not changing angle on throw -
                            //angleOffset = CalculateAngle();
                            angleOffset = 20f;

                            if (botPlayerScript.team == 0)
                            {
                                dirToTarget = gameManager.goalHoleB.transform.position - transform.position;
                            }
                            else
                            {
                                dirToTarget = gameManager.goalHoleA.transform.position - transform.position;
                            }
                            if (dirToTarget.magnitude < 20f)
                            {
                                angleOffset = 50f;
                            }
                            else
                            {
                                angleOffset = 20f;
                            }
                            angleOffset = botPlayerScript.team == 0 ? -angleOffset : angleOffset;
                            dirToTarget = dirToTarget.normalized;
                            //? Testing not changing angle on throw -
                            //if (angleOffset != 0f)
                            {
                                dirToTarget = transform.InverseTransformDirection(dirToTarget);
                                dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                                    * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                                dirToTarget = transform.TransformDirection(dirToTarget);
                                //dirToTarget = Quaternion.Euler(angleOffset, 0f, 0f) * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                            }
                        }
                        //! - No available enemy to target -
                        else
                        {
                            //! - Rotate towards gameball -
                            dirToTarget = (gameball.transform.position - transform.position).normalized;
                        }
                    }
                }
                //! - Bot team has possession -
                else if (gameManager.botManager.possessionState == botPlayerScript.team + 1)
                {
                    //! - If no teammates always go for ball during no possession -
                    //! - This bot is the bot closest on team to the gameball -
                    if (teammates.Count == 1 || (FindTeammateClosestToGameball() && FindTeammateClosestToGameball() == botPlayerScript))
                    {
                        //! - Holding nothing, try to get the gameball back -
                        if (!botPlayerScript.heldEntity)
                        {
                            //! - Rotate towards gameball -
                            dirToTarget = (gameball.transform.position - transform.position).normalized;
                        }
                        else
                        {
                            //! - Holding Gameball -
                            if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                            {
                                if (teammates.Count > 2 && FindTeammateToPassAsFielder())
                                {
                                    //! - Look for a teammate to pass to -
                                    Vector3 dirToTeammate = FindTeammateToPassAsFielder().transform.position - transform.position;
                                    float distance = dirToTeammate.magnitude;
                                    Vector3 angleAdjust = Vector3.zero;
                                    if (distance > 50f)
                                    {
                                        angleAdjust = Vector3.up * (Mathf.Pow(distance - 50f, 1.1f) / 4f);
                                    }
                                    dirToTarget = (TeammatePredictedPosition(FindTeammateToPassAsFielder()) + angleAdjust - transform.position).normalized;
                                }
                                else
                                {
                                    //! - Rotate to look at opposition goal -
                                    if (gameManager.newBotShooting)
                                    {
                                        //! - New shooting aiming 11/05 -
                                        //? Testing not changing angle on throw -
                                        //angleOffset = CalculateAngle();
                                        angleOffset = 20f;
                                        float yDistFromGoal;

                                        if (botPlayerScript.team == 0)
                                        {
                                            dirToTarget = gameManager.goalHoleB.transform.position - transform.position;
                                            yDistFromGoal = transform.position.y - gameManager.goalHoleB.transform.position.y;
                                        }
                                        else
                                        {
                                            dirToTarget = gameManager.goalHoleA.transform.position - transform.position;
                                            yDistFromGoal = transform.position.y - gameManager.goalHoleA.transform.position.y;
                                        }
                                        if (yDistFromGoal > 5)
                                        {
                                            angleOffset = 0f;
                                        }
                                        //! - Vertical angle offset -
                                        if (dirToTarget.magnitude < 20f)
                                        {
                                            angleOffset = 60f;
                                        }
                                        else
                                        {
                                            angleOffset = 20f;
                                        }
                                        //! - Horizontal angle offset -
                                        if (sideAngleOffset == 0f)
                                        {
                                            int multiplier = Random.value >= 0.5f ? -1 : 1;
                                            sideAngleOffset = 16f * (1 - dirToTarget.magnitude / 250f) * multiplier;
                                        }
                                        angleOffset = botPlayerScript.team == 0 ? -angleOffset : angleOffset;
                                        dirToTarget = dirToTarget.normalized;
                                        dirToTarget = transform.InverseTransformDirection(dirToTarget);
                                        dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, sideAngleOffset, 0f))
                                            * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                                        dirToTarget = transform.TransformDirection(dirToTarget);
                                    }
                                    else
                                    {
                                        //! - Old shooting aiming -
                                        //? Testing not changing angle on throw -
                                        //angleOffset = CalculateAngle();
                                        angleOffset = 20f;

                                        if (botPlayerScript.team == 0)
                                        {
                                            dirToTarget = gameManager.goalHoleB.transform.position - transform.position;
                                        }
                                        else
                                        {
                                            dirToTarget = gameManager.goalHoleA.transform.position - transform.position;
                                        }
                                        if (dirToTarget.magnitude < 20f)
                                        {
                                            angleOffset = 50f;
                                        }
                                        else
                                        {
                                            angleOffset = 20f;
                                        }
                                        angleOffset = botPlayerScript.team == 0 ? -angleOffset : angleOffset;
                                        dirToTarget = dirToTarget.normalized;
                                        dirToTarget = transform.InverseTransformDirection(dirToTarget);
                                        dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                                            * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                                        dirToTarget = transform.TransformDirection(dirToTarget);
                                    }
                                }
                            }
                            //! - Holding Player -
                            else if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                            {
                                angleOffset = 20f;
                                if (botPlayerScript.team == 0)
                                {
                                    //! - Throw teammate towards your enemy goal -
                                    if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                                    {
                                        dirToTarget = gameManager.goalHoleA.transform.position - transform.position;
                                    }
                                    //! - Throw enemy at your own goal to keep them away -
                                    else
                                    {
                                        dirToTarget = gameManager.goalHoleB.transform.position - transform.position;
                                        angleOffset = -angleOffset;
                                    }
                                }
                                else
                                {
                                    //! - Throw teammate towards your enemy goal -
                                    if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                                    {
                                        dirToTarget = gameManager.goalHoleB.transform.position - transform.position;
                                        angleOffset = -angleOffset;
                                    }
                                    //! - Throw enemy at your own goal to keep them away -
                                    else
                                    {
                                        dirToTarget = gameManager.goalHoleA.transform.position - transform.position;
                                    }
                                }

                                dirToTarget = dirToTarget.normalized;
                                dirToTarget = transform.InverseTransformDirection(dirToTarget);
                                dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                                    * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                                dirToTarget = transform.TransformDirection(dirToTarget);
                            }
                            //! - Holding Dodgeball -
                            else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                            {
                                angleOffset = 0f;
                                if (EnemyWithGameballWithinRange(100f))
                                {
                                    dirToTarget = EnemyWithGameballWithinRange(100f).transform.position - transform.position;
                                    //print($"enemy: {EnemyWithGameballWithinRange(100f).transform.position}, self: {transform.position}");
                                }
                                else
                                {
                                    dirToTarget = closestEnemyPlayerScript.transform.position - transform.position;
                                    //print($"enemy: {closestEnemyPlayerScript.transform.position}, self: {transform.position}");
                                }
                                dirToTarget = dirToTarget.normalized;
                                dirToTarget = transform.InverseTransformDirection(dirToTarget);
                                dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                                    * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                                dirToTarget = transform.TransformDirection(dirToTarget);
                            }
                            else
                            {
                                dirToTarget = transform.forward;
                            }
                        }
                    }
                    //! - Not the closest bot on team to gameball, try to find better position or attack enemy -
                    else
                    {
                        //! - Bot teammate has the gameball -
                        if (FindTeammateClosestToGameball().GetComponent<BotBrain>())
                        {
                            //! - Bot teammate trying to pass 
                            if (FindTeammateClosestToGameball().GetComponent<BotBrain>().FindTeammateToPassAsFielder())
                            {
                                //! - Look at teammate -
                                dirToTarget = (gameball.transform.position - transform.position).normalized;
                            }
                            //! - Bot teammate trying to shoot -
                            else
                            {
                                //! - Attack enemy player closest to ball -
                                if (FindEnemyClosestToGameball())
                                {
                                    dirToTarget = FindEnemyClosestToGameball().transform.position - transform.position;
                                    dirToTarget = dirToTarget.normalized;
                                    dirToTarget = transform.InverseTransformDirection(dirToTarget);
                                    dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                                        * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                                    dirToTarget = transform.TransformDirection(dirToTarget);
                                }
                                //! - No available enemy to target -
                                else
                                {
                                    //! - Rotate towards gameball -
                                    dirToTarget = (gameball.transform.position - transform.position).normalized;
                                }
                            }
                        }
                        //! - Player teammate has the ball -
                        else
                        {
                            //! - Player is close to the goal -
                            if (FindTeammateDistanceToGoal(FindTeammateClosestToGameball(), ownGoal: false) < 100f)
                            {
                                //! - Player is closer to goal than this bot -
                                if (FindTeammateDistanceToGoal(FindTeammateClosestToGameball(), ownGoal: false) <
                                    FindTeammateDistanceToGoal(botPlayerScript, ownGoal: false))
                                {
                                    //! - Attack enemy player closest to ball -
                                    if (FindEnemyClosestToGameball())
                                    {
                                        dirToTarget = FindEnemyClosestToGameball().transform.position - transform.position;
                                        dirToTarget = dirToTarget.normalized;
                                        dirToTarget = transform.InverseTransformDirection(dirToTarget);
                                        dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                                            * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                                        dirToTarget = transform.TransformDirection(dirToTarget);
                                    }
                                    //! - No available enemy to target -
                                    else
                                    {
                                        //! - Rotate towards gameball -
                                        dirToTarget = (gameball.transform.position - transform.position).normalized;
                                    }
                                }
                                //! - Bot is closer to goal than player with ball -
                                else
                                {
                                    //! - Attack enemy player closest to this bot -
                                    if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                                        !closestEnemyPlayerScript.playerRecovering)
                                    {
                                        dirToTarget = closestEnemyPlayerScript.transform.position - transform.position;
                                        dirToTarget = dirToTarget.normalized;
                                        dirToTarget = transform.InverseTransformDirection(dirToTarget);
                                        dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                                            * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                                        dirToTarget = transform.TransformDirection(dirToTarget);
                                    }
                                    //! - No available enemy to target -
                                    else
                                    {
                                        //! - Rotate towards gameball -
                                        dirToTarget = (gameball.transform.position - transform.position).normalized;
                                    }
                                }
                            }
                            //! - Player is far from the goal -
                            else
                            {
                                //! - Player is closer to goal than this bot -
                                if (FindTeammateDistanceToGoal(FindTeammateClosestToGameball(), ownGoal: false) < FindTeammateDistanceToGoal(botPlayerScript, ownGoal: false))
                                {
                                    //! - Attack enemy player closest to ball -
                                    if (FindEnemyClosestToGameball())
                                    {
                                        dirToTarget = FindEnemyClosestToGameball().transform.position - transform.position;
                                        dirToTarget = dirToTarget.normalized;
                                        dirToTarget = transform.InverseTransformDirection(dirToTarget);
                                        dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                                            * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                                        dirToTarget = transform.TransformDirection(dirToTarget);
                                    }
                                    //! - No available enemy to target -
                                    else
                                    {
                                        //! - Rotate towards gameball -
                                        dirToTarget = (gameball.transform.position - transform.position).normalized;
                                    }
                                }
                                //! - Bot is closer to goal than player with ball -
                                else
                                {
                                    //! - Attack enemy player closest to this bot only if they are close -
                                    if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                                        !closestEnemyPlayerScript.playerRecovering &&
                                        Vector3.Distance(transform.position, closestEnemyPlayerScript.transform.position) < 20f)
                                    {
                                        dirToTarget = closestEnemyPlayerScript.transform.position - transform.position;
                                        dirToTarget = dirToTarget.normalized;
                                        dirToTarget = transform.InverseTransformDirection(dirToTarget);
                                        dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                                            * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                                        dirToTarget = transform.TransformDirection(dirToTarget);
                                    }
                                    //! - Otherwise push up to goal -
                                    else
                                    {
                                        //! - Rotate towards gameball -
                                        dirToTarget = (gameball.transform.position - transform.position).normalized;
                                    }
                                }
                            }
                        }
                    }
                }
                //! - Enemy team has possession -
                else
                {
                    //! - Holding nothing, try to get the gameball back -
                    if (!botPlayerScript.heldEntity)
                    {
                        if (FindTeammateClosestToGameball() == botPlayerScript)
                        {
                            //! - If the closest team player to gameball, go for the gameball
                            dirToTarget = (gameball.transform.position - transform.position).normalized;
                        }
                        //! - Otherwise push back to own goal
                        else
                        {
                            //! - Attack enemy player closest to this bot only if they are close and this bot is close to the defending goal-
                            if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                                !closestEnemyPlayerScript.playerRecovering &&
                                Vector3.Distance(transform.position, closestEnemyPlayerScript.transform.position) < 20f &&
                                FindTeammateDistanceToGoal(botPlayerScript, ownGoal: true) < 100f)
                            {
                                dirToTarget = closestEnemyPlayerScript.transform.position - transform.position;
                                dirToTarget = dirToTarget.normalized;
                                dirToTarget = transform.InverseTransformDirection(dirToTarget);
                                dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                                    * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                                dirToTarget = transform.TransformDirection(dirToTarget);
                            }
                            //! - Otherwise go back to goal -
                            else
                            {
                                //! - Rotate towards gameball -
                                dirToTarget = (gameball.transform.position - transform.position).normalized;
                            }
                        }
                    }
                    else
                    {
                        //! - Holding Gameball -
                        if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                        {
                            if (teammates.Count > 2 && FindTeammateToPassAsFielder())
                            {
                                //! - Look for a teammate to pass to -
                                Vector3 dirToTeammate = FindTeammateToPassAsFielder().transform.position - transform.position;
                                float distance = dirToTeammate.magnitude;
                                Vector3 angleAdjust = Vector3.zero;
                                if (distance > 50f)
                                {
                                    angleAdjust = Vector3.up * (Mathf.Pow(distance - 50f, 1.1f) / 4f);
                                }
                                dirToTarget = (TeammatePredictedPosition(FindTeammateToPassAsFielder()) + angleAdjust - transform.position).normalized;
                            }
                            else
                            {
                                //! - Rotate to look at opposition goal -
                                if (gameManager.newBotShooting)
                                {
                                    //! - New shooting aiming 11/05 -
                                    //? Testing not changing angle on throw -
                                    //angleOffset = CalculateAngle();
                                    angleOffset = 20f;
                                    float yDistFromGoal;

                                    if (botPlayerScript.team == 0)
                                    {
                                        dirToTarget = gameManager.goalHoleB.transform.position - transform.position;
                                        yDistFromGoal = transform.position.y - gameManager.goalHoleB.transform.position.y;
                                    }
                                    else
                                    {
                                        dirToTarget = gameManager.goalHoleA.transform.position - transform.position;
                                        yDistFromGoal = transform.position.y - gameManager.goalHoleA.transform.position.y;
                                    }
                                    if (yDistFromGoal > 5)
                                    {
                                        angleOffset = 0f;
                                    }
                                    //! - Vertical angle offset -
                                    if (dirToTarget.magnitude < 20f)
                                    {
                                        angleOffset = 60f;
                                    }
                                    else
                                    {
                                        angleOffset = 20f;
                                    }
                                    //! - Horizontal angle offset -
                                    if (sideAngleOffset == 0f)
                                    {
                                        int multiplier = Random.value >= 0.5f ? -1 : 1;
                                        sideAngleOffset = 16f * (1 - dirToTarget.magnitude / 250f) * multiplier;
                                    }
                                    angleOffset = botPlayerScript.team == 0 ? -angleOffset : angleOffset;
                                    dirToTarget = dirToTarget.normalized;
                                    dirToTarget = transform.InverseTransformDirection(dirToTarget);
                                    dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, sideAngleOffset, 0f))
                                        * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                                    dirToTarget = transform.TransformDirection(dirToTarget);
                                }
                                else
                                {
                                    //! - Old shooting aiming -
                                    //? Testing not changing angle on throw -
                                    //angleOffset = CalculateAngle();
                                    angleOffset = 20f;

                                    if (botPlayerScript.team == 0)
                                    {
                                        dirToTarget = gameManager.goalHoleB.transform.position - transform.position;
                                    }
                                    else
                                    {
                                        dirToTarget = gameManager.goalHoleA.transform.position - transform.position;
                                    }
                                    if (dirToTarget.magnitude < 20f)
                                    {
                                        angleOffset = 50f;
                                    }
                                    else
                                    {
                                        angleOffset = 20f;
                                    }
                                    angleOffset = botPlayerScript.team == 0 ? -angleOffset : angleOffset;
                                    dirToTarget = dirToTarget.normalized;
                                    dirToTarget = transform.InverseTransformDirection(dirToTarget);
                                    dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                                        * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                                    dirToTarget = transform.TransformDirection(dirToTarget);
                                }
                            }
                        }
                        //! - Holding Player -
                        else if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                        {
                            angleOffset = 20f;
                            if (botPlayerScript.team == 0)
                            {
                                //! - Throw teammate towards your enemy goal -
                                if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                                {
                                    dirToTarget = gameManager.goalHoleA.transform.position - transform.position;
                                }
                                //! - Throw enemy at your own goal to keep them away -
                                else
                                {
                                    dirToTarget = gameManager.goalHoleB.transform.position - transform.position;
                                    angleOffset = -angleOffset;
                                }
                            }
                            else
                            {
                                //! - Throw teammate towards your enemy goal -
                                if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                                {
                                    dirToTarget = gameManager.goalHoleB.transform.position - transform.position;
                                    angleOffset = -angleOffset;
                                }
                                //! - Throw enemy at your own goal to keep them away -
                                else
                                {
                                    dirToTarget = gameManager.goalHoleA.transform.position - transform.position;
                                }
                            }

                            dirToTarget = dirToTarget.normalized;
                            dirToTarget = transform.InverseTransformDirection(dirToTarget);
                            dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                                * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                            dirToTarget = transform.TransformDirection(dirToTarget);
                        }
                        //! - Holding Dodgeball -
                        else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                        {
                            angleOffset = 0f;
                            if (EnemyWithGameballWithinRange(100f))
                            {
                                dirToTarget = EnemyWithGameballWithinRange(100f).transform.position - transform.position;
                                //print($"enemy: {EnemyWithGameballWithinRange(100f).transform.position}, self: {transform.position}");
                            }
                            else
                            {
                                dirToTarget = closestEnemyPlayerScript.transform.position - transform.position;
                                //print($"enemy: {closestEnemyPlayerScript.transform.position}, self: {transform.position}");
                            }
                            dirToTarget = dirToTarget.normalized;
                            dirToTarget = transform.InverseTransformDirection(dirToTarget);
                            dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                                * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                            dirToTarget = transform.TransformDirection(dirToTarget);
                        }
                        else
                        {
                            dirToTarget = transform.forward;
                        }
                    }
                }
            }
            //! - If goalie has ball move towards opponent goal -
            else
            {
                //! - Attack enemy player closest to this bot only if they are close -
                if (closestEnemyPlayerScript && !closestEnemyPlayerScript.hasNoControl &&
                    !closestEnemyPlayerScript.playerRecovering &&
                    Vector3.Distance(transform.position, closestEnemyPlayerScript.transform.position) < 20f)
                {
                    dirToTarget = closestEnemyPlayerScript.transform.position - transform.position;
                    dirToTarget = dirToTarget.normalized;
                    dirToTarget = transform.InverseTransformDirection(dirToTarget);
                    dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                        * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                    dirToTarget = transform.TransformDirection(dirToTarget);
                }
                //! - Rotate towards gameball -
                else
                {
                    dirToTarget = transform.InverseTransformDirection(gameball.transform.position - transform.position);
                }
            }
        }
        //! - Old bot rotaion -
        else
        {
            //! - Rotate towards ball or goal -
            if (isOnBall)
            {
                //! - Rotate to look at main ball -
                if (!botPlayerScript.heldEntity)
                {
                    dirToTarget = (gameball.transform.position - transform.position).normalized;
                }
                else
                {
                    //! - Holding Gameball -
                    if (botPlayerScript.heldEntity.GetComponent<GameBallScript>())
                    {
                        if (teammates.Count > 2 && FindTeammateToPassAsFielder())
                        {
                            //! - Look for a teammate to pass to -
                            Vector3 dirToTeammate = FindTeammateToPassAsFielder().transform.position - transform.position;
                            float distance = dirToTeammate.magnitude;
                            Vector3 angleAdjust = Vector3.zero;
                            if (distance > 50f)
                            {
                                angleAdjust = Vector3.up * (Mathf.Pow(distance - 50f, 1.1f) / 4f);
                            }
                            dirToTarget = (TeammatePredictedPosition(FindTeammateToPassAsFielder()) + angleAdjust - transform.position).normalized;
                        }
                        else
                        {
                            //! - Rotate to look at opposition goal -
                            //? Testing not changing angle on throw -
                            //angleOffset = CalculateAngle();
                            angleOffset = 20f;

                            if (botPlayerScript.team == 0)
                            {
                                dirToTarget = gameManager.goalHoleB.transform.position - gameManager.gameball.transform.position;
                            }
                            else
                            {
                                dirToTarget = gameManager.goalHoleA.transform.position - gameManager.gameball.transform.position;
                            }
                            if (dirToTarget.magnitude < 20f)
                            {
                                angleOffset = 50f;
                            }
                            else
                            {
                                angleOffset = 20f;
                            }
                            angleOffset = botPlayerScript.team == 0 ? -angleOffset : angleOffset;
                            dirToTarget = dirToTarget.normalized;
                            //? Testing not changing angle on throw -
                            //if (angleOffset != 0f)
                            {
                                dirToTarget = transform.InverseTransformDirection(dirToTarget);
                                dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                                    * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                                dirToTarget = transform.TransformDirection(dirToTarget);
                                //dirToTarget = Quaternion.Euler(angleOffset, 0f, 0f) * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                            }
                        }
                    }
                    //! - Holding Player -
                    else if (botPlayerScript.heldEntity.GetComponent<PlayerScript>())
                    {
                        angleOffset = 20f;
                        if (botPlayerScript.team == 0)
                        {
                            //! - Throw teammate towards your enemy goal -
                            if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                            {
                                dirToTarget = gameManager.goalHoleA.transform.position - transform.position;
                            }
                            //! - Throw enemy at your own goal to keep them away -
                            else
                            {
                                dirToTarget = gameManager.goalHoleB.transform.position - transform.position;
                                angleOffset = -angleOffset;
                            }
                        }
                        else
                        {
                            //! - Throw teammate towards your enemy goal -
                            if (botPlayerScript.heldEntity.GetComponent<PlayerScript>().team == botPlayerScript.team)
                            {
                                dirToTarget = gameManager.goalHoleB.transform.position - transform.position;
                                angleOffset = -angleOffset;
                            }
                            //! - Throw enemy at your own goal to keep them away -
                            else
                            {
                                dirToTarget = gameManager.goalHoleA.transform.position - transform.position;
                            }
                        }

                        dirToTarget = dirToTarget.normalized;
                        dirToTarget = transform.InverseTransformDirection(dirToTarget);
                        dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                            * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                        dirToTarget = transform.TransformDirection(dirToTarget);
                    }
                    //! - Holding Dodgeball -
                    else if (botPlayerScript.heldEntity.GetComponent<Dodgeball>())
                    {
                        angleOffset = 0f;
                        if (EnemyWithGameballWithinRange(100f))
                        {
                            dirToTarget = EnemyWithGameballWithinRange(100f).transform.position - transform.position;
                        }
                        else
                        {
                            dirToTarget = closestEnemyPlayerScript.transform.position - transform.position;
                        }
                        dirToTarget = dirToTarget.normalized;
                        dirToTarget = transform.InverseTransformDirection(dirToTarget);
                        dirToTarget = Quaternion.Euler(transform.InverseTransformDirection(angleOffset, 0f, 0f))
                            * botPlayerScript.GetPlanarVector(dirToTarget).normalized;
                        dirToTarget = transform.TransformDirection(dirToTarget);
                    }
                    else
                    {
                        dirToTarget = transform.forward;
                    }

                }
            }
            //! - Rotate towards enemy player -
            else
            {
                if (closestEnemyPlayerScript != null)
                {
                    //! - Rotate towards enemy -
                    dirToTarget = (closestEnemyPlayerScript.transform.position - transform.position).normalized;
                }
                else
                {
                    dirToTarget = Vector3.zero;
                }
            }
        }

        Vector3 rotationDir = transform.InverseTransformDirection(dirToTarget - botPlayerScript.sceneVCamScript.ReturnForward());
        //! - Helps rotation when target is behind character -
        if (Vector3.Angle(botPlayerScript.sceneVCamScript.ReturnForward(), dirToTarget) > 135f)
        {
            rotationDir.x = 1;
        }
        Vector2 dirToGameballInput = new Vector2(rotationDir.x, rotationDir.y);
        botPlayerScript.SimulateInputRS(dirToGameballInput);
        //! - Throw ball if rotation completed -
        if (botPlayerScript.heldEntity && timeStartedCharge != 0f &&
            (timeStartedCharge + MIN_CHARGE_TIME * 2f) < Time.time && rotationDir.magnitude < 0.01f)
        {
            FinishCharge();
        }
    }
    void RotateAsGoalie()
    {
        Vector3 dirToTarget;
        //! - Rotate to look at main ball -
        if (!botPlayerScript.heldEntity)
        {
            //! - Used for moving to goal position -
            if (!walkingToGameball || !botPlayerScript.IsInGoalBox())
            {
                //! Team A
                if (botPlayerScript.team == 0)
                {
                    dirToTarget = (transform.position - gameManager.goaliePositions[0].position).normalized;
                }
                //! Team B
                else
                {
                    dirToTarget = (transform.position - gameManager.goaliePositions[1].position).normalized;
                }
            }
            else
            {
                dirToTarget = (gameball.transform.position - transform.position).normalized;
            }
        }
        //! - Only pass if another teammate available -
        else if (teammates.Count > 1)
        {
            Vector3 dirToTeammate = FindTeammateToPassAsGoalie().transform.position - transform.position;
            float distance = dirToTeammate.magnitude;
            Vector3 angleAdjust = Vector3.zero;
            if (distance > 50f)
            {
                angleAdjust = Vector3.up * (30f * Mathf.Pow(distance - 50f, 1.1f) / 80);
                print($"angleAdjust: {angleAdjust}");
            }
            dirToTarget = (FindTeammateToPassAsGoalie().transform.position + angleAdjust - transform.position).normalized;
        }
        //! - Otherwise look at opponent goal -
        else
        {
            if (botPlayerScript.team == 0)
            {
                dirToTarget = (gameManager.goalHoleB.transform.position - gameManager.gameball.transform.position).normalized;
            }
            else
            {
                dirToTarget = (gameManager.goalHoleA.transform.position - gameManager.gameball.transform.position).normalized;
            }
        }
        Vector3 rotationDir = transform.InverseTransformDirection(dirToTarget - botPlayerScript.sceneVCamScript.ReturnForward());
        //! - Helps rotation when target is behind character -
        if (Vector3.Angle(botPlayerScript.sceneVCamScript.ReturnForward(), dirToTarget) > 135f)
        {
            rotationDir.x = 1;
        }
        Vector2 dirToGameballInput = new Vector2(rotationDir.x, rotationDir.y);
        botPlayerScript.SimulateInputRS(dirToGameballInput);
        //! - Throw ball if rotation completed -
        if (botPlayerScript.heldEntity && timeStartedCharge != 0f &&
            (timeStartedCharge + MIN_CHARGE_TIME * 2f < Time.time) && rotationDir.magnitude < 0.01f)
        {
            FinishCharge();
        }
    }
    void JumpOffWall()
    {
        if (botPlayerScript.isOnWall && timeOnWall == 0f)
        {
            timeOnWall = Time.time;
        }
        if (botPlayerScript.isOnWall && timeOnWall + ONWALL_DELAY_TIME < Time.time)
        {
            botPlayerScript.SimulateInputA();
            timeJumped = Time.time;
            timeOnWall = 0f;
        }
    }
    void JumpAsFielder()
    {
        //! - Jump when bot is not holding and on ground -
        if (gameManager.newBotMovement)
        {
            float distance;
            float horizontalDistance;
            float height;
            if (movingTargetState == GAMEBALL_TARGET)
            {
                distance = (gameball.transform.position - transform.position).magnitude;
                horizontalDistance = FindHorizontalDistanceFromThisBot(gameball.transform);
                height = gameball.transform.position.y - transform.position.y;
                //! - First Jump -
                if ((botPlayerScript.isOnGround && (horizontalDistance > 20f && height > -5f || height > 10f))
                    || (botPlayerScript.isOnWall && (horizontalDistance > 10f || height > 10f)))
                {
                    botPlayerScript.SimulateInputA();
                    timeJumped = Time.time;
                }
                //! - Second Jump -
                else if (!botPlayerScript.isOnGround && (horizontalDistance > 35f && height > 0f || horizontalDistance > 55f || height > 10f)
                    && timeJumped != 0f && timeJumped + 0.3f + INPUT_DELAY_TIME < Time.time)
                {
                    botPlayerScript.SimulateInputA();
                    timeJumped = Time.time;
                }
            }
            else if (movingTargetState == TEAM_A_GOAL_TARGET)
            {
                distance = (gameManager.goalHoleA.transform.position - transform.position).magnitude;
                height = gameManager.goalHoleA.transform.position.y - transform.position.y;
                //! - First Jump -
                if ((botPlayerScript.isOnGround || botPlayerScript.isOnWall) && distance > 10f)
                {
                    botPlayerScript.SimulateInputA();
                    timeJumped = Time.time;
                }
                //! - Second Jump -
                else if (!botPlayerScript.isOnGround && timeJumped != 0f
                    && timeJumped + INPUT_DELAY_TIME + 0.3f < Time.time && height > 0f)
                {
                    botPlayerScript.SimulateInputA();
                    timeJumped = 0f;
                }
            }
            else if (movingTargetState == TEAM_B_GOAL_TARGET)
            {
                distance = (gameManager.goalHoleB.transform.position - transform.position).magnitude; ;
                height = gameManager.goalHoleB.transform.position.y - transform.position.y; ;
                //! - First Jump -
                if ((botPlayerScript.isOnGround || botPlayerScript.isOnWall) && distance > 10f)
                {
                    botPlayerScript.SimulateInputA();
                    timeJumped = Time.time;
                }
                //! - Second Jump -
                else if (!botPlayerScript.isOnGround && timeJumped != 0f
                    && timeJumped + INPUT_DELAY_TIME + 0.3f < Time.time && height > 0f)
                {
                    botPlayerScript.SimulateInputA();
                    timeJumped = 0f;
                }
            }
            else if (movingTargetState == CLOSEST_ENEMY_TARGET)
            {
                if (closestEnemyPlayerScript != null)
                {
                    distance = (closestEnemyPlayerScript.transform.position - transform.position).magnitude;
                    horizontalDistance = FindHorizontalDistanceFromThisBot(closestEnemyPlayerScript.transform);
                    height = closestEnemyPlayerScript.transform.position.y - transform.position.y;
                    //! - First Jump -
                    if ((botPlayerScript.isOnGround && (horizontalDistance > 20f && height > -5f || height > 10f))
                    || (botPlayerScript.isOnWall && (horizontalDistance > 10f || height > 10f)))
                    {
                        botPlayerScript.SimulateInputA();
                        timeJumped = Time.time;
                    }
                    //! - Second Jump -
                    else if (!botPlayerScript.isOnGround && (horizontalDistance > 35f && height > 0f || horizontalDistance > 55f || height > 10f)
                        && timeJumped != 0f && timeJumped + 0.3f + INPUT_DELAY_TIME < Time.time)
                    {
                        botPlayerScript.SimulateInputA();
                        timeJumped = 0f;
                    }
                }
            }
            else if (movingTargetState == CLOSEST_ENEMY_TO_GAMEBALL_TARGET)
            {
                if (FindEnemyClosestToGameball() != null)
                {
                    distance = (FindEnemyClosestToGameball().transform.position - transform.position).magnitude;
                    horizontalDistance = FindHorizontalDistanceFromThisBot(FindEnemyClosestToGameball().transform);
                    height = FindEnemyClosestToGameball().transform.position.y - transform.position.y;
                    //! - First Jump -
                    if ((botPlayerScript.isOnGround && (horizontalDistance > 20f && height > -5f || height > 10f))
                    || (botPlayerScript.isOnWall && (horizontalDistance > 10f || height > 10f)))
                    {
                        botPlayerScript.SimulateInputA();
                        timeJumped = Time.time;
                    }
                    //! - Second Jump -
                    else if (!botPlayerScript.isOnGround && (horizontalDistance > 35f && height > 0f || horizontalDistance > 55f || height > 10f)
                        && timeJumped != 0f && timeJumped + 0.3f + INPUT_DELAY_TIME < Time.time)
                    {
                        botPlayerScript.SimulateInputA();
                        timeJumped = 0f;
                    }
                }
            }
            else if (movingTargetState == AWAY_FROM_CLOSEST_ENEMY_TARGET_TO_GAMEBALL)
            {
                if (FindEnemyClosestToGameball() != null)
                {
                    distance = (FindEnemyClosestToGameball().transform.position - transform.position).magnitude;
                    horizontalDistance = FindHorizontalDistanceFromThisBot(FindEnemyClosestToGameball().transform);
                    height = FindEnemyClosestToGameball().transform.position.y - transform.position.y;
                    //! - First Jump -
                    if ((botPlayerScript.isOnGround && horizontalDistance < 20f)
                    || (botPlayerScript.isOnWall && horizontalDistance < 20f))
                    {
                        botPlayerScript.SimulateInputA();
                        timeJumped = Time.time;
                    }
                    //! - Second Jump -
                    else if (!botPlayerScript.isOnGround && horizontalDistance < 35f
                        && timeJumped != 0f && timeJumped + 0.3f + INPUT_DELAY_TIME < Time.time)
                    {
                        botPlayerScript.SimulateInputA();
                        timeJumped = 0f;
                    }
                }
            }
        }
        else
        {
            //! old code
            //! - Chasing ball -
            if (isOnBall)
            {
                if (!botPlayerScript.heldEntity)
                {
                    //! - First Jump -
                    if ((botPlayerScript.isOnGround || botPlayerScript.isOnWall) &&
                        (gameball.transform.position - transform.position).magnitude > 25f)
                    {
                        botPlayerScript.SimulateInputA();
                        timeJumped = Time.time;
                    }
                    //! - Second Jump -
                    else if (!botPlayerScript.isOnGround && (gameball.transform.position - transform.position).magnitude > 35f
                         && timeJumped != 0f && timeJumped + INPUT_DELAY_TIME < Time.time)
                    {
                        botPlayerScript.SimulateInputA();
                        timeJumped = Time.time;
                    }
                }
                else
                {
                    float distance;
                    float height;
                    if (botPlayerScript.team == 0)
                    {
                        distance = (gameManager.goalHoleB.transform.position - transform.position).magnitude;
                        height = gameManager.goalHoleB.transform.position.y;
                    }
                    else
                    {
                        distance = (gameManager.goalHoleA.transform.position - transform.position).magnitude;
                        height = gameManager.goalHoleA.transform.position.y;
                    }
                    //! - First Jump -
                    if ((botPlayerScript.isOnGround || botPlayerScript.isOnWall) &&
                        distance > 10f)
                    {
                        botPlayerScript.SimulateInputA();
                        timeJumped = Time.time;
                    }
                    //! - Second Jump -
                    else if (!botPlayerScript.isOnGround && timeJumped != 0f
                        && timeJumped + INPUT_DELAY_TIME + 0.3f < Time.time && transform.position.y < height)
                    {
                        print("Double Jumped!");
                        botPlayerScript.SimulateInputA();
                        timeJumped = 0f;
                    }
                }
            }
            //! - Chasing enemy player -
            else
            {
                if (botPlayerScript.isOnGround || botPlayerScript.isOnWall)
                {
                    if (closestEnemyPlayerScript != null)
                    {
                        if (!botPlayerScript.heldEntity && (botPlayerScript.isOnGround || botPlayerScript.isOnWall))
                        {
                            if ((closestEnemyPlayerScript.transform.position - transform.position).magnitude > 30f)
                            {
                                botPlayerScript.SimulateInputA();
                                timeJumped = Time.time;
                            }
                            else if (!botPlayerScript.heldEntity && !botPlayerScript.isOnGround &&
                                (closestEnemyPlayerScript.transform.position - transform.position).magnitude > 45f)
                            {
                                botPlayerScript.SimulateInputA();
                                timeJumped = 0f;
                            }
                        }
                    }
                }
            }

            //if (isOnBall)
            //{
            //    if (!botPlayerScript.heldEntity)
            //    {
            //        //! - First Jump -
            //        if ((botPlayerScript.isOnGround || botPlayerScript.isOnWall) &&
            //            (gameball.transform.position - transform.position).magnitude > 25f)
            //        {
            //            botPlayerScript.SimulateInputA();
            //            timeJumped = Time.time;
            //        }
            //        //! - Second Jump -
            //        else if (!botPlayerScript.isOnGround && (gameball.transform.position - transform.position).magnitude > 35f
            //             && timeJumped != 0f && timeJumped + INPUT_DELAY_TIME < Time.time)
            //        {
            //            botPlayerScript.SimulateInputA();
            //            timeJumped = Time.time;
            //        }
            //    }
            //    else
            //    {
            //        float distance;
            //        float height;
            //        if (botPlayerScript.team == 0)
            //        {
            //            distance = (gameManager.goalHoleB.transform.position - transform.position).magnitude;
            //            height = gameManager.goalHoleB.transform.position.y;
            //        }
            //        else
            //        {
            //            distance = (gameManager.goalHoleA.transform.position - transform.position).magnitude;
            //            height = gameManager.goalHoleA.transform.position.y;
            //        }
            //        //! - First Jump -
            //        if ((botPlayerScript.isOnGround || botPlayerScript.isOnWall) &&
            //            distance > 10f)
            //        {
            //            botPlayerScript.SimulateInputA();
            //            timeJumped = Time.time;
            //        }
            //        //! - Second Jump -
            //        else if (!botPlayerScript.isOnGround && timeJumped != 0f
            //            && timeJumped + INPUT_DELAY_TIME + 0.3f < Time.time && transform.position.y < height)
            //        {
            //            print("Double Jumped!");
            //            botPlayerScript.SimulateInputA();
            //            timeJumped = 0f;
            //        }
            //    }
            //}
            ////! - Chasing enemy player -
            //else
            //{
            //    if (botPlayerScript.isOnGround || botPlayerScript.isOnWall)
            //    {
            //        if (closestEnemyPlayerScript != null)
            //        {
            //            if (!botPlayerScript.heldEntity && (botPlayerScript.isOnGround || botPlayerScript.isOnWall))
            //            {
            //                if ((closestEnemyPlayerScript.transform.position - transform.position).magnitude > 30f)
            //                {
            //                    botPlayerScript.SimulateInputA();
            //                    timeJumped = Time.time;
            //                }
            //                else if (!botPlayerScript.heldEntity && !botPlayerScript.isOnGround &&
            //                    (closestEnemyPlayerScript.transform.position - transform.position).magnitude > 45f)
            //                {
            //                    botPlayerScript.SimulateInputA();
            //                    timeJumped = 0f;
            //                }
            //            }
            //        }
            //    }
            //}
        }
    }
    void DiveAsFielder()
    {
        //! - Dash when bot is not holding and not on ground after a delay -
        //! - Chsing Ball -
        if ((timeOfAction + INPUT_DELAY_TIME + 2f < Time.time) && !botPlayerScript.heldEntity)
        {
            if (movingTargetState == GAMEBALL_TARGET && rotationTargetState == GAMEBALL_TARGET)
            {
                if ((gameball.transform.position - transform.position).magnitude < 30f)
                {
                    botPlayerScript.SimulateInputLB();
                    timeJumped = 0f;
                }
            }
            else if (movingTargetState == CLOSEST_ENEMY_TARGET && rotationTargetState == CLOSEST_ENEMY_TARGET)
            {
                if (closestEnemyPlayerScript != null && (closestEnemyPlayerScript.transform.position - transform.position).magnitude < 30f)
                {
                    botPlayerScript.SimulateInputLB();
                    timeJumped = 0f;
                }
            }
            else if (movingTargetState == CLOSEST_ENEMY_TO_GAMEBALL_TARGET && rotationTargetState == CLOSEST_ENEMY_TO_GAMEBALL_TARGET)
            {
                if (FindEnemyClosestToGameball() != null && (FindEnemyClosestToGameball().transform.position - transform.position).magnitude < 30f)
                {
                    botPlayerScript.SimulateInputLB();
                    timeJumped = 0f;
                }
            }

            //! - Old dive logic -
            #region old dive logic
            ////! - Dive at gameball if closest team player to gameball, or if closest teammate is not in a state to grab gameball -
            //if (FindTeammateClosestToGameball() && FindTeammateClosestToGameball() == botPlayerScript ||
            //        (FindTeammateClosestToGameball() && (FindTeammateClosestToGameball().hasNoControl || FindTeammateClosestToGameball().heldEntity)))
            //{
            //    float distToGameball = (gameball.transform.position - transform.position).magnitude;
            //    if (!botPlayerScript.heldEntity && distToGameball < 20f)
            //    {
            //        float goalToPlayerDistance;
            //        float goalToBallDistance;
            //        if (botPlayerScript.team == 0)
            //        {
            //            goalToPlayerDistance = Mathf.Abs(gameManager.goaliePositions[1].position.z - transform.position.z);
            //            goalToBallDistance = Mathf.Abs(gameManager.goaliePositions[1].position.z - gameball.transform.position.z);
            //        }
            //        else
            //        {
            //            goalToPlayerDistance = Mathf.Abs(gameManager.goaliePositions[0].position.z - transform.position.z);
            //            goalToBallDistance = Mathf.Abs(gameManager.goaliePositions[0].position.z - gameball.transform.position.z);
            //        }
            //        float diff = goalToPlayerDistance - goalToBallDistance;

            //        //if (diff > 0)
            //        {
            //            botPlayerScript.SimulateInputLB();
            //            timeJumped = 0f;
            //        }
            //    }
            //}
            ////! - Chasing enemy player -
            //else
            //{
            //    if (closestEnemyPlayerScript != null)
            //    {
            //        if ((closestEnemyPlayerScript.transform.position - transform.position).magnitude < 20f)
            //        {
            //            botPlayerScript.SimulateInputLB();
            //            timeJumped = 0f;
            //        }
            //    }
            //}
            #endregion
        }
    }
    void Slap()
    {
        if (canSearchForSlap)
        {
            slapColliders = Physics.OverlapBox(transform.position + botPlayerScript.sceneVCamScript.ReturnForward()
                * SLAP_DISTANCE, SLAP_BOX, transform.rotation, 1 << 3);
            bool canSlap = false;
            for (int i = 0; i < slapColliders.Length; i++)
            {
                //! - Don't slap if teammate detected -
                if (slapColliders[i].GetComponent<PlayerScript>().team == botPlayerScript.team &&
                    slapColliders[i].gameObject != gameObject)
                {
                    break;
                }
                //! - Check if any of the colliders are players on opposing team -
                if (!canSlap && slapColliders[i].GetComponent<PlayerScript>().team != botPlayerScript.team)
                {
                    canSlap = true;
                }
                //! - At the end of the loop see if target available to slap -
                if (i == slapColliders.Length - 1 && canSlap)
                {
                    botPlayerScript.SimulateInputX();
                    StartCoroutine(SlapCoolDown());
                    timeOfAction = Time.time;
                }
            }
        }
        //foreach (Collider hitCol in slapColliders)
        //{
        //    //! - Check if any of the colliders are players on opposing team -
        //    if (hitCol.GetComponent<PlayerScript>().team != botPlayerScript.team && hitCol.gameObject != gameObject)
        //    {
        //        botPlayerScript.SimulateInputX();
        //        timeSlapped = Time.time;
        //        timeOfAction = Time.time;
        //    }
        //}

    }
    void SpawnOrDespawnDodgeball()
    {
        //! - Spawning at the start of the game -
        if (gameManager.gameStarted && botPlayerScript.heldEntity == null && !botPlayerScript.isDodgeballSpawned && lookToThrowDodgeball)
        {
            botPlayerScript.SimulateInputY();
        }

        if (botPlayerScript.isDodgeballSpawned && !lookToThrowDodgeball &&
            botPlayerScript.heldEntity && botPlayerScript.heldEntity.GetComponent<Dodgeball>())
        {
            botPlayerScript.SimulateInputY();
        }
    }
    void RecallDodgeball()
    {
        if (!botPlayerScript.isGoalie)
        {
            //? + 0.8f is important value here as there is a LT cal back protection on playerScript for 0.5f seconds
            if (timeThrownDodgeball != 0f && timeThrownDodgeball + 0.8f < Time.time)
            {
                timeThrownDodgeball = 0f;
            }
            if (botPlayerScript.heldEntity == null && botPlayerScript.isDodgeballSpawned && timeThrownDodgeball == 0f && 
                lookToThrowDodgeball && movingTargetState != GAMEBALL_TARGET)
            {
                botPlayerScript.triggerValues.x = 1f;
            }
        }
        else
        {
            if (timeThrownDodgeball != 0f || botPlayerScript.triggerValues.x != 0f || lookToThrowDodgeball)
            timeThrownDodgeball = 0f;
            botPlayerScript.triggerValues.x = 0f;
            lookToThrowDodgeball = false;
        }
    }
    void CheckGrab()
    {
        if (!botPlayerScript.isGoalie)
        {
            //! - Testing bots with dodgeball changed 16/05 - 
            //! - codeReview: could just do one catch check and use if check for if collider is what you want
            if (gameManager.testingBotDodgeball)
            {
                //! - Check for gameball -
                grabColliders = Physics.BoxCastAll(transform.position, GRAB_BOX,
                    botPlayerScript.sceneVCamScript.ReturnForward(),
                    botPlayerScript.sceneVCamScript.transform.rotation, GRAB_DISTANCE * 2f, 1 << 6);
                foreach (RaycastHit hitRay in grabColliders)
                {
                    if (hitRay.collider.gameObject != gameObject)
                    {
                        //! - Check if any of the colliders is the gameball -
                        if (hitRay.collider.GetComponent<GameBallScript>() && (!gameManager.GameballInShieldArea(team: botPlayerScript.team)
                            || !DivingTowardsEnemyGoals(team: botPlayerScript.team)))
                        {
                            botPlayerScript.SimulateInputRB();
                            botPlayerScript.triggerValues.x = 0f;
                            timeOfAction = Time.time;
                        }
                    }
                }
                if (botPlayerScript.IsBeingCalledBack())
                {
                    //! - check for dodgeball -
                    dodgeballColliders = Physics.OverlapSphere(transform.position, 5f, 1 << 9);
                    foreach (Collider hitCol in dodgeballColliders)
                    {
                        if (hitCol.gameObject != gameObject)
                        {
                            //! - Check if any of the colliders is a dodgeball -
                            if (hitCol.GetComponent<Dodgeball>())
                            {
                                botPlayerScript.SimulateInputRB();
                                botPlayerScript.triggerValues.x = 0f;
                                timeOfAction = Time.time;
                            }
                        }
                    }
                }
            }
            else
            {
                if (isOnBall)
                {
                    grabColliders = Physics.BoxCastAll(transform.position, GRAB_BOX,
                        botPlayerScript.sceneVCamScript.ReturnForward(),
                        botPlayerScript.sceneVCamScript.transform.rotation, GRAB_DISTANCE * 2f, 1 << 6);
                    foreach (RaycastHit hitRay in grabColliders)
                    {
                        if (hitRay.collider.gameObject != gameObject)
                        {
                            //! - Check if any of the colliders are players or main ball -
                            if (hitRay.collider.gameObject.tag == "Flag")
                            {
                                botPlayerScript.SimulateInputRB();
                                timeOfAction = Time.time;
                            }
                        }
                    }
                }
            }
        }
        else
        {
            //! Check if main ball is in goal
            if (botPlayerScript.team == 0)
            {
                saveColliders = Physics.OverlapBox(GAMEBALL_CHECK_BOX_TEAM_A, GAMEBALL_CHECK_BOX,
                    gameManager.goalHoleA.transform.rotation, 1 << 6);
            }
            else
            {
                saveColliders = Physics.OverlapBox(GAMEBALL_CHECK_BOX_TEAM_B, GAMEBALL_CHECK_BOX,
                    gameManager.goalHoleB.transform.rotation, 1 << 6);
            }

            bool gameballInGoals = false;
            int i = 0;
            foreach (Collider collider in saveColliders)
            {
                //! - Check if any of the colliders are the main ball -
                if (collider.gameObject != gameObject && collider.gameObject.tag == "Flag")
                {
                    gameballInGoals = true;
                }
                i++;
            }

            if (gameballInGoals)
            {
                if (!walkingToGameball) walkingToGameball = true;
                if (!botPlayerScript.heldEntity)
                {
                    grabColliders = Physics.BoxCastAll(transform.position, GRAB_BOX,
                        botPlayerScript.sceneVCamScript.ReturnForward(),
                        botPlayerScript.sceneVCamScript.transform.rotation, GRAB_DISTANCE, 1 << 6);
                    foreach (RaycastHit hitRay in grabColliders)
                    {
                        if (hitRay.collider.gameObject != gameObject)
                        {
                            //! - Check if any of the colliders are players or main ball -
                            if (hitRay.collider.gameObject.tag == "Flag")
                            {
                                botPlayerScript.SimulateInputRB();
                                //todo This need to be here?
                                timeOfAction = Time.time;
                            }
                        }
                    }
                }
            }
            else
            {
                walkingToGameball = false;
            }
        }
    }
    void CheckSave()
    {
        if (!botPlayerScript.isDiving)
        {
            //! Check if main ball passes through the boxCheck 
            if (botPlayerScript.team == 0)
            {
                saveColliders = Physics.OverlapBox(gameManager.goalHoleA.transform.position, DIVE_BOX,
                    gameManager.goalHoleA.transform.rotation, 1 << 6);
            }
            else
            {
                saveColliders = Physics.OverlapBox(gameManager.goalHoleB.transform.position, DIVE_BOX,
                    gameManager.goalHoleB.transform.rotation, 1 << 6);
            }

            foreach (Collider collider in saveColliders)
            {
                if (collider.gameObject != gameObject)
                {
                    //! - Check if any of the colliders are the main ball -
                    if (collider.gameObject.tag == "Flag")
                    {
                        //! - Create dive direction and dive -
                        dirToBall = collider.transform.position - transform.position;
                        if (timeDetectedShotBall == 0f)
                        {
                            timeDetectedShotBall = Time.time;
                        }
                    }
                }
            }
        }
    }
    void DiveReactionTimeDelay()
    {
        if (timeDetectedShotBall != 0f && timeDetectedShotBall + reactionTime < Time.time)
        {
            timeDetectedShotBall = 0f;
            botPlayerScript.DiveAsBot(dirToBall);
            //todo This need to be here?
            timeOfAction = Time.time;
        }
    }
    void CheckThrow()
    {
        if (botPlayerScript.heldEntity)
        {
            if (timeStartedCharge == 0f && !botPlayerScript.isDiving)
            {
                //! - Delay this so that charging starts after input protection in playerScript -
                timeDelayForThrow = Time.time;
                timeStartedCharge = Time.time;
            }
            else if (timeDelayForThrow != 0f && timeDelayForThrow + INPUT_DELAY_TIME < Time.time)
            {
                timeDelayForThrow = 0f;
                StartCharge();
            }
            //! - Throw when timer for max charge hits OR if enemy is close and has done some throw charging -
            else if (!gameManager.middleWall.activeSelf && (timeStartedCharge + MAX_CHARGE_TIME < Time.time ||
                (timeStartedCharge + MAX_CHARGE_TIME / 2f < Time.time && closestEnemyPlayerScript != null &&
                Vector3.Distance(transform.position, closestEnemyPlayerScript.transform.position) < 3f)))
            {
                FinishCharge();
            }
        }
        else
        {
            FinishCharge();
        }
    }
    void CheckIfTeamGoalieHasBall()
    {
        //! - Check if team goalie has ball -
        foreach (PlayerScript player in teammates)
        {
            if (player.heldEntity == gameball && player.isGoalie)
            {
                teamGoalieHasBall = true;
                return;
            }
        }
        teamGoalieHasBall = false;
    }
    void CheckIfEnemyGoalieHasBall()
    {
        //! - Check if team goalie has ball -
        foreach (PlayerScript player in enemyPlayers)
        {
            if (player.heldEntity == gameball && player.isGoalie)
            {
                enemyGoalieHasBall = true;
                return;
            }
        }
        enemyGoalieHasBall = false;
    }
    void CheckIfItWasDodgeballThrown()
    {
        if (botPlayerScript.heldEntity && botPlayerScript.heldEntity.GetComponent<Dodgeball>())
        {
            //! - Used to make sure LT is not held down constantly -
            timeThrownDodgeball = Time.time;
            botPlayerScript.triggerValues.x = 0f;
        }
    }
    void StartCharge()
    {
        print("BOT CHARGE START");
        timeStartedCharge = Time.time;
        if (botPlayerScript.heldEntity.layer == LayerMask.NameToLayer("Player"))
        {
            timeStartedCharge -= 1f;
        }
        //angleOffset = CalculateAngle();

        botPlayerScript.SimulateInputRTPress();
        print($"isCharge: {botPlayerScript.isCharging}");
    }
    void FinishCharge()
    {
        print("BOT CHARGE FINISH");
        CheckIfItWasDodgeballThrown();
        botPlayerScript.SimulateInputRTRelease();
        sideAngleOffset = 0f;
        timeStartedCharge = 0f;
        timeOfAction = Time.time;
    }
    bool OnlyFielderOnTeam()
    {
        foreach (PlayerScript teammate in teammates)
        {
            if (teammate != botPlayerScript && !teammate.isGoalie)
            {
                return false;
            }
        }
        return true;
    }
    public PlayerScript FindTeammateToPassAsFielder()
    {
        //! - Only pass to teammate if far from opposition goal -
        int team = botPlayerScript.team;
        float distanceToGoal;
        if (team == 0)
        {
            distanceToGoal = Vector3.Distance(gameManager.goalHoleB.transform.position, transform.position);
        }
        else
        {
            distanceToGoal = Vector3.Distance(gameManager.goalHoleA.transform.position, transform.position);
        }
        if (distanceToGoal > 100f)
        {
            PlayerScript targetTeammate = null;
            bool nonBotTeammateFound = false;
            float shortestDistance = FIELD_DISTANCE;
            float newDistance;
            Vector3 newDirection;
            foreach (PlayerScript teammate in teammates)
            {
                if (teammate)
                {
                    if (!teammate.isGoalie)
                    {
                        //! - Calculate closest teammate to goalie -    
                        newDirection = teammate.transform.position - botPlayerScript.transform.position;
                        newDistance = newDirection.magnitude;
                        //! - Find closest non bot teammate -
                        if (nonBotTeammateFound)
                        {
                            //! - Calculate closest teammate to goalie -    
                            if (newDistance < shortestDistance && !teammate.isBot)
                            {
                                if (team == 0)
                                {
                                    if (newDirection.z > -10f)
                                    {
                                        shortestDistance = newDistance;
                                        targetTeammate = teammate;
                                    }
                                }
                                else
                                {
                                    if (newDirection.z < 10f)
                                    {
                                        shortestDistance = newDistance;
                                        targetTeammate = teammate;
                                    }
                                }
                            }
                        }
                        //! - Find closest bot teammate -
                        else
                        {
                            //! - Calculate closest teammate to goalie -    
                            if (newDistance < shortestDistance)
                            {
                                if (team == 0)
                                {
                                    if (newDirection.z > 0f)
                                    {
                                        shortestDistance = newDistance;
                                        targetTeammate = teammate;
                                        if (!teammate.isBot)
                                        {
                                            //! - Prioritize non bot player to pass to -
                                            nonBotTeammateFound = true;
                                        }
                                    }
                                }
                                else
                                {
                                    if (newDirection.z < 0f)
                                    {
                                        shortestDistance = newDistance;
                                        targetTeammate = teammate;
                                        if (!teammate.isBot)
                                        {
                                            //! - Prioritize non bot player to pass to -
                                            nonBotTeammateFound = true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return targetTeammate;
        }
        //! - Otherwise go for a goal shot -
        else
        {
            return null;
        }

    }
    PlayerScript FindTeammateToPassAsGoalie()
    {
        PlayerScript targetTeammate = null;
        List<GameObject> players = State.players;
        bool nonBotTeammateFound = false;
        int team = botPlayerScript.team;
        float shortestDistance = FIELD_DISTANCE;
        float newDistance;
        foreach (GameObject player in players)
        {
            if (player)
            {
                PlayerScript playerScript = player.GetComponent<PlayerScript>();
                if (playerScript.team == team && !playerScript.isGoalie)
                {
                    //! - Prioritize non bot player to pass to -
                    if (!playerScript.isBot && !nonBotTeammateFound)
                    {
                        targetTeammate = playerScript;
                        nonBotTeammateFound = true;
                    }
                    //! - Find closest non bot teammate -
                    if (nonBotTeammateFound)
                    {
                        //! - Calculate closest teammate to goalie -    
                        newDistance = (botPlayerScript.transform.position - playerScript.transform.position).magnitude;
                        if (newDistance < shortestDistance && !playerScript.isBot)
                        {
                            shortestDistance = newDistance;
                            targetTeammate = playerScript;
                        }
                    }
                    //! - Find closest bot teammate -
                    else
                    {
                        //! - Calculate closest teammate to goalie -    
                        newDistance = (botPlayerScript.transform.position - playerScript.transform.position).magnitude;
                        if (newDistance < shortestDistance)
                        {
                            shortestDistance = newDistance;
                            targetTeammate = playerScript;
                        }
                    }
                }
            }
        }
        return targetTeammate;
    }
    PlayerScript FindNearestPlayerTeammate()
    {
        //! - Find closest non bot teammate -
        int team = botPlayerScript.team;
        PlayerScript closestTeammate = null;
        float shortestDistance = FIELD_DISTANCE;
        float newDistance;
        foreach (PlayerScript teammate in teammates)
        {
            if (!teammate.isGoalie && !teammate.isBot)
            {
                //! - Calculate closest teammate -    
                newDistance = (teammate.transform.position - botPlayerScript.transform.position).magnitude;
                //! - Calculate closest teammate -    
                if (newDistance < shortestDistance)
                {
                    shortestDistance = newDistance;
                    closestTeammate = teammate;
                }
            }
        }
        return closestTeammate;
    }
    PlayerScript FindTeammateClosestToGameball()
    {
        //! - Find teammate closest to gameball -
        PlayerScript targetTeammate = null;
        float shortestDistance = FIELD_DISTANCE;
        float newDistance;
        foreach (PlayerScript teammate in teammates)
        {
            if (!teammate.isGoalie)
            {
                //! - Calculate closest teammate -    
                newDistance = (teammate.transform.position - gameManager.gameball.transform.position).magnitude;
                //! - Calculate closest teammate -    
                if (newDistance < shortestDistance)
                {
                    shortestDistance = newDistance;
                    targetTeammate = teammate;
                }
            }
        }
        return targetTeammate;
    }
    PlayerScript FindEnemyClosestToGameball()
    {
        //! - Returns enemy that is knocked or recovering closest to gameball -
        PlayerScript targetEnemy = null;
        float shortestDistance = FIELD_DISTANCE;
        float newDistance;
        foreach (PlayerScript enemy in enemyPlayers)
        {
            if (!enemy.isGoalie && !enemy.hasNoControl && !enemy.playerRecovering)
            {
                //! - Calculate closest teammate -    
                newDistance = (enemy.transform.position - gameManager.gameball.transform.position).magnitude;
                //! - Calculate closest teammate -    
                if (newDistance < shortestDistance)
                {
                    shortestDistance = newDistance;
                    targetEnemy = enemy;
                }
            }
        }
        return targetEnemy;
    }
    float FindTeammateDistanceToGoal(PlayerScript player, bool ownGoal)
    {
        float distance;
        //! - Returns distance of input player to goals -
        if (botPlayerScript.team == 0 && ownGoal || botPlayerScript.team == 1 && !ownGoal)
        {
            distance = Vector3.Distance(gameManager.goalHoleA.transform.position, player.transform.position);
        }
        else
        {
            distance = Vector3.Distance(gameManager.goalHoleB.transform.position, player.transform.position);
        }
        return distance;
    }
    Vector3 TeammatePredictedPosition(PlayerScript playerScript)
    {
        //! - Teammate predicted linear position after 60 frames using current velocity -
        Vector3 positionCheck = playerScript.transform.position;
        if (playerScript.GetComponent<Rigidbody>())
        {
            Rigidbody playerRb = playerScript.GetComponent<Rigidbody>();
            //! - Predict the position using teammate velocity -
            for (int i = 0; i < 60f; i++)
            {
                positionCheck += playerRb.velocity * Time.fixedDeltaTime;
            }
        }
        return positionCheck;
    }
    Vector3 PredictPosition(GameObject target, float distance, float speed)
    {
        //! Predict position of target by using its linear velocity -
        Vector3 position = target.transform.position;
        float time = distance / speed;
        if (target.GetComponent<Rigidbody>())
        {
            //! - iterate through and add velocity to original position -
            //! - Iterate count is calculated by time / time step (timestep = 0.02) -
            for (int i = 0; i < time / Time.fixedDeltaTime; i++)
            {
                position += target.GetComponent<Rigidbody>().velocity * Time.fixedDeltaTime;
            }
        }
        return position;
    }
    void CheckEnemyPlayer()
    {
        float distance = 400f; //! Full field distance = 256, add some to make sure all players included.
        float newDistance;
        if (enemyPlayers.Count == 0)
        {
            closestEnemyPlayerScript = null;
        }
        else
        {
            foreach (PlayerScript enemy in enemyPlayers)
            {
                if (!enemy.isGoalie)
                {
                    newDistance = (enemy.transform.position - transform.position).magnitude;
                    if (newDistance < distance)
                    {
                        distance = newDistance;
                        closestEnemyPlayerScript = enemy;
                    }
                }

            }
        }
    }
    PlayerScript EnemyWithGameballWithinRange(float distance)
    {
        if (enemyPlayers.Count == 0)
        {
            return null;
        }
        else
        {
            foreach (PlayerScript enemy in enemyPlayers)
            {
                if (enemy.heldEntity && enemy.heldEntity.GetComponent<GameBallScript>() &&
                    Vector3.Distance(enemy.transform.position, transform.position) < distance)
                {
                    return enemy;
                }
            }
            return null;
        }
    }
    bool GameballHeadingTowardsGoal()
    {
        //! - Check if ball is projected to hit goalie at current velocity -
        Rigidbody gameballRb = State.gameManager.gameball.GetComponent<Rigidbody>();
        if (!gameballRb)
        {
            //! - return false if rb is null (a player is holding it) -
            return false;
        }
        Vector3 positionCheck = gameballRb.transform.position;
        Vector3 goalPosition;
        //! Team A
        if (botPlayerScript.team == 0)
        {
            goalPosition = gameManager.goaliePositions[0].position + Vector3.up * 5f;
        }
        //! Team B
        else
        {
            goalPosition = gameManager.goaliePositions[1].position + Vector3.up * 5f;
        }
        if (Vector3.Distance(gameballRb.transform.position, goalPosition) < FIELD_DISTANCE / 2)
        {
            for (int i = 0; i < 60; i++)
            {
                positionCheck += (gameballRb.velocity + Physics.gravity) * Time.fixedDeltaTime;
                if (Vector3.Distance(positionCheck, goalPosition) < 4f)
                {
                    return true;
                }
            }
        }
        return false;
    }
    float CalculateAngle()
    {
        //! - Calculation Explanation -
        #region Calculation Explanation
        // Force (in this case acceleration) being applied (ignoring drag) to soccerball at constant: 
        // a(t) = 0i - 16j, (i represents x axis, j represents y axis) [x-axis is technically z-axis as it's forward but using x for simplicity]
        // Differentiate with intial conditioons as u = |v(0)| (u is the initial speed at throw), to obtain velocity vector
        //? NOTE: impulse on throw = 6000Ns = 6000kg.m/s /// u = impulse divided by ball mass = (6000kg.m/s) / (90kg) = 66.67m/s
        // v(t) = [u*cos(theta)]i + [u*sin(theta) - 16t]j
        // Differentiate again with intial conditioons as d(0) = 0i + 4j (main ball is 4 units in the air prior to being thrown),
        // to obtain velocity vector
        // d(t) = [u*cos(theta)*t]i + [4 + u*sin(theta)*t - 8t^2]
        //? Therefore, z(t) = u*cos(theta)*t and y(t) = 4 + u*sin(theta)*t - 8t^2
        // pretty impossible to rearrange for theta from here unfortunately >:(
        // ^This would have allowed to sub in z value and solve for angle which is WAY cleaner
        // Calculate for z(t) when y(t) = 3.5, u = 67 and theta = 0 (when throwing at a flat angle and
        // y = 3.5 represents the height of goal)
        // 3.5 = 4 + u*sin(theta)*t - 8t^2
        //? t = 0.25, when y(t) = 3.5, u = 66.67 and theta = 0
        // subbing everything into z(t) gives us 
        //? z ~= 16.67
        // So under 17 distance units, just use camera angle with full power.
        //! All other z distance untis are calculated in the same way, with subbing in values of theta
        //? at theta = 00, z ~= 016.67
        //? at theta = 05, z ~= 053.40
        //? at theta = 10, z ~= 097.77
        //? at theta = 15, z ~= 140.74
        //? at theta = 20, z ~= 179.93
        //? at theta = 25, z ~= 213.88
        //? at theta = 30, z ~= 241.45
        //? at theta = 35, z ~= 261.76
        //? at theta = 40, z ~= 274.18
        //? at theta = 45, z ~= 278.30
        //? at theta = 50, z ~= 274.00
        // ! Use theta = 45 as a theoretical max distance
        #endregion
        //! - - - - - - - -

        //! - Distance to goal -
        Vector3 planarDistance;
        float distance;
        if (botPlayerScript.team == 0)
        {
            planarDistance = botPlayerScript.GetPlanarVector(gameManager.goalHoleB.transform.position - gameManager.gameball.transform.position);
        }
        else
        {
            planarDistance = botPlayerScript.GetPlanarVector(gameManager.goalHoleA.transform.position - gameManager.gameball.transform.position);
        }
        distance = planarDistance.magnitude;

        //! - Return angle value depending on how far the goal is -
        if (distance < 17f)
        {
            return 0f;
        }
        else
        {
            float angle = ReturnSetAngle(distance) * Mathf.PI / 180; // Convert to radians
            float time = (INITIAL_SPEED * Mathf.Sin(angle) + Mathf.Sqrt(Mathf.Pow(INITIAL_SPEED * Mathf.Sin(angle), 2) + 16)) / 16f;
            float guessDistance = INITIAL_SPEED * Mathf.Cos(angle) * time;

            int i = 0;
            float divider = 2.5f * Mathf.PI / 180; //half of 5 degrees as radians 
                                                   //! - look to adjust angle when angle is less than 45 degrees and guess distance is off by more than 1 unit -
            while (angle < Mathf.PI / 4f && Mathf.Abs(distance - guessDistance) > 0.5f)
            {
                //! - Narrow down angle -
                if (guessDistance < distance)
                {
                    angle += divider;
                }
                else
                {
                    angle -= divider;
                }
                divider /= 2f;

                //! - Recalculate guessDistance
                time = (INITIAL_SPEED * Mathf.Sin(angle) + Mathf.Sqrt(Mathf.Pow(INITIAL_SPEED * Mathf.Sin(angle), 2) + 16)) / 16f;
                guessDistance = INITIAL_SPEED * Mathf.Cos(angle) * time;

                //! - Just so doesnt get stuck in loop in case anything goes wrong -
                i++;
                if (i > 10)
                {
                    print("Reached Limit");
                    break;
                }
            }
            //print($"iterations = {i}");
            //print($"Calculated Angle = {angle * 180 / Mathf.PI}");
            return angle * 180 / Mathf.PI; // Return angle as degrees
        }
    }
    float FindHorizontalDistanceFromThisBot(Transform target)
    {
        Vector2 horizontalVector = new Vector2(target.position.x - transform.position.x, target.position.z - transform.position.z);
        return horizontalVector.magnitude;
    }
    float ReturnSetAngle(float distance)
    {
        //! - This is for narrowing down the distance first, in order to reduce number of calculations -
        if (distance >= 17f && distance < 76f)
        {
            return 5f;
        }
        else if (distance >= 76f && distance < 119f)
        {
            return 10f;
        }
        else if (distance >= 119f && distance < 160f)
        {
            return 15f;
        }
        else if (distance >= 160f && distance < 197f)
        {
            return 20f;
        }
        else if (distance >= 197f && distance < 228f)
        {
            return 25f;
        }
        else if (distance >= 228f && distance < 252f)
        {
            return 30f;
        }
        else if (distance >= 252f && distance < 268f)
        {
            return 35f;
        }
        else if (distance >= 268f && distance < 278f)
        {
            return 40f;
        }
        else
        {
            return 45f;
        }
    }
    public void GetPlayers()
    {
        enemyPlayers.Clear();
        teammates.Clear();
        botTeammates.Clear();
        int i = 0;
        foreach (GameObject player in State.players)
        {
            if (player)
            {
                players.Add(player.GetComponent<PlayerScript>());
                if (players[i].team != botPlayerScript.team)
                {
                    enemyPlayers.Add(players[i]);
                }
                else if (players[i].team == botPlayerScript.team)
                {
                    teammates.Add(players[i]);
                    if (players[i].isBot)
                    {
                        botTeammates.Add(players[i]);
                    }
                }
                i++;
            }
        }
    }
    void CheckIfReturnToGoalJumpNeeded()
    {
        if (IsNearWall() && botPlayerScript.isOnGround && !isJumpingAtGoal)
        {
            StartCoroutine(ReturnToGoalJump());
        }
    }
    bool CheckIfAboutToMovePastTarget(Vector3 targetPosition)
    {
        if (!botPlayerScript.isOnGround && !botPlayerScript.isOnWall && targetPosition != Vector3.zero)
        {
            Vector3 position = transform.position;
            //! - iterate through and add velocity to original position -
            //! - Iterate count is calculated by time / time step (timestep = 0.02) -
            for (int i = 0; i < 2f / Time.fixedDeltaTime; i++)
            {
                position += transform.GetComponent<Rigidbody>().velocity * Time.fixedDeltaTime;
                if (Vector3.Distance(position, targetPosition) < 5f)
                {
                    return true;
                }
            }
            return true;
        }
        else
        {
            return false;
        }
    }
    void JumpInGoals()
    {
        if (botPlayerScript.IsInGoalBox() && (EnemyWithGameballWithinRange(75f) || GameballHeadingTowardsGoal()))
        {
            if (botPlayerScript.IsOnGround() && !jumpingInGoal)
            {
                StartCoroutine(BeginJumpInGoals());
            }
        }
    }

    IEnumerator ReturnToGoalJump()
    {
        isJumpingAtGoal = true;
        //! jump 1
        //! - have bot not move sideways whilst jumping -
        //botPlayerScript.SimulateInputLS(Vector2.zero);
        botPlayerScript.SimulateInputA();
        yield return new WaitForSeconds(JUMP_ONE_PEAK_TIME);
        //! Jump 2
        botPlayerScript.SimulateInputA();
        yield return new WaitForSeconds(JUMP_TWO_PEAK_TIME);
        isJumpingAtGoal = false;
    }
    public IEnumerator GoalieDive(Vector3 dirToBall)
    {
        yield return new WaitForSeconds(DIVE_REACTION_TIME);
        botPlayerScript.DiveAsBot(dirToBall);
        timeOfAction = Time.time;
    }
    public IEnumerator BeginJumpInGoals()
    {
        jumpingInGoal = true;
        //! jump 1
        //! - have bot not move sideways whilst jumping -
        //botPlayerScript.SimulateInputLS(Vector2.zero);
        botPlayerScript.SimulateInputA();
        print("jump one");
        yield return new WaitForSeconds(1.5f);
        //! Jump 2
        //botPlayerScript.SimulateInputA();
        //print("jump two");
        yield return new WaitForSeconds(2.5f);
        jumpingInGoal = false;
    }
    IEnumerator SlapCoolDown()
    {
        canSearchForSlap = false;
        yield return new WaitForSeconds(SLAP_COOLDOWN);
        canSearchForSlap = true;
    }

    //?  --- HELPER METHODS ---
    bool IsNearWall()
    {
        //! - Set up for spherecast -
        Vector3 overlapPosition = transform.position;
        float overlapRadius = 20f;
        Collider[] overlap;
        overlap = Physics.OverlapSphere(overlapPosition, overlapRadius, 1 << 23);

        if (overlap.Length > 0)
        {
            return true;
        }
        else
        {
            return false;
        }
    }
    Vector3 GetDirectionToEnemyGoal()
    {
        Vector3 dirToEnemyGoal;
        if (botPlayerScript.team == 0)
        {
            dirToEnemyGoal = transform.InverseTransformDirection(gameManager.goaliePositions[0].position - transform.position);
        }
        else
        {
            dirToEnemyGoal = transform.InverseTransformDirection(gameManager.goaliePositions[1].position - transform.position);
        }
        return dirToEnemyGoal;
    }
    bool DivingTowardsEnemyGoals(int team)
    {
        if (team == 0)
        {
            if ((gameball.transform.position - transform.position).z > 0)
            {
                return true;
            }
        }
        else
        {
            if ((gameball.transform.position - transform.position).z < 0)
            {
                return true;
            }
        }
        return false; 
    }
}
