using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MegaManX
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class Zero : MonoBehaviour, IHealth
    {
        #region Life
        [SerializeField] int _hp; 
        #endregion

        #region Locomotion
        [SerializeField] float _moveSpeed = 6;
        [SerializeField] float _accelerationTime = .1f;
        float velocityHSmoothing;
        float _hVelocity;
        float _vVelocity;
        float _maxYVelocity;
        float _facing;
        float _inputH;

        float _gravity;
        [SerializeField] float _gravityStrength = 1;

        [SerializeField] float _jumpVelocity;
        [SerializeField] float _jumpHeight = 4;
        [SerializeField] float _timeToJumpApex = .4f;
        bool _jump;
        int JUMP_STREAK = 2;
        int _jumpCount;

        const float DASH_DETECT_RAY = 0.375f;
        bool _lastDash;
        [SerializeField] float _dashSpeed = 16;
        [SerializeField] float _dashTime = 0.375f;
        float _dashTimer;
        enum DashStage
        {
            Begin, Dash, End
        }
        DashStage _dashStage = DashStage.End;
        bool _wantDash, _validDashBegin;
        #endregion

        #region Collision Detection
        const float SKIN_WIDTH = 0.0625f;
        [SerializeField] int horizontalRayCount = 4;
        [SerializeField] int verticalRayCount = 4;
        float _horizontalRaySpacing;
        float _verticalRaySpacing;
        [SerializeField] LayerMask _collisionMask = 0;
        LayerMask _collisionMaskBackup;
        struct RaycastOrigins
        {
            public Vector2 topLeft, topRight;
            public Vector2 bottomLeft, bottomRight;
        }
        RaycastOrigins _raycastOrigins;
        RaycastHit2D[] _rayHits = new RaycastHit2D[16];

        public struct CollisionInfo
        {
            public bool above, below;
            public bool left, right;

            public void Reset()
            {
                above = below = false;
                left = right = false;
            }
        }
        CollisionInfo _collisions;
        bool _lastWallSlide;
        bool wallSlide { get { return !_collisions.below && (_collisions.left || _collisions.right); } }
        #endregion

        #region Damage Presentation
        bool _inDamage, _inGhost;
        const float DAMAGE_TIME = 0.125f;
        const float GHOST_TIME = 2;
        float _damageTimer;
        #endregion

        #region Component
        Rigidbody2D _rigidbody;
        CapsuleCollider2D _collider;
        SpriteRenderer _bodySprite;

        [SerializeField] Transform _rayOrigin;
        #endregion

        #region Data

        #endregion

        void Awake()
        {
            _rigidbody = GetComponent<Rigidbody2D>();
            _collider = GetComponent<CapsuleCollider2D>();
            _bodySprite = GetComponent<SpriteRenderer>();
        }

        void Start()
        {
            _collisionMaskBackup = _collisionMask;
            _gravity = -(2 * _jumpHeight) / Mathf.Pow(_timeToJumpApex, 2);
            _jumpVelocity = Mathf.Abs(_gravity) * _timeToJumpApex;
            _maxYVelocity = _jumpVelocity;
            print("Gravity: " + _gravity + "  Jump Velocity: " + _jumpVelocity);

            CalculateRaySpacing();

            _jumpCount = 0;

            _inDamage = _inGhost = false;
            _damageTimer = 0;
        }

        void Update()
        {
            CalculateRaySpacing();
            UpdateRaycastOrigins();
            if (_facing < 0)
                _bodySprite.flipX = true;
            else if (_facing > 0)
                _bodySprite.flipX = false;
        }

        void FixedUpdate()
        {
            CalculateRaySpacing();
            UpdateRaycastOrigins();

            float wallSlideVelocity = _gravity * 1.25f * Time.fixedDeltaTime;

            #region Jump/Wall Slide
            if (_collisions.above || _collisions.below)
                _vVelocity = 0;
            if (_inDamage)
                _jump = false;
            bool jumpCache = _jump;
            if (_jump)
            {
                _jump = false;
                _vVelocity = _jumpVelocity;
                // 在牆上滑的時候跳，給予反向的水平速度。
                if (wallSlide)
                {
                    _vVelocity *= 0.75f;
                    if (_wantDash)
                    {
                        if (_collisions.left)
                            _hVelocity = _dashSpeed;
                        else if (_collisions.right)
                            _hVelocity = -_dashSpeed;
                    }
                    else
                    {
                        if (_collisions.left)
                            _hVelocity = _jumpVelocity * 0.75f;
                        else if (_collisions.right)
                            _hVelocity = -_jumpVelocity * 0.75f;
                    }
                }
            }
            if (!_inDamage)
            {
                if (wallSlide)
                {
                    if (jumpCache)
                    {
                        // 如果有按住衝刺
                        if (_wantDash)
                        {
                            if (_collisions.left)
                                _hVelocity = _dashSpeed * 2;
                            else if (_collisions.right)
                                _hVelocity = -_dashSpeed * 2;
                        }
                    }
                    // 正在牆上滑，沒有按跳，慢速落下。
                    else
                        _vVelocity = wallSlideVelocity;
                }
                else
                {
                    // 從有靠牆到沒靠牆，給一個初始的速度讓他往前移一點。
                    if (_lastWallSlide && _inputH == 0)
                        _hVelocity = _facing * _dashSpeed;
                    // 沒有在牆上滑，正常速度落下。
                    _vVelocity += _gravity * _gravityStrength * Time.fixedDeltaTime;
                    _vVelocity = Mathf.Clamp(_vVelocity, -_maxYVelocity, _maxYVelocity);
                }
            }
            _lastWallSlide = wallSlide;
            #endregion

            Vector2 deltaPosition = new Vector2(_hVelocity, _vVelocity) * Time.fixedDeltaTime;

            _collisions.Reset();
            if (deltaPosition.x != 0)
                DetectHorizontalCollisions(ref deltaPosition);
            if (deltaPosition.y != 0)
                DetectVerticalCollisions(ref deltaPosition);
            _rigidbody.MovePosition(_rigidbody.position + deltaPosition);

            if (_collisions.below)
                _jumpCount = 0;
        }

        void UpdateRaycastOrigins()
        {
            Bounds bounds = _collider.bounds;
            bounds.Expand(SKIN_WIDTH * -2);

            _raycastOrigins.bottomLeft.Set(bounds.min.x, bounds.min.y);
            _raycastOrigins.bottomRight.Set(bounds.max.x, bounds.min.y);
            _raycastOrigins.topLeft.Set(bounds.min.x, bounds.max.y);
            _raycastOrigins.topRight.Set(bounds.max.x, bounds.max.y);
        }

        void CalculateRaySpacing()
        {
            Bounds bounds = _collider.bounds;
            bounds.Expand(SKIN_WIDTH * -2);

            horizontalRayCount = Mathf.Clamp(horizontalRayCount, 2, int.MaxValue);
            verticalRayCount = Mathf.Clamp(verticalRayCount, 2, int.MaxValue);

            _horizontalRaySpacing = bounds.size.y / (horizontalRayCount - 1);
            _verticalRaySpacing = bounds.size.x / (verticalRayCount - 1);
        }

        void DetectHorizontalCollisions(ref Vector2 v)
        {
            float directionX = Mathf.Sign(v.x);
            float rayLength = Mathf.Abs(v.x) + SKIN_WIDTH;
            for (int i = 0; i < horizontalRayCount; ++i)
            {
                Vector2 rayOrigin = (directionX == -1) ? _raycastOrigins.bottomLeft : _raycastOrigins.bottomRight;
                rayOrigin += Vector2.up * (_horizontalRaySpacing * i);
                RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.right * directionX, rayLength, _collisionMask);

                Debug.DrawRay(rayOrigin, Vector2.right * directionX * rayLength, Color.black);

                if (hit)
                {
                    v.x = (hit.distance - SKIN_WIDTH) * directionX;
                    rayLength = hit.distance;

                    _collisions.left = directionX == -1;
                    _collisions.right = directionX == 1;
                }
            }
        }

        void DetectVerticalCollisions(ref Vector2 v)
        {
            float directionY = Mathf.Sign(v.y);
            float rayLength = Mathf.Abs(v.y) + SKIN_WIDTH;
            for (int i = 0; i < verticalRayCount; ++i)
            {
                Vector2 rayOrigin = (directionY == -1) ? _raycastOrigins.bottomLeft : _raycastOrigins.topLeft;
                rayOrigin += Vector2.right * (_verticalRaySpacing * i + v.x);
                RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.up * directionY, rayLength, _collisionMask);

                Debug.DrawRay(rayOrigin, Vector2.up * directionY * rayLength, Color.black);

                if (hit)
                {
                    v.y = (hit.distance - SKIN_WIDTH) * directionY;
                    rayLength = hit.distance;

                    _collisions.below = directionY == -1;
                    _collisions.above = directionY == 1;
                }
            }
        }

        public void MoveHorizontally(float h)
        {
            if (_inDamage)
                return;

            _inputH = h;
            //_hVelocity = h * _moveSpeed;
            float targetHVelocity = h * _moveSpeed;
            // 按住衝刺時且在空中，水平速度要是衝刺的速度。
            if (_wantDash && (_jump || !_collisions.below))
                targetHVelocity = h * _dashSpeed;
            if (_dashStage != DashStage.Dash)
            {
                if (h != 0)
                {
                    _hVelocity = Mathf.SmoothDamp(_hVelocity, targetHVelocity, ref velocityHSmoothing, _accelerationTime);
                    _facing = Mathf.Sign(h);
                }
                else
                    _hVelocity = Mathf.MoveTowards(_hVelocity, 0, Time.deltaTime * _moveSpeed * 100);
            }
            else
            {
                if (h != 0)
                {
                    if (_facing != Mathf.Sign(h))
                    {
                        _hVelocity = Mathf.SmoothDamp(_hVelocity, 0, ref velocityHSmoothing, _accelerationTime);
                        _dashStage = DashStage.End;
                    }
                    _facing = Mathf.Sign(h);
                }
            }
            // 在牆上滑時面對的方向要跟現在按的方向相反
            if (wallSlide && h != 0)
                _facing = -Mathf.Sign(h);
        }

        public bool Jump()
        {
            _jump = !_inDamage && (_jump || (!_collisions.above && (wallSlide || _collisions.below || _jumpCount < JUMP_STREAK)));
            if (_jump)
                ++_jumpCount;
            return _jump;
        }

        bool CanDash()
        {
            bool blocked = _inDamage && (_facing > 0 && _collisions.right) || (_facing < 0 && _collisions.left);
            bool leftBackup = _collisions.left;
            bool rightBackup = _collisions.right;
            Vector2 detectRay = (_facing > 0) ? Vector2.right : Vector2.left;
            detectRay *= DASH_DETECT_RAY;
            DetectHorizontalCollisions(ref detectRay);
            bool readyToBeBlocked = (_facing < 0 && _collisions.left) || (_facing > 0 && _collisions.right);
            _collisions.left = leftBackup;
            _collisions.right = rightBackup;
            bool able = _collisions.below && !blocked && !readyToBeBlocked;
            return able;
        }

        public bool StartDash()
        {
            _wantDash = true;
            bool able = CanDash() && _dashStage == DashStage.End;
            if (able)
            {
                _dashStage = DashStage.Begin;
                _dashTimer = 0;
                _validDashBegin = true;
                Debug.Log("DashStage.Begin");
            }
            return able;
        }

        public bool Dash()
        {
            _wantDash = true;
            if (_inDamage || _dashStage == DashStage.End || !_validDashBegin || !CanDash())
            {
                Debug.LogFormat("Can not dash! {0}", _hVelocity);
                if (_inputH == 0)
                    _hVelocity = Mathf.SmoothDamp(_hVelocity, 0, ref velocityHSmoothing, _accelerationTime);
                _validDashBegin = false;
                _dashStage = DashStage.End;
                return false;
            }
            bool able = _dashTimer < _dashTime;
            _dashTimer += Time.deltaTime;
            if (able)
            {
                float targetHVelocity = _facing * _dashSpeed;
                _hVelocity = Mathf.SmoothDamp(_hVelocity, targetHVelocity, ref velocityHSmoothing, _accelerationTime / 8);
                Debug.LogFormat("Dash! {0}", _hVelocity);
                _dashStage = DashStage.Dash;
            }
            else
            {
                Debug.LogFormat("overtime");
                _dashTimer = 0;
                _hVelocity = Mathf.SmoothDamp(_hVelocity, 0, ref velocityHSmoothing, _accelerationTime);
                _validDashBegin = false;
                _dashStage = DashStage.End;
            }
            return able;
        }

        public void DashCancel()
        {
            _wantDash = _validDashBegin = false;
            _dashTimer = 0;
            _dashStage = DashStage.End;
        }

        void OnCollisionEnter2D(Collision2D collision)
        {
            Debug.LogFormat("OnCollisionEnter2D: {0}", collision.gameObject.name);
            if (collision.gameObject.CompareTag(GameDefine.ENEMY))
            {
                TakeDamage(0);
            }
        }

        void OnCollisionStay2D(Collision2D collision)
        {
            Debug.LogFormat("OnCollisionStay2D: {0}", collision.gameObject.name);
            if (collision.gameObject.CompareTag(GameDefine.ENEMY))
            {
                TakeDamage(0);
            }
        }

        #region IHealth
        Coroutine _takeDamageTask, _makeDamageTask;

        public void TakeDamage(float damage)
        {
            if (_takeDamageTask == null)
            {
                gameObject.layer = LayerMask.NameToLayer(GameDefine.GHOST);
                _collisionMask = LayerMask.GetMask(GameDefine.DEFAULT);
                _takeDamageTask = StartCoroutine(TakeDamageTask());
            }
        }

        public void MakeDamage(IHealth target)
        {
        }

        IEnumerator TakeDamageTask()
        {
            _inDamage = _inGhost = true;
            _hVelocity = (_facing == 1) ? -3 : 3;
            _vVelocity = 0;
            yield return new WaitForSeconds(DAMAGE_TIME);
            _inDamage = false;
            yield return new WaitForSeconds(GHOST_TIME - DAMAGE_TIME);
            _inGhost = false;
            
            _collisionMask = _collisionMaskBackup;
            gameObject.layer = LayerMask.NameToLayer(GameDefine.PLAYER);
            
            _takeDamageTask = null;
        }
        #endregion
    }
}