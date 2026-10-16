using System.Collections.Generic;
using UnityEngine;

namespace DroneHud
{
    /// <summary>
    /// Fakes a small Crazyflie swarm: staggered take-off, orbits at different radii,
    /// battery drain with auto-landing, and occasional link dropouts.
    /// </summary>
    public class SimulatedTelemetrySource : DroneTelemetrySource
    {
        [SerializeField] private int m_droneCount = 10;
        [SerializeField] private float m_cruiseAltitude = 1.2f;
        [SerializeField] private float m_takeoffStagger = 0.8f;
        [SerializeField] private float m_autoLandPercent = 12f;
        [SerializeField] private Vector2 m_drainVoltsPerSecond = new(0.003f, 0.01f);
        [SerializeField] private float m_linkDropoutInterval = 20f;
        [SerializeField] private float m_linkDropoutDuration = 4f;

        private const float ClimbRate = 0.5f;
        private const float DescentRate = 0.4f;

        private readonly List<DroneState> m_drones = new();
        private float[] m_drainRates;
        private float[] m_orbitRadius;
        private float[] m_orbitSpeed;
        private float[] m_orbitAngle;
        private float[] m_takeoffAt;
        private float[] m_linkLostUntil;
        private float m_nextDropout;

        public override string SourceName => "SIM";
        public override IReadOnlyList<DroneState> Drones => m_drones;

        private void Awake()
        {
            m_drainRates = new float[m_droneCount];
            m_orbitRadius = new float[m_droneCount];
            m_orbitSpeed = new float[m_droneCount];
            m_orbitAngle = new float[m_droneCount];
            m_takeoffAt = new float[m_droneCount];
            m_linkLostUntil = new float[m_droneCount];

            for (var i = 0; i < m_droneCount; i++)
            {
                m_drainRates[i] = Random.Range(m_drainVoltsPerSecond.x, m_drainVoltsPerSecond.y);
                m_orbitRadius[i] = 0.5f + (i % 3) * 0.5f;
                m_orbitSpeed[i] = Random.Range(0.3f, 0.6f) * (i % 2 == 0 ? 1f : -1f);
                m_orbitAngle[i] = i * (2f * Mathf.PI / m_droneCount);
                m_takeoffAt[i] = Time.time + 1f + i * m_takeoffStagger;

                m_drones.Add(new DroneState
                {
                    Id = i,
                    Name = $"CF{i + 1:00}",
                    State = DroneFlightState.Idle,
                    Position = OrbitPoint(i, 0f),
                    BatteryVoltage = Random.Range(3.9f, DroneState.BatteryFullVoltage),
                    LinkQuality = 100f,
                    LastUpdateTime = Time.time,
                });
            }

            m_nextDropout = Time.time + m_linkDropoutInterval;
        }

        private void Update()
        {
            var now = Time.time;
            var dt = Time.deltaTime;

            if (now >= m_nextDropout && m_droneCount > 0)
            {
                m_linkLostUntil[Random.Range(0, m_droneCount)] = now + m_linkDropoutDuration;
                m_nextDropout = now + m_linkDropoutInterval * Random.Range(0.5f, 1.5f);
            }

            for (var i = 0; i < m_drones.Count; i++)
            {
                // While the link is down we simply stop updating, so the HUD sees stale data.
                if (now < m_linkLostUntil[i])
                {
                    continue;
                }

                var d = m_drones[i];
                var previous = d.Position;
                var altitude = previous.y;
                var airborne = d.State is DroneFlightState.TakingOff or DroneFlightState.Flying or DroneFlightState.Landing;

                switch (d.State)
                {
                    case DroneFlightState.Idle:
                        if (now >= m_takeoffAt[i])
                        {
                            d.State = DroneFlightState.TakingOff;
                        }
                        break;
                    case DroneFlightState.TakingOff:
                        altitude += ClimbRate * dt;
                        if (altitude >= m_cruiseAltitude)
                        {
                            altitude = m_cruiseAltitude;
                            d.State = DroneFlightState.Flying;
                        }
                        break;
                    case DroneFlightState.Flying:
                        m_orbitAngle[i] += m_orbitSpeed[i] * dt;
                        altitude = m_cruiseAltitude + 0.1f * Mathf.Sin(now * 0.7f + i);
                        if (d.BatteryPercent <= m_autoLandPercent)
                        {
                            d.State = DroneFlightState.Landing;
                        }
                        break;
                    case DroneFlightState.Landing:
                        altitude -= DescentRate * dt;
                        if (altitude <= 0f)
                        {
                            altitude = 0f;
                            d.State = DroneFlightState.Landed;
                        }
                        break;
                }

                var position = OrbitPoint(i, altitude);
                d.Velocity = dt > 0f ? (position - previous) / dt : Vector3.zero;
                d.Position = position;
                d.YawDegrees = Mathf.Repeat(-m_orbitAngle[i] * Mathf.Rad2Deg, 360f);
                d.BatteryVoltage -= m_drainRates[i] * dt * (airborne ? 1f : 0.05f);
                d.LinkQuality = Mathf.Clamp(80f + 20f * Mathf.PerlinNoise(i * 10f, now * 0.3f), 0f, 100f);
                d.LastUpdateTime = now;
            }
        }

        private Vector3 OrbitPoint(int i, float altitude) =>
            new(Mathf.Cos(m_orbitAngle[i]) * m_orbitRadius[i], altitude, Mathf.Sin(m_orbitAngle[i]) * m_orbitRadius[i]);
    }
}
