using System.Collections.Generic;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Static utility that walks the physical joint graph of an assembly
    /// using Breadth-First Search. Handles Unity's one-way FixedJoint
    /// references by building a bidirectional adjacency map first.
    ///
    /// CONTRACT FOR THE ASSEMBLY SYSTEM:
    /// - Every validated piece must have a StructuralPiece component.
    /// - Pieces are connected to each other (and ultimately to the root)
    ///   via FixedJoints. Joints may be one-way or bidirectional.
    /// - Decoration pieces (IDecoration) are ignored entirely.
    /// </summary>
    public static class AssemblyGraphUtility
    {
        /// <summary>
        /// Starting from the root Rigidbody, discovers every StructuralPiece
        /// in the connected joint graph, regardless of chain depth.
        /// The root itself is excluded from the result.
        /// </summary>
        /// <param name="root">The root Rigidbody (e.g., chair seat) that anchors the assembly.</param>
        /// <returns>A flat list of all StructuralPiece components connected to the root.</returns>
        public static List<StructuralPiece> GetAllConnectedPieces(Rigidbody root)
        {
            var result = new List<StructuralPiece>();
            if (root == null) return result;

            // ---------------------------------------------------------------
            // STEP 1: BUILD BIDIRECTIONAL ADJACENCY MAP
            // ---------------------------------------------------------------
            // Unity FixedJoints are one-directional: the joint sits on object A
            // and its connectedBody points to object B. But there is no built-in
            // way to ask object B "who is jointed to you?"
            //
            // To handle BOTH directions (and support future bidirectional joints
            // where A→B and B→A both exist), we scan all FixedJoints in the scene
            // and register edges in BOTH directions.
            //
            // Performance note: This runs ONLY on validation (button press),
            // never per-frame. FindObjectsByType is the modern Unity API.
            // ---------------------------------------------------------------
            var adjacency = new Dictionary<Rigidbody, HashSet<Rigidbody>>();

            FixedJoint[] allJoints = Object.FindObjectsByType<FixedJoint>(FindObjectsSortMode.None);

            foreach (FixedJoint joint in allJoints)
            {
                Rigidbody ownerBody = joint.GetComponent<Rigidbody>();
                Rigidbody connectedBody = joint.connectedBody;

                // Skip broken or incomplete joints
                if (ownerBody == null || connectedBody == null) continue;

                // Register edge in BOTH directions so BFS can traverse
                // regardless of which object "owns" the FixedJoint component.
                AddEdge(adjacency, ownerBody, connectedBody);
                AddEdge(adjacency, connectedBody, ownerBody);
            }

            // ---------------------------------------------------------------
            // STEP 2: BREADTH-FIRST SEARCH FROM ROOT
            // ---------------------------------------------------------------
            // Starting from the root, we walk the adjacency map layer by layer.
            // - The root is marked visited immediately so it's never collected.
            // - For each neighbor, we check if it has StructuralPiece (collect it)
            //   or IDecoration (skip it entirely — don't traverse through it).
            // - The visited set prevents infinite loops from bidirectional joints.
            // ---------------------------------------------------------------
            var visited = new HashSet<Rigidbody> { root };
            var queue = new Queue<Rigidbody>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                Rigidbody current = queue.Dequeue();

                // If this node has no outgoing edges, skip
                if (!adjacency.TryGetValue(current, out HashSet<Rigidbody> neighbors))
                    continue;

                foreach (Rigidbody neighbor in neighbors)
                {
                    // Already visited — skip (prevents infinite loops)
                    if (visited.Contains(neighbor)) continue;
                    visited.Add(neighbor);

                    // Skip decoration pieces — they are cosmetic, not structural
                    if (neighbor.GetComponent<IDecoration>() != null) continue;

                    // If this neighbor is a structural piece, collect it
                    StructuralPiece piece = neighbor.GetComponent<StructuralPiece>();
                    if (piece != null)
                    {
                        result.Add(piece);
                    }

                    // Continue traversal through this body regardless of whether
                    // it had StructuralPiece — there may be more pieces beyond it.
                    queue.Enqueue(neighbor);
                }
            }

            return result;
        }

        /// <summary>
        /// Adds a directed edge to the adjacency map. Called twice per joint
        /// (once per direction) to make the map bidirectional.
        /// </summary>
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
