namespace Test.Shared
{
    using System.Net.Http;

    /// <summary>
    /// A <see cref="ByteArrayContent"/> that records whether it has been disposed, so scenarios can prove the crawler
    /// releases HTTP responses.
    /// </summary>
    internal sealed class DisposalTrackingContent : ByteArrayContent
    {
        internal DisposalTrackingContent(byte[] content)
            : base(content)
        {
        }

        internal bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
