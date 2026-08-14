using OWML.Common;
using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Return
{
    internal static class InterloperTrajectoryController
    {
        private const string SettingsFileName = "interloper-orbit.json";

        private static float _sceneSixEpoch = float.NaN;

        public static float TerminalLoopTimeSeconds { get; private set; } =
            1320f;

        /// <summary>
        /// Seconds elapsed since the Scene Six trajectory was applied. This is
        /// the mod's own stopwatch, anchored to the moment Brittle Hollow
        /// gameplay starts, so time spent in Scenes 1-5 can never leak into
        /// the revival countdown or the Interloper schedule.
        /// </summary>
        public static float GetSceneSixElapsedSeconds()
        {
            if (float.IsNaN(_sceneSixEpoch))
            {
                return 0f;
            }
            return Mathf.Max(0f, Time.timeSinceLevelLoad - _sceneSixEpoch);
        }

        public static int GetRevivalMinutesRemaining()
        {
            float secondsRemaining = Mathf.Max(
                0f,
                TerminalLoopTimeSeconds - GetSceneSixElapsedSeconds()
            );
            return Mathf.CeilToInt(secondsRemaining / 60f);
        }

        public static void Apply(ReturnMod mod)
        {
            if (mod == null)
            {
                return;
            }

            OrbitSettings settings = LoadSettings(mod);
            if (settings == null || !settings.enabled)
            {
                mod.ModHelper.Console.WriteLine(
                    "[RETURN INTERLOPER] Custom trajectory is disabled.",
                    MessageType.Info
                );
                return;
            }

            OWRigidbody sun = FindBody(settings.sunBodyName);
            OWRigidbody comet = FindBody(settings.cometBodyName);
            OWRigidbody target = FindBody(settings.targetBodyName);
            if (sun == null || comet == null || target == null)
            {
                mod.ModHelper.Console.WriteLine(
                    "[RETURN INTERLOPER] Could not find the configured Sun, " +
                    "Interloper, or interception target.",
                    MessageType.Error
                );
                return;
            }

            FrozenSunProgressionController.Install(sun, mod);

            GravityVolume sunGravity = sun.GetAttachedGravityVolume();
            float gravitationalParameter = sunGravity == null
                ? 0f
                : sunGravity.GetStandardGravitationalParameter();
            if (gravitationalParameter <= 0f)
            {
                mod.ModHelper.Console.WriteLine(
                    "[RETURN INTERLOPER] The Sun's gravitational parameter " +
                    "could not be read.",
                    MessageType.Error
                );
                return;
            }

            // Scene Six is its own clock: the Interloper schedule starts at
            // zero right now, never inheriting time spent in Scenes 1-5.
            _sceneSixEpoch = Time.timeSinceLevelLoad;
            float currentLoopSeconds = 0f;
            float secondsToIntercept =
                settings.interceptTimeMinutes * 60f -
                currentLoopSeconds;
            if (secondsToIntercept < settings.minimumInterceptLeadSeconds)
            {
                mod.ModHelper.Console.WriteLine(
                    "[RETURN INTERLOPER] The configured interception time " +
                    "has already passed or is too close.",
                    MessageType.Error
                );
                return;
            }

            OrbitalState targetNow = new OrbitalState(
                target.GetPosition() - sun.GetPosition(),
                target.GetVelocity() - sun.GetVelocity()
            );
            float targetPredictionSeconds =
                secondsToIntercept + settings.targetPredictionLeadSeconds;
            // The White Hole Station is a kinematic body fixed in the solar
            // system rather than a free object falling through the Sun's
            // gravity. Predict it from its actual rigidbody velocity.
            OrbitalState targetAtIntercept = new OrbitalState(
                targetNow.position +
                    targetNow.velocity * targetPredictionSeconds,
                targetNow.velocity
            );
            // Put the future black-hole point on the station's Sun-facing
            // radial line, but farther away from the Sun. This makes the
            // clearance independent of the station's rotating local axes.
            Vector3 stationOutwardDirection =
                targetAtIntercept.position.normalized;
            float trajectoryCenterOffset =
                settings.interceptCenterDistanceMeters;
            targetAtIntercept.position +=
                stationOutwardDirection *
                trajectoryCenterOffset;
            targetAtIntercept.position += target.transform.TransformDirection(
                new Vector3(
                    settings.interceptOffsetTargetLocalX,
                    settings.interceptOffsetTargetLocalY,
                    settings.interceptOffsetTargetLocalZ
                )
            );

            OrbitalState startState = CalculateIncomingStartState(
                targetAtIntercept,
                secondsToIntercept,
                gravitationalParameter,
                settings,
                out float interceptRadialSpeed,
                out float interceptTangentialSpeed
            );
            OrbitalState predictedArrival = SimulateState(
                startState,
                secondsToIntercept,
                gravitationalParameter,
                settings.simulationStepSeconds
            );
            float predictedMissDistance = Vector3.Distance(
                predictedArrival.position,
                targetAtIntercept.position
            );
            float terminalRadius =
                settings.sunInitialRadiusMeters +
                settings.solarSurfaceClearanceMeters;
            float secondsUntilTerminal = FindTimeToPeriapsis(
                startState,
                gravitationalParameter,
                settings.simulationStepSeconds,
                settings.mapTrajectoryPreviewMinutes * 60f
            );
            TerminalLoopTimeSeconds =
                currentLoopSeconds + secondsUntilTerminal;

            InitialMotion originalMotion = comet.GetComponent<InitialMotion>();
            if (originalMotion != null)
            {
                originalMotion.enabled = false;
            }

            comet.WarpToPositionRotation(
                sun.GetPosition() + startState.position,
                comet.GetRotation()
            );
            comet.SetVelocity(sun.GetVelocity() + startState.velocity);
            Physics.SyncTransforms();

            InterloperMapTrajectoryLine.Install(
                comet,
                sun,
                gravitationalParameter,
                settings.mapTrajectoryPreviewMinutes * 60f,
                settings.mapTrajectoryPointCount,
                settings.mapRefreshIntervalSeconds,
                settings.mapSimulationStepSeconds,
                settings.mapMinimumSolarRadiusMeters,
                mod
            );
            InterloperTerminalController.Install(
                comet,
                sun,
                terminalRadius,
                settings.terminalRadiusToleranceMeters,
                mod
            );
            float radialVelocity = Vector3.Dot(
                startState.velocity,
                startState.position.normalized
            );
            mod.ModHelper.Console.WriteLine(
                "[RETURN INTERLOPER] Applied inbound trajectory. Start " +
                "distance=" + Format(startState.position.magnitude) +
                " m; initial speed=" + Format(startState.velocity.magnitude) +
                " m/s; radial speed=" + Format(radialVelocity) +
                " m/s; interception=" +
                Format(settings.interceptTimeMinutes) +
                " min; intercept radial speed=" +
                Format(-interceptRadialSpeed) +
                " m/s; intercept tangential speed=" +
                Format(interceptTangentialSpeed) +
                " m/s; station center distance=" +
                Format(trajectoryCenterOffset) +
                " m; solar surface clearance=" +
                Format(settings.solarSurfaceClearanceMeters) +
                " m; solar-center periapsis=" +
                Format(
                    settings.sunInitialRadiusMeters +
                    settings.solarSurfaceClearanceMeters
                ) +
                " m; terminal loop time=" +
                Format(TerminalLoopTimeSeconds / 60f) +
                " min; revival display=" +
                GetRevivalMinutesRemaining() +
                " min;" +
                " predicted miss=" +
                Format(predictedMissDistance) + " m.",
                MessageType.Success
            );

            if (radialVelocity >= 0f)
            {
                mod.ModHelper.Console.WriteLine(
                    "[RETURN INTERLOPER] The calculated initial velocity was " +
                    "not inbound, so the trajectory was not safe to use.",
                    MessageType.Error
                );
            }
        }

        private static OrbitalState CalculateIncomingStartState(
            OrbitalState targetAtIntercept,
            float duration,
            float gravitationalParameter,
            OrbitSettings settings,
            out float chosenRadialSpeed,
            out float chosenTangentialSpeed
        )
        {
            Vector3 radialDirection = targetAtIntercept.position.normalized;
            Vector3 targetTangentialVelocity =
                targetAtIntercept.velocity -
                radialDirection * Vector3.Dot(
                    targetAtIntercept.velocity,
                    radialDirection
                );
            Vector3 tangentDirection = targetTangentialVelocity.normalized;
            if (tangentDirection.sqrMagnitude < 0.5f)
            {
                tangentDirection = Vector3.Cross(
                    Vector3.up,
                    radialDirection
                ).normalized;
                if (tangentDirection.sqrMagnitude < 0.5f)
                {
                    tangentDirection = Vector3.Cross(
                        Vector3.right,
                        radialDirection
                    ).normalized;
                }
            }

            float interceptRadius =
                Mathf.Max(1f, targetAtIntercept.position.magnitude);
            float grazingRadius = Mathf.Clamp(
                settings.sunInitialRadiusMeters +
                    settings.solarSurfaceClearanceMeters,
                100f,
                interceptRadius - 100f
            );
            // At the lower bound the orbit is almost parabolic. Any faster
            // solution is hyperbolic and remains inbound all the way from
            // the configured starting distance.
            float minimumRadialSpeed = Mathf.Sqrt(
                Mathf.Max(
                    1f,
                    2f * gravitationalParameter *
                        (interceptRadius - grazingRadius) /
                        (interceptRadius * interceptRadius)
                )
            ) * 1.0001f;

            float lowSpeed = minimumRadialSpeed;
            float highSpeed = Mathf.Max(
                settings.maximumInterceptRadialSpeedMetersPerSecond,
                lowSpeed + 10f
            );
            float desiredDistance = settings.startDistanceFromSunMeters;

            OrbitalState lowState = BackwardStartState(
                targetAtIntercept.position,
                radialDirection,
                tangentDirection,
                lowSpeed,
                grazingRadius,
                duration,
                gravitationalParameter,
                settings.simulationStepSeconds
            );
            OrbitalState highState = BackwardStartState(
                targetAtIntercept.position,
                radialDirection,
                tangentDirection,
                highSpeed,
                grazingRadius,
                duration,
                gravitationalParameter,
                settings.simulationStepSeconds
            );

            int expansionCount = 0;
            while (highState.position.magnitude < desiredDistance &&
                expansionCount < 12)
            {
                highSpeed *= 1.5f;
                highState = BackwardStartState(
                    targetAtIntercept.position,
                    radialDirection,
                    tangentDirection,
                    highSpeed,
                    grazingRadius,
                    duration,
                    gravitationalParameter,
                    settings.simulationStepSeconds
                );
                expansionCount++;
            }

            if (desiredDistance <= lowState.position.magnitude)
            {
                chosenRadialSpeed = lowSpeed;
                chosenTangentialSpeed = CalculateGrazingTangentialSpeed(
                    interceptRadius,
                    lowSpeed,
                    grazingRadius,
                    gravitationalParameter
                );
                return lowState;
            }

            OrbitalState selectedState = highState;
            chosenRadialSpeed = highSpeed;
            for (int iteration = 0;
                iteration < settings.distanceSolverIterations;
                iteration++)
            {
                float middleSpeed = (lowSpeed + highSpeed) * 0.5f;
                OrbitalState middleState = BackwardStartState(
                    targetAtIntercept.position,
                    radialDirection,
                    tangentDirection,
                    middleSpeed,
                    grazingRadius,
                    duration,
                    gravitationalParameter,
                    settings.simulationStepSeconds
                );

                selectedState = middleState;
                chosenRadialSpeed = middleSpeed;
                if (middleState.position.magnitude < desiredDistance)
                {
                    lowSpeed = middleSpeed;
                }
                else
                {
                    highSpeed = middleSpeed;
                }
            }
            chosenTangentialSpeed = CalculateGrazingTangentialSpeed(
                interceptRadius,
                chosenRadialSpeed,
                grazingRadius,
                gravitationalParameter
            );
            return selectedState;
        }

        private static OrbitalState BackwardStartState(
            Vector3 interceptPosition,
            Vector3 radialDirection,
            Vector3 tangentDirection,
            float radialSpeed,
            float grazingRadius,
            float duration,
            float gravitationalParameter,
            float simulationStep
        )
        {
            float tangentialSpeed = CalculateGrazingTangentialSpeed(
                interceptPosition.magnitude,
                radialSpeed,
                grazingRadius,
                gravitationalParameter
            );
            OrbitalState interceptState = new OrbitalState(
                interceptPosition,
                -radialDirection * radialSpeed +
                    tangentDirection * tangentialSpeed
            );
            return SimulateState(
                interceptState,
                -duration,
                gravitationalParameter,
                simulationStep
            );
        }

        private static float CalculateGrazingTangentialSpeed(
            float interceptRadius,
            float radialSpeed,
            float grazingRadius,
            float gravitationalParameter
        )
        {
            float radius = Mathf.Max(grazingRadius + 1f, interceptRadius);
            float periapsis = Mathf.Clamp(
                grazingRadius,
                100f,
                radius - 1f
            );
            float numerator = radialSpeed * radialSpeed +
                2f * gravitationalParameter *
                (1f / periapsis - 1f / radius);
            float radiusRatio = radius / periapsis;
            float denominator = radiusRatio * radiusRatio - 1f;
            return Mathf.Sqrt(
                Mathf.Max(0f, numerator / Mathf.Max(0.0001f, denominator))
            );
        }

        private static float FindTimeToPeriapsis(
            OrbitalState startState,
            float gravitationalParameter,
            float requestedStep,
            float maximumDuration
        )
        {
            float step = Mathf.Clamp(requestedStep, 0.05f, 1f);
            OrbitalState state = startState;
            float previousRadialVelocity = Vector3.Dot(
                state.velocity,
                state.position.normalized
            );
            float elapsed = 0f;
            int maximumSteps = Mathf.CeilToInt(
                Mathf.Max(step, maximumDuration) / step
            );
            for (int index = 0; index < maximumSteps; index++)
            {
                OrbitalState next = SimulateState(
                    state,
                    step,
                    gravitationalParameter,
                    step
                );
                float radialVelocity = Vector3.Dot(
                    next.velocity,
                    next.position.normalized
                );
                if (previousRadialVelocity < 0f &&
                    radialVelocity >= 0f)
                {
                    float fraction = previousRadialVelocity /
                        (previousRadialVelocity - radialVelocity);
                    return elapsed + step * Mathf.Clamp01(fraction);
                }
                state = next;
                previousRadialVelocity = radialVelocity;
                elapsed += step;
            }
            return maximumDuration;
        }

        internal static OrbitalState SimulateState(
            OrbitalState initialState,
            float duration,
            float gravitationalParameter,
            float requestedStep
        )
        {
            if (Mathf.Approximately(duration, 0f))
            {
                return initialState;
            }

            int steps = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    Mathf.Abs(duration) / Mathf.Max(0.01f, requestedStep)
                )
            );
            float step = duration / steps;
            Vector3 position = initialState.position;
            Vector3 velocity = initialState.velocity;

            for (int index = 0; index < steps; index++)
            {
                Vector3 accelerationBefore = CalculateSolarAcceleration(
                    position,
                    gravitationalParameter
                );
                Vector3 nextPosition = position + velocity * step +
                    accelerationBefore * (0.5f * step * step);
                Vector3 accelerationAfter = CalculateSolarAcceleration(
                    nextPosition,
                    gravitationalParameter
                );
                velocity +=
                    (accelerationBefore + accelerationAfter) *
                    (0.5f * step);
                position = nextPosition;
            }
            return new OrbitalState(position, velocity);
        }

        internal static Vector3 CalculateSolarAcceleration(
            Vector3 relativePosition,
            float gravitationalParameter
        )
        {
            float radiusSquared = Mathf.Max(
                relativePosition.sqrMagnitude,
                1f
            );
            float inverseRadius = 1f / Mathf.Sqrt(radiusSquared);
            return -relativePosition *
                (gravitationalParameter * inverseRadius / radiusSquared);
        }

        private static OrbitSettings LoadSettings(ReturnMod mod)
        {
            OrbitSettings settings = new OrbitSettings();
            string path = Path.Combine(
                mod.ModHelper.Manifest.ModFolderPath,
                SettingsFileName
            );
            if (!File.Exists(path))
            {
                mod.ModHelper.Console.WriteLine(
                    "[RETURN INTERLOPER] Settings file was not found; using " +
                    "built-in defaults: " + path,
                    MessageType.Warning
                );
                return settings;
            }

            try
            {
                JsonUtility.FromJsonOverwrite(
                    File.ReadAllText(path),
                    settings
                );
                settings.Validate();
                mod.ModHelper.Console.WriteLine(
                    "[RETURN INTERLOPER] Loaded trajectory settings: " + path,
                    MessageType.Info
                );
                return settings;
            }
            catch (Exception exception)
            {
                mod.ModHelper.Console.WriteLine(
                    "[RETURN INTERLOPER] Failed to read trajectory settings: " +
                    exception.Message,
                    MessageType.Error
                );
                return null;
            }
        }

        internal static OWRigidbody FindBody(string name)
        {
            foreach (OWRigidbody body in
                Resources.FindObjectsOfTypeAll<OWRigidbody>())
            {
                if (body != null &&
                    body.gameObject.scene.IsValid() &&
                    body.name == name)
                {
                    return body;
                }
            }
            return null;
        }

        private static string Format(float value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }

        internal struct OrbitalState
        {
            public Vector3 position;
            public Vector3 velocity;

            public OrbitalState(Vector3 position, Vector3 velocity)
            {
                this.position = position;
                this.velocity = velocity;
            }
        }

        [Serializable]
        private sealed class OrbitSettings
        {
            public bool enabled = true;
            public string sunBodyName = "Sun_Body";
            public string cometBodyName = "Comet_Body";
            public string targetBodyName = "WhiteholeStation_Body";

            public float startDistanceFromSunMeters = 120000f;
            public float interceptTimeMinutes = 15f;
            public float targetPredictionLeadSeconds = 0f;
            public float interceptCenterDistanceMeters = 800f;
            public float sunInitialRadiusMeters = 2000f;
            public float solarSurfaceClearanceMeters = 800f;
            public float maximumInterceptRadialSpeedMetersPerSecond = 800f;
            public float interceptOffsetTargetLocalX = 0f;
            public float interceptOffsetTargetLocalY = 0f;
            public float interceptOffsetTargetLocalZ = 0f;

            public float simulationStepSeconds = 0.25f;
            public int distanceSolverIterations = 24;
            public float minimumInterceptLeadSeconds = 30f;

            public float mapTrajectoryPreviewMinutes = 18f;
            public int mapTrajectoryPointCount = 256;
            public float mapRefreshIntervalSeconds = 0.2f;
            public float mapSimulationStepSeconds = 1f;
            public float mapMinimumSolarRadiusMeters = 1500f;
            public float terminalRadiusToleranceMeters = 25f;

            public void Validate()
            {
                startDistanceFromSunMeters = Mathf.Max(
                    30000f,
                    startDistanceFromSunMeters
                );
                interceptTimeMinutes = Mathf.Clamp(
                    interceptTimeMinutes,
                    0.5f,
                    21.9f
                );
                interceptCenterDistanceMeters = Mathf.Max(
                    0f,
                    interceptCenterDistanceMeters
                );
                sunInitialRadiusMeters = Mathf.Max(
                    100f,
                    sunInitialRadiusMeters
                );
                solarSurfaceClearanceMeters = Mathf.Max(
                    0f,
                    solarSurfaceClearanceMeters
                );
                maximumInterceptRadialSpeedMetersPerSecond = Mathf.Max(
                    50f,
                    maximumInterceptRadialSpeedMetersPerSecond
                );
                simulationStepSeconds = Mathf.Clamp(
                    simulationStepSeconds,
                    0.05f,
                    2f
                );
                distanceSolverIterations = Mathf.Clamp(
                    distanceSolverIterations,
                    4,
                    48
                );
                minimumInterceptLeadSeconds = Mathf.Max(
                    1f,
                    minimumInterceptLeadSeconds
                );
                mapTrajectoryPreviewMinutes = Mathf.Clamp(
                    mapTrajectoryPreviewMinutes,
                    1f,
                    30f
                );
                mapTrajectoryPointCount = Mathf.Clamp(
                    mapTrajectoryPointCount,
                    32,
                    512
                );
                mapRefreshIntervalSeconds = Mathf.Clamp(
                    mapRefreshIntervalSeconds,
                    0.05f,
                    2f
                );
                mapSimulationStepSeconds = Mathf.Clamp(
                    mapSimulationStepSeconds,
                    0.1f,
                    5f
                );
                mapMinimumSolarRadiusMeters = Mathf.Max(
                    100f,
                    mapMinimumSolarRadiusMeters
                );
                terminalRadiusToleranceMeters = Mathf.Clamp(
                    terminalRadiusToleranceMeters,
                    1f,
                    200f
                );
            }
        }
    }

    internal sealed class InterloperTerminalController : MonoBehaviour
    {
        private OWRigidbody _comet;
        private OWRigidbody _sun;
        private ReturnMod _mod;
        private float _terminalRadius;
        private float _radiusTolerance;
        private float _previousRadialVelocity;
        private bool _triggered;

        public static void Install(
            OWRigidbody comet,
            OWRigidbody sun,
            float terminalRadius,
            float radiusTolerance,
            ReturnMod mod
        )
        {
            InterloperTerminalController controller =
                comet.gameObject.GetComponent<
                    InterloperTerminalController>();
            if (controller == null)
            {
                controller = comet.gameObject.AddComponent<
                    InterloperTerminalController>();
            }
            controller.Initialize(
                comet,
                sun,
                terminalRadius,
                radiusTolerance,
                mod
            );
        }

        private void Initialize(
            OWRigidbody comet,
            OWRigidbody sun,
            float terminalRadius,
            float radiusTolerance,
            ReturnMod mod
        )
        {
            _comet = comet;
            _sun = sun;
            _terminalRadius = terminalRadius;
            _radiusTolerance = radiusTolerance;
            _mod = mod;
            _triggered = false;
            Vector3 relativePosition =
                _comet.GetPosition() - _sun.GetPosition();
            Vector3 relativeVelocity =
                _comet.GetVelocity() - _sun.GetVelocity();
            _previousRadialVelocity = Vector3.Dot(
                relativeVelocity,
                relativePosition.normalized
            );
        }

        private void FixedUpdate()
        {
            if (_triggered || _comet == null || _sun == null)
            {
                return;
            }

            Vector3 relativePosition =
                _comet.GetPosition() - _sun.GetPosition();
            Vector3 relativeVelocity =
                _comet.GetVelocity() - _sun.GetVelocity();
            float radius = relativePosition.magnitude;
            float radialVelocity = Vector3.Dot(
                relativeVelocity,
                relativePosition.normalized
            );

            bool crossedPeriapsis =
                _previousRadialVelocity < 0f && radialVelocity >= 0f;
            bool reachedConfiguredTerminal =
                radius <= _terminalRadius + _radiusTolerance;
            if (crossedPeriapsis && reachedConfiguredTerminal)
            {
                TriggerTerminalDeath(radius);
            }
            _previousRadialVelocity = radialVelocity;
        }

        private void TriggerTerminalDeath(float actualRadius)
        {
            _triggered = true;
            SceneSixEndingController.MarkTerminalDeath();
            SceneSixController.MarkRevivalCheckpoint();
            TimeLoop.SetTimeLoopEnabled(false);
            _mod?.ModHelper.Console.WriteLine(
                "[RETURN TERMINAL] Interloper reached periapsis at " +
                actualRadius.ToString("F2", CultureInfo.InvariantCulture) +
                " m. Triggering death outside the time loop.",
                MessageType.Success
            );

            DeathManager deathManager = Locator.GetDeathManager();
            if (deathManager != null &&
                !deathManager.IsPlayerDying() &&
                !deathManager.IsPlayerDead())
            {
                deathManager.KillPlayer(DeathType.Energy);
            }
        }
    }

    internal sealed class FrozenSunProgressionController : MonoBehaviour
    {
        private SunController _sunController;
        private bool _listening;

        public static void Install(OWRigidbody sun, ReturnMod mod)
        {
            SunController sunController =
                sun.GetComponent<SunController>();
            if (sunController == null)
            {
                mod.ModHelper.Console.WriteLine(
                    "[RETURN SUN] Could not find SunController.",
                    MessageType.Error
                );
                return;
            }

            FrozenSunProgressionController freezer =
                sun.gameObject.GetComponent<
                    FrozenSunProgressionController>();
            if (freezer == null)
            {
                freezer = sun.gameObject.AddComponent<
                    FrozenSunProgressionController>();
            }
            freezer.Initialize(sunController);
            mod.ModHelper.Console.WriteLine(
                "[RETURN SUN] Solar aging and expansion frozen at the " +
                "initial state; collapse and supernova remain enabled.",
                MessageType.Success
            );
        }

        private void Initialize(SunController sunController)
        {
            if (_listening)
            {
                GlobalMessenger.RemoveListener(
                    "TriggerSupernova",
                    OnTriggerSupernova
                );
            }
            _sunController = sunController;
            _sunController.enabled = false;
            GlobalMessenger.AddListener(
                "TriggerSupernova",
                OnTriggerSupernova
            );
            _listening = true;
        }

        private void OnTriggerSupernova()
        {
            if (_sunController != null)
            {
                _sunController.enabled = true;
            }
            if (_listening)
            {
                GlobalMessenger.RemoveListener(
                    "TriggerSupernova",
                    OnTriggerSupernova
                );
                _listening = false;
            }
        }

        private void OnDestroy()
        {
            if (_listening)
            {
                GlobalMessenger.RemoveListener(
                    "TriggerSupernova",
                    OnTriggerSupernova
                );
            }
        }
    }

    internal sealed class InterloperMapTrajectoryLine : MonoBehaviour
    {
        private OWRigidbody _comet;
        private OWRigidbody _sun;
        private LineRenderer _lineRenderer;
        private float _gravitationalParameter;
        private float _previewSeconds;
        private int _pointCount;
        private float _refreshInterval;
        private float _simulationStep;
        private float _minimumSolarRadius;
        private float _nextRefreshTime;
        private bool _inMapView;
        private bool _initialized;

        public static void Install(
            OWRigidbody comet,
            OWRigidbody sun,
            float gravitationalParameter,
            float previewSeconds,
            int pointCount,
            float refreshInterval,
            float simulationStep,
            float minimumSolarRadius,
            ReturnMod mod
        )
        {
            EllipticOrbitLine original = null;
            foreach (EllipticOrbitLine candidate in
                Resources.FindObjectsOfTypeAll<EllipticOrbitLine>())
            {
                if (candidate != null &&
                    candidate.gameObject.scene.IsValid() &&
                    candidate.gameObject.name == "OrbitLine_CO")
                {
                    original = candidate;
                    break;
                }
            }

            if (original == null)
            {
                mod.ModHelper.Console.WriteLine(
                    "[RETURN INTERLOPER MAP] Could not find OrbitLine_CO.",
                    MessageType.Error
                );
                return;
            }

            GameObject lineObject = original.gameObject;
            original.enabled = false;
            Destroy(original);

            InterloperMapTrajectoryLine customLine =
                lineObject.GetComponent<InterloperMapTrajectoryLine>();
            if (customLine == null)
            {
                customLine =
                    lineObject.AddComponent<InterloperMapTrajectoryLine>();
            }
            customLine.Initialize(
                comet,
                sun,
                gravitationalParameter,
                previewSeconds,
                pointCount,
                refreshInterval,
                simulationStep,
                minimumSolarRadius
            );

            mod.ModHelper.Console.WriteLine(
                "[RETURN INTERLOPER MAP] Replaced the cached vanilla ellipse " +
                "with a live predicted trajectory.",
                MessageType.Success
            );
        }

        private void Initialize(
            OWRigidbody comet,
            OWRigidbody sun,
            float gravitationalParameter,
            float previewSeconds,
            int pointCount,
            float refreshInterval,
            float simulationStep,
            float minimumSolarRadius
        )
        {
            if (_initialized)
            {
                GlobalMessenger.RemoveListener(
                    "EnterMapView",
                    OnEnterMapView
                );
                GlobalMessenger.RemoveListener(
                    "ExitMapView",
                    OnExitMapView
                );
            }

            _comet = comet;
            _sun = sun;
            _gravitationalParameter = gravitationalParameter;
            _previewSeconds = previewSeconds;
            _pointCount = pointCount;
            _refreshInterval = refreshInterval;
            _simulationStep = simulationStep;
            _minimumSolarRadius = minimumSolarRadius;
            _lineRenderer = GetComponent<LineRenderer>();
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.loop = false;
            _lineRenderer.positionCount = _pointCount;
            _lineRenderer.enabled = false;

            GlobalMessenger.AddListener("EnterMapView", OnEnterMapView);
            GlobalMessenger.AddListener("ExitMapView", OnExitMapView);
            _initialized = true;
        }

        private void OnDestroy()
        {
            if (!_initialized)
            {
                return;
            }
            GlobalMessenger.RemoveListener("EnterMapView", OnEnterMapView);
            GlobalMessenger.RemoveListener("ExitMapView", OnExitMapView);
        }

        private void OnEnterMapView()
        {
            _inMapView = true;
            _lineRenderer.enabled = true;
            _nextRefreshTime = 0f;
            RebuildTrajectory();
        }

        private void OnExitMapView()
        {
            _inMapView = false;
            if (_lineRenderer != null)
            {
                _lineRenderer.enabled = false;
            }
        }

        private void Update()
        {
            if (!_inMapView ||
                _lineRenderer == null ||
                _comet == null ||
                _sun == null)
            {
                return;
            }

            if (Time.unscaledTime >= _nextRefreshTime)
            {
                RebuildTrajectory();
                _nextRefreshTime =
                    Time.unscaledTime + _refreshInterval;
            }

            OWCamera activeCamera = Locator.GetActiveCamera();
            if (activeCamera != null)
            {
                float distance = Vector3.Distance(
                    activeCamera.transform.position,
                    _sun.GetPosition()
                );
                _lineRenderer.widthMultiplier = Mathf.Min(
                    distance * 0.005f,
                    100f
                );
            }
        }

        private void RebuildTrajectory()
        {
            InterloperTrajectoryController.OrbitalState state =
                new InterloperTrajectoryController.OrbitalState(
                    _comet.GetPosition() - _sun.GetPosition(),
                    _comet.GetVelocity() - _sun.GetVelocity()
                );
            Vector3[] points = new Vector3[_pointCount];
            float segmentDuration =
                _previewSeconds / Mathf.Max(1, _pointCount - 1);
            for (int index = 0; index < _pointCount; index++)
            {
                points[index] = _sun.GetPosition() + state.position;
                if (index + 1 < _pointCount)
                {
                    if (state.position.magnitude <= _minimumSolarRadius)
                    {
                        for (int remainder = index + 1;
                            remainder < _pointCount;
                            remainder++)
                        {
                            points[remainder] = points[index];
                        }
                        break;
                    }
                    state = InterloperTrajectoryController.SimulateState(
                        state,
                        segmentDuration,
                        _gravitationalParameter,
                        _simulationStep
                    );
                }
            }
            _lineRenderer.positionCount = _pointCount;
            _lineRenderer.SetPositions(points);
        }
    }
}
