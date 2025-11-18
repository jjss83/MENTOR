using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using StarterAssets;

namespace MLAgents.Shhhunt
{
    /// <summary>
    /// ML-Agents Agent that learns to navigate and reach a target position.
    /// Uses the ThirdPersonController for movement via the IMovementController interface.
    /// </summary>
    public class MLReachTargetAgent : Agent
    {
        [Header("Target & Environment")]
        [Tooltip("The target object the agent should reach")]
        [SerializeField] private Transform target;
        
        [Tooltip("The ground/platform the agent is on")]
        [SerializeField] private Transform ground;
        
        [Header("Agent Configuration")]
        [Tooltip("Distance threshold to consider target reached")]
        [SerializeField] private float targetReachedThreshold = 1.5f;
        
        [Tooltip("Y position threshold for falling off platform")]
        [SerializeField] private float fallThreshold = -5f;
        
        [Tooltip("Maximum distance from target before episode fails")]
        [SerializeField] private float maxDistanceFromTarget = 100f;
        
        [Tooltip("Maximum distance for normalization (should match training area size)")]
        [SerializeField] private float maxDistance = 50f;
        
        [Tooltip("Maximum speed for velocity normalization")]
        [SerializeField] private float maxSpeed = 5.335f; // ThirdPersonController's SprintSpeed
        
        [Header("Reward Configuration")]
        [Tooltip("Reward scale for distance-based shaping")]
        [SerializeField] private float distanceRewardScale = 0.005f;
        
        [Tooltip("Time penalty per step to encourage efficiency")]
        [SerializeField] private float timePenalty = -0.0001f;
        
        [Header("Component References")]
        [Tooltip("Movement controller interface (assign ThirdPersonControllerAdapter)")]
        [SerializeField] private ThirdPersonControllerAdapter movementController;
        
        [Header("Spawn Configuration")]
        [Tooltip("Randomize agent start position on episode begin")]
        [SerializeField] private bool randomizeStartPosition = true;
        
        [Tooltip("Randomize target position on episode begin")]
        [SerializeField] private bool randomizeTargetPosition = true;
        
        [Tooltip("Area bounds for random spawning")]
        [SerializeField] private Vector3 spawnAreaSize = new Vector3(20f, 0f, 20f);
        
        // Private tracking variables
        private float _lastDistanceToTarget;
        private float _episodeStartTime;
        
        /// <summary>
        /// Initialize the agent - called once when enabled
        /// </summary>
        public override void Initialize()
        {
            // Auto-assign movement controller if not set
            if (movementController == null)
            {
                movementController = GetComponent<ThirdPersonControllerAdapter>();
                
                if (movementController == null)
                {
                    Debug.LogError("ThirdPersonControllerAdapter not found! Please add it to the agent GameObject.");
                }
            }
        }
        
        /// <summary>
        /// Called at the beginning of each episode
        /// Resets agent and target positions, velocities, and tracking variables
        /// </summary>
        public override void OnEpisodeBegin()
        {
            // 1. Reset Agent Position
            if (randomizeStartPosition)
            {
                transform.localPosition = GetRandomPositionInArea();
            }
            else
            {
                transform.localPosition = Vector3.zero;
            }
            
            // Random rotation to make learning rotation-invariant
            transform.localRotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            
            // 2. Reset Agent Velocity
            if (movementController != null)
            {
                movementController.ResetVelocity();
            }
            
            // 3. Randomize Target Position
            if (target != null)
            {
                if (randomizeTargetPosition)
                {
                    target.localPosition = GetRandomPositionInArea();
                }
                else
                {
                    // Fixed position for initial testing
                    target.localPosition = new Vector3(10f, 0.5f, 10f);
                }
            }
            
            // 4. Reset internal tracking variables
            _lastDistanceToTarget = Vector3.Distance(transform.position, target.position);
            _episodeStartTime = Time.time;
        }
        
        /// <summary>
        /// Collect observations about the environment
        /// Total: 10 observations (all normalized to [-1, 1] or [0, 1])
        /// </summary>
        public override void CollectObservations(VectorSensor sensor)
        {
            if (target == null || movementController == null)
            {
                // Add zero observations if references are missing
                for (int i = 0; i < 10; i++)
                {
                    sensor.AddObservation(0f);
                }
                return;
            }
            
            // 1. Agent Position Relative to Target (3 values)
            Vector3 relativePosition = (target.position - transform.position) / maxDistance;
            sensor.AddObservation(relativePosition); // x, y, z
            
            // 2. Agent Velocity (3 values)
            Vector3 velocity = movementController.GetVelocity();
            Vector3 normalizedVelocity = velocity / maxSpeed;
            sensor.AddObservation(normalizedVelocity); // x, y, z
            
            // 3. Agent Y Rotation (1 value) - using sine for continuity
            float yRotRad = transform.rotation.eulerAngles.y * Mathf.Deg2Rad;
            float yRotNormalized = Mathf.Sin(yRotRad);
            sensor.AddObservation(yRotNormalized);
            
            // 4. Distance to Target (1 value)
            float distance = Vector3.Distance(transform.position, target.position);
            float normalizedDistance = Mathf.Clamp01(distance / maxDistance);
            sensor.AddObservation(normalizedDistance);
            
            // 5. Target Direction in Local Space (2 values)
            Vector3 toTarget = target.position - transform.position;
            Vector3 localDirection = transform.InverseTransformDirection(toTarget.normalized);
            sensor.AddObservation(localDirection.x); // Right/Left
            sensor.AddObservation(localDirection.z); // Forward/Back
            
            // Total: 3 + 3 + 1 + 1 + 2 = 10 observations
        }
        
        /// <summary>
        /// Process actions received from the neural network or heuristic
        /// </summary>
        public override void OnActionReceived(ActionBuffers actions)
        {
            if (movementController == null || target == null)
                return;
            
            // 1. Extract and apply actions
            float moveForward = actions.ContinuousActions[0]; // [-1, 1]
            float moveStrafe = actions.ContinuousActions[1];  // [-1, 1]
            
            Vector2 moveInput = new Vector2(moveStrafe, moveForward);
            movementController.SetMovementInput(moveInput);
            
            // 2. Check if target reached (Success!)
            float distanceToTarget = Vector3.Distance(transform.position, target.position);
            
            if (distanceToTarget < targetReachedThreshold)
            {
                AddReward(+1.0f); // Large positive reward
                EndEpisode();
                return;
            }
            
            // 3. Distance-based reward shaping (getting closer is good)
            float distanceDelta = _lastDistanceToTarget - distanceToTarget;
            float distanceReward = distanceDelta * distanceRewardScale;
            AddReward(distanceReward);
            _lastDistanceToTarget = distanceToTarget;
            
            // 4. Time penalty (encourage efficiency)
            AddReward(timePenalty);
            
            // 5. Check if too far from target (Failure!)
            if (distanceToTarget > maxDistanceFromTarget)
            {
                AddReward(-1.0f); // Large negative reward
                EndEpisode();
                return;
            }
            
            // 6. Check if fallen off platform (Failure!)
            if (transform.position.y < fallThreshold)
            {
                AddReward(-1.0f); // Large negative reward
                EndEpisode();
                return;
            }
        }
        
        /// <summary>
        /// Manual control for testing (WASD keys)
        /// Enable by setting Behavior Type to "Heuristic Only" in Inspector
        /// </summary>
        public override void Heuristic(in ActionBuffers actionsOut)
        {
            ActionSegment<float> continuousActions = actionsOut.ContinuousActions;
            
            // Get input from StarterAssetsInputs (works with new Input System)
            StarterAssetsInputs input = GetComponent<StarterAssetsInputs>();
            
            if (input != null)
            {
                // Map WASD input to actions
                continuousActions[0] = input.move.y; // Forward/Back
                continuousActions[1] = input.move.x; // Left/Right
            }
            else
            {
                // No input available - agent won't move in Heuristic mode
                continuousActions[0] = 0f;
                continuousActions[1] = 0f;
                
                if (Time.frameCount % 120 == 0) // Log warning every 2 seconds
                {
                    Debug.LogWarning("StarterAssetsInputs component not found! Heuristic control disabled.");
                }
            }
        }
        
        /// <summary>
        /// Get a random position within the spawn area
        /// </summary>
        private Vector3 GetRandomPositionInArea()
        {
            float x = Random.Range(-spawnAreaSize.x / 2f, spawnAreaSize.x / 2f);
            float z = Random.Range(-spawnAreaSize.z / 2f, spawnAreaSize.z / 2f);
            float y = 0.5f; // Slightly above ground
            
            return new Vector3(x, y, z);
        }
        
        /// <summary>
        /// Debug visualization in Scene view
        /// </summary>
        private void OnDrawGizmos()
        {
            if (target == null)
                return;
            
            // Draw line to target
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, target.position);
            
            // Draw target reach threshold
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(target.position, targetReachedThreshold);
            
            // Draw velocity (if in play mode and has controller)
            if (Application.isPlaying && movementController != null)
            {
                Gizmos.color = Color.blue;
                Vector3 velocity = movementController.GetVelocity();
                Gizmos.DrawRay(transform.position, velocity);
            }
            
            // Draw spawn area
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireCube(Vector3.zero, spawnAreaSize);
        }
    }
}
