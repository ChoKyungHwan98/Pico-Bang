using RPGCharacterAnims.Extensions;
using RPGCharacterAnims.Lookups;
using UnityEngine;

namespace RPGCharacterAnims
{
	public class AnimationData
	{
		public static AnimatorWeapon ConvertToAnimatorWeapon(Weapon leftWeapon, Weapon rightWeapon)
		{
			if (rightWeapon.Is2HandedWeapon())
			{
				return (AnimatorWeapon)rightWeapon;
			}
			if (rightWeapon.HasNoWeapon() && leftWeapon.HasNoWeapon())
			{
				return (AnimatorWeapon)rightWeapon;
			}
			return AnimatorWeapon.UNARMED;
		}

		public static float AttackDuration(Side attackSide, Weapon weapon, int attackNumber)
		{
			float result = 1f;
			switch (attackSide)
			{
			case Side.None:
				if (weapon == Weapon.TwoHandSword)
				{
					result = 1.1f;
				}
				else
				{
					Debug.LogError("RPG Character: no weapon number " + weapon.ToString() + " for Side 0");
				}
				break;
			case Side.Left:
				if (weapon == Weapon.Unarmed)
				{
					result = 0.75f;
				}
				else
				{
					Debug.LogError("RPG Character: no weapon number " + weapon.ToString() + " for Side 1 (Left)");
				}
				break;
			case Side.Right:
				if (weapon == Weapon.Unarmed)
				{
					result = 0.75f;
				}
				else
				{
					Debug.LogError("RPG Character: no weapon number " + weapon.ToString() + " for Side 2 (Right)");
				}
				break;
			}
			return result;
		}

		public static float SheathDuration(Side attackSide, Weapon weapon)
		{
			if (weapon.HasNoWeapon())
			{
				return 0f;
			}
			if (weapon.Is2HandedWeapon())
			{
				return 1.2f;
			}
			return 1.05f;
		}

		public static int RandomAttackNumber(Side sideType, Weapon weapon)
		{
			switch (sideType)
			{
			case Side.None:
				if (weapon == Weapon.TwoHandSword)
				{
					return (int)AnimationVariations.TwoHandedSwordAttacks.TakeRandom();
				}
				Debug.LogError($"RPG Character: no weapon number {weapon} for Side 0");
				break;
			case Side.Left:
				if (weapon == Weapon.Unarmed)
				{
					return (int)AnimationVariations.UnarmedLeftAttacks.TakeRandom();
				}
				Debug.LogError($"RPG Character: no weapon number {weapon} for Side 1 (Left)");
				break;
			case Side.Right:
				if (weapon == Weapon.Unarmed)
				{
					return (int)AnimationVariations.UnarmedRightAttacks.TakeRandom();
				}
				Debug.LogError($"RPG Character: no weapon number {weapon} for Side 2 (Right)");
				break;
			}
			return 1;
		}

		public static Vector3 HitDirection(HitType hitType)
		{
			return hitType switch
			{
				HitType.Back1 => Vector3.forward, 
				HitType.Left1 => Vector3.right, 
				HitType.Right1 => Vector3.left, 
				_ => Vector3.back, 
			};
		}

		public static Vector3 HitDirection(KnockbackType hitType)
		{
			_ = hitType - 1;
			_ = 1;
			return Vector3.back;
		}

		public static Vector3 HitDirection(KnockdownType hitType)
		{
			return Vector3.back;
		}
	}
}
