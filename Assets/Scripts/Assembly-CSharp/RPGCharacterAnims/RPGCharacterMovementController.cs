using System.Collections;
using RPGCharacterAnims.Actions;
using RPGCharacterAnims.Extensions;
using RPGCharacterAnims.Lookups;
using UnityEngine;

namespace RPGCharacterAnims
{
	public class RPGCharacterMovementController : SuperStateMachine
	{
		private SuperCharacterController superCharacterController;

		private RPGCharacterController rpgCharacterController;

		private Rigidbody rb;

		private Animator animator;

		private CapsuleCollider capCollider;

		[Header("Knockback")]
		public float knockbackMultiplier = 1f;

		[Header("Movement Multiplier")]
		public float movementAnimationMultiplier = 1f;

		[HideInInspector]
		public Vector3 currentVelocity;

		[Header("Movement")]
		public float walkSpeed = 0.5f;

		public float walkAccel = 15f;

		public float runSpeed = 1f;

		public float runAccel = 30f;

		public float groundFriction = 120f;

		public float rotationSpeed = 100f;

		[HideInInspector]
		public bool canJump;

		[HideInInspector]
		public bool holdingJump;

		[HideInInspector]
		public bool canDoubleJump;

		private bool doublejumped;

		[Header("Jumping")]
		public float jumpSpeed = 12f;

		public float jumpGravity = 24f;

		public float doubleJumpSpeed = 8f;

		public float inAirSpeed = 8f;

		public float inAirAccel = 16f;

		public float fallGravity = 32f;

		public bool fallingControl;

		[Header("Debug Options")]
		public bool debugMessages;

		public bool acquiringGround => superCharacterController.currentGround.IsGrounded(currentlyGrounded: false, 0.01f);

		public bool maintainingGround => superCharacterController.currentGround.IsGrounded(currentlyGrounded: true, 0.5f);

		[HideInInspector]
		public Vector3 lookDirection { get; private set; }

		private void Awake()
		{
			rpgCharacterController = GetComponent<RPGCharacterController>();
			rpgCharacterController.SetHandler(HandlerTypes.AcquiringGround, new SimpleActionHandler(() =>
			{
			}, () =>
			{
			}));
			rpgCharacterController.SetHandler(HandlerTypes.MaintainingGround, new SimpleActionHandler(() =>
			{
			}, () =>
			{
			}));
			rpgCharacterController.SetHandler(HandlerTypes.DiveRoll, new DiveRoll(this));
			rpgCharacterController.SetHandler(HandlerTypes.DoubleJump, new DoubleJump(this));
			rpgCharacterController.SetHandler(HandlerTypes.Fall, new Fall(this));
			rpgCharacterController.SetHandler(HandlerTypes.GetHit, new GetHit(this));
			rpgCharacterController.SetHandler(HandlerTypes.Idle, new Idle(this));
			rpgCharacterController.SetHandler(HandlerTypes.Jump, new Jump(this));
			rpgCharacterController.SetHandler(HandlerTypes.Knockback, new Knockback(this));
			rpgCharacterController.SetHandler(HandlerTypes.Knockdown, new Knockdown(this));
			rpgCharacterController.SetHandler(HandlerTypes.Move, new Move(this));
		}

		private void Start()
		{
			superCharacterController = GetComponent<SuperCharacterController>();
			animator = GetComponentInChildren<Animator>();
			if (!animator)
			{
				Debug.LogError("ERROR: THERE IS NO ANIMATOR COMPONENT ON CHILD OF CHARACTER.");
				Debug.Break();
			}
			capCollider = GetComponent<CapsuleCollider>();
			rb = GetComponent<Rigidbody>();
			if (rb != null)
			{
				rb.constraints = (RigidbodyConstraints)80;
			}
			rpgCharacterController.OnLockMovement += LockMovement;
			rpgCharacterController.OnUnlockMovement += UnlockMovement;
			rpgCharacterController.GetAnimatorTarget().GetComponent<RPGCharacterAnimatorEvents>().OnMove.AddListener(AnimatorMove);
		}

		private void Update()
		{
			if (!superCharacterController.enabled)
			{
				base.gameObject.SendMessage("SuperUpdate", SendMessageOptions.DontRequireReceiver);
			}
		}

		protected override void EarlyGlobalSuperUpdate()
		{
			if (acquiringGround)
			{
				rpgCharacterController.StartAction(HandlerTypes.AcquiringGround);
			}
			else
			{
				rpgCharacterController.EndAction(HandlerTypes.AcquiringGround);
			}
			if (maintainingGround)
			{
				rpgCharacterController.StartAction(HandlerTypes.MaintainingGround);
			}
			else
			{
				rpgCharacterController.EndAction(HandlerTypes.MaintainingGround);
			}
		}

		protected override void LateGlobalSuperUpdate()
		{
			if (!base.enabled)
			{
				return;
			}
			base.transform.position += currentVelocity * superCharacterController.deltaTime;
			if (rpgCharacterController.canMove)
			{
				if (currentVelocity.magnitude > 0f)
				{
					animator.SetFloat(AnimationParameters.VelocityX, 0f);
					animator.SetFloat(AnimationParameters.VelocityZ, base.transform.InverseTransformDirection(currentVelocity).z * movementAnimationMultiplier);
					animator.SetBool(AnimationParameters.Moving, value: true);
				}
				else
				{
					animator.SetFloat(AnimationParameters.VelocityX, 0f);
					animator.SetFloat(AnimationParameters.VelocityZ, 0f);
					animator.SetBool(AnimationParameters.Moving, value: false);
				}
			}
			if (rpgCharacterController.isAiming || rpgCharacterController.isStrafing)
			{
				RotateTowardsTarget(rpgCharacterController.aimInput);
			}
			else if (rpgCharacterController.isFacing)
			{
				RotateTowardsDirection(rpgCharacterController.faceInput);
			}
			else if (rpgCharacterController.canMove)
			{
				RotateTowardsMovementDir();
			}
			if (base.currentState == null && rpgCharacterController.CanStartAction(HandlerTypes.Idle))
			{
				rpgCharacterController.StartAction(HandlerTypes.Idle);
			}
			animator.SetFloat(AnimationParameters.VelocityX, base.transform.InverseTransformDirection(currentVelocity).x * movementAnimationMultiplier);
			animator.SetFloat(AnimationParameters.VelocityZ, base.transform.InverseTransformDirection(currentVelocity).z * movementAnimationMultiplier);
		}

		private void Idle_EnterState()
		{
			if (debugMessages)
			{
				Debug.Log("Idle_EnterState");
			}
			superCharacterController.EnableSlopeLimit();
			superCharacterController.EnableClamping();
			canJump = true;
			doublejumped = false;
			canDoubleJump = false;
		}

		private void Idle_SuperUpdate()
		{
			if (!rpgCharacterController.TryStartAction(HandlerTypes.Fall))
			{
				currentVelocity = Vector3.MoveTowards(currentVelocity, Vector3.zero, groundFriction * superCharacterController.deltaTime);
				rpgCharacterController.TryStartAction(HandlerTypes.Move);
			}
		}

		private void Move_SuperUpdate()
		{
			if (rpgCharacterController.TryStartAction(HandlerTypes.Fall))
			{
				return;
			}
			if (rpgCharacterController.canMove)
			{
				float num = runSpeed;
				float num2 = runAccel;
				if (rpgCharacterController.isStrafing)
				{
					num = walkSpeed;
					num2 = walkAccel;
				}
				currentVelocity = Vector3.MoveTowards(currentVelocity, rpgCharacterController.cameraRelativeInput * num, num2 * superCharacterController.deltaTime);
			}
			rpgCharacterController.TryStartAction(HandlerTypes.Idle);
		}

		private void Jump_EnterState()
		{
			if (debugMessages)
			{
				Debug.Log("Jump_EnterState");
			}
			superCharacterController.DisableClamping();
			superCharacterController.DisableSlopeLimit();
			currentVelocity = new Vector3(currentVelocity.x, jumpSpeed, currentVelocity.z);
			animator.SetInteger(AnimationParameters.Jumping, 1);
			animator.SetAnimatorTrigger(AnimatorTrigger.JumpTrigger);
			canJump = false;
		}

		private void Jump_SuperUpdate()
		{
			holdingJump = rpgCharacterController.jumpInput.y != 0f;
			if (!holdingJump && currentVelocity.y > jumpSpeed / 4f)
			{
				currentVelocity = Vector3.MoveTowards(target: new Vector3(currentVelocity.x, jumpSpeed / 4f, currentVelocity.z), current: currentVelocity, maxDistanceDelta: fallGravity * superCharacterController.deltaTime);
			}
			Vector3 vector = Math3d.ProjectVectorOnPlane(superCharacterController.up, currentVelocity);
			Vector3 vector2 = currentVelocity - vector;
			if (currentVelocity.y < 0f)
			{
				currentVelocity = vector;
				base.currentState = CharacterState.Fall;
			}
			else
			{
				vector = Vector3.MoveTowards(vector, rpgCharacterController.cameraRelativeInput * inAirSpeed, inAirAccel * superCharacterController.deltaTime);
				vector2 -= superCharacterController.up * jumpGravity * superCharacterController.deltaTime;
				currentVelocity = vector + vector2;
			}
		}

		private void DoubleJump_EnterState()
		{
			if (debugMessages)
			{
				Debug.Log("DoubleJump_EnterState");
			}
			currentVelocity = new Vector3(currentVelocity.x, doubleJumpSpeed, currentVelocity.z);
			canDoubleJump = false;
			doublejumped = true;
			animator.SetInteger(AnimationParameters.Jumping, 3);
			animator.SetAnimatorTrigger(AnimatorTrigger.JumpTrigger);
		}

		private void DoubleJump_SuperUpdate()
		{
			Jump_SuperUpdate();
		}

		private void Fall_EnterState()
		{
			if (debugMessages)
			{
				Debug.Log("Fall_EnterState");
			}
			if (!doublejumped)
			{
				canDoubleJump = true;
			}
			superCharacterController.DisableClamping();
			superCharacterController.DisableSlopeLimit();
			canJump = false;
			animator.SetInteger(AnimationParameters.Jumping, 2);
			animator.SetAnimatorTrigger(AnimatorTrigger.JumpTrigger);
		}

		private void Fall_SuperUpdate()
		{
			if (rpgCharacterController.CanStartAction(HandlerTypes.Idle))
			{
				currentVelocity = Math3d.ProjectVectorOnPlane(superCharacterController.up, currentVelocity);
				rpgCharacterController.StartAction(HandlerTypes.Idle);
			}
			else if (fallingControl)
			{
				Vector3 vector = Math3d.ProjectVectorOnPlane(superCharacterController.up, currentVelocity);
				Vector3 vector2 = currentVelocity - vector;
				vector = Vector3.MoveTowards(vector, rpgCharacterController.cameraRelativeInput * inAirSpeed, inAirAccel * superCharacterController.deltaTime);
				vector2 -= superCharacterController.up * fallGravity * superCharacterController.deltaTime;
				currentVelocity = vector + vector2;
			}
			else
			{
				currentVelocity -= superCharacterController.up * fallGravity * superCharacterController.deltaTime;
			}
		}

		private void Fall_ExitState()
		{
			if (debugMessages)
			{
				Debug.Log("Fall_ExitState");
			}
			animator.SetInteger(AnimationParameters.Jumping, 0);
			animator.SetAnimatorTrigger(AnimatorTrigger.JumpTrigger);
		}

		private void DiveRoll_EnterState()
		{
			if (debugMessages)
			{
				Debug.Log("DiveRoll_EnterState");
			}
			rpgCharacterController.OnUnlockMovement += IdleOnceAfterMoveUnlock;
		}

		private void DiveRoll_SuperUpdate()
		{
			if (rpgCharacterController.CanStartAction(HandlerTypes.Idle))
			{
				currentVelocity = Math3d.ProjectVectorOnPlane(superCharacterController.up, currentVelocity);
				rpgCharacterController.StartAction(HandlerTypes.Idle);
			}
			else
			{
				currentVelocity -= superCharacterController.up * (fallGravity / 2f) * superCharacterController.deltaTime;
			}
		}

		private void Knockback_EnterState()
		{
			if (debugMessages)
			{
				Debug.Log("Knockback_EnterState");
			}
			rpgCharacterController.OnUnlockMovement += IdleOnceAfterMoveUnlock;
		}

		private void Knockdown_EnterState()
		{
			if (debugMessages)
			{
				Debug.Log("Knockdown_EnterState");
			}
			rpgCharacterController.OnUnlockMovement += IdleOnceAfterMoveUnlock;
		}

		private void RotateTowardsMovementDir()
		{
			Vector3 forward = new Vector3(currentVelocity.x, 0f, currentVelocity.z);
			if (forward.magnitude > 0.01f)
			{
				base.transform.rotation = Quaternion.Slerp(base.transform.rotation, Quaternion.LookRotation(forward), Time.deltaTime * rotationSpeed);
			}
		}

		private void RotateTowardsTarget(Vector3 targetPosition)
		{
			if (debugMessages)
			{
				Debug.Log($"RotateTowardsTarget: {targetPosition}");
			}
			Vector3 vector = new Vector3(targetPosition.x - base.transform.position.x, 0f, targetPosition.z - base.transform.position.z);
			if (vector != Vector3.zero)
			{
				Quaternion b = Quaternion.LookRotation(vector);
				base.transform.rotation = Quaternion.Lerp(base.transform.rotation, b, Time.deltaTime * rotationSpeed);
			}
		}

		private void RotateTowardsDirection(Vector3 direction)
		{
			if (debugMessages)
			{
				Debug.Log($"RotateTowardsDirection: {direction}");
			}
			Quaternion b = Quaternion.LookRotation(new Vector3(direction.x, 0f, 0f - direction.y), Vector3.up);
			base.transform.rotation = Quaternion.Lerp(base.transform.rotation, b, Time.deltaTime * rotationSpeed);
		}

		public void KnockbackForce(Vector3 knockDirection, float knockBackAmount, float variableAmount)
		{
			StartCoroutine(_KnockbackForce(knockDirection, knockBackAmount, variableAmount));
		}

		private IEnumerator _KnockbackForce(Vector3 knockDirection, float knockBackAmount, float variableAmount)
		{
			if (!(rb == null))
			{
				float startTime = Time.time;
				float elapsed = 0f;
				rb.isKinematic = false;
				while (elapsed < 0.1f)
				{
					rb.AddForce(knockDirection * ((knockBackAmount + Random.Range(0f - variableAmount, variableAmount)) * knockbackMultiplier * 10f), ForceMode.Impulse);
					elapsed = Time.time - startTime;
					yield return null;
				}
				rb.isKinematic = true;
			}
		}

		public void LockMovement()
		{
			currentVelocity = new Vector3(0f, 0f, 0f);
			animator.SetBool(AnimationParameters.Moving, value: false);
			animator.applyRootMotion = true;
		}

		public void UnlockMovement()
		{
			animator.applyRootMotion = false;
		}

		public void AnimatorMove(Vector3 deltaPosition, Quaternion rootRotation)
		{
			base.transform.position += deltaPosition;
			base.transform.rotation = rootRotation;
		}

		public void IdleOnceAfterMoveUnlock()
		{
			rpgCharacterController.StartAction(HandlerTypes.Idle);
			rpgCharacterController.OnUnlockMovement -= IdleOnceAfterMoveUnlock;
		}

		public void InstantSwitchOnceAfterMoveUnlock()
		{
			animator.SetAnimatorTrigger(AnimatorTrigger.InstantSwitchTrigger);
			rpgCharacterController.OnUnlockMovement -= InstantSwitchOnceAfterMoveUnlock;
		}
	}
}
