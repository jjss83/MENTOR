using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using StarterAssets;

namespace MLAgents.Shhhunt.Obstacles
{
    /// <summary>
    /// ML-Agents Agent variant that reaches a target while jumping onto/around obstacles.
    /// Independent from the base agent to keep training setups isolated per folder.
    /// </summary>
    [RequireComponent(typeof(ThirdPersonControllerAdapter))]
    public class MLReachTargetObstacleAgent : Agent
    {
        [Header("Target & Environment")]
        [Tooltip("Target transform the agent must reach")]
        [SerializeField] private Transform target;
        
        [Tooltip("Optional override for the center of the training area. If null ground position is used.")]
        [SerializeField] private Transform areaCenter;
        
        [Tooltip("Reference ground object for gizmos and fallback positioning")]
        [SerializeField] private Transform ground;
        
        [Header("Obstacle Placement")]
        [Tooltip("Allow the target to spawn on top of tagged obstacles")]
        [SerializeField] private bool allowTargetOnObstacles = true;
        
        [Tooltip("Tag applied to every obstacle the agent should avoid/jump")]
        [SerializeField] private string obstacleTag = "Obstacle";
        
        [Tooltip("Automatically collect colliders tagged as obstacles at the start of each episode")]
        [SerializeField] private bool autoCollectObstacles = true;
        
        [Tooltip("Optional manual obstacle colliders (added in addition to auto collection)")]
        [SerializeField] private List<Collider> manualObstacleColliders = new List<Collider>();
        
        [Tooltip("Vertical offset when placing the target on top of an obstacle")]
        [SerializeField] private float targetOnObstacleYOffset = 0.15f;
        
        [Tooltip("Vertical offset when placing the target on flat ground")]
        [SerializeField] private float targetGroundYOffset = 0.5f;
        
        [Header("Agent Configuration")]
        [Tooltip("Distance threshold to consider the target reached")]
        [SerializeField] private float targetReachedThreshold = 1.25f;
        
        [Tooltip("Y threshold that counts as falling off the platform")]
        [SerializeField] private float fallThreshold = -5f;
        
        [Tooltip("Maximum allowed distance from the target before failing")]
        [SerializeField] private float maxDistanceFromTarget = 100f;
        
        [Tooltip("Max distance used to normalize observations")]
        [SerializeField] private float maxDistance = 50f;
        
        [Tooltip("Max horizontal speed for velocity normalization")]
        [SerializeField] private float maxSpeed = 5.335f;
        
        [Tooltip("Max vertical speed magnitude for normalization")]
        [SerializeField] private float verticalVelocityNormalization = 10f;
        
        [Tooltip("Randomize agent spawn position each episode")]
        [SerializeField] private bool randomizeStartPosition = true;
        
        [Tooltip("Randomize target position each episode")]
        [SerializeField] private bool randomizeTargetPosition = true;
        
        [Tooltip("Half-extents of the spawn area around Area Center")]
        [SerializeField] private Vector3 spawnAreaSize = new Vector3(20f, 0f, 20f);
        
        [Tooltip("Layer mask used for ground sampling raycasts")]
        [SerializeField] private LayerMask groundMask = ~0;
        
        [Tooltip("Layer mask used when validating spawn clearance against obstacles")]
        [SerializeField] private LayerMask obstacleMask = ~0;
        
        [Tooltip("Attempts to find a valid (non-overlapping) spawn point")]
        [SerializeField] private int spawnRetries = 10;
        
        [Tooltip("Radius checked when ensuring the spawn is not inside an obstacle")]
        [SerializeField] private float spawnClearanceRadius = 0.75f;
        
        [Tooltip("Additional height added to agent spawns")]
        [SerializeField] private float agentSpawnYOffset = 0.5f;
        
        [Header("Movement & Actions")]
        [Tooltip("Movement adapter (auto-assigned if left null)")]
        [SerializeField] private ThirdPersonControllerAdapter movementController;
        
        [Tooltip("Continuous action threshold above which sprint/run input is considered active")]
        [SerializeField] private float sprintActionThreshold = 0.5f;
        
        [Tooltip("Continuous action value above this threshold counts as a jump request")]
        [SerializeField] private float jumpActionThreshold = 0.5f;
        
        [Tooltip("Cooldown between successful jumps (seconds)")]
        [SerializeField] private float jumpCooldownSeconds = 0.75f;
        
        [Tooltip("Reward granted when a jump successfully leaves the ground")]
        [SerializeField] private float successfulJumpReward = 0.05f;
        
        [Header("Rewards & Penalties")]
        [Tooltip("Distance shaping multiplier (positive when getting closer)")]
        [SerializeField] private float distanceRewardScale = 0.01f;
        
        [Tooltip("Per-step time penalty (negative to encourage speed)")]
        [SerializeField] private float timePenalty = -0.00015f;
        
        [Tooltip("Penalty applied when colliding with an obstacle")]
        [SerializeField] private float obstacleCollisionPenalty = -0.1f;
        
        [Tooltip("Penalty applied when jump requests are invalid (airborne/cooldown)")]
        [SerializeField] private float invalidJumpPenalty = -0.02f;
        
        [Header("Sensor Setup Checklist")]
#if UNITY_EDITOR
        [TextArea(4, 8)]
        [SerializeField] private string sensorSetupNotes =
            "Add one or more RayPerceptionSensorComponent3D to this agent. Configure each with:" +
            "\n- Detectable Tags = [Obstacle, Ground, Target]" +
            "\n- Rays Per Direction = 3 | Max Ray Degrees = 30 | Ray Length ~= 10" +
            "\n- Sphere Cast Radius ~= 0.1" +
            "\nDuplicate a second sensor pitched upward (Start Offset ~= 0.5, End Offset ~= 1.5)" +
            "\nEnsure every obstacle GameObject uses the 'Obstacle' tag so sensors and placement logic can find them." +
            "\n\nAction Space: 5 continuous actions [forward, strafe, reserved, run/sprint, jump]" +
            "\nObservation Space: 15 vector observations (excluding ray sensors)";
#endif
        
        // Internal state
        private readonly List<Collider> _cachedObstacleColliders = new List<Collider>();
        private float _lastDistanceToTarget;
        private float _jumpCooldownTimer;
        private bool _jumpRewardPending;
        private bool _recentObstacleCollision;
        private bool _targetCurrentlyOnObstacle;
        private bool _isCurrentlySprinting;
        
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
        }
        
        public override void OnEpisodeBegin()
        {
            CacheObstacleColliders();
            ResetAgentTransform();
            PlaceTarget();
            _lastDistanceToTarget = target != null ? Vector3.Distance(transform.position, target.position) : 0f;
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
            Vector3 relativePosition = toTarget / Mathf.Max(maxDistance, 0.0001f);
            sensor.AddObservation(relativePosition);
            
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
        }
        
        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var continuous = actionsOut.ContinuousActions;
            StarterAssetsInputs starterInputs = GetComponent<StarterAssetsInputs>();
            if (starterInputs != null)
            {
                continuous[0] = starterInputs.move.y;
                continuous[1] = starterInputs.move.x;
                continuous[2] = 0f; // Reserved for future use
                continuous[3] = starterInputs.sprint ? 1f : 0f;
                continuous[4] = starterInputs.jump ? 1f : 0f;
            }
            else
            {
                continuous[0] = 0f;
                continuous[1] = 0f;
                continuous[2] = 0f;
                continuous[3] = 0f;
                continuous[4] = 0f;
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
            _jumpCooldownTimer = 0f;
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
            
            if (!randomizeTargetPosition)
            {
                _targetCurrentlyOnObstacle = false;
                target.position = GetAreaCenter() + new Vector3(10f, targetGroundYOffset, 10f);
                return;
            }
            
            Vector3 obstaclePos = Vector3.zero;
            bool placedOnObstacle = allowTargetOnObstacles && TryGetRandomObstacleTop(out obstaclePos);
            Vector3 groundPos = GetRandomGroundPosition(targetGroundYOffset);
            target.position = placedOnObstacle ? obstaclePos : groundPos;
            _targetCurrentlyOnObstacle = placedOnObstacle;
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
            
            if (!autoCollectObstacles)
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
                if (_cachedObstacleColliders[i] == null)
                {
                    _cachedObstacleColliders.RemoveAt(i);
                }
            }
        }
        
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.collider != null && hit.collider.CompareTag(obstacleTag))
            {
                _recentObstacleCollision = true;
            }
        }
        
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.4f);
            Vector3 center = GetAreaCenter();
            Gizmos.DrawWireCube(center, new Vector3(spawnAreaSize.x, 0.1f, spawnAreaSize.z));
            
            if (target != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(target.position, targetReachedThreshold);
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
                AddReward(+1.0f);
                EndEpisode();
                return true;
            }
            
            if (distanceToTarget > maxDistanceFromTarget)
            {
                AddReward(-1.0f);
                EndEpisode();
                return true;
            }
            
            if (transform.position.y < fallThreshold)
            {
                AddReward(-1.0f);
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
            AddReward(timePenalty);
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
    }
}
