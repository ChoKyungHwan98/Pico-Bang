using RPGCharacterAnims.Extensions;
using RPGCharacterAnims.Lookups;

namespace RPGCharacterAnims.Actions
{
	public class Attack : BaseActionHandler<AttackContext>
	{
		public override bool CanStartAction(RPGCharacterController controller)
		{
			if (!active)
			{
				return controller.canAction;
			}
			return false;
		}

		public override bool CanEndAction(RPGCharacterController controller)
		{
			return active;
		}

		protected override void _StartAction(RPGCharacterController controller, AttackContext context)
		{
			Side side = Side.None;
			int num = context.number;
			Weapon weapon = controller.rightWeapon;
			float num2 = 0f;
			if (context.Side == Side.Right && weapon.Is2HandedWeapon())
			{
				context.Side = Side.None;
			}
			switch (context.Side)
			{
			case Side.None:
				side = context.Side;
				weapon = controller.rightWeapon;
				break;
			case Side.Left:
				side = context.Side;
				weapon = controller.leftWeapon;
				break;
			case Side.Right:
				side = context.Side;
				weapon = controller.rightWeapon;
				break;
			}
			if (num == -1)
			{
				string type = context.type;
				if (!(type == "Attack"))
				{
					if (type == "Special")
					{
						num = 1;
					}
				}
				else
				{
					num = AnimationData.RandomAttackNumber(side, weapon);
				}
			}
			num2 = AnimationData.AttackDuration(side, weapon, num);
			if (controller.isMoving)
			{
				controller.RunningAttack(side, leftWeapon: false, rightWeapon: false, controller.hasTwoHandedWeapon);
				EndAction(controller);
			}
			else if (context.type == "Attack")
			{
				controller.Attack(num, side, controller.leftWeapon, controller.rightWeapon, num2);
				EndAction(controller);
			}
		}

		protected override void _EndAction(RPGCharacterController controller)
		{
		}
	}
}
