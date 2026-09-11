using System;
using System.Collections;
using RPGCharacterAnims.Actions;
using RPGCharacterAnims.Extensions;
using RPGCharacterAnims.Lookups;
using UnityEngine;

namespace RPGCharacterAnims
{
	public class RPGCharacterWeaponController : MonoBehaviour
	{
		private RPGCharacterController rpgCharacterController;

		private Animator animator;

		private CoroutineQueue coroQueue;

		[Header("Debug Options")]
		public bool debugWalkthrough = true;

		public bool debugSwitchWeaponContext = true;

		public bool debugDoWeaponSwitch = true;

		public bool debugWeaponVisibility = true;

		public bool debugSetAnimator = true;

		[HideInInspector]
		private bool isWeaponSwitching;

		[Header("Weapon Models")]
		public GameObject twoHandSword;

		private void Awake()
		{
			coroQueue = new CoroutineQueue(1u, base.StartCoroutine);
			rpgCharacterController = GetComponent<RPGCharacterController>();
			rpgCharacterController.SetHandler(HandlerTypes.SwitchWeapon, new SwitchWeapon());
			animator = GetComponentInChildren<Animator>();
			StartCoroutine(_HideAllWeapons(timed: false, resetToUnarmed: false));
		}

		private void Start()
		{
			animator.gameObject.GetComponent<RPGCharacterAnimatorEvents>().OnWeaponSwitch.AddListener(WeaponSwitch);
		}

		public void AddCallback(Action callback)
		{
			coroQueue.RunCallback(callback);
		}

		public void UnsheathWeapon(Weapon weapon)
		{
			if (debugWalkthrough)
			{
				Debug.Log("UnsheathWeapon:" + weapon);
			}
			coroQueue.Run(_UnSheathWeapon(weapon));
		}

		private IEnumerator _UnSheathWeapon(Weapon weapon)
		{
			if (debugWalkthrough)
			{
				Debug.Log($"UnsheathWeapon - weapon:{weapon}");
			}
			isWeaponSwitching = true;
			animator.GetInteger(AnimationParameters.Weapon);
			Weapon currentWeaponType = (Weapon)animator.GetInteger(AnimationParameters.Weapon);
			if (!weapon.Is2HandedWeapon())
			{
				yield break;
			}
			if (debugWalkthrough)
			{
				Debug.Log($"Switching to 2Handed Weapon:{weapon}");
			}
			if (currentWeaponType.Is2HandedWeapon())
			{
				if (debugWalkthrough)
				{
					Debug.Log("Switching from 2Handed weapon.");
				}
				DoWeaponSwitch(0, weapon, weapon.ToAnimatorWeapon(), Side.Unchanged, sheath: false);
				yield return new WaitForSeconds(0.75f);
				SetWeaponWithDebug(weapon.ToAnimatorWeapon(), -2, currentWeaponType, Weapon.Unarmed, Side.Unchanged);
			}
			else
			{
				DoWeaponSwitch(0, weapon, weapon.ToAnimatorWeapon(), Side.Unchanged, sheath: false);
				yield return new WaitForSeconds(0.75f);
				SetWeaponWithDebug(weapon.ToAnimatorWeapon(), -2, weapon, Weapon.Unarmed, Side.Unchanged);
			}
		}

		public void SheathWeapon(Weapon fromWeapon, Weapon toWeapon)
		{
			if (debugWalkthrough)
			{
				Debug.Log($"SheathWeapon - fromWeapon:{fromWeapon}  toWeapon:{toWeapon}");
			}
			coroQueue.Run(_SheathWeapon(fromWeapon, toWeapon));
		}

		public IEnumerator _SheathWeapon(Weapon weaponToSheath, Weapon weaponToUnsheath)
		{
			if (debugWalkthrough)
			{
				Debug.Log($"Sheath Weapon - weaponToSheath:{weaponToSheath}  weaponToUnsheath:{weaponToUnsheath}");
			}
			animator.GetInteger(AnimationParameters.Weapon);
			AnimatorWeapon integer = (AnimatorWeapon)animator.GetInteger(AnimationParameters.Weapon);
			isWeaponSwitching = true;
			if (!weaponToUnsheath.HasNoWeapon())
			{
				yield break;
			}
			if (debugWalkthrough)
			{
				Debug.Log("Putting away a weapon.");
			}
			if (rpgCharacterController.rightWeapon.HasEquippedWeapon() || rpgCharacterController.leftWeapon.HasEquippedWeapon())
			{
				if (debugWalkthrough)
				{
					Debug.Log("Sheath 2Handed weapon.");
				}
				DoWeaponSwitch((int)weaponToUnsheath, weaponToSheath, integer, Side.Unchanged, sheath: true);
				yield return new WaitForSeconds(0.55f);
				SetWeaponWithDebug(weaponToUnsheath.ToAnimatorWeapon(), -2, Weapon.Unarmed, Weapon.Unarmed, Side.Unchanged);
			}
		}

		public void InstantWeaponSwitch(Weapon weapon)
		{
			coroQueue.Run(_InstantWeaponSwitch(weapon));
		}

		public IEnumerator _InstantWeaponSwitch(Weapon weapon)
		{
			if (debugWalkthrough)
			{
				Debug.Log($"_InstantWeaponSwitch:{weapon}");
			}
			animator.SetAnimatorTrigger(AnimatorTrigger.InstantSwitchTrigger);
			rpgCharacterController.SetIKOff();
			StartCoroutine(_HideAllWeapons(timed: false, resetToUnarmed: false));
			if (weapon.Is2HandedWeapon())
			{
				if (debugWalkthrough)
				{
					Debug.Log($"InstantSwitch to 2HandedWeapon - weapon:{weapon}");
				}
				animator.SetInteger(AnimationParameters.Weapon, (int)weapon);
				rpgCharacterController.rightWeapon = Weapon.Unarmed;
				rpgCharacterController.leftWeapon = Weapon.Unarmed;
				animator.SetInteger(AnimationParameters.LeftWeapon, 0);
				animator.SetInteger(AnimationParameters.RightWeapon, 0);
				StartCoroutine(_HideAllWeapons(timed: false, resetToUnarmed: false));
				StartCoroutine(_WeaponVisibility(weapon, visible: true));
				if (weapon.IsIKWeapon())
				{
					rpgCharacterController.SetIKOn((Weapon)animator.GetInteger(AnimationParameters.Weapon));
				}
			}
			else
			{
				animator.SetInteger(AnimationParameters.Weapon, (int)weapon);
				rpgCharacterController.rightWeapon = Weapon.Unarmed;
				rpgCharacterController.leftWeapon = Weapon.Unarmed;
				animator.SetInteger(AnimationParameters.LeftWeapon, 0);
				animator.SetInteger(AnimationParameters.RightWeapon, 0);
				animator.SetInteger(AnimationParameters.Side, 0);
				StartCoroutine(_HideAllWeapons(timed: false, resetToUnarmed: false));
			}
			yield return null;
		}

		private void DoWeaponSwitch(int weaponSwitch, Weapon weapon, AnimatorWeapon animatorWeapon, Side side, bool sheath)
		{
			if (debugDoWeaponSwitch)
			{
				Debug.Log($"DO WEAPON SWITCH - weaponSwitch:{weaponSwitch}  weapon:{weapon}  animatorWeapon:{animatorWeapon}  side:{side}  sheath:{sheath}");
			}
			rpgCharacterController.Lock(!rpgCharacterController.isMoving, lockAction: true, timed: true, 0f, 1f);
			animator.SetInteger(AnimationParameters.WeaponSwitch, weaponSwitch);
			animator.SetInteger(AnimationParameters.Weapon, (int)animatorWeapon);
			if (side != Side.Unchanged)
			{
				animator.SetInteger(AnimationParameters.Side, (int)side);
			}
			if (sheath)
			{
				animator.SetAnimatorTrigger(AnimatorTrigger.WeaponSheathTrigger);
				StartCoroutine(_WeaponVisibility(weapon, visible: false));
				if (rpgCharacterController.ikHands != null)
				{
					rpgCharacterController.ikHands.BlendIK(blendOn: false, 0f, 0.2f, weapon);
				}
			}
			else
			{
				animator.SetAnimatorTrigger(AnimatorTrigger.WeaponUnsheathTrigger);
				StartCoroutine(_WeaponVisibility(weapon, visible: true));
				if (rpgCharacterController.ikHands != null && weapon.IsIKWeapon())
				{
					rpgCharacterController.ikHands.BlendIK(blendOn: true, 0.75f, 1f, weapon);
				}
			}
		}

		private void SetWeaponWithDebug(AnimatorWeapon animatorWeapon, int weaponSwitch, Weapon leftWeapon, Weapon rightWeapon, Side weaponSide)
		{
			if (debugSetAnimator)
			{
				Debug.Log($"SET ANIMATOR - Weapon:{animatorWeapon}  WeaponSwitch:{weaponSwitch}  Lweapon:{leftWeapon}  Rweapon:{rightWeapon}  Weaponside:{weaponSide}");
			}
			animator.SetWeapons(animatorWeapon, weaponSwitch, leftWeapon, rightWeapon, weaponSide);
		}

		public void WeaponSwitch()
		{
			if (isWeaponSwitching)
			{
				isWeaponSwitching = false;
			}
		}

		public void SafeSetVisibility(GameObject weaponObject, bool visibility)
		{
			if (weaponObject != null)
			{
				weaponObject.SetActive(visibility);
			}
		}

		public void HideAllWeapons()
		{
			StartCoroutine(_HideAllWeapons(timed: false, resetToUnarmed: true));
		}

		public IEnumerator _HideAllWeapons(bool timed, bool resetToUnarmed)
		{
			if (timed)
			{
				while (!isWeaponSwitching)
				{
					yield return null;
				}
			}
			if (resetToUnarmed)
			{
				animator.SetInteger(AnimationParameters.Weapon, 0);
				rpgCharacterController.rightWeapon = Weapon.Unarmed;
				rpgCharacterController.leftWeapon = Weapon.Unarmed;
				StartCoroutine(_WeaponVisibility(rpgCharacterController.leftWeapon, visible: false));
				animator.SetInteger(AnimationParameters.RightWeapon, 0);
				animator.SetInteger(AnimationParameters.LeftWeapon, 0);
				animator.SetSide(Side.None);
			}
			SafeSetVisibility(twoHandSword, visibility: false);
		}

		public IEnumerator _WeaponVisibility(Weapon weaponNumber, bool visible)
		{
			if (debugWeaponVisibility)
			{
				Debug.Log($"WeaponVisibility:{weaponNumber}   Visible:{visible}");
			}
			while (isWeaponSwitching)
			{
				yield return null;
			}
			if (weaponNumber == Weapon.TwoHandSword)
			{
				SafeSetVisibility(twoHandSword, visible);
			}
			yield return null;
		}

		public void SyncWeaponVisibility()
		{
			coroQueue.Run(_SyncWeaponVisibility());
		}

		private IEnumerator _SyncWeaponVisibility()
		{
			while (isWeaponSwitching && (!rpgCharacterController.canAction || !rpgCharacterController.canMove))
			{
				yield return null;
			}
			StopCoroutine("_HideAllWeapons");
			StopCoroutine("_WeaponVisibility");
			SafeSetVisibility(twoHandSword, visibility: false);
			if (rpgCharacterController.rightWeapon == Weapon.TwoHandSword)
			{
				SafeSetVisibility(twoHandSword, visibility: true);
			}
		}
	}
}
