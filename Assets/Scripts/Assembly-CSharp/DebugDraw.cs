using UnityEngine;

public static class DebugDraw
{
	public static void DrawMarker(Vector3 position, float size, Color color, float duration, bool depthTest = true)
	{
		Vector3 start = position + Vector3.up * size * 0.5f;
		Vector3 end = position - Vector3.up * size * 0.5f;
		Vector3 start2 = position + Vector3.right * size * 0.5f;
		Vector3 end2 = position - Vector3.right * size * 0.5f;
		Vector3 start3 = position + Vector3.forward * size * 0.5f;
		Vector3 end3 = position - Vector3.forward * size * 0.5f;
		Debug.DrawLine(start, end, color, duration, depthTest);
		Debug.DrawLine(start2, end2, color, duration, depthTest);
		Debug.DrawLine(start3, end3, color, duration, depthTest);
	}

	public static void DrawPlane(Vector3 position, Vector3 normal, float size, Color color, float duration, bool depthTest = true)
	{
		Vector3 vector = ((!(normal.normalized != Vector3.forward)) ? (Vector3.Cross(normal, Vector3.up).normalized * normal.magnitude) : (Vector3.Cross(normal, Vector3.forward).normalized * normal.magnitude));
		Vector3 vector2 = position + vector * size;
		Vector3 vector3 = position - vector * size;
		vector = Quaternion.AngleAxis(90f, normal) * vector;
		Vector3 vector4 = position + vector * size;
		Vector3 vector5 = position - vector * size;
		Debug.DrawLine(vector2, vector3, color, duration, depthTest);
		Debug.DrawLine(vector4, vector5, color, duration, depthTest);
		Debug.DrawLine(vector2, vector4, color, duration, depthTest);
		Debug.DrawLine(vector4, vector3, color, duration, depthTest);
		Debug.DrawLine(vector3, vector5, color, duration, depthTest);
		Debug.DrawLine(vector5, vector2, color, duration, depthTest);
		Debug.DrawRay(position, normal * size, color, duration, depthTest);
	}

	public static void DrawVector(Vector3 position, Vector3 direction, float raySize, float markerSize, Color color, float duration, bool depthTest = true)
	{
		Debug.DrawRay(position, direction * raySize, color, 0f, depthTest: false);
		DrawMarker(position + direction * raySize, markerSize, color, 0f, depthTest: false);
	}

	public static void DrawTriangle(Vector3 a, Vector3 b, Vector3 c, Color color)
	{
		Debug.DrawLine(a, b, color);
		Debug.DrawLine(b, c, color);
		Debug.DrawLine(c, a, color);
	}

	public static void DrawTriangle(Vector3 a, Vector3 b, Vector3 c, Color color, Transform t)
	{
		a = t.TransformPoint(a);
		b = t.TransformPoint(b);
		c = t.TransformPoint(c);
		Debug.DrawLine(a, b, color);
		Debug.DrawLine(b, c, color);
		Debug.DrawLine(c, a, color);
	}

	public static void DrawMesh(Mesh mesh, Color color, Transform t)
	{
		for (int i = 0; i < mesh.triangles.Length; i += 3)
		{
			DrawTriangle(mesh.vertices[mesh.triangles[i]], mesh.vertices[mesh.triangles[i + 1]], mesh.vertices[mesh.triangles[i + 2]], color, t);
		}
	}

	public static Color RandomColor()
	{
		return new Color(Random.value, Random.value, Random.value);
	}
}
