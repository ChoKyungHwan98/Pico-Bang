using System;
using RPGCharacterAnims.Actions;
using RPGCharacterAnims.Lookups;
using UnityEngine;

namespace RPGCharacterAnims
{
	[HelpURL("https://docs.unity3d.com/Manual/class-InputManager.html")]
	public class RPGCharacterInputController : MonoBehaviour
	{
		private RPGCharacterController rpgCharacterController;

		private float inputHorizontal;

		private float inputVertical;

		private bool inputJump;

		private bool inputLightHit;

		private bool inputKnockdown;

		private bool inputAttackL;

		private bool inputAttackR;

		private float inputSwitchUpDown;

		private float inputAimBlock;

		private bool inputAiming;

		private bool inputFace;

		private float inputFacingHorizontal;

		private float inputFacingVertical;

		private bool inputRoll;

		private Vector3 moveInput;

		private bool isJumpHeld;

		private float inputPauseTimeout;

		private bool inputPaused;

		private void Awake()
		{
			rpgCharacterController = GetComponent<RPGCharacterController>();
		}

		private void Update()
		{
			if (inputPaused)
			{
				if (!(Time.time > inputPauseTimeout))
				{
					return;
				}
				inputPaused = false;
			}
			if (!inputPaused)
			{
				Inputs();
			}
			Moving();
			Jumping();
			Damage();
			SwitchWeapons();
			Strafing();
			Facing();
			Aiming();
			Rolling();
			Attacking();
		}

		public void PauseInput(float timeout)
		{
			inputPaused = true;
			inputPauseTimeout = Time.time + timeout;
		}

		private void Inputs()
		{
			try
			{
				inputJump = Input.GetButtonDown("Jump");
				isJumpHeld = Input.GetButton("Jump");
				inputLightHit = Input.GetButtonDown("LightHit");
				inputKnockdown = Input.GetButtonDown("Knockdown");
				inputAttackL = Input.GetButtonDown("AttackL");
				inputAttackR = Input.GetButtonDown("AttackR");
				inputSwitchUpDown = Input.GetAxisRaw("SwitchUpDown");
				inputAimBlock = Input.GetAxisRaw("Aim");
				inputAiming = Input.GetButton("Aiming");
				inputHorizontal = Input.GetAxisRaw("Horizontal");
				inputVertical = Input.GetAxisRaw("Vertical");
				inputFace = Input.GetMouseButton(1);
				inputFacingHorizontal = Input.GetAxisRaw("FacingHorizontal");
				inputFacingVertical = Input.GetAxisRaw("FacingVertical");
				inputRoll = Input.GetButtonDown("L3");
				if (rpgCharacterController.HandlerExists(HandlerTypes.SlowTime))
				{
					if (Input.GetKeyDown(KeyCode.T) && !rpgCharacterController.TryStartAction(HandlerTypes.SlowTime, 0.0125f))
					{
						rpgCharacterController.TryEndAction(HandlerTypes.SlowTime);
					}
					if (Input.GetKeyDown(KeyCode.P) && !rpgCharacterController.TryStartAction(HandlerTypes.SlowTime, 0f))
					{
						rpgCharacterController.TryEndAction(HandlerTypes.SlowTime);
					}
				}
			}
			catch (Exception)
			{
				Debug.LogError("Inputs not found! If you are using the InputSystem you need to extract the 'InputSystem - Requires InputSystem Package.unitypackage'.");
				Debug.LogError("Please read Readme in the Documentation folder, or watch https://www.youtube.com/watch?v=ruufqlXrCzU");
			}
		}

		public bool HasMoveInput()
		{
			return moveInput.magnitude > 0.1f;
		}

		public bool HasAimInput()
		{
			if (!inputAiming)
			{
				return inputAimBlock < -0.1f;
			}
			return true;
		}

		public bool HasFacingInput()
		{
			if (!((double)inputFacingHorizontal < -0.05) && !((double)inputFacingHorizontal > 0.05) && !((double)inputFacingVertical < -0.05) && !((double)inputFacingVertical > 0.05))
			{
				return inputFace;
			}
			return true;
		}

		public void Moving()
		{
			moveInput = new Vector3(inputHorizontal, inputVertical, 0f);
			if (HasMoveInput())
			{
				rpgCharacterController.SetMoveInput(moveInput);
			}
			else
			{
				rpgCharacterController.SetMoveInput(Vector3.zero);
			}
		}

		private void Jumping()
		{
			if (rpgCharacterController.HandlerExists(HandlerTypes.Jump))
			{
				Vector3 jumpInput = (isJumpHeld ? Vector3.up : Vector3.zero);
				rpgCharacterController.SetJumpInput(jumpInput);
				if (inputJump && rpgCharacterController.CanStartAction(HandlerTypes.Jump))
				{
					rpgCharacterController.StartAction(HandlerTypes.Jump);
				}
				else if (inputJump && rpgCharacterController.CanStartAction(HandlerTypes.DoubleJump))
				{
					rpgCharacterController.StartAction(HandlerTypes.DoubleJump);
				}
			}
		}

		public void Rolling()
		{
			if (inputRoll && rpgCharacterController.HandlerExists(HandlerTypes.DiveRoll) && rpgCharacterController.CanStartAction(HandlerTypes.DiveRoll))
			{
				rpgCharacterController.StartAction(HandlerTypes.DiveRoll, 1);
			}
		}

		private void Aiming()
		{
			Strafing();
		}

		private void Strafing()
		{
			if (rpgCharacterController.HandlerExists(HandlerTypes.Strafe) && rpgCharacterController.canStrafe)
			{
				if (inputAimBlock < -0.1f || inputAiming)
				{
					rpgCharacterController.TryStartAction(HandlerTypes.Strafe);
				}
				else
				{
					rpgCharacterController.TryEndAction(HandlerTypes.Strafe);
				}
			}
		}

		private void Facing()
		{
			if (!rpgCharacterController.HandlerExists(HandlerTypes.Face) || !rpgCharacterController.canFace)
			{
				return;
			}
			if (HasFacingInput())
			{
				if (inputFace)
				{
					Plane plane = new Plane(Vector3.up, base.transform.position);
					Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
					float enter = 0f;
					if (plane.Raycast(ray, out enter))
					{
						Vector3 point = ray.GetPoint(enter);
						Vector3 faceInput = new Vector3(point.x - base.transform.position.x, base.transform.position.z - point.z, 0f);
						rpgCharacterController.SetFaceInput(faceInput);
					}
				}
				else
				{
					rpgCharacterController.SetFaceInput(new Vector3(inputFacingHorizontal, inputFacingVertical, 0f));
				}
				rpgCharacterController.TryStartAction(HandlerTypes.Face);
			}
			else
			{
				rpgCharacterController.TryEndAction(HandlerTypes.Face);
			}
		}

		private void Attacking()
		{
			if (rpgCharacterController.HandlerExists(HandlerTypes.Attack) && rpgCharacterController.CanStartAction(HandlerTypes.Attack))
			{
				if (inputAttackL)
				{
					rpgCharacterController.StartAction(HandlerTypes.Attack, new AttackContext(HandlerTypes.Attack, Side.Left));
				}
				else if (inputAttackR)
				{
					rpgCharacterController.StartAction(HandlerTypes.Attack, new AttackContext(HandlerTypes.Attack, Side.Right));
				}
			}
		}

		private void Damage()
		{
			if (rpgCharacterController.HandlerExists(HandlerTypes.GetHit) && inputLightHit)
			{
				rpgCharacterController.StartAction(HandlerTypes.GetHit, new HitContext());
			}
			if (rpgCharacterController.HandlerExists(HandlerTypes.Knockdown) && inputKnockdown && rpgCharacterController.CanStartAction(HandlerTypes.Knockdown))
			{
				rpgCharacterController.StartAction(HandlerTypes.Knockdown, new HitContext(1, Vector3.back));
			}
		}

		private void SwitchWeapons()
		{
			if (!rpgCharacterController.HandlerExists(HandlerTypes.SwitchWeapon) || !rpgCharacterController.CanStartAction(HandlerTypes.SwitchWeapon))
			{
				return;
			}
			bool flag = false;
			SwitchWeaponContext switchWeaponContext = new SwitchWeaponContext();
			Weapon weapon = Weapon.Unarmed;
			if (Mathf.Abs(inputSwitchUpDown) > 0.1f)
			{
				Weapon[] array = new Weapon[1] { Weapon.TwoHandSword };
				if (Array.IndexOf(array, rpgCharacterController.rightWeapon) == -1)
				{
					weapon = array[0];
				}
				else
				{
					int num = Array.IndexOf(array, rpgCharacterController.rightWeapon);
					if (inputSwitchUpDown < -0.1f)
					{
						num = (num - 1 + array.Length) % array.Length;
					}
					else if (inputSwitchUpDown > 0.1f)
					{
						num = (num + 1) % array.Length;
					}
					weapon = array[num];
				}
				flag = true;
				switchWeaponContext.type = HandlerTypes.Switch;
				switchWeaponContext.side = "None";
				switchWeaponContext.leftWeapon = Weapon.Unarmed;
				switchWeaponContext.rightWeapon = weapon;
			}
			if (flag)
			{
				rpgCharacterController.StartAction(HandlerTypes.SwitchWeapon, switchWeaponContext);
			}
		}
	}
}
