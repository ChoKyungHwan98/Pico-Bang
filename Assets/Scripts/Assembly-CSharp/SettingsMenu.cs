using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 설정 메뉴. <see cref="GameSettings"/>를 읽고 쓰는 얇은 껍데기.
///
/// 패널 자체는 항상 활성 상태로 두고 CanvasGroup으로 켜고 끈다.
/// (비활성으로 껐다 켜면 Awake 타이밍이 꼬여서 슬라이더 초기화가 어긋난다)
/// </summary>
public class SettingsMenu : MonoBehaviour
{
	[Header("Root")]
	public CanvasGroup group;

	[Header("Sliders")]
	public Slider masterSlider;
	public Slider bgmSlider;
	public Slider sfxSlider;
	public Slider sensitivitySlider;

	[Header("Value Labels")]
	public TMP_Text masterValue;
	public TMP_Text bgmValue;
	public TMP_Text sfxValue;
	public TMP_Text sensitivityValue;

	[Header("Toggles & Buttons")]
	public Toggle fullscreenToggle;
	public Button closeButton;
	public Button resetButton;

	private bool isOpen;
	private bool suppressCallbacks;

	public bool IsOpen => isOpen;

	private void Awake()
	{
		if (group == null) { group = GetComponent<CanvasGroup>(); }

		if (masterSlider != null)      { masterSlider.onValueChanged.AddListener(OnMasterChanged); }
		if (bgmSlider != null)         { bgmSlider.onValueChanged.AddListener(OnBgmChanged); }
		if (sfxSlider != null)         { sfxSlider.onValueChanged.AddListener(OnSfxChanged); }
		if (sensitivitySlider != null) { sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged); }
		if (fullscreenToggle != null)  { fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged); }
		if (closeButton != null)       { closeButton.onClick.AddListener(Close); }
		if (resetButton != null)       { resetButton.onClick.AddListener(ResetDefaults); }

		Close();
	}

	private void Update()
	{
		if (!isOpen) { return; }

		Keyboard kb = Keyboard.current;
		if (kb != null && kb.escapeKey.wasPressedThisFrame)
		{
			Close();
		}
	}

	public void Open()
	{
		PullFromSettings();
		isOpen = true;
		ApplyGroup(1f, interactable: true);
	}

	public void Close()
	{
		isOpen = false;
		ApplyGroup(0f, interactable: false);
	}

	public void Toggle()
	{
		if (isOpen) { Close(); } else { Open(); }
	}

	private void ApplyGroup(float alpha, bool interactable)
	{
		if (group == null) { return; }
		group.alpha = alpha;
		group.interactable = interactable;
		group.blocksRaycasts = interactable;
	}

	/// <summary>저장된 설정값을 UI에 반영한다. 콜백이 되먹임되지 않도록 잠근다.</summary>
	private void PullFromSettings()
	{
		suppressCallbacks = true;

		if (masterSlider != null)      { masterSlider.SetValueWithoutNotify(GameSettings.MasterVolume); }
		if (bgmSlider != null)         { bgmSlider.SetValueWithoutNotify(GameSettings.BgmVolume); }
		if (sfxSlider != null)         { sfxSlider.SetValueWithoutNotify(GameSettings.SfxVolume); }
		if (sensitivitySlider != null) { sensitivitySlider.SetValueWithoutNotify(GameSettings.MouseSensitivity); }
		if (fullscreenToggle != null)  { fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen); }

		suppressCallbacks = false;
		RefreshLabels();
	}

	private void RefreshLabels()
	{
		if (masterValue != null)      { masterValue.text = Percent(GameSettings.MasterVolume); }
		if (bgmValue != null)         { bgmValue.text = Percent(GameSettings.BgmVolume); }
		if (sfxValue != null)         { sfxValue.text = Percent(GameSettings.SfxVolume); }
		if (sensitivityValue != null) { sensitivityValue.text = GameSettings.MouseSensitivity.ToString("0.00"); }
	}

	private static string Percent(float v) => Mathf.RoundToInt(v * 100f) + "%";

	// ── 콜백 ────────────────────────────────────────────

	private void OnMasterChanged(float v)
	{
		if (suppressCallbacks) { return; }
		GameSettings.MasterVolume = v;
		RefreshLabels();
	}

	private void OnBgmChanged(float v)
	{
		if (suppressCallbacks) { return; }
		GameSettings.BgmVolume = v;
		if (SoundManager.Instance != null) { SoundManager.Instance.RefreshVolume(); }
		RefreshLabels();
	}

	private void OnSfxChanged(float v)
	{
		if (suppressCallbacks) { return; }
		GameSettings.SfxVolume = v;
		RefreshLabels();
	}

	private void OnSensitivityChanged(float v)
	{
		if (suppressCallbacks) { return; }
		GameSettings.MouseSensitivity = v;
		RefreshLabels();
	}

	private void OnFullscreenChanged(bool on)
	{
		if (suppressCallbacks) { return; }
		GameSettings.Fullscreen = on;
	}

	public void ResetDefaults()
	{
		GameSettings.ResetToDefaults();
		if (SoundManager.Instance != null) { SoundManager.Instance.RefreshVolume(); }
		PullFromSettings();
	}
}
