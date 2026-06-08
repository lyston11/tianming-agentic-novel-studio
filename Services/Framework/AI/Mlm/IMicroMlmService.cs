using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Services.Framework.AI.Mlm
{
    public interface IMicroMlmService
    {
        Task<float[]> ScoreCandidatesAsync(
            string text,
            int anchorIdx,
            string key,
            IReadOnlyList<string> candidates,
            CancellationToken ct = default);

        void ReleaseSession();

        bool IsModelReady();

        void SetIdleReleaseMinutes(int minutes);

        Func<int>? IdleMinutesProvider { get; set; }
    }
}
