namespace ABXConsoleClient;

public interface IABXExchangeServerClient : IDisposable
{
    Result<ABXResponsePacket, TimeoutException> GetPacket(byte seq);
    Result<List<ABXResponsePacket>, TimeoutException> GetAllStreamResponse();
}
