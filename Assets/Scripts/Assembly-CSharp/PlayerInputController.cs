using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 입력 수집. 같은 프레임의 다른 스크립트보다 항상 먼저 돌아야
/// 캐릭터와 카메라가 같은 입력값을 본다.
/// </summary>
[DefaultExecutionOrder(-200)]
public class PlayerInputController : MonoBehaviour
{
	public PlayerInputData Current;

	[Header("Mouse Look")]
	[Tooltip("원시 마우스 델타(픽셀)에 곱해지는 값. 설정 메뉴에서 덮어쓴다")]
	public float mouseSensitivity = GameSettings.DefaultSensitivity;

	[Tooltip("설정 메뉴의 감도를 따를지 여부. 끄면 위 값을 그대로 쓴다(튜닝용)")]
	public bool useSettingsSensitivity = true;

	private void Start()
	{
		Current = default;
	}

	private void Update()
	{
		Keyboard kb = Keyboard.current;
		Mouse ms = Mouse.current;
		if (kb == null || ms == null) { return; }

		Vector3 moveInput = new Vector3(
			kb.dKey.ReadValue() - kb.aKey.ReadValue(),
			0f,
			kb.wKey.ReadValue() - kb.sKey.ReadValue());

		// 원시 델타를 그대로 각도로 쓰면 한 번 휘두를 때 여러 바퀴가 돈다.
		// 감도를 곱해 사람이 조준할 수 있는 범위로 낮춘다.
		float sens = useSettingsSensitivity ? GameSettings.MouseSensitivity : mouseSensitivity;

		Vector2 rawDelta = ms.delta.ReadValue();
		Vector2 mouseInput = rawDelta * sens;
		if (GameSettings.InvertY) { mouseInput.y = -mouseInput.y; }

		Current = new PlayerInputData
		{
			MoveInput = moveInput,
			MouseInput = mouseInput,
			JumpInput = kb.spaceKey.isPressed,
			SprintInput = kb.leftShiftKey.isPressed
		};
	}
}
