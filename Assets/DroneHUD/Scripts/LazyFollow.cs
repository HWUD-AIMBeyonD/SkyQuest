using UnityEngine;

namespace DroneHud
{
    /// <summary>
    /// Keeps a world-space panel in front of the user without rigidly head-locking it:
    /// the panel stays put until you look far enough away, then glides back into view.
    /// </summary>
    public class LazyFollow : MonoBehaviour
    {
        [SerializeField] private float m_distance = 1.0f;
        [SerializeField] private float m_verticalOffset = -0.1f;
        [SerializeField] private float m_angleThreshold = 30f;
        [SerializeField] private float m_distanceThreshold = 0.3f;
        [SerializeField] private float m_smoothTime = 0.35f;

        private Transform m_head;
        private Vector3 m_velocity;
        private bool m_isMoving;

        public void Recenter()
        {
            if (!TryGetHead())
            {
                return;
            }
            transform.position = TargetPosition();
            m_velocity = Vector3.zero;
            m_isMoving = false;
            FaceHead();
        }

        private void Start() => Recenter();

        private void LateUpdate()
        {
            if (!TryGetHead())
            {
                return;
            }

            var target = TargetPosition();
            var toPanel = transform.position - m_head.position;
            var angle = Vector3.Angle(Flatten(m_head.forward), Flatten(toPanel));
            var distanceError = Mathf.Abs(toPanel.magnitude - m_distance);

            if (!m_isMoving && (angle > m_angleThreshold || distanceError > m_distanceThreshold))
            {
                m_isMoving = true;
            }

            if (m_isMoving)
            {
                transform.position = Vector3.SmoothDamp(transform.position, target, ref m_velocity, m_smoothTime);
                if ((transform.position - target).sqrMagnitude < 0.0004f)
                {
                    m_isMoving = false;
                }
            }

            FaceHead();
        }

        private bool TryGetHead()
        {
            if (m_head == null && Camera.main != null)
            {
                m_head = Camera.main.transform;
            }
            return m_head != null;
        }

        private Vector3 TargetPosition()
        {
            var forward = Flatten(m_head.forward);
            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = Vector3.forward;
            }
            return m_head.position + forward.normalized * m_distance + Vector3.up * m_verticalOffset;
        }

        private void FaceHead() =>
            transform.rotation = Quaternion.LookRotation(transform.position - m_head.position, Vector3.up);

        private static Vector3 Flatten(Vector3 v) => new(v.x, 0f, v.z);
    }
}
