using RPGCharacterAnims.Actions;
using RPGCharacterAnims.Extensions;
using RPGCharacterAnims.Lookups;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RPGCharacterAnims
{
	public class GUIControls : MonoBehaviour
	{
		private RPGCharacterController rpgCharacterController;

		private RPGCharacterWeaponController rpgCharacterWeaponController;

		private float idleStatic;

		private bool useInstant;

		private bool useNavigation;


		public GameObject nav;

		private void Start()
		{
			rpgCharacterController = GetComponent<RPGCharacterController>();
			rpgCharacterWeaponController = GetComponent<RPGCharacterWeaponController>();
		}

		private void OnGUI()
		{
			if (rpgCharacterController.maintainingGround)
			{
				Navigation();
			}
			if (!rpgCharacterController.maintainingGround)
			{
				Jumping();
				return;
			}
			if (rpgCharacterController.canAction)
			{
				Idle();
				Attacks();
				Damage();
				DiveRoll();
				WeaponSwitching();
			}
			DebugRPGCharacter();
		}

		private void Idle()
		{
			GUI.Button(new Rect(540f, 140f, 60f, 30f), "Idle");
			idleStatic = GUI.HorizontalSlider(new Rect(540f, 170f, 60f, 30f), idleStatic, 0f, 1f);
			rpgCharacterController.animator.SetFloat(AnimationParameters.Idle, idleStatic);
		}

		private void Navigation()
		{
			if (!rpgCharacterController.HandlerExists(HandlerTypes.Navigation))
			{
				return;
			}
			useNavigation = GUI.Toggle(new Rect(550f, 105f, 100f, 30f), useNavigation, "Navigation");
			Transform child = nav.transform.GetChild(0);
			if (useNavigation)
			{
				child.GetComponent<MeshRenderer>().enabled = true;
				child.GetChild(0).GetComponent<MeshRenderer>().enabled = true;
				if (Physics.Raycast(Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue()), out var hitInfo, 100f))
				{
					nav.transform.position = hitInfo.point;
					if (Mouse.current.leftButton.wasPressedThisFrame)
					{
						rpgCharacterController.StartAction(HandlerTypes.Navigation, hitInfo.point);
					}
				}
			}
			else
			{
				child.GetComponent<MeshRenderer>().enabled = false;
				child.GetChild(0).GetComponent<MeshRenderer>().enabled = false;
				if (rpgCharacterController.CanEndAction(HandlerTypes.Navigation))
				{
					rpgCharacterController.EndAction(HandlerTypes.Navigation);
				}
			}
		}

		private void Attacks()
		{
			if (!rpgCharacterController.HandlerExists(HandlerTypes.Attack))
			{
				return;
			}
			if (rpgCharacterController.CanEndAction(HandlerTypes.Attack) && GUI.Button(new Rect(235f, 85f, 100f, 30f), "End Special"))
			{
				rpgCharacterController.EndAction(HandlerTypes.Attack);
			}
			if (rpgCharacterController.CanStartAction(HandlerTypes.Attack))
			{
				if (rpgCharacterController.leftWeapon == Weapon.Unarmed && rpgCharacterController.rightWeapon == Weapon.Unarmed && GUI.Button(new Rect(25f, 85f, 100f, 30f), "Attack L"))
				{
					rpgCharacterController.StartAction(HandlerTypes.Attack, new AttackContext("Attack", Side.Left));
				}
				if (rpgCharacterController.rightWeapon == Weapon.Unarmed && rpgCharacterController.leftWeapon == Weapon.Unarmed && GUI.Button(new Rect(130f, 85f, 100f, 30f), "Attack R"))
				{
					rpgCharacterController.StartAction(HandlerTypes.Attack, new AttackContext("Attack", Side.Right));
				}
				if (rpgCharacterController.hasTwoHandedWeapon && GUI.Button(new Rect(130f, 85f, 100f, 30f), "Attack"))
				{
					rpgCharacterController.StartAction(HandlerTypes.Attack, new AttackContext("Attack", Side.None));
				}
			}
		}

		private void Damage()
		{
			if (rpgCharacterController.HandlerExists(HandlerTypes.GetHit) && rpgCharacterController.CanStartAction(HandlerTypes.GetHit) && GUI.Button(new Rect(30f, 240f, 100f, 30f), "Get Hit"))
			{
				rpgCharacterController.StartAction(HandlerTypes.GetHit, new HitContext());
			}
			if (rpgCharacterController.HandlerExists(HandlerTypes.Knockback) && rpgCharacterController.CanStartAction(HandlerTypes.Knockback))
			{
				if (GUI.Button(new Rect(130f, 240f, 100f, 30f), "Knockback1"))
				{
					rpgCharacterController.StartAction(HandlerTypes.Knockback, new HitContext(1, Vector3.back));
				}
				if (GUI.Button(new Rect(230f, 240f, 100f, 30f), "Knockback2"))
				{
					rpgCharacterController.StartAction(HandlerTypes.Knockback, new HitContext(2, Vector3.back));
				}
			}
			if (rpgCharacterController.HandlerExists(HandlerTypes.Knockdown) && rpgCharacterController.CanStartAction(HandlerTypes.Knockdown) && GUI.Button(new Rect(130f, 270f, 100f, 30f), "Knockdown"))
			{
				rpgCharacterController.StartAction(HandlerTypes.Knockdown, new HitContext(1, Vector3.back));
			}
		}

		private void DiveRoll()
		{
			if (rpgCharacterController.HandlerExists(HandlerTypes.DiveRoll) && rpgCharacterController.CanStartAction(HandlerTypes.DiveRoll) && GUI.Button(new Rect(445f, 75f, 100f, 30f), "Dive Roll"))
			{
				rpgCharacterController.StartAction(HandlerTypes.DiveRoll, DiveRollType.DiveRoll1);
			}
		}

		private void Jumping()
		{
			if (rpgCharacterController.HandlerExists(HandlerTypes.Jump))
			{
				if (rpgCharacterController.CanStartAction(HandlerTypes.Jump) && GUI.Button(new Rect(25f, 175f, 100f, 30f), "Jump"))
				{
					rpgCharacterController.SetJumpInput(Vector3.up);
					rpgCharacterController.StartAction(HandlerTypes.Jump);
				}
				if (rpgCharacterController.CanStartAction(HandlerTypes.DoubleJump) && GUI.Button(new Rect(25f, 175f, 100f, 30f), "Jump Flip"))
				{
					rpgCharacterController.SetJumpInput(Vector3.up);
					rpgCharacterController.StartAction(HandlerTypes.DoubleJump);
				}
			}
		}

		private void DebugRPGCharacter()
		{
			if (GUI.Button(new Rect(600f, 20f, 120f, 30f), "Debug Controller"))
			{
				rpgCharacterController.DebugController();
			}
			if (GUI.Button(new Rect(600f, 50f, 120f, 30f), "Debug Animator"))
			{
				rpgCharacterController.animator.DebugAnimatorParameters();
			}
		}

		private void WeaponSwitching()
		{
			if (!rpgCharacterController.HandlerExists(HandlerTypes.SwitchWeapon))
			{
				return;
			}
			bool flag = false;
			SwitchWeaponContext switchWeaponContext = new SwitchWeaponContext();
			if ((rpgCharacterController.rightWeapon != Weapon.Unarmed || rpgCharacterController.leftWeapon != Weapon.Unarmed) && GUI.Button(new Rect(1115f, 280f, 100f, 30f), "Unarmed"))
			{
				flag = true;
				switchWeaponContext.type = "Switch";
				switchWeaponContext.side = "Both";
				switchWeaponContext.leftWeapon = Weapon.Unarmed;
				switchWeaponContext.rightWeapon = Weapon.Unarmed;
			}
			int num = 310;
			Weapon[] twoHandedWeapons = WeaponGroupings.TwoHandedWeapons;
			for (int i = 0; i < twoHandedWeapons.Length; i++)
			{
				Weapon weapon = twoHandedWeapons[i];
				if (rpgCharacterController.rightWeapon != weapon)
				{
					string text = weapon.ToString();
					if (text.StartsWith("TwoHand"))
					{
						text = text.Replace("TwoHand", "2H ");
					}
					if (GUI.Button(new Rect(1115f, num, 100f, 30f), text))
					{
						flag = true;
						switchWeaponContext.type = "Switch";
						switchWeaponContext.side = "None";
						switchWeaponContext.leftWeapon = Weapon.Unarmed;
						switchWeaponContext.rightWeapon = weapon;
					}
				}
				num += 30;
			}
			useInstant = GUI.Toggle(new Rect(1000f, 310f, 100f, 30f), useInstant, "Instant");
			if (useInstant)
			{
				switchWeaponContext.type = "Instant";
			}
			if (flag)
			{
				rpgCharacterController.TryStartAction(HandlerTypes.SwitchWeapon, switchWeaponContext);
			}
		}
	}
}
