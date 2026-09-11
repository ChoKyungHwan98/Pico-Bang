using UnityEngine;

/// <summary>
/// 3인칭 추적 카메라.
///
/// 실행 순서를 명시적으로 가장 뒤로 미룬다.
/// <see cref="SuperCharacterController"/>도 LateUpdate에서 플레이어 위치를 보정하는데,
/// 순서가 보장되지 않으면 카메라가 보정 전 위치를 읽는 프레임이 생겨 화면이 튄다.
/// </summary>
[DefaultExecutionOrder(200)]
public class PlayerCamera : MonoBehaviour
{
	public float Distance = 5f;

	public float Height = 2f;

	public GameObject PlayerTarget;

	[Header("Look Limits")]
	[Tooltip("위로 올려다볼 수 있는 한계(도)")]
	public float pitchMax = 70f;

	[Tooltip("아래로 내려다볼 수 있는 한계(도)")]
	public float pitchMin = -60f;

	[Header("Smoothing")]
	[Tooltip("카메라 위치 추종 지연(초). 0이면 즉시 붙는다. 크게 줄수록 부드럽지만 반응이 늦다")]
	[Range(0f, 0.3f)]
	public float positionSmoothTime = 0.03f;

	[Header("Collision")]
	[Tooltip("벽에 카메라가 파묻히지 않게 앞으로 당긴다")]
	public bool avoidWallClipping = true;

	public LayerMask collisionMask = ~0;

	[Tooltip("벽과 유지할 여유 거리")]
	public float collisionBuffer = 0.25f;

	public float collisionRadius = 0.25f;

	[Tooltip("벽에서 벗어날 때 원래 거리로 돌아오는 속도")]
	public float collisionReturnSpeed = 8f;

	private PlayerInputController input;

	private Transform target;

	private PlayerMachine machine;

	private float yRotation;

	private SuperCharacterController controller;

	private Vector3 positionVelocity;

	private float currentDistance;

	private bool initialized;

	private void Start()
	{
		input = PlayerTarget.GetComponent<PlayerInputController>();
		machine = PlayerTarget.GetComponent<PlayerMachine>();
		controller = PlayerTarget.GetComponent<SuperCharacterController>();
		target = PlayerTarget.transform;
		currentDistance = Distance;
	}

	private void LateUpdate()
	{
		if (target == null || machine == null || controller == null) { return; }

		// ── 시선 각도 ────────────────────────────────
		yRotation += input.Current.MouseInput.y;
		yRotation = Mathf.Clamp(yRotation, pitchMin, pitchMax);

		Vector3 up = controller.up;
		Vector3 axis = Vector3.Cross(machine.lookDirection, up);

		Quaternion rotation = Quaternion.LookRotation(machine.lookDirection, up);
		rotation = Quaternion.AngleAxis(yRotation, axis) * rotation;
		base.transform.rotation = rotation;

		// ── 위치 ────────────────────────────────────
		Vector3 pivot = target.position + up * Height;
		Vector3 back = -(rotation * Vector3.forward);

		float wantedDistance = Distance;
		if (avoidWallClipping)
		{
			wantedDistance = ResolveCollision(pivot, back, Distance);
		}

		// 벽에 붙을 땐 즉시 당기고, 벗어날 땐 서서히 돌아온다.
		// 반대로 하면 벽 모서리를 스칠 때마다 카메라가 튄다.
		if (wantedDistance < currentDistance)
		{
			currentDistance = wantedDistance;
		}
		else
		{
			currentDistance = Mathf.Lerp(currentDistance, wantedDistance, Time.deltaTime * collisionReturnSpeed);
		}

		Vector3 desired = pivot + back * currentDistance;

		if (!initialized || positionSmoothTime <= 0f)
		{
			base.transform.position = desired;
			initialized = true;
		}
		else
		{
			base.transform.position = Vector3.SmoothDamp(
				base.transform.position, desired, ref positionVelocity, positionSmoothTime);
		}
	}

	/// <summary>피벗에서 뒤로 빠지는 경로에 벽이 있으면 그 앞까지만 물러난다.</summary>
	private float ResolveCollision(Vector3 pivot, Vector3 back, float maxDistance)
	{
		if (Physics.SphereCast(pivot, collisionRadius, back, out RaycastHit hit,
				maxDistance, collisionMask, QueryTriggerInteraction.Ignore))
		{
			return Mathf.Max(0.1f, hit.distance - collisionBuffer);
		}
		return maxDistance;
	}

	/// <summary>재시작 시 시선을 초기화한다.</summary>
	public void ResetCamera()
	{
		yRotation = 0f;
		currentDistance = Distance;
		positionVelocity = Vector3.zero;
		initialized = false;
	}
}
