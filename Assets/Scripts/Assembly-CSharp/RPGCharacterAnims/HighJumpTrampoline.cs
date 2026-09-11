using RPGCharacterAnims.Lookups;
using UnityEngine;

namespace RPGCharacterAnims
{
	public class HighJumpTrampoline : MonoBehaviour
	{
		private GameObject character;

		private float oldJumpSpeed;

		private void Update()
		{
			if (character != null)
			{
				RPGCharacterController component = character.GetComponent<RPGCharacterController>();
				component.SetJumpInput(Vector3.up);
				component.TryStartAction(HandlerTypes.Jump);
			}
		}

		private void OnTriggerEnter(Collider collide)
		{
			if (collide.gameObject.GetComponent<RPGCharacterController>() != null)
			{
				character = collide.gameObject;
				RPGCharacterMovementController component = character.GetComponent<RPGCharacterMovementController>();
				oldJumpSpeed = component.jumpSpeed;
				component.jumpSpeed = oldJumpSpeed * 2f;
				Debug.Log("Trampoline!");
			}
		}

		private void OnTriggerExit(Collider collide)
		{
			if (collide.gameObject == character)
			{
				character.GetComponent<RPGCharacterMovementController>().jumpSpeed = oldJumpSpeed;
				character = null;
			}
		}
	}
}
