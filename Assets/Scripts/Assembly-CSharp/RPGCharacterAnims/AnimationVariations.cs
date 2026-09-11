using RPGCharacterAnims.Lookups;

namespace RPGCharacterAnims
{
	public class AnimationVariations
	{
		public static readonly KnockbackType[] Knockbacks = new KnockbackType[2]
		{
			KnockbackType.Knockback1,
			KnockbackType.Knockback2
		};

		public static readonly KnockdownType[] Knockdowns = new KnockdownType[1] { KnockdownType.Knockdown1 };

		public static readonly HitType[] Hits = new HitType[5]
		{
			HitType.Forward1,
			HitType.Forward2,
			HitType.Back1,
			HitType.Left1,
			HitType.Right1
		};

		public static readonly TwoHandedSwordAttack[] TwoHandedSwordAttacks = new TwoHandedSwordAttack[11]
		{
			TwoHandedSwordAttack.Attack1,
			TwoHandedSwordAttack.Attack2,
			TwoHandedSwordAttack.Attack3,
			TwoHandedSwordAttack.Attack4,
			TwoHandedSwordAttack.Attack5,
			TwoHandedSwordAttack.Attack6,
			TwoHandedSwordAttack.Attack7,
			TwoHandedSwordAttack.Attack8,
			TwoHandedSwordAttack.Attack9,
			TwoHandedSwordAttack.Attack10,
			TwoHandedSwordAttack.Attack11
		};

		public static readonly UnarmedAttack[] UnarmedLeftAttacks = new UnarmedAttack[3]
		{
			UnarmedAttack.LeftAttack1,
			UnarmedAttack.LeftAttack2,
			UnarmedAttack.LeftAttack3
		};

		public static readonly UnarmedAttack[] UnarmedRightAttacks = new UnarmedAttack[3]
		{
			UnarmedAttack.RightAttack1,
			UnarmedAttack.RightAttack2,
			UnarmedAttack.RightAttack3
		};
	}
}
