using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 마우스를 올리면 살짝 커지고, 누르면 눌린 느낌을 준다.
/// 버튼이 "반응한다"는 감각만으로 UI 완성도가 크게 올라간다.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class UIHoverScale : MonoBehaviour,
	IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
	[Tooltip("올렸을 때 배율")]
	public float hoverScale = 1.06f;

	[Tooltip("눌렀을 때 배율")]
	public float pressScale = 0.97f;

	[Tooltip("따라붙는 속도. 클수록 즉각적")]
	public float responsiveness = 14f;

	private RectTransform rect;
	private Vector3 baseScale;
	private float target = 1f;
	private bool hovering;

	private void Awake()
	{
		rect = GetComponent<RectTransform>();
		baseScale = rect.localScale;
	}

	private void OnDisable()
	{
		hovering = false;
		target = 1f;
		if (rect != null) { rect.localScale = baseScale; }
	}

	private void Update()
	{
		// timeScale이 0인 화면(일시정지 등)에서도 반응해야 한다
		float k = 1f - Mathf.Exp(-responsiveness * Time.unscaledDeltaTime);
		rect.localScale = Vector3.Lerp(rect.localScale, baseScale * target, k);
	}

	public void OnPointerEnter(PointerEventData e) { hovering = true;  target = hoverScale; }
	public void OnPointerExit(PointerEventData e)  { hovering = false; target = 1f; }
	public void OnPointerDown(PointerEventData e)  { target = pressScale; }
	public void OnPointerUp(PointerEventData e)    { target = hovering ? hoverScale : 1f; }
}
