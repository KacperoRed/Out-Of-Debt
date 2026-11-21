using Unity.Netcode.Components;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// NetworkTransform, w którym pozycję wysyła WŁAŚCICIEL obiektu (klient), a nie serwer.
    /// Używany przez gracza i pojazdy, żeby sterowanie było płynne bez opóźnienia.
    /// </summary>
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
