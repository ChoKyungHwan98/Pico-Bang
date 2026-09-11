using System;
using System.Collections.Generic;
using UnityEngine;

public class SuperCharacterController : MonoBehaviour
{
	protected struct IgnoredCollider
	{
		public Collider collider;

		public int layer;

		public IgnoredCollider(Collider collider, int layer)
		{
			this.collider = collider;
			this.layer = layer;
		}
	}

	public delegate void UpdateDelegate();

	public class SuperGround
	{
		private class GroundHit
		{
			public Vector3 point { get; private set; }

			public Vector3 normal { get; private set; }

			public float distance { get; private set; }

			public GroundHit(Vector3 point, Vector3 normal, float distance)
			{
				this.point = point;
				this.normal = normal;
				this.distance = distance;
			}
		}

		private LayerMask walkable;

		private SuperCharacterController controller;

		private readonly QueryTriggerInteraction triggerInteraction;

		private GroundHit primaryGround;

		private GroundHit nearGround;

		private GroundHit farGround;

		private GroundHit stepGround;

		private GroundHit flushGround;

		private const float groundingUpperBoundAngle = 60f;

		private const float groundingMaxPercentFromCenter = 0.85f;

		private const float groundingMinPercentFromcenter = 0.5f;

		public SuperCollisionType superCollisionType { get; private set; }

		public Transform transform { get; private set; }

		public SuperGround(LayerMask walkable, SuperCharacterController controller, QueryTriggerInteraction triggerInteraction)
		{
			this.walkable = walkable;
			this.controller = controller;
			this.triggerInteraction = triggerInteraction;
		}

		public void ProbeGround(Vector3 origin, int iter)
		{
			ResetGrounds();
			Vector3 up = controller.up;
			Vector3 vector = -up;
			Vector3 origin2 = origin + up * 0.1f;
			float radius = controller.radius - 0.010000001f;
			if (Physics.SphereCast(origin2, radius, vector, out var hitInfo, float.PositiveInfinity, walkable, triggerInteraction))
			{
				SuperCollisionType superCollisionType = hitInfo.collider.gameObject.GetComponent<SuperCollisionType>();
				if (superCollisionType == null)
				{
					superCollisionType = defaultCollisionType;
				}
				this.superCollisionType = superCollisionType;
				transform = hitInfo.transform;
				SimulateSphereCast(hitInfo.normal, out hitInfo);
				primaryGround = new GroundHit(hitInfo.point, hitInfo.normal, hitInfo.distance);
				if (Vector3.Distance(Math3d.ProjectPointOnPlane(controller.up, controller.transform.position, hitInfo.point), controller.transform.position) < 0.02f)
				{
					return;
				}
				Vector3 vector2 = Math3d.ProjectVectorOnPlane(up, (controller.transform.position - hitInfo.point).normalized * 0.02f);
				Vector3 vector3 = Quaternion.AngleAxis(-80f, Vector3.Cross(vector2, up)) * -vector2;
				Vector3 origin3 = hitInfo.point + vector2 + up * 0.02f;
				Vector3 origin4 = hitInfo.point + vector3 * 3f;
				Physics.Raycast(origin3, vector, out var hitInfo2, float.PositiveInfinity, walkable, triggerInteraction);
				Physics.Raycast(origin4, vector, out var hitInfo3, float.PositiveInfinity, walkable, triggerInteraction);
				nearGround = new GroundHit(hitInfo2.point, hitInfo2.normal, hitInfo2.distance);
				farGround = new GroundHit(hitInfo3.point, hitInfo3.normal, hitInfo3.distance);
				if (Vector3.Angle(hitInfo.normal, up) > superCollisionType.StandAngle)
				{
					Vector3 direction = Vector3.Cross(Vector3.Cross(hitInfo.normal, vector), hitInfo.normal);
					if (Physics.Raycast(hitInfo.point + hitInfo.normal * 0.02f, direction, out var hitInfo4, float.PositiveInfinity, walkable, triggerInteraction) && SimulateSphereCast(hitInfo4.normal, out var hit))
					{
						flushGround = new GroundHit(hit.point, hit.normal, hit.distance);
					}
				}
				if (!(Vector3.Angle(hitInfo2.normal, up) > superCollisionType.StandAngle) && !(hitInfo2.distance > 0.1f))
				{
					return;
				}
				SuperCollisionType superCollisionType2 = null;
				if (hitInfo2.collider != null)
				{
					superCollisionType2 = hitInfo2.collider.gameObject.GetComponent<SuperCollisionType>();
				}
				if (superCollisionType2 == null)
				{
					superCollisionType2 = defaultCollisionType;
				}
				if (Vector3.Angle(hitInfo2.normal, up) > superCollisionType2.StandAngle)
				{
					Vector3 direction2 = Vector3.Cross(Vector3.Cross(hitInfo2.normal, vector), hitInfo2.normal);
					if (Physics.Raycast(origin3, direction2, out var hitInfo5, float.PositiveInfinity, walkable, triggerInteraction))
					{
						stepGround = new GroundHit(hitInfo5.point, hitInfo5.normal, hitInfo5.distance);
					}
				}
				else
				{
					stepGround = new GroundHit(hitInfo2.point, hitInfo2.normal, hitInfo2.distance);
				}
			}
			else if (Physics.Raycast(origin2, vector, out hitInfo, float.PositiveInfinity, walkable, triggerInteraction))
			{
				SuperCollisionType superCollisionType3 = hitInfo.collider.gameObject.GetComponent<SuperCollisionType>();
				if (superCollisionType3 == null)
				{
					superCollisionType3 = defaultCollisionType;
				}
				this.superCollisionType = superCollisionType3;
				transform = hitInfo.transform;
				if (SimulateSphereCast(hitInfo.normal, out var hit2))
				{
					primaryGround = new GroundHit(hit2.point, hit2.normal, hit2.distance);
				}
				else
				{
					primaryGround = new GroundHit(hitInfo.point, hitInfo.normal, hitInfo.distance);
				}
			}
			else
			{
				Debug.LogWarning("[SuperCharacterController.cs]: WALKABLE LAYER NOT SET OR MISSING 'TempCast' LAYER.  SEE README FILE.");
			}
		}

		private void ResetGrounds()
		{
			primaryGround = null;
			nearGround = null;
			farGround = null;
			flushGround = null;
			stepGround = null;
		}

		public bool IsGrounded(bool currentlyGrounded, float distance)
		{
			Vector3 groundNormal;
			return IsGrounded(currentlyGrounded, distance, out groundNormal);
		}

		public bool IsGrounded(bool currentlyGrounded, float distance, out Vector3 groundNormal)
		{
			groundNormal = Vector3.zero;
			if (primaryGround == null || primaryGround.distance > distance)
			{
				return false;
			}
			if (farGround != null && Vector3.Angle(farGround.normal, controller.up) > superCollisionType.StandAngle)
			{
				if (flushGround != null && Vector3.Angle(flushGround.normal, controller.up) < superCollisionType.StandAngle && flushGround.distance < distance)
				{
					groundNormal = flushGround.normal;
					return true;
				}
				return false;
			}
			if (farGround != null && !OnSteadyGround(farGround.normal, primaryGround.point))
			{
				if (nearGround != null && nearGround.distance < distance && Vector3.Angle(nearGround.normal, controller.up) < superCollisionType.StandAngle && !OnSteadyGround(nearGround.normal, nearGround.point))
				{
					groundNormal = nearGround.normal;
					return true;
				}
				if (stepGround != null && stepGround.distance < distance && Vector3.Angle(stepGround.normal, controller.up) < superCollisionType.StandAngle)
				{
					groundNormal = stepGround.normal;
					return true;
				}
				return false;
			}
			if (farGround != null)
			{
				groundNormal = farGround.normal;
			}
			else
			{
				groundNormal = primaryGround.normal;
			}
			return true;
		}

		private bool OnSteadyGround(Vector3 normal, Vector3 point)
		{
			float t = Vector3.Angle(normal, controller.up) / 60f;
			float num = Mathf.Lerp(0.5f, 0.85f, t);
			return Vector3.Distance(Math3d.ProjectPointOnPlane(controller.up, controller.transform.position, point), controller.transform.position) <= num * controller.radius;
		}

		public Vector3 PrimaryNormal()
		{
			return primaryGround.normal;
		}

		public float Distance()
		{
			return primaryGround.distance;
		}

		public void DebugGround(bool primary, bool near, bool far, bool flush, bool step)
		{
			if (primary && primaryGround != null)
			{
				DebugDraw.DrawVector(primaryGround.point, primaryGround.normal, 2f, 1f, Color.yellow, 0f, depthTest: false);
			}
			if (near && nearGround != null)
			{
				DebugDraw.DrawVector(nearGround.point, nearGround.normal, 2f, 1f, Color.blue, 0f, depthTest: false);
			}
			if (far && farGround != null)
			{
				DebugDraw.DrawVector(farGround.point, farGround.normal, 2f, 1f, Color.red, 0f, depthTest: false);
			}
			if (flush && flushGround != null)
			{
				DebugDraw.DrawVector(flushGround.point, flushGround.normal, 2f, 1f, Color.cyan, 0f, depthTest: false);
			}
			if (step && stepGround != null)
			{
				DebugDraw.DrawVector(stepGround.point, stepGround.normal, 2f, 1f, Color.green, 0f, depthTest: false);
			}
		}

		private bool SimulateSphereCast(Vector3 groundNormal, out RaycastHit hit)
		{
			float num = Vector3.Angle(groundNormal, controller.up) * (MathF.PI / 180f);
			Vector3 origin = controller.transform.position + controller.up * 0.1f;
			if (!Mathf.Approximately(num, 0f))
			{
				float num2 = Mathf.Sin(num) * controller.radius;
				float num3 = (1f - Mathf.Cos(num)) * controller.radius;
				Vector3 vector = -Vector3.Cross(Vector3.Cross(groundNormal, controller.down), groundNormal);
				origin += Math3d.ProjectVectorOnPlane(controller.up, vector).normalized * num2 + controller.up * num3;
			}
			if (Physics.Raycast(origin, controller.down, out hit, float.PositiveInfinity, walkable, triggerInteraction))
			{
				hit.distance -= 0.120000005f;
				return true;
			}
			return false;
		}
	}

	[SerializeField]
	private Vector3 debugMove = Vector3.zero;

	[SerializeField]
	private QueryTriggerInteraction triggerInteraction;

	[SerializeField]
	private bool fixedTimeStep;

	[SerializeField]
	private int fixedUpdatesPerSecond;

	[SerializeField]
	private bool clampToMovingGround = true;

	[SerializeField]
	private bool debugSpheres = true;

	[SerializeField]
	private bool debugGrounding = true;

	[SerializeField]
	private bool debugPushbackMesssages = true;

	[SerializeField]
	private CollisionSphere[] spheres = new CollisionSphere[3]
	{
		new CollisionSphere(0.5f, isFeet: true, isHead: false),
		new CollisionSphere(1f, isFeet: false, isHead: false),
		new CollisionSphere(1.5f, isFeet: false, isHead: true)
	};

	public LayerMask Walkable;

	[SerializeField]
	private Collider ownCollider;

	[SerializeField]
	public float radius = 0.5f;

	private Vector3 initialPosition;


	private Vector3 lastGroundPosition;

	private bool clamping = true;

	private bool slopeLimiting = true;

	private List<Collider> ignoredColliders;

	private List<IgnoredCollider> ignoredColliderStack;

	private const float Tolerance = 0.1f;

	private const float TinyTolerance = 0.02f;

	private const string TemporaryLayer = "TempCast";

	private const int MaxPushbackIterations = 2;

	private int TemporaryLayerIndex;

	private float fixedDeltaTime;

	private static SuperCollisionType defaultCollisionType;

	public float deltaTime { get; private set; }

	public SuperGround currentGround { get; private set; }

	public CollisionSphere feet { get; private set; }

	public CollisionSphere head { get; private set; }

	public float height => Vector3.Distance(SpherePosition(head), SpherePosition(feet)) + radius * 2f;

	public Vector3 up => base.transform.up;

	public Vector3 down => -base.transform.up;

	public List<SuperCollision> collisionData { get; private set; }

	public Transform currentlyClampedTo { get; set; }

	public float heightScale { get; set; }

	public float radiusScale { get; set; }

	public bool manualUpdateOnly { get; set; }

	public event UpdateDelegate AfterSingleUpdate;

	private void Awake()
	{
		collisionData = new List<SuperCollision>();
		TemporaryLayerIndex = LayerMask.NameToLayer("TempCast");
		ignoredColliders = new List<Collider>();
		ignoredColliderStack = new List<IgnoredCollider>();
		currentlyClampedTo = null;
		fixedDeltaTime = 1f / (float)fixedUpdatesPerSecond;
		heightScale = 1f;
		if ((bool)ownCollider)
		{
			IgnoreCollider(ownCollider);
		}
		else
		{
			ownCollider = GetComponent<CapsuleCollider>();
		}
		CollisionSphere[] array = spheres;
		foreach (CollisionSphere collisionSphere in array)
		{
			if (collisionSphere.isFeet)
			{
				feet = collisionSphere;
			}
			if (collisionSphere.isHead)
			{
				head = collisionSphere;
			}
		}
		if (feet == null)
		{
			Debug.LogError("[SuperCharacterController] Feet not found on controller");
		}
		if (head == null)
		{
			Debug.LogError("[SuperCharacterController] Head not found on controller");
		}
		if (defaultCollisionType == null)
		{
			defaultCollisionType = new GameObject("DefaultSuperCollisionType", typeof(SuperCollisionType)).GetComponent<SuperCollisionType>();
		}
		currentGround = new SuperGround(Walkable, this, triggerInteraction);
		manualUpdateOnly = false;
		base.gameObject.SendMessage("SuperStart", SendMessageOptions.DontRequireReceiver);
	}

	private void Update()
	{
		if (manualUpdateOnly)
		{
			return;
		}
		if (!fixedTimeStep)
		{
			deltaTime = Time.deltaTime;
			SingleUpdate();
			return;
		}
		float num;
		for (num = Time.deltaTime; num > fixedDeltaTime; num -= fixedDeltaTime)
		{
			deltaTime = fixedDeltaTime;
			SingleUpdate();
		}
		if (num > 0f)
		{
			deltaTime = num;
			SingleUpdate();
		}
	}

	private void LateUpdate()
	{
		bool flag = clamping || currentlyClampedTo != null;
		Transform transform = ((currentlyClampedTo != null) ? currentlyClampedTo : currentGround.transform);
		if ((clampToMovingGround & flag) && transform != null && transform.position - lastGroundPosition != Vector3.zero)
		{
			base.transform.position += transform.position - lastGroundPosition;
		}
	}

	public void ManualUpdate(float deltaTime)
	{
		this.deltaTime = deltaTime;
		SingleUpdate();
	}

	private void SingleUpdate()
	{
		initialPosition = base.transform.position;
		ProbeGround(1);
		base.transform.position += debugMove * deltaTime;
		base.gameObject.SendMessage("SuperUpdate", SendMessageOptions.DontRequireReceiver);
		collisionData.Clear();
		RecursivePushback(0, 2);
		ProbeGround(2);
		if (slopeLimiting)
		{
			SlopeLimit();
		}
		ProbeGround(3);
		if (clamping)
		{
			ClampToGround();
		}
		bool num = clamping || currentlyClampedTo != null;
		Transform transform = ((currentlyClampedTo != null) ? currentlyClampedTo : currentGround.transform);
		if (num)
		{
			lastGroundPosition = transform.position;
		}
		if (debugGrounding)
		{
			currentGround.DebugGround(primary: true, near: true, far: true, flush: true, step: true);
		}
		if (AfterSingleUpdate != null)
		{
			AfterSingleUpdate();
		}
	}

	private void ProbeGround(int iter)
	{
		PushIgnoredColliders();
		currentGround.ProbeGround(SpherePosition(feet), iter);
		PopIgnoredColliders();
	}

	private bool SlopeLimit()
	{
		Vector3 vector = currentGround.PrimaryNormal();
		if (Vector3.Angle(vector, up) > currentGround.superCollisionType.SlopeLimit)
		{
			Vector3 vector2 = Math3d.ProjectVectorOnPlane(vector, base.transform.position - initialPosition);
			Vector3 vector3 = Vector3.Cross(vector, down);
			Vector3 to = Vector3.Cross(vector3, vector);
			if (Vector3.Angle(vector2, to) <= 90f)
			{
				return false;
			}
			Vector3 vector4 = Math3d.ProjectPointOnLine(initialPosition, vector3, base.transform.position);
			Vector3 vector5 = Math3d.ProjectVectorOnPlane(vector, vector4 - base.transform.position);
			if (Physics.CapsuleCast(SpherePosition(feet), SpherePosition(head), radius, vector5.normalized, out var hitInfo, vector5.magnitude, Walkable, triggerInteraction))
			{
				base.transform.position += to.normalized * hitInfo.distance;
			}
			else
			{
				base.transform.position += vector5;
			}
			return true;
		}
		return false;
	}

	private void ClampToGround()
	{
		float num = currentGround.Distance();
		base.transform.position -= up * num;
	}

	public void EnableClamping()
	{
		clamping = true;
	}

	public void DisableClamping()
	{
		clamping = false;
	}

	public void EnableSlopeLimit()
	{
		slopeLimiting = true;
	}

	public void DisableSlopeLimit()
	{
		slopeLimiting = false;
	}

	public bool IsClamping()
	{
		return clamping;
	}

	private void RecursivePushback(int depth, int maxDepth)
	{
		PushIgnoredColliders();
		bool flag = false;
		CollisionSphere[] array = spheres;
		foreach (CollisionSphere collisionSphere in array)
		{
			Collider[] array2 = Physics.OverlapSphere(SpherePosition(collisionSphere), radius, Walkable, triggerInteraction);
			foreach (Collider collider in array2)
			{
				Vector3 vector = SpherePosition(collisionSphere);
				if (!SuperCollider.ClosestPointOnSurface(collider, vector, radius, out var closestPointOnSurface))
				{
					return;
				}
				if (debugPushbackMesssages)
				{
					DebugDraw.DrawMarker(closestPointOnSurface, 2f, Color.cyan, 0f, depthTest: false);
				}
				Vector3 vector2 = closestPointOnSurface - vector;
				if (!(vector2 != Vector3.zero))
				{
					continue;
				}
				int layer = collider.gameObject.layer;
				collider.gameObject.layer = TemporaryLayerIndex;
				bool num = Physics.SphereCast(new Ray(vector, vector2.normalized), 0.02f, vector2.magnitude + 0.02f, 1 << TemporaryLayerIndex);
				collider.gameObject.layer = layer;
				if (num)
				{
					if (!(Vector3.Distance(vector, closestPointOnSurface) < radius))
					{
						continue;
					}
					vector2 = vector2.normalized * (radius - vector2.magnitude) * -1f;
				}
				else
				{
					vector2 = vector2.normalized * (radius + vector2.magnitude);
				}
				flag = true;
				base.transform.position += vector2;
				collider.gameObject.layer = TemporaryLayerIndex;
				Physics.SphereCast(new Ray(vector + vector2, closestPointOnSurface - (vector + vector2)), 0.02f, out var hitInfo, 1 << TemporaryLayerIndex);
				collider.gameObject.layer = layer;
				SuperCollisionType component = collider.gameObject.GetComponent<SuperCollisionType>();
				if (component == null)
				{
					component = defaultCollisionType;
				}
				SuperCollision item = new SuperCollision
				{
					collisionSphere = collisionSphere,
					superCollisionType = component,
					gameObject = collider.gameObject,
					point = closestPointOnSurface,
					normal = hitInfo.normal
				};
				collisionData.Add(item);
			}
		}
		PopIgnoredColliders();
		if ((depth < maxDepth) & flag)
		{
			RecursivePushback(depth + 1, maxDepth);
		}
	}

	private void PushIgnoredColliders()
	{
		ignoredColliderStack.Clear();
		for (int i = 0; i < ignoredColliders.Count; i++)
		{
			Collider collider = ignoredColliders[i];
			ignoredColliderStack.Add(new IgnoredCollider(collider, collider.gameObject.layer));
			collider.gameObject.layer = TemporaryLayerIndex;
		}
	}

	private void PopIgnoredColliders()
	{
		for (int i = 0; i < ignoredColliderStack.Count; i++)
		{
			IgnoredCollider ignoredCollider = ignoredColliderStack[i];
			ignoredCollider.collider.gameObject.layer = ignoredCollider.layer;
		}
		ignoredColliderStack.Clear();
	}

	private void OnDrawGizmos()
	{
		Gizmos.color = Color.red;
		if (debugSpheres && spheres != null)
		{
			if (heightScale == 0f)
			{
				heightScale = 1f;
			}
			CollisionSphere[] array = spheres;
			foreach (CollisionSphere collisionSphere in array)
			{
				Gizmos.color = (collisionSphere.isFeet ? Color.green : (collisionSphere.isHead ? Color.yellow : Color.cyan));
				Gizmos.DrawWireSphere(SpherePosition(collisionSphere), radius);
			}
		}
	}

	public Vector3 SpherePosition(CollisionSphere sphere)
	{
		if (sphere.isFeet)
		{
			return base.transform.position + sphere.offset * up;
		}
		return base.transform.position + sphere.offset * up * heightScale;
	}

	public bool PointBelowHead(Vector3 point)
	{
		return Vector3.Angle(point - SpherePosition(head), up) > 89f;
	}

	public bool PointAboveFeet(Vector3 point)
	{
		return Vector3.Angle(point - SpherePosition(feet), down) > 89f;
	}

	public void IgnoreCollider(Collider col)
	{
		ignoredColliders.Add(col);
	}

	public void RemoveIgnoredCollider(Collider col)
	{
		ignoredColliders.Remove(col);
	}

	public void ClearIgnoredColliders()
	{
		ignoredColliders.Clear();
	}
}
