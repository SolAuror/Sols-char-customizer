/*using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Weapons
{
    //MWC: Magic Projectile - ported from the "Magic Projectile" package (originally 'magicParticleTriggers').
    //Renamed to follow the project convention (M-prefix, PascalCase). Each particle of the attached ParticleSystem
    //casts a Sphere/Capsule to deal damage along its path through the owning MProjectile (hasImpacted = false,
    //so the projectile keeps travelling).
    [RequireComponent(typeof(ParticleSystem))]
    [AddComponentMenu("Malbers/Damage/Magic Particle Triggers")]
    public class MMagicParticleTriggers : MonoBehaviour
    {
        private enum DamagingTriggerShape { Sphere, Capsule };
        private enum DamagingTriggerCapsuleDirection { X, Y, Z };
        [Tooltip("Shape of trigger collider that each particle will cast damage with.")]
        [SerializeField] private DamagingTriggerShape _damagingTriggerShape;
        [Tooltip("Direction of Capsule trigger.")]
        [SerializeField] private DamagingTriggerCapsuleDirection _damagingTriggerCapsuleDirection;
        [Tooltip("Size, in meters, of sphere on each particle that does damage.")]
        [SerializeField] private float particleDamagingSphereSize = 1.0f;
        [Tooltip("Multiplier of capsule size on each particle that does damage.")]
        [SerializeField] private float particleDamagingCapsuleLenght = 1.0f;
        [Tooltip("Use particle size as DamagingSphereSize. \nParticleDamagingSphereSize will be ignored if this is set to true.")]
        [SerializeField] private bool useParticleSizeForDamagingSphere = false;
        [Tooltip("If using particle size as DamagingSphereSize multiply particleSize by particleSizeMultiplier. \nParticleDamagingSphereSize will be ignored if this is set to true.")]
        [SerializeField] private float particleSizeMultiplier = 10;

        [Tooltip("Do not make damage once ParticleSystem start to Stop. \nParticleSystem is stopped when no particles are active (particle count == 0)")]
        [SerializeField] private bool stopDamageWhenStoppingParticleSystem = true;


        private ParticleSystem _particleSystem; // Particle System
        private ParticleSystem.MainModule _mainParticleSystem; // Main module of ParticleSystem
        private ParticleSystem.Particle[] _particles; // Particles in Main ParticleSystem

        private MProjectile _projectile; // Projectile

        private bool _projectileFired = false; // Used to trigger SphereCast so make damage
        private bool _particleSystemStoping = false; // Used to stop damage when stoping Particle System
        private int __numberOfParticles = 0; // Store number of particles in ParticleSystem

        [SerializeField] private FloatReference timeBetweenHits = new(1);
        [Tooltip("If each particle maked damage set to 1. If damage is on every 2 particles set to 2 and so on. \nReduce damaging particles to save performance.")]
        [SerializeField] private IntReference whatParticleMakesDamage = new(1);

        [Space(10)]
        [SerializeField] private TransformEvent OnHit; // Use to Play hit particle effect when hit on collider layer
        [SerializeField] private TransformEvent OnHitTag; // Use to play hit particle effect when hit collider tag
        [SerializeField] private Vector3Event OnHitPositionWithoutImpact; // Use to play effect when particle hits
        [SerializeField] private TransformEvent OnHitTransformWithoutImpact; // Use to play effect when particle hits
        [SerializeField] private BoolEvent OnReachedMaxDistance; // Use to play effect when particle hits


        private float __lastHitTime = 0;

        Vector3 __particle_point1_position;
        Vector3 __particle_point2_position;
        Vector3 __capsule_particle_direction;

        private bool debug = false;

        private ParticleSystem[] childParticleSystems = new ParticleSystem[0];


        // Start is called before the first frame update
        void Start()
        {
            if (this.gameObject.GetComponent<ParticleSystem>() != null)
            {
                _particleSystem = this.gameObject.GetComponent<ParticleSystem>(); // Set ParticleSystem
                _mainParticleSystem = _particleSystem.main; // Set Main module of ParticleSystem
                _mainParticleSystem.stopAction = ParticleSystemStopAction.Callback; // Set callback for Main ParticleSystem

                if (_particles == null || _particles.Length < _mainParticleSystem.maxParticles)
                {
                    _particles = new ParticleSystem.Particle[_mainParticleSystem.maxParticles]; // Define array for particles
                }
            }

            if (this.gameObject.GetComponentInParent<MProjectile>() != null)
            {
                _projectile = this.gameObject.GetComponentInParent<MProjectile>(); // Set Projectile
                debug = _projectile.debug;
                _projectile.OnFire.AddListener(ProjectileFired); // Add listener for OnFire for Projectile
                _projectile.OnHit.AddListener(ProjectileHitEffect); // Add listener for OnHit which is collider layer based
                _projectile.OnHitTag.AddListener(ProjectileHitTagEffect); // Add listener for OnHitTag which is collider Malber Tag based
                _projectile.OnHitPositionWithoutImpact.AddListener(ProjectileHitPositionWithoutImpact); // Add listener for OnHitTag which is collider Malber Tag based
                _projectile.OnHitTransformWithoutImpact.AddListener(ProjectileHitTransformWithoutImpact); // Add listener for OnHitTag which is collider Malber Tag based
                _projectile.OnReachedMaxDistance.AddListener(ProjectileReachedMaxDistance); // Add listener for OnHitTag which is collider Malber Tag based
            }

            if (whatParticleMakesDamage <= 0)
            {
                whatParticleMakesDamage.Value = 1;
            }

            if (this.gameObject.GetComponentsInChildren<ParticleSystem>().Length > 0)
            {
                childParticleSystems = this.gameObject.GetComponentsInChildren<ParticleSystem>();
            }

        }

        private void ProjectileFired()
        {
            _projectileFired = true; // Used for SphereCast damage
        }

        private void OnParticleSystemStopped()
        {
            _projectileFired = false; // Stop SphereCast

        }
        private void OnDisable() // When returning Projectile to pool disable SphereCast if not stoped already
        {
            _projectileFired = false; // Stop SphereCast
            _particleSystemStoping = false;
        }

        private void OnDestroy()
        {
            //MWC: hardened - guard on _projectile (the original guarded on _particles, which NREs when there is no MProjectile parent)
            if (_projectile != null)
            {
                _projectile.OnFire.RemoveListener(ProjectileFired); // Remove listener for OnFire for Projectile
                _projectile.OnHit.RemoveListener(ProjectileHitEffect); // Remove listener for OnHit which is collider layer based
                _projectile.OnHitTag.RemoveListener(ProjectileHitTagEffect); // Remove listener for OnHitTag which is collider Malber Tag based
                _projectile.OnHitPositionWithoutImpact.RemoveListener(ProjectileHitPositionWithoutImpact); // Remove listener for OnHitTag which is collider Malber Tag based
                _projectile.OnHitTransformWithoutImpact.RemoveListener(ProjectileHitTransformWithoutImpact); // Add listener for OnHitTag which is collider Malber Tag based
                _projectile.OnReachedMaxDistance.RemoveListener(ProjectileReachedMaxDistance); // Add listener for OnHitTag which is collider Malber Tag based
            }
        }

        private void ProjectileHitEffect(Transform transformHit)
        {
            if (debug) Debug.Log("Projectile hit for effect Transform " + transformHit.name, transformHit.gameObject);
            OnHit.Invoke(transformHit);
        }

        private void ProjectileHitTagEffect(Transform transformHitTag)
        {
            if (debug) Debug.Log("Projectile hit for effect Transform with Tag " + transformHitTag.name, transformHitTag.gameObject);
            OnHitTag.Invoke(transformHitTag);
        }

        private void ProjectileHitPositionWithoutImpact(Vector3 hitPosition)
        {
            if (debug) Debug.Log("Projectile hit Position " + hitPosition);
            OnHitPositionWithoutImpact.Invoke(hitPosition);
        }

        private void ProjectileHitTransformWithoutImpact(Transform hitTransform)
        {
            if (debug) Debug.Log("Projectile hit Transform " + hitTransform.name, hitTransform.gameObject);
            OnHitTransformWithoutImpact.Invoke(hitTransform);
        }

        private void ProjectileReachedMaxDistance(bool maxDistanceReached)
        {
            if (debug) Debug.Log("Projectile reached MaxDistance " + maxDistanceReached + ". If False then deflection or slope angle stoped Projectile.");

            OnReachedMaxDistance.Invoke(maxDistanceReached);

            if (debug) Debug.Log("Start stoping ParticleSystem!", this.gameObject);

            _particleSystem.Stop();

            if (childParticleSystems.Length > 0)
            {
                foreach (ParticleSystem childParticleSystem in childParticleSystems)
                {
                    childParticleSystem.Stop();
                }
            }

            if (stopDamageWhenStoppingParticleSystem == true)
            {
                _particleSystemStoping = true;
            }

        }

        private void Update()
        {
            if (_projectileFired == true && _particleSystem.isPlaying == true && _particleSystemStoping == false) // Check if Projectile is fired and ParticleSystem plating
            {
                __numberOfParticles = _particleSystem.GetParticles(_particles); // Get number of particles

                if (__numberOfParticles > 1) // Check if there is more than one particle
                {
                    for (int i = 0; i < __numberOfParticles - whatParticleMakesDamage; i += whatParticleMakesDamage) // Loop through all particles
                    {
                        if (useParticleSizeForDamagingSphere == true) // Check if we need to use particle size
                        {
                            // Set particle Damaging Sphere Size to aproximation of particle size
                            particleDamagingSphereSize = _particles[i].GetCurrentSize3D(_particleSystem).sqrMagnitude * particleSizeMultiplier;
                            // Particle size is in most cases very small float so we multiply that with particleSizeMultiplier to have something functional
                        }

                        __particle_point1_position = _particles[i].position + new Vector3(0, particleDamagingSphereSize, 0);
                        __particle_point2_position = _particles[i + whatParticleMakesDamage].position + new Vector3(0, particleDamagingSphereSize, 0);

                        Vector3 particlesOffSet = __particle_point2_position - __particle_point1_position; // Offset of particle and next one

                        Vector3 particleDirection = particlesOffSet.normalized; // Direction from current particle to next one

                        float particleDistance = particlesOffSet.sqrMagnitude; // Distance from current particle to next one

                        if (_damagingTriggerShape == DamagingTriggerShape.Sphere)
                        {
                            if (_projectile.debug)
                            {
                                MDebug.DrawWireSphere(__particle_point1_position, Color.yellow, particleDamagingSphereSize);
                                MDebug.DrawLine(__particle_point1_position, __particle_point2_position, Color.cyan);
                            }

                            // SphereCast from current particle position in set DamageSphereSize in direction to next particle in distance to next particle, layers and triggers
                            if (Physics.SphereCast(__particle_point1_position, particleDamagingSphereSize, particleDirection, out RaycastHit hit, particleDistance,
                                                _projectile.Layer, _projectile.TriggerInteraction))
                            {
                                if (!_projectile.IsInvalid(hit.collider)) // Check hit collider
                                {
                                    if (__lastHitTime + timeBetweenHits < Time.time)
                                    {
                                        if (debug) Debug.Log("Particle SphereCast " + i + " making damage at position " + __particle_point1_position, this.gameObject);
                                        // Make damage from projectile, which is set by weapon that fired projectile
                                        _projectile.ProjectileImpact(hit.rigidbody, hit.collider, hit.point, hit.normal, false);

                                        // Set lastHitTime
                                        __lastHitTime = Time.time;

                                    }
                                }
                            }
                        }

                        if (_damagingTriggerShape == DamagingTriggerShape.Capsule)
                        {
                            __capsule_particle_direction = this.gameObject.transform.forward;

                            switch (_damagingTriggerCapsuleDirection)
                            {
                                case DamagingTriggerCapsuleDirection.X:
                                    __capsule_particle_direction = this.gameObject.transform.right;
                                    break;

                                case DamagingTriggerCapsuleDirection.Y:
                                    __capsule_particle_direction = this.gameObject.transform.up;
                                    break;

                                case DamagingTriggerCapsuleDirection.Z:
                                    __capsule_particle_direction = this.gameObject.transform.forward;
                                    break;

                                default:
                                    __capsule_particle_direction = this.gameObject.transform.forward;
                                    break;
                            }

                            // Change __particle_point2_position to offset position from __particle_point1_position since that is needed for capsule
                            Vector3 __particle_point2_position_capsule = __particle_point1_position + (__capsule_particle_direction * particleDamagingSphereSize * particleDamagingCapsuleLenght);

                            if (_projectile.debug)
                            {
                                // Draw 2 spheres as aproximation for capsule
                                MDebug.DrawWireSphere(__particle_point1_position, Color.yellow, particleDamagingSphereSize);
                                MDebug.DrawWireSphere(__particle_point2_position_capsule, Color.yellow, particleDamagingSphereSize);
                                MDebug.DrawLine(__particle_point1_position, __particle_point2_position, Color.cyan);
                            }


                            // SphereCast from current particle position in set DamageSphereSize in direction to next particle in distance to next particle, layers and triggers
                            if (Physics.CapsuleCast(__particle_point1_position, __particle_point2_position_capsule, particleDamagingSphereSize, particleDirection, out RaycastHit hit, particleDistance,
                                                _projectile.Layer, _projectile.TriggerInteraction))
                            {
                                if (!_projectile.IsInvalid(hit.collider)) // Check hit collider
                                {
                                    if (__lastHitTime + timeBetweenHits < Time.time)
                                    {
                                        if (debug) Debug.Log("Particle CapsuleCast " + i + " making damage at position " + __particle_point1_position, this.gameObject);
                                        // Make damage from projectile, which is set by weapon that fired projectile
                                        _projectile.ProjectileImpact(hit.rigidbody, hit.collider, hit.point, hit.normal, false);

                                        // Set lastHitTime
                                        __lastHitTime = Time.time;

                                    }
                                }
                            }
                        }
                    }

                    // Add Physics Cast to last particle
                    if (_damagingTriggerShape == DamagingTriggerShape.Sphere)
                    {
                        if (_projectile.debug)
                        {
                            MDebug.DrawWireSphere(_particles[__numberOfParticles - 1].position, Color.yellow, particleDamagingSphereSize);
                        }

                        Vector3 particlesOffSet = _particles[__numberOfParticles - 1].position - _particles[__numberOfParticles - 2].position; // Offset of particle and next one

                        Vector3 particleDirection = particlesOffSet.normalized; // Direction from current particle to next one

                        float particleDistance = particlesOffSet.sqrMagnitude; // Distance from current particle to next one

                        if (Physics.SphereCast(_particles[__numberOfParticles - 1].position + new Vector3(0, particleDamagingSphereSize, 0), particleDamagingSphereSize, particleDirection, out RaycastHit hit, particleDistance,
                                                _projectile.Layer, _projectile.TriggerInteraction))
                        {
                            if (!_projectile.IsInvalid(hit.collider)) // Check hit collider
                            {
                                if (__lastHitTime + timeBetweenHits < Time.time)
                                {
                                    if (debug) Debug.Log("Last Particle SphereCast " + (__numberOfParticles - 1) + " making damage at position " + _particles[__numberOfParticles - 1].position + new Vector3(0, particleDamagingSphereSize, 0), this.gameObject);
                                    // Make damage from projectile, which is set by weapon that fired projectile
                                    _projectile.ProjectileImpact(hit.rigidbody, hit.collider, hit.point, hit.normal, false);

                                    // Set lastHitTime
                                    __lastHitTime = Time.time;

                                }
                            }
                        }

                    }

                    // Add Physics Cast to last particle
                    if (_damagingTriggerShape == DamagingTriggerShape.Capsule)
                    {
                        Vector3 __particle_point2_position_capsule = _particles[__numberOfParticles - 1].position + (__capsule_particle_direction * particleDamagingSphereSize * particleDamagingCapsuleLenght);

                        if (_projectile.debug)
                        {
                            // Draw 2 spheres as aproximation for capsule
                            MDebug.DrawWireSphere(_particles[__numberOfParticles - 1].position + new Vector3(0, particleDamagingSphereSize, 0), Color.yellow, particleDamagingSphereSize);
                            MDebug.DrawWireSphere(__particle_point2_position_capsule, Color.yellow, particleDamagingSphereSize);
                        }

                        Vector3 particlesOffSet = _particles[__numberOfParticles - 1].position - _particles[__numberOfParticles - 2].position; // Offset of particle and next one

                        Vector3 particleDirection = particlesOffSet.normalized; // Direction from current particle to next one

                        float particleDistance = particlesOffSet.sqrMagnitude; // Distance from current particle to next one

                        if (Physics.CapsuleCast(_particles[__numberOfParticles - 1].position + new Vector3(0, particleDamagingSphereSize, 0), __particle_point2_position_capsule, particleDamagingSphereSize, particleDirection, out RaycastHit hit, particleDistance,
                                                _projectile.Layer, _projectile.TriggerInteraction))
                        {
                            if (!_projectile.IsInvalid(hit.collider)) // Check hit collider
                            {
                                if (__lastHitTime + timeBetweenHits < Time.time)
                                {
                                    if (debug) Debug.Log("Last Particle CapsuleCast " + (__numberOfParticles - 1) + " making damage at position " + _particles[__numberOfParticles - 1].position + new Vector3(0, particleDamagingSphereSize, 0), this.gameObject);
                                    // Make damage from projectile, which is set by weapon that fired projectile
                                    _projectile.ProjectileImpact(hit.rigidbody, hit.collider, hit.point, hit.normal, false);

                                    // Set lastHitTime
                                    __lastHitTime = Time.time;

                                }
                            }
                        }


                    }
                }
            }
        }
    }
}
*/