using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using StarterAssets;
using MLAgents.Shhhunt.Obstacles;
using MLAgents.Shhhunt.Curriculum;

namespace MLAgents.Shhhunt.Curriculum
{
    /// <summary>
    /// Curriculum-driven Reach Target agent that gradually unlocks obstacles, structures, and target placements.
    /// </summary>
    [RequireComponent(typeof(ThirdPersonControllerAdapter))]
    public partial class ShhhuntCurriculumLearningAgent : Agent
    {
        [System.Serializable]
        private class CurriculumPhase
        {
            [Tooltip("Friendly identifier for debugging and telemetry")] public string id = "GroundOnly";
            [Tooltip("Enable every obstacle object managed by EnvironmentManager")] public bool enableObstacles;
            [Tooltip("Enable every structure object managed by EnvironmentManager")] public bool enableStructures;
            [Tooltip("Allow the target to spawn on top of obstacles")] public bool allowTargetOnObstacles;
            [Tooltip("Force the target to spawn on obstacles only")] public bool requireTargetOnObstacles;
            [Tooltip("Whether the target position should be randomized each episode")] public bool randomizeTarget = true;
        }

        [Header("Target & Environment")]
        [SerializeField] private Transform target;
        [SerializeField] private Transform areaCenter;
        [SerializeField] private Transform ground;
        [SerializeField] private TargetPositioner targetPositioner;
        [SerializeField] private EnvironmentManager environmentManager;
        [SerializeField] private string obstacleTag = "Obstacle";
        [SerializeField] private bool autoCollectObstacles = true;
        [SerializeField] private List<Collider> manualObstacleColliders = new List<Collider>();
        [SerializeField] private float targetOnObstacleYOffset = 0.15f;
        [SerializeField] private float targetGroundYOffset = 0.5f;

        [Header("Curriculum")]
        [SerializeField] private List<CurriculumPhase> curriculum = new List<CurriculumPhase>
        {
            new CurriculumPhase
            {
                id = "GroundOnly",
                enableObstacles = false,
                enableStructures = false,
                allowTargetOnObstacles = false,
                requireTargetOnObstacles = false,
                randomizeTarget = true
            },
            new CurriculumPhase
            {
                id = "Obstacles",
                enableObstacles = true,
                enableStructures = false,
                allowTargetOnObstacles = false,
                requireTargetOnObstacles = false,
                randomizeTarget = true
            },
            new CurriculumPhase
            {
                id = "Structures",
                enableObstacles = true,
                enableStructures = true,
                allowTargetOnObstacles = false,
                requireTargetOnObstacles = false,
                randomizeTarget = true
            },
            new CurriculumPhase
            {
                id = "TargetOnObstacles",
                enableObstacles = true,
                enableStructures = true,
                allowTargetOnObstacles = true,
                requireTargetOnObstacles = true,
                randomizeTarget = true
            }
        };

        [Header("Episode Timing")]
        [SerializeField] private float maxEpisodeSeconds = 60f;
        [SerializeField] private float overtimePenalty = -0.5f;

        [Header("Agent Configuration")]
        [SerializeField] private float targetReachedThreshold = 1.25f;
        [SerializeField] private float fallThreshold = -5f;
        [SerializeField] private float maxDistanceFromTarget = 100f;
        [SerializeField] private float maxDistance = 50f;
        [SerializeField] private float maxSpeed = 5.335f;
        [SerializeField] private float verticalVelocityNormalization = 10f;
        [SerializeField] private bool randomizeStartPosition = true;
        [SerializeField] private Vector3 spawnAreaSize = new Vector3(20f, 0f, 20f);
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private LayerMask obstacleMask = ~0;
        [SerializeField] private int spawnRetries = 10;
        [SerializeField] private float spawnClearanceRadius = 0.75f;
        [SerializeField] private float agentSpawnYOffset = 0.5f;

        [Header("Movement & Actions")]
        [SerializeField] private ThirdPersonControllerAdapter movementController;
        [SerializeField] private float sprintActionThreshold = 0.5f;
        [SerializeField] private float jumpActionThreshold = 0.5f;
        [SerializeField] private float jumpCooldownSeconds = 0.75f;
        [SerializeField] private float successfulJumpReward = 0.05f;

        [Header("Rewards & Penalties")]
        [SerializeField] private float distanceRewardScale = 0.01f;
        [SerializeField] private float timePenalty = -0.0002f;
        [SerializeField] private float obstacleCollisionPenalty = -0.08f;
        [SerializeField] private float invalidJumpPenalty = -0.02f;
        [SerializeField] private float elevatedTargetProgressBonus = 0.02f;
        [SerializeField] private float elevatedTargetCompletionBonus = 0.25f;

        [Header("Stuck Detection")]
        [SerializeField] private bool enableStuckPenalty = true;
        [SerializeField] private float stuckVelocityThreshold = 0.15f;
        [SerializeField] private float stuckContactMemory = 0.4f;
        [SerializeField] private float stuckTimeThreshold = 1.5f;
        [SerializeField] private float stuckPenalty = -0.1f;
        [SerializeField] private float stuckPenaltyCooldown = 1f;

#if UNITY_EDITOR
        [Header("Sensor Checklist")]
        [TextArea(4, 8)]
        [SerializeField] private string sensorSetupNotes =
            "Add RayPerceptionSensorComponent3D for Obstacle/Ground/Target tags." +
            "\nMatch 5 continuous actions: [forward, strafe, reserved, sprint, jump]." +
            "\nVector observations: 15 floats (without rays).";
#endif

        private const string CurriculumStageParameter = "shhhunt_curriculum_stage";
        private const string StatSuccesses = "Shhhunt/Successes";
        private const string StatFailures = "Shhhunt/Failures";
        private const string StatTimeouts = "Shhhunt/Timeouts";
        private const string StatOutOfBounds = "Shhhunt/OutOfBounds";
        private const string StatFalls = "Shhhunt/Falls";
        private const string StatTimeToTarget = "Shhhunt/TimeToTarget";
        private const string StatGroundTargets = "Shhhunt/GroundTargets";
        private const string StatObstacleTargets = "Shhhunt/ObstacleTargets";
        private const string StatStuckPenalties = "Shhhunt/StuckPenalties";

        private readonly List<Collider> _cachedObstacleColliders = new List<Collider>();
        private float _lastDistanceToTarget;
        private float _jumpCooldownTimer;
        private bool _jumpRewardPending;
        private bool _recentObstacleCollision;
        private bool _targetCurrentlyOnObstacle;
        private bool _isCurrentlySprinting;
        private float _episodeTimer;
        private int _currentPhaseIndex;
        private StatsRecorder _statsRecorder;
        private float _stuckTimer;
        private float _lastObstacleContactTime = float.NegativeInfinity;
        private float _stuckPenaltyCooldownTimer;

        // Runtime stats (not serialized): shown read-only in a CustomEditor
        private int _reachedTargetCount;
        private float _bestTimeToTarget = float.PositiveInfinity;
        private int _timeoutBeforeTargetCount;

        // Public getters for Inspector display
        public int ReachedTargetCount => _reachedTargetCount;
        public float BestTimeToTarget => float.IsPositiveInfinity(_bestTimeToTarget) ? -1f : _bestTimeToTarget;
        public int TimeoutBeforeTargetCount => _timeoutBeforeTargetCount;

        private CurriculumPhase CurrentPhase => curriculum.Count == 0
            ? new CurriculumPhase()
            : curriculum[Mathf.Clamp(_currentPhaseIndex, 0, curriculum.Count - 1)];

        private void Awake()
        {
            if (movementController == null)
            {
                movementController = GetComponent<ThirdPersonControllerAdapter>();
            }
        }

        public override void Initialize()
        {
            _statsRecorder = Academy.Instance != null ? Academy.Instance.StatsRecorder : null;
            CacheObstacleColliders();
            _currentPhaseIndex = ResolveCurriculumStage();
        }

        public override void OnEpisodeBegin()
        {
            _currentPhaseIndex = ResolveCurriculumStage();
            ApplyCurriculumPhase();
            CacheObstacleColliders();
            ResetAgentTransform();
            PlaceTarget();
            _lastDistanceToTarget = target != null ? Vector3.Distance(transform.position, target.position) : 0f;
            _episodeTimer = 0f;
            _jumpCooldownTimer = 0f;
            _stuckTimer = 0f;
            _stuckPenaltyCooldownTimer = 0f;
            _lastObstacleContactTime = float.NegativeInfinity;
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            if (movementController == null || target == null)
            {
                for (int i = 0; i < 15; i++)
                {
                    sensor.AddObservation(0f);
                }
                return;
            }

            Vector3 toTarget = target.position - transform.position;
            sensor.AddObservation(toTarget / Mathf.Max(maxDistance, 0.0001f));

            Vector3 velocity = movementController.GetVelocity();
            sensor.AddObservation(velocity / Mathf.Max(maxSpeed, 0.0001f));

            float yRotRad = transform.rotation.eulerAngles.y * Mathf.Deg2Rad;
            sensor.AddObservation(Mathf.Sin(yRotRad));

            float normalizedDistance = Mathf.Clamp01(toTarget.magnitude / Mathf.Max(maxDistance, 0.0001f));
            sensor.AddObservation(normalizedDistance);

            Vector3 localDirection = transform.InverseTransformDirection(toTarget.normalized);
            sensor.AddObservation(localDirection.x);
            sensor.AddObservation(localDirection.z);

            sensor.AddObservation(movementController.IsGrounded() ? 1f : 0f);
            float normalizedVerticalVelocity = Mathf.Clamp(velocity.y / Mathf.Max(verticalVelocityNormalization, 0.0001f), -1f, 1f);
            sensor.AddObservation(normalizedVerticalVelocity);

            float cooldownNormalized = jumpCooldownSeconds <= 0f
                ? 1f
                : Mathf.Clamp01(1f - (_jumpCooldownTimer / Mathf.Max(jumpCooldownSeconds, 0.0001f)));
            sensor.AddObservation(cooldownNormalized);
            sensor.AddObservation(_targetCurrentlyOnObstacle ? 1f : 0f);
            sensor.AddObservation(_isCurrentlySprinting ? 1f : 0f);
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            if (!HasValidReferences())
            {
                return;
            }

            _episodeTimer += Time.deltaTime;
            if (_episodeTimer > maxEpisodeSeconds)
            {
                AddReward(overtimePenalty);
                // Count only timeouts as failures "before time's up"
                _timeoutBeforeTargetCount++;
                LogFailure(StatTimeouts);
                EndEpisode();
                return;
            }

            _jumpCooldownTimer = Mathf.Max(_jumpCooldownTimer - Time.deltaTime, 0f);
            ApplyMovement(actions);
            HandleJumpAction(actions.ContinuousActions[4]);

            Vector3 currentVelocity = movementController.GetVelocity();
            float distanceToTarget = Vector3.Distance(transform.position, target.position);
            if (CheckTerminalConditions(distanceToTarget))
            {
                return;
            }

            ApplyShapingRewards(distanceToTarget);
            HandleObstaclePenalty();
            HandleStuckPenalty(currentVelocity);
            AddReward(timePenalty);
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var continuous = actionsOut.ContinuousActions;
            StarterAssetsInputs starterInputs = GetComponent<StarterAssetsInputs>();
            if (starterInputs != null)
            {
                continuous[0] = starterInputs.move.y;
                continuous[1] = starterInputs.move.x;
                continuous[2] = 0f;
                continuous[3] = starterInputs.sprint ? 1f : 0f;
                continuous[4] = starterInputs.jump ? 1f : 0f;
                return;
            }

            for (int i = 0; i < continuous.Length; i++)
            {
                continuous[i] = 0f;
            }
        }

        private void ApplyCurriculumPhase()
        {
            CurriculumPhase phase = CurrentPhase;
            ApplyPhaseFlags(phase);
            ApplyEnvironmentPhase(phase);
        }

        private static void ApplyPhaseFlags(CurriculumPhase phase)
        {
            // Per-phase hooks: set phase flags explicitly based on id
            // so environment toggles and target placement remain in sync.
            switch (phase.id)
            {
                case "GroundOnly":
                    phase.enableObstacles = false;
                    phase.enableStructures = false;
                    phase.allowTargetOnObstacles = false;
                    phase.requireTargetOnObstacles = false;
                    break;
                case "Obstacles":
                    phase.enableObstacles = true;
                    phase.enableStructures = false;
                    phase.allowTargetOnObstacles = false;
                    phase.requireTargetOnObstacles = false;
                    break;
                case "Structures":
                    phase.enableObstacles = true;
                    phase.enableStructures = true;
                    phase.allowTargetOnObstacles = false;
                    phase.requireTargetOnObstacles = false;
                    break;
                case "TargetOnObstacles":
                    phase.enableObstacles = true;
                    phase.enableStructures = true;
                    phase.allowTargetOnObstacles = true;
                    phase.requireTargetOnObstacles = true;
                    break;
            }
        }


        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.collider != null && hit.collider.CompareTag("Obstacle"))
            {
                _recentObstacleCollision = true;
                _lastObstacleContactTime = Time.time;
            }
        }

        private bool HasValidReferences()
        {
            return movementController != null && target != null;
        }

        private void ApplyMovement(ActionBuffers actions)
        {
            float moveForward = actions.ContinuousActions[0];
            float moveStrafe = actions.ContinuousActions[1];
            float runSignal = actions.ContinuousActions[3];
            Vector2 moveInput = new Vector2(moveStrafe, moveForward);
            _isCurrentlySprinting = runSignal > sprintActionThreshold;
            movementController.SetMovementInput(moveInput);
            movementController.SetSprintInput(_isCurrentlySprinting);
        }

        private void HandleJumpAction(float jumpSignal)
        {
            bool jumpRequested = jumpSignal > jumpActionThreshold;
            bool grounded = movementController.IsGrounded();
            bool canJump = jumpRequested && grounded && _jumpCooldownTimer <= 0f;
            movementController.SetJumpInput(canJump);

            if (canJump)
            {
                _jumpCooldownTimer = jumpCooldownSeconds;
                _jumpRewardPending = true;
            }
            else
            {
                ApplyInvalidJumpPenalty(jumpRequested);
            }

            EvaluatePendingJumpReward();
        }

        private void EvaluatePendingJumpReward()
        {
            if (!_jumpRewardPending || movementController.IsGrounded())
            {
                return;
            }

            AddReward(successfulJumpReward);
            _jumpRewardPending = false;
        }

        private void ApplyInvalidJumpPenalty(bool jumpRequested)
        {
            if (!jumpRequested)
            {
                return;
            }

            AddReward(invalidJumpPenalty);
        }

        private bool CheckTerminalConditions(float distanceToTarget)
        {
            if (distanceToTarget < targetReachedThreshold)
            {
                float timeBonus = Mathf.Clamp01(1f - (_episodeTimer / Mathf.Max(1f, maxEpisodeSeconds)));
                AddReward(1f + timeBonus * 0.5f);
                if (_targetCurrentlyOnObstacle)
                {
                    AddReward(elevatedTargetCompletionBonus);
                }
                RecordSuccess();
                EndEpisode();
                return true;
            }

            if (distanceToTarget > maxDistanceFromTarget)
            {
                AddReward(-1f);
                LogFailure(StatOutOfBounds);
                EndEpisode();
                return true;
            }

            if (transform.position.y < fallThreshold)
            {
                AddReward(-1f);
                LogFailure(StatFalls);
                EndEpisode();
                return true;
            }

            return false;
        }

        private void ApplyShapingRewards(float distanceToTarget)
        {
            float distanceDelta = _lastDistanceToTarget - distanceToTarget;
            AddReward(distanceDelta * distanceRewardScale);
            if (_targetCurrentlyOnObstacle && distanceDelta > 0f)
            {
                AddReward(distanceDelta * elevatedTargetProgressBonus);
            }
            _lastDistanceToTarget = distanceToTarget;
        }

        private void HandleObstaclePenalty()
        {
            if (!_recentObstacleCollision)
            {
                return;
            }

            AddReward(obstacleCollisionPenalty);
            _recentObstacleCollision = false;
        }

        private void HandleStuckPenalty(Vector3 velocity)
        {
            if (!enableStuckPenalty)
            {
                return;
            }

            if (_stuckPenaltyCooldownTimer > 0f)
            {
                _stuckPenaltyCooldownTimer = Mathf.Max(0f, _stuckPenaltyCooldownTimer - Time.deltaTime);
            }

            bool phaseHasObstacles = CurrentPhase.enableObstacles;
            if (!phaseHasObstacles)
            {
                _stuckTimer = 0f;
                return;
            }

            bool lowVelocity = velocity.magnitude <= Mathf.Max(stuckVelocityThreshold, 0.0001f);
            bool recentObstacleHit = Time.time - _lastObstacleContactTime <= stuckContactMemory;

            if (lowVelocity && recentObstacleHit)
            {
                _stuckTimer += Time.deltaTime;
                if (_stuckTimer >= stuckTimeThreshold && _stuckPenaltyCooldownTimer <= 0f)
                {
                    AddReward(stuckPenalty);
                    LogStat(StatStuckPenalties);
                    _stuckPenaltyCooldownTimer = Mathf.Max(stuckPenaltyCooldown, 0f);
                    _stuckTimer = 0f;
                }
            }
            else
            {
                _stuckTimer = Mathf.Max(_stuckTimer - Time.deltaTime, 0f);
            }
        }

        private void LogStat(string metricName, float value = 1f)
        {
            _statsRecorder?.Add(metricName, value);
        }

        private void LogFailure(string reasonMetric)
        {
            LogStat(StatFailures);
            if (!string.IsNullOrEmpty(reasonMetric))
            {
                LogStat(reasonMetric);
            }
        }

        private int ResolveCurriculumStage()
        {
            if (curriculum == null || curriculum.Count == 0)
            {
                return 0;
            }

            float stageValue = Academy.Instance.EnvironmentParameters.GetWithDefault(CurriculumStageParameter, 0f);
            return Mathf.Clamp(Mathf.RoundToInt(stageValue), 0, curriculum.Count - 1);
        }

        private void RecordSuccess()
        {
            _reachedTargetCount++;
            if (_episodeTimer < _bestTimeToTarget)
            {
                _bestTimeToTarget = _episodeTimer;
            }

            LogStat(StatSuccesses);
            LogStat(StatTimeToTarget, _episodeTimer);
            if (_targetCurrentlyOnObstacle)
            {
                LogStat(StatObstacleTargets);
            }
            else
            {
                LogStat(StatGroundTargets);
            }
        }
    }
}
