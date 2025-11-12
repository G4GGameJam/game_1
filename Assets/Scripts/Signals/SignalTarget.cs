/* This script defines the base class for any component that can receive a signal.
 * All SignalReceiver-like scripts inherit from this and must implement OnSignal().
 */

using UnityEngine;

public abstract class SignalTarget : MonoBehaviour //Base class for objects that can respond to signals.
{
    public abstract void OnSignal(SignalEmitter from); //Abstract method each subclass must override to define signal behavior.
}
