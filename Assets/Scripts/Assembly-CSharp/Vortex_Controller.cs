using System.Collections;
using UnityEngine;

public class Vortex_Controller : MonoBehaviour
{
	[Header("Applied to the effects at start")]
	[SerializeField]
	private Color effectsColor;

	[Header("Changing these might `break` the effects")]
	[Space(20f)]
	[SerializeField]
	private Renderer[] meshRenderers;

	[SerializeField]
	private Transform rocksBaseTF;

	[SerializeField]
	private ParticleSystem[] effectsParticles;

	[SerializeField]
	private Light vortexLight;

	[SerializeField]
	private AudioSource[] effectsAudio;

	private float maxIntLight = 4f;

	private float transitionSpeed = 0.6f;

	private Transform[] rocks = new Transform[2];

	private bool inTransition;

	private bool activated;


	private Material matVortexInstance;

	private Material matRocksInstance;

	private float fadeFloat;

	private Coroutine transitionCor;

	private Coroutine animateRocksCor;

	private void Awake()
	{
		matVortexInstance = meshRenderers[0].GetComponent<Renderer>().material;
		meshRenderers[1].material = matVortexInstance;
		matVortexInstance.SetColor("_EmissionColor", effectsColor);
		matVortexInstance.SetFloat("_EmissionStrength", 0f);
		matRocksInstance = meshRenderers[2].GetComponent<Renderer>().material;
		meshRenderers[3].material = matRocksInstance;
		matRocksInstance.SetColor("_EmissionColor", effectsColor);
		matRocksInstance.SetFloat("_EmissionStrength", 0f);
		rocks[0] = rocksBaseTF.GetChild(0).transform;
		rocks[1] = rocksBaseTF.GetChild(1).transform;
		vortexLight.color = effectsColor;
		maxIntLight = vortexLight.intensity;
		vortexLight.intensity = 0f;
		ParticleSystem[] array = effectsParticles;
		for (int i = 0; i < array.Length; i++)
		{
			ParticleSystem.MainModule main = array[i].main;
			main.startColor = effectsColor;
		}
	}

	public void F_ToggleVortex(bool _activate)
	{
		if (inTransition || _activate == activated)
		{
			return;
		}
		if (_activate)
		{
			activated = true;
			ParticleSystem[] array = effectsParticles;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Play();
			}
			AudioSource[] array2 = effectsAudio;
			for (int i = 0; i < array2.Length; i++)
			{
				array2[i].Play();
			}
			transitionCor = StartCoroutine(TransitionSequence());
			animateRocksCor = StartCoroutine(RocksAnimation());
		}
		else if (!_activate)
		{
			activated = false;
			ParticleSystem[] array = effectsParticles;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Stop();
			}
			transitionCor = StartCoroutine(TransitionSequence());
			StopCoroutine(animateRocksCor);
		}
	}

	private IEnumerator TransitionSequence()
	{
		inTransition = true;
		Vector3 rocksBasePos = rocksBaseTF.localPosition;
		while (true)
		{
			if (activated)
			{
				fadeFloat = Mathf.MoveTowards(fadeFloat, 1f, Time.deltaTime * transitionSpeed);
				if (fadeFloat >= 1f)
				{
					inTransition = false;
					StopCoroutine(transitionCor);
				}
			}
			else
			{
				fadeFloat = Mathf.MoveTowards(fadeFloat, 0f, Time.deltaTime * transitionSpeed);
				if (fadeFloat <= 0f)
				{
					AudioSource[] array = effectsAudio;
					for (int i = 0; i < array.Length; i++)
					{
						array[i].Stop();
					}
					inTransition = false;
					StopCoroutine(transitionCor);
				}
			}
			effectsAudio[1].volume = fadeFloat * 0.8f;
			effectsAudio[2].volume = fadeFloat * 0.8f;
			effectsAudio[3].volume = fadeFloat * 0.2f;
			matVortexInstance.SetFloat("_EmissionStrength", fadeFloat * 0.4f);
			matRocksInstance.SetFloat("_EmissionStrength", fadeFloat * 0.4f);
			vortexLight.intensity = maxIntLight * fadeFloat;
			rocksBasePos[1] = fadeFloat * 0.5f;
			rocksBaseTF.localPosition = rocksBasePos;
			yield return null;
		}
	}

	private IEnumerator RocksAnimation()
	{
		Vector3[] rocksPos = new Vector3[2]
		{
			rocks[0].localPosition,
			rocks[1].localPosition
		};
		while (true)
		{
			for (int i = 0; i < 2; i++)
			{
				rocks[i].localRotation = Random.rotation;
				rocks[i].localPosition = rocksPos[i] + Random.onUnitSphere * 0.05f;
			}
			yield return new WaitForSeconds(Random.Range(0.05f, 0.1f));
		}
	}
}
