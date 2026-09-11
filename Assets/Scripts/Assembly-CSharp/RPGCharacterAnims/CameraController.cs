using UnityEngine;
using UnityEngine.InputSystem;

namespace RPGCharacterAnims
{
	public class CameraController : MonoBehaviour
	{
		public GameObject cameraTarget;

		public float cameraTargetOffsetY;

		private Vector3 cameraTargetOffset;

		public float rotateSpeed = 2f;

		private float rotate;

		public float height = 6f;

		public float distance = 5f;

		public float zoomAmount = 0.1f;

		public float smoothing = 2f;

		private Vector3 offset;

		private bool following = true;

		private Vector3 lastPosition;

		private bool inputFollow;

		private bool inputRotateR;

		private bool inputRotateL;

		private bool inputMouseScrollUp;

		private bool inputMouseScrollDown;

		private void Start()
		{
			if (cameraTarget == null)
			{
				cameraTarget = GameObject.FindWithTag("Player");
			}
			if (!cameraTarget)
			{
				Debug.LogError("No target selected for Camera.");
			}
			else
			{
				SetStartPosition();
			}
		}

		private void SetStartPosition()
		{
			offset = new Vector3(cameraTarget.transform.position.x, cameraTarget.transform.position.y + height, cameraTarget.transform.position.z - distance);
			lastPosition = new Vector3(cameraTarget.transform.position.x, cameraTarget.transform.position.y + height, cameraTarget.transform.position.z - distance);
			distance = 1f;
			height = 1f;
		}

		private void Inputs()
		{
			inputFollow = Keyboard.current.fKey.isPressed;
			inputRotateL = Keyboard.current.qKey.isPressed;
			inputRotateR = Keyboard.current.eKey.isPressed;
			inputMouseScrollUp = Mouse.current.scroll.ReadValue().y > 0f;
			inputMouseScrollDown = Mouse.current.scroll.ReadValue().y < 0f;
		}

		private void Update()
		{
			if (!cameraTarget)
			{
				return;
			}
			Inputs();
			if (inputFollow)
			{
				if (following)
				{
					following = false;
				}
				else
				{
					following = true;
				}
			}
			if (following)
			{
				CameraFollow();
			}
			else
			{
				base.transform.position = lastPosition;
			}
			if (inputRotateL)
			{
				rotate = -1f;
			}
			else if (inputRotateR)
			{
				rotate = 1f;
			}
			else
			{
				rotate = 0f;
			}
			if (inputMouseScrollUp)
			{
				distance += zoomAmount;
				height += zoomAmount;
			}
			else if (inputMouseScrollDown)
			{
				distance -= zoomAmount;
				height -= zoomAmount;
			}
			cameraTargetOffset = cameraTarget.transform.position + new Vector3(0f, cameraTargetOffsetY, 0f);
			base.transform.rotation = Quaternion.Slerp(base.transform.rotation, Quaternion.LookRotation(cameraTargetOffset - base.transform.position), Time.deltaTime * smoothing);
		}

		private void CameraFollow()
		{
			offset = Quaternion.AngleAxis(rotate * rotateSpeed, Vector3.up) * offset;
			base.transform.position = new Vector3(Mathf.Lerp(lastPosition.x, cameraTarget.transform.position.x + offset.x, smoothing * Time.deltaTime), Mathf.Lerp(lastPosition.y, cameraTarget.transform.position.y + offset.y * height, smoothing * Time.deltaTime), Mathf.Lerp(lastPosition.z, cameraTarget.transform.position.z + offset.z * distance, smoothing * Time.deltaTime));
		}

		private void LateUpdate()
		{
			lastPosition = base.transform.position;
		}
	}
}
