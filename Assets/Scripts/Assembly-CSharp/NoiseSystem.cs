using UnityEngine;

/// <summary>
/// 소음 전파 시스템.
///
/// 게임 내에서 "소리가 나는 사건"은 전부 이곳을 거친다.
/// 소리를 낸 쪽은 어떤 몬스터가 듣는지 알 필요가 없고,
/// 듣는 쪽은 누가 소리를 냈는지 알 필요가 없다.
///
/// 기획 의도: 플레이어의 진행 행동(사격)은 반드시 위험을 동반한다.
/// </summary>
public enum NoiseKind
{
    /// <summary>총을 쐈다. 발생 위치 = 플레이어. 쏘는 순간 자기 위치가 노출된다.</summary>
    Shot = 0,

    /// <summary>과녁이 부서졌다. 발생 위치 = 과녁. 플레이어 위치와 다르다.</summary>
    TargetDestroyed = 1
}

public static class NoiseSystem
{
    public static bool DrawDebug = true;

    /// <summary>
    /// 소음을 발생시킨다. 살아 있는 모든 몬스터에게 전달되고,
    /// 실제로 들리는지는 각 몬스터가 자기 청각 반경으로 판정한다.
    /// </summary>
    /// <param name="position">소리가 난 지점 (플레이어 위치가 아닐 수 있음)</param>
    /// <param name="radius">이 소리의 기본 도달 반경</param>
    public static void Emit(Vector3 position, float radius, NoiseKind kind)
    {
        if (radius <= 0f) { return; }

        // 역순 순회 — 반응 도중 몬스터가 비활성화돼도 안전하도록.
        var monsters = MonsterAI.activeMonsters;
        for (int i = monsters.Count - 1; i >= 0; i--)
        {
            MonsterAI m = monsters[i];
            if (m != null) { m.OnHearNoise(position, radius, kind); }
        }

        if (DrawDebug)
        {
            Color c = (kind == NoiseKind.Shot) ? Color.yellow : new Color(1f, 0.5f, 0f);
            DrawDebugRing(position, radius, c, 1.5f);
        }
    }

    private static void DrawDebugRing(Vector3 center, float radius, Color color, float duration)
    {
        const int segments = 24;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Debug.DrawLine(prev, next, color, duration);
            prev = next;
        }
    }
}
