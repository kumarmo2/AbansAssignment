using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace ABXConsoleClient;

public class ExchangeClientV2 : IABXExchangeServerClient
{
    private TcpClient _tcp;
    // TODO: see if its possible to replace with buffStream
    private NetworkStream _stream;
    private const int RETRIES = 5;
    private readonly ExchangeServerConnectionConfig _exchangeServerConfig;
    private readonly ILogger<ExchangeClientV2> _logger;
    private const int _recoverableIssueWaitTimeout = 200;
    private const int _nonRecoverableIssueWaitTimeout = 500;

    public ExchangeClientV2(IOptions<ExchangeServerConnectionConfig> exchangeConfig, ILogger<ExchangeClientV2> logger)
    {
        _exchangeServerConfig = exchangeConfig.Value;
        _logger = logger;
    }

    private void SetNewConnection()
    {
        _logger.LogDebug("connecting a new connection");
        var tcp = new TcpClient();
        tcp.Connect(_exchangeServerConfig.Host, _exchangeServerConfig.Port);
        var stream = tcp.GetStream();
        stream.ReadTimeout = _recoverableIssueWaitTimeout;
        stream.WriteTimeout = _recoverableIssueWaitTimeout;
        _tcp = tcp;
        _stream = stream;
    }

    private void SetNewConnectionIfRequired()
    {
        if (_tcp is null || _stream is null)
        {
            SetNewConnection();
        }
    }

    private void ReconnectOnRecoverableConnectionIssue()
    {
        FreeTCP();
        SetNewConnection();
    }

    private Result<bool, TimeoutException> TryReconnectingOnNonRecoverableConnectionIssue()
    {
        var retryNum = 0;
        while (retryNum < RETRIES)
        {
            try
            {
                ReconnectOnRecoverableConnectionIssue();
                return new Result<bool, TimeoutException>(true);
            }
            catch (SocketException e)
            {
                _logger.LogError("Could not connect to the exchange server, retryNum: {retryNum}, exceptionMessage: {exceptionMessage}", retryNum, e.Message);
                if (retryNum == RETRIES - 1)
                {
                    _logger.LogError("even after retrying {retries} times, connection could not be established., exceptionMessage: {exceptionMessage}", RETRIES, e.Message);
                    return new Result<bool, TimeoutException>(new TimeoutException());
                }
            }
            retryNum++;
            Thread.Sleep(retryNum * _nonRecoverableIssueWaitTimeout);
        }
        throw new UnreachableException();
    }

    private void FreeTCP()
    {
        if (_stream is not null)
        {
            _stream.Close();
            _stream.Dispose();
            _stream = null;
        }
        if (_tcp is not null)
        {
            _tcp.Close();
            _tcp.Dispose();
            _tcp = null;
        }
    }

    private static (int, ABXResponsePacket) ReadPacketV2(Span<byte> singlePacketBuffer, NetworkStream stream)
    {
        Span<char> chars = stackalloc char[4];
        var readBytes = stream.Read(singlePacketBuffer);
        if (readBytes <= 0)
        {
            return (0, null);
        }
        if (readBytes != ABXResponsePacket.PACKET_SIZE_BYTES)
        {
            throw new Exception("wrong size of the packet;");
        }
        var symbolBytes = singlePacketBuffer.Slice(0, 4);
        for (var j = 0; j < 4; j++)
        {
            chars[j] = (char)symbolBytes[j];
        }
        var symbolString = new String(chars);
        var orderType = (char)singlePacketBuffer[4];
        var quantityBytes = singlePacketBuffer.Slice(5, 4);
        var quantity = BinaryPrimitives.ReadUInt32BigEndian(quantityBytes);
        var priceBytes = singlePacketBuffer.Slice(9, 4);
        var price = BinaryPrimitives.ReadUInt32BigEndian(priceBytes);
        var seqBytes = singlePacketBuffer.Slice(13, 4);
        var seq = BinaryPrimitives.ReadUInt32BigEndian(seqBytes);
        var packet = new ABXResponsePacket
        {
            Price = price,
            Symbol = symbolString,
            Quantity = quantity,
            Sequence = seq,
            OrderType = orderType,
        };
        return (readBytes, packet);
    }

    public Result<ABXResponsePacket, TimeoutException> GetPacket(byte seq)
    {
        var retryNum = 0;
        Span<byte> singlePacketBuffer = stackalloc byte[ABXResponsePacket.PACKET_SIZE_BYTES];
        while (retryNum < RETRIES)
        {
            try
            {
                SetNewConnectionIfRequired();
                _logger.LogInformation("get packet: {packet}, retryNum: {retryNum}", seq, retryNum);
                ABXRequest request = new ABXRequest { CallType = ABXRequest.RESEND_PACKET, ResendSeq = (byte)seq };
                request.WriteToSTream(_stream);
                var (bytesRead, packet) = ReadPacketV2(singlePacketBuffer, _stream);
                if (bytesRead == 0 && retryNum < RETRIES - 1)
                {
                    _logger.LogInformation("... bytesRead from GetPacket were 0, reconnecting");
                    ReconnectOnRecoverableConnectionIssue();
                }
                else
                {
                    return new Result<ABXResponsePacket, TimeoutException>(packet);
                }
            }
            catch (SocketException)
            {
                _logger.LogError("Looks like could not establish connection to the exchange server, retrying now...");
                var result = TryReconnectingOnNonRecoverableConnectionIssue();
                if (result.Err != null)
                {
                    return new Result<ABXResponsePacket, TimeoutException>(result.Err);
                }
            }
            catch (IOException e)
            {
                _logger.LogError("......caught IOException in client. will retry now,  {exceptionMessage}", e.Message);
                ReconnectOnRecoverableConnectionIssue();
            }
            retryNum++;
            if (retryNum > 0)
            {
                Thread.Sleep(retryNum * _stream.ReadTimeout);
            }
        }
        return new Result<ABXResponsePacket, TimeoutException>(new TimeoutException());
    }


    public Result<List<ABXResponsePacket>, TimeoutException> GetAllStreamResponse()
    {
        Span<byte> singlePacketBuffer = stackalloc byte[ABXResponsePacket.PACKET_SIZE_BYTES];
        var packets = new List<ABXResponsePacket>();
        var retryNum = 0;
        // TODO: take out the retrying logic at a common place?
        try
        {
            while (retryNum < ExchangeClientV2.RETRIES)
            {
                try
                {
                    SetNewConnectionIfRequired();
                    _logger.LogInformation(">>>> GetAllStreamResponse, retryNum: {retryNum}", retryNum);
                    var request = new ABXRequest { CallType = ABXRequest.STREAM_ALL, ResendSeq = 0 };
                    request.WriteToSTream(_stream);
                    while (true)
                    {
                        var (bytesRead, packet) = ReadPacketV2(singlePacketBuffer, _stream);
                        if (bytesRead <= 0)
                        {
                            break;
                        }
                        packets.Add(packet);
                    }
                    return new Result<List<ABXResponsePacket>, TimeoutException>(packets);
                }
                catch (SocketException)
                {
                    _logger.LogError("Looks like could not establish connection to the exchange server, retrying now...");
                    var result = TryReconnectingOnNonRecoverableConnectionIssue();
                    if (result.Err != null)
                    {
                        return new Result<List<ABXResponsePacket>, TimeoutException>(result.Err);
                    }
                }
                catch (IOException e)
                {
                    _logger.LogInformation("......caught IOException in client. will retry now,  {exceptionMessage}", e.Message);
                    ReconnectOnRecoverableConnectionIssue();
                }
                retryNum++;
                if (retryNum > 0)
                {
                    Thread.Sleep(retryNum * _stream.ReadTimeout);
                }
            }
        }
        finally
        {
            // WE are freeing here because after the "StreamAll" response, the exchange server
            // closes the connection itself always.
            FreeTCP();
        }
        return new Result<List<ABXResponsePacket>, TimeoutException>(new TimeoutException());
    }


    public void Dispose()
    {
        // TODO: Implement dispose correctly.
        if (_stream is not null)
        {
            _stream.Dispose();
        }
        if (_tcp is not null)
        {
            _tcp.Dispose();
        }
    }
}
