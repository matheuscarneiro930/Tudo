using UnityEngine;

namespace Fodinha.Game.UI
{
    /// <summary>
    /// Geometria da mesa em retrato. O jogador do aparelho fica embaixo (fora da oval);
    /// os adversários se distribuem em sentido horário pela oval, de baixo-esquerda até baixo-direita,
    /// na mesma ordem dos assentos do motor.
    /// </summary>
    public static class TableLayout
    {
        /// <summary>Raio horizontal/vertical (fração da área da mesa) onde ficam os adversários.</summary>
        public const float SeatRadiusX = 0.385f;
        public const float SeatRadiusY = 0.40f;

        /// <summary>Fração do caminho centro → assento onde ficam as cartas jogadas na vaza.</summary>
        public const float TrickDistance = 0.55f; // deixa o meio livre para a vira sem encostar nos assentos

        private const float StartAngle = 225f;   // baixo-esquerda
        private const float SweepAngle = 270f;   // até baixo-direita, passando pelo topo

        /// <summary>
        /// Direção (no círculo unitário) do adversário de índice relativo <paramref name="relative"/> (1..opponents),
        /// onde 1 é o próximo jogador depois do jogador local em sentido horário.
        /// </summary>
        public static Vector2 OpponentDirection(int relative, int opponents)
        {
            if (opponents <= 0) return Vector2.up;
            float t = (relative - 0.5f) / opponents;
            float angle = (StartAngle - SweepAngle * t) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        /// <summary>Direção do jogador local (embaixo).</summary>
        public static Vector2 LocalDirection => Vector2.down;

        /// <summary>Âncora (0..1 dentro da área da mesa) do assento numa direção.</summary>
        public static Vector2 SeatAnchor(Vector2 direction)
        {
            return new Vector2(0.5f + direction.x * SeatRadiusX, 0.5f + direction.y * SeatRadiusY);
        }

        /// <summary>
        /// Empurra o assento para dentro da área quando ele sairia pela borda (assentos das laterais).
        /// Sem tamanho conhecido da área (0), devolve a âncora como está.
        /// </summary>
        public static Vector2 ClampSeatAnchor(Vector2 anchor, Vector2 seatSize, Vector2 areaSize)
        {
            const float margin = 6f;
            if (areaSize.x > 0f)
            {
                float half = (seatSize.x * 0.5f + margin) / areaSize.x;
                anchor.x = half >= 0.5f ? 0.5f : Mathf.Clamp(anchor.x, half, 1f - half);
            }
            if (areaSize.y > 0f)
            {
                float half = (seatSize.y * 0.5f + margin) / areaSize.y;
                anchor.y = half >= 0.5f ? 0.5f : Mathf.Clamp(anchor.y, half, 1f - half);
            }
            return anchor;
        }

        /// <summary>Âncora (0..1) da carta jogada por quem está naquela direção.</summary>
        public static Vector2 TrickAnchor(Vector2 direction)
        {
            return new Vector2(0.5f + direction.x * SeatRadiusX * TrickDistance,
                               0.5f + direction.y * SeatRadiusY * TrickDistance);
        }

        /// <summary>Escala dos widgets dos adversários conforme a lotação da mesa.</summary>
        public static float SeatScale(int opponents)
        {
            if (opponents <= 4) return 1f;
            if (opponents <= 7) return 0.8f;
            if (opponents <= 10) return 0.7f;
            if (opponents <= 12) return 0.54f;
            return 0.47f; // 14–15 na mesa: os dois do topo ficam lado a lado
        }

        /// <summary>Altura das cartas da vaza no centro, conforme a lotação.</summary>
        public static float TrickCardHeight(int players)
        {
            if (players <= 4) return 210f;
            if (players <= 7) return 180f;
            if (players <= 10) return 150f;
            return 120f;
        }

        /// <summary>Índice relativo de um assento em relação ao jogador local (0 = o próprio).</summary>
        public static int Relative(int seat, int localSeat, int playerCount)
        {
            if (localSeat < 0) return seat + 1; // espectador: todos ao redor da mesa
            return ((seat - localSeat) % playerCount + playerCount) % playerCount;
        }
    }
}
