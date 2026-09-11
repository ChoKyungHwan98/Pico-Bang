using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class SimpleStateMachine : MonoBehaviour
{
	public class State
	{
		public Action DoUpdate = DoNothing;

		public Action DoFixedUpdate = DoNothing;

		public Action DoLateUpdate = DoNothing;

		public Action DoManualUpdate = DoNothing;

		public Action enterState = DoNothing;

		public Action exitState = DoNothing;

		public Enum currentState;
	}

	public bool DebugGui;

	public Vector2 DebugGuiPosition;

	public string DebugGuiTitle = "Simple Machine";

	protected Enum queueCommand;

	protected float timeEnteredState;

	public State state = new State();

	[HideInInspector]
	public Enum lastState;

	private Dictionary<Enum, Dictionary<string, Delegate>> _cache = new Dictionary<Enum, Dictionary<string, Delegate>>();

	public Enum currentState
	{
		get
		{
			return state.currentState;
		}
		set
		{
			if (state.currentState != value)
			{
				ChangingState();
				state.currentState = value;
				ConfigureCurrentState();
			}
		}
	}

	private void OnGUI()
	{
		if (DebugGui)
		{
			GUI.Box(new Rect(DebugGuiPosition.x, DebugGuiPosition.y, 200f, 50f), DebugGuiTitle);
			GUI.TextField(new Rect(DebugGuiPosition.x + 10f, DebugGuiPosition.y + 20f, 180f, 20f), $"State: {currentState}");
		}
	}

	private void ChangingState()
	{
		lastState = state.currentState;
		timeEnteredState = Time.time;
	}

	private void ConfigureCurrentState()
	{
		if (state.exitState != null)
		{
			state.exitState();
		}
		state.DoUpdate = ConfigureDelegate<Action>("Update", DoNothing);
		state.DoFixedUpdate = ConfigureDelegate<Action>("FixedUpdate", DoNothing);
		state.DoLateUpdate = ConfigureDelegate<Action>("LateUpdate", DoNothing);
		state.DoManualUpdate = ConfigureDelegate<Action>("ManualUpdate", DoNothing);
		state.enterState = ConfigureDelegate<Action>("EnterState", DoNothing);
		state.exitState = ConfigureDelegate<Action>("ExitState", DoNothing);
		if (state.enterState != null)
		{
			state.enterState();
		}
	}

	private T ConfigureDelegate<T>(string methodRoot, T Default) where T : class
	{
		if (!_cache.TryGetValue(state.currentState, out var value))
		{
			value = (_cache[state.currentState] = new Dictionary<string, Delegate>());
		}
		if (!value.TryGetValue(methodRoot, out var value2))
		{
			MethodInfo method = GetType().GetMethod(state.currentState.ToString() + "_" + methodRoot, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.InvokeMethod);
			value2 = (value[methodRoot] = ((!(method != null)) ? (Default as Delegate) : Delegate.CreateDelegate(typeof(T), this, method)));
		}
		return value2 as T;
	}

	private void Update()
	{
		EarlyGlobalSuperUpdate();
		state.DoUpdate();
		LateGlobalSuperUpdate();
	}

	private void FixedUpdate()
	{
		state.DoFixedUpdate();
	}

	private void LateUpdate()
	{
		state.DoLateUpdate();
	}

	protected virtual void EarlyGlobalSuperUpdate()
	{
	}

	protected virtual void LateGlobalSuperUpdate()
	{
	}

	private static void DoNothing()
	{
	}
}
