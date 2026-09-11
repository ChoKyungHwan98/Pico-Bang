using RPGCharacterAnims.Lookups;

namespace RPGCharacterAnims.Actions
{
	public class SwitchWeaponContext
	{
		public string type;

		public string side;

		public Weapon rightWeapon;

		public Weapon leftWeapon;

		public SwitchWeaponContext()
		{
			type = "Instant";
			side = "None";
			rightWeapon = Weapon.Unarmed;
			leftWeapon = Weapon.Unarmed;
		}

		public SwitchWeaponContext(string type, string side, Weapon rightWeapon = Weapon.Unarmed, Weapon leftWeapon = Weapon.Unarmed)
		{
			this.type = type;
			this.side = side;
			this.rightWeapon = rightWeapon;
			this.leftWeapon = leftWeapon;
		}

		public void LowercaseStrings()
		{
			type = type.ToLower();
			side = side.ToLower();
		}
	}
}
