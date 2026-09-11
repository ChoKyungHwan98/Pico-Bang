using UnityEngine;

public struct PlayerInputData
{
	public Vector3 MoveInput;

	public Vector2 MouseInput;

	public bool JumpInput;

	/// <summary>Shift — 달리기. 속도선 연출의 트리거이기도 하다.</summary>
	public bool SprintInput;
}
