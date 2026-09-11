using RPGCharacterAnims.Actions;
using RPGCharacterAnims.Lookups;
using UnityEngine;
using UnityEngine.AI;

namespace RPGCharacterAnims
{
	[RequireComponent(typeof(NavMeshAgent))]
	[RequireComponent(typeof(RPGCharacterController))]
	public class RPGCharacterNavigationController : MonoBehaviour
	{
		[HideInInspector]
		public NavMeshAgent navMeshAgent;

		private RPGCharacterController rpgCharacterController;

		private RPGCharacterMovementController rpgCharacterMovementController;

		private Animator animator;

		public bool debugNavigation;

		[HideInInspector]
		public bool isNavigating;

		[SerializeField]
		private float moveSpeed = 1f;

		[SerializeField]
		private float rotationSpeed = 1f;

		[SerializeField]
		private float maxNavPathLength = 40f;

		private void Awake()
		{
			navMeshAgent = GetComponent<NavMeshAgent>();
			navMeshAgent.enabled = false;
			rpgCharacterController = GetComponent<RPGCharacterController>();
			rpgCharacterMovementController = GetComponent<RPGCharacterMovementController>();
			rpgCharacterController.SetHandler(HandlerTypes.Navigation, new Navigation(this));
		}

		private void Start()
		{
			animator = rpgCharacterController.animator;
			if (!(animator != null))
			{
				Debug.LogError("No Animator component found!");
				Debug.Break();
			}
		}

		private void Update()
		{
			if (isNavigating)
			{
				RotateTowardsMovementDir();
				navMeshAgent.speed = moveSpeed * 7f;
				if (navMeshAgent.velocity.sqrMagnitude > 0f)
				{
					animator.SetBool(AnimationParameters.Moving, value: true);
					animator.SetFloat(AnimationParameters.VelocityZ, moveSpeed);
				}
				else
				{
					StopAnimation();
				}
			}
			if (isNavigating && navMeshAgent.destination == base.transform.position)
			{
				StopNavigating();
				StopAnimation();
			}
		}

		private float GetPathLength(NavMeshPath path)
		{
			float num = 0f;
			if (path.corners.Length < 2)
			{
				return num;
			}
			for (int i = 0; i < path.corners.Length - 1; i++)
			{
				num += Vector3.Distance(path.corners[i], path.corners[i + 1]);
			}
			return num;
		}

		public bool CanMoveTo(Vector3 destination)
		{
			NavMeshPath navMeshPath = new NavMeshPath();
			if (!NavMesh.CalculatePath(base.transform.position, destination, -1, navMeshPath))
			{
				return false;
			}
			if (navMeshPath.status != NavMeshPathStatus.PathComplete)
			{
				return false;
			}
			if (GetPathLength(navMeshPath) > maxNavPathLength)
			{
				return false;
			}
			return true;
		}

		public void MeshNavToPoint(Vector3 destination)
		{
			if (CanMoveTo(destination))
			{
				if (debugNavigation)
				{
					Vector3 vector = destination;
					Debug.Log("MeshNavToPoint: " + vector.ToString());
				}
				navMeshAgent.enabled = true;
				isNavigating = true;
				navMeshAgent.SetDestination(destination);
				if (rpgCharacterMovementController != null)
				{
					rpgCharacterMovementController.enabled = false;
				}
			}
		}

		public void StopNavigating()
		{
			isNavigating = false;
			navMeshAgent.enabled = false;
			if (rpgCharacterMovementController != null)
			{
				rpgCharacterMovementController.enabled = true;
			}
		}

		public void StopAnimation()
		{
			animator.SetFloat(AnimationParameters.VelocityZ, 0f);
			animator.SetBool(AnimationParameters.Moving, value: false);
		}

		private void RotateTowardsMovementDir()
		{
			if (navMeshAgent.velocity.sqrMagnitude > 0.01f)
			{
				base.transform.rotation = Quaternion.Slerp(base.transform.rotation, Quaternion.LookRotation(navMeshAgent.velocity), Time.deltaTime * navMeshAgent.angularSpeed * rotationSpeed);
				Quaternion rotation = base.transform.rotation;
				rotation.eulerAngles = new Vector3(0f, rotation.eulerAngles.y, 0f);
				base.transform.rotation = rotation;
			}
		}
	}
}
