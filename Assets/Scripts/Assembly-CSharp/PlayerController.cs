using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour, IGameResettable
{
	[Header("Movement Settings")]
	[SerializeField]
	private float walkSpeed = 5f;

	[SerializeField]
	private float sprintSpeed = 10f;

	[SerializeField]
	private float jumpForce = 5f;

	[SerializeField]
	private float speedSmoothTime = 0.1f;

	[Tooltip("8방향 애니메이션이 새 방향으로 돌아가는 시간(초)")]
	[SerializeField]
	private float directionSmoothTime = 0.1f;

	[Tooltip("이동 속도가 올라가는 가속도")]
	[SerializeField]
	private float acceleration = 48f;

	[Tooltip("키를 놓았을 때 멈추는 감속도")]
	[SerializeField]
	private float deceleration = 70f;

	[Header("Look Settings")]
	[Tooltip("설정 메뉴의 마우스 감도에 곱해지는 개인 배수. 1이 기본")]
	[SerializeField]
	private float lookSensitivityScale = 1f;

	[Tooltip("좌우 대비 상하 감도 비율")]
	[SerializeField]
	private float verticalSensitivityRatio = 1f;

	[SerializeField]
	private float minXAngle = -60f;

	[SerializeField]
	private float maxXAngle = 60f;

	[Header("Ground Check")]
	[SerializeField]
	private LayerMask groundLayer;

	[SerializeField]
	private float rayLength = 0.2f;

	[SerializeField]
	private Vector3 rayOriginOffset = new Vector3(0f, 0.1f, 0f);

	[Header("Sprint Camera Response")]
	[SerializeField]
	private float runThreshold = 9f;

	[Header("State Info (Read Only)")]
	[SerializeField]
	private bool isGrounded;

	[SerializeField]
	private bool isJumping;

	[SerializeField]
	private bool isFalling;

	[SerializeField]
	private float currentSpeed;

	[Header("References")]
	[SerializeField]
	private Transform cameraPivot;

	[SerializeField]
	private Animator animator;

	[SerializeField]
	private InputActionReference moveAction;

	[SerializeField]
	private InputActionReference lookAction;

	[SerializeField]
	private InputActionReference jumpAction;

	[SerializeField]
	private InputActionReference sprintAction;

	private Rigidbody rb;

	private Vector2 moveInput;

	private float currentXRotation;

	// 좌우 시선(yaw). 카메라는 이 값을 매 프레임 그대로 쓰고, 몸은 FixedUpdate에서 MoveRotation으로 따라온다.
	// 예전에는 보간이 켜진 리지드바디의 transform을 Update에서 직접 돌렸다 — 다음 프레임 보간이 옛 회전으로 덮어써서
	// 돌린 양의 약 40%가 사라지고 프레임마다 역회전이 섞였다(측정 2026-09-25: 편차 24%, 220프레임 중 역회전 21번)
	private float yaw;
	private Vector3 pivotOffset;

	private float animationSpeed;

	private float speedSmoothVelocity;
	private Vector3 planarVelocity;
	private float animationDirectionAngle;
	private float animationDirectionVelocity;
	private bool animationDirectionInitialized;

	private Vector3 initialPosition;

	private Quaternion initialRotation;

	// Sprint camera distance and field of view still use this 0-1 value.
	private float speedEffectIntensity;
	public float SprintVisualStrength => Mathf.Clamp01(speedEffectIntensity);

	private void Awake()
	{
		rb = GetComponent<Rigidbody>();

		// 카메라가 인게임에서 플레이어 밑(tpsCamPos)에 붙는다.
		// 리지드바디는 FixedUpdate(기본 50Hz)로 움직이므로, 보간을 켜지 않으면
		// 렌더 프레임마다 위치가 계단식으로 튀고 그게 그대로 화면 흔들림이 된다.
		rb.interpolation = RigidbodyInterpolation.Interpolate;
		rb.freezeRotation = true;
		yaw = transform.eulerAngles.y;
		if (cameraPivot != null) pivotOffset = cameraPivot.localPosition;

		ResetEffectIntensity();
	}

	public void StopSpeedEffect()
	{
		ResetEffectIntensity();
	}

	private void ResetEffectIntensity()
	{
		speedEffectIntensity = 0f;
	}

	public void SaveInitialState()
	{
		initialPosition = base.transform.position;
		initialRotation = base.transform.rotation;
	}

	/// <summary>
	/// 리지드바디를 안전하게 순간이동시킨다.
	///
	/// 보간이 켜진 리지드바디는 transform만 옮기면 물리 엔진이 직전 위치를 그대로 들고 있어서
	/// 다음 프레임에 되돌아가거나 엉뚱한 지점에서 보간이 시작된다.
	/// rb.position까지 맞추고 SyncTransforms로 물리 쪽 상태를 강제로 맞춰야 한다.
	/// </summary>
	public void TeleportTo(Vector3 position, Quaternion rotation)
	{
		if (rb == null)
		{
			base.transform.SetPositionAndRotation(position, rotation);
			return;
		}

		RigidbodyInterpolation previous = rb.interpolation;
		rb.interpolation = RigidbodyInterpolation.None;

		base.transform.SetPositionAndRotation(position, rotation);
		rb.position = position;
		rb.rotation = rotation;
		yaw = rotation.eulerAngles.y;

		if (!rb.isKinematic)
		{
			rb.linearVelocity = Vector3.zero;
			rb.angularVelocity = Vector3.zero;
		}

		Physics.SyncTransforms();
		rb.interpolation = previous;
	}

	public void ResetToInitialState()
	{
		// 사망 시 kinematic으로 바뀐 상태를 먼저 풀어야 속도를 만질 수 있다.
		// (kinematic 상태에서 velocity를 대입하면 경고가 뜨고 무시된다)
		if (rb != null)
		{
			rb.isKinematic = false;
		}

		TeleportTo(initialPosition, initialRotation);
		currentXRotation = 0f;
		if (cameraPivot != null)
		{
			cameraPivot.localRotation = Quaternion.identity;
			cameraPivot.localPosition = pivotOffset;
		}
		if (animator != null)
		{
			animator.SetFloat("Speed", 0f);
			animator.SetFloat("Move X", 0f);
			animator.SetFloat("Move Y", 0f);
			animator.SetFloat("VerticalVelocity", 0f);
			animator.SetBool("IsGrounded", value: true);
			animator.SetBool("IsJumping", value: false);
			animator.SetBool("IsFalling", value: false);
			animator.SetBool("IsDancing", value: true);
		}
		planarVelocity = Vector3.zero;
		animationDirectionAngle = 0f;
		animationDirectionVelocity = 0f;
		animationDirectionInitialized = false;
		StopSpeedEffect();
	}

	private void OnEnable()
	{
		if (moveAction != null)
		{
			moveAction.action.Enable();
		}
		if (lookAction != null)
		{
			lookAction.action.Enable();
		}
		if (jumpAction != null)
		{
			jumpAction.action.Enable();
		}
		if (sprintAction != null)
		{
			sprintAction.action.Enable();
		}
	}

	private void OnDisable()
	{
		if (moveAction != null)
		{
			moveAction.action.Disable();
		}
		if (lookAction != null)
		{
			lookAction.action.Disable();
		}
		if (jumpAction != null)
		{
			jumpAction.action.Disable();
		}
		if (sprintAction != null)
		{
			sprintAction.action.Disable();
		}
		StopSpeedEffect();
	}

	private void Update()
	{
		if (GameFlowManager.Instance != null && !GameFlowManager.Instance.IsGameRunning)
		{
			StopSpeedEffect();
			return;
		}
		HandleLook();
		moveInput = moveAction.action.ReadValue<Vector2>();
		if (jumpAction.action.triggered && isGrounded)
		{
			PerformJump();
		}
		HandleSpeedEffect();
	}

	private void FixedUpdate()
	{
		if (!(GameFlowManager.Instance != null) || GameFlowManager.Instance.IsGameRunning)
		{
			// 몸은 물리로 돌린다 — 보간이 부드럽게 이어 준다
			rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));
			CheckStatus();
			HandleMovement();
			UpdateAnimation();
		}
	}

	private void HandleSpeedEffect()
	{
		// Keep the camera response independent of the removed full-screen speed lines.
		float target = sprintAction != null && sprintAction.action.ReadValue<float>() > 0f
			? Mathf.InverseLerp(runThreshold - 1f, sprintSpeed, currentSpeed) : 0f;
		speedEffectIntensity = Mathf.MoveTowards(speedEffectIntensity, target, Time.deltaTime * (target > speedEffectIntensity ? 1.8f : 2.8f));
		if (speedEffectIntensity < 0.01f)
		{
			speedEffectIntensity = 0f;
		}

	}

	private void HandleLook()
	{
		Vector2 lookDelta = lookAction.action.ReadValue<Vector2>();

		// 마우스 델타는 "이번 프레임 동안 움직인 픽셀 수"라 이미 시간이 반영된 값이다.
		// 여기에 Time.deltaTime을 또 곱하면 회전량이 프레임 시간의 제곱에 비례해서,
		// 프레임이 조금만 흔들려도 화면이 튄다. 곱하지 않는 것이 맞다.
		float sens = GameSettings.MouseSensitivity * lookSensitivityScale;
		if (GameSettings.InvertY) { lookDelta.y = -lookDelta.y; }

		yaw += lookDelta.x * sens + DebugYawRate * Time.deltaTime;
		currentXRotation -= lookDelta.y * sens * verticalSensitivityRatio;
		currentXRotation = Mathf.Clamp(currentXRotation, minXAngle, maxXAngle);
		ApplyCameraPivot();
	}

	/// <summary>테스트용: 마우스 입력 대신 초당 이만큼(도) 좌우로 돈다. Update의 HandleLook 경로를 그대로 탄다.</summary>
	public static float DebugYawRate;

	/// <summary>테스트용: 마우스 입력 없이 좌우로 degrees만큼 돌린다(HandleLook과 같은 경로).</summary>
	public void DebugApplyLook(float degrees)
	{
		yaw += degrees;
		ApplyCameraPivot();
	}

	/// <summary>
	/// 카메라 축은 몸의 보간 회전을 따라가지 않고 시선 값(yaw·pitch)을 그대로 쓴다.
	/// 위치도 몸의 보간 위치 + 시선 방향 기준 어깨 오프셋 — 몸이 한 물리 프레임 늦게 돌아도 카메라는 흔들리지 않는다.
	/// </summary>
	private void ApplyCameraPivot()
	{
		if (cameraPivot == null) return;
		Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
		cameraPivot.SetPositionAndRotation(base.transform.position + yawRotation * pivotOffset,
			Quaternion.Euler(currentXRotation, yaw, 0f));
	}

	private void LateUpdate()
	{
		// 보간이 몸의 위치·회전을 바꾼 뒤 카메라 축을 다시 맞춘다
		if (GameFlowManager.Instance != null && !GameFlowManager.Instance.IsGameRunning) return;
		ApplyCameraPivot();
	}

	private void HandleMovement()
	{
		Vector2 input = Vector2.ClampMagnitude(moveInput, 1f);
		if (input.magnitude < 0.1f) input = Vector2.zero;
		float targetSpeed = sprintAction != null && sprintAction.action.ReadValue<float>() > 0f
			? sprintSpeed : walkSpeed;
		// 이동 방향은 몸이 아니라 시선 기준 — 몸은 한 물리 프레임 늦게 돈다
		Quaternion look = Quaternion.Euler(0f, yaw, 0f);
		Vector3 direction = look * Vector3.forward * input.y + look * Vector3.right * input.x;
		if (direction.sqrMagnitude > 1f) direction.Normalize();
		Vector3 desired = direction * targetSpeed * input.magnitude;
		float rate = desired.sqrMagnitude > planarVelocity.sqrMagnitude ? acceleration : deceleration;
		planarVelocity = Vector3.MoveTowards(planarVelocity, desired, rate * Time.fixedDeltaTime);
		rb.linearVelocity = new Vector3(planarVelocity.x, rb.linearVelocity.y, planarVelocity.z);
	}

	private void PerformJump()
	{
		rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
		rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
	}

	private void CheckStatus()
	{
		Vector3 origin = base.transform.position + rayOriginOffset;
		isGrounded = Physics.Raycast(origin, Vector3.down, rayLength, groundLayer);
		isJumping = !isGrounded && rb.linearVelocity.y > 0.1f;
		isFalling = !isGrounded && rb.linearVelocity.y < -0.1f;
		currentSpeed = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z).magnitude;
	}

	private void UpdateAnimation()
	{
		if (!(animator == null))
		{
			// 물리 속도와 애니메이션 속도를 맞춘다. Shift를 누르는 순간 달리기 클립으로
			// 튀면 실제 몸은 아직 가속 중인데 발만 빨라져 미끄러지거나 떠 보인다.
			float target = Mathf.Clamp(currentSpeed, 0f, sprintSpeed);
			animationSpeed = Mathf.SmoothDamp(animationSpeed, target, ref speedSmoothVelocity, speedSmoothTime);
			animator.SetFloat("Speed", animationSpeed);
			animator.SetFloat("VerticalVelocity", rb.linearVelocity.y);

			// 방향값의 크기는 항상 1로 유지하고 각도만 부드럽게 돌린다.
			// 기존 방식은 걷기 속도(5)를 달리기 속도(10)로 나눠 0.5만 전달했고,
			// 반대 방향 전환 때 벡터가 0을 지나며 여러 클립이 엉뚱하게 섞였다.
			Vector2 animationInput = Vector2.ClampMagnitude(moveInput, 1f);
			if (animationInput.sqrMagnitude > .01f)
			{
				float targetAngle = Mathf.Atan2(animationInput.x, animationInput.y) * Mathf.Rad2Deg;
				if (!animationDirectionInitialized)
				{
					animationDirectionAngle = targetAngle;
					animationDirectionInitialized = true;
				}
				else
				{
					animationDirectionAngle = Mathf.SmoothDampAngle(animationDirectionAngle, targetAngle,
						ref animationDirectionVelocity, directionSmoothTime, Mathf.Infinity, Time.fixedDeltaTime);
				}

				float radians = animationDirectionAngle * Mathf.Deg2Rad;
				animator.SetFloat("Move X", Mathf.Sin(radians));
				animator.SetFloat("Move Y", Mathf.Cos(radians));
			}
			animator.SetBool("IsGrounded", isGrounded);
			animator.SetBool("IsJumping", isJumping);
			animator.SetBool("IsFalling", isFalling);
		}
	}

	private void OnDrawGizmos()
	{
		Gizmos.color = Color.cyan;
		Vector3 vector = base.transform.position + rayOriginOffset;
		Gizmos.DrawLine(vector, vector + Vector3.down * rayLength);
	}
}
