using System;
using System.Collections;
using System.Collections.Generic;
using RPGCharacterAnims.Actions;
using RPGCharacterAnims.Extensions;
using RPGCharacterAnims.Lookups;
using UnityEngine;

namespace RPGCharacterAnims
{
	public class RPGCharacterController : MonoBehaviour
	{
		[HideInInspector]
		public Animator animator;

		public float animationSpeed = 1f;

		[HideInInspector]
		public IKHands ikHands;

		public Transform target;

		private bool _canAction;

		private bool _canFace = true;

		private bool _canMove;

		private bool _canStrafe = true;

		private bool _isAttacking;

		private Vector3 _moveInput;

		private Vector3 _aimInput;

		private Vector3 _faceInput;

		private Vector3 _jumpInput;

		private Vector3 _cameraRelativeInput;

		[HideInInspector]
		public Weapon rightWeapon;

		[HideInInspector]
		public Weapon leftWeapon;

		private Dictionary<string, IActionHandler> actionHandlers = new Dictionary<string, IActionHandler>();

		public bool canAction
		{
			get
			{
				if (_canAction)
				{
					return !isNavigating;
				}
				return false;
			}
		}

		public bool canFace => _canFace;

		public bool canMove => _canMove;

		public bool canStrafe => _canStrafe;

		public bool acquiringGround => TryGetHandlerActive(HandlerTypes.AcquiringGround);

		public bool isAiming => TryGetHandlerActive(HandlerTypes.Aim);

		public bool isAttacking => _isAttacking;

		public bool isFacing => TryGetHandlerActive(HandlerTypes.Face);

		public bool isFalling => TryGetHandlerActive(HandlerTypes.Fall);

		public bool isIdle => TryGetHandlerActive(HandlerTypes.Idle);

		public bool isMoving => TryGetHandlerActive(HandlerTypes.Move);

		public bool isNavigating => TryGetHandlerActive(HandlerTypes.Navigation);

		public bool isRolling => TryGetHandlerActive(HandlerTypes.Roll);

		public bool isKnockback => TryGetHandlerActive(HandlerTypes.Knockback);

		public bool isKnockdown => TryGetHandlerActive(HandlerTypes.Knockdown);

		public bool isStrafing => TryGetHandlerActive(HandlerTypes.Strafe);

		public bool maintainingGround => TryGetHandlerActive(HandlerTypes.MaintainingGround);

		public Vector3 moveInput => _moveInput;

		public Vector3 aimInput => _aimInput;

		public Vector3 faceInput => _faceInput;

		public Vector3 jumpInput => _jumpInput;

		public Vector3 cameraRelativeInput => _cameraRelativeInput;

		public bool hasTwoHandedWeapon => rightWeapon.Is2HandedWeapon();

		public bool hasNoWeapon
		{
			get
			{
				if (rightWeapon.HasNoWeapon())
				{
					return leftWeapon.HasNoWeapon();
				}
				return false;
			}
		}

		public event Action OnLockActions = () =>
		{
		};

		public event Action OnUnlockActions = () =>
		{
		};

		public event Action OnLockMovement = () =>
		{
		};

		public event Action OnUnlockMovement = () =>
		{
		};

		private void Awake()
		{
			animator = GetComponentInChildren<Animator>();
			if (!animator)
			{
				Debug.LogError("ERROR: THERE IS NO ANIMATOR COMPONENT ON CHILD OF CHARACTER.");
				Debug.Break();
			}
			animator.gameObject.AddComponent<RPGCharacterAnimatorEvents>();
			animator.updateMode = AnimatorUpdateMode.Normal;
			animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
			animator.SetInteger(AnimationParameters.Weapon, 0);
			animator.SetInteger(AnimationParameters.WeaponSwitch, 0);
			ikHands = GetComponentInChildren<IKHands>();
			SetHandler(HandlerTypes.Attack, new Attack());
			SetHandler(HandlerTypes.Face, new SimpleActionHandler(StartFace, EndFace));
			SetHandler(HandlerTypes.Null, new Null());
			SetHandler(HandlerTypes.SlowTime, new SlowTime());
			SetHandler(HandlerTypes.Strafe, new SimpleActionHandler(StartStrafe, EndStrafe));
			Unlock(movement: true, actions: true);
			SetAimInput(target.transform.position);
		}

		public void SetHandler(string action, IActionHandler handler)
		{
			actionHandlers[action] = handler;
		}

		public IActionHandler GetHandler(string action)
		{
			if (HandlerExists(action))
			{
				return actionHandlers[action];
			}
			Debug.LogError("RPGCharacterController: No handler for action \"" + action + "\"");
			return actionHandlers[HandlerTypes.Null];
		}

		public bool HandlerExists(string action)
		{
			return actionHandlers.ContainsKey(action);
		}

		public bool TryGetHandlerActive(string action)
		{
			if (HandlerExists(action))
			{
				return IsActive(action);
			}
			return false;
		}

		public bool IsActive(string action)
		{
			return GetHandler(action).IsActive();
		}

		public bool CanStartAction(string action)
		{
			return GetHandler(action).CanStartAction(this);
		}

		public bool TryStartAction(string action, object context = null)
		{
			if (!CanStartAction(action))
			{
				return false;
			}
			if (context == null)
			{
				StartAction(action);
			}
			else
			{
				StartAction(action, context);
			}
			return true;
		}

		public bool TryEndAction(string action)
		{
			if (!CanEndAction(action))
			{
				return false;
			}
			EndAction(action);
			return true;
		}

		public bool CanEndAction(string action)
		{
			return GetHandler(action).CanEndAction(this);
		}

		public void StartAction(string action, object context = null)
		{
			GetHandler(action).StartAction(this, context);
		}

		public void EndAction(string action)
		{
			GetHandler(action).EndAction(this);
		}

		private void LateUpdate()
		{
			animator.SetFloat(AnimationParameters.AnimationSpeed, animationSpeed);
		}

		public void SetMoveInput(Vector3 _moveInput)
		{
			this._moveInput = _moveInput;
			Vector3 vector = Camera.main.transform.TransformDirection(Vector3.forward);
			vector.y = 0f;
			vector = vector.normalized;
			Vector3 vector2 = new Vector3(vector.z, 0f, 0f - vector.x);
			Vector3 vector3 = _moveInput.x * vector2 + _moveInput.y * vector;
			if (vector3.magnitude > 1f)
			{
				vector3.Normalize();
			}
			_cameraRelativeInput = vector3;
		}

		public void SetFaceInput(Vector3 _faceInput)
		{
			this._faceInput = _faceInput;
		}

		public void SetAimInput(Vector3 _aimInput)
		{
			this._aimInput = _aimInput;
		}

		public void SetJumpInput(Vector3 _jumpInput)
		{
			this._jumpInput = _jumpInput;
		}

		public void DiveRoll(DiveRollType rollType)
		{
			animator.TriggerDiveRoll(rollType);
			Lock(lockMovement: true, lockAction: true, timed: true, 0f, 1f);
			SetIKPause(1.05f);
		}

		public void Knockback(KnockbackType direction)
		{
			animator.TriggerKnockback(direction);
			switch (direction)
			{
			case KnockbackType.Knockback1:
				SetIKPause(1.125f);
				Lock(lockMovement: true, lockAction: true, timed: true, 0f, 1f);
				break;
			case KnockbackType.Knockback2:
				SetIKPause(1f);
				Lock(lockMovement: true, lockAction: true, timed: true, 0f, 0.8f);
				break;
			}
		}

		public void Knockdown(KnockdownType direction)
		{
			animator.TriggerKnockdown(direction);
			Lock(lockMovement: true, lockAction: true, timed: true, 0f, 5.25f);
			SetIKPause(5.25f);
		}

		public void Attack(int attackNumber, Side attackSide, Weapon leftWeapon, Weapon rightWeapon, float duration)
		{
			animator.SetSide(attackSide);
			_isAttacking = true;
			Lock(lockMovement: true, lockAction: true, timed: true, 0f, duration);
			AnimatorTrigger trigger = AnimatorTrigger.AttackTrigger;
			animator.SetActionTrigger(trigger, attackNumber);
		}

		public void RunningAttack(Side side, bool leftWeapon, bool rightWeapon, bool twoHandedWeapon)
		{
			if ((side == Side.Right) & rightWeapon)
			{
				animator.SetActionTrigger(AnimatorTrigger.AttackTrigger, 4);
			}
			else if (hasNoWeapon)
			{
				animator.SetSide(side);
				animator.SetActionTrigger(AnimatorTrigger.AttackTrigger, 1);
			}
		}

		public void StartFace()
		{
		}

		public void EndFace()
		{
		}

		public void StartStrafe()
		{
		}

		public void EndStrafe()
		{
		}

		public void GetHit(int hitNumber)
		{
			animator.TriggerGettingHit(hitNumber);
			Lock(lockMovement: true, lockAction: true, timed: true, 0.1f, 0.4f);
			SetIKPause(0.6f);
		}

		public GameObject GetAnimatorTarget()
		{
			return animator.gameObject;
		}

		private float CurrentAnimationLength(int animationlayer)
		{
			return animator.GetCurrentAnimatorClipInfo(animationlayer).Length;
		}

		public void Lock(bool lockMovement, bool lockAction, bool timed, float delayTime, float lockTime)
		{
			StopCoroutine("_Lock");
			StartCoroutine(_Lock(lockMovement, lockAction, timed, delayTime, lockTime));
		}

		private IEnumerator _Lock(bool lockMovement, bool lockAction, bool timed, float delayTime, float lockTime)
		{
			if (delayTime > 0f)
			{
				yield return new WaitForSeconds(delayTime);
			}
			if (lockMovement)
			{
				_canMove = false;
				OnLockMovement();
			}
			if (lockAction)
			{
				_canAction = false;
				OnLockActions();
			}
			if (timed)
			{
				if (lockTime > 0f)
				{
					yield return new WaitForSeconds(lockTime);
				}
				Unlock(lockMovement, lockAction);
			}
		}

		public void Unlock(bool movement, bool actions)
		{
			if (movement)
			{
				_canMove = true;
				OnUnlockMovement();
			}
			if (actions)
			{
				_canAction = true;
				if (_isAttacking)
				{
					_isAttacking = false;
				}
				OnUnlockActions();
			}
		}

		public void SetIKOff()
		{
			if (!(ikHands == null))
			{
				ikHands.leftHandPositionWeight = 0f;
				ikHands.leftHandRotationWeight = 0f;
			}
		}

		public void SetIKOn(Weapon weapon)
		{
			if (ikHands != null)
			{
				ikHands.BlendIK(blendOn: true, 0f, 0f, weapon);
			}
		}

		public void SetIKPause(float pauseTime)
		{
			if (ikHands != null && ikHands.isUsed)
			{
				ikHands.SetIKPause(pauseTime);
			}
		}
	}
}
