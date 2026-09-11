using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class SuperStateMachine : MonoBehaviour
{
	public class State
	{
		public Action DoSuperUpdate = DoNothing;

		public Action enterState = DoNothing;

		public Action exitState = DoNothing;

		public Enum currentState;
	}

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
		state.DoSuperUpdate = ConfigureDelegate<Action>("SuperUpdate", DoNothing);
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

	private void SuperUpdate()
	{
		EarlyGlobalSuperUpdate();
		state.DoSuperUpdate();
		LateGlobalSuperUpdate();
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
