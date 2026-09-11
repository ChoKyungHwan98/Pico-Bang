using RPGCharacterAnims.Extensions;
using RPGCharacterAnims.Lookups;
using UnityEngine;

namespace RPGCharacterAnims.Actions
{
	public class SwitchWeapon : BaseActionHandler<SwitchWeaponContext>
	{
		public override bool CanStartAction(RPGCharacterController controller)
		{
			return !IsActive();
		}

		public override bool CanEndAction(RPGCharacterController controller)
		{
			return IsActive();
		}

		protected override void _StartAction(RPGCharacterController controller, SwitchWeaponContext context)
		{
			RPGCharacterWeaponController component = controller.GetComponent<RPGCharacterWeaponController>();
			if (component == null)
			{
				EndAction(controller);
				return;
			}
			context.LowercaseStrings();
			bool flag = false;
			bool flag2 = false;
			bool flag3 = false;
			Weapon rightWeapon = controller.rightWeapon;
			Weapon weapon = context.rightWeapon;
			bool flag4 = false;
			bool sheathLeft = false;
			bool unsheathLeft = false;
			Weapon leftWeapon = controller.leftWeapon;
			Weapon weapon2 = context.leftWeapon;
			switch (context.side)
			{
			case "none":
			case "right":
				flag = true;
				if (weapon.Is2HandedWeapon() && !leftWeapon.HasNoWeapon())
				{
					flag4 = true;
					weapon2 = Weapon.Unarmed;
				}
				break;
			case "both":
				flag4 = true;
				flag = true;
				break;
			}
			if (context.type == "sheath")
			{
				weapon2 = ((!(context.side == "left") && !(context.side == "dual") && !(context.side == "both")) ? leftWeapon : Weapon.Unarmed);
				weapon = ((!(context.side == "none") && !(context.side == "right") && !(context.side == "dual") && !(context.side == "both")) ? rightWeapon : Weapon.Unarmed);
			}
			AnimationData.ConvertToAnimatorWeapon(controller.leftWeapon, controller.rightWeapon);
			if (context.type == "switch")
			{
				sheathLeft = flag4 && leftWeapon != weapon2 && !leftWeapon.HasNoWeapon();
				flag2 = flag && rightWeapon != weapon && !rightWeapon.HasNoWeapon();
				unsheathLeft = flag4 && leftWeapon != weapon2 && !weapon2.HasNoWeapon();
				flag3 = flag && rightWeapon != weapon && !weapon.HasNoWeapon();
			}
			if (context.type == "sheath" || context.type == "switch")
			{
				sheathLeft = flag4 && leftWeapon != weapon2 && !leftWeapon.HasNoWeapon();
				flag2 = flag && rightWeapon != weapon && !rightWeapon.HasNoWeapon();
			}
			if (context.type == "unsheath" || context.type == "switch")
			{
				unsheathLeft = flag4 && leftWeapon != weapon2 && !weapon2.HasNoWeapon();
				flag3 = flag && rightWeapon != weapon && !weapon.HasNoWeapon();
			}
			DebugSwitchWeapon(component, context, flag, flag4, flag2, sheathLeft, flag3, unsheathLeft, rightWeapon, weapon, leftWeapon, weapon2);
			if (context.type == "instant")
			{
				Debug.Log("Instant Switch");
				if (flag4 & flag)
				{
					component.InstantWeaponSwitch(weapon);
				}
				else if (flag4)
				{
					component.InstantWeaponSwitch(weapon2);
				}
				else if (flag)
				{
					component.InstantWeaponSwitch(weapon);
				}
			}
			else
			{
				if (flag2)
				{
					Debug.Log("Sheath Right - fromRightAnim: " + rightWeapon.ToString() + " > toRightAnim: " + weapon);
					component.SheathWeapon(rightWeapon, weapon);
				}
				if (flag3)
				{
					Debug.Log("Unsheath Right:" + weapon);
					component.UnsheathWeapon(weapon);
				}
			}
			EndSwitch(controller, component, flag, weapon, flag4, weapon2);
		}

		private void EndSwitch(RPGCharacterController controller, RPGCharacterWeaponController weaponController, bool changeRight, Weapon toRightWeapon, bool changeLeft, Weapon toLeftWeapon)
		{
			weaponController.AddCallback(() =>
			{
				if (changeLeft)
				{
					controller.leftWeapon = toLeftWeapon;
				}
				if (changeRight)
				{
					controller.rightWeapon = toRightWeapon;
				}
				weaponController.SyncWeaponVisibility();
				EndAction(controller);
			});
		}

		private static void DebugSwitchWeapon(RPGCharacterWeaponController weaponController, SwitchWeaponContext context, bool changeRight, bool changeLeft, bool sheathRight, bool sheathLeft, bool unsheathRight, bool unsheathLeft, Weapon fromRightWeapon, Weapon toRightWeapon, Weapon fromLeftWeapon, Weapon toLeftWeapon)
		{
			if (weaponController.debugSwitchWeaponContext)
			{
				Debug.Log("===SwitchWeaponContext===");
				Debug.Log($"leftWeapon:{context.leftWeapon}   rightWeapon:{context.rightWeapon}   " + "side:" + context.side + "    type:" + context.type + "    " + $"changeLeft:{changeLeft}    changeRight:{changeRight}    sheathRight:{sheathRight}    sheathLeft:{sheathLeft}");
				Debug.Log($"fromRightWeapon:{fromRightWeapon}   toRightWeapon:{toRightWeapon}   " + $"fromLeftWeapon:{fromLeftWeapon}    toLeftWeapon:{toLeftWeapon}");
			}
		}

		protected override void _EndAction(RPGCharacterController controller)
		{
		}
	}
}
