using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using StarterAssets;
using MLAgents.Shhhunt.Obstacles;
using MLReachTargetObstacleAgent.Scripts;

namespace MLAgents.Shhhunt.Curriculum
{
    /// <summary>
    /// Curriculum-driven Reach Target agent that gradually unlocks obstacles, structures, and target placements.
    /// </summary>
    [RequireComponent(typeof(ThirdPersonControllerAdapter))]
    public class ShhhuntCurriculumLearningAgent : Agent
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
        [SerializeField] private float obstacleCollisionPenalty = -0.1f;
        [SerializeField] private float invalidJumpPenalty = -0.02f;

#if UNITY_EDITOR
        [Header("Sensor Checklist")]
        [TextArea(4, 8)]
        [SerializeField] private string sensorSetupNotes =
            "Add RayPerceptionSensorComponent3D for Obstacle/Ground/Target tags." +
            "\nMatch 5 continuous actions: [forward, strafe, reserved, sprint, jump]." +
            "\nVector observations: 15 floats (without rays).";
#endif

        private const string CurriculumStageParameter = "shhhunt_curriculum_stage";

        private readonly List<Collider> _cachedObstacleColliders = new List<Collider>();
        private float _lastDistanceToTarget;
        private float _jumpCooldownTimer;
        private bool _jumpRewardPending;
        private bool _recentObstacleCollision;
        private bool _targetCurrentlyOnObstacle;
        private bool _isCurrentlySprinting;
        private float _episodeTimer;
        private int _currentPhaseIndex;

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
                EndEpisode();
                return;
            }

            _jumpCooldownTimer = Mathf.Max(_jumpCooldownTimer - Time.deltaTime, 0f);
            ApplyMovement(actions);
            HandleJumpAction(actions.ContinuousActions[4]);

            float distanceToTarget = Vector3.Distance(transform.position, target.position);
            if (CheckTerminalConditions(distanceToTarget))
            {
                return;
            }

            ApplyShapingRewards(distanceToTarget);
            HandleObstaclePenalty();
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
            // Pseudocode placeholder for future per-phase hooks:
            // switch (phase.id)
            // {
            //     case "GroundOnly":
            //         // Disable every obstacle/structure and pin targets to ground.
            //         break;
            //     case "Obstacles":
            //         // Enable only obstacles; keep structures hidden.
            //         break;
            //     case "Structures":
            //         // Enable both obstacles and structures for extended parkour.
            //         break;
            //     case "TargetOnObstacles":
            //         // Leave everything active and force target placement on elevated geometry.
            //         break;
            // }
            if (environmentManager != null)
            {
                // Placeholder: when stage advances, invoke EnableAllObstacles/EnableAllStructure
                // exactly once to avoid redundant SetActive calls.
                if (phase.enableObstacles)
                {
                    environmentManager.EnableAllObstacles();
                }
                else
                {
                    environmentManager.DisableAllObstacles();
                }

                // Placeholder: Use FIFO helpers (EnableNextStructure / DisableNextStructure)
                // if you want to reveal geometry gradually instead of toggling everything.
                if (phase.enableStructures)
                {
                    environmentManager.EnableAllStructure();
                }
                else
                {
                    environmentManager.DisableAllStructure();
                }
            }
        }

        private void ResetAgentTransform()
        {
            if (movementController != null)
            {
                movementController.ResetVelocity();
                movementController.SetJumpInput(false);
            }

            Vector3 spawnPoint = randomizeStartPosition ? FindValidSpawnPoint() : transform.position;
            transform.position = spawnPoint;
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            _jumpRewardPending = false;
            _recentObstacleCollision = false;
            _isCurrentlySprinting = false;
        }

        private void PlaceTarget()
        {
            if (target == null)
            {
                return;
            }

            CurriculumPhase phase = CurrentPhase;
            if (targetPositioner != null)
            {
                ConfigureTargetPositioner(phase);
                // Placeholder: tie TargetPositioner.AllowOnObstacles to curriculum stage so
                // EnvironmentManager + target placement stay in sync.
                targetPositioner.PlaceTarget(phase.randomizeTarget);
                _targetCurrentlyOnObstacle = targetPositioner.TargetCurrentlyOnObstacle;
                return;
            }

            Vector3 obstaclePos = Vector3.zero;
            bool placedOnObstacle = phase.allowTargetOnObstacles && TryGetRandomObstacleTop(out obstaclePos);
            if (phase.requireTargetOnObstacles && !placedOnObstacle)
            {
                placedOnObstacle = TryGetRandomObstacleTop(out obstaclePos);
            }

            Vector3 groundPos = GetRandomGroundPosition(targetGroundYOffset);
            target.position = placedOnObstacle ? obstaclePos : groundPos;
            _targetCurrentlyOnObstacle = placedOnObstacle;
        }

        private void ConfigureTargetPositioner(CurriculumPhase phase)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo allowField = typeof(TargetPositioner).GetField("allowTargetOnObstacles", flags);
            FieldInfo mustField = typeof(TargetPositioner).GetField("targetMustSpawnOnObstacles", flags);
            FieldInfo onField = typeof(TargetPositioner).GetField("targetOnObstacleYOffset", flags);
            FieldInfo groundField = typeof(TargetPositioner).GetField("targetGroundYOffset", flags);
            allowField?.SetValue(targetPositioner, phase.allowTargetOnObstacles);
            mustField?.SetValue(targetPositioner, phase.requireTargetOnObstacles);
            onField?.SetValue(targetPositioner, targetOnObstacleYOffset);
            groundField?.SetValue(targetPositioner, targetGroundYOffset);
        }

        private Vector3 FindValidSpawnPoint()
        {
            for (int attempt = 0; attempt < Mathf.Max(spawnRetries, 1); attempt++)
            {
                Vector3 candidate = GetRandomGroundPosition(agentSpawnYOffset);
                if (!Physics.CheckSphere(candidate, spawnClearanceRadius, obstacleMask, QueryTriggerInteraction.Ignore))
                {
                    return candidate;
                }
            }

            return GetRandomGroundPosition(agentSpawnYOffset);
        }

        private Vector3 GetRandomGroundPosition(float heightOffset)
        {
            Vector3 center = GetAreaCenter();
            float x = Random.Range(-spawnAreaSize.x * 0.5f, spawnAreaSize.x * 0.5f);
            float z = Random.Range(-spawnAreaSize.z * 0.5f, spawnAreaSize.z * 0.5f);
            Vector3 rayStart = center + new Vector3(x, 10f, z);
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 30f, groundMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point + Vector3.up * heightOffset;
            }

            return center + new Vector3(x, heightOffset, z);
        }

        private Vector3 GetAreaCenter()
        {
            if (areaCenter != null)
            {
                return areaCenter.position;
            }

            if (ground != null)
            {
                return ground.position;
            }

            return transform.parent != null ? transform.parent.position : Vector3.zero;
        }

        private bool TryGetRandomObstacleTop(out Vector3 position)
        {
            CleanCachedObstacleList();
            if (_cachedObstacleColliders.Count == 0)
            {
                position = Vector3.zero;
                return false;
            }

            Collider chosen = _cachedObstacleColliders[Random.Range(0, _cachedObstacleColliders.Count)];
            Bounds bounds = chosen.bounds;
            float x = Random.Range(bounds.min.x, bounds.max.x);
            float z = Random.Range(bounds.min.z, bounds.max.z);
            position = new Vector3(x, bounds.max.y + targetOnObstacleYOffset, z);
            return true;
        }

        private void CacheObstacleColliders()
        {
            _cachedObstacleColliders.Clear();
            foreach (Collider manual in manualObstacleColliders)
            {
                if (manual != null && !_cachedObstacleColliders.Contains(manual))
                {
                    _cachedObstacleColliders.Add(manual);
                }
            }

            if (!autoCollectObstacles || string.IsNullOrEmpty(obstacleTag))
            {
                return;
            }

            GameObject[] taggedObstacles = GameObject.FindGameObjectsWithTag(obstacleTag);
            foreach (GameObject obstacle in taggedObstacles)
            {
                if (obstacle.TryGetComponent(out Collider collider) && !_cachedObstacleColliders.Contains(collider))
                {
                    _cachedObstacleColliders.Add(collider);
                }
            }
        }

        private void CleanCachedObstacleList()
        {
            for (int i = _cachedObstacleColliders.Count - 1; i >= 0; i--)
            {
                Collider collider = _cachedObstacleColliders[i];
                if (collider == null || !collider.gameObject.activeInHierarchy)
                {
                    _cachedObstacleColliders.RemoveAt(i);
                }
            }
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.collider != null && hit.collider.CompareTag("Obstacle"))
            {
                _recentObstacleCollision = true;
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
                EndEpisode();
                return true;
            }

            if (distanceToTarget > maxDistanceFromTarget)
            {
                AddReward(-1f);
                EndEpisode();
                return true;
            }

            if (transform.position.y < fallThreshold)
            {
                AddReward(-1f);
                EndEpisode();
                return true;
            }

            return false;
        }

        private void ApplyShapingRewards(float distanceToTarget)
        {
            float distanceDelta = _lastDistanceToTarget - distanceToTarget;
            AddReward(distanceDelta * distanceRewardScale);
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

        private int ResolveCurriculumStage()
        {
            if (curriculum == null || curriculum.Count == 0)
            {
                return 0;
            }

            float stageValue = Academy.Instance.EnvironmentParameters.GetWithDefault(CurriculumStageParameter, 0f);
            return Mathf.Clamp(Mathf.RoundToInt(stageValue), 0, curriculum.Count - 1);
        }
    }
}
