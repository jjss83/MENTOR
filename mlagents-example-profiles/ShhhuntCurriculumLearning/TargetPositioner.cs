using System.Collections.Generic;
using UnityEngine;

namespace MLAgents.Shhhunt.Obstacles
{
    public class TargetPositioner : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Transform areaCenter;
        [SerializeField] private Transform ground;
        [SerializeField] private string obstacleTag = "Obstacle";
        [SerializeField] private float targetOnObstacleYOffset = 0.15f;
        [SerializeField] private float targetGroundYOffset = 0.5f;
        [SerializeField] private bool allowTargetOnObstacles = true;
        [SerializeField] private bool targetMustSpawnOnObstacles = false;
        [SerializeField] private Vector3 spawnAreaSize = new Vector3(20f, 0f, 20f);
        [SerializeField] private LayerMask groundMask = ~0;

        public bool TargetCurrentlyOnObstacle { get; private set; }

        public void PlaceTarget(bool randomizeTargetPosition)
        {
            if (target == null) return;

            if (!randomizeTargetPosition)
            {
                TargetCurrentlyOnObstacle = false;
                target.position = GetAreaCenter() + new Vector3(10f, targetGroundYOffset, 10f);
                return;
            }

            Vector3 groundPos = GetRandomGroundPosition(targetGroundYOffset);
            bool placedOnElevatedSurface = TryGetElevatedTargetPosition(out Vector3 elevatedPos);
            if (targetMustSpawnOnObstacles)
            {
                if (placedOnElevatedSurface)
                {
                    target.position = elevatedPos;
                    TargetCurrentlyOnObstacle = true;
                }
                else
                {
                    Debug.LogWarning("TargetPositioner requires obstacle-only target placement, but no valid obstacle was found. Target position remains unchanged.");
                }
                return;
            }

            if (placedOnElevatedSurface)
            {
                target.position = elevatedPos;
                TargetCurrentlyOnObstacle = true;
                return;
            }

            target.position = groundPos;
            TargetCurrentlyOnObstacle = false;
        }

        private Vector3 GetAreaCenter()
        {
            if (areaCenter != null) return areaCenter.position;
            if (ground != null) return ground.position;
            return transform.parent != null ? transform.parent.position : Vector3.zero;
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

        private bool TryGetElevatedTargetPosition(out Vector3 position)
        {
            if ((allowTargetOnObstacles || targetMustSpawnOnObstacles) && TryGetRandomObstacleTop(out position))
            {
                return true;
            }
            position = Vector3.zero;
            return false;
        }

        private bool TryGetRandomObstacleTop(out Vector3 position)
        {
            return TryGetRandomSurfaceTop(obstacleTag, targetOnObstacleYOffset, out position);
        }

        private bool TryGetRandomSurfaceTop(string tag, float heightOffset, out Vector3 position)
        {
            position = Vector3.zero;
            if (string.IsNullOrEmpty(tag)) return false;
            GameObject[] taggedObjects = GameObject.FindGameObjectsWithTag(tag);
            if (taggedObjects == null || taggedObjects.Length == 0) return false;
            List<Collider> colliders = new List<Collider>(taggedObjects.Length);
            foreach (GameObject taggedObject in taggedObjects)
            {
                if (taggedObject.TryGetComponent(out Collider collider))
                {
                    colliders.Add(collider);
                }
            }
            if (colliders.Count == 0) return false;
            Collider chosen = colliders[Random.Range(0, colliders.Count)];
            Bounds bounds = chosen.bounds;
            float x = Random.Range(bounds.min.x, bounds.max.x);
            float z = Random.Range(bounds.min.z, bounds.max.z);
            position = new Vector3(x, bounds.max.y + heightOffset, z);
            return true;
        }
    }
}
