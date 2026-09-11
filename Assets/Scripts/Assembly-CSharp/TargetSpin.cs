using UnityEngine;

/// <summary>
/// 과녁이 눈에 띄게 만드는 최소한의 움직임.
///
/// 미로형 맵에서 과녁이 가만히 있으면 벽 무늬에 묻힌다.
/// 정면 축을 중심으로 천천히 돌고 아주 조금 맥동시켜서 "저기 있다"를 알린다.
/// 과하면 조준을 방해하므로 진폭은 작게.
/// </summary>
public class TargetSpin : MonoBehaviour
{
	[Tooltip("정면 축 기준 회전 속도(도/초)")]
	public float spinSpeed = 22f;

	[Tooltip("맥동 배율 폭. 0이면 크기 변화 없음")]
	public float pulseAmplitude = 0.04f;

	[Tooltip("맥동 주기(초)")]
	public float pulsePeriod = 1.8f;

	private Vector3 baseScale;
	private float seed;

	private void Awake()
	{
		baseScale = base.transform.localScale;
		// 여러 개가 동시에 같은 박자로 뛰면 인공적으로 보인다
		seed = Random.value * 10f;
	}

	private void Update()
	{
		if (spinSpeed != 0f)
		{
			base.transform.Rotate(Vector3.forward, spinSpeed * Time.deltaTime, Space.Self);
		}

		if (pulseAmplitude > 0f && pulsePeriod > 0.01f)
		{
			float s = 1f + Mathf.Sin((Time.time + seed) * Mathf.PI * 2f / pulsePeriod) * pulseAmplitude;
			base.transform.localScale = baseScale * s;
		}
	}
}
