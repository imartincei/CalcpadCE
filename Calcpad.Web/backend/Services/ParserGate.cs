namespace Calcpad.Server.Services
{
    /// <summary>
    /// Serializes access to the endpoints that touch <c>Calcpad.Core</c>'s process-global
    /// <c>MacroParser.Macros</c> dictionary (convert, docx, portable/bundle) — two concurrent
    /// calls into it corrupt each other's macro state. This is the server-side counterpart to
    /// the frontend's own request queue (see <c>CalcpadApiClient.requestQueue</c>): that queue
    /// keeps a well-behaved client from ever sending two of these at once, but this gate is
    /// what makes the guarantee hold regardless of the client — a second CalcpadApiClient
    /// instance, a raw fetch that bypasses it, or a future caller that forgets to serialize.
    /// </summary>
    public sealed class ParserGate
    {
        private readonly SemaphoreSlim _gate = new(1, 1);

        /// <summary>
        /// Waits for exclusive access, honoring <paramref name="cancellationToken"/> so a
        /// request whose client already gave up doesn't sit in line for its turn. Dispose the
        /// result to release.
        /// </summary>
        public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Releaser(_gate);
        }

        private sealed class Releaser(SemaphoreSlim gate) : IDisposable
        {
            public void Dispose() => gate.Release();
        }
    }
}
