using System.Collections.Generic;
using UnityEngine;

namespace SpacePartition
{

	// A Dynamic, Loose 2D Quadtree for storing any objects that can be described with AABB rectangle
	// Quadtree: An quadtree is a tree data structure which divides 2D space into smaller partitions
	//		(nodes) and places objects into the appropriate nodes. This allows fast access to objects
	//		in an area of interest without having to check every object.
	// Dynamic: The quadtree grows or shrinks as required when objects as added or removed
	//		It also splits and merges nodes as appropriate. There is no maximum depth.
	//		Nodes have a constant - numObjectsAllowed - which sets the amount of items allowed in a node
	//		before it splits.
	// Loose:	The quadtree's nodes can be larger than 1/2 their parent's length and width, so they overlap
	//		to some extent.
	//		This can alleviate the problem of even tiny objects ending up in large nodes if they're near
	//		boundaries.
	//		A looseness value of 1.0 will make it a "normal" octree.
	// T:	The content of the quadtree can be anything, since the bounds data is supplied separately.
	public class RectQuadtree<T>
	{
		#region variables
		// Root node of the octree
		private RectQuadtreeNode<T> rootNode;

		// Should be a value between 1 and 2. A multiplier for the base size of a node.
		// 1.0 is a "normal" octree, while values > 1 have overlap
		private readonly float looseness;

		// Size that the octree was on creation
		private readonly float initialSize;

		// Minimum side length that a node can be - essentially an alternative to having a max depth
		private readonly float minSize;
		#endregion

		// For collision visualisation. Automatically removed in builds.
#if UNITY_EDITOR
		private const int _NumCollisionsToSave = 4;
		private readonly Queue<Rect> lastRectCollisionChecks = new Queue<Rect>();
		private readonly Queue<Ray> lastRayCollisionChecks = new Queue<Ray>();
#endif

		#region properties
		// The total amount of objects currently in the tree
		public int Count { get; private set; }
		#endregion

		/// <summary>
		/// Constructor for the bounds octree.
		/// </summary>
		/// <param name="initialWorldSize">
		/// Size of the sides of the initial node, in metres. The octree will 
		/// never shrink smaller than this.
		/// </param>
		/// <param name="initialWorldPos">
		/// Position of the centre of the initial node.
		/// </param>
		/// <param name="minNodeSize">
		/// Nodes will stop splitting if the new nodes would be smaller than this 
		/// (metres).
		/// </param>
		/// <param name="loosenessVal">
		/// Clamped between 1 and 2. Values > 1 let nodes overlap.
		/// </param>
		public RectQuadtree(float initialWorldSize, Vector2 initialWorldPos, float minNodeSize,
			float loosenessVal)
		{
			if (minNodeSize > initialWorldSize)
			{
				Debug.LogWarning("Minimum node size must be at least as big as the initial world size. " +
					"Was: " + minNodeSize + " Adjusted to: " + initialWorldSize);
				minNodeSize = initialWorldSize;
			}

			Count = 0;
			initialSize = initialWorldSize;
			minSize = minNodeSize;
			looseness = Mathf.Clamp(loosenessVal, 1.0f, 2.0f);
			rootNode = new RectQuadtreeNode<T>(initialSize, minSize, looseness, initialWorldPos);
		}

		/// <summary>
		/// Add an object.
		/// </summary>
		/// <param name="obj">Object to add.</param>
		/// <param name="rect">3D bounding box around the object.</param>
		public void Add(T obj, Rect rect)
		{
			// Add object or expand the octree until it can be added
			int count = 0; // Safety check against infinite/excessive growth
			while (!rootNode.Add(obj, rect))
			{
				Grow(rect.center - rootNode.Center);
				if (++count > 20)
				{
					Debug.LogError("Aborted Add operation as it seemed to be going on forever (" +
						(count - 1) + ") attempts at growing the octree.");
					return;
				}
			}
			Count++;
		}

		/// <summary>
		/// Remove an object. Makes the assumption that the object only exists once in the tree.
		/// </summary>
		/// <param name="obj">Object to remove.</param>
		/// <returns>True if the object was removed successfully.</returns>
		public bool Remove(T obj)
		{
			bool removed = rootNode.Remove(obj);

			// See if we can shrink the octree down now that we've removed the item
			if (removed)
			{
				Count--;
				Shrink();
			}

			return removed;
		}

		/// <summary>
		/// Removes the specified object at the given position. Makes the assumption that the object only 
		/// exists once in the tree.
		/// </summary>
		/// <param name="obj">Object to remove.</param>
		/// <param name="rect">3D bounding box around the object.</param>
		/// <returns>True if the object was removed successfully.</returns>
		public bool Remove(T obj, Rect rect)
		{
			bool removed = rootNode.Remove(obj, rect);

			// See if we can shrink the octree down now that we've removed the item
			if (removed)
			{
				Count--;
				Shrink();
			}

			return removed;
		}

		/// <summary>
		/// Check if the specified bounds intersect with anything in the tree. See also: GetColliding.
		/// </summary>
		/// <param name="checkBounds">bounds to check.</param>
		/// <returns>True if there was a collision.</returns>
		public bool IsColliding(Rect checkBounds)
		{
			//#if UNITY_EDITOR
			// For debugging
			//AddCollisionCheck(checkBounds);
			//#endif
			return rootNode.IsColliding(ref checkBounds);
		}

		/// <summary>
		/// Returns an array of objects that intersect with the specified bounds, if any. Otherwise returns 
		/// an empty array. See also: IsColliding.
		/// </summary>
		/// <param name="collidingWith">list to store intersections.</param>
		/// <param name="checkRect">bounds to check.</param>
		/// <returns>Objects that intersect with the specified bounds.</returns>
		public void GetColliding(List<T> collidingWith, Rect checkRect)
		{
			//#if UNITY_EDITOR
			// For debugging
			//AddCollisionCheck(checkBounds);
			//#endif
			rootNode.GetColliding(ref checkRect, collidingWith);
		}

		public List<T> GetWithinFrustum(Camera cam)
		{
			var planes = GeometryUtility.CalculateFrustumPlanes(cam);

			var list = new List<T>();
			rootNode.GetWithinFrustum(planes, list);
			return list;
		}

		public Rect GetMaxRect()
		{
			return rootNode.GetRect();
		}

		/// <summary>
		/// Draws node boundaries visually for debugging.
		/// Must be called from OnDrawGizmos externally. See also: DrawAllObjects.
		/// </summary>
		public void DrawAllBounds()
		{
			rootNode.DrawAllBounds();
		}

		/// <summary>
		/// Draws the bounds of all objects in the tree visually for debugging.
		/// Must be called from OnDrawGizmos externally. See also: DrawAllBounds.
		/// </summary>
		public void DrawAllObjects()
		{
			rootNode.DrawAllObjects();
		}

		// Intended for debugging. Must be called from OnDrawGizmos externally
		// See also DrawAllBounds and DrawAllObjects
		/// <summary>
		/// Visualises collision checks from IsColliding and GetColliding.
		/// Collision visualisation code is automatically removed from builds so that collision checks 
		/// aren't slowed down.
		/// </summary>
#if UNITY_EDITOR
		public void DrawCollisionChecks()
		{
			int count = 0;
			foreach (Rect collisionCheck in lastRectCollisionChecks)
			{
				Gizmos.color = new Color(1.0f, 1.0f - ((float)count / _NumCollisionsToSave), 1.0f);
				Gizmos.DrawCube(collisionCheck.center, collisionCheck.size);
				count++;
			}

			foreach (Ray collisionCheck in lastRayCollisionChecks)
			{
				Gizmos.color = new Color(1.0f, 1.0f - ((float)count / _NumCollisionsToSave), 1.0f);
				Gizmos.DrawRay(collisionCheck.origin, collisionCheck.direction);
				count++;
			}
			Gizmos.color = Color.white;
		}
#endif

		/// <summary>
		/// Used for visualising collision checks with DrawCollisionChecks.
		/// Automatically removed from builds so that collision checks aren't slowed down.
		/// </summary>
		/// <param name="checkRect">bounds that were passed in to check for collisions.</param>
#if UNITY_EDITOR
		void AddCollisionCheck(Rect checkRect)
		{
			lastRectCollisionChecks.Enqueue(checkRect);
			if (lastRectCollisionChecks.Count > _NumCollisionsToSave)
			{
				lastRectCollisionChecks.Dequeue();
			}
		}
#endif

		/// <summary>
		/// Used for visualising collision checks with DrawCollisionChecks.
		/// Automatically removed from builds so that collision checks aren't slowed down.
		/// </summary>
		/// <param name="checkRay">ray that was passed in to check for collisions.</param>
#if UNITY_EDITOR
		void AddCollisionCheck(Ray checkRay)
		{
			lastRayCollisionChecks.Enqueue(checkRay);
			if (lastRayCollisionChecks.Count > _NumCollisionsToSave)
			{
				lastRayCollisionChecks.Dequeue();
			}
		}
#endif

		/// <summary>
		/// Grow the octree to fit in all objects.
		/// </summary>
		/// <param name="direction">Direction to grow.</param>
		void Grow(Vector3 direction)
		{
			RectQuadtreeNode<T> oldRoot = rootNode;
			float half = rootNode.BaseLength / 2;
			float newLength = rootNode.BaseLength * 2;
			Vector2 newCenter = rootNode.Center;
			int horizDir, vertDir;
			horizDir = direction.x >= 0 ? 1 : -1;
			vertDir = direction.y >= 0 ? 1 : -1;
			newCenter.x += horizDir * half;
			newCenter.y += vertDir * half;

			// Create a new, bigger octree root node
			rootNode = new RectQuadtreeNode<T>(newLength, minSize, looseness, newCenter);

			if (oldRoot.HasAnyObjects())
			{
				// Create another 3 new octree children to go with the old root as children of the new root
				int rootPos = rootNode.BestFitChild(oldRoot.Center);
				RectQuadtreeNode<T>[] children = new RectQuadtreeNode<T>[4];
				for (int i = 0; i < 4; i++)
				{
					if (i == rootPos)
					{
						children[i] = oldRoot;
					}
					else
					{
						horizDir = i % 2 == 0 ? -1 : 1;
						vertDir = i > 1 ? -1 : 1;
						Vector2 newSubCenter = newCenter;
						newCenter.x += horizDir * half;
						newCenter.y += vertDir * half;
                        children[i] = new RectQuadtreeNode<T>(oldRoot.BaseLength, minSize, looseness,
							newSubCenter);
					}
				}

				// Attach the new children to the new root node
				rootNode.SetChildren(children);
			}
		}

		/// <summary>
		/// Shrink the octree if possible, else leave it the same.
		/// </summary>
		void Shrink()
		{
			rootNode = rootNode.ShrinkIfPossible(initialSize);
		}
	}
}


