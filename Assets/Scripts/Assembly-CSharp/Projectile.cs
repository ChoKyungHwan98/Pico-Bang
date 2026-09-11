using System;
using UnityEngine;

/// <summary>
/// 눈에 보이는 발사체.
///
/// 명중 판정은 발사 순간 레이캐스트로 이미 끝나 있고, 이 객체는 연출만 담당한다.
/// 도착하면 콜백으로 실제 피격 처리를 넘긴다.
///
/// 판정을 발사체에 맡기면 빠르게 움직이는 대상에서 빗나가기 시작하고,
/// 플레이어는 "분명 맞췄는데"라고 느낀다. 판정과 연출은 분리하는 편이 낫다.
/// </summary>
public class Projectile : MonoBehaviour
{
	private Vector3 startPoint;
	private Vector3 endPoint;
	private float travelTime;
	private float elapsed;
	private Action onArrive;
	private bool arrived;

	private static Material sharedMaterial;

	/// <summary>
	/// 발사체를 쏜다.
	/// </summary>
	/// <param name="speed">초당 이동 거리. 너무 느리면 답답하고 너무 빠르면 안 보인다</param>
	public static Projectile Fire(
		Vector3 from, Vector3 to, float speed, float size, Color color,
		float trailTime, Action onArrive)
	{
		GameObject go = new GameObject("~Projectile");
		go.transform.position = from;

		Projectile p = go.AddComponent<Projectile>();
		p.startPoint = from;
		p.endPoint = to;
		p.onArrive = onArrive;

		float distance = Vector3.Distance(from, to);
		p.travelTime = Mathf.Max(0.02f, distance / Mathf.Max(1f, speed));

		p.BuildVisual(size, color, trailTime);
		return p;
	}

	private void BuildVisual(float size, Color color, float trailTime)
	{
		// 코어 — 작은 구체
		GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		core.name = "Core";
		core.transform.SetParent(base.transform, false);
		core.transform.localScale = Vector3.one * size;

		Collider col = core.GetComponent<Collider>();
		if (col != null) { Destroy(col); }

		Renderer rend = core.GetComponent<Renderer>();
		rend.sharedMaterial = GetSharedMaterial();
		rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		rend.receiveShadows = false;

		// 개체별 색은 프로퍼티 블록으로 — 공유 머티리얼을 건드리지 않는다
		MaterialPropertyBlock mpb = new MaterialPropertyBlock();
		mpb.SetColor("_Color", color);
		mpb.SetColor("_BaseColor", color);
		rend.SetPropertyBlock(mpb);

		// 꼬리
		TrailRenderer trail = base.gameObject.AddComponent<TrailRenderer>();
		trail.time = trailTime;
		trail.startWidth = size * 0.9f;
		trail.endWidth = 0f;
		trail.material = GetSharedMaterial();
		trail.numCapVertices = 2;
		trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		trail.receiveShadows = false;
		trail.startColor = color;
		trail.endColor = new Color(color.r, color.g, color.b, 0f);
	}

	private static Material GetSharedMaterial()
	{
		if (sharedMaterial != null) { return sharedMaterial; }

		Shader shader = Shader.Find("Sprites/Default");
		if (shader == null) { shader = Shader.Find("Unlit/Color"); }

		sharedMaterial = new Material(shader) { name = "Projectile (runtime)" };
		sharedMaterial.hideFlags = HideFlags.HideAndDontSave;
		return sharedMaterial;
	}

	private void Update()
	{
		if (arrived) { return; }

		elapsed += Time.deltaTime;
		float t = Mathf.Clamp01(elapsed / travelTime);
		base.transform.position = Vector3.Lerp(startPoint, endPoint, t);

		if (t >= 1f)
		{
			arrived = true;
			base.transform.position = endPoint;

			try { onArrive?.Invoke(); }
			finally
			{
				// 꼬리가 사라질 시간을 준 뒤 정리
				TrailRenderer trail = GetComponent<TrailRenderer>();
				float linger = (trail != null) ? trail.time : 0f;

				Transform core = base.transform.Find("Core");
				if (core != null) { core.gameObject.SetActive(false); }

				Destroy(base.gameObject, linger + 0.05f);
			}
		}
	}
}
