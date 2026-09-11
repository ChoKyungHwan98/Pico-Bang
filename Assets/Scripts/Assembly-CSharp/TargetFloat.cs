using UnityEngine;

/// <summary>
/// 공중 과녁이 제자리에서 둥실둥실 떠 있게 한다.
///
/// 위아래로 아주 조금만 움직인다 — 맞히기 어렵게 만들려는 게 아니라 "떠 있다"는 느낌만 주기 위함이다.
/// 과녁마다 시작 위상을 다르게 해서 모두가 같은 박자로 출렁이지 않게 한다.
/// </summary>
public class TargetFloat : MonoBehaviour
{
	[Tooltip("위아래로 움직이는 폭(m)")]
	[SerializeField]
	private float amplitude = 0.12f;

	[Tooltip("한 번 오르내리는 데 걸리는 시간(초)")]
	[SerializeField]
	private float period = 2.4f;

	[Tooltip("천천히 도는 속도(도/초)")]
	[SerializeField]
	private float spinSpeed = 25f;

	private Vector3 basePosition;
	private float phase;

	private void Start()
	{
		basePosition = base.transform.position;
		phase = Random.value * Mathf.PI * 2f;
	}

	private void Update()
	{
		float y = Mathf.Sin(Time.time * (Mathf.PI * 2f / Mathf.Max(0.1f, period)) + phase) * amplitude;
		base.transform.position = basePosition + Vector3.up * y;
		base.transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
	}
}
