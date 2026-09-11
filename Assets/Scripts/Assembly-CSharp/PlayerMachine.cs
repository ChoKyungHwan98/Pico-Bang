using UnityEngine;

[RequireComponent(typeof(SuperCharacterController))]
[RequireComponent(typeof(PlayerInputController))]
public class PlayerMachine : SuperStateMachine
{
	private enum PlayerStates
	{
		Idle = 0,
		Walk = 1,
		Jump = 2,
		Fall = 3
	}

	public Transform AnimatedMesh;

	public float WalkSpeed = 4f;

	public float WalkAcceleration = 30f;

	public float JumpAcceleration = 5f;

	public float JumpHeight = 3f;

	public float Gravity = 25f;

	private SuperCharacterController controller;

	private Vector3 moveDirection;

	private PlayerInputController input;

	public Vector3 lookDirection { get; private set; }

	private void Start()
	{
		input = base.gameObject.GetComponent<PlayerInputController>();
		controller = base.gameObject.GetComponent<SuperCharacterController>();
		lookDirection = base.transform.forward;
		base.currentState = PlayerStates.Idle;
	}

	protected override void EarlyGlobalSuperUpdate()
	{
		lookDirection = Quaternion.AngleAxis(input.Current.MouseInput.x * (controller.deltaTime / Time.deltaTime), controller.up) * lookDirection;
	}

	protected override void LateGlobalSuperUpdate()
	{
		base.transform.position += moveDirection * controller.deltaTime;
		AnimatedMesh.rotation = Quaternion.LookRotation(lookDirection, controller.up);
	}

	private bool AcquiringGround()
	{
		return controller.currentGround.IsGrounded(currentlyGrounded: false, 0.01f);
	}

	private bool MaintainingGround()
	{
		return controller.currentGround.IsGrounded(currentlyGrounded: true, 0.5f);
	}

	public void RotateGravity(Vector3 up)
	{
		lookDirection = Quaternion.FromToRotation(base.transform.up, up) * lookDirection;
	}

	private Vector3 LocalMovement()
	{
		Vector3 vector = Vector3.Cross(controller.up, lookDirection);
		Vector3 zero = Vector3.zero;
		if (input.Current.MoveInput.x != 0f)
		{
			zero += vector * input.Current.MoveInput.x;
		}
		if (input.Current.MoveInput.z != 0f)
		{
			zero += lookDirection * input.Current.MoveInput.z;
		}
		return zero.normalized;
	}

	private float CalculateJumpSpeed(float jumpHeight, float gravity)
	{
		return Mathf.Sqrt(2f * jumpHeight * gravity);
	}

	private void Idle_EnterState()
	{
		controller.EnableSlopeLimit();
		controller.EnableClamping();
	}

	private void Idle_SuperUpdate()
	{
		if (input.Current.JumpInput)
		{
			base.currentState = PlayerStates.Jump;
		}
		else if (!MaintainingGround())
		{
			base.currentState = PlayerStates.Fall;
		}
		else if (input.Current.MoveInput != Vector3.zero)
		{
			base.currentState = PlayerStates.Walk;
		}
		else
		{
			moveDirection = Vector3.MoveTowards(moveDirection, Vector3.zero, 10f * controller.deltaTime);
		}
	}

	private void Idle_ExitState()
	{
	}

	private void Walk_SuperUpdate()
	{
		if (input.Current.JumpInput)
		{
			base.currentState = PlayerStates.Jump;
		}
		else if (!MaintainingGround())
		{
			base.currentState = PlayerStates.Fall;
		}
		else if (input.Current.MoveInput != Vector3.zero)
		{
			moveDirection = Vector3.MoveTowards(moveDirection, LocalMovement() * WalkSpeed, WalkAcceleration * controller.deltaTime);
		}
		else
		{
			base.currentState = PlayerStates.Idle;
		}
	}

	private void Jump_EnterState()
	{
		controller.DisableClamping();
		controller.DisableSlopeLimit();
		moveDirection += controller.up * CalculateJumpSpeed(JumpHeight, Gravity);
	}

	private void Jump_SuperUpdate()
	{
		Vector3 vector = Math3d.ProjectVectorOnPlane(controller.up, moveDirection);
		Vector3 vector2 = moveDirection - vector;
		if (Vector3.Angle(vector2, controller.up) > 90f && AcquiringGround())
		{
			moveDirection = vector;
			base.currentState = PlayerStates.Idle;
		}
		else
		{
			vector = Vector3.MoveTowards(vector, LocalMovement() * WalkSpeed, JumpAcceleration * controller.deltaTime);
			vector2 -= controller.up * Gravity * controller.deltaTime;
			moveDirection = vector + vector2;
		}
	}

	private void Fall_EnterState()
	{
		controller.DisableClamping();
		controller.DisableSlopeLimit();
	}

	private void Fall_SuperUpdate()
	{
		if (AcquiringGround())
		{
			moveDirection = Math3d.ProjectVectorOnPlane(controller.up, moveDirection);
			base.currentState = PlayerStates.Idle;
		}
		else
		{
			moveDirection -= controller.up * Gravity * controller.deltaTime;
		}
	}
}
