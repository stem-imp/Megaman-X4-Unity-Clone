using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MegaManX
{
    public class ZeroController : MonoBehaviour
    {
        public Zero _zero;
        Vector2 _velocity;

        void Start()
        {
            Reset();
        }

        void Reset()
        {
        }

        // Update is called once per frame
        void Update()
        {
            float h = Input.GetAxisRaw("Horizontal");
            _zero.MoveHorizontally(h);
            if (Input.GetButtonDown("Jump"))
                _zero.Jump();
            if (Input.GetKeyDown(KeyCode.Z))
                _zero.StartDash();
            if (Input.GetKey(KeyCode.Z))
                _zero.Dash();
            if (Input.GetKeyUp(KeyCode.Z))
                _zero.DashCancel();
        }
    }
}