using System;

namespace RPGCharacterAnims.Actions
{
	public interface IActionHandler
	{
		bool CanStartAction(RPGCharacterController controller);

		void StartAction(RPGCharacterController controller, object context);

		void AddStartListener(Action callback);

		void RemoveStartListener(Action callback);

		bool IsActive();

		bool CanEndAction(RPGCharacterController controller);

		void EndAction(RPGCharacterController controller);

		void AddEndListener(Action callback);

		void RemoveEndListener(Action callback);
	}
}
