using Setsuna;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// Rescue from a game softlock. PlayerControl.WalkUpdate refuses every step while the leader's body
    /// (a sphere of 0.9 x capsule radius at the capsule centre) overlaps any non-sphere HitGround collider,
    /// so once the party ends up inside a wall mesh it can't move in any direction (happened in Serendale;
    /// the only way out was reloading). The mod remembers where the leader last stood clear; after two
    /// seconds of pushing without moving while overlapping, it puts the leader back there and calls the
    /// game's own PlayerControl.ChangeScene() to settle height and movement state, as on entering a map.
    /// </summary>
    internal static class Unstick
    {
        private static Vector3 _good;
        private static bool _hasGood;
        private static float _nextSample, _stuckSince = -1f;
        private static Vector3 _stuckPos;
        private static string _scene;
        private static int _groundMask = -1;

        /// <summary>Per frame from Field.Tick, only while the party can be walked.</summary>
        public static void Tick(PlayerControl leader, bool pushing)
        {
            if (_groundMask == -1) _groundMask = LayerMask.GetMask("HitGround");
            var scene = Application.loadedLevelName;
            if (scene != _scene) { _scene = scene; _hasGood = false; _stuckSince = -1f; }
            if (leader == null || leader.Capsule == null) return;
            var now = Time.unscaledTime;
            var pos = leader.transform.position;

            if (now >= _nextSample)
            {
                _nextSample = now + 0.3f;
                if (!Overlapping(leader)) { _good = pos; _hasGood = true; }
            }

            if (!pushing) { _stuckSince = -1f; return; }
            if (_stuckSince < 0f || (pos - _stuckPos).sqrMagnitude > 0.05f * 0.05f) { _stuckSince = now; _stuckPos = pos; return; }
            if (now - _stuckSince < 2f || !_hasGood || !Overlapping(leader)) return;

            NavLog.Line("unstick: leader inside the scenery at " + NavLog.P(pos) + ", moved back to " + NavLog.P(_good));
            leader.transform.position = _good;
            var body = leader.GetComponent<Rigidbody>();
            if (body != null) body.velocity = Vector3.zero;
            leader.ChangeScene();
            _stuckSince = -1f;
            Speech.Say(Strings.Unstuck);
        }

        /// <summary>The game's own test from PlayerControl.WalkUpdate.</summary>
        private static bool Overlapping(PlayerControl leader)
        {
            var cap = leader.Capsule;
            var t = leader.transform;
            var ray = new Ray(new Vector3(t.position.x, t.position.y + cap.center.y, t.position.z),
                              new Vector3(t.forward.x, 0f, t.forward.z));
            var r = cap.radius * 0.9f;
            foreach (var hit in Physics.SphereCastAll(ray, r, 0f, _groundMask))
            {
                var sphere = hit.collider as SphereCollider;
                if (sphere != null && r + sphere.radius < (ray.origin - hit.transform.position).magnitude) continue;
                return true;
            }
            return false;
        }
    }
}
