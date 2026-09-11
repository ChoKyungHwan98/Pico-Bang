using UnityEngine;

public class SpawnEffect : MonoBehaviour
{
	public float spawnEffectTime = 2f;

	public float pause = 1f;

	public AnimationCurve fadeIn;

	private ParticleSystem ps;

	private float timer;

	private Renderer _renderer;

	private int shaderProperty;

	private void Start()
	{
		shaderProperty = Shader.PropertyToID("_cutoff");
		_renderer = GetComponent<Renderer>();
		ps = GetComponentInChildren<ParticleSystem>();
		ParticleSystem.MainModule main = ps.main;
		main.duration = spawnEffectTime;
		ps.Play();
	}

	private void Update()
	{
		if (timer < spawnEffectTime + pause)
		{
			timer += Time.deltaTime;
		}
		else
		{
			ps.Play();
			timer = 0f;
		}
		_renderer.material.SetFloat(shaderProperty, fadeIn.Evaluate(Mathf.InverseLerp(0f, spawnEffectTime, timer)));
	}
}
