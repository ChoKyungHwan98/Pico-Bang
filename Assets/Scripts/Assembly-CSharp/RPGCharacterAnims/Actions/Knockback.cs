using RPGCharacterAnims.Extensions;
using RPGCharacterAnims.Lookups;
using UnityEngine;

namespace RPGCharacterAnims.Actions
{
	public class Knockback : MovementActionHandler<HitContext>
	{
		public Knockback(RPGCharacterMovementController movement)
			: base(movement)
		{
		}

		public override bool CanStartAction(RPGCharacterController controller)
		{
			return controller.canAction;
		}

		protected override void _StartAction(RPGCharacterController controller, HitContext context)
		{
			int num = context.number;
			Vector3 vector = context.direction;
			float force = context.force;
			float variableForce = context.variableForce;
			if (num == -1)
			{
				num = (int)AnimationVariations.Knockbacks.TakeRandom();
				vector = AnimationData.HitDirection((KnockbackType)num);
				vector = controller.transform.rotation * vector;
			}
			else if (context.relative)
			{
				vector = controller.transform.rotation * vector;
			}
			controller.Knockback((KnockbackType)num);
			movement.KnockbackForce(vector, force, variableForce);
			movement.currentState = CharacterState.Knockback;
		}

		public override bool IsActive()
		{
			if (movement.currentState != null)
			{
				return (CharacterState)(object)movement.currentState == CharacterState.Knockback;
			}
			return false;
		}
	}
}
