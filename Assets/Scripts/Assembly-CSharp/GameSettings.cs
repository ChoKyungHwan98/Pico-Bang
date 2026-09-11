using UnityEngine;

/// <summary>
/// 플레이어가 바꿀 수 있는 설정값. PlayerPrefs에 저장된다.
///
/// 게임 로직은 이 정적 프로퍼티만 읽으면 되고,
/// 설정 UI는 이 프로퍼티만 쓰면 된다.
/// </summary>
public static class GameSettings
{
    private const string KeyMasterVolume = "opt.masterVolume";
    private const string KeyBgmVolume    = "opt.bgmVolume";
    private const string KeySfxVolume    = "opt.sfxVolume";
    private const string KeySensitivity  = "opt.mouseSensitivity";
    private const string KeyInvertY      = "opt.invertY";
    private const string KeyFullscreen   = "opt.fullscreen";

    // 기본값 — 기존 코드의 (감도 15 × Time.deltaTime)이 90fps에서 만들던 체감과
    // 비슷하게 맞췄다. 다만 이제는 프레임레이트에 흔들리지 않는다.
    public const float DefaultSensitivity = 0.15f;

    private static bool loaded;

    private static float masterVolume = 1f;
    private static float bgmVolume    = 0.7f;
    private static float sfxVolume    = 1f;
    private static float sensitivity  = DefaultSensitivity;
    private static bool  invertY;

    public static float MasterVolume
    {
        get { EnsureLoaded(); return masterVolume; }
        set { EnsureLoaded(); masterVolume = Mathf.Clamp01(value); ApplyAudio(); Save(KeyMasterVolume, masterVolume); }
    }

    public static float BgmVolume
    {
        get { EnsureLoaded(); return bgmVolume; }
        set { EnsureLoaded(); bgmVolume = Mathf.Clamp01(value); Save(KeyBgmVolume, bgmVolume); }
    }

    public static float SfxVolume
    {
        get { EnsureLoaded(); return sfxVolume; }
        set { EnsureLoaded(); sfxVolume = Mathf.Clamp01(value); Save(KeySfxVolume, sfxVolume); }
    }

    public static float MouseSensitivity
    {
        get { EnsureLoaded(); return sensitivity; }
        set { EnsureLoaded(); sensitivity = Mathf.Clamp(value, 0.01f, 1f); Save(KeySensitivity, sensitivity); }
    }

    public static bool InvertY
    {
        get { EnsureLoaded(); return invertY; }
        set { EnsureLoaded(); invertY = value; PlayerPrefs.SetInt(KeyInvertY, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public static bool Fullscreen
    {
        get { EnsureLoaded(); return Screen.fullScreen; }
        set
        {
            EnsureLoaded();
            Screen.fullScreenMode = value ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            PlayerPrefs.SetInt(KeyFullscreen, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    private static void EnsureLoaded()
    {
        if (loaded) { return; }
        loaded = true;

        masterVolume = PlayerPrefs.GetFloat(KeyMasterVolume, 1f);
        bgmVolume    = PlayerPrefs.GetFloat(KeyBgmVolume, 0.7f);
        sfxVolume    = PlayerPrefs.GetFloat(KeySfxVolume, 1f);
        sensitivity  = PlayerPrefs.GetFloat(KeySensitivity, DefaultSensitivity);
        invertY      = PlayerPrefs.GetInt(KeyInvertY, 0) == 1;

        ApplyAudio();
    }

    /// <summary>게임 시작 시 저장된 설정을 실제로 적용한다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyOnStartup()
    {
        EnsureLoaded();

        // 프레임 페이싱 — 상한 없이 돌면 프레임 간격이 들쭉날쭉해 화면이 튄다.
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = -1;   // vSync가 상한을 잡는다

        if (PlayerPrefs.HasKey(KeyFullscreen))
        {
            bool fs = PlayerPrefs.GetInt(KeyFullscreen, 1) == 1;
            Screen.fullScreenMode = fs ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }
    }

    private static void ApplyAudio()
    {
        AudioListener.volume = masterVolume;
    }

    private static void Save(string key, float value)
    {
        PlayerPrefs.SetFloat(key, value);
        PlayerPrefs.Save();
    }

    public static void ResetToDefaults()
    {
        MasterVolume = 1f;
        BgmVolume = 0.7f;
        SfxVolume = 1f;
        MouseSensitivity = DefaultSensitivity;
        InvertY = false;
    }
}
