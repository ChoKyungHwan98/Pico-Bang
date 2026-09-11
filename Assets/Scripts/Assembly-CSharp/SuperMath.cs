using UnityEngine;

public static class SuperMath
{
	public static Vector3 ClampAngleOnPlane(Vector3 origin, Vector3 direction, float angle, Vector3 planeNormal)
	{
		if (Vector3.Angle(origin, direction) < angle)
		{
			return direction;
		}
		return Quaternion.AngleAxis((float)((Vector3.Angle(Vector3.Cross(planeNormal, origin), direction) < 90f) ? 1 : (-1)) * angle, planeNormal) * origin;
	}

	public static float BoundedInterpolation(float[] bounds, float[] values, float t)
	{
		for (int i = 0; i < bounds.Length; i++)
		{
			if (t < bounds[i])
			{
				return values[i];
			}
		}
		return values[^1];
	}

	public static bool PointAbovePlane(Vector3 planeNormal, Vector3 planePoint, Vector3 point)
	{
		return Vector3.Angle(point - planePoint, planeNormal) < 90f;
	}

	public static bool Timer(float startTime, float duration)
	{
		return Time.time > startTime + duration;
	}

	public static float ClampAngle(float angle)
	{
		if (angle < -360f)
		{
			angle += 360f;
		}
		if (angle > 360f)
		{
			angle -= 360f;
		}
		return angle;
	}

	public static float CalculateJumpSpeed(float jumpHeight, float gravity)
	{
		return Mathf.Sqrt(2f * jumpHeight * gravity);
	}
}
