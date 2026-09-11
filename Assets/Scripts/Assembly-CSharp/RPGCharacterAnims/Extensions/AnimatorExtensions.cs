using System;
using RPGCharacterAnims.Lookups;
using UnityEngine;

namespace RPGCharacterAnims.Extensions
{
	public static class AnimatorExtensions
	{
		public static void SetAnimatorTrigger(this Animator animator, AnimatorTrigger trigger)
		{
			Debug.Log($"SetAnimatorTrigger: {trigger} - {(int)trigger}");
			animator.SetInteger(AnimationParameters.TriggerNumber, (int)trigger);
			animator.SetTrigger(AnimationParameters.Trigger);
		}

		public static void SetActionTrigger(this Animator animator, AnimatorTrigger trigger, int actionNumber)
		{
			Debug.Log($"SetActionTrigger: {trigger} - {(int)trigger} - action {actionNumber}");
			animator.SetInteger(AnimationParameters.Action, actionNumber);
			animator.SetAnimatorTrigger(trigger);
		}

		public static void SetSide(this Animator animator, Side side)
		{
			animator.SetInteger(AnimationParameters.Side, (int)side);
		}

		public static void TriggerDiveRoll(this Animator animator, DiveRollType rollType)
		{
			animator.SetActionTrigger(AnimatorTrigger.DiveRollTrigger, (int)rollType);
		}

		public static void TriggerKnockback(this Animator animator, KnockbackType knockbackType)
		{
			animator.SetActionTrigger(AnimatorTrigger.KnockbackTrigger, (int)knockbackType);
		}

		public static void TriggerKnockdown(this Animator animator, KnockdownType knockdownType)
		{
			animator.SetActionTrigger(AnimatorTrigger.KnockdownTrigger, (int)knockdownType);
		}

		public static void TriggerGettingHit(this Animator animator, HitType hitType)
		{
			animator.SetActionTrigger(AnimatorTrigger.GetHitTrigger, (int)hitType);
		}

		public static void TriggerGettingHit(this Animator animator, int hitType)
		{
			animator.SetActionTrigger(AnimatorTrigger.GetHitTrigger, hitType);
		}

		[Obsolete("This is for backwards compatibility with older trigger names.")]
		public static void LegacySetAnimationTrigger(this Animator animator, string trigger)
		{
			AnimatorTrigger animatorTrigger = (AnimatorTrigger)Enum.Parse(typeof(AnimatorTrigger), trigger);
			Debug.Log($"LegacyAnimationTrigger: {animatorTrigger} - {animatorTrigger}");
			animator.SetAnimatorTrigger(animatorTrigger);
		}

		public static void DebugAnimatorParameters(this Animator animator)
		{
			Debug.Log("ANIMATOR SETTINGS---------------------------");
			Debug.Log("Moving: " + animator.GetBool(AnimationParameters.Moving));
			Debug.Log("Aiming: " + animator.GetBool(AnimationParameters.Aiming));
			Debug.Log($"Weapon: {animator.GetInteger(AnimationParameters.Weapon)}");
			Debug.Log($"WeaponSwitch: {animator.GetInteger(AnimationParameters.WeaponSwitch)}");
			Debug.Log($"Side: {animator.GetInteger(AnimationParameters.Side)}");
			Debug.Log($"LeftWeapon: {animator.GetInteger(AnimationParameters.LeftWeapon)}");
			Debug.Log($"RightWeapon: {animator.GetInteger(AnimationParameters.RightWeapon)}");
			Debug.Log("Jumping: " + animator.GetInteger(AnimationParameters.Jumping));
			Debug.Log("Action: " + animator.GetInteger(AnimationParameters.Action));
			Debug.Log("Velocity X: " + animator.GetFloat(AnimationParameters.VelocityX));
			Debug.Log("Velocity Z: " + animator.GetFloat(AnimationParameters.VelocityZ));
		}

		public static void SetWeapons(this Animator animator, AnimatorWeapon animatorWeapon, int weaponSwitch, Weapon leftWeapon, Weapon rightWeapon, Side weaponSide)
		{
			animator.SetInteger(AnimationParameters.Weapon, (int)animatorWeapon);
			if (weaponSwitch != -2)
			{
				animator.SetInteger(AnimationParameters.WeaponSwitch, weaponSwitch);
			}
			if (leftWeapon != Weapon.Unarmed)
			{
				animator.SetInteger(AnimationParameters.LeftWeapon, (int)leftWeapon);
			}
			if (rightWeapon != Weapon.Unarmed)
			{
				animator.SetInteger(AnimationParameters.RightWeapon, (int)rightWeapon);
			}
			if (weaponSide != Side.Unchanged)
			{
				animator.SetInteger(AnimationParameters.Side, (int)weaponSide);
			}
		}
	}
}
