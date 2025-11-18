using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

// Auto-generated stub for the SoccerTwos example.
// Replace or extend this with the real logic from your project.
public class SoccerTwosAgent : Agent
{
    public override void Initialize()
    {
        // TODO: Initialize agent state here.
    }

    public override void OnEpisodeBegin()
    {
        // TODO: Reset environment and agent state here.
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // TODO: Add observations to the sensor.
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // TODO: Apply actions, update environment, assign rewards, and handle termination.
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // OPTIONAL: Implement a manual control policy for debugging.
    }
}
