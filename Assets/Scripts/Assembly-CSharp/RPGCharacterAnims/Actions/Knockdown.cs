using RPGCharacterAnims.Extensions;
using RPGCharacterAnims.Lookups;
using UnityEngine;

namespace RPGCharacterAnims.Actions
{
	public class Knockdown : MovementActionHandler<HitContext>
	{
		public Knockdown(RPGCharacterMovementController movement)
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
				num = (int)AnimationVariations.Knockdowns.TakeRandom();
				vector = AnimationData.HitDirection((KnockdownType)num);
				vector = controller.transform.rotation * vector;
			}
			else if (context.relative)
			{
				vector = controller.transform.rotation * vector;
			}
			controller.Knockdown((KnockdownType)num);
			movement.KnockbackForce(vector, force, variableForce);
			movement.currentState = CharacterState.Knockdown;
		}

		public override bool IsActive()
		{
			if (movement.currentState != null)
			{
				return (CharacterState)(object)movement.currentState == CharacterState.Knockdown;
			}
			return false;
		}
	}
}
