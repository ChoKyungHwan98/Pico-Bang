using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Rigidbody))]
public class MonsterController : MonoBehaviour
{
	[Header("Animation Smoothing")]
	[SerializeField]
	private float speedSmoothTime = 0.1f;

	[Header("Ground Check (For State Anim)")]
	[SerializeField]
	private LayerMask groundLayer;

	[SerializeField]
	private float rayLength = 0.5f;

	[SerializeField]
	private Vector3 rayOriginOffset = new Vector3(0f, 0.5f, 0f);

	private Animator animator;

	private NavMeshAgent agent;

	private Rigidbody rb;

	private MonsterAI monsterAI;

	private float moveX;

	private float moveY;

	private float moveXVelocity;

	private float moveYVelocity;

	private void Awake()
	{
		animator = GetComponent<Animator>();
		agent = GetComponent<NavMeshAgent>();
		rb = GetComponent<Rigidbody>();
		monsterAI = GetComponent<MonsterAI>();
	}

	private void FixedUpdate()
	{
		UpdateGroundStates();
	}

	private void LateUpdate()
	{
		if (monsterAI != null && monsterAI.IsInStun)
		{
			animator.SetFloat("Move X", 0f);
			animator.SetFloat("Move Y", 0f);
			animator.SetFloat("Speed", 0f);
		}
		else
		{
			UpdateMovementAnimation();
		}
	}

	private void UpdateGroundStates()
	{
		bool flag = Physics.Raycast(base.transform.position + rayOriginOffset, Vector3.down, rayLength, groundLayer);
		animator.SetBool("IsGrounded", flag);
		float y = rb.linearVelocity.y;
		animator.SetBool("IsJumping", !flag && y > 0.1f);
		animator.SetBool("IsFalling", !flag && y < -0.1f);
		animator.SetFloat("VerticalVelocity", y);
	}

	private void UpdateMovementAnimation()
	{
		if (!(animator == null))
		{
			Vector3 velocity = agent.velocity;
			Vector3 vector = base.transform.InverseTransformDirection(velocity);
			float num = ((agent.speed > 0f) ? agent.speed : 1f);
			float target = vector.x / num;
			float target2 = vector.z / num;
			moveX = Mathf.SmoothDamp(moveX, target, ref moveXVelocity, speedSmoothTime);
			moveY = Mathf.SmoothDamp(moveY, target2, ref moveYVelocity, speedSmoothTime);
			animator.SetFloat("Move X", moveX);
			animator.SetFloat("Move Y", moveY);
			animator.SetFloat("Speed", velocity.magnitude);
		}
	}

	private void OnDrawGizmos()
	{
		Gizmos.color = Color.magenta;
		Vector3 vector = base.transform.position + rayOriginOffset;
		Gizmos.DrawLine(vector, vector + Vector3.down * rayLength);
	}
}
