/// <summary>
    /// Operaciones seguras para los relojes circulares de Photon.
    /// </summary>
    public static class PhotonTimestamp
    {
        private const double NetworkTimeWrapSeconds = 4294967.296d;

        /// <summary>
        /// Devuelve newer - older incluso cuando ServerTimestamp pasó de
        /// int.MaxValue a int.MinValue. Los instantes deben estar separados por
        /// menos de aproximadamente 24,8 días.
        /// </summary>
        public static int DeltaMilliseconds(int newer, int older)
        {
            return unchecked(newer - older);
        }

        public static bool IsEarlier(int candidate, int current)
        {
            return DeltaMilliseconds(candidate, current) < 0;
        }

        /// <summary>
        /// Calcula segundos transcurridos con PhotonNetwork.Time aunque el reloj
        /// haya vuelto a cero.
        /// </summary>
        public static double ElapsedNetworkSeconds(double now, double startedAt)
        {
            double elapsed = now - startedAt;
            return elapsed >= 0d ? elapsed : elapsed + NetworkTimeWrapSeconds;
        }
    }
