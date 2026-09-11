using System.Collections;
using UnityEngine;

public class SoundManager : MonoBehaviour
{
	public static SoundManager Instance;

	[Header("BGM Clips")]
	public AudioClip homeBGM;

	public AudioClip inGameBGM;

	[Header("SFX Clips")]
	public AudioClip fullCountdownSFX;

	[Header("Settings")]
	public float fadeDuration = 1f;

	[Tooltip("BGM의 기준 볼륨. 설정 메뉴의 BGM 슬라이더가 여기에 곱해진다")]
	[Range(0f, 1f)]
	public float maxVolume = 0.5f;

	private AudioSource bgmSource;

	private AudioSource sfxSource;

	// BGM 전환은 항상 하나만 돌아야 한다.
	// 페이드아웃과 페이드인이 동시에 돌면 서로 볼륨을 밀고 당기다가
	// 페이드아웃 쪽이 마지막에 Stop()을 불러 방금 켠 곡을 꺼버린다.
	private Coroutine bgmRoutine;

	/// <summary>설정값이 반영된 실제 BGM 목표 볼륨.</summary>
	private float TargetBgmVolume => maxVolume * GameSettings.BgmVolume;

	private void Awake()
	{
		if (Instance == null)
		{
			Instance = this;
			Object.DontDestroyOnLoad(base.gameObject);
			bgmSource = base.gameObject.AddComponent<AudioSource>();
			bgmSource.loop = true;
			bgmSource.playOnAwake = false;
			sfxSource = base.gameObject.AddComponent<AudioSource>();
			sfxSource.playOnAwake = false;
		}
		else
		{
			Object.Destroy(base.gameObject);
		}
	}

	public void PlayHomeBGM()
	{
		PlayBGM(homeBGM);
	}

	public void PlayInGameBGM()
	{
		PlayBGM(inGameBGM);
	}

	public void PlayBGM(AudioClip clip)
	{
		if (clip == null) { return; }

		// 이미 같은 곡이 정상 재생 중이면 건드리지 않는다
		if (bgmSource.clip == clip && bgmSource.isPlaying && bgmRoutine == null)
		{
			bgmSource.volume = TargetBgmVolume;
			return;
		}

		StartBgmRoutine(SwitchTo(clip));
	}

	public void StopBGMWithFade()
	{
		StartBgmRoutine(FadeOutAndStop());
	}

	private void StartBgmRoutine(IEnumerator routine)
	{
		if (bgmRoutine != null)
		{
			StopCoroutine(bgmRoutine);
			bgmRoutine = null;
		}
		bgmRoutine = StartCoroutine(routine);
	}

	private IEnumerator SwitchTo(AudioClip clip)
	{
		// 나가기
		if (bgmSource.isPlaying && bgmSource.volume > 0f)
		{
			yield return FadeVolume(bgmSource.volume, 0f);
		}

		bgmSource.Stop();
		bgmSource.clip = clip;
		bgmSource.volume = 0f;
		bgmSource.Play();

		// 들어오기
		yield return FadeVolume(0f, TargetBgmVolume);

		bgmSource.volume = TargetBgmVolume;
		bgmRoutine = null;
	}

	private IEnumerator FadeOutAndStop()
	{
		if (bgmSource.isPlaying)
		{
			yield return FadeVolume(bgmSource.volume, 0f);
		}
		bgmSource.Stop();
		bgmRoutine = null;
	}

	private IEnumerator FadeVolume(float from, float to)
	{
		if (fadeDuration <= 0f)
		{
			bgmSource.volume = to;
			yield break;
		}

		float elapsed = 0f;
		while (elapsed < fadeDuration)
		{
			// 게임오버 등으로 timeScale이 0이어도 페이드는 돌아야 한다
			elapsed += Time.unscaledDeltaTime;
			bgmSource.volume = Mathf.Lerp(from, to, elapsed / fadeDuration);
			yield return null;
		}
		bgmSource.volume = to;
	}

	public void PlayFullCountdownSFX()
	{
		PlaySFX(fullCountdownSFX);
	}

	public void PlaySFX(AudioClip clip)
	{
		if (clip == null || sfxSource == null) { return; }
		sfxSource.PlayOneShot(clip, GameSettings.SfxVolume);
	}

	/// <summary>설정 메뉴에서 BGM 볼륨을 바꿨을 때 즉시 반영한다.</summary>
	public void RefreshVolume()
	{
		if (bgmSource != null && bgmSource.isPlaying && bgmRoutine == null)
		{
			bgmSource.volume = TargetBgmVolume;
		}
	}
}
