using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshCollider))]
public class BSPTree : MonoBehaviour
{
	public class Node
	{
		public Vector3 partitionPoint;

		public Vector3 partitionNormal;

		public Node positiveChild;

		public Node negativeChild;

		public int[] triangles;
	}

	[SerializeField]
	private bool drawMeshTreeOnStart;

	private int triangleCount;

	private int vertexCount;

	private Vector3[] vertices;

	private int[] tris;

	private Vector3[] triangleNormals;

	private Mesh mesh;

	private Node tree;

	private void Awake()
	{
		mesh = GetComponent<MeshCollider>().sharedMesh;
		tris = mesh.triangles;
		vertices = mesh.vertices;
		vertexCount = mesh.vertices.Length;
		triangleCount = mesh.triangles.Length / 3;
		triangleNormals = new Vector3[triangleCount];
		for (int i = 0; i < tris.Length; i += 3)
		{
			Vector3 normalized = Vector3.Cross((vertices[tris[i + 1]] - vertices[tris[i]]).normalized, (vertices[tris[i + 2]] - vertices[tris[i]]).normalized).normalized;
			triangleNormals[i / 3] = normalized;
		}
		if (!drawMeshTreeOnStart)
		{
			BuildTriangleTree();
		}
	}

	private void Start()
	{
		if (drawMeshTreeOnStart)
		{
			BuildTriangleTree();
		}
	}

	public Vector3 ClosestPointOn(Vector3 to, float radius)
	{
		to = base.transform.InverseTransformPoint(to);
		List<int> list = new List<int>();
		FindClosestTriangles(tree, to, radius, list);
		Vector3 position = ClosestPointOnTriangle(list.ToArray(), to);
		return base.transform.TransformPoint(position);
	}

	private void FindClosestTriangles(Node node, Vector3 to, float radius, List<int> triangles)
	{
		if (node.triangles == null)
		{
			if (PointDistanceFromPlane(node.partitionPoint, node.partitionNormal, to) <= radius)
			{
				FindClosestTriangles(node.positiveChild, to, radius, triangles);
				FindClosestTriangles(node.negativeChild, to, radius, triangles);
			}
			else if (PointAbovePlane(node.partitionPoint, node.partitionNormal, to))
			{
				FindClosestTriangles(node.positiveChild, to, radius, triangles);
			}
			else
			{
				FindClosestTriangles(node.negativeChild, to, radius, triangles);
			}
		}
		else
		{
			triangles.AddRange(node.triangles);
		}
	}

	private Vector3 ClosestPointOnTriangle(int[] triangles, Vector3 to)
	{
		float num = float.MaxValue;
		Vector3 result = Vector3.zero;
		foreach (int num2 in triangles)
		{
			Vector3 vertex = vertices[tris[num2]];
			Vector3 vertex2 = vertices[tris[num2 + 1]];
			Vector3 vertex3 = vertices[tris[num2 + 2]];
			ClosestPointOnTriangleToPoint(ref vertex, ref vertex2, ref vertex3, ref to, out var result2);
			float sqrMagnitude = (to - result2).sqrMagnitude;
			if (sqrMagnitude <= num)
			{
				num = sqrMagnitude;
				result = result2;
			}
		}
		return result;
	}

	private void BuildTriangleTree()
	{
		List<int> list = new List<int>();
		for (int i = 0; i < tris.Length; i += 3)
		{
			list.Add(i);
		}
		tree = new Node();
		RecursivePartition(list, 0, tree);
	}

	private void RecursivePartition(List<int> triangles, int depth, Node parent)
	{
		Vector3 zero = Vector3.zero;
		Vector3 vector = new Vector3(float.MinValue, float.MinValue, float.MinValue);
		Vector3 vector2 = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
		foreach (int triangle in triangles)
		{
			zero += vertices[tris[triangle]] + vertices[tris[triangle + 1]] + vertices[tris[triangle + 2]];
			vector2.x = Mathf.Min(vector2.x, vertices[tris[triangle]].x, vertices[tris[triangle + 1]].x, vertices[tris[triangle + 2]].x);
			vector2.y = Mathf.Min(vector2.y, vertices[tris[triangle]].y, vertices[tris[triangle + 1]].y, vertices[tris[triangle + 2]].y);
			vector2.z = Mathf.Min(vector2.z, vertices[tris[triangle]].z, vertices[tris[triangle + 1]].z, vertices[tris[triangle + 2]].z);
			vector.x = Mathf.Max(vector.x, vertices[tris[triangle]].x, vertices[tris[triangle + 1]].x, vertices[tris[triangle + 2]].x);
			vector.y = Mathf.Max(vector.y, vertices[tris[triangle]].y, vertices[tris[triangle + 1]].y, vertices[tris[triangle + 2]].y);
			vector.z = Mathf.Max(vector.z, vertices[tris[triangle]].z, vertices[tris[triangle + 1]].z, vertices[tris[triangle + 2]].z);
		}
		zero /= (float)vertexCount;
		zero = vector2 + Math3d.SetVectorLength(vector - vector2, (vector - vector2).magnitude * 0.5f);
		Vector3 vector3 = new Vector3(Mathf.Abs(vector.x - vector2.x), Mathf.Abs(vector.y - vector2.y), Mathf.Abs(vector.z - vector2.z));
		Vector3 partitionNormal = ((vector3.x >= vector3.y && vector3.x >= vector3.z) ? Vector3.right : ((!(vector3.y >= vector3.x) || !(vector3.y >= vector3.z)) ? Vector3.forward : Vector3.up));
		Split(triangles, zero, partitionNormal, out var positiveTriangles, out var negativeTriangles);
		parent.partitionNormal = partitionNormal;
		parent.partitionPoint = zero;
		Node node = (parent.positiveChild = new Node());
		Node node2 = (parent.negativeChild = new Node());
		if (positiveTriangles.Count < triangles.Count && positiveTriangles.Count > 3)
		{
			RecursivePartition(positiveTriangles, depth + 1, node);
		}
		else
		{
			node.triangles = positiveTriangles.ToArray();
			if (drawMeshTreeOnStart)
			{
				DrawTriangleSet(node.triangles, DebugDraw.RandomColor());
			}
		}
		if (negativeTriangles.Count < triangles.Count && negativeTriangles.Count > 3)
		{
			RecursivePartition(negativeTriangles, depth + 1, node2);
			return;
		}
		node2.triangles = negativeTriangles.ToArray();
		if (drawMeshTreeOnStart)
		{
			DrawTriangleSet(node2.triangles, DebugDraw.RandomColor());
		}
	}

	private void Split(List<int> triangles, Vector3 partitionPoint, Vector3 partitionNormal, out List<int> positiveTriangles, out List<int> negativeTriangles)
	{
		positiveTriangles = new List<int>();
		negativeTriangles = new List<int>();
		foreach (int triangle in triangles)
		{
			bool flag = PointAbovePlane(partitionPoint, partitionNormal, vertices[tris[triangle]]);
			bool flag2 = PointAbovePlane(partitionPoint, partitionNormal, vertices[tris[triangle + 1]]);
			bool flag3 = PointAbovePlane(partitionPoint, partitionNormal, vertices[tris[triangle + 2]]);
			if (flag & flag2 & flag3)
			{
				positiveTriangles.Add(triangle);
				continue;
			}
			if (!flag && !flag2 && !flag3)
			{
				negativeTriangles.Add(triangle);
				continue;
			}
			positiveTriangles.Add(triangle);
			negativeTriangles.Add(triangle);
		}
	}

	private bool PointAbovePlane(Vector3 planeOrigin, Vector3 planeNormal, Vector3 point)
	{
		return Vector3.Dot(point - planeOrigin, planeNormal) >= 0f;
	}

	private float PointDistanceFromPlane(Vector3 planeOrigin, Vector3 planeNormal, Vector3 point)
	{
		return Mathf.Abs(Vector3.Dot(point - planeOrigin, planeNormal));
	}

	public static void ClosestPointOnTriangleToPoint(ref Vector3 vertex1, ref Vector3 vertex2, ref Vector3 vertex3, ref Vector3 point, out Vector3 result)
	{
		Vector3 vector = vertex2 - vertex1;
		Vector3 vector2 = vertex3 - vertex1;
		Vector3 rhs = point - vertex1;
		float num = Vector3.Dot(vector, rhs);
		float num2 = Vector3.Dot(vector2, rhs);
		if (num <= 0f && num2 <= 0f)
		{
			result = vertex1;
			return;
		}
		Vector3 rhs2 = point - vertex2;
		float num3 = Vector3.Dot(vector, rhs2);
		float num4 = Vector3.Dot(vector2, rhs2);
		if (num3 >= 0f && num4 <= num3)
		{
			result = vertex2;
			return;
		}
		float num5 = num * num4 - num3 * num2;
		if (num5 <= 0f && num >= 0f && num3 <= 0f)
		{
			float num6 = num / (num - num3);
			result = vertex1 + num6 * vector;
			return;
		}
		Vector3 rhs3 = point - vertex3;
		float num7 = Vector3.Dot(vector, rhs3);
		float num8 = Vector3.Dot(vector2, rhs3);
		if (num8 >= 0f && num7 <= num8)
		{
			result = vertex3;
			return;
		}
		float num9 = num7 * num2 - num * num8;
		if (num9 <= 0f && num2 >= 0f && num8 <= 0f)
		{
			float num10 = num2 / (num2 - num8);
			result = vertex1 + num10 * vector2;
			return;
		}
		float num11 = num3 * num8 - num7 * num4;
		if (num11 <= 0f && num4 - num3 >= 0f && num7 - num8 >= 0f)
		{
			float num12 = (num4 - num3) / (num4 - num3 + (num7 - num8));
			result = vertex2 + num12 * (vertex3 - vertex2);
			return;
		}
		float num13 = 1f / (num11 + num9 + num5);
		float num14 = num9 * num13;
		float num15 = num5 * num13;
		result = vertex1 + vector * num14 + vector2 * num15;
	}

	private void DrawTriangleSet(int[] triangles, Color color)
	{
		foreach (int num in triangles)
		{
			DebugDraw.DrawTriangle(vertices[tris[num]], vertices[tris[num + 1]], vertices[tris[num + 2]], color, base.transform);
		}
	}
}
