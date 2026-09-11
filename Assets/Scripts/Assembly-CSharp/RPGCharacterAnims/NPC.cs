using RPGCharacterAnims.Lookups;
using UnityEngine;

namespace RPGCharacterAnims
{
	[RequireComponent(typeof(RPGCharacterController))]
	[RequireComponent(typeof(RPGCharacterNavigationController))]
	public class NPC : MonoBehaviour
	{
		private RPGCharacterController rpgCharacterController;

		private RPGCharacterNavigationController rpgNavigationController;

		private Vector3 targetPosition;

		public float followDistance = 3f;

		private void Awake()
		{
			rpgCharacterController = GetComponent<RPGCharacterController>();
			rpgNavigationController = GetComponent<RPGCharacterNavigationController>();
		}

		private void Update()
		{
			targetPosition = rpgCharacterController.target.transform.position;
			if (IsOutOfRange(base.transform.position, targetPosition))
			{
				rpgCharacterController.StartAction(HandlerTypes.Navigation, RandomOffset(targetPosition));
			}
		}

		private Vector3 RandomOffset(Vector3 position)
		{
			return new Vector3(position.x - (float)Random.Range(1, 2), position.y, position.z - (float)Random.Range(1, 2));
		}

		private bool IsOutOfRange(Vector3 npc, Vector3 player)
		{
			if (Vector3.Distance(npc, player) > followDistance)
			{
				return true;
			}
			return false;
		}
	}
}
