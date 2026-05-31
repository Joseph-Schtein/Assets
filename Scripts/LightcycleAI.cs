using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using System.Collections.Generic;

[RequireComponent(typeof(DecisionRequester))]
[RequireComponent(typeof(BehaviorParameters))]
public class LightcycleAI : Agent
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  STATE MACHINE — 4 tactical states
    // ═══════════════════════════════════════════════════════════════════════════
    public enum AIState { Hunting, Trapping, Charging, Surviving }

    [Header("AI Status")]
    public AIState currentState = AIState.Hunting;
    public float currentCharge = 100f;

    [Header("Arena Settings")]
    public float arenaLimit = 150f;
    public float boundaryWarningZone = 25f;

    [Header("Base Settings")]
    public float maxCharge = 100f;
    public float chargeDrainRate = 5f;
    public float panicThreshold = 30f;
    public float criticalThreshold = 15f;
    public float baseMoveSpeed = 15f;

    [Header("Free Steering Settings")]
    [Tooltip("How fast the AI rotates toward its target (degrees/sec).")]
    public float steerSpeed = 120f;
    [Tooltip("How far ahead to scan for obstacles.")]
    public float lookAheadDistance = 25f;

    [Header("Dash Settings")]
    public float dashSpeedMultiplier = 2f;

    [Header("Personality / Dynamic")]
    public float wanderAmplitude = 30f;
    public float wanderFrequency = 0.6f;
    [Range(0f, 1f)]
    public float aggressiveness = 0.7f;
    [Range(0f, 1f)]
    public float cautiousness = 0.3f;

    // ── Private State ─────────────────────────────────────────────────────────
    private static readonly float[] s_angles = { 0f, 15f, -15f, 30f, -30f, 45f, -45f, 60f, -60f, 90f, -90f };
    private static readonly float[] s_obsAngles = { 0f, 15f, -15f, 30f, -30f, 45f }; // Exactly 6 angles to prevent 25-size truncate log spam
    private static readonly float[] s_dangerAngles = { 0f, 10f, -10f, 20f, -20f };

    private Vector3 currentMoveDirection = Vector3.forward;
    private float currentSteerAction = 0f;

    private Transform currentTarget;
    private Transform huntTarget;        // Cached opponent for hunting
    private Transform chargeTarget;      // Cached spotlight for charging
    private bool isDashing = false;
    private PlayerEnergy _playerEnergy;
    private Vector3 startPos;
    private Quaternion startRot;
    private bool isDead = false;
    private float _wanderPhaseOffset;
    private bool _isMainMenu;

    // ── Tracking for reward shaping ───────────────────────────────────────────
    private float _prevDistToTarget = float.MaxValue;
    private bool _wasInDanger = false;
    private int _stepsInBoundaryZone = 0;

    // ═══════════════════════════════════════════════════════════════════════════
    //  INITIALIZATION
    // ═══════════════════════════════════════════════════════════════════════════
    public override void Initialize()
    {
        startPos = transform.position;
        startRot = transform.rotation;
        _isMainMenu = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu";

        int aiCount = PlayerPrefs.GetInt("AIPlayerCount", 15);
        arenaLimit = Mathf.Lerp(100f, 250f, (aiCount - 5f) / 15f);

        _playerEnergy = GetComponent<PlayerEnergy>();
        if (_playerEnergy == null)
            Debug.LogWarning($"{name}: No PlayerEnergy component found! AI state decisions will fall back to currentCharge.");

        // Compensate for AI hovering at Y=0.1 by raising the light to local Y=0.9
        // This ensures the Spot Light (Y=1.0 world) matches the human player's light size
        if (_playerEnergy != null && _playerEnergy.sparkLight != null)
        {
            Vector3 lightPos = _playerEnergy.sparkLight.transform.localPosition;
            lightPos.y = 0.9f;
            _playerEnergy.sparkLight.transform.localPosition = lightPos;
        }

        var bp = GetComponent<BehaviorParameters>();
        bp.BehaviorName = "LightcycleAI";
        // 1 Continuous Action for steering (-1 to 1)
        // 1 Discrete Action for dashing (0 or 1)
        bp.BrainParameters.ActionSpec = new ActionSpec(1, new[] { 2 });

        // OPTIMIZATION: Reduce Neural Network CPU usage by asking for decisions less frequently.
        // Default is usually 5. Changing to 10 significantly cuts CPU time.
        var requester = GetComponent<DecisionRequester>();
        if (requester != null)
        {
            requester.DecisionPeriod = 10;
        }

        RandomisePersonality();
    }

    private void RandomisePersonality()
    {
        wanderAmplitude = Random.Range(15f, 45f);
        wanderFrequency = Random.Range(0.4f, 1.0f);
        aggressiveness = Random.Range(0.5f, 0.95f);
        cautiousness = 1f - aggressiveness; // Inverse relationship
        _wanderPhaseOffset = Random.Range(0f, Mathf.PI * 2f);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  EPISODE LIFECYCLE
    // ═══════════════════════════════════════════════════════════════════════════
    public override void OnEpisodeBegin()
    {
        if (isDead)
        {
            StartCoroutine(RespawnRoutine("Episode Reset"));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  OBSERVATIONS — 27 floats
    //
    //  1.  Energy ratio                                         (1)
    //  2.  Is dashing (0/1)                                     (1)
    //  3.  Direction to nearest spotlight (x,z) + norm dist      (3)
    //  4.  Spotlight time remaining (normalized)                 (1)
    //  5.  Direction to nearest opponent (x,z) + norm dist       (3)
    //  6.  Opponent forward direction (x,z)                      (2)
    //  7.  Position relative to boundaries (x/limit, z/limit)   (2)
    //  8.  7-direction raycasts × 2 (distance + isDeadly)       (14)
    //                                                    TOTAL: 27
    // ═══════════════════════════════════════════════════════════════════════════
    public override void CollectObservations(VectorSensor sensor)
    {
        float energyRatio = GetEnergyRatio();

        // 1. Own energy ratio (1 float)
        sensor.AddObservation(energyRatio);

        // 2. Is dashing (1 float)
        sensor.AddObservation(isDashing ? 1f : 0f);

        // 3. Nearest spotlight direction + distance (3 floats)
        chargeTarget = FindBestSpotlight();
        if (chargeTarget != null)
        {
            Vector3 dirToLight = (chargeTarget.position - transform.position);
            dirToLight.y = 0f;
            float distToLight = dirToLight.magnitude;
            Vector3 normDir = distToLight > 0.1f ? dirToLight / distToLight : Vector3.zero;
            sensor.AddObservation(normDir.x);
            sensor.AddObservation(normDir.z);
            sensor.AddObservation(Mathf.Clamp01(distToLight / (arenaLimit * 2f)));
        }
        else
        {
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(1f);
        }

        // 4. Spotlight time remaining — normalized (1 float)
        if (chargeTarget != null)
        {
            chargeTarget.TryGetComponent<LightSource>(out LightSource ls);
            if (ls == null) ls = chargeTarget.GetComponentInParent<LightSource>();
            if (ls != null && ls.MaxTimeRemaining > 0f)
                sensor.AddObservation(Mathf.Clamp01(ls.TimeRemaining / ls.MaxTimeRemaining));
            else
                sensor.AddObservation(1f); // Assume full time if unknown
        }
        else
        {
            sensor.AddObservation(0f);
        }

        // 5. Nearest opponent direction + distance (3 floats)
        huntTarget = FindNearestOpponent();
        if (huntTarget != null)
        {
            Vector3 dirToOpp = (huntTarget.position - transform.position);
            dirToOpp.y = 0f;
            float distToOpp = dirToOpp.magnitude;
            Vector3 normDirOpp = distToOpp > 0.1f ? dirToOpp / distToOpp : Vector3.zero;
            sensor.AddObservation(normDirOpp.x);
            sensor.AddObservation(normDirOpp.z);
            sensor.AddObservation(Mathf.Clamp01(distToOpp / (arenaLimit * 2f)));
        }
        else
        {
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(1f);
        }

        // 6. Opponent forward direction — for prediction (2 floats)
        if (huntTarget != null)
        {
            sensor.AddObservation(huntTarget.forward.x);
            sensor.AddObservation(huntTarget.forward.z);
        }
        else
        {
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
        }

        // 7. Position relative to boundaries (2 floats)
        sensor.AddObservation(transform.position.x / arenaLimit);
        sensor.AddObservation(transform.position.z / arenaLimit);

        // 8. 6-direction raycasts × 2 floats each = 12 floats
        //    Total = 13 + 12 = 25 floats (matches BehaviorParameters to prevent log spam)
        foreach (float angle in s_obsAngles)
        {
            Vector3 scanDir = Quaternion.AngleAxis(angle, Vector3.up) * currentMoveDirection;
            float physicalHitDist = lookAheadDistance;
            bool physicalIsDeadly = false;

            if (Physics.Raycast(new Vector3(transform.position.x, 1f, transform.position.z),
                                scanDir, out RaycastHit scanHit, lookAheadDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                physicalHitDist = scanHit.distance;
                if (scanHit.collider.CompareTag("DeadlyTrail"))
                {
                    scanHit.collider.TryGetComponent<TrailData>(out TrailData td);
                    physicalIsDeadly = (td == null || td.owner != this.gameObject);
                }
            }

            // Virtual boundary wall: compute distance to arena edge along this ray
            float boundaryDist = DistanceToBoundary(transform.position, scanDir);

            // Report whichever is closer — physical obstacle or arena boundary
            if (boundaryDist < physicalHitDist && boundaryDist < lookAheadDistance)
            {
                sensor.AddObservation(boundaryDist / lookAheadDistance);
                sensor.AddObservation(1f); // Boundary IS deadly
            }
            else
            {
                sensor.AddObservation(physicalHitDist / lookAheadDistance);
                sensor.AddObservation(physicalIsDeadly ? 1f : 0f);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  REWARD SHAPING — "Kill-First, Die-Never"
    // ── Reward shaping ────────────────────────────────────────────────────────
    //  Kill reward (+1.5) is given externally in PlayerEnergy.cs.
    //  Death penalty (−1.0) is given in Die().
    //  This method handles per-step shaping rewards.
    // ═══════════════════════════════════════════════════════════════════════════
    public override void OnActionReceived(ActionBuffers actions)
    {
        if (isDead) return;

        currentSteerAction = actions.ContinuousActions[0]; // -1 to +1
        isDashing = actions.DiscreteActions[0] == 1;

        float energyRatio = GetEnergyRatio();

        // ── Tiny survival drip ────────────────────────────────────────────────
        AddReward(0.0001f);

        // ── Penalise spinning (anti-circle) and reward straight driving ──────
        // EXCEPTION: Spinning is perfectly fine if they are inside a spotlight to charge
        bool inSpotlight = (_playerEnergy != null && _playerEnergy.isIlluminated);
        float turnMagnitude = Mathf.Abs(currentSteerAction);

        if (inSpotlight)
        {
            // Give a MASSIVE dense reward for sitting in the light to charge!
            AddReward(0.005f);
        }
        else
        {
            if (turnMagnitude > 0.5f)
                AddReward(-0.002f * turnMagnitude); // Spin penalty in open field
            else if (turnMagnitude < 0.1f)
                AddReward(0.0005f); // Reward for driving straight and exploring
        }

        // ── Reward heading toward current target ─────────────────────────────
        if (currentTarget != null)
        {
            Vector3 toTarget = (currentTarget.position - transform.position);
            toTarget.y = 0f;
            float dist = toTarget.magnitude;

            if (dist > 0.1f)
            {
                float dot = Vector3.Dot(currentMoveDirection, toTarget / dist);
                if (dot > 0.7f)
                    AddReward(0.0005f);
            }

            // ── Reward closing distance when hunting ─────────────────────────
            if (currentState == AIState.Hunting || currentState == AIState.Trapping)
            {
                if (dist < _prevDistToTarget)
                    AddReward(0.0003f);
            }
            _prevDistToTarget = dist;
        }

        // ── Penalise critically low energy ───────────────────────────────────
        if (energyRatio < criticalThreshold / 100f)
            AddReward(-0.002f);

        // ── Penalise being near boundaries — GENTLE WARNING ─────────────────
        float posX = Mathf.Abs(transform.position.x);
        float posZ = Mathf.Abs(transform.position.z);
        float maxEdge = Mathf.Max(posX, posZ);
        if (maxEdge > arenaLimit - boundaryWarningZone)
        {
            // Gentle penalty: -0.002 per step maximum (so they don't get terrified and spin in circles)
            float penetration = Mathf.Clamp01((maxEdge - (arenaLimit - boundaryWarningZone)) / boundaryWarningZone);
            AddReward(-0.002f * penetration);
        }
        else
        {
            _stepsInBoundaryZone = 0;
            // Small reward for being away from walls — teaches center preference
            if (maxEdge < arenaLimit * 0.5f)
                AddReward(0.0001f);
        }

        // ── OFFENSIVE POSITIONING REWARD (The "Cut Off" maneuver) ────────────
        // Teaches the AI that getting in front of an opponent's face is a great idea
        bool executingTrap = false;
        if (huntTarget != null)
        {
            Vector3 toMe = (transform.position - huntTarget.position);
            toMe.y = 0f;
            float distToOpp = toMe.magnitude;

            if (distToOpp < 40f && distToOpp > 2f)
            {
                // Are we in the opponent's front cone? 
                float inFrontDot = Vector3.Dot(huntTarget.forward, toMe.normalized);
                if (inFrontDot > 0.7f)
                {
                    executingTrap = true;

                    // Small base reward for getting in front
                    AddReward(0.01f);

                    // THE TRUE TRAP: Are we driving ACROSS their path? (Perpendicular)
                    float crossingDot = Mathf.Abs(Vector3.Dot(currentMoveDirection, huntTarget.forward));
                    if (crossingDot < 0.5f)
                    {
                        // MASSIVE reward for turning sideways to lay a wall!
                        AddReward(0.05f);

                        if (isDashing)
                            // ABSOLUTE JACKPOT for boosting across their face!
                            AddReward(0.1f);
                    }
                }
            }
        }

        // ── Penalise wasted dashing (not aligned with target and not trapping) ─
        if (isDashing)
        {
            bool alignedWithTarget = false;
            if (currentTarget != null)
            {
                Vector3 toTarget = (currentTarget.position - transform.position).normalized;
                float dot = Vector3.Dot(currentMoveDirection, toTarget);
                alignedWithTarget = dot > 0.5f;
            }
            if (!alignedWithTarget && !executingTrap)
                AddReward(-0.001f);
        }

        // ── Reward dodging danger ────────────────────────────────────────────
        bool inDangerNow = IsInImmediateDanger();
        if (_wasInDanger && !inDangerNow)
            AddReward(0.001f); // Successfully evaded
        _wasInDanger = inDangerNow;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  HEURISTIC — Expert Demonstration for Training
    //
    //  This is the "teacher brain" that runs when BehaviorType = HeuristicOnly.
    //  It includes intelligent trail interception, spotlight triage, and
    //  predictive avoidance.
    // ═══════════════════════════════════════════════════════════════════════════
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        var discreteActions = actionsOut.DiscreteActions;

        // ── 1. OBSTACLE AVOIDANCE — scan for trails, players, boundaries ─────
        Vector3 avoidance = Vector3.zero;
        bool immediateTrailDanger = false;
        float closestTrailDist = lookAheadDistance;

        foreach (float angle in s_angles)
        {
            Vector3 scanDir = Quaternion.AngleAxis(angle, Vector3.up) * currentMoveDirection;
            if (Physics.Raycast(new Vector3(transform.position.x, 1f, transform.position.z),
                                scanDir, out RaycastHit scanHit, lookAheadDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (scanHit.collider.CompareTag("DeadlyTrail"))
                {
                    scanHit.collider.TryGetComponent<TrailData>(out TrailData td);
                    if (td == null || td.owner != this.gameObject)
                    {
                        float proximity = 1f - (scanHit.distance / lookAheadDistance);
                        // Exponential urgency — close trails get MUCH stronger avoidance
                        float urgency = proximity * proximity * 30f;
                        avoidance -= scanDir * urgency;

                        if (scanHit.distance < 5f && Mathf.Abs(angle) < 30f)
                            immediateTrailDanger = true;
                        if (scanHit.distance < closestTrailDist)
                            closestTrailDist = scanHit.distance;
                    }
                }
                else if (scanHit.collider.CompareTag("Player") || scanHit.collider.CompareTag("AI"))
                {
                    if (scanHit.collider.gameObject != this.gameObject)
                    {
                        float proximity = 1f - (scanHit.distance / lookAheadDistance);
                        // Less avoidance from players — we WANT to be near them (but not crash)
                        avoidance -= scanDir * proximity * 3f;
                    }
                }
            }

            // Virtual boundary wall detection for heuristic avoidance
            float bDist = DistanceToBoundary(transform.position, scanDir);
            if (bDist < lookAheadDistance)
            {
                float proximity = 1f - (bDist / lookAheadDistance);
                float urgency = proximity * proximity * 35f; // Stronger than trails — walls are instant death
                avoidance -= scanDir * urgency;

                if (bDist < 5f && Mathf.Abs(angle) < 30f)
                    immediateTrailDanger = true; // Treat imminent wall hit like trail danger
            }
        }

        // ── 2. BOUNDARY AVOIDANCE — smooth U-turn near walls ─────────────────
        Vector3 pos = transform.position;
        float boundaryUrgencyX = 0f, boundaryUrgencyZ = 0f;

        if (Mathf.Abs(pos.x) > arenaLimit - boundaryWarningZone)
        {
            float penetration = (Mathf.Abs(pos.x) - (arenaLimit - boundaryWarningZone)) / boundaryWarningZone;
            boundaryUrgencyX = Mathf.Sign(pos.x) * Mathf.Lerp(3f, 15f, penetration);
            avoidance.x -= boundaryUrgencyX;
        }
        if (Mathf.Abs(pos.z) > arenaLimit - boundaryWarningZone)
        {
            float penetration = (Mathf.Abs(pos.z) - (arenaLimit - boundaryWarningZone)) / boundaryWarningZone;
            boundaryUrgencyZ = Mathf.Sign(pos.z) * Mathf.Lerp(3f, 15f, penetration);
            avoidance.z -= boundaryUrgencyZ;
        }

        // ── 3. WANDER — organic movement when not chasing hard ───────────────
        float activeAmplitude = wanderAmplitude;
        if (currentState == AIState.Charging) activeAmplitude *= 0.25f;
        if (currentState == AIState.Surviving) activeAmplitude *= 0.1f;
        if (avoidance.sqrMagnitude > 2f) activeAmplitude = 0f; // Override wander when dodging

        float wanderDeg = Mathf.Sin(Time.time * wanderFrequency * Mathf.PI * 2f + _wanderPhaseOffset) * activeAmplitude;
        Vector3 wanderDir = Quaternion.AngleAxis(wanderDeg, Vector3.up) * currentMoveDirection;

        // ── 4. TARGET PURSUIT — different strategies per state ────────────────
        Vector3 desiredDir = wanderDir;

        if (currentTarget != null)
        {
            Vector3 targetPos = currentTarget.position;

            if (currentState == AIState.Hunting)
            {
                // Lead the target: predict where they'll be based on their forward direction
                float distToTarget = Vector3.Distance(transform.position, targetPos);
                float leadTime = Mathf.Clamp(distToTarget / baseMoveSpeed, 0.3f, 1.5f);
                targetPos += currentTarget.forward * baseMoveSpeed * leadTime * 0.5f;
            }
            else if (currentState == AIState.Trapping)
            {
                // Trail-cut maneuver: steer to cross IN FRONT of the opponent
                float distToTarget = Vector3.Distance(transform.position, targetPos);
                float interceptTime = Mathf.Clamp(distToTarget / baseMoveSpeed, 0.5f, 2f);
                Vector3 predictedPos = targetPos + currentTarget.forward * baseMoveSpeed * interceptTime;

                // Aim for a point perpendicular to their path, slightly ahead
                Vector3 oppRight = Vector3.Cross(Vector3.up, currentTarget.forward).normalized;
                Vector3 toMe = (transform.position - targetPos).normalized;
                float side = Vector3.Dot(toMe, oppRight);
                targetPos = predictedPos + oppRight * Mathf.Sign(side) * 5f;
            }

            Vector3 toTarget = (targetPos - transform.position);
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.1f)
            {
                // Blend between wander and target based on aggressiveness and state
                float blendFactor = aggressiveness;
                if (currentState == AIState.Charging) blendFactor = 0.9f; // Very focused on spotlight
                if (currentState == AIState.Surviving) blendFactor = 0.3f; // Less focused, more evasive
                if (currentState == AIState.Trapping) blendFactor = 0.85f;

                desiredDir = Vector3.Lerp(wanderDir, toTarget.normalized, blendFactor).normalized;
            }
        }

        // ── 5. EMERGENCY OVERRIDE — immediate trail danger ───────────────────
        if (immediateTrailDanger)
        {
            // Hard turn away — override everything else
            desiredDir = avoidance.normalized;
        }
        else
        {
            desiredDir = (desiredDir + avoidance).normalized;
        }

        if (desiredDir.sqrMagnitude < 0.01f) desiredDir = currentMoveDirection;

        // ── 6. MAP TO ACTIONS ────────────────────────────────────────────────
        float signedAngle = Vector3.SignedAngle(currentMoveDirection, desiredDir, Vector3.up);
        continuousActions[0] = Mathf.Clamp(signedAngle / 10f, -1f, 1f);

        // ── 7. DASHING DECISION ──────────────────────────────────────────────
        ConsiderDashing();
        discreteActions[0] = isDashing ? 1 : 0;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  UPDATE — State Machine + Movement
    // ═══════════════════════════════════════════════════════════════════════════
    void Update()
    {
        if (isDead) return;

        currentCharge -= chargeDrainRate * Time.deltaTime;
        currentCharge = Mathf.Max(0f, currentCharge);

        // ── State Machine Decision ───────────────────────────────────────────
        UpdateStateMachine();

        // ── Target Selection Based on State ──────────────────────────────────
        bool mouseInsideScreen = _isMainMenu &&
                                 Input.mousePosition.x >= 0 && Input.mousePosition.x <= Screen.width &&
                                 Input.mousePosition.y >= 0 && Input.mousePosition.y <= Screen.height;

        if (mouseInsideScreen && MenuAISpawner.MouseTarget != null)
        {
            currentTarget = MenuAISpawner.MouseTarget;
        }
        else
        {
            switch (currentState)
            {
                case AIState.Hunting:
                case AIState.Trapping:
                    if (huntTarget == null) huntTarget = FindNearestOpponent();
                    currentTarget = huntTarget;
                    break;
                case AIState.Charging:
                    if (chargeTarget == null) chargeTarget = FindBestSpotlight();
                    currentTarget = chargeTarget;
                    break;
                case AIState.Surviving:
                    // In surviving mode, find the safest direction (handled in heuristic)
                    // Still target a spotlight if one is nearby
                    if (chargeTarget == null) chargeTarget = FindBestSpotlight();
                    currentTarget = chargeTarget;
                    break;
            }
        }

        // ── Apply Movement and Steering ──────────────────────────────────────
        float currentSpeed = isDashing ? baseMoveSpeed * dashSpeedMultiplier : baseMoveSpeed;

        currentMoveDirection = Quaternion.AngleAxis(currentSteerAction * steerSpeed * Time.deltaTime, Vector3.up) * currentMoveDirection;
        currentMoveDirection.y = 0f;
        currentMoveDirection.Normalize();

        transform.position += currentMoveDirection * currentSpeed * Time.deltaTime;
        transform.position = new Vector3(
            Mathf.Clamp(transform.position.x, -arenaLimit, arenaLimit),
            0.1f,
            Mathf.Clamp(transform.position.z, -arenaLimit, arenaLimit));

        if (currentMoveDirection != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(currentMoveDirection);

        if (isDashing)
        {
            if (_playerEnergy != null) _playerEnergy.UseEnergy(_playerEnergy.drainRate * Time.deltaTime);
            else currentCharge -= chargeDrainRate * Time.deltaTime;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  STATE MACHINE — Intelligent decision-making every frame
    // ═══════════════════════════════════════════════════════════════════════════
    void UpdateStateMachine()
    {
        float energyRatio = GetEnergyRatio();
        bool inBoundaryDanger = Mathf.Abs(transform.position.x) > arenaLimit - boundaryWarningZone * 0.5f
                             || Mathf.Abs(transform.position.z) > arenaLimit - boundaryWarningZone * 0.5f;
        bool inImmediateDanger = IsInImmediateDanger();

        // ── Priority 1: SURVIVING — immediate mortal danger ──────────────────
        if (inImmediateDanger || (inBoundaryDanger && energyRatio < 0.2f))
        {
            currentState = AIState.Surviving;
            return;
        }

        // ── Priority 2: CHARGING — energy too low ────────────────────────────
        if (energyRatio < panicThreshold / 100f)
        {
            currentState = AIState.Charging;
            return;
        }

        // ── Priority 3: TRAPPING — close enough to opponent to lay a trap ────
        if (energyRatio > 0.4f && huntTarget != null)
        {
            float distToOpp = Vector3.Distance(transform.position, huntTarget.position);
            if (distToOpp < 20f)
            {
                // Check if opponent is moving roughly perpendicular to us — ideal for trail-cut
                Vector3 toOpp = (huntTarget.position - transform.position).normalized;
                float dotForward = Mathf.Abs(Vector3.Dot(currentMoveDirection, huntTarget.forward));
                float dotApproach = Vector3.Dot(currentMoveDirection, toOpp);

                // Trail-cut condition: opponent moving crosswise AND we're approaching
                if (dotForward < 0.5f && dotApproach > 0.3f)
                {
                    currentState = AIState.Trapping;
                    return;
                }
            }
        }

        // ── Priority 4: CHARGING — moderate energy and spotlight nearby ──────
        if (energyRatio < 0.6f)
        {
            Transform nearestSpot = FindBestSpotlight();
            if (nearestSpot != null)
            {
                float distToSpot = Vector3.Distance(transform.position, nearestSpot.position);
                // Switch to charging if spotlight is reasonably close
                if (distToSpot < arenaLimit * 0.4f)
                {
                    // More cautious AIs charge earlier
                    if (energyRatio < Mathf.Lerp(0.6f, 0.4f, aggressiveness))
                    {
                        currentState = AIState.Charging;
                        return;
                    }
                }
            }
        }

        // ── Default: HUNTING ─────────────────────────────────────────────────
        currentState = AIState.Hunting;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  DASHING — Intelligent energy-aware boost
    // ═══════════════════════════════════════════════════════════════════════════
    void ConsiderDashing()
    {
        isDashing = false;
        if (currentTarget == null) return;

        float energyRatio = GetEnergyRatio();

        // Never dash when energy is low
        if (energyRatio <= (panicThreshold + 10f) / 100f) return;

        // Never dash near boundaries
        if (Mathf.Abs(transform.position.x) > arenaLimit - boundaryWarningZone ||
            Mathf.Abs(transform.position.z) > arenaLimit - boundaryWarningZone) return;

        // Never dash when in immediate danger
        if (IsInImmediateDanger()) return;

        Vector3 dirToTarget = (currentTarget.position - transform.position).normalized;
        float dist = Vector3.Distance(transform.position, currentTarget.position);
        float lookDot = Vector3.Dot(currentMoveDirection, dirToTarget);

        float huntDashRange = Mathf.Lerp(30f, 80f, aggressiveness);
        float chargeDashRange = Mathf.Lerp(15f, 40f, aggressiveness);
        float huntDotThreshold = Mathf.Lerp(0.7f, 0.4f, aggressiveness);

        switch (currentState)
        {
            case AIState.Hunting:
                // Dash to close distance on opponent when aligned
                if (dist < huntDashRange && lookDot > huntDotThreshold)
                    isDashing = true;
                break;

            case AIState.Trapping:
                // Dash to execute trail-cut when perfectly aligned
                if (dist < 25f && lookDot > 0.6f)
                    isDashing = true;
                break;

            case AIState.Charging:
                // Dash to reach spotlight quickly when well-aligned
                if (lookDot > 0.9f && dist < chargeDashRange && energyRatio > 0.5f)
                    isDashing = true;
                break;

            case AIState.Surviving:
                // Never dash when surviving — conserve energy
                isDashing = false;
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  TARGET FINDING
    // ═══════════════════════════════════════════════════════════════════════════

    // ── STATIC TARGET CACHING (Eliminates CPU lag from FindGameObjectsWithTag) ──
    private static Transform cachedPlayer;
    private static Transform[] cachedAIs = new Transform[0];
    private static Transform[] cachedSpotlights = new Transform[0];
    private static LightSource[] cachedLightSources = new LightSource[0];
    private static float lastCacheTime = -1f;

    static void UpdateSharedCache()
    {
        // Only do the heavy hierarchy search twice a second, shared across ALL 20 AIs
        if (Time.time - lastCacheTime > 0.5f)
        {
            lastCacheTime = Time.time;

            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            cachedPlayer = playerObj != null ? playerObj.transform : null;

            GameObject[] ais = GameObject.FindGameObjectsWithTag("AI");
            cachedAIs = new Transform[ais.Length];
            for (int i = 0; i < ais.Length; i++) cachedAIs[i] = ais[i].transform;

            GameObject[] spots = GameObject.FindGameObjectsWithTag("Spotlight");
            cachedSpotlights = new Transform[spots.Length];
            cachedLightSources = new LightSource[spots.Length];
            for (int i = 0; i < spots.Length; i++)
            {
                cachedSpotlights[i] = spots[i].transform;
                spots[i].TryGetComponent<LightSource>(out LightSource ls);
                if (ls == null) ls = spots[i].GetComponentInParent<LightSource>();
                cachedLightSources[i] = ls;
            }
        }
    }

    Transform FindBestSpotlight()
    {
        UpdateSharedCache();
        Transform bestTarget = null;
        float bestScore = float.MinValue;

        for (int i = 0; i < cachedSpotlights.Length; i++)
        {
            Transform spot = cachedSpotlights[i];
            if (spot == null) continue;

            float dist = Vector3.Distance(transform.position, spot.position);

            LightSource ls = cachedLightSources[i];

            float timeScore = 1f;
            if (ls != null)
            {
                if (ls.TimeRemaining < 5f && ls.TimeRemaining > 0f)
                    continue;

                if (ls.MaxTimeRemaining > 0f)
                    timeScore = Mathf.Clamp01(ls.TimeRemaining / ls.MaxTimeRemaining);
            }

            float score = timeScore / Mathf.Max(dist, 1f);
            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = spot;
            }
        }

        return bestTarget;
    }

    Transform FindNearestOpponent()
    {
        UpdateSharedCache();
        Transform closestTarget = null;
        float closestDistance = Mathf.Infinity;

        if (cachedPlayer != null)
        {
            float d = Vector3.Distance(transform.position, cachedPlayer.position);
            if (d < closestDistance) { closestDistance = d; closestTarget = cachedPlayer; }
        }

        for (int i = 0; i < cachedAIs.Length; i++)
        {
            Transform ai = cachedAIs[i];
            if (ai == null || ai == this.transform) continue;

            float d = Vector3.Distance(transform.position, ai.position);
            if (d < closestDistance) { closestDistance = d; closestTarget = ai; }
        }

        return closestTarget;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  DANGER DETECTION
    // ═══════════════════════════════════════════════════════════════════════════
    bool IsInImmediateDanger()
    {
        // Check for deadly trails directly ahead (narrow cone, short range)
        foreach (float angle in s_dangerAngles)
        {
            Vector3 scanDir = Quaternion.AngleAxis(angle, Vector3.up) * currentMoveDirection;
            if (Physics.Raycast(new Vector3(transform.position.x, 1f, transform.position.z),
                                scanDir, out RaycastHit hit, lookAheadDistance * 0.4f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.CompareTag("DeadlyTrail"))
                {
                    hit.collider.TryGetComponent<TrailData>(out TrailData td);
                    if (td == null || td.owner != this.gameObject)
                        return true;
                }
            }
        }
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════════════════
    float GetEnergyRatio()
    {
        return _playerEnergy != null
            ? _playerEnergy.currentEnergy / _playerEnergy.maxEnergy
            : currentCharge / maxCharge;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  TRIGGERS — Energy Gathering
    // ═══════════════════════════════════════════════════════════════════════════
    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Spotlight"))
        {
            currentCharge += 25f * Time.deltaTime;
            currentCharge = Mathf.Clamp(currentCharge, 0, maxCharge);
            AddReward(0.005f); // Reward for gathering energy
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (_playerEnergy != null) return;
        if (other.CompareTag("DeadlyTrail"))
        {
            other.TryGetComponent<TrailData>(out TrailData data);
            if (data != null && data.owner != this.gameObject)
            {
                currentCharge = 0;
                Die("Hit the opponent trail");
            }
        }
        else if (other.CompareTag("Player") || other.CompareTag("AI"))
        {
            if (other.gameObject != this.gameObject && other.transform.root.gameObject != this.gameObject)
            {
                currentCharge = 0;
                Die("Hit other player");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  DEATH & RESPAWN
    // ═══════════════════════════════════════════════════════════════════════════
    public void Die(string reason)
    {
        if (isDead) return;
        AddReward(-0.5f); // Significantly lowered death penalty to encourage aggressive risk-taking
        EndEpisode();
        StartCoroutine(RespawnRoutine(reason));
    }

    private System.Collections.IEnumerator RespawnRoutine(string reason)
    {
        isDead = true;
        TrailCollision[] trailColls = GetComponentsInChildren<TrailCollision>();
        foreach (var tc in trailColls) tc.DestroyTrailMesh();

        Collider[] colliders = GetComponents<Collider>();
        foreach (var c in colliders) c.enabled = false;

        TrailRenderer tr = GetComponentInChildren<TrailRenderer>();
        if (tr != null)
        {
            tr.emitting = false;
            tr.Clear();
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (var r in renderers) r.enabled = false;

        Light[] lights = GetComponentsInChildren<Light>();
        foreach (var l in lights) l.enabled = false;

        yield return new WaitForSeconds(3f);

        transform.position = startPos;
        transform.rotation = startRot;
        currentCharge = maxCharge;
        if (_playerEnergy != null)
        {
            _playerEnergy.currentEnergy = _playerEnergy.maxEnergy;
            _playerEnergy.isDead = false;
        }
        currentState = AIState.Hunting;
        currentMoveDirection = Vector3.forward;
        _prevDistToTarget = float.MaxValue;
        _wasInDanger = false;
        _stepsInBoundaryZone = 0;
        RandomisePersonality();

        if (tr != null) { tr.Clear(); tr.emitting = true; }
        TrailCollision[] freshTrailColls = GetComponentsInChildren<TrailCollision>();
        foreach (var tc in freshTrailColls) tc.ResetTrail();
        foreach (var c in colliders) c.enabled = true;
        foreach (var r in renderers) r.enabled = true;
        foreach (var l in lights) l.enabled = true;

        isDead = false;
    }

    public void ResetDirection()
    {
        currentMoveDirection = Vector3.forward;
        transform.rotation = Quaternion.LookRotation(currentMoveDirection);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  VIRTUAL BOUNDARY DETECTION
    //  Calculates the distance from a position to the arena edge along a ray.
    //  This lets the AI "see" the invisible boundary as if it were a wall.
    // ═══════════════════════════════════════════════════════════════════════════
    float DistanceToBoundary(Vector3 origin, Vector3 direction)
    {
        // The arena is a square from -arenaLimit to +arenaLimit on X and Z.
        // We find where the ray (origin + t*direction) exits this square.
        float tMin = float.MaxValue;

        if (Mathf.Abs(direction.x) > 0.001f)
        {
            // Distance to +X wall
            float t1 = (arenaLimit - origin.x) / direction.x;
            if (t1 > 0f && t1 < tMin) tMin = t1;
            // Distance to -X wall
            float t2 = (-arenaLimit - origin.x) / direction.x;
            if (t2 > 0f && t2 < tMin) tMin = t2;
        }

        if (Mathf.Abs(direction.z) > 0.001f)
        {
            // Distance to +Z wall
            float t3 = (arenaLimit - origin.z) / direction.z;
            if (t3 > 0f && t3 < tMin) tMin = t3;
            // Distance to -Z wall
            float t4 = (-arenaLimit - origin.z) / direction.z;
            if (t4 > 0f && t4 < tMin) tMin = t4;
        }

        return tMin;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  KILL REGISTRATION
    // ═══════════════════════════════════════════════════════════════════════════
    public void RegisterKill(GameObject victim)
    {
        float dist = Vector3.Distance(transform.position, victim.transform.position);

        // If the opponent dies close to us, we executed a deliberate trap!
        if (dist < 40f)
        {
            AddReward(5.0f); // MASSIVE JACKPOT for a confirmed kill
        }
        else
        {
            // Random kill from across the map (they hit our old trail)
            AddReward(0.5f);
        }
    }
}