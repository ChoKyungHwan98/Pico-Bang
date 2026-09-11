using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class SimpleCharacterMotor : MonoBehaviour
{
	public CursorLockMode cursorLockMode = CursorLockMode.Locked;

	public bool cursorVisible;

	[Header("Movement")]
	public float walkSpeed = 2f;

	public float runSpeed = 4f;

	public float gravity = 9.8f;

	[Space]
	[Header("Look")]
	public Transform cameraPivot;

	public float lookSpeed = 45f;

	public bool invertY = true;

	[Space]
	[Header("Smoothing")]
	public float movementAcceleration = 1f;

	private CharacterController controller;

	private Vector3 movement;

	private Vector3 finalMovement;

	private float speed;

	private Quaternion targetRotation;

	private Quaternion targetPivotRotation;

	private void Awake()
	{
		controller = GetComponent<CharacterController>();
		Cursor.lockState = cursorLockMode;
		Cursor.visible = cursorVisible;
		targetRotation = (targetPivotRotation = Quaternion.identity);
	}

	private void Update()
	{
		UpdateTranslation();
		UpdateLookRotation();
	}

	private void UpdateLookRotation()
	{
		float axis = Input.GetAxis("Mouse Y");
		float axis2 = Input.GetAxis("Mouse X");
		axis *= (float)((!invertY) ? 1 : (-1));
		targetRotation = base.transform.localRotation * Quaternion.AngleAxis(axis2 * lookSpeed * Time.deltaTime, Vector3.up);
		targetPivotRotation = cameraPivot.localRotation * Quaternion.AngleAxis(axis * lookSpeed * Time.deltaTime, Vector3.right);
		base.transform.localRotation = targetRotation;
		cameraPivot.localRotation = targetPivotRotation;
	}

	private void UpdateTranslation()
	{
		if (controller.isGrounded)
		{
			float axis = Input.GetAxis("Horizontal");
			float axis2 = Input.GetAxis("Vertical");
			bool key = Input.GetKey(KeyCode.LeftShift);
			Vector3 vector = new Vector3(axis, 0f, axis2);
			speed = (key ? runSpeed : walkSpeed);
			movement = base.transform.TransformDirection(vector * speed);
		}
		else
		{
			movement.y -= gravity * Time.deltaTime;
		}
		finalMovement = Vector3.Lerp(finalMovement, movement, Time.deltaTime * movementAcceleration);
		controller.Move(finalMovement * Time.deltaTime);
	}
}
