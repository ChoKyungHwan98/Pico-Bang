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

	[Header("Visual Effects (VFX)")]
	[SerializeField]
	private Material speedEffectMaterial;

	[SerializeField]
	private string materialPropertyName = "_FullscreenIntensity";

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

	private float animationSpeed;

	private float speedSmoothVelocity;

	private Vector3 initialPosition;

	private Quaternion initialRotation;

	private int intensityPropID;

	// 달리기 연출 강도 0~1. 풀스크린 속도선 머티리얼(_FullscreenIntensity)로 넘어간다.
	private float speedEffectIntensity;

	private void Awake()
	{
		rb = GetComponent<Rigidbody>();

		// 카메라가 인게임에서 플레이어 밑(tpsCamPos)에 붙는다.
		// 리지드바디는 FixedUpdate(기본 50Hz)로 움직이므로, 보간을 켜지 않으면
		// 렌더 프레임마다 위치가 계단식으로 튀고 그게 그대로 화면 흔들림이 된다.
		rb.interpolation = RigidbodyInterpolation.Interpolate;
		rb.freezeRotation = true;

		intensityPropID = Shader.PropertyToID(materialPropertyName);
		ResetEffectIntensity();
	}

	public void StopSpeedEffect()
	{
		ResetEffectIntensity();
	}

	private void ResetEffectIntensity()
	{
		speedEffectIntensity = 0f;
		if (speedEffectMaterial != null)
		{
			speedEffectMaterial.SetFloat(intensityPropID, 0f);
		}
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
			CheckStatus();
			HandleMovement();
			UpdateAnimation();
		}
	}

	private void HandleSpeedEffect()
	{
		// 강도는 머티리얼에서 읽지 않고 여기서 들고 있는다 (머티리얼 값은 에셋이라 이전 플레이 값이 남아 있을 수 있다)
		float target = (currentSpeed >= runThreshold) ? 1f : 0f;
		speedEffectIntensity = Mathf.Lerp(speedEffectIntensity, target, Time.deltaTime * 5f);
		if (speedEffectIntensity < 0.01f)
		{
			speedEffectIntensity = 0f;
		}

		if (speedEffectMaterial != null)
		{
			speedEffectMaterial.SetFloat(intensityPropID, speedEffectIntensity);
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

		base.transform.Rotate(Vector3.up * (lookDelta.x * sens));

		currentXRotation -= lookDelta.y * sens * verticalSensitivityRatio;
		currentXRotation = Mathf.Clamp(currentXRotation, minXAngle, maxXAngle);
		if (cameraPivot != null)
		{
			cameraPivot.localRotation = Quaternion.Euler(currentXRotation, 0f, 0f);
		}
	}

	private void HandleMovement()
	{
		float num = ((sprintAction.action.ReadValue<float>() > 0f) ? sprintSpeed : walkSpeed);
		if (moveInput.magnitude < 0.1f)
		{
			num = 0f;
		}
		Vector3 vector = base.transform.forward * moveInput.y + base.transform.right * moveInput.x;
		if (vector.magnitude > 1f)
		{
			vector.Normalize();
		}
		Vector3 vector2 = vector * num;
		rb.linearVelocity = new Vector3(vector2.x, rb.linearVelocity.y, vector2.z);
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
			bool flag = sprintAction.action.ReadValue<float>() > 0f;
			float target = ((moveInput.magnitude < 0.1f) ? 0f : (flag ? 10f : 5f));
			animationSpeed = Mathf.SmoothDamp(animationSpeed, target, ref speedSmoothVelocity, speedSmoothTime);
			animator.SetFloat("Speed", animationSpeed);
			animator.SetFloat("VerticalVelocity", rb.linearVelocity.y);
			animator.SetFloat("Move X", moveInput.x, 0.08f, Time.deltaTime);
			animator.SetFloat("Move Y", moveInput.y, 0.08f, Time.deltaTime);
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
