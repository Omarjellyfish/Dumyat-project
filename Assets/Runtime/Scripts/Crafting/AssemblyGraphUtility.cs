using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    public static class AssemblyGraphUtility
    {
        public static List<StructuralPiece> GetAllConnectedPieces(Rigidbody root)
        {
            var result = new List<StructuralPiece>();
            if (root == null) return result;

            var adjacency = new Dictionary<Rigidbody, HashSet<Rigidbody>>();
            FixedJoint[] allJoints = Object.FindObjectsByType<FixedJoint>(FindObjectsSortMode.None);

            foreach (FixedJoint joint in allJoints)
            {
                Rigidbody ownerBody = joint.GetComponent<Rigidbody>();
                Rigidbody connectedBody = joint.connectedBody;

                if (ownerBody == null || connectedBody == null) continue;

                AddEdge(adjacency, ownerBody, connectedBody);
                AddEdge(adjacency, connectedBody, ownerBody);
            }

            var visited = new HashSet<Rigidbody> { root };
            var queue = new Queue<Rigidbody>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                Rigidbody current = queue.Dequeue();

                if (!adjacency.TryGetValue(current, out HashSet<Rigidbody> neighbors))
                    continue;

                foreach (Rigidbody neighbor in neighbors)
                {
                    if (visited.Contains(neighbor)) continue;
                    visited.Add(neighbor);

                    if (neighbor.GetComponent<IDecoration>() != null) continue;

                    StructuralPiece piece = neighbor.GetComponent<StructuralPiece>();
                    if (piece != null)
                    {
                        result.Add(piece);
                    }

                    queue.Enqueue(neighbor);
                }
            }

            return result;
        }

        private static void AddEdge(
            Dictionary<Rigidbody, HashSet<Rigidbody>> adjacency,
            Rigidbody from,
            Rigidbody to)
        {
            if (!adjacency.TryGetValue(from, out HashSet<Rigidbody> neighbors))
            {
                neighbors = new HashSet<Rigidbody>();
                adjacency[from] = neighbors;
            }
            neighbors.Add(to);
        }
    }
}
