using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LayerLab.GUIScripts
{
	public class PanelControl : MonoBehaviour
	{
		private int _page;

		private bool _isReady;

		private TextMeshProUGUI _textTitle;

		[SerializeField]
		private List<GameObject> defaultPanels = new List<GameObject>();

		[SerializeField]
		private List<GameObject> otherPanels = new List<GameObject>();

		[SerializeField]
		private Transform panelTransformDefault;

		[SerializeField]
		private Transform panelTransformOther;

		[SerializeField]
		private Button buttonPrev;

		[SerializeField]
		private Button buttonNext;

		private bool IsOtherMode { get; set; }

		private void OnValidate()
		{
			GameObject gameObject = GameObject.Find("Panels");
			if ((bool)gameObject)
			{
				panelTransformDefault = gameObject.transform;
			}
			buttonPrev = base.transform.GetChild(0).GetComponent<Button>();
			buttonNext = base.transform.GetChild(2).GetComponent<Button>();
		}

		private void Reset()
		{
			OnValidate();
		}

		private void Start()
		{
			_textTitle = base.transform.GetComponentInChildren<TextMeshProUGUI>();
			buttonPrev.onClick.AddListener(Click_Prev);
			buttonNext.onClick.AddListener(Click_Next);
			foreach (Transform item in panelTransformDefault)
			{
				defaultPanels.Add(item.gameObject);
				item.gameObject.SetActive(value: false);
			}
			defaultPanels[_page].SetActive(value: true);
			if (panelTransformOther == null)
			{
				return;
			}
			foreach (Transform item2 in panelTransformOther)
			{
				otherPanels.Add(item2.gameObject);
				item2.gameObject.SetActive(value: false);
			}
			if (otherPanels.Count > 0)
			{
				otherPanels[_page].SetActive(value: true);
			}
			_isReady = true;
			CheckControl();
		}

		private void Update()
		{
			if (defaultPanels.Count > 0 && _isReady)
			{
				if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
				{
					Click_Prev();
				}
				else if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
				{
					Click_Next();
				}
			}
		}

		private void Click_Prev()
		{
			if (_page > 0)
			{
				defaultPanels[_page].SetActive(value: false);
				if (otherPanels.Count > 0)
				{
					otherPanels[_page].SetActive(value: false);
				}
				_page--;
				defaultPanels[_page].SetActive(value: true);
				if (otherPanels.Count > 0)
				{
					otherPanels[_page].SetActive(value: true);
				}
				if (!IsOtherMode)
				{
					_textTitle.text = defaultPanels[_page].name;
				}
				else if (otherPanels.Count > 0)
				{
					_textTitle.text = otherPanels[_page].name;
				}
				CheckControl();
			}
		}

		private void Click_Next()
		{
			if (_page < defaultPanels.Count - 1)
			{
				defaultPanels[_page].SetActive(value: false);
				if (otherPanels.Count > 0)
				{
					otherPanels[_page].SetActive(value: false);
				}
				_page++;
				defaultPanels[_page].SetActive(value: true);
				if (otherPanels.Count > 0)
				{
					otherPanels[_page].SetActive(value: true);
				}
				CheckControl();
			}
		}

		private void SetArrowActive()
		{
			buttonPrev.gameObject.SetActive(_page > 0);
			buttonNext.gameObject.SetActive(_page < defaultPanels.Count - 1);
		}

		private void CheckControl()
		{
			if (!IsOtherMode)
			{
				_textTitle.text = defaultPanels[_page].name.Replace("_", " ");
			}
			else if (otherPanels.Count > 0)
			{
				_textTitle.text = otherPanels[_page].name.Replace("_", " ");
			}
			SetArrowActive();
		}

		public void Click_Mode()
		{
			IsOtherMode = !IsOtherMode;
			SetMode();
			CheckControl();
		}

		private void SetMode()
		{
			panelTransformDefault.gameObject.SetActive(!IsOtherMode);
			if (otherPanels.Count > 0)
			{
				panelTransformOther.gameObject.SetActive(IsOtherMode);
			}
		}
	}
}
