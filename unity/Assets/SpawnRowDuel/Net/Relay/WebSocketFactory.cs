namespace SpawnRowDuel.Net
{
    /// <summary>
    /// The one place that knows which platform we are on. Everything above takes an
    /// <see cref="IWebSocketFactory"/> and never finds out.
    /// </summary>
    public sealed class PlatformWebSocketFactory : IWebSocketFactory
    {
        public IWebSocket Create()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new BrowserWebSocket();
#else
            return new SystemWebSocket();
#endif
        }
    }
}
