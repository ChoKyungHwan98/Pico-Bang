using UnityEngine;

/// <summary>
/// 스턴 당한 몬스터 몸에 전기가 흐르는 연출.
///
/// 외부 에셋 없이 LineRenderer로 지그재그 아크를 직접 그린다.
/// 셰이더 종류를 가리지 않도록 렌더러 깜빡임은 enabled 토글로 처리한다
/// (PlayerHealth의 피격 깜빡임과 같은 방식이라 게임 내 표현 언어가 일관된다).
/// </summary>
public class StunSparkEffect : MonoBehaviour
{
	private struct Arc
	{
		public LineRenderer line;
		public Vector3 localStart;
		public Vector3 localEnd;
		public float nextReshapeTime;
	}

	private Arc[] arcs;
	private Renderer[] bodyRenderers;
	private Bounds localBounds;
	private float endTime;
	private float nextBlinkTime;
	private bool blinkState = true;

	private float arcWidth;
	private float jaggedness;
	private int segments;
	private float reshapeInterval;
	private float blinkInterval;
	private Color colorA;
	private Color colorB;

	/// <summary>
	/// 대상에게 전기 연출을 붙인다. 지속 시간이 끝나면 스스로 정리한다.
	/// </summary>
	public static StunSparkEffect Play(
		Transform target,
		float duration,
		int arcCount = 5,
		float width = 0.045f,
		float jagged = 0.16f,
		int segmentCount = 9,
		float reshapeEvery = 0.045f,
		float blinkEvery = 0.09f)
	{
		if (target == null || duration <= 0f) { return null; }

		// 이미 붙어 있으면 시간만 연장한다 (중복 생성 방지)
		StunSparkEffect existing = target.GetComponentInChildren<StunSparkEffect>();
		if (existing != null)
		{
			existing.endTime = Mathf.Max(existing.endTime, Time.time + duration);
			return existing;
		}

		GameObject go = new GameObject("~StunSpark");
		go.transform.SetParent(target, false);

		StunSparkEffect fx = go.AddComponent<StunSparkEffect>();
		fx.arcWidth = width;
		fx.jaggedness = jagged;
		fx.segments = Mathf.Max(3, segmentCount);
		fx.reshapeInterval = reshapeEvery;
		fx.blinkInterval = blinkEvery;
		fx.colorA = new Color(0.55f, 0.85f, 1f, 1f);
		fx.colorB = new Color(1f, 1f, 1f, 1f);
		fx.endTime = Time.time + duration;
		fx.Build(target, arcCount);

		return fx;
	}

	private void Build(Transform target, int arcCount)
	{
		bodyRenderers = target.GetComponentsInChildren<Renderer>(true);
		localBounds = EstimateLocalBounds(target);

		Material mat = CreateArcMaterial();

		arcs = new Arc[Mathf.Max(1, arcCount)];
		for (int i = 0; i < arcs.Length; i++)
		{
			GameObject lgo = new GameObject("Arc" + i);
			lgo.transform.SetParent(base.transform, false);

			LineRenderer lr = lgo.AddComponent<LineRenderer>();
			lr.useWorldSpace = false;
			lr.positionCount = segments;
			lr.widthMultiplier = arcWidth;
			lr.numCapVertices = 2;
			lr.material = mat;
			lr.textureMode = LineTextureMode.Stretch;
			lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			lr.receiveShadows = false;
			lr.alignment = LineAlignment.View;

			arcs[i] = new Arc
			{
				line = lr,
				localStart = RandomPointOnBounds(),
				localEnd = RandomPointOnBounds(),
				nextReshapeTime = 0f
			};
		}
	}

	private static Material CreateArcMaterial()
	{
		// Sprites/Default는 어떤 렌더 파이프라인에서도 있는 언릿 셰이더라
		// URP/빌트인 어디서든 안전하게 뜬다.
		Shader shader = Shader.Find("Sprites/Default");
		if (shader == null) { shader = Shader.Find("Unlit/Color"); }

		Material mat = new Material(shader) { name = "StunArc (runtime)" };
		mat.hideFlags = HideFlags.HideAndDontSave;
		return mat;
	}

	private Bounds EstimateLocalBounds(Transform target)
	{
		if (bodyRenderers == null || bodyRenderers.Length == 0)
		{
			return new Bounds(Vector3.up, new Vector3(1f, 2f, 1f));
		}

		Bounds world = bodyRenderers[0].bounds;
		for (int i = 1; i < bodyRenderers.Length; i++)
		{
			if (bodyRenderers[i] != null) { world.Encapsulate(bodyRenderers[i].bounds); }
		}

		Vector3 localCenter = target.InverseTransformPoint(world.center);
		Vector3 lossy = target.lossyScale;
		Vector3 localSize = new Vector3(
			world.size.x / Mathf.Max(0.0001f, lossy.x),
			world.size.y / Mathf.Max(0.0001f, lossy.y),
			world.size.z / Mathf.Max(0.0001f, lossy.z));

		return new Bounds(localCenter, localSize);
	}

	private Vector3 RandomPointOnBounds()
	{
		Vector3 e = localBounds.extents;
		return localBounds.center + new Vector3(
			Random.Range(-e.x, e.x),
			Random.Range(-e.y, e.y),
			Random.Range(-e.z, e.z));
	}

	private void Update()
	{
		if (Time.time >= endTime) { Cleanup(); return; }

		float now = Time.time;

		for (int i = 0; i < arcs.Length; i++)
		{
			if (arcs[i].line == null) { continue; }

			if (now >= arcs[i].nextReshapeTime)
			{
				arcs[i].localStart = RandomPointOnBounds();
				arcs[i].localEnd = RandomPointOnBounds();
				arcs[i].nextReshapeTime = now + reshapeInterval * Random.Range(0.6f, 1.5f);

				Color c = Color.Lerp(colorA, colorB, Random.value);
				arcs[i].line.startColor = c;
				arcs[i].line.endColor = new Color(c.r, c.g, c.b, 0.35f);
			}

			ShapeArc(arcs[i]);
		}

		// 몸 깜빡임
		if (now >= nextBlinkTime)
		{
			nextBlinkTime = now + blinkInterval;
			blinkState = !blinkState;
			SetBodyVisible(blinkState);
		}
	}

	private void ShapeArc(Arc arc)
	{
		Vector3 a = arc.localStart;
		Vector3 b = arc.localEnd;

		for (int s = 0; s < segments; s++)
		{
			float t = (float)s / (segments - 1);
			Vector3 p = Vector3.Lerp(a, b, t);

			// 양 끝은 붙이고 가운데만 흔들어야 아크처럼 보인다
			float wobble = Mathf.Sin(t * Mathf.PI) * jaggedness;
			p += new Vector3(
				Random.Range(-wobble, wobble),
				Random.Range(-wobble, wobble),
				Random.Range(-wobble, wobble));

			arc.line.SetPosition(s, p);
		}
	}

	private void SetBodyVisible(bool visible)
	{
		if (bodyRenderers == null) { return; }
		foreach (Renderer r in bodyRenderers)
		{
			if (r != null && !(r is LineRenderer)) { r.enabled = visible; }
		}
	}

	private void Cleanup()
	{
		SetBodyVisible(true);
		Destroy(base.gameObject);
	}

	private void OnDestroy()
	{
		// 도중에 강제로 파괴돼도 몸이 사라진 채로 남지 않도록
		SetBodyVisible(true);
	}
}
