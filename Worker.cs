using System.Buffers.Binary;
using System.Linq;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace ABXConsoleClient;


public class Worker
{
    private readonly ILogger<Worker> _logger;
    private readonly ExchangeServerConnectionConfig _exchangeServerConfig;
    private readonly IABXExchangeServerClient client;

    public Worker(ILogger<Worker> logger, IOptions<ExchangeServerConnectionConfig> config, IABXExchangeServerClient c)
    {
        _logger = logger;
        _exchangeServerConfig = config.Value;
        client = c;
    }
    public void doWorkV2()
    {
        _logger.LogInformation("exchange server host: {host}, port: {port}", _exchangeServerConfig.Host, _exchangeServerConfig.Port);
        List<ABXResponsePacket> intermediateResults = new List<ABXResponsePacket>();
        var allStreamResult = client.GetAllStreamResponse();
        if (allStreamResult.Err != null)
        {
            throw new Exception("Even After retrying, could not connect to the exchange server");
        }
        intermediateResults = allStreamResult.Ok;
        _logger.LogInformation(">>>> stream was fetched <<<<");
        Thread.Sleep(5000);
        intermediateResults.Sort((first, second) =>
                {
                    return (int)first.Sequence - (int)second.Sequence;
                });

        var lastSeq = intermediateResults[intermediateResults.Count - 1].Sequence;
        _logger.LogInformation("lastSeq: {lastSeq}", lastSeq);

        var presentSequences = intermediateResults.ToDictionary(i => i.Sequence);
        var finalResult = new List<ABXResponsePacket>();
        uint i = 1;
        while (i <= lastSeq)
        {
            if (presentSequences.ContainsKey(i))
            {
                finalResult.Add(presentSequences[i]);
                i++;
                continue;
            }
            // _logger.LogInformation("packet i: {i} is missing, isClientConnected: {isClientConnected}", i, client.Connected);
            _logger.LogInformation("packet i: {i} is missing", i);
            Thread.Sleep(5000);
            // _logger.LogInformation(">>> got the stream");
            var packetResult = client.GetPacket((byte)i);
            if (packetResult.Err != null)
            {
                _logger.LogInformation("packet {i} could not be fetched even after retrying");
                i++;
                continue;
            }
            _logger.LogInformation("missing packet: {i} was fetched successfully", i);
            var packet = packetResult.Ok;
            finalResult.Add(packet);
            i++;
        }
        _logger.LogInformation("is finalResult null: {isNull}", finalResult is null);

        foreach (var item in finalResult)
        {
            _logger.LogInformation("seq: {seq}", item.Sequence);
        }

        var packets = System.Text.Json.JsonSerializer.Serialize(finalResult);
        var path = Directory.GetCurrentDirectory() + "/a.json";
        _logger.LogInformation("writing to the path: {path}", path);
        File.WriteAllText(path, packets);

    }



    private (int, ABXResponsePacket) ReadPacketV2(Span<byte> singlePacketBuffer, ABXExchangeServerClient client)
    {

        Span<char> chars = stackalloc char[4];
        var readBytes = client.Read(singlePacketBuffer);
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
        // var symbol = BinaryPrimitives.ReadUInt32BigEndian(symbolBytes);
        var orderType = (char)singlePacketBuffer[4];
        var quantityBytes = singlePacketBuffer.Slice(5, 4);
        var quantity = BinaryPrimitives.ReadUInt32BigEndian(quantityBytes);
        var priceBytes = singlePacketBuffer.Slice(9, 4);
        var price = BinaryPrimitives.ReadUInt32BigEndian(priceBytes);
        var seqBytes = singlePacketBuffer.Slice(13, 4);
        var seq = BinaryPrimitives.ReadUInt32BigEndian(seqBytes);
        // _logger.LogInformation($"symbolString: {symbolString},  orderType: {orderType}, quantity: {quantity}, price: {price}, seq: {seq}");
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


}
