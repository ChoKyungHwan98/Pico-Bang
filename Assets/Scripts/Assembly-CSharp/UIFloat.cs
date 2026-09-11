using UnityEngine;

/// <summary>
/// 로고가 가만히 멈춰 있으면 화면이 죽어 보인다.
/// 아주 미세하게 위아래로 떠다니고 숨쉬듯 커졌다 작아지게 한다.
/// 눈에 띄면 과한 것이니 진폭은 작게 유지할 것.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class UIFloat : MonoBehaviour
{
	[Tooltip("위아래로 움직이는 폭(픽셀)")]
	public float bobAmplitude = 10f;

	[Tooltip("위아래 주기(초)")]
	public float bobPeriod = 3.2f;

	[Tooltip("숨쉬는 배율 폭. 0이면 크기 변화 없음")]
	public float breathAmplitude = 0.015f;

	[Tooltip("숨쉬는 주기(초)")]
	public float breathPeriod = 4.1f;

	private RectTransform rect;
	private Vector2 basePosition;
	private Vector3 baseScale;
	private float seed;

	private void Awake()
	{
		rect = GetComponent<RectTransform>();
		basePosition = rect.anchoredPosition;
		baseScale = rect.localScale;
		seed = Random.value * 10f;
	}

	private void OnDisable()
	{
		if (rect == null) { return; }
		rect.anchoredPosition = basePosition;
		rect.localScale = baseScale;
	}

	private void Update()
	{
		float t = Time.unscaledTime + seed;

		if (bobPeriod > 0.01f)
		{
			float y = Mathf.Sin(t * Mathf.PI * 2f / bobPeriod) * bobAmplitude;
			rect.anchoredPosition = basePosition + new Vector2(0f, y);
		}

		if (breathPeriod > 0.01f && breathAmplitude > 0f)
		{
			float s = 1f + Mathf.Sin(t * Mathf.PI * 2f / breathPeriod) * breathAmplitude;
			rect.localScale = baseScale * s;
		}
	}
}
