using System.Collections.Generic;
using UnityEngine;

namespace MLReachTargetObstacleAgent.Scripts
{
    /// <summary>
    /// FIFO toggler for structures and obstacles so curriculum code can progressively reveal geometry.
    /// </summary>
    public class EnvironmentManager : MonoBehaviour
    {
        [Header("Structure Elements")]
        [SerializeField]
        private List<GameObject> structureElements = new List<GameObject>();

        [Header("Obstacle Elements")]
        [SerializeField]
        private List<GameObject> obstacleElements = new List<GameObject>();

        private readonly Queue<GameObject> structureQueue = new Queue<GameObject>();
        private readonly Queue<GameObject> obstacleQueue = new Queue<GameObject>();

        private void Awake()
        {
            ResetOrder();
        }

        public void AddStructure(GameObject element)
        {
            structureElements.Add(element);
            ResetOrder();
        }

        public void RemoveStructure(GameObject element)
        {
            structureElements.Remove(element);
            ResetOrder();
        }

        public void AddObstacle(GameObject element)
        {
            obstacleElements.Add(element);
            ResetOrder();
        }

        public void RemoveObstacle(GameObject element)
        {
            obstacleElements.Remove(element);
            ResetOrder();
        }

        public void EnableNextStructure()
        {
            GameObject next = GetNextStructure();
            if (next != null)
                next.SetActive(true);
        }

        public void DisableNextStructure()
        {
            GameObject next = GetNextStructure();
            if (next != null)
                next.SetActive(false);
        }

        public void EnableNextObstacle()
        {
            GameObject next = GetNextObstacle();
            if (next != null)
                next.SetActive(true);
        }

        public void DisableNextObstacle()
        {
            GameObject next = GetNextObstacle();
            if (next != null)
                next.SetActive(false);
        }

        public void EnableAllStructure()
        {
            foreach (var element in structureElements)
                element.SetActive(true);
        }

        public void DisableAllStructure()
        {
            foreach (var element in structureElements)
                element.SetActive(false);
        }

        public void EnableAllObstacles()
        {
            foreach (var element in obstacleElements)
                element.SetActive(true);
        }

        public void DisableAllObstacles()
        {
            foreach (var element in obstacleElements)
                element.SetActive(false);
        }

        private void ResetOrder()
        {
            structureQueue.Clear();
            obstacleQueue.Clear();
            foreach (var element in structureElements)
            {
                structureQueue.Enqueue(element);
            }
            foreach (var element in obstacleElements)
            {
                obstacleQueue.Enqueue(element);
            }
        }

        private GameObject GetNextStructure()
        {
            return structureQueue.Count > 0 ? structureQueue.Dequeue() : null;
        }

        private GameObject GetNextObstacle()
        {
            return obstacleQueue.Count > 0 ? obstacleQueue.Dequeue() : null;
        }
    }
}
