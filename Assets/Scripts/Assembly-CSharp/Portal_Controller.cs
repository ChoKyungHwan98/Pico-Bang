using System.Collections;
using System.Linq;
using UnityEngine;

public class Portal_Controller : MonoBehaviour
{
	[Header("Applied to the effects at start")]
	[SerializeField]
	private Color portalEffectColor;

	[Header("Changing these might `break` the effects")]
	[Space(20f)]
	[SerializeField]
	private Renderer portalRenderer;

	[SerializeField]
	private ParticleSystem[] effectsParticles;

	[SerializeField]
	private Light portalLight;

	[SerializeField]
	private AudioSource orbAudio;

	[SerializeField]
	private AudioSource flashAudio;

	[SerializeField]
	private AudioSource portalAudio;

	private float maxVolOrb = 0.08f;

	private float maxVolportal = 0.8f;

	private float maxIntPortalLight = 4f;

	private float transitionSpeed = 0.3f;

	private bool inTransition;

	private bool activated;

	private Material portalMat;

	private Material portalEffectMat;

	private float fadeFloat;

	private Coroutine transitionCor;

	private void Awake()
	{
		Setup();
	}

	public void TogglePortal(bool _activate)
	{
		if (!inTransition && _activate != activated)
		{
			if (_activate)
			{
				activated = true;
				transitionCor = StartCoroutine(PreActivate());
			}
			else if (!_activate)
			{
				activated = false;
				effectsParticles[2].Stop();
				transitionCor = StartCoroutine(TransitionSequence());
			}
		}
	}

	private IEnumerator PreActivate()
	{
		inTransition = true;
		orbAudio.volume = maxVolOrb;
		orbAudio.Play();
		effectsParticles[0].Play();
		yield return new WaitForSeconds(2.2f);
		flashAudio.Play();
		portalAudio.Play();
		yield return new WaitForSeconds(0.3f);
		transitionCor = StartCoroutine(TransitionSequence());
		effectsParticles[2].Play();
	}

	private IEnumerator TransitionSequence()
	{
		inTransition = true;
		while (inTransition)
		{
			if (activated)
			{
				fadeFloat = Mathf.MoveTowards(fadeFloat, 1f, Time.deltaTime * transitionSpeed);
				orbAudio.volume -= Time.deltaTime * 0.1f;
				if (fadeFloat >= 1f)
				{
					inTransition = false;
					orbAudio.Stop();
				}
			}
			else
			{
				fadeFloat = Mathf.MoveTowards(fadeFloat, 0f, Time.deltaTime * transitionSpeed);
				if (fadeFloat <= 0f)
				{
					inTransition = false;
					portalAudio.Stop();
					effectsParticles[2].Stop();
				}
			}
			portalAudio.volume = maxVolportal * fadeFloat;
			portalEffectMat.SetFloat("_PortalFade", fadeFloat);
			portalMat.SetFloat("_EmissionStrength", fadeFloat);
			portalLight.intensity = maxIntPortalLight * fadeFloat;
			yield return null;
		}
	}

	private void Setup()
	{
		Material[] array = portalRenderer.materials.ToArray();
		portalMat = array[0];
		portalEffectMat = array[1];
		portalMat.SetColor("_EmissionColor", portalEffectColor);
		portalMat.SetFloat("_EmissionStrength", 0f);
		portalEffectMat.SetColor("_ColorMain", portalEffectColor);
		portalEffectMat.SetFloat("_PortalFade", 0f);
		ParticleSystem[] array2 = effectsParticles;
		for (int i = 0; i < array2.Length; i++)
		{
			ParticleSystem.MainModule main = array2[i].main;
			main.startColor = portalEffectColor;
		}
		portalAudio.volume = 0f;
		portalLight.intensity = 0f;
	}
}
