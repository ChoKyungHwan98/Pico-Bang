using UnityEngine;

public static class SuperCollider
{
	public static bool ClosestPointOnSurface(Collider collider, Vector3 to, float radius, out Vector3 closestPointOnSurface)
	{
		if (collider is BoxCollider)
		{
			closestPointOnSurface = ClosestPointOnSurface((BoxCollider)collider, to);
			return true;
		}
		if (collider is SphereCollider)
		{
			closestPointOnSurface = ClosestPointOnSurface((SphereCollider)collider, to);
			return true;
		}
		if (collider is CapsuleCollider)
		{
			closestPointOnSurface = ClosestPointOnSurface((CapsuleCollider)collider, to);
			return true;
		}
		if (collider is TerrainCollider)
		{
			closestPointOnSurface = ClosestPointOnSurface((TerrainCollider)collider, to, radius);
			return true;
		}
		if (collider is MeshCollider)
		{
			BSPTree component = collider.GetComponent<BSPTree>();
			if (component != null)
			{
				closestPointOnSurface = component.ClosestPointOn(to, radius);
				return true;
			}
			Debug.LogError($"'{collider.gameObject.name}' is missing BSPTree script.");
			closestPointOnSurface = Vector3.zero;
			return false;
		}
		Debug.LogError($"{collider.GetType()} does not have an implementation for ClosestPointOnSurface; GameObject.Name='{collider.gameObject.name}'");
		closestPointOnSurface = Vector3.zero;
		return false;
	}

	public static Vector3 ClosestPointOnSurface(SphereCollider collider, Vector3 to)
	{
		Vector3 vector = to - (collider.transform.position + collider.center);
		vector.Normalize();
		vector *= collider.radius * collider.transform.localScale.x;
		return vector + (collider.transform.position + collider.center);
	}

	public static Vector3 ClosestPointOnSurface(BoxCollider collider, Vector3 to)
	{
		Transform transform = collider.transform;
		Vector3 vector = transform.InverseTransformPoint(to);
		vector -= collider.center;
		Vector3 vector2 = collider.size * 0.5f;
		Vector3 position = new Vector3(Mathf.Clamp(vector.x, 0f - vector2.x, vector2.x), Mathf.Clamp(vector.y, 0f - vector2.y, vector2.y), Mathf.Clamp(vector.z, 0f - vector2.z, vector2.z));
		float num = Mathf.Min(Mathf.Abs(vector2.x - position.x), Mathf.Abs(0f - vector2.x - position.x));
		float num2 = Mathf.Min(Mathf.Abs(vector2.y - position.y), Mathf.Abs(0f - vector2.y - position.y));
		float num3 = Mathf.Min(Mathf.Abs(vector2.z - position.z), Mathf.Abs(0f - vector2.z - position.z));
		if (num < num2 && num < num3)
		{
			position.x = Mathf.Sign(position.x) * vector2.x;
		}
		else if (num2 < num && num2 < num3)
		{
			position.y = Mathf.Sign(position.y) * vector2.y;
		}
		else if (num3 < num && num3 < num2)
		{
			position.z = Mathf.Sign(position.z) * vector2.z;
		}
		position += collider.center;
		return transform.TransformPoint(position);
	}

	public static Vector3 ClosestPointOnSurface(CapsuleCollider collider, Vector3 to)
	{
		Transform transform = collider.transform;
		float num = collider.height - collider.radius * 2f;
		Vector3 up = Vector3.up;
		Vector3 vector = up * num * 0.5f + collider.center;
		Vector3 vector2 = -up * num * 0.5f + collider.center;
		Vector3 vector3 = transform.InverseTransformPoint(to);
		Vector3 zero = Vector3.zero;
		Vector3 vector4 = Vector3.zero;
		if (vector3.y < num * 0.5f && vector3.y > (0f - num) * 0.5f)
		{
			vector4 = up * vector3.y + collider.center;
		}
		else if (vector3.y > num * 0.5f)
		{
			vector4 = vector;
		}
		else if (vector3.y < (0f - num) * 0.5f)
		{
			vector4 = vector2;
		}
		zero = vector3 - vector4;
		zero.Normalize();
		zero = zero * collider.radius + vector4;
		return transform.TransformPoint(zero);
	}

	public static Vector3 ClosestPointOnSurface(TerrainCollider collider, Vector3 to, float radius, bool debug = false)
	{
		TerrainData terrainData = collider.terrainData;
		Vector3 point = collider.transform.InverseTransformPoint(to);
		float num = terrainData.size.x / (float)(terrainData.heightmapResolution - 1);
		float num2 = terrainData.size.z / (float)(terrainData.heightmapResolution - 1);
		float num3 = Mathf.Clamp01(point.z / terrainData.size.z);
		float num4 = Mathf.Clamp01(point.x / terrainData.size.x) * (float)(terrainData.heightmapResolution - 1);
		float num5 = num3 * (float)(terrainData.heightmapResolution - 1);
		int num6 = Mathf.FloorToInt(num4);
		int num7 = Mathf.FloorToInt(num5);
		float num8 = (num4 - (float)num6) * num;
		float num9 = (num5 - (float)num7) * num2;
		float num10 = radius - num8;
		float num11 = radius - (num - num8);
		int num12 = ((num10 > 0f) ? (Mathf.FloorToInt(num10 / num) + 1) : 0);
		int num13 = ((num11 > 0f) ? (Mathf.FloorToInt(num11 / num) + 1) : 0);
		float num14 = radius - num9;
		float num15 = radius - (num2 - num9);
		int num16 = ((num14 > 0f) ? (Mathf.FloorToInt(num14 / num2) + 1) : 0);
		int num17 = ((num15 > 0f) ? (Mathf.FloorToInt(num15 / num2) + 1) : 0);
		int num18 = num6 - num12;
		int num19 = num7 - num16;
		int num20 = num13 + num12 + 1;
		int num21 = num17 + num16 + 1;
		if (num18 < 0)
		{
			num20 -= Mathf.Abs(num18);
			num18 = 0;
		}
		if (num19 < 0)
		{
			num21 -= Mathf.Abs(num19);
			num19 = 0;
		}
		if (num18 + num20 + 1 > terrainData.heightmapResolution)
		{
			num20 = terrainData.heightmapResolution - num18 - 1;
		}
		if (num19 + num21 + 1 > terrainData.heightmapResolution)
		{
			num21 = terrainData.heightmapResolution - num19 - 1;
		}
		float[,] heights = terrainData.GetHeights(num18, num19, num20 + 1, num21 + 1);
		for (int i = 0; i < num20 + 1; i++)
		{
			for (int j = 0; j < num21 + 1; j++)
			{
				heights[j, i] *= terrainData.size.y;
			}
		}
		float num22 = float.MaxValue;
		Vector3 position = Vector3.zero;
		for (int k = 0; k < num20; k++)
		{
			for (int l = 0; l < num21; l++)
			{
				Vector3 vertex = new Vector3((float)(num18 + k) * num, heights[l, k], (float)(num19 + l) * num2);
				Vector3 vertex2 = new Vector3((float)(num18 + k + 1) * num, heights[l, k + 1], (float)(num19 + l) * num2);
				Vector3 vertex3 = new Vector3((float)(num18 + k) * num, heights[l + 1, k], (float)(num19 + l + 1) * num2);
				Vector3 vertex4 = new Vector3((float)(num18 + k + 1) * num, heights[l + 1, k + 1], (float)(num19 + l + 1) * num2);
				BSPTree.ClosestPointOnTriangleToPoint(ref vertex, ref vertex4, ref vertex3, ref point, out var result);
				float sqrMagnitude = (point - result).sqrMagnitude;
				if (sqrMagnitude <= num22)
				{
					num22 = sqrMagnitude;
					position = result;
				}
				BSPTree.ClosestPointOnTriangleToPoint(ref vertex, ref vertex2, ref vertex4, ref point, out result);
				sqrMagnitude = (point - result).sqrMagnitude;
				if (sqrMagnitude <= num22)
				{
					num22 = sqrMagnitude;
					position = result;
				}
				if (debug)
				{
					DebugDraw.DrawTriangle(vertex, vertex4, vertex3, Color.cyan);
					DebugDraw.DrawTriangle(vertex, vertex2, vertex4, Color.red);
				}
			}
		}
		return collider.transform.TransformPoint(position);
	}
}
