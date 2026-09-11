using RPGCharacterAnims.Lookups;

namespace RPGCharacterAnims.Extensions
{
	public static class WeaponExtensions
	{
		public static bool Is2HandedWeapon(this Weapon weapon)
		{
			return weapon == Weapon.TwoHandSword;
		}

		public static bool HasEquippedWeapon(this Weapon weapon)
		{
			return weapon != Weapon.Unarmed;
		}

		public static bool HasNoWeapon(this Weapon weapon)
		{
			return weapon == Weapon.Unarmed;
		}

		public static bool IsIKWeapon(this Weapon weapon)
		{
			return weapon == Weapon.TwoHandSword;
		}

		public static AnimatorWeapon ToAnimatorWeapon(this Weapon weapon)
		{
			if (weapon == Weapon.Unarmed || weapon == Weapon.TwoHandSword)
			{
				return (AnimatorWeapon)weapon;
			}
			return AnimatorWeapon.UNARMED;
		}

		public static bool Is2HandedAnimWeapon(this AnimatorWeapon weapon)
		{
			return weapon == AnimatorWeapon.TWOHANDSWORD;
		}

		public static bool HasNoAnimWeapon(this AnimatorWeapon weapon)
		{
			return weapon == AnimatorWeapon.UNARMED;
		}
	}
}
