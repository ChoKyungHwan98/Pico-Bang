using UnityEngine;

/// <summary>
/// 코드로 만드는 일회성 파티클 터짐.
/// 외부 프리팹 없이 쓸 수 있어서, 에셋이 정해지기 전에도 연출을 붙여둘 수 있다.
/// 나중에 정식 VFX 에셋이 생기면 이 호출부만 갈아끼우면 된다.
/// </summary>
public static class BurstVfx
{
	private static Material sharedMaterial;

	public static void Play(
		Vector3 position,
		Color color,
		int count = 24,
		float speed = 6f,
		float size = 0.18f,
		float lifetime = 0.55f,
		float gravity = 0.6f)
	{
		GameObject go = new GameObject("~Burst");
		go.transform.position = position;

		ParticleSystem ps = go.AddComponent<ParticleSystem>();
		ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

		ParticleSystem.MainModule main = ps.main;
		main.duration = lifetime;
		main.loop = false;
		main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
		main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.45f, speed);
		main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
		main.startColor = color;
		main.gravityModifier = gravity;
		main.simulationSpace = ParticleSystemSimulationSpace.World;
		main.playOnAwake = false;
		main.stopAction = ParticleSystemStopAction.Destroy;
		main.maxParticles = Mathf.Max(count * 2, 64);

		ParticleSystem.EmissionModule emission = ps.emission;
		emission.rateOverTime = 0f;
		emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

		ParticleSystem.ShapeModule shape = ps.shape;
		shape.enabled = true;
		shape.shapeType = ParticleSystemShapeType.Sphere;
		shape.radius = 0.12f;

		// 끝으로 갈수록 작아지고 옅어져야 "터졌다"로 읽힌다
		ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
		sol.enabled = true;
		sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

		ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
		col.enabled = true;
		Gradient grad = new Gradient();
		grad.SetKeys(
			new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 0.35f) },
			new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
		col.color = new ParticleSystem.MinMaxGradient(grad);

		ParticleSystemRenderer psr = go.GetComponent<ParticleSystemRenderer>();
		psr.material = GetSharedMaterial();
		psr.renderMode = ParticleSystemRenderMode.Billboard;
		psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		psr.receiveShadows = false;

		ps.Play();
		Object.Destroy(go, lifetime + 1f);
	}

	private static Material GetSharedMaterial()
	{
		if (sharedMaterial != null) { return sharedMaterial; }

		Shader shader = Shader.Find("Sprites/Default");
		if (shader == null) { shader = Shader.Find("Unlit/Color"); }

		sharedMaterial = new Material(shader) { name = "Burst (runtime)" };
		sharedMaterial.hideFlags = HideFlags.HideAndDontSave;
		return sharedMaterial;
	}
}
