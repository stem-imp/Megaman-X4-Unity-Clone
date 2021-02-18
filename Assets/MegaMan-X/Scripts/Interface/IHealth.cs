using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IHealth
{
    void TakeDamage(float damage);
    void MakeDamage(IHealth target);
}
