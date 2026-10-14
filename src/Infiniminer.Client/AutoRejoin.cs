using System.Net;

namespace Infiniminer
{
    // After a match the server restarts with a new map, which disconnects everyone. Instead of leaving
    // players in the server list, the client waits a moment and rejoins, retrying while the new world
    // is being generated.
    public sealed class AutoRejoin
    {
        public const string RestartReason = "The server is restarting.";
        public const double FirstAttemptDelay = 3, RetryInterval = 2, GiveUpAfter = 30;

        double startedAt, nextAttemptAt;
        bool attempting;

        public IPEndPoint Target { get; private set; }

        // The team and class to pick again once back in, if the player had chosen them.
        public (PlayerTeam team, PlayerClass playerClass)? Choice { get; private set; }

        public bool Active
        {
            get { return Target != null; }
        }

        public void Begin(IPEndPoint target, double now, (PlayerTeam, PlayerClass)? choice = null)
        {
            Target = target;
            Choice = choice;
            startedAt = now;
            nextAttemptAt = now + FirstAttemptDelay;
            attempting = false;
        }

        // Time to try connecting? Each attempt is reported once.
        public bool ShouldAttempt(double now)
        {
            if (!Active || attempting || now < nextAttemptAt)
                return false;
            attempting = true;
            return true;
        }

        // The server wasn't back yet. Returns false (and stops) once it has been too long.
        public bool AttemptFailed(double now)
        {
            if (!Active)
                return false;
            if (now - startedAt > GiveUpAfter)
            {
                Cancel();
                return false;
            }
            attempting = false;
            nextAttemptAt = now + RetryInterval;
            return true;
        }

        public void Cancel()
        {
            Target = null;
            Choice = null;
            attempting = false;
        }
    }
}
