using System;

namespace Fodinha.Hosting
{
    /// <summary>Uma sala pública como aparece na lista (só informação pública).</summary>
    [Serializable]
    public sealed class RoomListing
    {
        public string Code = "";
        public string HostName = "";
        public int Players;
        public int MaxPlayers;
        public bool InProgress;
        public int StartingLives = 3;

        public bool IsFull => Players >= MaxPlayers;
        public bool CanJoin => !IsFull && !InProgress;
    }
}
